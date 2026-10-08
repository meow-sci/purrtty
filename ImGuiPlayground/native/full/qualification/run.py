#!/usr/bin/env python3
"""Explicit full qualification build/bundle/target runner; no ordinary native hooks."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import platform
import re
import shutil
import stat
import struct
import sys
import zipfile
from pathlib import Path, PurePosixPath
from typing import Any, cast

sys.dont_write_bytecode = True
HERE = Path(__file__).absolute().parent
ROOT = HERE.parents[3]
PROJECT = ROOT / 'ImGuiPlayground'
SCRATCH = PROJECT / '.tmp/native/qualification'
RIDS = {'osx-arm64': ('Darwin', 'arm64', 'libimgui.dylib'), 'linux-x64': ('Linux', 'x86_64', 'libimgui.so'), 'win-x64': ('Windows', 'amd64', 'imgui.dll')}


def require(value, message):
    if not value:
        raise ValueError('qualification: ' + message)


def physical(raw, missing=False):
    path = Path(raw)
    require('..' not in path.parts, 'parent traversal: ' + str(path))
    path = path.absolute()
    for item in reversed((path, *path.parents)):
        try:
            info = item.lstat()
        except FileNotFoundError:
            if missing:
                continue
            raise
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0), 'redirect: ' + str(item))
        require(stat.S_ISDIR(info.st_mode) or (item == path and stat.S_ISREG(info.st_mode) and info.st_nlink == 1), 'nonregular/shared path: ' + str(item))
    return path


def tree(raw):
    path = physical(raw)
    require(path.is_dir(), 'directory required')
    for base, dirs, files in os.walk(path, followlinks=False):
        for name in dirs + files:
            physical(Path(base) / name)
    return path


def source_files(root=ROOT):
    files = []
    for folder in ('ImGuiPlayground', 'ImGuiPlayground.Checks'):
        physical(root / folder)
        for base, dirs, names in os.walk(root / folder, followlinks=False):
            dirs[:] = [n for n in dirs if n not in ('.tmp', 'bin', 'obj', '__pycache__')]
            for name in dirs:
                physical(Path(base) / name)
            for name in names:
                files.append(physical(Path(base) / name))
            cache = physical(Path(base) / '__pycache__', missing=True)
            if cache.exists():
                tree(cache)
    return sorted(files)


def fresh(raw, protected):
    path = physical(raw, missing=True)
    require(not path.exists(), 'fresh output required: ' + str(path))
    for value in protected:
        value = physical(value, missing=True)
        require(not path.is_relative_to(value) and not value.is_relative_to(path), 'output overlaps protected input: ' + str(value))
    return path


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    path = physical(path)
    require(path.stat().st_size <= 128 * 1024 * 1024, 'JSON bound')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'duplicate JSON key: ' + key)
            result[key] = value
        return result
    def constant(value):
        raise ValueError('qualification: nonfinite JSON: ' + value)
    return json.loads(path.read_text(), object_pairs_hook=unique, parse_constant=constant)


def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def module(name, path):
    path = physical(path)
    physical(Path(importlib.util.cache_from_source(str(path))), missing=True)
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError('qualification: module unavailable')
    value = importlib.util.module_from_spec(spec)
    sys.modules[name] = value
    spec.loader.exec_module(value)
    return value


def processes():
    return module('qualification_process', PROJECT / 'native/process_utils.py')


def command(argv, directory, name, timeout=300, env=None, reject=None):
    write(directory / (name + '.command.json'), argv)
    result = processes().run(argv, cwd=directory, capture_output=True, text=True, timeout=timeout, env=env)
    (directory / (name + '.log')).write_text(result.stdout + result.stderr)
    require(result.returncode == 0 if reject is None else result.returncode != 0 and reject in result.stdout + result.stderr, 'command failed/wrong rejection: ' + str(directory / (name + '.log')))
    return result.stdout


def relative(name):
    require(isinstance(name, str), 'unsafe bundle relative path')
    path = PurePosixPath(name)
    require('\\' not in name and ':' not in name and not path.is_absolute() and '..' not in path.parts and str(path) == name and name not in ('', '.'), 'unsafe bundle relative path')
    require(re.fullmatch(r'[A-Za-z0-9._ /-]+', name) is not None, 'nonportable bundle path')
    reserved = {'CON', 'PRN', 'AUX', 'NUL'} | {prefix + str(n) for prefix in ('COM', 'LPT') for n in range(1, 10)}
    require(all(part.rstrip(' .') == part and part.split('.')[0].upper() not in reserved for part in path.parts), 'reserved/aliased bundle path')
    return path


def bootstrap_bundle(bundle, anchor):
    """Verify all anchored bytes using this stdlib-only entrypoint before helper imports."""
    bundle = tree(bundle)
    require(re.fullmatch('[0-9a-f]{64}', anchor) is not None, 'independent bundle trust anchor mismatch')
    manifest_path = physical(bundle / 'bundle.json')
    blob = manifest_path.read_bytes()
    require(len(blob) <= 128 * 1024 * 1024, 'bundle manifest JSON bound')
    require(hashlib.sha256(blob).hexdigest() == anchor, 'independent bundle trust anchor mismatch')

    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'duplicate bundle JSON key: ' + key)
            result[key] = value
        return result

    def constant(value):
        raise ValueError('qualification: nonfinite bundle JSON: ' + value)

    parsed = json.loads(blob, object_pairs_hook=unique, parse_constant=constant)
    require(type(parsed) is dict, 'bundle manifest root')
    record = cast(dict[str, Any], parsed)
    require(set(record) == {'schema', 'version', 'files', 'targets', 'sourceProfile', 'toolchain', 'managedPins', 'productionExports', 'fixtureExports', 'trust'}, 'bundle schema keys')
    require(record.get('schema') == 'purr.full-bundle' and type(record.get('version')) is int and record.get('version') == 1, 'bundle schema/version')
    files_value = record.get('files')
    require(type(files_value) is dict and 0 < len(files_value) < 20000, 'bundle file inventory')
    files = cast(dict[str, Any], files_value)
    names = list(files)
    require(all(isinstance(name, str) for name in names), 'bundle file path schema')
    names = cast(list[str], names)
    require(len(files) == len({name.casefold() for name in names}), 'case-aliased bundle paths')
    total_bytes = len(blob)
    for name in names:
        pin_value = files[name]
        require(type(pin_value) is dict, 'bundle file schema: ' + name)
        pin = cast(dict[str, Any], pin_value)
        relative(name)
        require(set(pin) == {'sha256', 'bytes', 'executable'}, 'bundle file schema: ' + name)
        file_size = pin.get('bytes')
        file_hash = pin.get('sha256')
        executable = pin.get('executable')
        require(type(file_size) is int and 0 <= file_size <= 128 * 1024 * 1024, 'bundle file size schema: ' + name)
        require(type(executable) is bool and isinstance(file_hash, str) and re.fullmatch('[0-9a-f]{64}', file_hash) is not None, 'bundle file pin schema: ' + name)
        require(not name.endswith('.dll') or name.startswith('native/') or '/runtimes/' in name, 'private managed DLL in bundle')
        require(not any(str(parent) in files for parent in PurePosixPath(name).parents), 'bundle file/directory conflict')
        total_bytes += cast(int, file_size)
    require(total_bytes <= 1024 * 1024 * 1024, 'bundle expanded-size bound')

    observed_files = {path.relative_to(bundle).as_posix() for path in bundle.rglob('*') if path.is_file()}
    require(observed_files == set(names) | {'bundle.json'}, 'bundle file omission/extra')
    expected_dirs = {str(parent) for name in names for parent in PurePosixPath(name).parents if str(parent) != '.'}
    observed_dirs = {path.relative_to(bundle).as_posix() for path in bundle.rglob('*') if path.is_dir()}
    require(observed_dirs == expected_dirs, 'bundle directory membership drift')
    for name in names:
        pin = cast(dict[str, Any], files[name])
        path = physical(bundle / name)
        require(path.stat().st_size == pin['bytes'], 'bundle file size: ' + name)
        require(digest(path) == pin['sha256'], 'bundle file hash: ' + name)
    return record


def host_rid():
    system, machine = platform.system(), platform.machine().lower()
    for rid, (os_name, arch, _) in RIDS.items():
        if system == os_name and machine in ({'amd64', 'x86_64'} if arch in ('amd64', 'x86_64') else {'arm64', 'aarch64'}):
            return rid
    raise ValueError('qualification: unsupported actual host')


def verify_bundle(bundle, anchor, require_bundled_source=False):
    record = bootstrap_bundle(bundle, anchor)
    bundle = physical(bundle)
    if require_bundled_source:
        expected = physical(bundle / 'source/ImGuiPlayground/native/full/qualification')
        require(physical(HERE) == expected, 'runner source differs from anchored selected bundle')
    manifest_helper = bundle / 'source/ImGuiPlayground/native/full/qualification/extract_bundle.py'
    module('qualification_manifest', manifest_helper).validate_manifest(record)
    require(set(record) == {'schema', 'version', 'files', 'targets', 'sourceProfile', 'toolchain', 'managedPins', 'productionExports', 'fixtureExports', 'trust'}, 'bundle schema keys')
    require(record['schema'] == 'purr.full-bundle' and type(record['version']) is int and record['version'] == 1 and record['targets'] == list(RIDS), 'bundle schema/version/targets')
    require(type(record['productionExports']) is int and record['productionExports'] == 1175 and type(record['fixtureExports']) is int and record['fixtureExports'] == 1546, 'bundle surface')
    require(record['sourceProfile'] == {'sourceCommit': '031a18c417158427217bc5890e0ec0cb7e7b4b63', 'configSha256': '18e6ca90823b36a891eca1c182eba6deb8a35d64431d03db3b91477a1e5049f3', 'patchManifestSha256': '060599ff6f13e95c5df83610b26c336c3e2ba44e46cde67f9806f24427eaf36d'}, 'bundle source profile')
    require(len(record['files']) == len({n.casefold() for n in record['files']}), 'case-aliased bundle paths')
    observed = {p.relative_to(bundle).as_posix() for p in bundle.rglob('*') if p.is_file()}
    require(observed == set(record['files']) | {'bundle.json'}, 'bundle file omission/extra')
    expected_dirs = {str(parent) for name in record['files'] for parent in PurePosixPath(name).parents if str(parent) != '.'}
    require({p.relative_to(bundle).as_posix() for p in bundle.rglob('*') if p.is_dir()} == expected_dirs, 'bundle directory membership drift')
    for name, pin in record['files'].items():
        relative(name)
        path = bundle / name
        require(set(pin) == {'sha256', 'bytes', 'executable'} and type(pin['bytes']) is int and type(pin['executable']) is bool, 'bundle file schema')
        require(path.stat().st_size == pin['bytes'] and digest(path) == pin['sha256'], 'bundle file hash: ' + name)
    return record


def binary_gate(bundle, rid, kind, staged=None):
    verify = module('qualification_verify', HERE / 'verify.py')
    full = bundle / 'source/ImGuiPlayground/native/full'
    inspector = module('qualification_binary', full / 'manual/binary_symbols.py')
    rows = read(bundle / 'evidence/exports.json')
    verify.validate_exports(rows, verify.export_inventory(bundle, read))
    path = bundle / 'native' / rid / kind / RIDS[rid][2]
    if staged is not None:
        require(digest(staged) == digest(path), 'selected/staged native artifact mismatch')
        path = staged
    data = path.read_bytes()
    names = verify.exported_names(data, rid)
    expected = sorted(r['name'] for r in rows if kind == 'fixture' or r['surface'] == 'production')
    require(names == expected, 'actual binary missing/extra/fixture leakage')
    inspector.inspect(data, rid, names)
    return names


def llvm_table(llvm_text, symbol, width):
    lines = [line for line in llvm_text.splitlines() if line.startswith('@') and symbol in line.split(' = ')[0] and ' = ' in line]
    require(len(lines) == 1, 'target LLVM table absent/ambiguous: ' + symbol)
    declaration = lines[0].split(' = ', 1)[1]
    declared = re.fullmatch(r'[a-z_ ]*constant \[(\d+) x (%struct\.LayoutEntry|\[\d+ x i64\]|i64)\] \[(.*)\], align \d+(?:, !dbg !\d+)?', declaration)
    if declared is None:
        raise ValueError('qualification: target LLVM table declaration/unsupported syntax')
    count, element, body = int(declared.group(1)), declared.group(2), declared.group(3)
    require(0 < count < 65536, 'target LLVM table count bound')
    integer = r'i64 -?\d+'
    values = integer + (r', ' + integer) * (width - 1)
    if element == 'i64':
        require(count == width and re.fullmatch(values, body) is not None, 'target LLVM table scalar shape')
        return [[int(v) for v in re.findall(r'i64 (-?\d+)', body)]]
    if element == '%struct.LayoutEntry':
        require(width == 8, 'target LLVM table entry width')
        row = r'%struct\.LayoutEntry \{ ' + values + r' \}'
    else:
        require(element == '[' + str(width) + ' x i64]', 'target LLVM table nested width')
        row = re.escape(element) + r' (?:zeroinitializer|\[' + values + r'\])'
    matches = list(re.finditer(row, body))
    require(len(matches) == count and ', '.join(match.group(0) for match in matches) == body, 'target LLVM table row coverage/unsupported initializer')
    return [[0] * width if match.group(0).endswith('zeroinitializer') else [int(v) for v in re.findall(r'i64 (-?\d+)', match.group(0))] for match in matches]


def build(args):
    # The maintained producer is trusted code, but all its accepted identity checks
    # and current source freeze are independently re-established here.
    combined = module('qualification_combined', PROJECT / 'native/full/combined/run.py')
    consumer = module('qualification_combined_consumer', PROJECT / 'native/full/combined/managed.py')
    manifest = consumer.verified_build(args.combined, combined)
    inputs = {k: Path(v) for k, v in manifest['inputs'].items()}
    output = args.output
    output.mkdir(parents=True)
    compiler = shutil.which(read(inputs['layout'] / 'osx-arm64/static.command.json')[0])
    require(compiler is not None, 'actual producer compiler unavailable')
    compiler_version = command([compiler, 'version'], output, 'zig-version').strip()
    require(compiler_version == '0.17.0', 'actual producer compiler version')
    toolchain = {'zigVersion': compiler_version, 'compilerSha256': digest(compiler), 'targetTriples': {'osx-arm64': 'aarch64-macos.11.0', 'linux-x64': 'x86_64-linux-gnu.2.17', 'win-x64': 'x86_64-windows-gnu'}}
    bundle = output / 'bundle'
    bundle.mkdir()
    source = bundle / 'source'
    for path in source_files():
        dest = source / path.relative_to(ROOT)
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, dest)
    evidence = bundle / 'evidence'
    evidence.mkdir()
    copy = {
        'managed-contract.json': PROJECT / '.tmp/native/binding-contract.json',
        'selected-mapping.json': inputs['selected'] / 'selected-mapping.json',
        'declarations.json': inputs['selected'] / 'ast-model/declarations.json',
        'wrappers.cpp': inputs['selected'] / 'generated/wrappers.cpp',
        'coverage.json': inputs['selected'] / 'generated/coverage.json',
        'source-record.json': inputs['selected'] / 'source-record.json',
        'interface.json': inputs['layout'] / 'interface.json',
        'matrix.json': inputs['enums'] / 'matrix.json',
        'harvest.json': inputs['enums'] / 'harvest.json',
        'census.json': inputs['enums'] / 'census.json',
        'accessors.json': inputs['accessors'] / 'manifest.json',
        'exports.json': args.combined / 'exports.json',
        'combined-build.json': args.combined / 'manifest.json',
    }
    for name, path in copy.items():
        shutil.copyfile(path, evidence / name)
    comparison = read(inputs['layout'] / 'osx-arm64/comparison.json')
    write(evidence / 'conflicts.json', comparison['knownConflicts'])
    reverse = read(evidence / 'census.json')
    write(evidence / 'raw-fields.json', [{k: v for k, v in row.items() if k not in ('associatedStorage', 'disposition')} for row in reverse['fields']])
    for rid in RIDS:
        destination = bundle / 'native' / rid
        destination.mkdir(parents=True)
        for kind in ('production', 'fixture'):
            (destination / kind).mkdir()
            shutil.copyfile(args.combined / rid / kind / RIDS[rid][2], destination / kind / RIDS[rid][2])
            for name in ('ownership.json', 'symbol-sections.json', 'dependencies.log', 'architecture.log', 'link.command.json'):
                shutil.copyfile(args.combined / rid / kind / name, destination / kind / name)
        suffix = '.exe' if rid == 'win-x64' else ''
        shutil.copyfile(inputs['layout'] / rid / ('layout-probe' + suffix), destination / ('layout-probe' + suffix))
        shutil.copyfile(inputs['enums'] / rid / ('enum-test' + suffix), destination / ('enum-probe' + suffix))
        for probe in ('layout-probe', 'enum-probe'):
            (destination / (probe + suffix)).chmod(0o755)
        # Actual compiler constants close every row, including member align/array
        # stride, without pretending cross-compiled executables ran on this host.
        compiler_command = read(inputs['layout'] / rid / 'static.command.json')
        llvm = output / (rid + '-layout.ll')
        compiler_command[compiler_command.index('-o') + 1] = str(llvm)
        compiler_command.remove('-c')
        compiler_command += ['-S', '-emit-llvm']
        command(compiler_command, output, rid + '-static-tables')
        text = llvm.read_text()
        native: dict[str, Any] = {'entries': llvm_table(text, 'playground_layout_entries', 8), 'bitfieldDeclaredTypes': llvm_table(text, 'LayoutBitfieldDeclaredTypes', 3), 'enumValues': llvm_table(text, 'LayoutEnumValues', 3)}
        # Configured STB's concrete shape is also derived on each target, not copied
        # from a host report. A compiler-only appended constant has no image export.
        probe_text = (inputs['layout'] / 'probe.cpp').read_text()
        shape_source = output / (rid + '-shape.cpp')
        shape_source.write_text(probe_text + '\nextern "C" const long long purr_qualification_stb_shape[] = {sizeof(stbrp_context), alignof(stbrp_context), sizeof(stbrp_context_opaque)};\n')
        shape_command = list(compiler_command)
        shape_command[shape_command.index(str(inputs['layout'] / 'probe.cpp'))] = str(shape_source)
        shape_command += ['-I', str(inputs['layout'])]
        shape_llvm = output / (rid + '-shape.ll')
        shape_command[shape_command.index('-o') + 1] = str(shape_llvm)
        command(shape_command, output, rid + '-shape')
        text = shape_llvm.read_text()
        shape = llvm_table(text, 'purr_qualification_stb_shape', 3)[0]
        native['rectPackContext'] = {key: shape[i] for i, key in enumerate(('sizeBytes', 'alignment', 'capacityBytes'))}
        write(destination / 'static-layout.json', native)
        shutil.copyfile(llvm, destination / 'layout-tables.ll')
        shutil.copyfile(shape_llvm, destination / 'shape-table.ll')
        for name in (rid + '-static-tables.command.json', rid + '-shape.command.json'):
            shutil.copyfile(output / name, destination / name)
        binary_gate(bundle, rid, 'production')
        binary_gate(bundle, rid, 'fixture')
    files = {}
    for path in sorted(bundle.rglob('*')):
        if path.is_file():
            name = path.relative_to(bundle).as_posix()
            require(not (path.suffix == '.dll' and '/runtimes/' not in name and not name.startswith('native/')), 'private managed DLL in bundle')
            files[name] = {'sha256': digest(path), 'bytes': path.stat().st_size, 'executable': path.name in ('layout-probe', 'enum-probe', 'layout-probe.exe', 'enum-probe.exe')}
    record = {'schema': 'purr.full-bundle', 'version': 1, 'files': files, 'targets': list(RIDS), 'toolchain': toolchain, 'sourceProfile': {'sourceCommit': manifest['sourceCommit'], 'configSha256': manifest['configSha256'], 'patchManifestSha256': '060599ff6f13e95c5df83610b26c336c3e2ba44e46cde67f9806f24427eaf36d'}, 'managedPins': read(evidence / 'selected-mapping.json')['selectedAssemblies'], 'productionExports': 1175, 'fixtureExports': 1546, 'trust': 'independently retain this manifest SHA; integrity relative to trusted maintainer builder, not authenticity against a malicious builder'}
    write(bundle / 'bundle.json', record)
    anchor = digest(bundle / 'bundle.json')
    verify_bundle(bundle, anchor)
    archive = output / 'qualification.zip'
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as z:
        for path in sorted(bundle.rglob('*')):
            if path.is_file():
                name = path.relative_to(bundle).as_posix()
                info = zipfile.ZipInfo(name)
                info.create_system = 3
                executable = files.get(name, {}).get('executable', False)
                info.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
                z.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED)
    shutil.copyfile(HERE / 'extract_bundle.py', output / 'extract_bundle.py')
    write(output / 'build-result.json', {'schema': 'purr.full-bundle-build.v1', 'extractorSha256': digest(output / 'extract_bundle.py'), 'toolchain': toolchain, 'bundleManifestSha256': anchor, 'archiveSha256': digest(archive), 'combinedManifestSha256': digest(args.combined / 'manifest.json'), 'privateManagedDllsIncluded': False, 'foreignExecution': False})


def extract(args):
    extractor = module('qualification_extractor', HERE / 'extract_bundle.py')
    extractor.extract(args.archive, args.anchor, args.output)
    verify_bundle(args.output, args.anchor)


def guard(args):
    record = verify_bundle(args.bundle, args.anchor)
    rid = host_rid()
    stage = tree(args.stage)
    binary_gate(args.bundle, rid, args.kind, stage / RIDS[rid][2])
    for name, pin in record['managedPins'].items():
        require(digest(args.selection / (name + '.dll')) == digest(stage / (name + '.dll')) == pin['sha256'], 'selected/staged contributor mismatch')
    args.output.mkdir(parents=True)
    suffix = '.exe' if rid == 'win-x64' else ''
    for label, probe in [('native', 'layout-probe'), ('enum', 'enum-probe')]:
        text = command([str(args.bundle / 'native' / rid / (probe + suffix))], args.output, label + '-probe', timeout=60)
        (args.output / (label + '.json')).write_text(text)
    verifier = module('qualification_verify', HERE / 'verify.py')
    report = verifier.validate(args.bundle, read(args.contract), read(args.output / 'native.json'), read(args.output / 'enum.json'), rid, read)
    report.update(bundleManifestSha256=args.anchor, actualContractSha256=digest(args.contract), nativeArtifactSha256=digest(stage / RIDS[rid][2]), artifactKind=args.kind, status='passed')
    write(args.output / 'guard.json', report)


def run_target(args):
    record = verify_bundle(args.bundle, args.anchor)
    rid = host_rid()
    for name, pin in record['managedPins'].items():
        require(digest(args.selection / (name + '.dll')) == pin['sha256'], 'explicit selected contributor mismatch')
    args.output.mkdir(parents=True)
    source = args.bundle / 'source'
    checks = source / 'ImGuiPlayground.Checks/ImGuiPlayground.Checks.csproj'
    env = dict(os.environ, KSAFolder=str(args.selection), KSA_DLL_DIR=str(args.selection), PYTHONDONTWRITEBYTECODE='1')
    command(['dotnet', 'build', str(checks), '--artifacts-path', str(args.output / 'ordinary-artifacts'), '-c', 'Release', '--nologo', '-v:minimal', '-p:KSAFolder=' + str(args.selection), '-p:TreatWarningsAsErrors=true'], args.output, 'ordinary-build', env=env)
    built = args.output / 'ordinary-artifacts/bin/ImGuiPlayground.Checks/release'
    tree(built)
    processes_run = []
    for mode in ('accessor', 'profile', 'manual', 'composition', 'renderer', 'capture'):
        kind = 'production' if mode in ('renderer', 'capture') else 'fixture'
        stage = args.output / ('stage-' + mode)
        shutil.copytree(built, stage)
        native_source = args.bundle / 'native' / rid / kind / RIDS[rid][2]
        shutil.copyfile(native_source, stage / RIDS[rid][2])
        receipt = {'schema': 'purr.full-stage.v1', 'rid': rid, 'kind': kind, 'bundleManifestSha256': args.anchor, 'builtDirectory': str(built), 'stageDirectory': str(stage), 'selectedDirectory': str(args.selection), 'managedHashes': {p.name: digest(p) for p in built.glob('*.dll')}, 'nativeSource': str(native_source), 'nativeSha256': digest(native_source)}
        receipt_path = args.output / (mode + '-stage.json')
        write(receipt_path, receipt)
        process_output = args.output / ('process-' + mode)
        command(['dotnet', str(stage / 'ImGuiPlayground.Checks.dll'), '--full-qualification', mode, str(args.bundle), args.anchor, sys.executable, str(receipt_path), str(process_output)], args.output, mode, timeout=240, env=env)
        report = read(process_output / 'report.json')
        require(report['status'] == 'passed' and report['nativeSha256'] == receipt['nativeSha256'] and report['mode'] == mode, 'child qualification report mismatch')
        processes_run.append({'mode': mode, 'reportSha256': digest(process_output / 'report.json'), 'nativeSha256': report['nativeSha256'], 'pid': report['processId']})
    verify_bundle(args.bundle, args.anchor)
    write(args.output / 'results.json', {'schema': 'purr.full-target-run.v1', 'rid': rid, 'bundleManifestSha256': args.anchor, 'processes': processes_run, 'status': 'passed', 'foreignExecutionClaimed': False, 'allEndpointSemanticsClaimed': False})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['build', 'extract', 'run', 'guard', 'test', 'ordinary'])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--combined', type=Path)
    parser.add_argument('--bundle', type=Path)
    parser.add_argument('--anchor')
    parser.add_argument('--archive', type=Path)
    parser.add_argument('--selection', type=Path)
    parser.add_argument('--stage', type=Path)
    parser.add_argument('--contract', type=Path)
    parser.add_argument('--run-output', type=Path)
    parser.add_argument('--kind', choices=['production', 'fixture'])
    args = parser.parse_args()
    source_files()  # Guard all local source/modules/cache paths before project imports.
    protected = []
    if args.mode in ('run', 'ordinary', 'test'):
        selected = os.environ.get('KSAFolder') or args.selection or os.environ.get('KSA_DLL_DIR')  # noqa: SIM112
        if selected is None:
            raise ValueError('qualification: explicit KSAFolder/KSA_DLL_DIR or --selection required')
        args.selection = Path(selected)
    for name in ('combined', 'bundle', 'selection', 'stage', 'run_output'):
        if getattr(args, name) is not None:
            value = tree(getattr(args, name))
            setattr(args, name, value)
            protected.append(value)
    for name in ('archive', 'contract'):
        if getattr(args, name) is not None:
            value = physical(getattr(args, name))
            setattr(args, name, value)
            protected.append(value if name == 'archive' else value.parent)
    args.output = fresh(args.output, protected + [PROJECT / 'native', ROOT / 'ImGuiPlayground.Checks'])
    if args.mode in ('run', 'guard', 'test', 'ordinary'):
        if args.mode == 'run':
            require(args.bundle is not None and args.anchor is not None, 'run requires --bundle and independent --anchor')
        elif args.mode == 'guard':
            require(all(v is not None for v in (args.bundle, args.anchor, args.selection, args.stage, args.contract, args.kind)), 'guard inputs missing')
        elif args.mode == 'test':
            require(args.bundle is not None and args.anchor is not None and args.run_output is not None, 'test requires --bundle, --anchor and actual target --run-output')
        else:
            require(args.bundle is not None and args.anchor is not None and args.selection is not None, 'ordinary requires extracted --bundle, --anchor and explicit selection')
        verify_bundle(args.bundle, args.anchor, require_bundled_source=True)
    if args.mode == 'build':
        require(args.combined is not None and args.output.is_relative_to(SCRATCH), 'build requires --combined and owned qualification output')
        build(args)
    elif args.mode == 'extract':
        require(args.archive is not None and args.anchor is not None, 'extract requires --archive and independent --anchor')
        extract(args)
    elif args.mode == 'guard':
        guard(args)
    elif args.mode == 'run':
        run_target(args)
    else:
        module('qualification_' + args.mode, HERE / (args.mode + '.py')).run(args, sys.modules[__name__])


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, IndexError, struct.error) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
