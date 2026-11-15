#!/usr/bin/env python3
"""Compare production SHA with the pre-optimization baseline through the pinned AOT.

This measures a CPU phase, not HTTPS/BBA/SD throughput. PowerPC mode produces an
ELF/DOL for tests/run-throughput-dolphin.py; host mode executes the native AOT.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
from xml.sax.saxutils import escape

ROOT=Path(__file__).resolve().parents[1]
BASELINE='c03ea7d8bf1073e89497dc4c1db29328164875a0'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-source',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--storage-probe',action='store_true',help='host-only optional 32 MiB checked-writer batching diagnostic')
    parser.add_argument('--target',choices=['host','powerpc'],default='host')
    parser.add_argument('--devkitpro',type=Path)
    parser.add_argument('--libogc-overlay',type=Path)
    args=parser.parse_args();sdk=args.sdk_source.resolve();work=args.output.resolve();work.mkdir(parents=True,exist_ok=True)
    def run(*command,**kw):return subprocess.run(list(map(str,command)),check=True,cwd=ROOT,**kw)
    lock=json.loads((ROOT/'sdk.lock.json').read_text())
    actual=subprocess.check_output(['git','-C',str(sdk),'rev-parse','HEAD'],text=True).strip()
    if actual!=lock['commit'] or subprocess.check_output(['git','-C',str(sdk),'status','--porcelain','--untracked-files=no'],text=True).strip():raise SystemExit('Use a clean checkout of the exact sdk.lock.json revision')
    original=run('git','show',BASELINE+':src/Sha256.cs',capture_output=True,text=True).stdout
    candidate=(ROOT/'src/Sha256.cs').read_text()
    for name,source in [('BaselineSha256',original),('CandidateSha256',candidate)]:
        (work/(name+'.cs')).write_text(source.replace('class Sha256','class '+name))
    digest=hashlib.sha256(bytes(i%251 for i in range(16384))*16).hexdigest()
    program='''using System; using Dolphin.Timing;
namespace AnimalTown.Updater;
internal static class Program {
 internal static long[] Before=new long[5],After=new long[5];
 private static long Baseline(byte[] b){var h=new BaselineSha256();long t=Clock.Timestamp;for(int i=0;i<16;i++)h.Append(b,0,b.Length);if(h.Finish()!="DIGEST")throw new Exception("Baseline digest");return Clock.Timestamp-t;}
 private static long Candidate(byte[] b){var h=new CandidateSha256();long t=Clock.Timestamp;for(int i=0;i<16;i++)h.Append(b,0,b.Length);if(h.Finish()!="DIGEST")throw new Exception("Candidate digest");return Clock.Timestamp-t;}
 private static int Main(){var b=new byte[16384];for(int i=0;i<b.Length;i++)b[i]=(byte)(i%251);
 Baseline(b);Candidate(b);
 for(int i=0;i<5;i++){if(i%2==0){Before[i]=Baseline(b);After[i]=Candidate(b);}else{After[i]=Candidate(b);Before[i]=Baseline(b);}Console.WriteLine("HASH before_us="+Before[i]+" after_us="+After[i]);}return 0;}
}'''.replace('DIGEST',digest)
    (work/'Program.cs').write_text(program)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="BaselineSha256.cs"/><Compile Include="CandidateSha256.cs"/><ProjectReference Include="'+escape(str(sdk/'src/DolphinDotNet.Core/DolphinDotNet.Core.csproj'))+'"/></ItemGroup></Project>'
    (work/'Bench.csproj').write_text(project)
    run('dotnet','build',work/'Bench.csproj','-c','Release','-v','quiet')
    compiler=sdk/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll'
    run('dotnet','build',sdk/'tools/DolphinDotNet.Compiler','-c','Release','-v','quiet')
    run('dotnet',compiler,'--aot',work/'bin/Release/net8.0/Bench.dll',work/'bench.c')
    (work/'host.c').write_text((ROOT/'tests/throughput-host.c').read_text())
    sources=[sdk/'source'/(s+'.c') for s in ['managed_runtime','async_runtime','stw','gc_stw','text','numeric','bcl','timing']]
    flags=['-O2','-g','-fno-strict-aliasing','-ffunction-sections','-fdata-sections','-I'+str(sdk/'include')]
    metadata={'baseline_commit':BASELINE,'sdk_commit':actual,'candidate_sha256':hashlib.sha256(candidate.encode()).hexdigest(),'bytes_per_sample':262144,'pairs':5,'digest':digest,'compiler_flags':flags[:5]}
    if args.storage_probe and args.target!='host':raise SystemExit('--storage-probe is host-only')
    if args.target=='host':
        run('cc',*flags,'-D_DEFAULT_SOURCE','-pthread',work/'host.c',*sources,'-Wl,--gc-sections','-lm','-o',work/'bench-host')
        result=run(work/'bench-host',capture_output=True,text=True,timeout=60)
        pairs=re.findall(r'HASH before_us=(\d+) after_us=(\d+)',result.stdout)
        if len(pairs)!=5:raise AssertionError(result.stdout)
        metadata.update(scope='Host native AOT SHA CPU phase; not GameCube throughput',samples_us={'Before':[int(a) for a,b in pairs],'After':[int(b) for a,b in pairs]})
        (work/'result.json').write_text(json.dumps(metadata,indent=2)+'\n');print(result.stdout,end='')
        if args.storage_probe:
            run('cc',*flags,ROOT/'tests/throughput-write-host.c',sdk/'source/contiguous_write.c','-Wl,--gc-sections','-o',work/'write-bench')
            probe=run(work/'write-bench',work/'write.data',capture_output=True,text=True,timeout=120)
            pairs=[tuple(map(int,line.split())) for line in probe.stdout.splitlines()]
            if len(pairs)!=9:raise AssertionError(probe.stdout)
            (work/'write-result.json').write_text(json.dumps({'scope':'Host filesystem + real pinned checked writer, including fsync; not SD or HTTP throughput','bytes_per_sample':33554432,'samples_us':{'Write4096':[a for a,b in pairs],'Write16384':[b for a,b in pairs]}},indent=2)+'\n')
    else:
        if not args.devkitpro or not args.libogc_overlay:raise SystemExit('PowerPC requires --devkitpro and --libogc-overlay (SDK corrected libogc.a directory)')
        dev=args.devkitpro.resolve();ogc=dev/'libogc2/gamecube'
        run(dev/'devkitPPC/bin/powerpc-eabi-gcc',*flags,'-DGEKKO','-DDND_GAMECUBE_STORAGE','-mogc','-mcpu=750','-meabi','-mhard-float','-I'+str(ogc/'include'),work/'host.c',*sources,'-L'+str(args.libogc_overlay.resolve()),'-L'+str(ogc/'lib'),'-Wl,--gc-sections','-logc','-lm','-o',work/'bench.elf')
        run(dev/'tools/bin/elf2dol',work/'bench.elf',work/'bench.dol')
    (work/'build.json').write_text(json.dumps(metadata,indent=2)+'\n')

if __name__=='__main__':main()
