#!/usr/bin/env python3
"""Read verified AOT hash timings from real emulated PowerPC memory; no BBA rate."""
import argparse
import hashlib
import json
from pathlib import Path
import socket
import struct
import subprocess
import sys
import time

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-source',required=True,type=Path)
    parser.add_argument('--work',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--dolphin',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--port',type=int,default=7176)
    args=parser.parse_args();work=args.work.resolve();root=args.output.resolve();root.mkdir(parents=True,exist_ok=False)
    sys.path.insert(0,str(args.sdk_source.resolve()/'scripts'))
    from dolphin_controller_test import request
    from dolphin_gdb_probe import _send,_reply
    config=root/'user/Config';config.mkdir(parents=True)
    (config/'Dolphin.ini').write_text('[Core]\nGFXBackend = Null\nCPUCore = 1\nCPUThread = False\nEmulationSpeed = 0.0\n[General]\nGDBPort = '+str(args.port)+'\n[Interface]\nDebugModeEnabled = True\n[DSP]\nBackend = No Audio Output\n')
    symbols={}
    names=['benchmark_result','benchmark_stage','dnd_static_AnimalTown_Updater_Program_After','dnd_static_AnimalTown_Updater_Program_Before']
    for line in subprocess.check_output(['nm','-a',str(work/'bench.elf')],text=True).splitlines():
        p=line.split()
        if len(p)==3 and p[2] in names:symbols[p[2]]=int(p[0],16)
    if set(symbols)!=set(names):raise AssertionError('Missing benchmark symbols')
    log=(root/'emulator.log').open('w')
    process=subprocess.Popen([args.dolphin,'--platform','headless','-v','Null','-u',str(root/'user'),'-e',str(work/'bench.dol')],stdout=log,stderr=subprocess.STDOUT)
    conn=None
    try:
        deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if process.poll() is not None:raise RuntimeError('Dolphin exited '+str(process.returncode))
            try:conn=socket.create_connection(('127.0.0.1',args.port),timeout=.3);break
            except OSError:time.sleep(.1)
        if conn is None:raise RuntimeError('No debugger')
        time.sleep(.3)
        for attempt in range(15):
            _send(conn,'c');time.sleep(2);conn.sendall(b'\x03');_reply(conn,time.monotonic()+8)
            stage=int(request(conn,f"m{symbols['benchmark_stage']:x},4"),16)
            if stage==3:break
        if stage!=3:raise AssertionError('Guest benchmark did not finish')
        status=int(request(conn,f"m{symbols['benchmark_result']:x},4"),16)
        if status!=0:raise AssertionError('Guest benchmark/digest failed: '+str(status))
        arrays={}
        for name in ['Before','After']:
            address=int(request(conn,f"m{symbols['dnd_static_AnimalTown_Updater_Program_'+name]:x},4"),16)
            header=bytes.fromhex(request(conn,f'm{address:x},18'))
            if struct.unpack_from('>II',header,8)!=(5,8):raise AssertionError('Unexpected managed timing array')
            arrays[name]=list(struct.unpack('>5q',bytes.fromhex(request(conn,f'm{address+24:x},28'))))
            if min(arrays[name])<=0:raise AssertionError('Empty guest timing')
        result=json.loads((work/'build.json').read_text())
        result.update(scope='Dolphin emulated PowerPC guest-clock SHA phase; no BBA, TLS or SD throughput measured',samples_us=arrays,dol_sha256=hashlib.sha256((work/'bench.dol').read_bytes()).hexdigest(),emulator=subprocess.check_output([args.dolphin,'--version'],text=True).strip())
        (root/'result.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result),flush=True)
    finally:
        if conn:conn.close()
        process.terminate()
        try:process.wait(timeout=5)
        except subprocess.TimeoutExpired:process.kill();process.wait()
        log.close()

if __name__=='__main__':main()
