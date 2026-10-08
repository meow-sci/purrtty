#!/usr/bin/env python3
"""Actual entrypoint canaries for pre-import/input/output preflight; no native semantics."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import shutil
import sys
from pathlib import Path
from typing import Any

HERE = Path(__file__).absolute().parent
# This invoked entrypoint is the trusted bootstrap. Check the guard leaf/cache
# and every ancestor lexically BEFORE importing any selected project module.
guard_file = HERE / 'path_guard.py'
for selected_module in (guard_file, Path(importlib.util.cache_from_source(str(guard_file)))):
    if '..' in selected_module.parts:
        raise ValueError('accessor preflight: parent traversal not allowed: '+str(selected_module))
    for candidate in (selected_module, *selected_module.parents):
        if candidate.is_symlink():
            raise ValueError('accessor preflight: symlink not allowed: '+str(candidate))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('purr_accessor_paths', guard_file)
assert spec is not None and spec.loader is not None
paths: Any = importlib.util.module_from_spec(spec)
spec.loader.exec_module(paths)
paths.preflight_component(HERE)
runner = paths.load_module('purr_accessor_input_runner', HERE / 'run.py')


def fixture_environment(inherited: dict[str, str]) -> dict[str, str]:
    # Fixture arguments, not the operator's installation, select synthetic inputs.
    # Windows normalizes environment keys; remove aliases case-insensitively.
    env = {key: value for key, value in inherited.items()
           if key.casefold() not in ('ksafolder', 'ksa_dll_dir')}
    env['PYTHONDONTWRITEBYTECODE'] = '1'
    return env


def snapshot(root: Path) -> dict[str, str]:
    result = {}
    for directory, dirs, files in os.walk(root, followlinks=False):
        for name in dirs + files:
            path = Path(directory) / name
            key = str(path.relative_to(root))
            if path.is_symlink():
                result[key] = 'link:' + os.readlink(path)
            elif path.is_dir():
                result[key] = 'directory'
            else:
                result[key] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def test(output: Path) -> dict:
    output = runner.new_output(output)
    if fixture_environment({'KSAFolder':'first', 'KSAFOLDER':'second',
                            'KSA_DLL_DIR':'third', 'ksa_dll_dir':'fourth',
                            'KEEP_FIXTURE_ENV':'preserved', 'PYTHONDONTWRITEBYTECODE':'0'}) != {
                                'KEEP_FIXTURE_ENV':'preserved', 'PYTHONDONTWRITEBYTECODE':'1'}:
        raise ValueError('fixture environment did not isolate selection aliases')
    results = []
    original = runner.PROJECT.parent
    source_files = [
        *[p for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts],
        *[p for p in (HERE.parent/'layout').rglob('*') if p.is_file() and '__pycache__' not in p.parts],
        runner.NATIVE/'process_utils.py', runner.NATIVE/'source.lock.json',
        runner.NATIVE/'include/playground_imgui_config.h',
        runner.PROJECT/'NativeFieldAccessors.cs',
        runner.PROJECT.parent/'ImGuiPlayground.Checks/FullAccessorChecks.cs',
        runner.PROJECT/'.tmp/native/binding-contract.json',
        runner.PROJECT/'.tmp/native/imgui-031a18c417158427217bc5890e0ec0cb7e7b4b63.tar.gz']
    for path in source_files:
        paths.input_file(path)
    output.mkdir(parents=True)

    def fixture(name: str) -> tuple[Path, Path, Path, Path]:
        case = output/name
        checkout = case/'checkout'
        for source in source_files:
            destination = checkout/source.relative_to(original)
            destination.parent.mkdir(parents=True, exist_ok=True)
            # Independent files: no mutation can affect a hardlinked original.
            shutil.copy2(source, destination)
        tool = case/'compiler-marker'
        tool.write_text('#!'+sys.executable+'\nfrom pathlib import Path\nimport sys\n'
                        +'Path('+repr(str(case/'compiler.marker'))+').write_text("invoked")\n'
                        +'print("CONTROLLED_COMPILER_MARKER",file=sys.stderr)\nraise SystemExit(79)\n')
        tool.chmod(0o700)
        return case, checkout, checkout/HERE.relative_to(original), tool

    def marker(case: Path) -> str:
        return ('from pathlib import Path\nPath('+repr(str(case/'import.marker'))
                +').write_text("executed")\nraise RuntimeError("CONTROLLED_IMPORT_MARKER")\n')

    def invoke(case: Path, checkout: Path, command: list[str], expected: str, destination: Path | None,
               *, import_expected: bool = False, compiler_expected: bool = False,
               ambient_selection: Path | None = None) -> None:
        before = snapshot(checkout)
        target = case/'targets'
        targets_before = snapshot(target) if target.exists() else {}
        inherited = dict(os.environ)
        if ambient_selection is not None:
            inherited.update(KSAFolder=str(ambient_selection), KSA_DLL_DIR=str(ambient_selection))
        env = fixture_environment(inherited)
        with (case/'entrypoint.log').open('w') as log:
            log.write(json.dumps(command)+'\n')
            log.flush()
            result = runner.process_utils.run(command, cwd=checkout, env=env, stdout=log, stderr=log, timeout=30)
        log_text = (case/'entrypoint.log').read_text()
        if result.returncode == 0 or expected not in log_text:
            raise ValueError('entrypoint did not report expected rejection: '+str(case))
        if (case/'import.marker').exists() != import_expected or (case/'compiler.marker').exists() != compiler_expected:
            raise ValueError('entrypoint crossed import/compiler preflight: '+str(case))
        if destination is not None and destination.exists():
            raise ValueError('entrypoint created rejected destination: '+str(destination))
        if snapshot(checkout) != before or (snapshot(target) if target.exists() else {}) != targets_before:
            raise ValueError('entrypoint changed protected input bytes/membership: '+str(case))
        results.append({'name':case.name, 'command':command, 'exitCode':result.returncode,
                        'expectedDiagnostic':expected, 'importMarker':import_expected,
                        'compilerMarker':compiler_expected, 'destinationCreated':False,
                        'protectedInputsUnchanged':True,
                        'ambientSelectionOverrideExercised':ambient_selection is not None})

    # Positive controls prove the actual invoked import/compiler marker works.
    case, checkout, component, tool = fixture('control-import')
    (component/'generate.py').write_text(marker(case))
    invoke(case, checkout, [sys.executable,str(component/'run.py'),'--help'], 'CONTROLLED_IMPORT_MARKER', None, import_expected=True)
    case, checkout, component, tool = fixture('control-compiler')
    dest = checkout/'ImGuiPlayground/.tmp/native/accessors/result'
    invoke(case, checkout, [sys.executable,str(component/'run.py'),'--out',str(dest),'--zig',str(tool)],
           'returned non-zero exit status 79',dest,compiler_expected=True)

    relative_component = HERE.relative_to(original)
    module_inputs = [
        relative_component/'path_guard.py', relative_component/'generate.py',
        relative_component/'run_managed.py', relative_component/'self_test.py',
        relative_component/'input_self_test.py',
        Path('ImGuiPlayground/native/full/layout/generate.py'),
        Path('ImGuiPlayground/native/full/layout/patch_source.py'),
        Path('ImGuiPlayground/native/process_utils.py')]
    native_inputs = [relative_component/name for name in ('accessors.cpp','accessors.h','fixture.cpp','fixture.h')]
    data_inputs = [Path(name) for name in (
        'ImGuiPlayground/native/source.lock.json', 'ImGuiPlayground/native/include/playground_imgui_config.h',
        'ImGuiPlayground/.tmp/native/binding-contract.json',
        'ImGuiPlayground/.tmp/native/imgui-031a18c417158427217bc5890e0ec0cb7e7b4b63.tar.gz',
        'ImGuiPlayground/native/full/layout/mapping.json', 'ImGuiPlayground/native/full/layout/bitfields.json',
        'ImGuiPlayground/native/full/layout/enum-transport.json', 'ImGuiPlayground/native/full/layout/patch-manifest.json',
        'ImGuiPlayground/NativeFieldAccessors.cs', 'ImGuiPlayground.Checks/FullAccessorChecks.cs')]
    data_inputs += [p.relative_to(original) for p in (HERE.parent/'layout/patches').glob('*.json')]
    # Each actual command (not new_output unit tests) sees the bad selected input.
    for relative in module_inputs + native_inputs + data_inputs:
        for dangling in (False, True):
            name = 'input-'+str(relative).replace('/','_')+('-dangling' if dangling else '-existing')
            case, checkout, component, tool = fixture(name)
            dest = checkout/'ImGuiPlayground/.tmp/native/accessors/result'
            selected = checkout/relative
            targets = case/'targets'
            targets.mkdir()
            target = targets/'selected-input'
            if not dangling:
                target.write_text(marker(case) if relative in module_inputs else '#error CONTROLLED_NATIVE_INPUT\n')
            selected.unlink()
            selected.symlink_to(target)
            # Native/data rejection must happen before even normal generator
            # execution. Its controlled, regular-file import canary proves that.
            if relative not in module_inputs:
                with (component/'generate.py').open('a') as stream:
                    stream.write('\n'+marker(case))
            invoke(case, checkout, [sys.executable,str(component/'run.py'),'--out',str(dest),'--zig',str(tool)],
                   'accessor preflight: symlink not allowed: '+str(selected),dest)

    # The reproduced --help trigger, every consuming bootstrap, and the generator
    # itself must guard the selected dependency BEFORE import execution.
    for entry, selected_name in [('run.py','generate.py'),('run_managed.py','run.py'),
                                 ('self_test.py','run.py'),('input_self_test.py','run.py'),
                                 ('generate.py','../layout/generate.py')]:
        for dangling in (False,True):
            case, checkout, component, tool = fixture('bootstrap-'+entry+('-dangling' if dangling else '-existing'))
            selected = component.parent/'layout/generate.py' if selected_name.startswith('../') else component/selected_name
            targets=case/'targets'
            targets.mkdir()
            target=targets/'module.py'
            if not dangling:
                target.write_text(marker(case))
            selected.unlink()
            selected.symlink_to(target)
            invoke(case,checkout,[sys.executable,str(component/entry),'--help'],
                   'accessor preflight: symlink not allowed: '+str(selected),None)

    for relative in [Path('ImGuiPlayground/native/full/layout'),Path('ImGuiPlayground/native/include'),
                     Path('ImGuiPlayground/.tmp/native')]:
        for dangling in (False,True):
            case,checkout,component,tool=fixture('ancestor-'+str(relative).replace('/','_')+('-dangling' if dangling else '-existing'))
            selected=checkout/relative
            targets=case/'targets'
            targets.mkdir()
            target=targets/'directory'
            if not dangling:
                shutil.copytree(selected,target)
            shutil.rmtree(selected)
            selected.symlink_to(target,target_is_directory=True)
            with (component/'generate.py').open('a') as stream:
                stream.write('\n'+marker(case))
            dest=checkout/'ImGuiPlayground/.tmp/native/accessors/result'
            invoke(case,checkout,[sys.executable,str(component/'run.py'),'--out',str(dest),'--zig',str(tool)],
                   'accessor preflight: symlink not allowed: '+str(selected),dest)

    # Equivalent selected-source write-plan regression: a nested output must fail
    # before consumer receipt reads, mkdir, generator replay or compiler discovery.
    for entry in ['run.py','run_managed.py','self_test.py','input_self_test.py']:
        case,checkout,component,tool=fixture('protected-generated-source-'+entry)
        build=checkout/'ImGuiPlayground/.tmp/native/accessors/selected'
        source=build/'source'
        source.mkdir(parents=True)
        (source/'protected.cpp').write_text('protected native source\n')
        (build/'source-record.json').write_text('{}\n')
        dest=source/'must-not-exist'
        command=[sys.executable,str(component/entry),'--out',str(dest)]
        if entry=='run.py':
            command+=['--zig',str(tool)]
        elif entry=='run_managed.py':
            command+=['--build',str(build),'--managed-directory',str(checkout)]
        elif entry=='self_test.py':
            command+=['--build',str(build),'--managed',str(checkout)]
        invoke(case,checkout,command,'accessor preflight: output',dest)

    for entry, selected_kind in [('run_managed.py','build'),('run_managed.py','managed-directory'),
                                 ('self_test.py','build'),('self_test.py','managed')]:
        case,checkout,component,tool=fixture('protected-selected-'+entry+'-'+selected_kind)
        build=checkout/'ImGuiPlayground/.tmp/native/accessors/build-input'
        managed=checkout/'ImGuiPlayground/.tmp/native/accessors/managed-input'
        build.mkdir(parents=True)
        managed.mkdir()
        (build/'protected.bin').write_bytes(b'protected build input')
        (managed/'protected.bin').write_bytes(b'protected managed input')
        dest=(build if selected_kind=='build' else managed)/'nested-output'
        option='--managed-directory' if entry=='run_managed.py' else '--managed'
        command=[sys.executable,str(component/entry),'--build',str(build),option,str(managed),'--out',str(dest)]
        ambient = None
        if entry == 'run_managed.py' and selected_kind == 'managed-directory':
            # Without isolation this unrelated ambient directory wins selection,
            # bypasses the intended overlap and reaches a missing manifest instead.
            ambient = case/'ambient-selection'
            ambient.mkdir()
        invoke(case,checkout,command,'accessor preflight: output overlaps protected input:',dest,
               ambient_selection=ambient)

    for entry in ['run_managed.py','self_test.py']:
        for relative in ['manifest.json','source/owned-input.h']:
            for dangling in (False,True):
                case,checkout,component,tool=fixture('consumer-'+entry+'-'+relative.replace('/','_')+('-dangling' if dangling else '-existing'))
                build=checkout/'ImGuiPlayground/.tmp/native/accessors/build-input'
                managed=checkout/'ImGuiPlayground/.tmp/native/accessors/managed-input'
                selected=build/relative
                selected.parent.mkdir(parents=True)
                managed.mkdir(parents=True)
                targets=case/'targets'
                targets.mkdir()
                target=targets/'redirected-input'
                if not dangling:
                    target.write_text('must not be read as a manifest/header\n')
                selected.symlink_to(target)
                dest=checkout/'ImGuiPlayground/.tmp/native/accessors/result'
                option='--managed-directory' if entry=='run_managed.py' else '--managed'
                command=[sys.executable,str(component/entry),'--build',str(build),option,str(managed),'--out',str(dest)]
                invoke(case,checkout,command,'accessor preflight: symlink not allowed: '+str(selected),dest)

    report={'schema':'purr.accessor-input-preflight-tests','version':1,'count':len(results),
            'positiveControls':2,'negativeChecks':len(results)-2,'skipped':0,
            'environmentIsolation':{'caseInsensitiveSelectionAliasesRemoved':True,
                                    'unrelatedVariablesPreserved':True,
                                    'conflictingAmbientSelectionCases':sum(c['ambientSelectionOverrideExercised'] for c in results)},
            'cases':results}
    (output/'input-self-test.json').write_text(json.dumps(report,indent=2)+'\n')
    return report


def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out',type=Path,required=True)
    args=parser.parse_args()
    test(args.out)


if __name__=='__main__':
    try:
        main()
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr)
        sys.exit(1)
