"""Deterministic, fail-closed non-format wrapper emission. No build-on-import."""
from __future__ import annotations

import argparse
import hashlib
import importlib
import json
import re
import sys
from pathlib import Path

sys.dont_write_bytecode = True
output_paths = importlib.import_module('output_paths')
HERE = Path(__file__).resolve().parent
RESERVED = sorted(['BulletText', 'DebugLog', 'LabelText', 'LogText', 'SetItemTooltip', 'SetTooltip', 'Text', 'TextColored', 'TextDisabled', 'TextWrapped', 'TreeNode_1', 'TreeNode_2', 'TreeNodeEx_1', 'TreeNodeEx_2', 'TextAligned_internal', 'TextV'])
PODS = {'float2': 'PurrVec2', 'float3': 'PurrVec3', 'float4': 'PurrVec4', 'floatRect': 'PurrRect', 'int2': 'PurrInt2', 'int3': 'PurrInt3', 'int4': 'PurrInt4', 'ImTextureRef': 'PurrTextureRef', 'ImGuiListClipperRange': 'PurrClipperRange'}
PRIMITIVES = {'Void': 'void', 'Byte': 'uint8_t', 'Single': 'float', 'Double': 'double', 'Int32': 'int32_t', 'UInt32': 'uint32_t', 'UInt16': 'uint16_t', 'Int64': 'int64_t', 'UInt64': 'uint64_t', 'IntPtr': 'intptr_t', 'UIntPtr': 'uintptr_t', 'Bool8': 'uint8_t', 'ImGuiID': 'uint32_t', 'ImFontAtlasRectId': 'int32_t', 'ImTextureID': 'intptr_t', 'ImGuiKeyChord': 'int32_t'}

class ContractError(RuntimeError):
    pass

def require(condition, message):
    if not condition:
        raise ContractError(message)

def digest(data):
    return hashlib.sha256(data).hexdigest()

def encoded(value):
    return (json.dumps(value, sort_keys=True, indent=2, ensure_ascii=True) + '\n').encode()

def load(path):

    def unique(pairs):
        result = {}
        for (k, v) in pairs:
            require(k not in result, 'duplicate JSON key: ' + k)
            result[k] = v
        return result
    require(path.stat().st_size <= 128 * 1024 * 1024, 'input exceeds bound')
    return json.loads(path.read_text(), object_pairs_hook=unique)
# The portable projection omits observations, trace IDs, and assembly identities.
# Attribute identities retain their full type name, not runtime version/hash.
OMIT = {'host', 'counts', 'limits', 'limitations', 'assemblies', 'dependencyScope', 'exporterVersion', 'metadataToken', 'moduleMvid', 'assemblyQualifiedName', 'assembly', 'bindingAssembly', 'measurement', 'marshalerMeasurement', 'runtimeOffset', 'marshalerOffset', 'storageWidth', 'overlaps', 'refReturnProperties', 'nativeDisposition', 'resolutionSource', 'source'}

def structural(value):
    if isinstance(value, dict):
        return {k: structural(v) for (k, v) in value.items() if k not in OMIT}
    if isinstance(value, list):
        return [structural(v) for v in value]
    if isinstance(value, str) and ', Version=' in value:
        return value.split(',')[0]
    return value

def projection(contract):
    return {'schema': 'playground_imgui.portable-api-projection', 'schemaVersion': 1, 'imports': structural(contract['imports']), 'types': structural(contract['types']), 'dynamicExports': structural(contract['dynamicExports'])}

def short(t):
    return t.split('::')[-1].split('.')[-1]

def abi_type(t, types):
    if t.endswith('*'):
        if 'ImVector`1' in t:
            return 'void*'
        return abi_type(t[:-1], types) + '*'
    s = short(t)
    if s in PODS:
        return PODS[s]
    if s in PRIMITIVES:
        return PRIMITIVES[s]
    require(t in types, 'missing structural type: ' + t)
    if types[t]['kind'] == 'enum':
        require(types[t]['scalar'] == {'underlyingType':'System.Private.CoreLib::System.Int32',
                                      'signedness':'signed', 'pointerSized':False},
                'unsupported enum representation: '+t)
        return 'int32_t'
    require(t.startswith('Brutal.ImGui::') and re.fullmatch('Im\\w+', s), 'unsupported ABI type: ' + t)
    return s

