#!/usr/bin/env python3
"""Explicit component-only maintainer gate. Outputs only to a new scratch tree."""
import argparse
import importlib
import importlib.util
import json
import os
import re
import shlex
import shutil
import stat
import subprocess
import sys
from pathlib import Path
from xml.sax.saxutils import escape

sys.dont_write_bytecode = True
# Only stdlib code runs before the bootstrap helper and entrypoint are checked.
# Keep this small guard in both entrypoints: importing paths.py to validate itself
# would execute a redirected file before the boundary could reject it.
for candidate in (Path(__file__).absolute(), Path(__file__).absolute().with_name('paths.py')):
    for item in reversed((candidate, *candidate.parents)):
        info = item.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0):
            raise ValueError('redirect forbidden before import: ' + str(item))
        if item != candidate and not stat.S_ISDIR(info.st_mode):
            raise ValueError('non-directory import ancestor: ' + str(item))
        if item == candidate and (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1):
            raise ValueError('non-regular/shared import leaf: ' + str(item))
HERE = Path(__file__).absolute().parent
spec = importlib.util.spec_from_file_location('purr_manual_paths', HERE / 'paths.py')
if spec is None or spec.loader is None:
    raise ValueError('manual path bootstrap unavailable')
paths = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = paths
spec.loader.exec_module(paths)
paths.component_inputs()
sys.path.insert(0, str(HERE.parents[3]))
contract = paths.load_module('purr_manual_contract', HERE / 'contract.py')
binary_symbols = paths.load_module('purr_manual_binary_symbols', HERE / 'binary_symbols.py')
binary_negative = paths.load_module('purr_manual_binary_negative', HERE / 'binary_negative.py')

builder = importlib.import_module('ImGuiPlayground.native.build')
process = importlib.import_module('ImGuiPlayground.native.process_utils')
patch_source = importlib.import_module('ImGuiPlayground.native.full.layout.patch_source')
NATIVE = HERE.parents[1]
PROJECT = NATIVE.parent
RIDS = ('osx-arm64', 'linux-x64', 'win-x64')


def encoded(value):
    return json.dumps(value, indent=2, sort_keys=True) + '\n'


def logged(command, directory, name, env, timeout=240, reject=False):
    (directory / (name + '.command.json')).write_text(encoded(command))
    with (directory / (name + '.log')).open('w') as log:
        result = process.run(command, stdout=log, stderr=subprocess.STDOUT, env=env, timeout=timeout)
    if (result.returncode == 0) == reject:
        raise ValueError(('negative unexpectedly passed: ' if reject else 'command failed: ') + str(directory / (name + '.log')))
    return (directory / (name + '.log')).read_text()


def symbols(artifact, rid, directory, env, fixture=False):
    command = ['objdump', '-p', str(artifact)] if rid == 'win-x64' else ['nm', '-gU', str(artifact)] if rid == 'osx-arm64' else ['nm', '--dynamic', '--defined-only', str(artifact)]
    text = logged(command, directory, ('fixture-' if fixture else '') + 'symbols', env)
    if rid == 'win-x64':
        text = text.split('Export Table:')[1]
        names = [p[2] for line in text.splitlines() if len(p := line.split()) == 3 and p[0].isdigit() and p[1].startswith('0x')]
    else:
        names = [p[-1][1:] if rid == 'osx-arm64' else p[-1] for line in text.splitlines() if len(p := line.split()) == 3]
    # Mach-O linker-synthesized image bookkeeping, not owned C ABI exports.
    linker_symbols = ('__dso_handle', '_mh_dylib_header') if rid == 'osx-arm64' else ()
    expected = sorted(contract.IMPORTS + contract.DYNAMIC + (contract.SUPPLEMENTS if fixture else ()) + linker_symbols)
    contract.require(sorted(names) == expected, 'compiled export set: ' + rid + str(names))
    return [name for name in names if name not in linker_symbols]


