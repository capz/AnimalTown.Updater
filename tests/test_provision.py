"""Host-only provisioning checks; all images are bounded synthetic headers."""
import contextlib
import importlib.util
import io
import os
import struct
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location('updater_provision', Path(__file__).resolve().parents[1] / 'tools' / 'provision.py')
provision = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(provision)


class ProvisionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / 'SD'
        self.root.mkdir()
        self.ca = self.base / 'ca.pem'
        self.ca.write_bytes(b'-----BEGIN CERTIFICATE-----\npublic roots\n')
        self.swiss = self.base / 'swiss.dol'
        dol = bytearray(0x120)
        for offset, value in ((0, 0x100), (0x48, 0x80003100), (0x90, 32), (0xe0, 0x80003100)):
            struct.pack_into('>I', dol, offset, value)
        self.swiss.write_bytes(dol)
        self.folder = self.root / 'animaltown-updater'
        self.record = self.folder / 'image-path.txt'

    def image(self, name='Games/My Town.nkit.iso', version='0.11.24'):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        header = bytearray(0x440)
        header[:6] = b'AMTE01'
        header[0x1c:0x20] = b'\xc2\x33\x9f\x3d'
        title = ('AnimalTown / ' + version).encode('ascii')
        header[0x20:0x20 + len(title)] = title
        path.write_bytes(header)
        return path

    def invoke(self, image=None, extra=(), swiss=True):
        args = [str(self.root), '--roots', str(self.ca), '--utc-offset-minutes', '60']
        if swiss:
            args += ['--swiss', str(self.swiss)]
        if image is not None:
            args += ['--image', str(image)]
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output), mock.patch.object(provision.os, 'urandom', return_value=b'Q' * 48):
            provision.main(args + list(extra))
        return output.getvalue()

    def snapshot(self):
        return {str(path.relative_to(self.root)): ('link', os.readlink(path)) if path.is_symlink() else path.read_bytes()
                for path in self.root.rglob('*') if path.is_symlink() or path.is_file()}

    def reject_unchanged(self, image=None, extra=(), swiss=True):
        before = self.snapshot()
        with self.assertRaises(SystemExit) as failure:
            self.invoke(image, extra, swiss=swiss)
        self.assertEqual(failure.exception.code, 2)
        self.assertEqual(before, self.snapshot())

    def old_inputs(self, seed=None):
        self.folder.mkdir(exist_ok=True)
        (self.folder / 'roots.pem').write_bytes(b'old trusted roots')
        (self.folder / 'utc-offset-minutes.txt').write_bytes(b'-120')
        if seed is not None:
            (self.folder / 'entropy.bin').write_bytes(seed)

    def test_explicit_relative_absolute_and_arbitrary_image_names(self):
        for name in ('Renamed.iso', 'Games/Animal Town/Autumn.nkit.iso', '街/Forêt 🦊.GCM'):
            with self.subTest(name=name):
                path = self.image(name)
                output = self.invoke(path if name == 'Renamed.iso' else Path(name))
                self.assertEqual(self.record.read_bytes(), ('gcloader:/' + name + '\n').encode('utf-8'))
                self.assertEqual((self.folder / 'entropy.bin').read_bytes(), b'Q' * 48)
                self.assertNotIn((b'Q' * 48).decode(), output)
                self.assertNotIn((b'Q' * 48).hex(), output)
                (self.folder / 'entropy.bin').unlink()

    def test_current_review_and_legacy_headers(self):
        for version in ('0.11.24', '0.11.24+abcdef123456', '0.14.9.abcdef123456', '0.14.9.' + 'a' * 40):
            with self.subTest(version=version):
                self.image(version=version)
                self.invoke(Path('Games/My Town.nkit.iso'))
                (self.folder / 'entropy.bin').unlink()

    def test_legacy_tls_only_call_does_not_guess_an_image(self):
        self.image('AnimalTown.iso')
        output = self.invoke()
        self.assertFalse(self.record.exists())
        self.assertIn('discovered automatically', output)
        self.assertEqual((self.folder / 'swiss.dol').read_bytes(), self.swiss.read_bytes())
        self.assertEqual(len((self.folder / 'entropy.bin').read_bytes()), 48)

    def test_existing_valid_record_is_preserved_byte_exact(self):
        self.image()
        self.folder.mkdir()
        original = b'gcloader:/Games/My Town.nkit.iso'
        self.record.write_bytes(original)
        output = self.invoke()
        self.assertEqual(self.record.read_bytes(), original)
        self.assertIn('no image mapping is required', output)

    def test_legacy_records_do_not_block_automatic_discovery(self):
        self.old_inputs()
        for data in (b'gcloader:/Missing.iso\n', b'gcloader:/../Outside.iso\n', b'dvd:/Wrong.iso\n', b'gcloader:/a.iso\ngcloader:/b.iso\n', b'\xff'):
            with self.subTest(data=data):
                self.record.write_bytes(data)
                self.invoke()
                self.assertEqual(self.record.read_bytes(), data)
                (self.folder / 'entropy.bin').unlink()
        self.image()
        self.invoke(Path('Games/My Town.nkit.iso'))
        self.assertEqual(self.record.read_bytes(), b'gcloader:/Games/My Town.nkit.iso\n')

    def test_invalid_selection_preserves_record_roots_clock_and_unused_seed(self):
        self.image()
        outside = self.base / 'Outside.iso'
        outside.write_bytes((self.root / 'Games/My Town.nkit.iso').read_bytes())
        self.old_inputs(b'S' * 48)
        self.record.write_bytes(b'gcloader:/Old.iso\n')
        for selected in (Path('Missing.iso'), Path('../Outside.iso'), outside, Path('Games/../Games/My Town.nkit.iso'), Path('gcloader:/Games/My Town.nkit.iso')):
            with self.subTest(selected=selected):
                self.reject_unchanged(selected)
        self.assertEqual(outside.read_bytes(), (self.root / 'Games/My Town.nkit.iso').read_bytes())

    def test_wrong_game_magic_truncation_and_invalid_release_are_rejected(self):
        self.old_inputs(b'S' * 48)
        self.record.write_bytes(b'gcloader:/Old.iso\n')
        for version in ('0.11', '0.11.24+abc', '0.11.24-dirty', '0.999999999999.0'):
            with self.subTest(version=version):
                self.image(version=version)
                self.reject_unchanged(Path('Games/My Town.nkit.iso'))
        for offset in (0, 0x1c):
            path = self.image()
            header = bytearray(path.read_bytes())
            header[offset] ^= 1
            path.write_bytes(header)
            self.reject_unchanged(path)
        self.image().write_bytes(b'AMTE01')
        self.reject_unchanged(Path('Games/My Town.nkit.iso'))

    def test_fat_aliases_wrong_extension_and_filename_limits(self):
        self.old_inputs()
        for name in ('Town.dol', 'Town.iso.bak', 'Games./Town.iso', 'Town=.iso', 'Town?.iso', 'Town\\.iso', 'a' * 245 + '.iso'):
            with self.subTest(name=name):
                self.image(name)
                self.reject_unchanged(Path(name))

    def test_symlink_escape_never_changes_the_record_or_external_files(self):
        self.image()
        outside = self.base / 'Outside.iso'
        outside.write_bytes((self.root / 'Games/My Town.nkit.iso').read_bytes())
        (self.root / 'Linked.iso').symlink_to(outside)
        self.old_inputs()
        self.record.write_bytes(b'gcloader:/Old.iso\n')
        self.reject_unchanged(Path('Linked.iso'))
        old = outside.read_bytes()
        self.record.unlink()
        self.record.symlink_to(outside)
        self.reject_unchanged(Path('Games/My Town.nkit.iso'))
        self.assertEqual(outside.read_bytes(), old)

    def test_settings_directory_symlink_escape_is_rejected(self):
        path = self.image()
        outside = self.base / 'Other settings'
        outside.mkdir()
        self.folder.symlink_to(outside, target_is_directory=True)
        self.reject_unchanged(path)
        self.assertEqual(list(outside.iterdir()), [])

    def test_image_mapping_only_update_retains_unused_launch_inputs(self):
        path = self.image()
        self.old_inputs(b'P' * 64)
        self.record.write_bytes(b'gcloader:/Missing old.iso\n')
        originals = {name: (self.folder / name).read_bytes() for name in ('roots.pem', 'utc-offset-minutes.txt', 'entropy.bin')}
        with mock.patch.object(provision.os, 'urandom', side_effect=AssertionError('mapping-only must not generate entropy')):
            # Record-only repair does not depend on a new PC CA bundle either.
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                provision.main([str(self.root), '--image', str(path), '--swiss', str(self.swiss), '--roots', str(self.base / 'Missing CA')])
        self.assertEqual(self.record.read_bytes(), b'gcloader:/Games/My Town.nkit.iso\n')
        self.assertIn('retained unused launch inputs unchanged', output.getvalue())
        for name, data in originals.items():
            self.assertEqual((self.folder / name).read_bytes(), data)
        self.reject_unchanged(swiss=False)

    def test_swiss_is_required_for_first_setup_and_reused_later(self):
        self.reject_unchanged(swiss=False)
        self.invoke()
        installed = (self.folder / 'swiss.dol').read_bytes()
        (self.folder / 'entropy.bin').unlink()
        self.invoke(swiss=False)
        self.assertEqual((self.folder / 'swiss.dol').read_bytes(), installed)

    def test_invalid_swiss_preserves_existing_launch_inputs(self):
        self.old_inputs(b'S' * 48)
        for invalid in (b'', bytes(0x100), self.swiss.read_bytes()[:0x110]):
            with self.subTest(size=len(invalid)):
                self.swiss.write_bytes(invalid)
                self.reject_unchanged()

    def test_existing_swiss_cannot_escape_the_sd_root(self):
        self.folder.mkdir()
        (self.folder / 'swiss.dol').symlink_to(self.swiss)
        self.reject_unchanged(swiss=False)

    def test_bad_or_escaping_existing_seed_does_not_authorize_mapping_changes(self):
        path = self.image()
        self.old_inputs(b'S' * 47)
        self.record.write_bytes(b'gcloader:/Old.iso\n')
        self.reject_unchanged(path)
        seed = self.folder / 'entropy.bin'
        seed.unlink()
        outside = self.base / 'private seed'
        outside.write_bytes(b'T' * 48)
        seed.symlink_to(outside)
        self.reject_unchanged(path)
        self.assertEqual(outside.read_bytes(), b'T' * 48)

    def test_atomic_record_failure_preserves_original_and_removes_temporary(self):
        path = self.image()
        self.old_inputs(b'S' * 48)
        self.record.write_bytes(b'gcloader:/Old.iso\n')
        with mock.patch.object(provision.os, 'replace', side_effect=OSError('injected publication failure')):
            self.reject_unchanged(path)

    def test_bad_tls_inputs_never_publish_a_record_or_seed(self):
        path = self.image()
        self.old_inputs()
        self.record.write_bytes(b'gcloader:/Old.iso\n')
        self.reject_unchanged(path, ('--utc-offset-minutes', '9999'))
        self.ca.write_bytes(b'not a certificate bundle')
        self.reject_unchanged(path)


if __name__ == '__main__':
    unittest.main()