def validate_contract(c, mapping):
    require(c.get('schema') == 'playground_imgui.managed-binding-contract' and c.get('schemaVersion') == 1, 'unsupported contract schema')
    require(c['host']['pointerSizeBytes'] == 8 and c['host']['endianness'] == 'little', 'unsupported host representation')
    selected = {a['name']: {'sha256':a['sha256'], 'moduleMvid':a['moduleMvid']}
                for a in c['assemblies'] if a['resolutionSource'] == 'selected-directory'}
    require(selected == mapping['selectedAssemblies'], 'selected BRUTAL assembly provenance mismatch')
    imports = c['imports']
    names = [m['entryPoint'] for m in imports]
    require(len(names) == len(set(names)) == 1146, 'missing or duplicate imports')
    require(sorted(names) == sorted([m['entryPoint'] for m in mapping['mappings']] + RESERVED), 'import ownership mismatch')
    type_ids = [t['id'] for t in c['types']]
    require(len(type_ids) == len(set(type_ids)), 'duplicate structural types')
    for m in imports:
        require(m['library'] == 'imgui' and re.fullmatch('[A-Za-z_]\\w*', m['entryPoint']), 'invalid import')
        require(m['dllImport'] == mapping['dllImportPolicy'], 'unsupported import marshalling: ' + m['entryPoint'])
        for p in [m['return']] + m['parameters']:
            require(p['attributes'] == [] and p['requiredModifiers'] == [] and (p['optionalModifiers'] == []) and not any(p[k] for k in ('isIn', 'isOut', 'isOptional', 'hasDefaultValue')) and (p['parameterAttributes'] == 'None'), 'unsupported parameter marshalling: ' + m['entryPoint'])
    require(digest(encoded(projection(c))) == mapping['portableProjectionSha256'], 'portable structural ABI projection mismatch')

def declaration_key(d):
    return (d['scope'], d['name'], d['signature'], d['kind'], d['static'])

def conversion(managed, native, name, types, is_return=False, bool_direction=None):
    """Finite type-directed conversions, checked again against owned mapping evidence."""
    a = abi_type(managed, types)
    s = short(managed)
    ns = native.strip()
    base = ns.replace('const ', '').replace(' *', '*').replace(' &', '&')
    expr = name
    if is_return:
        if a == 'void':
            return (name, 'void')
        if s == 'Bool8':
            return ('uint8_t(' + name + ' ? 1 : 0)', 'bool-to-Bool8-normalized')
        if s in PODS:
            return ('purr::from_native(' + name + ')', 'aggregate-fieldwise-return')
        if '*' in a:
            expr = '&(' + name + ')' if ns.endswith('&') else name
            return ('reinterpret_cast<' + a + '>(const_cast<void*>(static_cast<const void*>(' + expr + ')))', 'stable-native-reference-address' if ns.endswith('&') else 'stable-native-pointer')
        if s == 'ImTextureID':
            return ('purr::bits<intptr_t>(' + name + ')', 'texture-id-bit-pattern')
        return ('static_cast<' + a + '>(' + name + ')', 'fixed-width-scalar-return')
    if s == 'Bool8*':
        require(ns == 'bool *' and bool_direction in ('inout', 'out'), 'missing/unsupported Bool8 pointer direction')
        if bool_direction == 'out':
            return ('purr::bool_output(' + name + ')', 'Bool8-output-address-view-no-entry-access')
        return ('purr::bool_inout(' + name + ')', 'Bool8-inout-address-view-in-place-normalization')
    if s == 'Bool8':
        return ('(' + name + ' != 0)', 'Bool8-to-bool-normalized')
    if s in PODS:
        require(base.rstrip('&') in ('ImVec2', 'ImVec4', 'ImRect', 'ImTextureRef'), 'unsupported value POD conversion')
        return ('purr::to_native(' + name + ')', 'aggregate-fieldwise-value')
    if s == 'ImTextureRef*':
        require(ns in ('const ImTextureRef &', 'ImTextureRef *'), 'unsupported texture pointer')
        return ('purr::to_native(*' + name + ')', 'texture-fieldwise-read')
    if 'System.IntPtr' in managed:
        if s == 'IntPtr*':
            require(ns in ('ImGuiMemAllocFunc *', 'ImGuiMemFreeFunc *'), 'unknown callback output')
            return ('&cb_' + name, 'typed-callback-output-bit-copy')
        require('(*' in ns or ns in ('ImDrawCallback', 'ImGuiInputTextCallback', 'ImGuiSizeCallback', 'ImGuiMemAllocFunc', 'ImGuiMemFreeFunc'), 'unknown opaque callback')
        return ('reinterpret_cast<' + ns + '>(static_cast<uintptr_t>(' + name + '))', 'typed-upstream-callback-address')
    if s == 'ImTextureID':
        return ('purr::bits<ImTextureID>(' + name + ')', 'texture-id-bit-pattern')
    if ns == 'ImGuiSelectionUserData':
        return ('purr::bits<ImGuiSelectionUserData>(' + name + ')', 'selection-user-data-bit-pattern')
    if managed.endswith('*'):
        if s == 'Byte**' and ns == 'const char **':
            return ('static_cast<const char **>(static_cast<void*>(' + name + '))', 'byte-pointer-output-preserve-address')
        if ns.endswith('&'):
            target = ns[:-1].strip() + '*'
            return ('*reinterpret_cast<' + target + '>(' + name + ')', 'proven-POD-reference-view' if short(managed[:-1]) in PODS else 'native-object-reference')
        if '*' in ns:
            return ('reinterpret_cast<' + ns + '>(' + name + ')', 'proven-POD-pointer-array-view' if short(managed[:-1]) in PODS else 'native-pointer-preserve-null-address')
        if base in ('ImVec2', 'ImVec4', 'ImRect'):
            return ('purr::to_native(*' + name + ')', 'aggregate-fieldwise-dereferenced-value')
        raise ContractError('unsupported pointer adaptation: ' + managed + ' -> ' + ns)
    require('*' not in ns and '&' not in ns, 'unsupported scalar adaptation')
    return ('static_cast<' + ns + '>(' + name + ')', 'fixed-width-scalar')

