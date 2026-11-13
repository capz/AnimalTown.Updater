#!/usr/bin/env python3
"""Provision one-use HTTPS inputs and the Swiss automatic-launch helper."""
import argparse
import datetime
import os
from pathlib import Path
import re
import ssl
import tempfile
import struct

IMAGE_PREFIX = 'gcloader:/'
IMAGE_RECORD = 'image-path.txt'
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
    for section in range(18):
        offset = struct.unpack_from('>I', data, section * 4)[0]
        address = struct.unpack_from('>I', data, 0x48 + section * 4)[0]
        size = struct.unpack_from('>I', data, 0x90 + section * 4)[0]
        if not size:
            continue
        if offset < 0x100 or offset > len(data) or size > len(data) - offset or address < 0x80000000 or address + size > 0x81800000:
            raise ValueError('Swiss DOL has an invalid section')
        if section < 7 and address <= entry < address + size:
            executable = True
    if not executable:
        raise ValueError('Swiss DOL has no executable entry point')
    return data


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sd_root',type=Path)
    parser.add_argument('--image', type=Path, help='optional legacy installation record for older AnimalTown builds; new builds discover stable ISOs automatically')
    parser.add_argument('--swiss', type=Path, help='official Swiss DOL to install as animaltown-updater/swiss.dol; required if the helper is not already installed')
    parser.add_argument('--roots',type=Path,default=Path(ssl.get_default_verify_paths().cafile or 'roots.pem'))
    parser.add_argument('--utc-offset-minutes',type=int,default=int(datetime.datetime.now().astimezone().utcoffset().total_seconds()//60),help='UTC offset of the correctly set GameCube local clock (default: this PC)')
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
        swiss = folder / 'swiss.dol'
        if swiss.exists() or swiss.is_symlink():
            _inside(root, swiss.resolve(strict=True))
        # The native SDK performs full DOL staging/overlap validation before
        # deleting an ISO. Reject broken host copies before writing launch inputs.
        helper = _swiss_bytes(args.swiss.resolve(strict=True) if args.swiss is not None else swiss)
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
    print('Provisioned one updater launch. Keep this SD private; safely eject it.')
    if image is None:
        print('Stable AnimalTown images will be discovered automatically; no image mapping is required.')
    else:
        print('Installed image record ' + ('updated.' if args.image is not None else 'preserved.'))


if __name__=='__main__':main()
