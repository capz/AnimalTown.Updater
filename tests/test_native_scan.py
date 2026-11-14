#!/usr/bin/env python3
"""Run the real updater scan through SDK AOT with the console's 1 MiB heap."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess
import struct
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]

def run(*args,cwd=ROOT,**kwargs):
    print('+',' '.join(map(str,args)),flush=True)
    return subprocess.run(list(map(str,args)),cwd=cwd,check=True,**kwargs)

def image(path,version):
    header=bytearray(0x440);header[:6]=b'AMTE01';header[0x1c:0x20]=bytes.fromhex('c2339f3d')
    title=('AnimalTown / '+version).encode('ascii');header[0x20:0x20+len(title)]=title
    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(header)

def fixture(root,scenario,width,highest_created_first=False):
    volume=root/'gcloader';volume.mkdir()
    if highest_created_first:image(volume/'AnimalTown-12.34.56.nkit.iso','12.34.56')
    if scenario=='wide':
        for i in range(width):(volume/('unrelated-'+str(i).zfill(6)+'-'+'x'*80+'.iso')).touch()
    elif scenario=='candidates':
        for i in range(width):
            image(volume/f'AnimalTown-0.{i}.0.nkit.iso',f'0.{i}.0')
            (volume/f'AnimalTown-0.{i}.0+012345abcdef.nkit.iso').touch()
    elif scenario=='deep':
        directory=volume
        for i in range(30):
            directory/=f'directory-{i}';directory.mkdir()
            for n in range(width):(directory/f'unrelated-{n:04}.txt').touch()
            image(directory/f'AnimalTown-0.{i}.0.nkit.iso',f'0.{i}.0')
    if not highest_created_first:image(volume/'AnimalTown-12.34.56.nkit.iso','12.34.56')
    return volume

def snapshot(volume):
    return {str(p.relative_to(volume)):None if p.is_dir() else hashlib.sha256(p.read_bytes()).hexdigest() for p in volume.rglob('*')}

def swiss_fixture(volume):
    data=bytearray(2*1024*1024+256)
    for offset,value in ((0,0x100),(0x48,0x80004000),(0x90,0x80),(0xe0,0x80004000),(0x104,0x5f617267)):
        struct.pack_into('>I',data,offset,value)
    data[0x120]=0x53
    path=volume/'swiss-streaming-fixture/renamed.DOL';path.parent.mkdir();path.write_bytes(data)
    assert hashlib.sha256(data).hexdigest()=='442ed67973d6f2e9a689f7aa823b100915d6da9777b0a2476d55f4e23d6b74d2'

def instrument_runtime(sdk,output):
    source=(sdk/'source/managed_runtime.c').read_text()
    shape='uint32_t size;\n    uint8_t marked;\n    uint8_t free;\n    uint16_t reserved;\n    struct DndHeapBlock *next;'
    if shape not in source:raise RuntimeError('Pinned runtime heap-block diagnostic shape changed')
    point='    rebuild_free_list(heap);\n    return object;'
    if source.count(point)!=1:raise RuntimeError('Pinned allocation observation point changed')
    source=source.replace(point,'    rebuild_free_list(heap);\n    dnd_scan_heap_allocated(heap,block_header_size()+block->size);\n    return object;')
    point='    rebuild_free_list(heap);\n}\n\nvoid dnd_eh_push'
    if source.count(point)!=1:raise RuntimeError('Pinned GC observation point changed')
    source=source.replace(point,'    rebuild_free_list(heap);\n    dnd_scan_heap_collected(heap);\n}\n\nvoid dnd_eh_push')
    source='#include "dnd_managed.h"\nvoid dnd_scan_heap_allocated(DndManagedHeap *,size_t);\nvoid dnd_scan_heap_collected(DndManagedHeap *);\n'+source
    output.write_text(source)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-source',type=Path,default=ROOT.parent/'DolphinDotNet')
    parser.add_argument('--output',type=Path)
    parser.add_argument('--width',type=int,default=5000)
    parser.add_argument('--scenario',choices=['small','wide','candidates','deep','all'],default='all')
    parser.add_argument('--ui',action='store_true',help='Exercise the real updater console managed drawing code between scan yields; native GX/font are fixed stubs')
    parser.add_argument('--automatic',action='store_true',help='Use SDK automatic executors and AOT safepoints, with no explicit scheduler pump')
    parser.add_argument('--updater',action='store_true',help='Run production Updater.Run until the expected missing-Swiss boundary after scanning, retaining full network/launch reachability')
    parser.add_argument('--highest-created-first',action='store_true',help='Reverse highest-image creation order; emitted IO counters identify its actual enumeration position')
    parser.add_argument('--optimized',action='store_true',help='Match console O3/LTO and aliasing flags')
    args=parser.parse_args();sdk=args.sdk_source.resolve()
    project=ROOT/'tests/ScanAotSmoke/ScanAotSmoke.csproj'
    run('dotnet','build',project,'-c','Release','-v','quiet','-p:ScanSdkSource='+str(sdk),'-p:ScanWithUi='+str(args.ui).lower(),'-p:ScanAutomatic='+str(args.automatic).lower(),'-p:ScanUpdater='+str(args.updater).lower())
    run('dotnet','build',sdk/'tools/DolphinDotNet.Compiler','-c','Release','-v','quiet')
    work=args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix='updater-native-scan-'))
    work.mkdir(parents=True,exist_ok=True)
    generated=work/'scan.c';instrumented=work/'managed_runtime.c'
    run('dotnet',sdk/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll',
        '--aot',project.parent/'bin/Release/net8.0/ScanAotSmoke.dll',generated)
    generated.write_text('#define dnd_fs_dir_next dnd_scan_dir_next\n#define dnd_fs_open dnd_scan_open\n#define dnd_fs_read dnd_scan_read\n#define dnd_fs_close dnd_scan_close\n'+generated.read_text())
    instrument_runtime(sdk,instrumented)
    runtime=['storage','contiguous_write','file_optimize','file_extents','verified_copy','timing',
             'async_runtime','stw','gc_stw','text','numeric','bcl']
    sources=[sdk/'source'/f'{name}.c' for name in runtime]
    if args.updater:sources += [sdk/'source'/f'{name}.c' for name in ['network','network_posix','tls','platform_entropy','dol','dol_gamecube']]
    binary=work/'scan'
    flags=['cc','-std=c11','-O3' if args.optimized else '-O1','-D_DEFAULT_SOURCE','-D_DARWIN_C_SOURCE','-Wall','-Wextra',
           '-ffunction-sections','-fdata-sections','-I'+str(sdk/'include'),'-pthread']
    if args.optimized:flags += ['-flto','-fno-strict-aliasing']
    fault_enabled=args.updater and sys.platform.startswith('linux')
    if fault_enabled:flags += ['-DDND_SCAN_NATIVE_FAULT=1','-Wl,--wrap=readdir']
    dead_strip='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
    if args.ui:sources.append(ROOT/'tests/native-scan-ui.c')
    run(*flags,generated,ROOT/'tests/native-scan-host.c',instrumented,*sources,dead_strip,'-lm','-o',binary)
    scenarios=['small','wide','candidates','deep'] if args.scenario=='all' else [args.scenario]
    if fault_enabled:scenarios += ['native-memory-fault']
    results=[]
    for scenario in scenarios:
        with tempfile.TemporaryDirectory(prefix='volume-'+scenario+'-',dir=work) as fixture_root:
            volume_root=Path(fixture_root)
            volume=fixture(volume_root,scenario,args.width if scenario!='deep' else min(args.width,100),args.highest_created_first)
            if args.updater:swiss_fixture(volume)
            before=snapshot(volume)
            env=dict(os.environ,DND_STORAGE_ROOT=str(volume_root),DND_ASYNC_MANUAL='1',DND_SCAN_HEAP_BYTES='1048576')
            # Production Swiss discovery makes a second top-directory pass;
            # the injected-catalog streaming fixture makes one additional pass.
            env['DND_SCAN_EXPECT_ENTRIES']=str(len(before)+(len(list(volume.iterdir()))+1 if args.updater else 0))
            env['DND_SCAN_EXPECT_IMAGE_ENTRIES']=str(len(before))
            if args.updater:env['DND_SCAN_EXPECT_SWISS_BYTES']=str(2097408+256+32)
            if args.automatic:env.pop('DND_ASYNC_MANUAL',None)
            if scenario=='native-memory-fault':env['DND_SCAN_NATIVE_MEMORY_FAULT']='1'
            else:env.pop('DND_SCAN_NATIVE_MEMORY_FAULT',None)
            result=subprocess.run([str(binary)],env=env,text=True,capture_output=True,timeout=120)
            unchanged=before==snapshot(volume)
            print(scenario+':\n'+result.stdout+result.stderr,flush=True)
            results.append({'scenario':scenario,'returncode':result.returncode,'unchanged':unchanged,'stdout':result.stdout,'stderr':result.stderr})
    (work/'results.json').write_text(json.dumps(results,indent=2)+'\n')
    print('Native scan evidence:',work)
    if any(result['returncode'] or not result['unchanged'] for result in results):raise SystemExit(1)

if __name__=='__main__':main()
