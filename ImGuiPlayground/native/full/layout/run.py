#!/usr/bin/env python3
"""Explicit opt-in layout source/probe builder. No .NET dependency or runtime promotion."""
from __future__ import annotations

import argparse
import json
import platform
import re
import shutil
import sys
import tarfile
from pathlib import Path

import generate as storage  # pyright: ignore[reportMissingImports]
from patch_source import HERE, LayoutError, digest, prepare_source, reject_symlinks

NATIVE = HERE.parents[1]
sys.path.insert(0, str(NATIVE))
import process_utils  # noqa: E402  # pyright: ignore[reportMissingImports]


def logged(command: list[str], path: Path, timeout: float = 240) -> None:
    with path.open('w') as log:
        log.write(json.dumps(command) + '\n')
        log.flush()
        result = process_utils.run(command, stdout=log, stderr=log, timeout=timeout)
    if result.returncode:
        raise LayoutError(f'command failed ({result.returncode}); see {path}')


def build(contract_path: Path, archive: Path, output: Path, targets: list[str], zig: str, *, patched: bool) -> None:
    reject_symlinks(output.absolute())
    if output.exists():
        raise LayoutError('new output directory required')
    contract, mapping, bits = storage.load_inputs(contract_path)
    lock = json.loads((NATIVE / 'source.lock.json').read_bytes())
    config = NATIVE / lock['configuration']['header']
    if digest(config.read_bytes()) != lock['configuration']['sha256']:
        raise LayoutError('configuration hash mismatch')
    compiler = shutil.which(zig)
    if not compiler:
        raise LayoutError('Zig not found')
    version = process_utils.run([compiler, 'version'], capture_output=True, text=True, timeout=15, check=True).stdout.strip()
    if version != lock['toolchain']['zigVersion']:
        raise LayoutError('wrong Zig version')
    environment = process_utils.run([compiler, 'env'], capture_output=True, text=True, timeout=15, check=True).stdout
    lib_match = re.search(r'\.lib_dir = "([^"]+)"', environment)
    if not lib_match:
        raise LayoutError('cannot locate pinned Zig compiler headers')
    cpp_headers = Path(lib_match[1]) / 'libcxx' / 'include'
    output.mkdir(parents=True)
    source = output / 'source'
    record = prepare_source(archive, source, patched=patched)
    (output / 'source-record.json').write_text(json.dumps(record, indent=2) + '\n')
    with tarfile.open(archive, 'r:gz') as tar:
        headers = {}
        for name in ('imgui.h', 'imgui_internal.h'):
            stream = tar.extractfile('imgui-' + lock['upstream']['sourceCommit'] + '/' + name)
            if stream is None:
                raise LayoutError('original header missing')
            with stream:
                headers[name] = stream.read().decode('utf-8-sig')
    storage.verify_original_bitfields(headers, bits)
    interface = storage.generate(contract, mapping, output, patched=patched)
    for rid in targets:
        target = lock['targets'][rid]['zigTarget']
        folder = output / rid
        folder.mkdir()
        executable = folder / ('layout-probe.exe' if rid == 'win-x64' else 'layout-probe')
        command = [compiler, 'c++', '-std=c++11', '-O2', '-DNDEBUG', '-fno-exceptions', '-fno-rtti',
                   '-fno-threadsafe-statics', '-nostdlib++', '-Wno-invalid-constexpr',
                   '-Wno-invalid-offsetof', '-target', target, '-I', str(source.resolve()), '-I', str(HERE),
                   '-include', str(config), f'-DLAYOUT_PATCHED={int(patched)}',
                   '-isystem', str(cpp_headers), '-D_LIBCPP_HARDENING_MODE=_LIBCPP_HARDENING_MODE_NONE',
                   '-D_LIBCPP_ASSERTION_SEMANTIC_DEFAULT=_LIBCPP_ASSERTION_SEMANTIC_IGNORE']
        # offsetof for derived types is retained only as compiler evidence; host
        # runtime offsets use addresses on a real constructed ImGuiViewportP.
        if rid == 'osx-arm64':
            sdk = process_utils.run(['xcrun', '--sdk', 'macosx', '--show-sdk-path'], capture_output=True, text=True, timeout=15, check=True).stdout.strip()
            command += ['-isysroot', sdk, '-mmacosx-version-min=11.0']
        sources = [output / 'probe.cpp'] + [source / name for name in lock['build']['sourceFiles'] if name not in ('imgui.cpp', 'src/PlaygroundBrutalAdapter.cpp')]
        compile_command = command + [str(p.resolve()) for p in sources] + ['-o', str(executable.resolve())]
        verify_source_tree(source, record['sourceFiles'])
        logged(compile_command, folder / 'build.log')
        component_hashes = {str(p.relative_to(HERE)): digest(p.read_bytes()) for p in sorted(HERE.rglob('*'))
                            if p.is_file() and '__pycache__' not in p.parts and p.suffix in ('.py', '.h', '.inc', '.json')}
        result = {'rid': rid, 'target': target, 'zigVersion': version, 'command': compile_command,
                  'componentSourceSha256': component_hashes, 'compilerSha256': digest(Path(compiler).read_bytes()),
                  'artifactSha256': digest(executable.read_bytes()), 'artifactBytes': executable.stat().st_size,
                  'sourceRecordSha256': digest((output / 'source-record.json').read_bytes()),
                  'configSha256': digest(config.read_bytes()), 'interface': interface,
                  'executed': False, 'staticOrdinaryParity': False, 'patched': patched}
        (folder / 'build-record.json').write_text(json.dumps(result, indent=2) + '\n')
        if rid == 'osx-arm64' and platform.system() == 'Darwin' and platform.machine() == 'arm64':
            with (folder / 'native.json').open('w') as out, (folder / 'runtime.log').open('w') as err:
                run = process_utils.run([str(executable.resolve())], stdout=out, stderr=err, timeout=30)
            if run.returncode:
                raise LayoutError('native execution failed')
            if (folder / 'native.json').stat().st_size > 64 * 1024 * 1024:
                raise LayoutError('native payload exceeds explicit 64 MiB validation bound')
            native = json.loads((folder / 'native.json').read_bytes())
            report = storage.compare(contract, mapping, interface, native)
            (folder / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n')
            result['executed'] = True
            result['unexpectedMismatchCount'] = len(report['unexpectedMismatches'])
            result['knownConflictCount'] = len(report['knownConflicts'])
            (folder / 'build-record.json').write_text(json.dumps(result, indent=2) + '\n')
            if patched and report['unexpectedMismatches']:
                raise LayoutError('unresolved ordinary storage mismatch; see comparison.json')
        if patched:
            # Cross-target static proof is explicitly not execution. This uses the
            # target compiler's sizeof/offset/width/count, not native JSON literals.
            logged(command + ['-DLAYOUT_ENFORCE_MANAGED=1', '-Xclang', '-fdump-record-layouts', '-c', str((output / 'probe.cpp').resolve()), '-o', str((folder / 'checked.o').resolve())], folder / 'static-check.log')
            result['staticOrdinaryParity'] = True
            result['checkedObjectSha256'] = digest((folder / 'checked.o').read_bytes())
            result['compilerRecordLayoutsSha256'] = digest((folder / 'static-check.log').read_bytes())
        result['executionStatus'] = 'macOS native process executed' if result['executed'] else 'pending; cross compilation is not execution'
        (folder / 'build-record.json').write_text(json.dumps(result, indent=2) + '\n')
    # Detect any accidental compiler/source mutation, including pristine-vs-patched
    # header mixing. Every compiled source includes only the one verified tree.
    verify_source_tree(source, record['sourceFiles'])


def verify_source_tree(source: Path, expected: dict) -> None:
    reject_symlinks(source)
    for path in source.rglob('*'):
        reject_symlinks(path)
    actual = {str(p.relative_to(source)): digest(p.read_bytes()) for p in sorted(source.rglob('*')) if p.is_file()}
    if actual != expected:
        raise LayoutError('scratch source/header hash or membership changed')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--targets', nargs='+', choices=['osx-arm64', 'linux-x64', 'win-x64'], default=['osx-arm64'])
    parser.add_argument('--zig', default='zig')
    parser.add_argument('--baseline', action='store_true', help='Independent pristine observations; mismatches retained, not a passing parity gate')
    args = parser.parse_args()
    build(args.contract, args.archive, args.out, args.targets, args.zig, patched=not args.baseline)


if __name__ == '__main__':
    try:
        main()
    except (LayoutError, OSError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
