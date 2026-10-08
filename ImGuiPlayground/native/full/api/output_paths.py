"""Fail-closed write boundary for explicit API tools; existing redirects are rejected.

Outputs must be under this project's .tmp/native/full-api, using physical paths.
Validation precedes all mkdir/write/compiler actions. Concurrent filesystem swaps
are outside this maintainer-tool contract; this is not a hostile-user sandbox.
"""
from __future__ import annotations

import importlib
import os
import stat
import sys
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parents[3]
sys.dont_write_bytecode = True
sys.path.insert(0, str(PROJECT_DIR.parent))
builder = importlib.import_module('ImGuiPlayground.native.build')


class OutputPathError(ValueError):
    pass


def root():
    return PROJECT_DIR / '.tmp' / 'native' / 'full-api'


def absolute(path):
    path = Path(path)
    if '..' in path.parts:
        raise OutputPathError('output paths may not contain parent traversal')
    # Do not resolve: that would erase symlink/dangling-redirect evidence.
    return path if path.is_absolute() else Path.cwd() / path


def checked(path, kind):
    path = absolute(path)
    try:
        path.relative_to(root())
    except ValueError as error:
        raise OutputPathError('output must be inside project .tmp/native/full-api: '+str(path)) from error
    if path == root() and kind == 'file':
        raise OutputPathError('scratch root is not an artifact leaf')
    for item in reversed((path, *path.parents)):
        try:
            info = item.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0):
            raise OutputPathError('output redirects are forbidden: '+str(item))
        if item != path or kind == 'directory':
            if not stat.S_ISDIR(info.st_mode):
                raise OutputPathError('output ancestor/directory is not a directory: '+str(item))
        elif not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
            raise OutputPathError('output leaf must be an unshared regular file: '+str(item))
    return path


def preflight(files=(), directories=(), protected=()):
    """Validate the complete write set before any output, including sibling leaves."""
    checked(root(), 'directory')
    files = [checked(p, 'file') for p in files]
    directories = [checked(p, 'directory') for p in directories]
    for output in files:
        for source in protected:
            source = Path(source)
            if output == source.absolute() or (output.exists() and source.exists() and os.path.samefile(output, source)):
                raise OutputPathError('output would overwrite a protected input: '+str(output))
    return files, directories


def prepare(files=(), directories=(), protected=()):
    files, directories = preflight(files, directories, protected)
    folders = set(directories)
    for path in [*files, *directories]:
        for parent in path.parents:
            if parent == root().parent:
                break
            folders.add(parent)
    # Reuse the shared builder's strict .tmp/native and safe-child semantics only
    # AFTER our whole-path/all-leaf preflight, never after resolving user input.
    try:
        scratch = builder.project_scratch(PROJECT_DIR)
        base = builder.safe_child(scratch, 'full-api')
        base.mkdir(exist_ok=True)
        for folder in sorted(folders, key=lambda p: (len(p.parts), str(p))):
            builder.safe_child(base, *folder.relative_to(base).parts).mkdir(exist_ok=True)
    except builder.BuildError as error:
        raise OutputPathError(str(error)) from error
    return files, directories
