#!/usr/bin/env python3
"""Closed-input, path, determinism and real native/C# mutation gates (maintainer only)."""
from __future__ import annotations

import argparse
import copy
import importlib.util
import json
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
runner = paths.load_module('purr_accessor_runner_tests', HERE / 'run.py')


def test(build: Path, managed: Path, output: Path) -> None:
    paths.preflight_component(HERE)
    output=runner.new_output(output, protected=(build, managed))
    build=paths.input_tree(build)
    managed=paths.input_tree(managed)
    for p in [build/'manifest.json', build/'osx-arm64/build-record.json', managed/'run-record.json']:
        paths.input_file(p)
    manifest=json.loads((build/'manifest.json').read_bytes())
    build_record=json.loads((build/'osx-arm64/build-record.json').read_bytes())
    run_record=json.loads((managed/'run-record.json').read_bytes())
    command=run_record['executeCommand']
    output=runner.new_output(output, protected=(build, managed, *[Path(command[i]) for i in (1, 2, 3, 4, 5)]))
    for index in (1, 2, 3, 5):
        paths.input_file(Path(command[index]))
    paths.input_tree(Path(command[4]))
    for index, argument in enumerate(build_record['command'][1:], 1):
        if build_record['command'][index-1] == '-I':
            paths.input_tree(Path(argument))
        elif build_record['command'][index-1] == '-include' or (not argument.startswith('-') and argument.endswith('.cpp')):
            paths.input_file(Path(argument))
    if run_record['identity']!=manifest['identity'] or run_record['nativeArtifactSha256']!=build_record['artifactSha256']:
        raise ValueError('mismatched positive control inputs')
    for name,hash_ in run_record['managedSourceHashes'].items():
        path=runner.PROJECT/name if name=='NativeFieldAccessors.cs' else runner.PROJECT.parent/'ImGuiPlayground.Checks'/name
        if runner.digest(paths.input_file(path).read_bytes())!=hash_:
            raise ValueError('managed positive control source changed')
    source_record=json.loads(paths.input_file(build/'source-record.json').read_bytes())
    runner.verify_tree(build/'source',source_record['sourceFiles'])
    output.mkdir(parents=True)
    input_checks=paths.load_module('purr_accessor_input_checks', HERE/'input_self_test.py')
    input_report=input_checks.test(output/'entrypoint-preflight')
    passed=['entrypoint-'+case['name'] for case in input_report['cases']]
    def reject(name: str, action: Any) -> None:
        try:
            action()
        except (ValueError,OSError):
            passed.append(name)
            return
        raise ValueError('negative was accepted: '+name)

    # Existing root, ancestor, leaf, dangling links are checked before resolve or
    # mkdir. A virtual PROJECT lets root/ancestor tests stay wholly in new scratch;
    # do not mutate the actual project scratch root or any accepted source input.
    path_cases=output/'paths'
    path_cases.mkdir()
    protected=path_cases/'protected'
    protected.mkdir()
    (protected/'sentinel').write_text('must remain unchanged')
    saved_project=runner.PROJECT
    try:
        for level in ['project','tmp','native','accessors','ancestor','leaf']:
            for dangling in [False,True]:
                case=path_cases/(level+('-dangling' if dangling else '-existing'))
                case.mkdir()
                project=case/'project'
                components=[project,project/'.tmp',project/'.tmp/native',project/'.tmp/native/accessors',project/'.tmp/native/accessors/ancestor',project/'.tmp/native/accessors/ancestor/result']
                index=['project','tmp','native','accessors','ancestor','leaf'].index(level)
                link=components[index]
                link.parent.mkdir(parents=True,exist_ok=True)
                link.symlink_to(protected/'absent' if dangling else protected,target_is_directory=True)
                runner.PROJECT=project
                reject('path-'+level+('-dangling' if dangling else '-existing'),lambda target=components[-1]:runner.new_output(target))
        runner.PROJECT=saved_project
        reject('outside-owned-scratch',lambda:runner.new_output(runner.PROJECT/'native/accessors-rejected-output'))
        reject('existing-output',lambda:runner.new_output(output))
        reject('dotdot-output',lambda:runner.new_output(output/'x/../out'))
        reject('protected-pristine-input',lambda:runner.new_output(runner.PROJECT/'.tmp/native/source'))
        reject('protected-contract-input',lambda:runner.new_output(runner.PROJECT/'.tmp/native/binding-contract.json'))
        reject('protected-component-input',lambda:runner.new_output(HERE/'accessors.cpp'))
    finally:
        runner.PROJECT=saved_project
    if sorted(p.name for p in protected.iterdir())!=['sentinel'] or (protected/'sentinel').read_text()!='must remain unchanged':
        raise ValueError('negative path check wrote through redirect')
    passed.append('protected-target-unchanged')

    contract=runner.PROJECT/'.tmp/native/binding-contract.json'
    replay=output/'replay'
    replay.mkdir()
    replay_manifest=runner.generator.generate(contract,replay)
    if replay_manifest!=manifest:
        raise ValueError('deterministic source/profile manifest replay failed')
    for name in ['manifest.json','fields.inc','fixture_fields.inc','identity.h']:
        if (build/name).read_bytes()!=(replay/name).read_bytes():
            raise ValueError('deterministic generation failed: '+name)
    passed.append('deterministic-generation')
    parsed_contract,mapping,bits=runner.generator.layout.load_inputs(contract)
    source_lock=json.loads((runner.NATIVE/'source.lock.json').read_bytes())
    source_lock['toolchain']['zigVersion']='unapproved'
    reject('closed-source-profile-mutation',lambda:runner.generator.verify_source_lock(json.dumps(source_lock).encode()))
    changed=copy.deepcopy(bits)
    changed['members'].pop()
    reject('closed47-omission',lambda:runner.generator.layout.validate_mapping(parsed_contract,mapping,changed))
    changed=copy.deepcopy(bits)
    changed['members'][0]['originalWidthBits']=2
    reject('closed-width-mutation',lambda:runner.generator.layout.validate_mapping(parsed_contract,mapping,changed))
    changed_mapping=copy.deepcopy(mapping)
    next(t for t in changed_mapping['types'] if t['disposition']=='opaque-placeholder')['native']='invented'
    reject('closed13-shape-mutation',lambda:runner.generator.layout.validate_mapping(parsed_contract,changed_mapping,bits))

    def managed_negative(name: str, library: Path, manifest_path: Path, expected: str) -> None:
        folder=output/name
        folder.mkdir(exist_ok=True)
        invocation=[command[0],command[1],str(library),str(manifest_path),command[4],command[5],str(folder/'must-not-pass.json')]
        with (folder/'run.log').open('w') as log:
            log.write(json.dumps(invocation)+'\n')
            log.flush()
            result=runner.process_utils.run(invocation,stdout=log,stderr=log,timeout=120)
        text=(folder/'run.log').read_text()
        if result.returncode==0 or expected not in text or (folder/'must-not-pass.json').exists():
            raise ValueError('mutation not meaningfully rejected: '+name+'; see '+str(folder/'run.log'))
        passed.append(name)

    # Manifest checks execute the ACTUAL helper check consumer, not a Python-only
    # approximation. Keep the advertised identity unchanged to exercise rehashing.
    native=Path(command[2])
    for name,change in [
        ('manifest-field-omitted',lambda m:m['fields'].pop()),
        ('manifest-opaque-omitted',lambda m:m['opaqueShapes'].pop()),
        ('manifest-source-profile-changed',lambda m:m.__setitem__('sourceLockSha256','0'*64)),
        ('manifest-patch-profile-changed',lambda m:m.__setitem__('patchManifestSha256','0'*64)),
        ('manifest-accessor-contract-changed',lambda m:m.__setitem__('version',2)),
    ]:
        folder=output/name
        folder.mkdir()
        mutated=copy.deepcopy(manifest)
        change(mutated)
        path=folder/'manifest.json'
        path.write_text(json.dumps(mutated,indent=2)+'\n')
        managed_negative(name,native,path,'manifest schema mismatch' if name.endswith('contract-changed') else 'changed source/profile/accessor contract')

    mutants=[
        ('wrong-native-field','fields.inc','((ImFontGlyph*)object)->Colored =','((ImFontGlyph*)object)->Visible =','mask/neighbor/ordinary adjacent field/adjacent RECORD preservation'),
        ('wrong-signed-get','fields.inc','*out = ((const ImFontAtlasRectEntry*)object)->TargetIndex;','*out = (uint32_t)((const ImFontAtlasRectEntry*)object)->TargetIndex;','signed/native/oracle read mismatch'),
        ('wrong-native-mask','fixture.cpp','out[i]^=((const uint8_t*)f.objects)[i];','out[i]=(uint8_t)((out[i]^((const uint8_t*)f.objects)[i])<<1);','mask/neighbor/ordinary adjacent field/adjacent RECORD preservation'),
        ('wrong-style-clr6-stride','accessors.cpp','const ImGuiStyleVarInfo* info = ImGui::GetStyleVarInfo(index);','const ImGuiStyleVarInfo* info = (const ImGuiStyleVarInfo*)((const unsigned char*)ImGui::GetStyleVarInfo(0) + index*6);','style native indexing (never CLR6 stride)'),
        ('wrong-rect-clr7-stride','accessors.cpp','const ImFontAtlasRectEntry& e = v[index];','const ImFontAtlasRectEntry& e = *(const ImFontAtlasRectEntry*)((const unsigned char*)v.Data + index*7);','real RectEntry vector native4 indexing, not CLR7'),
        ('missing-production-export','accessors.cpp','int32_t purr_accessor_get(','int32_t purr_missing_accessor_get(','missing purr_accessor_get'),
        ('wrong-native-identity','identity.h',manifest['identity'],'0'*64,'source/profile/ABI identity mismatch'),
        ('native-allocation-leak','fixture.cpp','void purr_fixture_field_destroy(void* fixture) { IM_DELETE((PurrFieldFixture*)fixture); }','void purr_fixture_field_destroy(void* fixture) { IM_NEW(ImFontGlyph)(); IM_DELETE((PurrFieldFixture*)fixture); }','native allocation audit failed'),
    ]
    for name,file,old,new,expected in mutants:
        folder=output/name
        folder.mkdir()
        for basename in ['accessors.cpp','accessors.h','fixture.cpp','fixture.h']:
            shutil.copy2(HERE/basename,folder/basename)
        for basename in ['fields.inc','fixture_fields.inc','identity.h']:
            shutil.copy2(build/basename,folder/basename)
        path=folder/file
        contents=path.read_text()
        if contents.count(old)!=1:
            raise ValueError('mutation seam moved: '+name)
        path.write_text(contents.replace(old,new))
        artifact=folder/'libmutant.dylib'
        compile_command=[str(folder/Path(arg).name) if arg in [str(HERE/'accessors.cpp'),str(HERE/'fixture.cpp')] else arg for arg in build_record['command']]
        compile_command[-1]=str(artifact)
        runner.logged(compile_command,folder/'build.log')
        managed_negative(name,artifact,build/'manifest.json',expected)
    runner.verify_tree(build/'source',source_record['sourceFiles'])
    (output/'self-test.json').write_text(json.dumps({'schema':'purr.accessor-self-tests','version':1,'passed':passed,'count':len(passed),'skipped':0,'identity':manifest['identity'],'nativeMutants':len(mutants),'actualManagedManifestMutants':5,'inputPreflightCases':input_report['count'],'inputPreflightPositiveControls':input_report['positiveControls'],'foreignRuntimeExecution':'pending; not inferred from compilation'},indent=2)+'\n')


def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build',type=Path,required=True)
    parser.add_argument('--managed',type=Path,required=True)
    parser.add_argument('--out',type=Path,required=True)
    args=parser.parse_args()
    test(args.build,args.managed,args.out)


if __name__=='__main__':
    try:
        main()
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr)
        sys.exit(1)
