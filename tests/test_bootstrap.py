#!/usr/bin/env python3
"""Execute production ConfigureTls through the SDK AOT with a 1 MiB heap.

Only platform entropy, RTC reads and the final native TLS initializer are mocked.
No filesystem bridge is linked. Synthetic seeds are fixture-only public data.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT=Path(__file__).resolve().parents[1]


def instrument_runtime(sdk,destination):
    source=(sdk/"source/managed_runtime.c").read_text()
    array_point="    array->elements_are_references = references ? 1 : 0;\n    return array;"
    allocation_point="    rebuild_free_list(heap);\n    return object;"
    if source.count(array_point)!=1 or source.count(allocation_point)!=1:
        raise RuntimeError("SDK runtime observation point changed; review the bootstrap fixture")
    source=source.replace(array_point,"    array->elements_are_references = references ? 1 : 0;\n"
                          "    dnd_bootstrap_observe_array(array);\n    return array;")
    source=source.replace(allocation_point,"    rebuild_free_list(heap);\n"
                          "    dnd_bootstrap_observe_heap(heap);\n    return object;")
    destination.write_text('#include "dnd_managed.h"\n'
                           'void dnd_bootstrap_observe_array(DndArray *);\n'
                           'void dnd_bootstrap_observe_heap(DndManagedHeap *);\n'+source)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-source",type=Path,default=ROOT.parent/"DolphinDotNet")
    parser.add_argument("--output",type=Path)
    options=parser.parse_args();sdk=options.sdk_source.resolve()
    work=options.output.resolve() if options.output else Path(tempfile.mkdtemp(prefix="updater-bootstrap-aot-"))
    work.mkdir(parents=True,exist_ok=True)
    dotnet=os.environ.get("DOTNET","dotnet")
    def run(*args,**kwargs):
        print("+"," ".join(map(str,args)),flush=True)
        return subprocess.run(list(map(str,args)),cwd=ROOT,check=True,**kwargs)
    def xml(path):return escape(str(path),{'"':'&quot;'})
    project=work/"BootstrapAot.csproj"
    production=[path for path in sorted((ROOT/"src").glob("*.cs"))
                if path.name not in ("Program.cs","UpdaterConsole.cs","ConsoleText.cs")]
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        '<Nullable>enable</Nullable><Optimize>true</Optimize></PropertyGroup><ItemGroup>'
        '<Compile Include="Program.cs" />'+''.join('<Compile Include="'+xml(path)+'" />' for path in production)+
        '<ProjectReference Include="'+xml(sdk/"src/DolphinDotNet.Core/DolphinDotNet.Core.csproj")+'" />'
        '</ItemGroup></Project>')
    (work/"Program.cs").write_text((ROOT/"tests/bootstrap-program.cs.in").read_text())
    run(dotnet,"build",project,"-c","Release","-v","quiet","-p:NuGetAudit=false")
    run(dotnet,"build",sdk/"tools/DolphinDotNet.Compiler","-c","Release","-v","quiet","-p:NuGetAudit=false")
    generated=work/"bootstrap.c"
    run(dotnet,sdk/"tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll",
        "--aot",work/"bin/Release/net8.0/BootstrapAot.dll",generated)
    code=generated.read_text()
    for call in ("dnd_calendar_read(","dnd_platform_entropy_fill_array(","dnd_net_tls_configure_local_clock("):
        if call not in code:raise AssertionError("Production bootstrap lost required native mapping: "+call)
    for forbidden in ("dnd_fs_","dnd_storage_","roots.pem","entropy.bin","utc-offset-minutes.txt"):
        if forbidden in code:raise AssertionError("Production bootstrap still reaches a TLS sidecar/filesystem: "+forbidden)
    if "dnd_net_tls_configure(" in code:raise AssertionError("Bootstrap bypassed the local-calendar verifier")
    instrumented=work/"managed_runtime.c";instrument_runtime(sdk,instrumented)
    flags=[os.environ.get("CC","cc"),"-std=c11","-O3","-flto","-fno-strict-aliasing",
           "-D_DEFAULT_SOURCE","-D_DARWIN_C_SOURCE","-Wall","-Wextra","-Werror","-Wno-unused-function",
           "-ffunction-sections","-fdata-sections","-pthread","-I"+str(sdk/"include")]
    timer=work/"timing.o"
    run(*flags,"-Ddnd_calendar_read=dnd_bootstrap_unused_calendar_read","-c",sdk/"source/timing.c","-o",timer)
    runtime=[sdk/"source"/(name+".c") for name in ("async_runtime","text","numeric","bcl","verified_copy")]
    binary=work/"bootstrap"
    strip="-Wl,-dead_strip" if sys.platform=="darwin" else "-Wl,--gc-sections"
    run(*flags,generated,ROOT/"tests/bootstrap-host.c",instrumented,*runtime,timer,strip,"-lm","-o",binary)
    results=[]
    for manual in (True,False):
        env=dict(os.environ)
        if manual:env["DND_ASYNC_MANUAL"]="1"
        else:env.pop("DND_ASYNC_MANUAL",None)
        for arguments in ([],["--gc-stress"]):
            result=run(binary,*arguments,env=env,text=True,capture_output=True,timeout=120)
            print(result.stdout,end="",flush=True)
            results.append({"manual":manual,"arguments":arguments,"passed":True,
                            "stdout":result.stdout,"stderr":result.stderr})
    (work/"results.json").write_text(json.dumps(results,indent=2)+"\n")
    print("Production TLS bootstrap evidence:",work)


if __name__=="__main__":main()
