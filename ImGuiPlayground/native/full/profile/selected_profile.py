#!/usr/bin/env python3
"""Opt-in selected-source adaptation. Accepted API/layout sources are read-only."""
from __future__ import annotations

import argparse
import copy
import difflib
import importlib.util
import sys
from pathlib import Path

sys.dont_write_bytecode = True
# Validate the preflight module itself before executing any local import.
import stat
_bootstrap = Path(__file__).absolute().parent / 'input_paths.py'
for _item in reversed((_bootstrap, *_bootstrap.parents)):
    _info = _item.lstat()
    if stat.S_ISLNK(_info.st_mode) or getattr(_info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0):
        raise ValueError('input redirect: ' + str(_item))
    if _item != _bootstrap and not stat.S_ISDIR(_info.st_mode):
        raise ValueError('input ancestor is not a directory: ' + str(_item))
    if _item == _bootstrap and (not stat.S_ISREG(_info.st_mode) or _info.st_nlink != 1):
        raise ValueError('input module must be an unshared regular file: ' + str(_item))
safe_inputs = importlib.import_module('input_paths')
safe_inputs.modules()
HERE = Path(__file__).absolute().parent
API = HERE.parent / 'api'
LAYOUT = HERE.parent / 'layout'
NATIVE = HERE.parents[1]
PROJECT = NATIVE.parent
SCRATCH = PROJECT / '.tmp/native'
sys.path.insert(0, str(API))
api = importlib.import_module('generate')
declarations = importlib.import_module('declarations')
api_check = importlib.import_module('check')
paths = importlib.import_module('output_paths')
checked, preflight, prepare = paths.checked, paths.preflight, paths.prepare

def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(str(path))
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value

patch = module('profile_patch_source', LAYOUT / 'patch_source.py')
# layout.generate imports patch_source, not the API generator.
sys.modules['patch_source'] = patch
storage = module('profile_layout_generate', LAYOUT / 'generate.py')
builder = declarations.builder
process = declarations.process_utils
require, digest, encoded = api.require, api.digest, api.encoded
PRIOR_MAPPING_SHA256 = '8c9f56f9fdf022c56af931ac2044cd4536fcf7aba9b1604ae9714a5a6bb733ca'


def load_prior():
    require(digest((API/'mapping.json').read_bytes()) == PRIOR_MAPPING_SHA256, 'reviewed pristine mapping pin changed')
    return api.load(API/'mapping.json')


def new_output(path, protected=()):
    path = checked(path, 'directory')
    require(path.is_relative_to(SCRATCH / 'full-api/profile'), 'profile output must be under full-api/profile')
    safe_inputs.disjoint_output(path, protected)
    # Published/prepared input trees stay immutable even for the source-preparing
    # entrypoint, which has no --selected argument of its own.
    for ancestor in path.parents:
        if ancestor == SCRATCH/'full-api/profile':
            break
        for marker in ('profile.json','source-record.json','matrix.json'):
            if (ancestor/marker).exists() or (ancestor/marker).is_symlink():
                safe_inputs.physical(ancestor/marker)
                raise ValueError('output is nested in a prepared profile/enum input: '+str(ancestor))
    require(not path.exists(), 'new profile output directory required')
    preflight(directories=[path])
    return path