def managed(output, artifact, selection, env):
    selected = paths.inspect(Path(selection))
    hashes = {
        'Brutal.ImGui': 'b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c',
        'Brutal.Core.Common': '4cbae1473d6a3759345f1eec8e62e6c7da1e19f8d10db8d13c005203ec590de4',
        'Brutal.Core.Numerics': 'a217a6098116a1895e965b3a1d4fdee931e59a7a55f326c7d710b0d7eeccd17a'
    }
    for name, expected in hashes.items():
        p = paths.inspect(selected / (name + '.dll'))
        contract.require(contract.digest(p.read_bytes()) == expected, 'selected ABI contributor: ' + name)
    harness = output / 'harness'
    harness.mkdir()
    references = ''
    for name in (*hashes, 'Brutal.Core.Strings', 'Brutal.Core.Logging'):
        p = paths.inspect(selected / (name + '.dll'))
        contract.require(p.is_file(), 'required managed dependency: ' + name)
        references += f'<Reference Include="{name}"><HintPath>{escape(str(p))}</HintPath><Private>true</Private></Reference>'
    seam = HERE.parents[3] / 'ImGuiPlayground.Checks/FullManualAbiChecks.cs'
    project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AllowUnsafeBlocks>true</AllowUnsafeBlocks><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><Compile Include="{escape(str(seam))}"/><Compile Include="Program.cs"/>{references}<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0"/><PackageReference Include="Microsoft.Extensions.ObjectPool" Version="11.0.0-rc.1.26425.128"/></ItemGroup></Project>'''
    (harness / 'ManualHarness.csproj').write_text(project)
    (harness / 'Program.cs').write_text('''using System.Runtime.InteropServices;
using System.Text.Json;
using Brutal.ImGuiApi;
string path = Path.Combine(AppContext.BaseDirectory, "libimgui.dylib");
nint handle = NativeLibrary.Load(path);
NativeLibrary.SetDllImportResolver(typeof(ImGui).Assembly, (name, assembly, search) => name == "imgui" ? handle : 0);
var report = FullManualAbiChecks.Run(handle);
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
// Resolver and Interop's explicit library references live to process exit.
''')
    build = harness / 'build'
    logged(['dotnet', 'build', str(harness / 'ManualHarness.csproj'), '-o', str(build), '--nologo', '-v:q', '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false'], harness, 'build', env)
    stage = harness / 'stage'
    shutil.copytree(build, stage)
    for name, expected in hashes.items():
        for directory in (build, stage):
            contract.require(contract.digest((directory / (name + '.dll')).read_bytes()) == expected, 'built/staged ABI identity: ' + name)
        contract.require(contract.digest((selected / (name + '.dll')).read_bytes()) == expected, 'selected identity changed during build')
    shutil.copyfile(artifact, stage / 'libimgui.dylib')
    contract.require(contract.digest((stage / 'libimgui.dylib').read_bytes()) == contract.digest(artifact.read_bytes()), 'native staging identity')
    logged(['dotnet', str(stage / 'ManualHarness.dll'), str(harness / 'managed-results.json')], harness, 'run', env, timeout=45)
    report = json.loads((harness / 'managed-results.json').read_text())
    report['selectedDirectory'] = str(selected)
    report['selectedBuiltStagedHashes'] = hashes
    report['nativeArtifactSha256'] = contract.digest(artifact.read_bytes())
    return report


def ordinary_build(output, selected, env):
    # Exercise the REAL Host-to-Checks ProjectReference graph, not a generated
    # direct-source substitute. All outputs use SDK per-project artifact folders.
    checks = PROJECT.parent / 'ImGuiPlayground.Checks'
    for root in (PROJECT, checks):
        for directory, folders, files in os.walk(root, followlinks=False):
            folders[:] = [name for name in folders if not name.startswith('.') and name not in ('bin', 'obj')]
            for name in folders:
                paths.inspect(Path(directory) / name)
            for name in files:
                paths.input_file(Path(directory) / name)
    command = ['dotnet', 'build', str(checks / 'ImGuiPlayground.Checks.csproj'),
               '--artifacts-path', str(output / 'ordinary-artifacts'), '--nologo', '-v:minimal',
               '-p:KSAFolder=' + str(selected), '-p:TreatWarningsAsErrors=true']
    logged(command, output, 'ordinary-project-build', env)


