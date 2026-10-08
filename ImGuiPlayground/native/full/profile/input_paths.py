"""Existing-path and immutable-input preflight for this component only.

No concurrent-swap guarantee; toolchain/SDK and standard library resolution is not
restricted. Owned modules and explicitly consumed project/managed inputs are.
"""
from __future__ import annotations

import os
import stat
from pathlib import Path

HERE = Path(__file__).absolute().parent
NATIVE = HERE.parents[1]
PROJECT = NATIVE.parent
COMMIT = '031a18c417158427217bc5890e0ec0cb7e7b4b63'


def physical(path, directory=False):
    path = Path(path)
    if '..' in path.parts:
        raise ValueError('input parent traversal: '+str(path))
    path = path.absolute()
    for item in reversed((path, *path.parents)):
        info = item.lstat()  # Missing/dangling inputs fail; no resolve before lstat.
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0):
            raise ValueError('input redirect: '+str(item))
        is_directory = item != path or directory
        if is_directory and not stat.S_ISDIR(info.st_mode):
            raise ValueError('input ancestor is not a directory: '+str(item))
        if not is_directory and (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1):
            raise ValueError('input must be an unshared regular file: '+str(item))
    return path


def tree(path):
    path = physical(path, True)
    for root, directories, files in os.walk(path, followlinks=False):
        for name in directories:
            physical(Path(root)/name, True)
        for name in files:
            physical(Path(root)/name)
    return path


def modules():
    # Before any local/dependency import, including its package initializers.
    tree(HERE)
    tree(HERE.parent/'api')
    tree(HERE.parent/'layout')
    for name in ('__init__.py', 'build.py', 'process_utils.py'):
        physical(NATIVE/name)


def fixed_inputs():
    for name in ('source.lock.json', 'include/playground_imgui_config.h'):
        physical(NATIVE/name)
    scratch = PROJECT/'.tmp/native'
    physical(scratch/'binding-contract.json')
    physical(scratch/('imgui-'+COMMIT+'.tar.gz'))
    tree(scratch/'source'/COMMIT)


def disjoint_output(output, inputs):
    output = Path(output).absolute()
    for raw in inputs:
        path = Path(raw).absolute()
        if output == path or path in output.parents or output in path.parents:
            raise ValueError('output overlaps immutable input: '+str(path))


def selected(path):
    # A selected profile's complete tree, not just its source/header leaves, is an input.
    return tree(path)


def managed_selection(path):
    path = physical(path, True)
    for name in ('Brutal.ImGui', 'Brutal.Core.Common', 'Brutal.Core.Numerics', 'Brutal.Core.Strings'):
        physical(path/(name+'.dll'))
    physical(PROJECT/'NuGet.Config')
    for name in ('FullEnumAbiChecks.cs', 'FullProfileAbiChecks.cs'):
        physical(PROJECT.parent/'ImGuiPlayground.Checks'/name)
    return path