def derive(contract, prior, selected, original_headers, selected_headers):
    """Allow only reviewed header receipts, exact unchanged-line relocation and sort width."""
    require(digest(encoded(prior)) == PRIOR_MAPPING_SHA256, 'prior mapping identity is not the reviewed mapping')
    manifest = patch.read_manifest()
    final = dict(prior['headers'])
    for item in manifest['patches']:
        final[item['path']] = item['postimageSha256']
    require(selected['headers'] == final, 'selected header receipts differ from reviewed patches')
    require({n:digest(t.encode()) for n,t in original_headers.items()} == prior['headers'], 'pristine header evidence changed')
    require({n:digest(t.encode()) for n,t in selected_headers.items()} == final, 'selected source evidence changed')
    require(selected['configSha256'] == prior['configSha256'], 'configuration changed')
    require(selected['callbackTypes'] == prior['callbackTypes'], 'callback declarations changed')
    enums = copy.deepcopy(prior['enumUnderlyingTypes'])
    require(enums['ImGuiSortDirection'] == {'canonical':'unsigned char','spelling':'ImU8'}, 'prior sort representation changed')
    enums['ImGuiSortDirection'] = {'canonical':'int','spelling':'int'}
    require(selected['enumUnderlyingTypes'] == enums, 'unapproved enum representation change')
    lines = {}
    for name in original_headers:
        old, new = original_headers[name].splitlines(), selected_headers[name].splitlines()
        # Only exact unchanged source lines qualify; no fuzzy source-location inference.
        lines[name] = {a+i+1:b+i+1 for a,b,size in difflib.SequenceMatcher(None, old, new, autojunk=False).get_matching_blocks() for i in range(size)}
    candidates = {}
    for d in selected['declarations']:
        key = api.declaration_key(d)
        require(key not in candidates, 'ambiguous selected declaration')
        candidates[key] = d
    result = copy.deepcopy(prior)
    result['headers'], result['enumUnderlyingTypes'] = final, enums
    delta = []
    for row in result['mappings']:
        before = copy.deepcopy(row['native'])
        after = copy.deepcopy(before)
        require(before['line'] in lines[before['file']], 'mapped declaration line was substantively edited: '+row['entryPoint'])
        after['line'] = lines[before['file']][before['line']]
        require(candidates.get(api.declaration_key(before)) == after, 'unapproved mapped declaration change: '+row['entryPoint'])
        row['native'] = after
        # Embedded target is the sole permitted conversion-evidence difference.
        expected = copy.deepcopy(row['evidence'])
        expected['target'] = after
        _, observed = api.render(row, {t['id']:t for t in contract['types']})
        require(observed == expected, 'unapproved conversion change: '+row['entryPoint'])
        row['evidence'] = observed
        if before != after:
            delta.append({'entryPoint':row['entryPoint'], 'file':before['file'], 'beforeLine':before['line'], 'afterLine':after['line'], 'sourceLine':selected_headers[after['file']].splitlines()[after['line']-1]})
    for row in result['scalarAdaptations']:
        require(row['entryPoint'] in ('TableGetColumnNextSortDirection_internal','TableSetColumnSortDirection_internal') and row['validValues'] == [0,1,2], 'unapproved scalar adaptation')
        row.update(nativeBits=32, nativeSigned=True, nativeUnderlying=enums['ImGuiSortDirection'], profile='reviewed layout-patched headers; named domain unchanged')
    wrappers, coverage = api.generate(contract, selected, result)
    return result, wrappers, coverage, {'schema':'playground_imgui.profile-delta','schemaVersion':1,'headers':{'before':prior['headers'],'after':final},'declarationRelocations':delta,'scalarAdaptations':result['scalarAdaptations'],'invariants':'all 1130 prior identities, ordered parameters, static/kind/callback/conversions and Bool8 policies preserved'}


def build_profile(output, targets, zig='zig', runtime=True):
    output = new_output(output)
    safe_inputs.fixed_inputs()
    lock = builder.read_json(NATIVE / 'source.lock.json')
    builder.validate_lock(lock)
    config = builder.verify_config_pin(lock)
    archive = SCRATCH / ('imgui-'+lock['upstream']['sourceCommit']+'.tar.gz')
    contract_path = SCRATCH / 'binding-contract.json'
    contract = api.load(contract_path)
    prior = load_prior()
    api.validate_contract(contract, prior)
    files = api_check.output_files(output, targets, runtime)
    files += [output/n for n in ('source-record.json','selected-mapping.json','delta.json','profile.json')]
    preflight(files, [output, output/'source'])
    prepare(files, [output])
    receipt = patch.prepare_source(archive, output/'source')
    (output/'source-record.json').write_bytes(encoded(receipt))
    source = output/'source'
    selected = importlib.import_module('native_model').declarations(source, output/'ast-model', zig)
    original = SCRATCH/'source'/lock['upstream']['sourceCommit']
    api_check.pristine(lock, original)
    def headers(folder):
        return {n:(folder/n).read_text() for n in prior['headers']}
    mapping, wrappers, coverage, delta = derive(contract, prior, selected, headers(original), headers(source))
    (output/'selected-mapping.json').write_bytes(encoded(mapping))
    (output/'delta.json').write_bytes(encoded(delta))
    (output/'generated/wrappers.cpp').write_bytes(wrappers)
    coverage.update(inputContractSha256=digest(contract_path.read_bytes()), mappingSha256=digest(encoded(mapping)), generatorSha256=digest((API/'generate.py').read_bytes()), commonHeaderSha256=digest((API/'purr_abi.h').read_bytes()), wrapperSha256=digest(wrappers))
    (output/'generated/coverage.json').write_bytes(encoded(coverage))
    results = []
    for rid in targets:
        results.append(build_target(output, source, config, lock, rid, zig, wrappers, coverage, runtime))
    (output/'results.json').write_bytes(encoded(results))
    manifest = {'schema':'playground_imgui.selected-profile','schemaVersion':1,'source':str(source),'sourceRecordSha256':digest(encoded(receipt)),'patchManifestSha256':patch.MANIFEST_SHA256,'configSha256':digest(config.read_bytes()),'archiveSha256':receipt['archiveSha256'],'headers':selected['headers'],'declarations':'ast-model/declarations.json','mapping':'selected-mapping.json','wrappers':'generated/wrappers.cpp','coverage':'generated/coverage.json','delta':'delta.json','requiredAllTranslationUnitFlags':['-fno-strict-aliasing'],'reservedImports':api.RESERVED,'reservedDynamicExports':contract['dynamicExports'],'results':results,'fullQualification':False}
    manifest['artifacts'] = {name:digest((output/name).read_bytes()) for name in ('source-record.json','ast-model/ast.json','ast-model/declarations.json','selected-mapping.json','generated/wrappers.cpp','generated/coverage.json','delta.json','results.json')}
    manifest['dependencies'] = {'pristineMappingSha256':PRIOR_MAPPING_SHA256,'commonHeaderSha256':digest((API/'purr_abi.h').read_bytes()),'apiGeneratorSha256':digest((API/'generate.py').read_bytes()),'portableApiProjectionSha256':mapping['portableProjectionSha256'],'managedContractHostProvenanceSha256':digest(contract_path.read_bytes())}
    (output/'profile.json').write_bytes(encoded(manifest))
    return manifest


