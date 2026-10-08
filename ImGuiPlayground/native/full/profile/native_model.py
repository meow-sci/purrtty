"""Pinned-compiler AST extension, including CPP-local enum/field declarations."""
from __future__ import annotations

import importlib
import shlex
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

p = importlib.import_module('selected_profile')


def extract(source, output, body, zig='zig', ast_filter=None):
    safe_inputs.disjoint_output(output, [source])
    safe_inputs.tree(source)
    safe_inputs.fixed_inputs()
    files = p.declarations.output_files(output)
    p.preflight(files,[output])
    lock = p.builder.read_json(p.NATIVE/'source.lock.json')
    config = p.builder.verify_config_pin(lock)
    version = p.process.run([zig,'version'],capture_output=True,text=True,timeout=10,check=True).stdout.strip()
    p.require(version == '0.17.0','compiler pin mismatch')
    p.prepare(files,[output])
    tu = output/'declarations.cpp'
    tu.write_text(body)
    command = [zig,'c++','-###','-std=c++11','-fno-strict-aliasing','-target','aarch64-macos.11.0','-isysroot',p.builder.macos_sdk_path(),'-include',str(config),'-I',str(source),'-fsyntax-only',str(tu),'-o',str(output/'driver-unused.o')]
    run = p.process.run(command,capture_output=True,text=True,timeout=30)
    (output/'driver.log').write_text(run.stderr)
    errors = [line for line in run.stderr.splitlines() if 'error:' in line]
    p.require(run.returncode == 0 or (run.returncode == 1 and len(errors) == 1 and "error: failed to rename 'tmp/" in errors[0] and errors[0].endswith(': FileNotFound')),'unexpected Zig AST driver failure')
    candidates = [shlex.split(line) for line in run.stderr.splitlines() if line.lstrip().startswith('"') and '"-cc1"' in line]
    p.require(len(candidates) == 1,'ambiguous cc1 invocation')
    command = candidates[0]+['-ast-dump=json']
    if ast_filter:
        command += ['-ast-dump-filter',ast_filter]
    (output/'ast-command.json').write_bytes(p.encoded(command))
    with (output/'ast.json').open('w') as stdout, (output/'ast.log').open('w') as stderr:
        p.process.run(command,stdout=stdout,stderr=stderr,timeout=120,check=True)
    return read_ast(output/'ast.json')


def read_ast(path):
    safe_inputs.physical(path)
    # The CPP-local extended AST is larger than the ordinary API metadata bound.
    p.require(path.stat().st_size < 256*1024*1024,'extended AST too large')
    import json
    text = path.read_text()
    decoder = json.JSONDecoder()
    nodes = []
    offset = 0
    while offset < len(text):
        while offset < len(text) and text[offset].isspace():
            offset += 1
        if offset == len(text):
            break
        node, offset = decoder.raw_decode(text,offset)
        nodes.append(node)
    return nodes[0] if len(nodes) == 1 else {'kind':'TranslationUnitDecl','inner':nodes}


def declarations(source, output, zig='zig'):
    ast = extract(source,output,'#include "imgui.h"\n#include "imgui_internal.h"\n',zig)
    headers = {n:(source/n).read_text() for n in ('imgui.h','imgui_internal.h')}
    config = p.builder.verify_config_pin(p.builder.read_json(p.NATIVE/'source.lock.json'))
    result = {'schema':'playground_imgui.pinned-native-declarations','schemaVersion':1,'compiler':'Zig 0.17.0 embedded Clang AST','target':'aarch64-macos.11.0','headers':{n:p.digest(t.encode()) for n,t in headers.items()},'configSha256':p.digest(config.read_bytes()),'callbackTypes':p.declarations.callback_types(ast),'enumUnderlyingTypes':p.declarations.enum_underlying_types(ast),'declarations':p.declarations.extract(ast,headers)}
    (output/'declarations.json').write_bytes(p.encoded(result))
    return result


def source_location(node, sources):
    name = node.get('name','')
    offset = node.get('loc',{}).get('offset')
    line = node.get('loc',{}).get('line')
    found = [{'file':f,'line':t[:offset].count('\n')+1,'offset':offset,'token':name} for f,t in sources.items() if name and offset is not None and t[offset:offset+len(name)] == name and (line is None or t[:offset].count('\n')+1 == line)]
    p.require(len(found) <= 1,'ambiguous extended AST source: '+name)
    return found[0] if found else None


def harvest(ast, source):
    sources = {n:(source/n).read_text() for n in ('imgui.h','imgui_internal.h','imgui.cpp')}
    enums, typedefs, fields, aliases = {}, {}, [], {}
    def visit(node, scope=()):
        kind, name = node.get('kind'), node.get('name','')
        if kind == 'TypeAliasDecl' and name.startswith(('ProfileRepresentation','ProfileConstantRepresentation')):
            aliases[name] = node['type'].get('desugaredQualType',node['type']['qualType'])
        location = source_location(node,sources) if kind in ('EnumDecl','TypedefDecl','FieldDecl') else None
        if kind == 'EnumDecl' and location and any(n['kind']=='EnumConstantDecl' for n in node.get('inner',[])):
            constants = []
            value = -1
            for item in node['inner']:
                if item['kind'] != 'EnumConstantDecl':
                    continue
                def values(n):
                    if n.get('kind') == 'ConstantExpr':
                        return [int(n['value'])]
                    return [v for child in n.get('inner',[]) for v in values(child)]
                observed = values(item)
                p.require(len(observed) <= 1,'ambiguous constant expression')
                value = observed[0] if observed else value+1
                constants.append({'name':item['name'],'value':value,'source':source_location(item,sources)})
            enums[name] = {'kind':'true-enum','fixedUnderlying':node.get('fixedUnderlyingType'),'constants':constants,'source':location}
        if kind == 'TypedefDecl' and location:
            typedefs[name] = {'kind':'typedef','canonical':node['type'].get('desugaredQualType',node['type']['qualType']),'source':location}
        if kind == 'FieldDecl' and location:
            fields.append({'record':'::'.join(scope),'name':name,'declared':node['type']['qualType'],'canonical':node['type'].get('desugaredQualType',node['type']['qualType']),'bitfield':node.get('isBitfield',False),'source':location})
        if kind in ('NamespaceDecl','CXXRecordDecl') and name:
            scope += (name,)
        for child in node.get('inner',[]):
            visit(child,scope)
    visit(ast)
    return {'enums':enums,'typedefs':typedefs,'fields':fields,'representationAliases':aliases}
