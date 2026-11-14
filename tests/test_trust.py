"""Embedded public trust provenance, generation and bounded native AOT checks."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import ssl
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('generate_trust', ROOT / 'tools/generate-trust.py')
TRUST = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(TRUST)


class EmbeddedTrustTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='updater-trust-test-')
        self.addCleanup(self.temporary.cleanup)
        self.assets = Path(self.temporary.name) / 'https'
        shutil.copytree(ROOT / 'assets/https', self.assets)

    def manifest(self):
        return json.loads((self.assets / 'source.json').read_text())

    def write_manifest(self, manifest):
        (self.assets / 'source.json').write_text(json.dumps(manifest))

    def fingerprint_source(self, content):
        manifest = self.manifest()
        manifest['source'].update(bytes=len(content), sha256=hashlib.sha256(content).hexdigest(),
                                  gitBlob=hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest())
        self.write_manifest(manifest)

    def test_pinned_public_roots_parse_without_system_trust(self):
        bundle, manifest = TRUST.load_bundle(self.assets)
        self.assertEqual(len(bundle), 178673)
        self.assertEqual(manifest['embedded']['certificates'], 118)
        self.assertLessEqual(len(bundle), 262144)
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        self.assertEqual(context.cert_store_stats()['x509'], 0)
        context.load_verify_locations(cadata=bundle.decode('ascii'))
        self.assertEqual(context.cert_store_stats()['x509_ca'], 118)
        self.assertEqual(len(context.get_ca_certs()), 118)

    def test_generated_bytes_are_exact_and_fresh_for_each_call(self):
        bundle, manifest = TRUST.load_bundle(self.assets)
        source = (ROOT / 'src/EmbeddedTrust.cs').read_text()
        self.assertEqual(source, TRUST.render(bundle, manifest))
        literals = re.findall(r'0x([0-9a-f]{2})\b', source)
        self.assertEqual(bytes(int(value, 16) for value in literals), bundle)
        self.assertIn('LoadRoots()=>new byte[]{', source)
        self.assertNotIn('static readonly byte[]', source)

    def test_modified_source_is_rejected(self):
        source = self.assets / 'cacert.pem'
        source.write_bytes(source.read_bytes() + b'# local or proxy trust must not enter a build\n')
        with self.assertRaisesRegex(ValueError, 'source fingerprint'):
            TRUST.load_bundle(self.assets)

    def test_embedded_fingerprint_or_count_cannot_drift(self):
        for field, value in [('sha256', '0' * 64), ('bytes', 1), ('certificates', 1)]:
            original = self.manifest(); changed = self.manifest(); changed['embedded'][field] = value
            self.write_manifest(changed)
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, 'bundle fingerprint'):
                TRUST.load_bundle(self.assets)
            self.write_manifest(original)

    def test_license_notice_is_preserved(self):
        notice = self.assets / 'LICENSE.certifi'
        self.assertIn('Mozilla Public License', notice.read_text())
        notice.write_text('missing license')
        with self.assertRaisesRegex(ValueError, 'license notice'):
            TRUST.load_bundle(self.assets)

    def test_oversized_bundle_is_rejected_before_generation(self):
        source = self.assets / 'cacert.pem'; content = source.read_bytes() * 2
        source.write_bytes(content); self.fingerprint_source(content)
        with self.assertRaisesRegex(ValueError, '256 KiB'):
            TRUST.load_bundle(self.assets)

    def test_duplicate_roots_are_rejected(self):
        source = self.assets / 'cacert.pem'; content = source.read_bytes()
        first = TRUST.CERTIFICATE.search(content).group(0)
        content += b'\n' + first + b'\n'; source.write_bytes(content); self.fingerprint_source(content)
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            TRUST.load_bundle(self.assets)

    def test_check_rejects_stale_output_without_rewriting_it(self):
        output = Path(self.temporary.name) / 'EmbeddedTrust.cs'; output.write_text('stale')
        with self.assertRaises(SystemExit):
            TRUST.main(['--check', '--source-directory', str(self.assets), '--output', str(output)])
        self.assertEqual(output.read_text(), 'stale')


def native_aot(sdk, output=None):
    sdk = Path(sdk).resolve()
    work = Path(output).resolve() if output else Path(tempfile.mkdtemp(prefix='updater-native-trust-'))
    work.mkdir(parents=True, exist_ok=True)
    project = work / 'TrustAot.csproj'
    def xml(path):
        return str(path).replace('&', '&amp;').replace('"', '&quot;').replace('<', '&lt;')
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                      '<TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                      '<Optimize>true</Optimize></PropertyGroup><ItemGroup><Compile Include="Program.cs" />'
                      '<Compile Include="' + xml(ROOT / 'src/EmbeddedTrust.cs') + '" />'
                      '<Compile Include="' + xml(ROOT / 'src/Sha256.cs') + '" />'
                      '<ProjectReference Include="' + xml(sdk / 'src/DolphinDotNet.Core/DolphinDotNet.Core.csproj') + '" />'
                      '</ItemGroup></Project>')
    (work / 'Program.cs').write_text('using AnimalTown.Updater;\ninternal static class Program {\n'
        'private static int Main(){var first=EmbeddedTrust.LoadRoots();'
        'if(first.Length!=EmbeddedTrust.ByteLength)return 1;'
        'var hash=new Sha256();hash.Append(first,0,first.Length);'
        'if(hash.Finish()!=EmbeddedTrust.Sha256)return 2;'
        'first[0]=0;var second=EmbeddedTrust.LoadRoots();'
        'if(second.Length!=178673||second[0]!=45||ReferenceEquals(first,second))return 3;'
        'return 0;} }\n')
    def run(*arguments):
        print('+', ' '.join(map(str, arguments)), flush=True)
        subprocess.run(list(map(str, arguments)), cwd=ROOT, check=True)
    run('dotnet', 'build', project, '-c', 'Release', '-v', 'quiet')
    run('dotnet', 'build', sdk / 'tools/DolphinDotNet.Compiler', '-c', 'Release', '-v', 'quiet')
    generated = work / 'trust.c'
    run('dotnet', sdk / 'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll',
        '--aot', work / 'bin/Release/net8.0/TrustAot.dll', generated)
    (work / 'host.c').write_text('#include "dnd_managed.h"\n#include <stdio.h>\n'
        'extern intptr_t dnd_value_aot_entry(DndManagedHeap *);\n'
        'int main(int argc,char **argv){(void)argv;static unsigned char memory[1048576];DndManagedHeap heap;'
        'dnd_managed_heap_init(&heap,memory,sizeof memory);dnd_gc_set_stress(argc>1);'
        'intptr_t result=dnd_value_aot_entry(&heap);'
        'printf("TRUST_AOT bytes=178673 heap=%zu used=%zu collections=%zu result=%ld exception=%d\\n",'
        'heap.capacity,heap.used,heap.collections,(long)result,dnd_exception_pending());'
        'if(result||dnd_exception_pending()){fprintf(stderr,"%s\\n",dnd_exception_message());return 1;}'
        'return 0;}\n')
    runtime = [sdk / 'source' / (name + '.c') for name in
               ('managed_runtime', 'async_runtime', 'gc_stw', 'stw', 'text', 'numeric', 'timing', 'bcl')]
    strip = '-Wl,-dead_strip' if sys.platform == 'darwin' else '-Wl,--gc-sections'
    binary = work / 'trust'
    run('cc', '-std=c11', '-O3', '-flto', '-fno-strict-aliasing', '-D_DEFAULT_SOURCE', '-D_DARWIN_C_SOURCE',
        '-Wall', '-Wextra', '-ffunction-sections', '-fdata-sections', '-pthread', '-I' + str(sdk / 'include'),
        generated, work / 'host.c', *runtime, strip, '-lm', '-o', binary)
    env = dict(os.environ, DND_ASYNC_MANUAL='1')
    results = []
    for arguments in ([], ['--gc-stress']):
        result = subprocess.run([str(binary), *arguments], env=env, check=True, text=True, capture_output=True)
        print(result.stdout, end='', flush=True)
        results.append({'arguments': arguments, 'stdout': result.stdout, 'stderr': result.stderr, 'passed': True})
    (work / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print('Native embedded trust evidence:', work)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-aot', action='store_true')
    parser.add_argument('--sdk-source', type=Path)
    parser.add_argument('--output', type=Path)
    options = parser.parse_args()
    result = unittest.TextTestRunner().run(unittest.defaultTestLoader.loadTestsFromTestCase(EmbeddedTrustTests))
    if not result.wasSuccessful():
        raise SystemExit(1)
    if options.native_aot:
        if options.sdk_source is None:
            parser.error('--native-aot requires --sdk-source')
        native_aot(options.sdk_source, options.output)


if __name__ == '__main__':
    main()
