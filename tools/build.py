#!/usr/bin/env python3
"""Build against the exact SDK PR revision; no NuGet publication required."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
IMAGE='ghcr.io/extremscorner/libogc2@sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85'
def run(*args, cwd=ROOT):
    subprocess.run(list(map(str,args)), cwd=cwd, check=True)

def build_native(backend):
    # The pinned SDK's BuildDol target bundles and invokes make as one step,
    # without an extension hook. Bundle into project output, patch only that
    # generated copy, then run the same pinned Make toolchain and heap budget.
    package=Path(subprocess.check_output(['dotnet','msbuild','AnimalTown.Updater.csproj','-nologo','-getProperty:_DndPackageRoot'],cwd=ROOT,text=True).strip())
    compiler=package/'tools/compiler/DolphinDotNet.Compiler.dll'
    if not compiler.is_file():raise SystemExit('Pinned updater SDK compiler was not resolved')
    native=ROOT/'out/native'
    binary=ROOT/'bin/Release/net8.0'
    for extension in ('dol','elf'):
        (binary/f'animal-town-updater.{extension}').unlink(missing_ok=True)
    if native.exists():shutil.rmtree(native)
    run('dotnet',compiler,'--bundle',binary/'animal-town-updater.dll',native)
    run('python3',ROOT/'tools/patch-launch-arguments.py',native)
    # CI/Linux hosts check the exact pinned SDK producer and the bounded reader.
    # Docker-only desktop hosts still compile the bridge in the native build.
    if sys.platform.startswith('linux') and shutil.which('cc'):
        for fixture in ('launch-arguments','launch-handoff'):
            check=ROOT/f'out/native-{fixture}-check'
            extra=[native/'source/dol.c'] if fixture=='launch-handoff' else []
            run('cc','-std=c11','-Wall','-Wextra','-Werror','-ffunction-sections','-fdata-sections','-I'+str(native/'include'),ROOT/f'tests/native-{fixture}.c',*extra,'-Wl,--gc-sections','-o',check)
            run(check)
    arguments=['make','-j2','DND_MANAGED_HEAP_BYTES=1048576']
    if backend=='Auto':
        backend='Native' if os.name!='nt' and os.environ.get('DEVKITPRO') and os.environ.get('DEVKITPPC') and shutil.which('elf2dol') else 'Docker'
    if backend=='Native':run(*arguments,cwd=native)
    else:
        docker=['docker','run','--rm','--init','--platform','linux/amd64']
        if os.name!='nt':docker+=['--user',f'{os.getuid()}:{os.getgid()}']
        for variable in ('HTTP_PROXY','HTTPS_PROXY','NO_PROXY','http_proxy','https_proxy','no_proxy'):
            if os.environ.get(variable):docker+=['--env',variable]
        ca=next((Path(os.environ[v]).resolve() for v in ('GIT_SSL_CAINFO','SSL_CERT_FILE','CURL_CA_BUNDLE') if os.environ.get(v) and Path(os.environ[v]).is_file()),None)
        if ca:docker+=['--volume',f'{ca}:/tmp/dnd-ca-bundle.pem:ro','--env','GIT_SSL_CAINFO=/tmp/dnd-ca-bundle.pem','--env','SSL_CERT_FILE=/tmp/dnd-ca-bundle.pem']
        docker+=['--volume',f'{native}:/work','--workdir','/work',IMAGE,*arguments]
        run(*docker)
    for extension in ('dol','elf'):
        source=native/f'Game.{extension}'
        if not source.is_file() or source.stat().st_size<256:raise SystemExit('Native updater build did not produce '+source.name)
        shutil.copy2(source,binary/f'animal-town-updater.{extension}')
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-source',type=Path,help='existing checkout of the locked SDK revision')
    parser.add_argument('--backend',choices=['Auto','Native','Docker'],default='Auto')
    options=parser.parse_args()
    lock=json.loads((ROOT/'sdk.lock.json').read_text())
    sdk=(options.sdk_source or ROOT/'out/sdk-source').resolve()
    if not (sdk/'.git').exists():
        sdk.mkdir(parents=True,exist_ok=True)
        run('git','init',sdk)
        run('git','remote','add','origin',lock['repository'],cwd=sdk)
        run('git','fetch','--depth','1','origin',lock['commit'],cwd=sdk)
        run('git','checkout','--detach','FETCH_HEAD',cwd=sdk)
    actual=subprocess.check_output(['git','rev-parse','HEAD'],cwd=sdk,text=True).strip()
    if actual!=lock['commit'] or subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=sdk,text=True).strip():
        raise SystemExit('SDK must be a clean checkout of sdk.lock.json commit')
    feed=ROOT/'out/sdk-feed'
    marker=feed/'revision.txt'
    if not marker.exists() or marker.read_text()!=actual:
        run('python3',sdk/'eng/pack.py','--version',lock['packageVersion'],'--output',feed,cwd=sdk)
        marker.write_text(actual)
    run('python3','-m','unittest','discover','-s','tests','-p','test_provision.py')
    run('dotnet','run','--project','tests/Updater.Tests.csproj','-c','Release')
    run('dotnet','build','AnimalTown.Updater.csproj','-c','Release','-p:DndBuildNative=false')
    build_native(options.backend)
    output=ROOT/'out/release';output.mkdir(parents=True,exist_ok=True)
    for extension in ('dol','elf'):
        shutil.copy2(ROOT/f'bin/Release/net8.0/animal-town-updater.{extension}',output)
    (output/'build.json').write_text(json.dumps({'sdk':actual,'source':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'dirty':bool(subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip())},indent=2)+'\n')
    files=sorted(output.glob('animal-town-updater.*'))+[output/'build.json']
    (output/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in files))
    print(output/'animal-town-updater.dol')
if __name__=='__main__':main()
