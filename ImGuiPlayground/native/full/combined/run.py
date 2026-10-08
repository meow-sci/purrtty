#!/usr/bin/env python3
"""Opt-in combined milestone tooling; never promotes artifacts or builds graphics."""
from __future__ import annotations

import argparse
import hashlib
import importlib
import importlib.util
import json
import os
import re
import stat
import sys
from pathlib import Path

sys.dont_write_bytecode = True
HERE = Path(__file__).absolute().parent
FULL = HERE.parent
NATIVE = FULL.parent
PROJECT = NATIVE.parent
ROOT = PROJECT.parent
SCRATCH = PROJECT / '.tmp/native'
RIDS = ['osx-arm64', 'linux-x64', 'win-x64']
COMMIT = '031a18c417158427217bc5890e0ec0cb7e7b4b63'
ACCESSOR_ID = 'e86ced3fb638c94c285378697c77e6e723bd75864c337e2b557449b4e4677f72'


def require(value, message):
    if not value:
        raise ValueError('combined: ' + message)


def physical(raw, missing=False):
    path = Path(raw)
    require('..' not in path.parts, 'parent traversal: ' + str(path))
    path = path.absolute()
    for item in reversed((path, *path.parents)):
        if not item.exists() and not item.is_symlink() and missing:
            continue
        info = item.lstat()
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0), 'redirect: ' + str(item))
        require(stat.S_ISDIR(info.st_mode) or (item == path and stat.S_ISREG(info.st_mode) and info.st_nlink == 1), 'nonregular/shared input: ' + str(item))
    return path


def tree(raw):
    path = physical(raw)
    require(path.is_dir(), 'directory required: ' + str(path))
    for directory, dirs, files in os.walk(path, followlinks=False):
        for name in dirs + files:
            physical(Path(directory) / name)
    return path


def source_files():
    files = []
    for root in (PROJECT, ROOT / 'ImGuiPlayground.Checks'):
        physical(root)
        for directory, dirs, names in os.walk(root, followlinks=False):
            dirs[:] = [d for d in dirs if d not in ('.tmp', 'bin', 'obj', '__pycache__')]
            for name in dirs:
                physical(Path(directory) / name)
            for name in names:
                files.append(physical(Path(directory) / name))
            # A cached local module must not redirect an import, even though Python writes are disabled.
            cache = Path(directory) / '__pycache__'
            physical(cache, missing=True)
            if cache.exists():
                tree(cache)
    return sorted(files)


def new_output(raw, protected=(), root=SCRATCH / 'combined'):
    path = physical(raw, missing=True)
    root = physical(root, missing=True)
    require(path != root and path.is_relative_to(root), 'output outside owned scratch: ' + str(path))
    for value in protected:
        value = physical(value, missing=True)
        require(not path.is_relative_to(value) and not value.is_relative_to(path), 'output overlaps input: ' + str(value))
    for ancestor in path.parents:
        if ancestor == root:
            break
        require(not (ancestor / 'manifest.json').exists(), 'output inside published input')
    require(not path.exists(), 'new output required: ' + str(path))
    return path


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text())


def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def load(name, path):
    path = physical(path)
    physical(Path(importlib.util.cache_from_source(str(path))), missing=True)
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError('combined: selected module unavailable: ' + str(path))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def dependencies():
    # Called only AFTER complete project/input/output preflight in main.
    sys.path.insert(0, str(FULL / 'profile'))
    p = importlib.import_module('selected_profile')
    return p, importlib.import_module('enums'), importlib.import_module('layout_gate')


def logged(process, command, folder, name, timeout=300, reject=None):
    write(folder / (name + '.command.json'), command)
    result = process.run(command, capture_output=True, text=True, timeout=timeout)
    (folder / (name + '.log')).write_text(result.stdout + result.stderr)
    if reject is None:
        require(result.returncode == 0, 'command failed: ' + str(folder / (name + '.log')))
    else:
        require(result.returncode != 0 and reject in result.stdout + result.stderr, 'wrong negative diagnostic: ' + name)
    return result.stdout + result.stderr