def render(m, types):
    d = m['native']
    pars = m['managedParameters']
    member = m['memberTarget']
    offset = int(member)
    ret = d['signature'].split('(')[0].strip()
    native_params = [p['type'] for p in d['parameters']]
    suffix = d['signature'][d['signature'].rfind(')') + 1:]
    lines = []
    conversions = []
    args = []
    bool_parameters = m['boolPointerParameters']
    require([b['parameter'] for b in bool_parameters] == [i for i,p in enumerate(pars) if short(p) == 'Bool8*'],
            'missing/duplicate Bool8 pointer direction records')
    for b in bool_parameters:
        require(b['direction'] in ('inout', 'out') and b['nullability'] in ('optional', 'required')
                and b['lifetime'] == 'borrowed-native-call' and bool(b['sourceEvidence'])
                and b['aliasPolicy'] == 'original address preserved, including callbacks/UserData and global allocator callback aliases; native write ordering retained'
                and b['sharedWritePolicy'] == 'caller/callback writes must be canonical 0/1 while shared with native bool execution',
                'unsupported Bool8 pointer policy')
        require(b['name'] == d['parameters'][b['parameter'] - offset]['name'], 'Bool8 pointer name mismatch')
    bool_by_index = {b['parameter']:b for b in bool_parameters}
    require(len(pars) - offset == len(native_params), 'native parameter count mismatch')
    for i in range(offset, len(pars)):
        p, n = pars[i], native_params[i - offset]
        if short(p) == 'IntPtr*':
            lines.append(n.rstrip(' *') + ' cb_a' + str(i) + ' = nullptr;')
        (expr, rule) = conversion(p, n, 'a' + str(i), types,
                                  bool_direction=bool_by_index.get(i, {}).get('direction'))
        if m['entryPoint'] == 'GetAllocatorFunctions' and i == 2:
            lines.append('void* cb_a2 = nullptr;')
            expr, rule = '&cb_a2', 'allocator-user-data-output-ordered-copy'
        args.append(expr)
        conversions.append({'parameter': i, 'managed': p, 'abi': abi_type(p, types), 'native': n, 'rule': rule, 'expression': expr})
    pointer = '*'
    if member and (not d['static']):
        pointer = d['scope'] + '::*'
    lines.append('using NativeCall = ' + ret + ' (' + pointer + ')(' + ', '.join(native_params) + ')' + suffix + ';')
    lines.append('const NativeCall native_call = static_cast<NativeCall>(&' + d['scope'] + '::' + d['name'] + ');')
    callee = 'native_call'
    if member:
        if d['static']:
            lines.append('(void)a0; // Synthetic managed target of a static factory: never dereference.')
            conversions.insert(0, {'parameter': 0, 'rule': 'synthetic-unused-static-target', 'abi': abi_type(pars[0], types)})
        else:
            if short(pars[0]) == 'ImTextureRef*':
                lines.append('ImTextureRef target = purr::to_native(*a0);')
                target = '&target'
                rule = 'texture-fieldwise-read'
            else:
                target = 'reinterpret_cast<' + d['scope'] + '*>(a0)'
                rule = 'proven-POD-member-view' if short(pars[0][:-1]) in PODS else 'native-object-identity'
            conversions.insert(0, {'parameter': 0, 'rule': rule, 'abi': abi_type(pars[0], types)})
            callee = '(' + target + '->*native_call)' if target != '&target' else '((&target)->*native_call)'
    call = callee + '(' + ', '.join(args) + ')'
    if ret == 'void':
        lines.append(call + ';')
    else:
        lines.append(('auto&' if ret.endswith('&') else 'auto') + ' result = ' + call + ';')
    for (i, p) in enumerate(pars):
        if short(p) == 'IntPtr*':
            lines.append('*a' + str(i) + ' = purr::bits<intptr_t>(cb_a' + str(i) + ');')
    if m['entryPoint'] == 'GetAllocatorFunctions':
        lines.append('*a2 = cb_a2; // Preserve native alloc/free/user write order, including aliases.')
    (result_expr, result_rule) = conversion(m['managedReturn'], ret, 'result', types, True)
    if ret != 'void':
        lines.append('return ' + result_expr + ';')
    signature = abi_type(m['managedReturn'], types) + ' ' + m['entryPoint'] + '(' + ', '.join(abi_type(p, types) + ' a' + str(i) for (i, p) in enumerate(pars)) + ')'
    text = '// ' + d['file'] + ':' + str(d['line']) + ' ' + d['scope'] + '::' + d['name'] + ' ' + d['signature'] + '\nPURR_API ' + signature + '\n{\n' + ''.join('    ' + line + '\n' for line in lines) + '}\n'
    return (text, {'entryPoint': m['entryPoint'], 'managedReturn': m['managedReturn'], 'abiSignature': signature, 'target': d, 'conversions': conversions, 'returnConversion': result_rule, 'boolPointerParameters': bool_parameters})

