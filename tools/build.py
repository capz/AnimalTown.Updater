#!/usr/bin/env python3
"""Build against the exact SDK PR revision; no NuGet publication required."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
def run(*args, cwd=ROOT):
    subprocess.run(list(map(str,args)), cwd=cwd, check=True)
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
    run('dotnet','run','--project','tests/Updater.Tests.csproj','-c','Release')
    run('dotnet','build','AnimalTown.Updater.csproj','-c','Release','-p:DndBuildNative=true','-p:DndBuildBackend='+options.backend)
    output=ROOT/'out/release';output.mkdir(parents=True,exist_ok=True)
    for extension in ('dol','elf'):
        shutil.copy2(ROOT/f'bin/Release/net8.0/animal-town-updater.{extension}',output)
    (output/'build.json').write_text(json.dumps({'sdk':actual,'source':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'dirty':bool(subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip())},indent=2)+'\n')
    files=sorted(output.glob('animal-town-updater.*'))+[output/'build.json']
    (output/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in files))
    print(output/'animal-town-updater.dol')
if __name__=='__main__':main()