def inventory(p, enums, selected, enum_dir):
    mapping = read(selected / 'selected-mapping.json')
    manual = load('combined_manual_contract', FULL / 'manual/contract.py')
    paths = load('combined_manual_paths', FULL / 'manual/paths.py')
    manual.load(paths)
    matrix = read(enum_dir / 'matrix.json')
    expected = enums.validate(enums.load_policy(), read(SCRATCH / 'binding-contract.json'), read(FULL / 'layout/mapping.json'), read(enum_dir / 'harvest.json'), mapping, enums.checked_census(selected, enum_dir))
    require(matrix == expected, 'enum manifest/census drift')
    code, enum_exports = enums.generate(matrix)
    require(code == (enum_dir / 'enum-fixture.cpp').read_bytes(), 'generated enum source drift')
    def header(name):
        return re.findall(r'PURR_ACCESSOR_EXPORT\s+.+?\b(purr_\w+)\([^;]*\);', (FULL / 'accessors' / name).read_text())
    managed = load('combined_profile_managed', FULL / 'profile/managed.py')
    groups = {'api': [r['entryPoint'] for r in mapping['mappings']], 'manual': list(manual.IMPORTS), 'dynamic': list(manual.DYNAMIC), 'helpers': header('accessors.h'), 'profileFixture': managed.SUPPLEMENTAL + enum_exports, 'manualFixture': list(manual.SUPPLEMENTS), 'accessorFixture': header('fixture.h')}
    require([len(groups[k]) for k in ('api', 'manual', 'dynamic', 'helpers')] == [1130, 16, 6, 23], 'production producer inventory drift')
    require(len(groups['accessorFixture']) == 19, 'accessor fixture producer inventory drift')
    rows = []
    for component, names in groups.items():
        for name in names:
            rows.append({'name': name, 'component': component, 'surface': 'fixture' if component.endswith('Fixture') else 'production', 'kind': 'writable-pointer-slot' if name.startswith('Platform_') else 'function'})
    validate_inventory(rows)
    require(sorted(r['name'] for r in rows if r['component'] in ('api', 'manual')) == sorted(r['entryPoint'] for r in read(SCRATCH / 'binding-contract.json')['imports']), 'full 1146 import identity mismatch')
    return rows


def validate_inventory(rows):
    require(len(rows) == len({r['name'] for r in rows}), 'duplicate inventory export')
    require(sum(r['surface'] == 'production' for r in rows) == 1175, 'production inventory cardinality')
    for row in rows:
        require(row['surface'] == ('fixture' if row['component'].endswith('Fixture') else 'production'), 'misclassified fixture/production export')
        require(row['kind'] == ('writable-pointer-slot' if row['name'].startswith('Platform_') else 'function'), 'misclassified export kind')


def export_match(names, rows, fixture):
    expected = sorted(r['name'] for r in rows if fixture or r['surface'] == 'production')
    require(sorted(names) == expected, 'missing/extra/duplicate or fixture-leaking binary exports')


