#!/usr/bin/env python3
"""Legacy provisioner for older updater releases using SD-based HTTPS inputs.

The current updater embeds public CA roots, collects fresh platform entropy and
validates its local clock without these files. No PC provisioning is needed.
Install its supported normal Swiss DOL directly at the SD root instead.
"""
import argparse
import datetime
import os
from pathlib import Path
import re
import ssl
import tempfile
import struct
import hashlib

IMAGE_PREFIX = 'gcloader:/'
IMAGE_RECORD = 'image-path.txt'

# Normal argv-capable DOLs, checked against the official release checksums.
# Provenance: build/swiss-builds.json; keep src/SwissBuilds.cs in sync.
SWISS_BUILDS = (
    (2119, 1350216, '3a19ea4b814ed7d43d7dbc81c49f981c215698cfaae6ed7bca471517a74aa1d1'),
    (2092, 790920, 'b844ece3877621f6256a1eb8d196af9d0a3b58c3e17bde7e94d239b5bb668fff'),
    (2073, 787720, '5553ad6641716ea7d1ceb155490f18485096e3ffe25617f1901c6c0500e2fa75'),
    (2041, 778728, '3a7b8e2ce6148ea33254298eead5c79488d0affd35a1c6dad1766af20a0527d2'),
    (2018, 777736, 'a90723468cd6239252b45e608e02075b10c4aa8c891fa21fa1de6257dd603fcf'),
    (1984, 767176, 'bcffa33a98dc5f9910592b1458a1a5b765a2d4c5689bd679740d6a20594ab64b'),
    (1983, 767272, 'e4d8c28d4640890ce3229042e33cca52260e8a5e884131a0b446a80dcd18de31'),
    (1957, 739496, '89dd5fab2a85250af8dd61ba53f7d92e79d3931c625edd7c775d6f7cbb2d865a'),
    (1913, 734824, 'e878a1c6dada59f912332abc469ab6c833d0eb6ca39cf3fcf0dcb9dee5e919f2'),
    (1899, 735048, '5942a67ef31c9cbeea1a9fb43e23757ac73bd9b22853683696763992302a12a2'),
    (1889, 734728, '8ce96ff4e4682dbfef8067c021a82c078cc12936674d6969e6d9a939a94382ae'),
    (1867, 729160, 'eec706e0c7b4cff943e5d7d3a2dba72b1855d8b8f58c016b6fea4ba773d2b611'),
)
VERSION = re.compile(r'([0-9]+)\.([0-9]+)\.([0-9]+)(?:\+[0-9a-fA-F]{12}|\.(?:[0-9a-fA-F]{12}|[0-9a-fA-F]{40}))?')


def _inside(root, path):
    try:
        return path.relative_to(root)
    except ValueError:
        raise ValueError('the selected path must remain inside the SD root') from None


