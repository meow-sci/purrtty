#!/usr/bin/env python3
"""Explicit scratch-only cross-compile/link and symbol checks. Never a .NET dependency."""
from __future__ import annotations

import argparse
import importlib
import re
import subprocess
import sys
import tarfile
from pathlib import Path

sys.dont_write_bytecode = True
generate = importlib.import_module('generate')
output_paths = generate.output_paths
# These maintainer scripts execute directly from their component directory.
declarations = importlib.import_module('declarations')
HERE = Path(__file__).resolve().parent
NATIVE = HERE.parents[1]
SCRATCH = NATIVE.parent / '.tmp' / 'native'
process_utils = declarations.process_utils
builder = declarations.builder


def logged(command, log, timeout=240):
    output_paths.prepare([log, log.with_suffix('.command.json')])
    log.with_suffix('.command.json').write_bytes(generate.encoded(command))
    with log.open('w') as stream:
        process_utils.run(command, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout, check=True)


def pristine(lock, source):
    archive = SCRATCH / ('imgui-'+lock['upstream']['sourceCommit']+'.tar.gz')
    builder.verify_archive(archive, lock['upstream']['archiveSha256'])
    hashes = {}
    with tarfile.open(archive, 'r:gz') as bundle:
        for member in bundle.getmembers():
            path = Path(member.name)
            if len(path.parts) == 2 and member.isfile() and path.suffix in ('.h', '.cpp'):
                stream = bundle.extractfile(member)
                if stream is None:
                    raise generate.ContractError('missing archive member stream: '+member.name)
                expected = stream.read()
                actual = (source/path.name).read_bytes()
                generate.require(expected == actual, 'non-pristine source: '+path.name)
                hashes[path.name] = generate.digest(actual)
    return hashes


def export_names(artifact, rid, directory):
    output_paths.prepare([directory/'symbols.log'])
    if rid == 'win-x64':
        command = ['objdump', '-p', str(artifact)]
    elif rid == 'osx-arm64':
        command = ['nm', '-gU', str(artifact)]
    else:
        command = ['nm', '--dynamic', '--defined-only', str(artifact)]
    run = process_utils.run(command, capture_output=True, text=True, timeout=30, check=True)
    (directory/'symbols.log').write_text(run.stdout+run.stderr)
    if rid == 'win-x64':
        names = []
        in_exports = False
        for line in run.stdout.splitlines():
            if 'Export Table:' in line:
                in_exports = True
            elif in_exports:
                parts = line.split()
                if len(parts) == 3 and parts[0].isdigit() and parts[1].startswith('0x'):
                    names.append(parts[2])
        return names
    return [line.split()[-1][1:] if rid == 'osx-arm64' else line.split()[-1]
            for line in run.stdout.splitlines() if len(line.split()) == 3]


def output_files(output, rids, runtime):
    files = declarations.output_files(output/'ast-model')
    files += [output/'results.json', output/'generated/wrappers.cpp', output/'generated/coverage.json']
    for rid in rids:
        names = ['build.command.json', 'build.log', 'c-header-test.command.json', 'c-header-test.log',
                 'abi_c_test.o', 'symbols.log', 'signature-mutation.cpp', 'must-not-compile.o',
                 'negative-compile.command.json', 'negative-compile.log', 'result.json',
                 'bool-view-proof.ll', 'bool-view-proof.command.json', 'bool-view-proof.log']
        names += {'osx-arm64':['libimgui.dylib'], 'linux-x64':['libimgui.so'],
                  'win-x64':['imgui.dll', 'imgui.lib', 'imgui.pdb']}[rid]
        if runtime and rid == 'osx-arm64':
            names += ['api_native_test', 'test-build.command.json', 'test-build.log', 'test-run.command.json',
                      'test-run.log', 'library-test.command.json', 'library-test.log']
        files += [output/rid/name for name in names]
    return files


