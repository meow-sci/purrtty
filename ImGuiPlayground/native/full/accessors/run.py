#!/usr/bin/env python3
"""Maintainer-only pinned native accessor fixture build; never production promotion."""
from __future__ import annotations

import argparse
import importlib.util
import json
import re
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
NATIVE = HERE.parents[1]
PROJECT = NATIVE.parent
generator = paths.load_module('purr_accessor_generate', HERE / 'generate.py')
patch = generator.patch
LayoutError, digest, prepare_source, reject_symlinks = patch.LayoutError, patch.digest, patch.prepare_source, paths.checked_path
process_utils = paths.load_module('purr_accessor_process_utils', NATIVE / 'process_utils.py')


def new_output(path: Path, *, protected: tuple[Path, ...] = ()) -> Path:
    """Validate the whole new write tree before consuming selected inputs."""
    absolute = paths.output_location(path, PROJECT / '.tmp/native/accessors', protected)
    if absolute.exists():
        raise LayoutError('new output directory required')
    return absolute


def logged(command: list[str], log: Path, timeout: float = 240) -> None:
    with log.open('w') as stream:
        stream.write(json.dumps(command) + '\n')
        stream.flush()
        result = process_utils.run(command, stdout=stream, stderr=stream, timeout=timeout)
    if result.returncode:
        raise LayoutError(f'command failed ({result.returncode}); retained log {log}')


def verify_tree(source: Path, hashes: dict) -> None:
    for p in [source, *source.rglob('*')]:
        reject_symlinks(p)
    actual = {str(p.relative_to(source)):digest(p.read_bytes()) for p in source.rglob('*') if p.is_file()}
    if actual != hashes:
        raise LayoutError('source tree membership/hash changed')