def _canonical_path(relative):
    components = relative.parts
    if not components or any(part in ('.', '..') for part in components):
        raise ValueError('select a canonical image path without traversal')
    for part in components:
        if (part.endswith((' ', '.')) or
                any(ord(char) < 32 or ord(char) == 127 or char in '\\:=*?"<>|' for char in part) or
                len(part.encode('utf-16-le')) // 2 > 255):
            raise ValueError('the image path contains an unsupported filename component')
    if len(components[-1].encode('utf-16-le')) // 2 > 248:
        raise ValueError('the image filename leaves no room for recovery files')
    if relative.suffix.lower() not in ('.iso', '.gcm'):
        raise ValueError('select an ISO, NKit ISO or GCM image')
    canonical = IMAGE_PREFIX + '/'.join(components)
    if len(canonical.encode('utf-8')) > 900:
        raise ValueError('the selected image path exceeds 900 UTF-8 bytes')
    return canonical


def _verify_image(path):
    with path.open('rb') as stream:
        header = stream.read(0x440)
    if len(header) != 0x440:
        raise ValueError('the selected image has a truncated GameCube header')
    if header[:6] != b'AMTE01' or header[0x1c:0x20] != b'\xc2\x33\x9f\x3d':
        raise ValueError('the selected image is not AnimalTown')
    title = header[0x20:0x420].split(b'\0', 1)[0].decode('ascii')
    prefix = 'AnimalTown / '
    if not title.startswith(prefix):
        raise ValueError('the selected image has no AnimalTown release identity')
    version = VERSION.fullmatch(title[len(prefix):])
    if version is None or any(int(counter) > 2147483647 for counter in version.groups()):
        raise ValueError('the selected image has an invalid AnimalTown release identity')


def select_image(root, selected):
    """Resolve a PC path to a verified, bounded SDK volume path."""
    if '..' in selected.parts:
        raise ValueError('--image must not contain parent-directory traversal')
    if not selected.is_absolute() and ':/' in str(selected):
        raise ValueError('--image expects a PC path, not a device URI')
    path = (selected if selected.is_absolute() else root / selected).resolve(strict=True)
    relative = _inside(root, path)
    if not path.is_file():
        raise ValueError('--image must select a regular installed image file')
    canonical = _canonical_path(relative)
    _verify_image(path)
    return canonical


def existing_image(root, record):
    """Never silently repair or guess a stale installation record."""
    _inside(root, record.resolve(strict=True))
    with record.open('rb') as stream:
        data = stream.read(902)
    if len(data) > 901:
        raise ValueError('the existing image-path.txt record is too long; select --image to repair it')
    text = data.decode('utf-8')
    if text.endswith('\n'):
        text = text[:-1]
    if '\n' in text or '\r' in text or not text.startswith(IMAGE_PREFIX):
        raise ValueError('the existing image-path.txt record is invalid; select --image to repair it')
    selected = Path(text[len(IMAGE_PREFIX):])
    canonical = select_image(root, selected)
    if canonical != text:
        raise ValueError('the existing image-path.txt record is not canonical; select --image to repair it')
    return canonical


def _atomic_write(path, data):
    descriptor, temporary = tempfile.mkstemp(prefix='.' + path.name + '-', dir=path.parent)
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        # Directory fsync is available on POSIX; Windows replace still publishes
        # the complete closed file without exposing a partially written record.
        if os.name != 'nt':
            directory = os.open(path.parent, os.O_RDONLY)
            try:
                os.fsync(directory)
            finally:
                os.close(directory)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def _swiss_bytes(path):
    if not path.is_file() or not 0x100 <= path.stat().st_size <= 8 * 1024 * 1024:
        raise ValueError('provide a Swiss DOL of 256 bytes to 8 MiB')
    data = path.read_bytes()
    entry = struct.unpack_from('>I', data, 0xe0)[0]
    executable = False
    sections = []
    for section in range(18):
        offset = struct.unpack_from('>I', data, section * 4)[0]
        address = struct.unpack_from('>I', data, 0x48 + section * 4)[0]
        size = struct.unpack_from('>I', data, 0x90 + section * 4)[0]
        if not size:
            continue
        if offset < 0x100 or offset > len(data) or size > len(data) - offset or address < 0x80003100 or address + size > 0x81800000 or address % 4:
            raise ValueError('Swiss DOL has an invalid section')
        if any(offset < old_offset + old_size and old_offset < offset + size or
               address < old_address + old_size and old_address < address + size
               for old_offset, old_address, old_size in sections):
            raise ValueError('Swiss DOL has overlapping sections')
        sections.append((offset, address, size))
        if section < 7 and address <= entry and entry + 32 <= address + size and not entry % 4:
            at = offset + entry - address
            if data[at + 4:at + 8] != b'_arg':
                raise ValueError('Swiss DOL does not support launch arguments')
            executable = True
    if not executable:
        raise ValueError('Swiss DOL has no executable entry point')
    bss, size = struct.unpack_from('>II', data, 0xd8)
    if size and (bss < 0x80003100 or bss + size > 0x81800000):
        raise ValueError('Swiss DOL has an invalid BSS range')
    digest = hashlib.sha256(data).hexdigest()
    if not any(length == len(data) and known == digest for _, length, known in SWISS_BUILDS):
        raise ValueError('provide a supported official normal Swiss DOL; renamed files are accepted, custom and legacy-LZ builds are not recognized')
    return data


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sd_root',type=Path)
    parser.add_argument('--image', type=Path, help='optional legacy installation record for older AnimalTown builds; new builds discover stable ISOs automatically')
    parser.add_argument('--swiss', type=Path, help='optional official Swiss DOL to copy to the SD root as swiss.dol; the updater also discovers existing root DOLs by their contents')
    parser.add_argument('--roots',type=Path,default=Path(ssl.get_default_verify_paths().cafile or 'roots.pem'),help='legacy updater PEM trust bundle; ignored by current updater releases')
    parser.add_argument('--utc-offset-minutes',type=int,default=int(datetime.datetime.now().astimezone().utcoffset().total_seconds()//60),help='legacy updater offset of the correctly set GameCube local clock (default: this PC); current releases need no offset file')
    args=parser.parse_args(argv)
    try:
        root = args.sd_root.resolve(strict=True)
        if not root.is_dir():
            raise ValueError('SD root must be an existing directory')
        folder = root / 'animaltown-updater'
        _inside(root, folder.resolve())
        record = folder / IMAGE_RECORD
        if args.image is not None and (record.exists() or record.is_symlink()):
            _inside(root, record.resolve(strict=True))
        # Validate all paths and image content before changing any existing
        # record, roots, clock configuration or private one-use seed.
        image = select_image(root, args.image) if args.image is not None else None
        swiss = root / 'swiss.dol'
        if args.swiss is not None and (swiss.exists() or swiss.is_symlink()):
            _inside(root, swiss.resolve(strict=True))
        # The native SDK performs full DOL staging/overlap validation before
        # deleting an ISO. Reject broken host copies before writing launch inputs.
        helper = _swiss_bytes(args.swiss.resolve(strict=True)) if args.swiss is not None else None
        seed = folder / 'entropy.bin'
        if seed.exists() or seed.is_symlink():
            if args.image is None and args.swiss is None:
                raise ValueError('an unconsumed seed already exists; it can be used for the next updater launch')
            _inside(root, seed.resolve(strict=True))
            if not seed.is_file() or not 48 <= seed.stat().st_size <= 64:
                raise ValueError('the existing launch seed must be a regular file of 48..64 bytes')
            if image is not None:
                _atomic_write(record, (image + '\n').encode('utf-8'))
            if args.swiss is not None:
                _atomic_write(swiss, helper)
            print('Updated launch helper or legacy image record; retained unused launch inputs unchanged.')
            return
        roots = args.roots.read_bytes()
        if not 0 < len(roots) <= 262144 or b'-----BEGIN CERTIFICATE-----' not in roots:
            raise ValueError('provide a PEM CA bundle of at most 256 KiB')
        if not -840 <= args.utc_offset_minutes <= 840:
            raise ValueError('UTC offset must be within -840..840 minutes')
        folder.mkdir(parents=True, exist_ok=True)
        if args.swiss is not None:
            _atomic_write(swiss, helper)
        if args.image is not None:
            _atomic_write(record, (image + '\n').encode('utf-8'))
        _atomic_write(folder / 'roots.pem', roots)
        _atomic_write(folder / 'utc-offset-minutes.txt', str(args.utc_offset_minutes).encode('ascii'))
        # Exclusive creation prevents replacement of an unused seed. Its bytes
        # are never logged, included in public outputs or reused by this tool.
        descriptor = os.open(seed, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(os.urandom(48))
            stream.flush()
            os.fsync(stream.fileno())
    except (OSError, ValueError, UnicodeError) as error:
        parser.error(str(error))
    print('Provisioned HTTPS inputs for one legacy updater launch. Current updater releases ignore these files. Keep this SD private; safely eject it.')
    if image is None:
        print('Stable AnimalTown images will be discovered automatically; no image mapping is required.')
    else:
        print('Installed image record ' + ('updated.' if args.image is not None else 'preserved.'))


if __name__=='__main__':main()