def run_checks(rids, zig='zig', runtime=False):
    output = output_paths.root()
    files = output_files(output, rids, runtime)
    output_paths.preflight(files)
    lock = builder.read_json(NATIVE/'source.lock.json')
    builder.validate_lock(lock)
    config = builder.verify_config_pin(lock)
    source = SCRATCH/'source'/lock['upstream']['sourceCommit']
    hashes = pristine(lock, source)
    output_paths.prepare(files)
    model = declarations.produce(source, output/'ast-model', zig)
    contract_path = SCRATCH/'binding-contract.json'
    contract = generate.load(contract_path)
    mapping = generate.load(HERE/'mapping.json')
    wrappers, coverage = generate.generate(contract, model, mapping)
    generated = output/'generated'
    (generated/'wrappers.cpp').write_bytes(wrappers)
    coverage.update(inputContractSha256=generate.digest(contract_path.read_bytes()),
                    generatorSha256=generate.digest((HERE/'generate.py').read_bytes()),
                    mappingSha256=generate.digest((HERE/'mapping.json').read_bytes()),
                    commonHeaderSha256=generate.digest((HERE/'purr_abi.h').read_bytes()),
                    wrapperSha256=generate.digest(wrappers))
    (generated/'coverage.json').write_bytes(generate.encoded(coverage))
    expected = sorted(m['entryPoint'] for m in mapping['mappings'])
    records = []
    for rid in rids:
        target = lock['targets'][rid]
        directory = output/rid
        artifact = directory/target['file']
        command = [zig, 'c++', '-std=c++11', '-O2', '-DNDEBUG', '-fPIC', '-fvisibility=hidden',
                   '-fno-strict-aliasing', '-fno-exceptions', '-fno-rtti', '-fno-threadsafe-statics',
                   '-nostdlib++', '-target', target['zigTarget'], '-I', str(source), '-I', str(HERE),
                   '-include', str(config)]
        if rid == 'osx-arm64':
            command += ['-isysroot', builder.macos_sdk_path(), '-mmacosx-version-min=11.0']
        if rid == 'win-x64':
            command += ['-DPLAYGROUND_IMGUI_WINDOWS=1']
        # All source TUs, not only wrappers, share the representation-contract flag.
        inputs = [str(source/n) for n in lock['build']['sourceFiles'] if n != 'src/PlaygroundBrutalAdapter.cpp']
        inputs += [str(generated/'wrappers.cpp')]
        shared = ['-dynamiclib', '-Wl,-install_name,@rpath/libimgui.dylib'] if rid == 'osx-arm64' else ['-shared']
        if rid == 'linux-x64':
            shared += ['-Wl,--no-undefined', '-Wl,-soname,libimgui.so']
        logged(command+shared+inputs+['-o', str(artifact)], directory/'build.log')
        c_command = [zig, 'cc', '-std=c11', '-fno-strict-aliasing', '-target', target['zigTarget'], '-I', str(HERE)]
        if rid == 'osx-arm64':
            c_command += ['-isysroot', builder.macos_sdk_path()]
        logged(c_command+['-c', str(HERE/'abi_c_test.c'), '-o', str(directory/'abi_c_test.o')], directory/'c-header-test.log')
        # LLVM evidence isolates conversion from upstream behavior: output view
        # must be pointer identity with no load/store/call, on every selected ABI.
        proof = directory/'bool-view-proof.ll'
        logged(command+['-g0', '-S', '-emit-llvm', str(HERE/'bool_view_proof.cpp'), '-o', str(proof)], directory/'bool-view-proof.log')
        proof_text = proof.read_text()
        match = re.search(r'define [^\n]*@purr_bool_output_proof\([^\n]*\)[^{]*\{([^}]+)\}', proof_text)
        if match is None:
            raise generate.ContractError('missing compiler bool output-view proof')
        body = match.group(1).strip()
        generate.require(re.fullmatch(r'ret ptr %\w+', body) is not None, 'output bool view performs an entry access: '+body)
        names = export_names(artifact, rid, directory)
        # No format/bridge stubs and exactly one entry for every owned wrapper.
        actual = sorted(n for n in names if n in expected)
        generate.require(actual == expected, 'missing/duplicate compiled export: '+rid)
        generate.require(not (set(names) & set(generate.RESERVED)), 'reserved format export emitted')
        generate.require(not (set(names) & {d['name'] for d in contract['dynamicExports']}), 'reserved dynamic export emitted')
        # Deliberately break the first selected overload type. Real compiler
        # failure proves the generated static_cast gate is active on each target.
        mutated = wrappers.decode()
        start = mutated.index('    using NativeCall = ')
        end = mutated.index(';', start)
        mutated = mutated[:start] + '    using NativeCall = void (*)()' + mutated[end:]
        negative_source = directory/'signature-mutation.cpp'
        negative_source.write_text(mutated)
        negative_command = command+['-c', str(negative_source), '-o', str(directory/'must-not-compile.o')]
        (directory/'negative-compile.command.json').write_bytes(generate.encoded(negative_command))
        negative = process_utils.run(negative_command, capture_output=True, text=True, timeout=120, check=False)
        (directory/'negative-compile.log').write_text(negative.stdout+negative.stderr)
        generate.require(negative.returncode != 0 and 'static_cast' in negative.stderr,
                         'compiler accepted a deliberately mismatched overload type: '+rid)
        record = {'rid':rid, 'artifactSha256':generate.digest(artifact.read_bytes()),
                  'negativeSignatureCompileRejected':True, 'cHeaderCompiled':True,
                  'boolOutputViewCompilerProof':body, 'boolRepresentationAssertionsCompiled':True,
                  'compiledOwnedExports':len(actual), 'totalVisibleExports':len(names),
                  'sourceHashes':hashes, 'runtimeExecuted':False, 'layoutQualified':False}
        if runtime and rid == 'osx-arm64':
            executable = directory/'api_native_test'
            logged(command+inputs+[str(HERE/'api_native_test.cpp'), str(HERE/'bool_pointer_test.cpp'), '-o', str(executable)], directory/'test-build.log')
            logged([str(executable)], directory/'test-run.log', timeout=30)
            logged([sys.executable, str(HERE/'api_library_test.py'), str(artifact), str(generated/'coverage.json')],
                   directory/'library-test.log', timeout=30)
            record['runtimeExecuted'] = True
            record['runtimeScope'] = 'owned native C++ wrapper fixtures plus actual-library ctypes load/smoke; not managed ABI or exhaustive behavior'
        (directory/'result.json').write_bytes(generate.encoded(record))
        records.append(record)
    (output/'results.json').write_bytes(generate.encoded(records))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', action='append', choices=['osx-arm64', 'linux-x64', 'win-x64'], required=True)
    parser.add_argument('--zig', default='zig')
    parser.add_argument('--runtime', action='store_true', help='execute only owned macOS native fixture')
    args = parser.parse_args()
    try:
        run_checks(args.rid, args.zig, args.runtime)
    except (generate.ContractError, OSError, subprocess.SubprocessError, KeyError, ValueError) as error:
        parser.exit(1, 'full API check failed: '+str(error)+'\n')


if __name__ == '__main__':
    main()