def run(args):
    inputs = paths.component_inputs()
    selected = paths.selected_managed(args.ksa) if args.ksa is not None else None
    protected = inputs + ([selected] if selected is not None else [])
    # Validate the full destination/selected-input relation before reading the
    # contract or creating output, not after native compilation in managed().
    paths.planned_tree(args.output, protected)
    manifest = contract.load(paths)
    lock = builder.read_json(paths.input_file(NATIVE / 'source.lock.json'))
    builder.validate_lock(lock)
    paths.input_file(NATIVE / lock['configuration']['header'])
    config = builder.verify_config_pin(lock)
    archive = paths.input_file(PROJECT / '.tmp/native' / ('imgui-' + lock['upstream']['sourceCommit'] + '.tar.gz'))
    tooling_hashes = {name: contract.digest(paths.input_file(HERE / name).read_bytes()) for name in paths.COMPONENT_FILES}
    output = paths.new_tree(args.output, protected + [archive, config])
    env = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(output / 'zig-cache'), ZIG_LOCAL_CACHE_DIR=str(output / 'zig-local'), PYTHONDONTWRITEBYTECODE='1')
    logged([args.zig, 'version'], output, 'zig-version', env)
    contract.require((output / 'zig-version.log').read_text().strip() == lock['toolchain']['zigVersion'], 'Zig version')
    source = output / 'source'
    provenance = patch_source.prepare_source(archive, source)
    (output / 'source-provenance.json').write_text(encoded(provenance))
    sdk = builder.macos_sdk_path() if 'osx-arm64' in args.rid else None
    records = []
    for rid in args.rid:
        directory = output / rid
        directory.mkdir()
        target = lock['targets'][rid]
        flags = [args.zig, 'c++', '-std=c++11', '-O2', '-fPIC', '-fvisibility=hidden', '-fno-strict-aliasing', '-fno-exceptions', '-fno-rtti', '-fno-threadsafe-statics', '-nostdlib++', '-target', target['zigTarget'], '-I', str(source), '-I', str(HERE), '-include', str(config)]
        if rid == 'osx-arm64':
            flags += ['-isysroot', sdk, '-mmacosx-version-min=11.0']
        objects = []
        for filename in ('imgui.cpp', 'imgui_draw.cpp', 'imgui_tables.cpp', 'imgui_widgets.cpp'):
            obj = directory / (filename + '.o')
            logged(flags + ['-c', str(source / filename), '-o', str(obj)], directory, filename, env)
            objects.append(str(obj))
        for filename in ('manual.cpp', 'fixture.cpp'):
            logged(flags + ['-c', str(HERE / filename), '-o', str(directory / (filename + '.o'))], directory, filename, env)
        artifact = directory / target['file']
        shared = ['-dynamiclib', '-Wl,-install_name,@rpath/libimgui.dylib'] if rid == 'osx-arm64' else ['-shared']
        if rid == 'linux-x64':
            shared += ['-Wl,--no-undefined']
        logged(flags + shared + objects + [str(directory / 'manual.cpp.o'), '-o', str(artifact)], directory, 'link', env)
        exports = symbols(artifact, rid, directory, env)
        symbol_sections = binary_symbols.inspect(artifact.read_bytes(), rid, exports)
        (directory / 'production-symbol-sections.json').write_text(encoded(symbol_sections))
        logged(flags + ['-c', str(HERE / 'abi_proof.cpp'), '-o', str(directory / 'abi_proof.o')], directory, 'abi-proof', env)
        fixture_dir = directory / 'fixture'
        fixture_dir.mkdir()
        fixture = fixture_dir / target['file']
        logged(flags + shared + objects + [str(directory / 'manual.cpp.o'), str(directory / 'fixture.cpp.o'), '-o', str(fixture)], directory, 'fixture-link', env)
        fixture_exports = symbols(fixture, rid, directory, env, True)
        fixture_sections = binary_symbols.inspect(fixture.read_bytes(), rid, fixture_exports)
        (directory / 'fixture-symbol-sections.json').write_text(encoded(fixture_sections))
        kind_negatives = []
        for name in contract.SUPPLEMENTS + tuple(n for n in contract.DYNAMIC if n.startswith('Platform_')):
            opposite = 'Text' if name.startswith('Platform_') else 'Platform_GetWindowPos_ManagedFunctionPointer'
            negative_dir = directory / 'binary-kind-negatives' / name
            negative_dir.mkdir(parents=True)
            changed = binary_negative.relocate(fixture.read_bytes(), rid, name, opposite)
            mutated = negative_dir / target['file']
            mutated.write_bytes(changed)
            # All names still pass: section/kind rejection is additional evidence,
            # not a successful name resolver misreported as a function proof.
            names = symbols(mutated, rid, negative_dir, env, True)
            try:
                binary_symbols.inspect(changed, rid, names)
            except ValueError as error:
                contract.require(name in str(error), 'wrong binary-kind rejection: ' + str(error))
                (negative_dir / 'rejection.log').write_text(str(error) + '\n')
                kind_negatives.append(name)
            else:
                raise ValueError('binary-kind mutation accepted: ' + name)
        # Actual compiler-lowered parameter/return and slot definitions retained.
        logged(flags + ['-S', '-emit-llvm', str(HERE / 'manual.cpp'), '-o', str(directory / 'manual.ll')], directory, 'abi-llvm', env)
        llvm = (directory / 'manual.ll').read_text()
        signature = re.search(r'^define .*@TextV\([^\n]+', llvm, re.M)
        if signature is None or len(re.findall(r'\bptr\b', signature[0])) != 2:
            raise ValueError('TextV must lower to exactly two pointers')
        for bridge in contract.BRIDGES:
            contract.require(re.search(r'^@Platform_' + bridge + r'_ManagedFunctionPointer = .*global ptr null, align 8(?:, !dbg ![0-9]+)?$', llvm, re.M), 'writable pointer slot IR')
        # C consumer: real target stdarg header, no copied SysV implementation.
        c_source = directory / 'header.c'
        c_source.write_text('#include "manual.h"\nvoid (*purr_c_textv)(const char*, va_list) = &TextV;\n')
        c_flags = [args.zig, 'cc', '-std=c11', '-fno-strict-aliasing', '-target', target['zigTarget'], '-I', str(HERE)]
        if sdk and rid == 'osx-arm64':
            c_flags += ['-isysroot', sdk]
        logged(c_flags + ['-c', str(c_source), '-o', str(directory / 'header.o')], directory, 'c-header', env)
        va_source = directory / 'va-declaration.c'
        va_source.write_text('#include <stdarg.h>\nvoid purr_va_declaration(const char* fmt, va_list args);\n')
        # Zig's driver cannot rename an object after a frontend-only action.
        # As in the accepted AST tool, run the pinned driver's exact cc1 command.
        driver_command = c_flags + ['-###', '-fsyntax-only', str(va_source), '-o', str(directory / 'driver-unused.o')]
        (directory / 'va-driver.command.json').write_text(encoded(driver_command))
        driver = process.run(driver_command, capture_output=True, text=True, env=env, timeout=30)
        (directory / 'va-driver.log').write_text(driver.stdout + driver.stderr)
        errors = [line for line in driver.stderr.splitlines() if 'error:' in line]
        contract.require(driver.returncode == 0 or (driver.returncode == 1 and len(errors) == 1 and "error: failed to rename 'tmp/" in errors[0] and errors[0].endswith(': FileNotFound')), 'unexpected Zig AST driver failure')
        commands = [shlex.split(line) for line in driver.stderr.splitlines() if line.lstrip().startswith('"') and '"-cc1"' in line]
        contract.require(len(commands) == 1, 'ambiguous pinned cc1 invocation')
        ast_text = logged(commands[0] + ['-ast-dump=json'], directory, 'va-ast', env)
        ast = json.loads(ast_text)
        declarations = [node for node in ast['inner'] if node.get('name') in ('__builtin_va_list', 'va_list', 'purr_va_declaration')]
        contract.require({d['name'] for d in declarations} == {'__builtin_va_list', 'va_list', 'purr_va_declaration'}, 'actual va_list AST evidence')
        (directory / 'va-declarations.json').write_text(encoded(declarations))
        # One deliberate native-overload signature mutation per fixed import/TextV.
        original = (HERE / 'manual.cpp').read_text()
        positions = list(re.finditer(r'using NativeCall = [^;]+;', original))
        contract.require(len(positions) == 16, '16 native signature gates')
        for i, match in enumerate(positions):
            mutation = directory / ('signature-negative-' + str(i) + '.cpp')
            mutation.write_text(original[:match.start()] + 'using NativeCall = void (*)();' + original[match.end():])
            log = logged(flags + ['-c', str(mutation), '-o', str(directory / 'rejected.o')], directory, 'signature-negative-' + str(i), env, reject=True)
            contract.require('static_cast' in log, 'negative did not fail at typed native overload')
        macro_source = directory / 'assert-macros.cpp'
        macro_source.write_text('#include "manual.h"\n')
        macros_path = directory / 'assert-macros.txt'
        logged(flags + ['-E', '-dM', str(macro_source), '-o', str(macros_path)], directory, 'assert-macros', env)
        assert_macros = [line for line in macros_path.read_text().splitlines() if re.match(r'#define (NDEBUG|assert\(|IM_ASSERT\()', line)]
        record = {'rid': rid, 'ownedExports': exports, 'fixtureExports': fixture_exports, 'artifactSha256': contract.digest(artifact.read_bytes()), 'fixtureArtifactSha256': contract.digest(fixture.read_bytes()), 'textVLLVMDefinition': signature[0], 'nativeSignatureNegatives': 16, 'productionSymbolSections': symbol_sections, 'fixtureSymbolSections': fixture_sections, 'fixtureKindNegatives': kind_negatives, 'assertMacros': assert_macros, 'vaDeclarations': declarations, 'runtimeExecuted': False}
        if args.runtime and rid == 'osx-arm64':
            executable = directory / 'native_test'
            logged(flags + [str(HERE / 'native_test.cpp'), str(fixture), '-Wl,-rpath,' + str(fixture_dir), '-o', str(executable)], directory, 'native-test-build', env)
            logged([str(executable)], directory, 'native-test-run', env, timeout=30)
            record['nativeRuntimeExecuted'] = True
            record['wrongTreeResultCases'] = 8
            # Separate diagnostic configuration only; production profile flags
            # and algorithms are unchanged. Prove that explicit -UNDEBUG really
            # enables upstream assertions, then repeat the real fixture tests.
            control = directory / 'assertion-control'
            control.mkdir()
            control_macros = control / 'macros.txt'
            logged(flags + ['-UNDEBUG', '-E', '-dM', str(macro_source), '-o', str(control_macros)], control, 'macros', env)
            control_text = control_macros.read_text()
            contract.require('#define NDEBUG' not in control_text and '#define assert(e) ((void)0)' not in control_text, 'assertion control disabled')
            control_sources = [str(source / n) for n in ('imgui.cpp', 'imgui_draw.cpp', 'imgui_tables.cpp', 'imgui_widgets.cpp')]
            control_sources += [str(HERE / n) for n in ('manual.cpp', 'fixture.cpp', 'native_test.cpp')]
            logged(flags + ['-UNDEBUG'] + control_sources + ['-o', str(control / 'native_test')], control, 'build', env)
            logged([str(control / 'native_test')], control, 'run', env, timeout=30)
            record['assertionEnabledControlPassed'] = True
            contract.require(selected is not None, 'explicit --ksa, KSAFolder or KSA_DLL_DIR required for managed runtime')
            record['managed'] = managed(directory, fixture, selected, env)
            ordinary_build(directory, selected, env)
            record['ordinaryProjectReferenceBuildPassed'] = True
            record['runtimeExecuted'] = True
        records.append(record)
        (directory / 'result.json').write_text(encoded(record))
    final_hashes = {name: contract.digest(paths.input_file(HERE / name).read_bytes()) for name in paths.COMPONENT_FILES}
    contract.require(final_hashes == tooling_hashes, 'component tooling changed during checks')
    (output / 'results.json').write_text(encoded({'toolingSha256': tooling_hashes, 'schema': 'purr.manual.results.v1', 'manifestSha256': contract.digest((HERE / 'manifest.json').read_bytes()), 'records': records, 'sourceSha256': manifest['sourceSha256']}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, help='new run name under .tmp/native/full-manual')
    parser.add_argument('--rid', action='append', choices=RIDS, required=True)
    parser.add_argument('--runtime', action='store_true')
    parser.add_argument('--zig', default='zig')
    # Mixed-case KSAFolder is the existing explicit-selection alias, not a new variable.
    selection = os.environ.get('KSAFolder') or os.environ.get('KSA_DLL_DIR')  # noqa: SIM112
    parser.add_argument('--ksa', default=selection)
    args = parser.parse_args()
    try:
        run(args)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        parser.exit(1, str(error) + '\n')


if __name__ == '__main__':
    main()