def build(args):
    p, enums, layout = dependencies()
    source = layout.verify_source(args.selected)
    rows = inventory(p, enums, args.selected, args.enums)
    lock = read(NATIVE / 'source.lock.json')
    require(digest(NATIVE / 'source.lock.json') == '0f8dba40bb599030d1a13d8907a07a411a4c55a5b6fad208768d3e43b0602a77', 'source lock drift')
    config = p.builder.verify_config_pin(lock)
    layout_results = read(args.layout / 'results.json')
    require([r['rid'] for r in layout_results] == RIDS and all(r['staticOrdinaryStorageGate'] and r['sourceRecordSha256'] == digest(args.selected / 'source-record.json') and r['configSha256'] == digest(config) for r in layout_results), 'same-source all-target layout evidence')
    comparison = read(args.layout / 'osx-arm64/comparison.json')
    require(comparison['ordinaryStorageParity'] and not comparison['unexpectedMismatches'] and not comparison['rawAliasesSafe'], 'native layout/classified-conflict evidence')
    require(layout_results[0]['comparisonSha256'] == digest(args.layout / 'osx-arm64/comparison.json'), 'layout comparison drift')
    gen = load('combined_accessor_generator', FULL / 'accessors/generate.py')
    args.replay.mkdir(parents=True)
    accessor = gen.generate(SCRATCH / 'binding-contract.json', args.replay)
    require(accessor['identity'] == ACCESSOR_ID, 'helper identity changed')
    for name in ('fields.inc', 'fixture_fields.inc', 'identity.h', 'manifest.json'):
        require((args.accessors / name).read_bytes() == (args.replay / name).read_bytes(), 'accessor generated source drift: ' + name)
    output = args.output
    output.mkdir(parents=True)
    write(output / 'exports.json', rows)
    source_hashes = {str(f.relative_to(ROOT)): digest(f) for f in source_files()}
    write(output / 'source-freeze.json', source_hashes)
    require(p.process.run([args.zig, 'version'], capture_output=True, text=True, check=True, timeout=15).stdout.strip() == lock['toolchain']['zigVersion'], 'wrong Zig version')
    sections = load('combined_binary_symbols', FULL / 'manual/binary_symbols.py')
    mutants = load('combined_binary_negative', FULL / 'manual/binary_negative.py')
    records, database = [], []
    for rid in RIDS:
        folder = output / rid
        folder.mkdir()
        command = p.compile_command(source, config, lock, rid, args.zig) + ['-I', str(args.accessors), '-I', str(args.enums), '-I', str(FULL / 'accessors')]
        core = [source / n for n in lock['build']['sourceFiles'] if n not in ('imgui.cpp', 'src/PlaygroundBrutalAdapter.cpp')]
        common = core + [args.selected / 'generated/wrappers.cpp', FULL / 'manual/manual.cpp', FULL / 'accessors/accessors.cpp']
        extra = [FULL / 'manual/fixture.cpp', FULL / 'profile/managed_fixture.cpp', HERE / 'fixture-core.cpp']
        objects = {}
        for index, item in enumerate(common + [source / 'imgui.cpp'] + extra):
            obj = folder / (str(index) + '.o')
            logged(p.process, command + ['-c', str(item), '-o', str(obj)], folder, 'compile-' + str(index))
            objects[item] = obj
            database.append({'file': str(item), 'directory': str(output), 'arguments': command + ['-c', str(item)]})
        logged(p.process, command + ['-c', str(FULL / 'manual/abi_proof.cpp'), '-o', str(folder / 'manual-abi-proof.o')], folder, 'manual-abi-proof')
        header = folder / 'combined-header.c'
        header.write_text('#include "manual.h"\n#include "accessors.h"\n_Static_assert(sizeof(PurrFieldInfo)==24,"info");\n_Static_assert(sizeof(PurrTriple)==12,"triple");\n_Static_assert(sizeof(PurrSharedSnapshot)==20,"shared");\n_Static_assert(sizeof(PurrTextEditSnapshot)==16,"textedit");\n_Static_assert(sizeof(PurrCellSnapshot)==8,"cell");\nvoid (*purr_textv)(const char*, va_list) = &TextV;\n')
        cflags = [args.zig, 'cc', '-std=c11', '-fno-strict-aliasing', '-target', lock['targets'][rid]['zigTarget'], '-I', str(FULL / 'manual'), '-I', str(FULL / 'accessors'), '-include', str(config)]
        if rid == 'osx-arm64':
            cflags += ['-isysroot', p.builder.macos_sdk_path()]
        logged(p.process, cflags + ['-c', str(header), '-o', str(folder / 'combined-header.o')], folder, 'combined-c11')
        # Retain actual preprocessed body and include trace, not just resolver breadth.
        logged(p.process, command + ['-E', '-H', str(HERE / 'fixture-core.cpp'), '-o', str(folder / 'fixture-core.ii')], folder, 'preprocess')
        # Zig may serve a cached preprocess result without repeating -H stderr.
        # Entry line markers in the actual .ii are stable even on cache hits.
        trace = (folder / 'fixture-core.ii').read_text()
        included = re.findall(r'^# 1 "([^"]+)" 1$', trace, re.M)
        for expected in (source / 'imgui.cpp', FULL / 'combined/../accessors/fixture.cpp', args.enums / 'enum-fixture.cpp'):
            require(included.count(str(expected)) == 1, 'include ownership: ' + str(expected))
        write(folder / 'include-ownership.json', included)
        # Explicit externally supplied-marker negative: fail before a native body is used.
        logged(p.process, command + ['-DPURR_COMBINED_CORE_INCLUDED=1', '-c', str(HERE / 'fixture-core.cpp'), '-o', str(folder / 'bad.o')], folder, 'marker-negative', reject='combined core marker must not be supplied')
        shared = ['-dynamiclib', '-Wl,-install_name,@rpath/libimgui.dylib'] if rid == 'osx-arm64' else ['-shared']
        if rid == 'linux-x64':
            shared += ['-Wl,--no-undefined', '-Wl,-soname,libimgui.so']
        ownership = {}
        for item, obj in objects.items():
            text = logged(p.process, ['nm', '--defined-only', str(obj)], folder, obj.stem + '-symbols')
            ownership[str(item)] = len(re.findall(r'^\S+\s+[BSD]\s+_?GImGui$', text, re.M))
        def check_ownership(inputs, owner, definitions=ownership):
            require(len(inputs) == len(set(inputs)), 'duplicate linked object')
            require(sum(definitions[str(f)] for f in inputs) == 1 and definitions[str(owner)] == 1, 'conflicting engine ownership')
        try:
            check_ownership(common + extra + [source / 'imgui.cpp'], HERE / 'fixture-core.cpp')
        except ValueError as error:
            require('conflicting engine ownership' in str(error), 'wrong composition negative')
            (folder / 'duplicate-core-negative.log').write_text(str(error) + '\n')
        else:
            raise ValueError('combined: duplicate engine accepted')
        target_records = []
        for fixture in (False, True):
            kind = 'fixture' if fixture else 'production'
            destination = folder / kind
            destination.mkdir()
            artifact = destination / lock['targets'][rid]['file']
            inputs = common + (extra if fixture else [source / 'imgui.cpp'])
            check_ownership(inputs, HERE / 'fixture-core.cpp' if fixture else source / 'imgui.cpp')
            link = command + shared + [str(objects[item]) for item in inputs] + ['-o', str(artifact)]
            logged(p.process, link, destination, 'link')
            symbol_command = ['objdump', '-p', str(artifact)] if rid == 'win-x64' else ['nm', '-gU', str(artifact)] if rid == 'osx-arm64' else ['nm', '--dynamic', '--defined-only', str(artifact)]
            symbols = logged(p.process, symbol_command, destination, 'exports')
            if rid == 'win-x64':
                names = [parts[2] for line in symbols.split('Export Table:')[1].splitlines() if len(parts := line.split()) == 3 and parts[0].isdigit() and parts[1].startswith('0x')]
            else:
                names = [parts[-1][1:] if rid == 'osx-arm64' else parts[-1] for line in symbols.splitlines() if len(parts := line.split()) == 3]
            linker = ['__dso_handle', '_mh_dylib_header'] if rid == 'osx-arm64' else []
            owned = [name for name in names if name not in linker]
            export_match(owned, rows, fixture)
            observed = sections.inspect(artifact.read_bytes(), rid, owned)
            write(destination / 'symbol-sections.json', observed)
            arch = logged(p.process, ['file', str(artifact)], destination, 'architecture')
            require(('arm64' if rid == 'osx-arm64' else 'x86-64') in arch, 'wrong artifact architecture')
            deps = logged(p.process, ['otool', '-L', str(artifact)] if rid == 'osx-arm64' else ['objdump', '-p', str(artifact)], destination, 'dependencies')
            dependency_names = re.findall(r'^[ \t]+(/\S+|@rpath/\S+) \(', deps, re.M) if rid == 'osx-arm64' else re.findall(r'(?:NEEDED|DLL Name:)\s+(\S+)', deps)
            install_name = None
            if rid == 'osx-arm64':
                require(dependency_names[0] == '@rpath/libimgui.dylib', 'Mach-O install name')
                install_name, dependency_names = dependency_names[0], dependency_names[1:]
            require(bool(dependency_names), 'dependency inspection empty')
            require(not any('stdc++' in name or 'libc++' in name for name in dependency_names), 'unexpected C++ runtime dependency')
            write(destination / 'ownership.json', {'translationUnits': [str(f) for f in inputs], 'objects': [str(objects[f]) for f in inputs], 'coreOwner': str(HERE / 'fixture-core.cpp' if fixture else source / 'imgui.cpp'), 'requiredFlags': command, 'separateFixtureBodiesLinked': False})
            target_records.append({'kind': kind, 'path': str(artifact), 'sha256': digest(artifact), 'ownedExports': len(owned), 'dependencies': dependency_names, 'installName': install_name, 'executed': False})
            # Binary negatives retain all names while moving slot/function to the wrong section.
            for name, opposite in [('Platform_GetWindowPos_ManagedFunctionPointer', 'Text'), ('Text', 'Platform_GetWindowPos_ManagedFunctionPointer')]:
                changed = mutants.relocate(artifact.read_bytes(), rid, name, opposite)
                try:
                    sections.inspect(changed, rid, owned)
                except ValueError as error:
                    require(name in str(error), 'wrong binary-kind negative')
                    (destination / (name + '-negative.log')).write_text(str(error) + '\n')
                else:
                    raise ValueError('combined: binary-kind negative accepted')
        if rid == 'osx-arm64':
            actual_fixture = folder / 'fixture' / lock['targets'][rid]['file']
            native_test = folder / 'manual-native-test'
            logged(p.process, command + [str(FULL / 'manual/native_test.cpp'), str(actual_fixture), '-Wl,-rpath,' + str(actual_fixture.parent), '-o', str(native_test)], folder, 'manual-native-build')
            logged(p.process, [str(native_test)], folder, 'manual-native-run', timeout=30)
            target_records[1]['executed'] = True
        records.append({'rid': rid, 'artifacts': target_records, 'engineDefinitionsByTU': ownership, 'incorrectMarkerRejected': True, 'duplicateCoreRejected': True, 'c11': True, 'manualAbiProof': True, 'combinedManualNativeExecuted': rid == 'osx-arm64'})
    write(output / 'compile_commands.json', database)
    manifest = {'schema': 'purr.combined.milestone1', 'version': 1, 'sourceCommit': COMMIT, 'sourceRecordSha256': digest(args.selected / 'source-record.json'), 'configSha256': digest(config), 'helperIdentity': ACCESSOR_ID, 'productionExports': 1175, 'fixtureOnlyExports': sum(r['surface'] == 'fixture' for r in rows), 'targets': records, 'fullQualification': False, 'inputs': {k: str(getattr(args, k)) for k in ('selected', 'enums', 'layout', 'accessors')}, 'evidenceHashes': {str(f): digest(f) for root in (args.selected, args.enums, args.layout, args.accessors) for f in sorted(root.rglob('*')) if f.is_file() and f.suffix in ('.json', '.cpp', '.inc', '.h')}}
    layout.verify_source(args.selected)
    require(source_hashes == {str(f.relative_to(ROOT)): digest(f) for f in source_files()}, 'source changed during build')
    write(output / 'manifest.json', manifest)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['build', 'managed', 'test'])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--selected', type=Path)
    parser.add_argument('--enums', type=Path)
    parser.add_argument('--layout', type=Path)
    parser.add_argument('--accessors', type=Path)
    parser.add_argument('--build', type=Path)
    parser.add_argument('--managed-directory', type=Path)
    parser.add_argument('--zig', default='zig')
    args = parser.parse_args()
    # No project imports, resolution, input reads, mkdir or compiler before this boundary.
    source_files()
    for path in (SCRATCH / 'binding-contract.json', SCRATCH / ('imgui-' + COMMIT + '.tar.gz')):
        physical(path)
    tree(SCRATCH / 'source' / COMMIT)
    names = ('selected', 'enums', 'layout', 'accessors') if args.mode == 'build' else ('build',)
    for name in names:
        require(getattr(args, name) is not None, '--' + name + ' required')
        setattr(args, name, tree(getattr(args, name)))
    if args.mode == 'managed':
        # Existing selection aliases retain their documented precedence.
        selection = os.environ.get('KSAFolder') or args.managed_directory or os.environ.get('KSA_DLL_DIR')  # noqa: SIM112
        require(selection is not None, 'explicit managed directory required')
        args.managed_directory = tree(selection)
        names += ('managed_directory',)
    args.output = new_output(args.output, [getattr(args, name) for name in names])
    if args.mode == 'build':
        args.replay = new_output(SCRATCH / 'accessors' / ('combined-' + args.output.name + '-replay'), [getattr(args, name) for name in names], SCRATCH / 'accessors')
        build(args)
    else:
        # Exact checked local module load after preflight, including bytecode-cache path.
        module = load('combined_' + args.mode, HERE / (args.mode + '.py'))
        module.run(args, sys.modules[__name__])


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
