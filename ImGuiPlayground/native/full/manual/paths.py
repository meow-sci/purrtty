"""Concrete manual-tool input/import and new-output boundaries; no redirect reuse."""
import importlib.util
import re
import stat
import sys
from pathlib import Path

HERE = Path(__file__).absolute().parent
PROJECT = HERE.parents[2]
ROOT = PROJECT / '.tmp' / 'native' / 'full-manual'
COMPONENT_FILES = (
    '.clangd', 'README.md', 'abi_proof.cpp', 'binary_symbols.py', 'binary_negative.py',
    'check.py', 'contract.py', 'fixture.cpp', 'fixture.h', 'manifest.json',
    'manual.cpp', 'manual.h', 'native_test.cpp', 'paths.py', 'self_test.py',
    'entrypoint_test.py',
)
MANAGED_NAMES = ('Brutal.ImGui', 'Brutal.Core.Common', 'Brutal.Core.Numerics',
                 'Brutal.Core.Strings', 'Brutal.Core.Logging', 'Brutal.Glfw')


class BoundaryError(ValueError):
    pass


def inspect(path):
    path = Path(path)
    if '..' in path.parts:
        raise BoundaryError('parent traversal forbidden')
    path = path.absolute()
    for item in reversed((path, *path.parents)):
        try:
            info = item.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0):
            raise BoundaryError('redirect forbidden: ' + str(item))
        if item != path and not stat.S_ISDIR(info.st_mode):
            raise BoundaryError('non-directory ancestor: ' + str(item))
        if item == path and not (stat.S_ISDIR(info.st_mode) or (stat.S_ISREG(info.st_mode) and info.st_nlink == 1)):
            raise BoundaryError('non-regular/shared leaf: ' + str(item))
    return path


def input_file(path):
    path = inspect(path)
    if not path.is_file():
        raise BoundaryError('input file missing: ' + str(path))
    return path


def component_inputs():
    """Closed leaves, validated before any component/dependency module imports.

    Optional package initializers are checked too: namespace packages must not
    turn into executing redirected __init__.py files. System/SDK/toolchain paths
    are deliberately not subject to this project-input redirect restriction.
    """
    native = PROJECT / 'native'
    layout = native / 'full/layout'
    files = [HERE / name for name in COMPONENT_FILES]
    files += [PROJECT.parent / 'ImGuiPlayground.Checks/FullManualAbiChecks.cs',
              native / 'full/api/purr_abi.h', native / 'build.py', native / 'process_utils.py',
              layout / 'patch_source.py', layout / 'patch-manifest.json',
              native / 'source.lock.json', native / 'include/playground_imgui_config.h',
              PROJECT / '.tmp/native/binding-contract.json',
              PROJECT / '.tmp/native/imgui-031a18c417158427217bc5890e0ec0cb7e7b4b63.tar.gz']
    files += [layout / 'patches' / (name + '.json') for name in
              ('01-keymods', '02-stack-datatype', '03-active-mouse', '04-dock-boundary', '05-sort-forward', '06-sort-definition')]
    for path in files:
        input_file(path)
    for directory in (PROJECT, native, native / 'full', layout):
        inspect(directory / '__init__.py')
    return files


def load_module(name, path):
    """No sys.path search can select a different component module."""
    path = input_file(path)
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise BoundaryError('module loader unavailable: ' + str(path))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def selected_managed(path):
    selected = inspect(path)
    if not selected.is_dir():
        raise BoundaryError('selected managed directory missing: ' + str(selected))
    for name in MANAGED_NAMES:
        input_file(selected / (name + '.dll'))
    return selected


def disjoint(output, protected):
    output, protected = inspect(output), inspect(protected)
    if output == protected or output in protected.parents or protected in output.parents:
        raise BoundaryError('output/input overlap: ' + str(protected))


def planned_tree(name, protected=()):
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}', name):
        raise BoundaryError('output must be a simple new run name')
    output = inspect(ROOT / name)
    if output.exists():
        raise BoundaryError('output already exists: ' + str(output))
    for source in protected:
        disjoint(output, source)
    return output


def new_tree(name, protected=()):
    output = planned_tree(name, protected)
    # A fresh subtree is the entire compiler/dotnet write plan. Existing leaves
    # cannot alias inputs. Concurrent hostile swaps are outside this contract.
    output.mkdir(parents=True)
    return output