def generate(contract, declarations, mapping):
    require(mapping.get('schema') == 'playground_imgui.nonformat-mapping' and mapping.get('schemaVersion') == 1, 'unsupported mapping schema')
    require(declarations.get('schema') == 'playground_imgui.pinned-native-declarations' and declarations.get('schemaVersion') == 1, 'unsupported native declaration schema')
    require(declarations['callbackTypes'] == mapping['callbackTypes'], 'native callback signature mutation')
    require(declarations['enumUnderlyingTypes'] == mapping['enumUnderlyingTypes'], 'native enum underlying type mutation')
    validate_contract(contract, mapping)
    rows = mapping['mappings']
    require(len(rows) == 1130 and len({m['entryPoint'] for m in rows}) == 1130, 'mapping missing/duplicate')
    decls = {}
    for d in declarations['declarations']:
        key = declaration_key(d)
        require(key not in decls, 'ambiguous duplicate native declaration: ' + str(key))
        decls[key] = d
    require(declarations['headers'] == mapping['headers'] and declarations['configSha256'] == mapping['configSha256'], 'native declaration inputs changed')
    types = {t['id']: t for t in contract['types']}
    imports = {m['entryPoint']: m for m in contract['imports']}
    source = ['// Generated by full/api/generate.py; do not edit.\n#include "purr_abi.h"\n']
    imported_types = {p['type'].rstrip('*') for m in contract['imports'] for p in [m['return']] + m['parameters']}
    for type_id in sorted(imported_types):
        if types.get(type_id, {}).get('kind') == 'enum':
            native_name = short(type_id)
            underlying = mapping['enumUnderlyingTypes'].get(native_name, {}).get('canonical', 'int')
            require(underlying in ('int', 'unsigned char'), 'unsupported selected native enum representation')
            source.append('static_assert(sizeof('+native_name+') == sizeof('+underlying+') && alignof('+native_name+') == alignof('+underlying+'), "selected-header enum width/alignment");\n')
            if native_name in mapping['enumUnderlyingTypes']:
                source.append('static_assert(__is_same(__underlying_type('+native_name+'), '+underlying+'), "selected-header enum signedness");\n')
    evidence = []
    for m in rows:
        observed = imports[m['entryPoint']]
        require([p['type'] for p in observed['parameters']] == m['managedParameters'] and observed['return']['type'] == m['managedReturn'], 'signature mutation: ' + m['entryPoint'])
        require(declaration_key(m['native']) in decls and decls[declaration_key(m['native'])] == m['native'], 'missing/changed native declaration: ' + m['entryPoint'])
        require(not m['native']['variadic'], 'variadic import outside reserved set')
        require([p['name'] for p in observed['parameters'][int(m['memberTarget']):]] ==
                [p['name'] for p in m['native']['parameters']], 'ordered parameter names differ from native declaration')
        callback_indices = [i for i,p in enumerate(m['managedParameters']) if 'System.IntPtr' in p]
        contracts = m['callbackContracts']
        require([p['parameter'] for p in contracts] == callback_indices, 'missing/duplicate callback contracts')
        for callback in contracts:
            native = m['native']['parameters'][callback['parameter'] - int(m['memberTarget'])]['type']
            underlying = mapping['callbackTypes'].get(native.rstrip(' *'), native)
            require(callback['nativeParameterType'] == native and callback['signature'] == underlying
                    and bool(callback['lifetime']), 'opaque callback mapping mismatch')
        (text, record) = render(m, types)
        require(record == m['evidence'], 'conversion evidence changed: ' + m['entryPoint'])
        source.append(text)
        record = dict(record, callbackContracts=contracts)
        evidence.append(record)
    manifest = {'schema': 'playground_imgui.nonformat-coverage', 'schemaVersion': 1, 'inputContractSha256': None, 'portableProjectionSha256': mapping['portableProjectionSha256'], 'headers': mapping['headers'], 'configSha256': mapping['configSha256'], 'callbackTypes': mapping['callbackTypes'], 'enumUnderlyingTypes': mapping['enumUnderlyingTypes'], 'scalarAdaptations': mapping['scalarAdaptations'], 'selectedAssemblies': mapping['selectedAssemblies'], 'implementedCount': 1130, 'reservedCount': 16, 'importCount': 1146, 'implemented': evidence, 'reserved': [{'entryPoint': n, 'owner': 'manual-format-adapter', 'implemented': False} for n in RESERVED], 'dynamicExports': [dict(x, owner='manual-bridge-adapter', implemented=False) for x in contract['dynamicExports']], 'requiredAllTranslationUnitFlags': ['-fno-strict-aliasing'], 'qualification': 'compiled forwarding coverage only; layout and target execution are independent gates'}
    return (''.join(source).encode(), manifest)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--declarations', type=Path, required=True)
    parser.add_argument('--mapping', type=Path, default=HERE / 'mapping.json')
    parser.add_argument('--output', type=Path, required=True)
    a = parser.parse_args()
    try:
        output = output_paths.checked(a.output, 'directory')
        files = [output/'wrappers.cpp', output/'coverage.json']
        protected = [a.contract, a.declarations, a.mapping, HERE/'purr_abi.h']
        output_paths.preflight(files, [output], protected)
        (source, coverage) = generate(load(a.contract), load(a.declarations), load(a.mapping))
        coverage['inputContractSha256'] = digest(a.contract.read_bytes())
        coverage['generatorSha256'] = digest(Path(__file__).read_bytes())
        coverage['mappingSha256'] = digest(a.mapping.read_bytes())
        coverage['commonHeaderSha256'] = digest((HERE / 'purr_abi.h').read_bytes())
        coverage['wrapperSha256'] = digest(source)
        output_paths.prepare(files, [output], protected)
        (output / 'wrappers.cpp').write_bytes(source)
        (output / 'coverage.json').write_bytes(encoded(coverage))
    except (ContractError, ValueError, KeyError, TypeError, OSError) as error:
        parser.exit(1, 'generation failed: ' + str(error) + '\n')
if __name__ == '__main__':
    main()
