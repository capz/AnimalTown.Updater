#!/usr/bin/env python3
"""Real SDK AOT scheduling acceptance, not network/hardware throughput."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-source',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args();sdk=args.sdk_source.resolve();work=args.output.resolve();work.mkdir(parents=True,exist_ok=True)
    def run(*command,**kwargs):return subprocess.run(list(map(str,command)),cwd=ROOT,check=True,**kwargs)
    project=ROOT/'tests/WorkerAotSmoke/WorkerAotSmoke.csproj'
    run('dotnet','build',project,'-c','Release','-p:WorkerSdkSource='+str(sdk))
    run('dotnet','build',sdk/'tools/DolphinDotNet.Compiler','-c','Release','-v','quiet')
    run('dotnet',sdk/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--aot',project.parent/'bin/Release/net8.0/WorkerAotSmoke.dll',work/'worker.c')
    sources=['managed_runtime','async_runtime','stw','gc_stw','text','numeric','bcl','timing']
    strip='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
    run('cc','-std=c11','-D_DEFAULT_SOURCE','-O3','-flto','-fno-strict-aliasing','-Wall','-Wextra','-ffunction-sections','-fdata-sections','-pthread','-I'+str(sdk/'include'),'-I'+str(work),ROOT/'tests/native-worker-host.c',*[sdk/'source'/f'{name}.c' for name in sources],strip,'-lm','-o',work/'worker')
    result=run(work/'worker',capture_output=True,text=True,timeout=30)
    print(result.stdout,end='');print(result.stderr,end='',file=sys.stderr)
    (work/'result.json').write_text(json.dumps({'scope':'Native host AOT with real execution gate and simulated 20 ms presentation waits; not BBA throughput','stdout':result.stdout,'stderr':result.stderr,'exit_code':result.returncode},indent=2)+'\n')
if __name__=='__main__':main()
