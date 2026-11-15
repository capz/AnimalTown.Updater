#!/usr/bin/env python3
"""Execute production batching/SHA with the real SDK AOT and fixed 1 MiB heap."""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--sdk-source',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args()
sdk=a.sdk_source.resolve();work=a.output.resolve();work.mkdir(parents=True,exist_ok=True)
def run(*cmd,**kw):return subprocess.run(list(map(str,cmd)),cwd=ROOT,check=True,**kw)
project=ROOT/'tests/TransferAotSmoke/TransferAotSmoke.csproj'
run('dotnet','build',project,'-c','Release','-p:TransferSdkSource='+str(sdk))
run('dotnet',sdk/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--aot',project.parent/'bin/Release/net8.0/TransferAotSmoke.dll',work/'transfer.c')
strip='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
run('cc','-std=c11','-D_DEFAULT_SOURCE','-O3','-flto','-fno-strict-aliasing','-ffunction-sections','-fdata-sections','-pthread','-I'+str(sdk/'include'),work/'transfer.c',ROOT/'tests/native-transfer-host.c',*[sdk/'source'/(n+'.c') for n in ['managed_runtime','async_runtime','stw','gc_stw','text','numeric','bcl','timing']],strip,'-lm','-o',work/'transfer')
results=[]
for args in ([],['--gc-stress']):
 r=run(work/'transfer',*args,capture_output=True,text=True,timeout=60);print(r.stdout,end='');results.append({'arguments':args,'stdout':r.stdout,'stderr':r.stderr,'exit_code':r.returncode})
(work/'results.json').write_text(json.dumps({'scope':'Native host SDK AOT; real batching/hash; synthetic stream/writer; no physical SD throughput','results':results},indent=2)+'\n')
