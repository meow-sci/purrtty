#!/usr/bin/env python3
"""Consume the accepted ordinary-storage gate on the exact selected tree/config."""
from __future__ import annotations

import argparse
import importlib
import re
import sys
import tarfile
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
p = importlib.import_module('selected_profile')


def verify_source(selected):
    selected = safe_inputs.selected(selected)
    safe_inputs.fixed_inputs()
    manifest = p.api.load(selected/'profile.json')
    receipt = p.api.load(selected/'source-record.json')
    p.require(p.digest(p.encoded(receipt)) == manifest['sourceRecordSha256'],'source receipt drift')
    source = selected/'source'
    for f in source.rglob('*'):
        p.checked(f, 'file' if f.is_file() else 'directory')
    actual = {str(f.relative_to(source)):p.digest(f.read_bytes()) for f in source.rglob('*') if f.is_file()}
    patch_manifest = p.patch.read_manifest()
    archive = p.SCRATCH/('imgui-'+patch_manifest['sourceCommit']+'.tar.gz')
    p.builder.verify_archive(archive,patch_manifest['archiveSha256'])
    original = {}
    with tarfile.open(archive,'r:gz') as bundle:
        for member in bundle.getmembers():
            if member.isfile():
                stream = bundle.extractfile(member)
                if stream is None:
                    raise p.api.ContractError('missing source archive stream')
                original['/'.join(Path(member.name).parts[1:])] = p.digest(stream.read())
    expected = dict(original)
    for item in patch_manifest['patches']:
        expected[item['path']] = item['postimageSha256']
    p.require(receipt['pristineFiles'] == original and actual == receipt['sourceFiles'] == expected,'selected source drift')
    required_artifacts = {'source-record.json','ast-model/ast.json','ast-model/declarations.json','selected-mapping.json','generated/wrappers.cpp','generated/coverage.json','delta.json','results.json'}
    p.require(set(manifest['artifacts']) == required_artifacts,'profile artifact inventory changed')
    for name in required_artifacts:
        path = p.checked(selected/name,'file')
        p.require(p.digest(path.read_bytes()) == manifest['artifacts'][name],'selected profile artifact drift: '+name)
    config = p.builder.verify_config_pin(p.builder.read_json(p.NATIVE/'source.lock.json'))
    p.require(manifest['configSha256'] == p.digest(config.read_bytes()),'selected config drift')
    prior = p.load_prior()
    pristine = p.SCRATCH/'source'/patch_manifest['sourceCommit']
    selected_headers = {n:(source/n).read_text() for n in prior['headers']}
    original_headers = {n:(pristine/n).read_text() for n in prior['headers']}
    derived, wrappers, _, delta = p.derive(p.api.load(p.SCRATCH/'binding-contract.json'),prior,p.api.load(selected/'ast-model/declarations.json'),original_headers,selected_headers)
    p.require(p.encoded(derived) == (selected/'selected-mapping.json').read_bytes() and wrappers == (selected/'generated/wrappers.cpp').read_bytes() and p.encoded(delta) == (selected/'delta.json').read_bytes(),'profile mapping/code derivation changed')
    p.require(receipt['patched'] and receipt['patchManifestSha256'] == p.patch.MANIFEST_SHA256,'patch selection drift')
    return source


def run(selected, output, targets, zig='zig'):
    output = p.new_output(output, [selected])
    safe_inputs.fixed_inputs()
    selected = safe_inputs.selected(selected)
    selected = p.checked(selected,'directory')
    source = verify_source(selected)
    contract, mapping, bits = p.storage.load_inputs(p.SCRATCH/'binding-contract.json')
    lock = p.builder.read_json(p.NATIVE/'source.lock.json')
    config = p.builder.verify_config_pin(lock)
    files = [output/n for n in ('probe.cpp','interface.json','original_bitfields.inc','results.json')]
    for rid in targets:
        files += [output/rid/n for n in ('layout-probe','layout-probe.exe','layout-probe.pdb','build.log','build.command.json','checked.o','static.log','static.command.json','native.json','runtime.log','comparison.json')]
    p.prepare(files,[output])
    p.storage.generate(contract,mapping,output,patched=True)
    environment = p.process.run([zig,'env'],capture_output=True,text=True,timeout=15,check=True).stdout
    lib = re.search(r'\.lib_dir = "([^"]+)"',environment)
    if lib is None:
        raise p.api.ContractError('compiler include location missing')
    cpp_headers = Path(lib.group(1))/'libcxx/include'
    results = []
    for rid in targets:
        directory = output/rid
        command = p.compile_command(source,config,lock,rid,zig)+['-I',str(p.LAYOUT),'-Wno-invalid-constexpr','-Wno-invalid-' + 'offsetof','-isystem',str(cpp_headers),'-DLAYOUT_PATCHED=1','-D_LIBCPP_HARDENING_MODE=_LIBCPP_HARDENING_MODE_NONE','-D_LIBCPP_ASSERTION_SEMANTIC_DEFAULT=_LIBCPP_ASSERTION_SEMANTIC_IGNORE']
        inputs = [str(output/'probe.cpp')]+[str(source/n) for n in lock['build']['sourceFiles'] if n not in ('imgui.cpp','src/PlaygroundBrutalAdapter.cpp')]
        exe = directory/('layout-probe.exe' if rid == 'win-x64' else 'layout-probe')
        p.api_check.logged(command+inputs+['-o',str(exe)],directory/'build.log')
        p.api_check.logged(command+['-DLAYOUT_ENFORCE_MANAGED=1','-c',str(output/'probe.cpp'),'-o',str(directory/'checked.o')],directory/'static.log')
        record = {'rid':rid,'staticOrdinaryStorageGate':True,'sourceRecordSha256':p.digest((selected/'source-record.json').read_bytes()),'configSha256':p.digest(config.read_bytes()),'executed':rid == 'osx-arm64'}
        if rid == 'osx-arm64':
            with (directory/'native.json').open('w') as stdout, (directory/'runtime.log').open('w') as stderr:
                p.process.run([str(exe)],stdout=stdout,stderr=stderr,timeout=30,check=True)
            report = p.storage.compare(contract,mapping,p.api.load(output/'interface.json'),p.api.load(directory/'native.json'))
            (directory/'comparison.json').write_bytes(p.encoded(report))
            p.require(report['ordinaryStorageParity'],'ordinary storage gate failed')
            record.update(comparisonSha256=p.digest(p.encoded(report)),knownConflicts=len(report['knownConflicts']))
        results.append(record)
    verify_source(selected)
    (output/'results.json').write_bytes(p.encoded(results))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--selected',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args = parser.parse_args()
    run(args.selected,args.output,['osx-arm64','linux-x64','win-x64'])