def compile_command(source, config, lock, rid, zig):
    command = [zig,'c++','-std=c++11','-O2','-DNDEBUG','-fPIC','-fvisibility=hidden','-fno-strict-aliasing','-fno-exceptions','-fno-rtti','-fno-threadsafe-statics','-nostdlib++','-target',lock['targets'][rid]['zigTarget'],'-I',str(source),'-I',str(API),'-include',str(config)]
    if rid == 'osx-arm64':
        command += ['-isysroot',builder.macos_sdk_path(),'-mmacosx-version-min=11.0']
    if rid == 'win-x64':
        command += ['-DPLAYGROUND_IMGUI_WINDOWS=1']
    return command


def build_target(output, source, config, lock, rid, zig, wrappers, coverage, runtime):
    import re
    folder = output/rid
    artifact = folder/lock['targets'][rid]['file']
    command = compile_command(source, config, lock, rid, zig)
    inputs = [str(source/n) for n in lock['build']['sourceFiles'] if n != 'src/PlaygroundBrutalAdapter.cpp']+[str(output/'generated/wrappers.cpp')]
    shared = ['-dynamiclib','-Wl,-install_name,@rpath/libimgui.dylib'] if rid == 'osx-arm64' else ['-shared']
    if rid == 'linux-x64':
        shared += ['-Wl,--no-undefined','-Wl,-soname,libimgui.so']
    api_check.logged(command+shared+inputs+['-o',str(artifact)],folder/'build.log')
    ccommand = [zig,'cc','-std=c11','-fno-strict-aliasing','-target',lock['targets'][rid]['zigTarget'],'-I',str(API)]
    if rid == 'osx-arm64':
        ccommand += ['-isysroot',builder.macos_sdk_path()]
    api_check.logged(ccommand+['-c',str(API/'abi_c_test.c'),'-o',str(folder/'abi_c_test.o')],folder/'c-header-test.log')
    api_check.logged(command+['-g0','-S','-emit-llvm',str(API/'bool_view_proof.cpp'),'-o',str(folder/'bool-view-proof.ll')],folder/'bool-view-proof.log')
    match = re.search(r'define [^\n]*@purr_bool_output_proof\([^\n]*\)[^{]*\{([^}]+)\}',(folder/'bool-view-proof.ll').read_text())
    require(match is not None and re.fullmatch(r'ret ptr %\w+',match.group(1).strip()) is not None,'Bool8 output entry access')
    actual = sorted(api_check.export_names(artifact,rid,folder))
    expected = sorted(r['entryPoint'] for r in coverage['implemented'])
    require([n for n in actual if n not in ('__dso_handle','_mh_dylib_header')] == expected,'owned export inventory mismatch')
    mutated = wrappers.decode()
    start = mutated.index('    using NativeCall = ')
    end = mutated.index(';',start)
    (folder/'signature-mutation.cpp').write_text(mutated[:start]+'    using NativeCall = void (*)()'+mutated[end:])
    negative = command+['-c',str(folder/'signature-mutation.cpp'),'-o',str(folder/'must-not-compile.o')]
    (folder/'negative-compile.command.json').write_bytes(encoded(negative))
    run = process.run(negative,capture_output=True,text=True,timeout=120)
    (folder/'negative-compile.log').write_text(run.stdout+run.stderr)
    require(run.returncode != 0 and 'static_cast' in run.stderr,'wrong-signature compiler negative failed')
    executed = runtime and rid == 'osx-arm64'
    if executed:
        executable = folder/'api_native_test'
        api_check.logged(command+inputs+[str(API/'api_native_test.cpp'),str(API/'bool_pointer_test.cpp'),'-o',str(executable)],folder/'test-build.log')
        api_check.logged([str(executable)],folder/'test-run.log',30)
        api_check.logged([sys.executable,str(API/'api_library_test.py'),str(artifact),str(output/'generated/coverage.json')],folder/'library-test.log',30)
    record = {'rid':rid,'artifactSha256':digest(artifact.read_bytes()),'ownedExports':1130,'typedSignatureNegative':True,'c11':True,'boolOutputNoRead':True,'nativeFixturesExecuted':executed,'managedExecution':False}
    (folder/'result.json').write_bytes(encoded(record))
    return record

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--targets',nargs='+',choices=['osx-arm64','linux-x64','win-x64'],default=['osx-arm64','linux-x64','win-x64'])
    p.add_argument('--zig',default='zig')
    a = p.parse_args()
    build_profile(a.output,a.targets,a.zig)