def build(output: Path, targets: list[str], zig: str) -> None:
    paths.preflight_component(HERE)
    output = new_output(output)
    if not targets or len(set(targets)) != len(targets) or any(rid not in ('osx-arm64','linux-x64','win-x64') for rid in targets):
        raise LayoutError('a nonempty unique list of closed target IDs is required')
    contract = PROJECT / '.tmp/native/binding-contract.json'
    archive = PROJECT / '.tmp/native/imgui-031a18c417158427217bc5890e0ec0cb7e7b4b63.tar.gz'
    for path in [contract, archive, NATIVE/'source.lock.json',
                 *[HERE/name for name in ('accessors.cpp', 'accessors.h', 'fixture.cpp', 'fixture.h')]]:
        paths.input_file(path)
    lock = generator.verify_source_lock((NATIVE/'source.lock.json').read_bytes())
    config = NATIVE / lock['configuration']['header']
    paths.input_file(config)
    if digest(config.read_bytes()) != lock['configuration']['sha256']:
        raise LayoutError('config hash mismatch')
    compiler = shutil.which(zig)
    if not compiler:
        raise LayoutError('Zig unavailable')
    version = process_utils.run([compiler,'version'],capture_output=True,text=True,check=True,timeout=15).stdout.strip()
    if version != lock['toolchain']['zigVersion']:
        raise LayoutError('wrong Zig version')
    generator.layout.load_inputs(contract)
    output.mkdir(parents=True)
    source=output/'source'
    source_record=prepare_source(archive,source)
    (output/'source-record.json').write_text(json.dumps(source_record,indent=2)+'\n')
    manifest=generator.generate(contract,output)
    exports={}
    for name in ['accessors','fixture']:
        text=(HERE/(name+'.h')).read_text()
        exports[name]=re.findall(r'PURR_ACCESSOR_EXPORT\s+(.+?\bpurr_\w+\([^;]*\));',text)
    if len(exports['accessors']) != 23 or len(exports['fixture']) != 19:
        raise LayoutError('closed production23/fixture19 export inventory changed')
    (output/'exports.json').write_text(json.dumps({'schema':'purr.accessor-exports','version':1,'production':exports['accessors'],'testOnly':exports['fixture']},indent=2)+'\n')
    (output/'header.c').write_text('#include "accessors.h"\n_Static_assert(sizeof(PurrFieldInfo)==24,"info");\n_Static_assert(sizeof(PurrTriple)==12,"triple");\n_Static_assert(sizeof(PurrSharedSnapshot)==20,"shared");\n_Static_assert(sizeof(PurrTextEditSnapshot)==16,"textedit");\n_Static_assert(sizeof(PurrCellSnapshot)==8,"cell");\n')
    compile_database=[]
    for rid in targets:
        folder=output/rid
        folder.mkdir()
        artifact=folder/lock['targets'][rid]['file']
        common=[compiler,'c++','-std=c++11','-O2','-DNDEBUG','-fno-exceptions','-fno-rtti','-fno-threadsafe-statics','-fno-strict-aliasing','-nostdlib++','-fvisibility=hidden','-target',lock['targets'][rid]['zigTarget'],'-I',str(source),'-I',str(HERE),'-I',str(output),'-include',str(config)]
        if rid=='osx-arm64':
            sdk=process_utils.run(['xcrun','--sdk','macosx','--show-sdk-path'],capture_output=True,text=True,check=True,timeout=15).stdout.strip()
            common+=['-isysroot',sdk,'-mmacosx-version-min=11.0']
        # fixture.cpp includes actual imgui.cpp once to see CPP-local const table.
        sources=[HERE/'accessors.cpp',HERE/'fixture.cpp']+[source/n for n in lock['build']['sourceFiles'] if n not in ('imgui.cpp','src/PlaygroundBrutalAdapter.cpp')]
        command=common+['-shared']+[str(p) for p in sources]+['-o',str(artifact)]
        verify_tree(source,source_record['sourceFiles'])
        logged(command,folder/'build.log')
        ccommand=[compiler,'cc','-std=c11','-fno-strict-aliasing','-target',lock['targets'][rid]['zigTarget'],'-I',str(HERE),'-c',str(output/'header.c'),'-o',str(folder/'header.o')]
        logged(ccommand,folder/'header.log')
        symbols_command = (['objdump','-p',str(artifact)] if rid=='win-x64' else ['nm','-gU',str(artifact)] if rid=='osx-arm64' else ['nm','--dynamic','--defined-only',str(artifact)])
        symbols = process_utils.run(symbols_command,capture_output=True,text=True,check=True,timeout=30)
        (folder/'symbols.log').write_text(json.dumps(symbols_command)+'\n'+symbols.stdout+symbols.stderr)
        function_kinds={}
        if rid=='win-x64':
            sections_command=['objdump','-h',str(artifact)]
            sections=process_utils.run(sections_command,capture_output=True,text=True,check=True,timeout=30)
            (folder/'symbol-sections.log').write_text(json.dumps(sections_command)+'\n'+sections.stdout+sections.stderr)
            image_base=re.search(r'^ImageBase\s+([0-9a-fA-F]+)$',symbols.stdout,re.MULTILINE)
            if image_base is None:
                raise LayoutError('PE image base missing from symbol evidence')
            text_ranges=[]
            for line in sections.stdout.splitlines():
                parts=line.split()
                if len(parts)==5 and parts[0].isdigit() and parts[4]=='TEXT':
                    start=int(parts[3],16)-int(image_base.group(1),16)
                    text_ranges.append((start,start+int(parts[2],16)))
            if not text_ranges:
                raise LayoutError('PE executable TEXT section missing')
            names=[]
            in_exports=False
            for line in symbols.stdout.splitlines():
                if 'Export Table:' in line:
                    in_exports=True
                elif in_exports:
                    parts=line.split()
                    if len(parts)==3 and parts[0].isdigit() and parts[1].startswith('0x'):
                        names.append(parts[2])
                        if parts[2].startswith('purr_'):
                            rva=int(parts[1],16)
                            if not any(start<=rva<end for start,end in text_ranges):
                                raise LayoutError('accessor export is not executable code: '+parts[2])
                            function_kinds[parts[2]]='PE export RVA in TEXT section'
        else:
            names=[]
            for line in symbols.stdout.splitlines():
                parts=line.split()
                if len(parts)!=3:
                    continue
                name=parts[2][1:] if rid=='osx-arm64' else parts[2]
                names.append(name)
                if name.startswith('purr_'):
                    if parts[1]!='T':
                        raise LayoutError('accessor export is not a defined external text symbol: '+name)
                    function_kinds[name]='T'
        expected_names=[]
        for prototype in exports['accessors']+exports['fixture']:
            matches=re.findall(r'\bpurr_\w+(?=\()',prototype)
            if len(matches)!=1:
                raise LayoutError('invalid export prototype')
            expected_names.append(matches[0])
        expected_names.sort()
        actual_names=sorted(name for name in names if name.startswith('purr_'))
        if actual_names!=expected_names:
            raise LayoutError('missing/extra/duplicate native accessor/fixture export: '+rid)
        for p in sources:
            compile_database.append({'directory':str(output),'file':str(p),'arguments':common+['-c',str(p)]})
        record={'schema':'purr.accessor-build','version':1,'rid':rid,'command':command,'c11Command':ccommand,'artifactSha256':digest(artifact.read_bytes()),'compilerSha256':digest(Path(compiler).read_bytes()),'zigVersion':version,'configSha256':digest(config.read_bytes()),'sourceRecordSha256':digest((output/'source-record.json').read_bytes()),'identity':manifest['identity'],'componentHashes':{str(p.relative_to(HERE)):digest(p.read_bytes()) for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts},'compiledExports':actual_names,'compiledExportKinds':function_kinds,'symbolsCommand':symbols_command,'executed':False,'executionStatus':'cross-build only; macOS managed/native execution is a separate explicit gate'}
        (folder/'build-record.json').write_text(json.dumps(record,indent=2)+'\n')
    (output/'compile_commands.json').write_text(json.dumps(compile_database,indent=2)+'\n')
    verify_tree(source,source_record['sourceFiles'])


def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--targets',nargs='+',choices=['osx-arm64','linux-x64','win-x64'],default=['osx-arm64','linux-x64','win-x64'])
    parser.add_argument('--zig',default='zig')
    args=parser.parse_args()
    build(args.out,args.targets,args.zig)


if __name__=='__main__':
    try:
        main()
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr)
        sys.exit(1)
