"""Lexical preflight for accessor-owned inputs and outputs; no project imports.

Entrypoints validate THIS module's leaf/ancestors before loading it from source.
No protection against edits to the invoked bootstrap or concurrent hostile swaps
is claimed. Compiler/framework resolution is deliberately outside these guards.
"""
from __future__ import annotations

import importlib.util
import os
import sys
from pathlib import Path
from typing import Any

PREFIX = 'accessor preflight: '


def checked_path(path: Path) -> Path:
    if '..' in path.parts:
        raise ValueError(PREFIX + 'parent traversal not allowed: ' + str(path))
    path = path.absolute()
    for candidate in (path, *path.parents):
        if candidate.is_symlink():
            raise ValueError(PREFIX + 'symlink not allowed: ' + str(candidate))
    return path


def input_file(path: Path) -> Path:
    path = checked_path(path)
    if not path.is_file():
        raise ValueError(PREFIX + 'input file missing or not regular: ' + str(path))
    return path


def input_tree(path: Path) -> Path:
    path = checked_path(path)
    if not path.is_dir():
        raise ValueError(PREFIX + 'input directory missing: ' + str(path))
    for directory, directories, files in os.walk(path, followlinks=False):
        for name in directories + files:
            checked_path(Path(directory) / name)
    return path


def preflight_component(here: Path) -> None:
    """Check the complete selected closure BEFORE any other project import/read.

    Missing data is diagnosed by the consuming operation, so --help needs no
    downloaded archive/contract. Existing/dangling redirects are always rejected.
    Actual module loads require regular files, not a permissive import fallback.
    """
    here = input_tree(here)
    native = here.parents[1]
    project = native.parent
    layout = here.parent / 'layout'
    input_tree(layout)
    paths = [here / name for name in (
        'path_guard.py', 'generate.py', 'run.py', 'run_managed.py', 'self_test.py',
        'input_self_test.py', 'accessors.cpp', 'accessors.h', 'fixture.cpp', 'fixture.h')]
    paths += [layout / name for name in (
        'generate.py', 'patch_source.py', 'mapping.json', 'bitfields.json',
        'enum-transport.json', 'patch-manifest.json')]
    paths += [native / 'process_utils.py', native / 'source.lock.json',
              native / 'include/playground_imgui_config.h',
              project / '.tmp/native/binding-contract.json',
              project / '.tmp/native/imgui-031a18c417158427217bc5890e0ec0cb7e7b4b63.tar.gz',
              project / 'NativeFieldAccessors.cs',
              project.parent / 'ImGuiPlayground.Checks/FullAccessorChecks.cs']
    for path in paths:
        checked_path(path)


def load_module(name: str, path: Path) -> Any:
    """Load the selected .py without sys.path fallbacks; guard its cache too."""
    path = input_file(path)
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError(PREFIX + 'cannot load selected module: ' + str(path))
    checked_path(Path(importlib.util.cache_from_source(str(path))))
    sys.dont_write_bytecode = True
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def output_location(path: Path, scratch_root: Path, protected: tuple[Path, ...] = ()) -> Path:
    """Validate location before mkdir, reads of consumer inputs, or compiler work."""
    path = checked_path(path)
    root = checked_path(scratch_root)
    if not path.is_relative_to(root) or path == root:
        raise ValueError(PREFIX + 'output must be a descendant of .tmp/native/accessors')
    for selected in protected:
        selected = checked_path(selected)
        if path.is_relative_to(selected) or selected.is_relative_to(path):
            raise ValueError(PREFIX + 'output overlaps protected input: ' + str(selected))
    # A previous build's generated native source remains protected even when not
    # selected as this invocation's input. Do not write a new build inside it.
    for ancestor in path.parents:
        if ancestor == root:
            break
        receipt = checked_path(ancestor / 'source-record.json')
        if receipt.exists() and path.is_relative_to(ancestor / 'source'):
            raise ValueError(PREFIX + 'output inside generated source tree: ' + str(ancestor / 'source'))
    return path


def generation_output(path: Path, project: Path, contract: Path) -> Path:
    path = output_location(path, project / '.tmp/native/accessors', (contract,))
    for name in ('fields.inc', 'fixture_fields.inc', 'identity.h', 'manifest.json'):
        leaf = checked_path(path / name)
        if leaf.exists():
            raise ValueError(PREFIX + 'new generation outputs required: ' + str(leaf))
    return path
