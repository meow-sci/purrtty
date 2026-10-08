#!/usr/bin/env python3
"""Extract the pinned Zig Clang declaration AST, never parse C++ using name grep."""
from __future__ import annotations

import argparse
import importlib
import shlex
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
generate = importlib.import_module('generate')
output_paths = generate.output_paths
NATIVE = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(NATIVE.parents[1]))
process_utils = importlib.import_module('ImGuiPlayground.native.process_utils')
builder = importlib.import_module('ImGuiPlayground.native.build')


def extract(ast, headers):
    result = []

    def walk(node, scope=(), template=False):
        kind = node.get('kind')
        name = node.get('name', '')
        if kind in ('NamespaceDecl', 'CXXRecordDecl') and name:
            scope += (name,)
        template = template or kind in ('FunctionTemplateDecl', 'ClassTemplateDecl', 'ClassTemplateSpecializationDecl')
        if kind in ('CXXMethodDecl', 'FunctionDecl') and name and not template and not node.get('isImplicit'):
            loc = node.get('loc', {})
            offset = loc.get('offset')
            line = loc.get('line')
            # Clang elides repeated file names. Resolve source evidence by exact
            # token offset and line in the two pinned headers; ambiguity is fatal.
            matches = [f for f, text in headers.items() if offset is not None
                       and text[offset:offset+len(name)] == name
                       and (line is None or text[:offset].count('\n')+1 == line)]
            generate.require(len(matches) <= 1, 'ambiguous AST source location: '+name)
            # Out-of-line definitions are not new declarations. Their canonical
            # record declarations above are the authoritative scoped candidates.
            if matches and not (kind == 'CXXMethodDecl' and not scope):
                file = matches[0]
                result.append({'scope':'::'.join(scope), 'name':name, 'kind':kind,
                               'static':node.get('storageClass') == 'static',
                               'signature':node['type']['qualType'],
                               'parameters':[{'name':p.get('name'), 'type':p['type']['qualType']}
                                             for p in node.get('inner', []) if p['kind']=='ParmVarDecl'],
                               'file':file, 'line':headers[file][:offset].count('\n')+1,
                               'variadic':node.get('variadic', False)})
        for child in node.get('inner', []):
            walk(child, scope, template)

    walk(ast)
    return sorted(result, key=generate.declaration_key)


def callback_types(ast):
    names = {'ImDrawCallback', 'ImGuiInputTextCallback', 'ImGuiSizeCallback',
             'ImGuiMemAllocFunc', 'ImGuiMemFreeFunc'}
    result = {}

    def walk(node):
        if node.get('kind') == 'TypedefDecl' and node.get('name') in names:
            name = node['name']
            generate.require(name not in result, 'duplicate callback typedef: '+name)
            result[name] = node['type']['qualType']
        for child in node.get('inner', []):
            walk(child)

    walk(ast)
    generate.require(set(result) == names, 'missing native callback typedef')
    return result


def enum_underlying_types(ast):
    result = {}

    def walk(node):
        if node.get('kind') == 'EnumDecl' and node.get('name') and 'fixedUnderlyingType' in node:
            underlying = node['fixedUnderlyingType']
            value = {'spelling':underlying['qualType'],
                     'canonical':underlying.get('desugaredQualType', underlying['qualType'])}
            name = node['name']
            generate.require(name not in result or result[name] == value, 'conflicting enum underlying type: '+name)
            result[name] = value
        for child in node.get('inner', []):
            walk(child)

    walk(ast)
    return result


def output_files(output):
    output = output_paths.checked(output, 'directory')
    return [output/name for name in ('declarations.cpp', 'driver-unused.o', 'driver.log',
                                     'ast-command.json', 'ast.json', 'ast.log', 'declarations.json')]


def produce(source, output, zig='zig'):
    output = output_paths.checked(output, 'directory')
    files = output_files(output)
    protected = [source/'imgui.h', source/'imgui_internal.h', NATIVE/'source.lock.json']
    output_paths.preflight(files, [output], protected)
    lock = builder.read_json(NATIVE/'source.lock.json')
    builder.validate_lock(lock)
    config = builder.verify_config_pin(lock)
    version = process_utils.run([zig, 'version'], capture_output=True, text=True, timeout=10, check=True).stdout.strip()
    generate.require(version == '0.17.0', 'Zig pin mismatch')
    output_paths.prepare(files, [output], [*protected, config])
    tu = output/'declarations.cpp'
    tu.write_text('#include "imgui.h"\n#include "imgui_internal.h"\n')
    # The Zig driver tries to rename a nonexistent object after -ast-dump. Obtain
    # its exact cc1 invocation and execute that pinned embedded frontend directly.
    command = [zig, 'c++', '-###', '-std=c++11', '-target', 'aarch64-macos.11.0',
               '-isysroot', builder.macos_sdk_path(), '-include', str(config),
               '-I', str(source), '-fsyntax-only', str(tu), '-o', str(output/'driver-unused.o')]
    run = process_utils.run(command, capture_output=True, text=True, timeout=30, check=False)
    (output/'driver.log').write_text(run.stderr)
    errors = [line for line in run.stderr.splitlines() if 'error:' in line]
    generate.require(run.returncode == 0 or (run.returncode == 1 and len(errors) == 1
                     and "error: failed to rename 'tmp/" in errors[0] and errors[0].endswith(': FileNotFound')),
                     'unexpected Zig driver failure; see driver.log')
    candidates = [shlex.split(line) for line in run.stderr.splitlines() if line.lstrip().startswith('"') and '"-cc1"' in line]
    generate.require(len(candidates)==1, 'cannot obtain unambiguous pinned cc1 invocation')
    command = candidates[0] + ['-ast-dump=json']
    (output/'ast-command.json').write_bytes(generate.encoded(command))
    with (output/'ast.json').open('w') as stdout, (output/'ast.log').open('w') as stderr:
        process_utils.run(command, stdout=stdout, stderr=stderr, timeout=120, check=True)
    headers = {n:(source/n).read_text() for n in ('imgui.h', 'imgui_internal.h')}
    ast = generate.load(output/'ast.json')
    result = {'schema':'playground_imgui.pinned-native-declarations', 'schemaVersion':1,
              'compiler':'Zig 0.17.0 embedded Clang AST', 'target':'aarch64-macos.11.0',
              'headers':{n:generate.digest((source/n).read_bytes()) for n in headers},
              'configSha256':generate.digest(config.read_bytes()),
              'callbackTypes':callback_types(ast),
              'enumUnderlyingTypes':enum_underlying_types(ast),
              'declarations':extract(ast, headers)}
    (output/'declarations.json').write_bytes(generate.encoded(result))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--zig', default='zig')
    args = parser.parse_args()
    try:
        produce(args.source, args.output, args.zig)
    except (generate.ContractError, OSError, subprocess.SubprocessError, ValueError, KeyError) as error:
        parser.exit(1, 'declaration extraction failed: '+str(error)+'\n')


if __name__ == '__main__':
    main()
