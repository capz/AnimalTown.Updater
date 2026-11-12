#!/usr/bin/env python3
"""Install the project-owned libogc argv bridge into an already bundled build."""
from pathlib import Path
import re
import shutil
import sys

def patch(native):
    native=Path(native)
    generated=native/'source/generated_program.c'
    text=generated.read_text()
    pattern=r'intptr_t (dnd_value_\w*_NativeLaunchArguments_Copy_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\r?\n\}'
    matches=list(re.finditer(pattern,text,re.S))
    if len(matches)!=1:
        raise RuntimeError('Expected exactly one project NativeLaunchArguments.Copy bridge')
    text=re.sub(pattern,lambda m:f'intptr_t {m[1]}(intptr_t {m[2]}) {{ return animaltown_launch_arguments_copy((DndArray*){m[2]}); }}',text,flags=re.S)
    text='#include "dnd_managed.h"\nextern int animaltown_launch_arguments_copy(DndArray*);\n'+text
    generated.write_text(text)
    root=Path(__file__).resolve().parent
    shutil.copy2(root/'native-launch-arguments.c',native/'source/animaltown_launch_arguments.c')
    shutil.copy2(root/'native-launch-arguments.h',native/'include/animaltown_launch_arguments.h')

if __name__=='__main__':
    if len(sys.argv)!=2:raise SystemExit('usage: patch-launch-arguments.py NATIVE_DIRECTORY')
    patch(sys.argv[1])
