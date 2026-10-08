#!/usr/bin/env python3
"""Closed enum/value/use join and separately classified typed/underlying-bit fixtures."""
from __future__ import annotations

import argparse
import importlib
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
native_model = importlib.import_module('native_model')
layout_gate = importlib.import_module('layout_gate')
use_graph = importlib.import_module('enum_uses')
POLICY_SHA256 = '6a8c6b970f7dcd905cfac12b5a223db7afd5cdc5dcf30e7bb64d5ddeaa8ff6bc'


def load_policy():
    path = p.HERE/'enum-policy.json'
    p.require(p.digest(path.read_bytes()) == POLICY_SHA256,'closed enum/domain policy changed')
    return p.api.load(path)


def validate(policy, contract, layout, model, mapping, reverse):
    types = {t['id']:t for t in contract['types'] if t['kind']=='enum'}
    mapped = {t['id']:t for t in layout['types'] if t['id'] in types}
    rows = policy['types']
    p.require(len(rows) == 85 and len({t['type'] for t in rows}) == 85,'enum type omission/duplicate')
    p.require({t['type'] for t in rows} == {k for k,v in mapped.items() if v['native']},'independent enum inventory mismatch')
    p.require(policy['excluded'] == ['System.Private.CoreLib::System.TypeCode'],'unapproved excluded enum')
    p.require(set(types) == {t['type'] for t in rows}|set(policy['excluded']),'unclassified enum')
    constants = {v['name']:(e,v) for e,d in model['enums'].items() for v in d['constants']}
    exceptions = p.storage.read_pinned('enum-transport.json')['exceptions']
    nominal = use_graph.roots(policy, contract, layout, model)
    representations = {t['native']:t['representation'] for t in rows}
    result = []
    for index, t in enumerate(rows):
        ct = types[t['type']]
        p.require(t['native'] == mapped[t['type']]['native'],'native enum map changed')
        p.require(ct['scalar']['signedness'] == 'signed' and ct['scalar']['underlyingType'].endswith('::System.Int32'),'managed enum backing changed')
        values = t['values']
        p.require(len(values) == len(ct['enumValues']) and {v['managed']:str(v['value']) for v in values} == {v['name']:v['value'] for v in ct['enumValues']},'managed named-value omission/mutation')
        kind = 'typedef' if t['native'] in model['typedefs'] else 'true-enum'
        p.require(t['kind'] == kind,'enum versus typedef misclassification')
        p.require(t['representation'] == model['representationAliases']['ProfileRepresentation'+str(index)],'native representation/signedness changed')
        p.require(t['representation'] in ('int','unsigned int'),'unsupported enum representation')
        p.require(t['storage'] in model['typedefs'] or t['storage'] == t['native'],'unapproved storage type')
        if t['storage'] != t['native']:
            p.require(model['typedefs'][t['storage']]['canonical'] == 'int','private constants cannot replace signed typedef storage')
        declared = model['enums'][t['constantsDeclaration']]
        declared_rep = model['representationAliases']['ProfileConstantRepresentation'+str(index)]
        named = []
        for v in values:
            p.require(v['native'] in constants and constants[v['native']][1]['value'] == v['value'],'native named value absent/mutated')
            owner, native = constants[v['native']]
            p.require(owner == t['constantsDeclaration'] or ('Private_' in t['native'] and owner == t['storage']+'_'),'unapproved constant declaration association')
            named.append(dict(v,source=native['source'],constantsOwner=owner))
        combos = []
        by_name = {v['managed']:v for v in values}
        for names in t['combinations']:
            p.require(len(names) >= 2 and len(names) == len(set(names)) and all(n in by_name for n in names),'invalid combination domain')
            p.require(all(by_name[n]['value'] > 0 and by_name[n]['value'] & (by_name[n]['value']-1) == 0 for n in names),'combination must use named positive independent bits')
            value = 0
            for name in names:
                value |= by_name[name]['value']
            combos.append({'names':names,'value':value,'nativeExpression':' | '.join(by_name[n]['native'] for n in names)})
        uses = []
        for m in mapping['mappings']:
            d = m['native']
            native_types = [('return',d['signature'].split('(')[0].strip(),m['managedReturn'])]+[(str(i+int(m['memberTarget'])),v['type'],m['managedParameters'][i+int(m['memberTarget'])]) for i,v in enumerate(d['parameters'])]
            for position, native, managed in native_types:
                leaf = use_graph.associate(native, native, nominal, model, representations)
                if leaf and t['type'] in nominal[leaf['leaf']]:
                    leaf['canonicalLeaf'] = model['typedefs'].get(leaf['leaf'],{}).get('canonical',leaf['leaf'])
                    uses.append({'entryPoint':m['entryPoint'],'position':position,'native':native,'managed':managed,'source':{'file':d['file'],'line':d['line']},'leafStorage':leaf})
        reserved = [{'entryPoint':m['entryPoint'],'position':('return' if i == 0 else str(i-1)),'managed':v['type'],'disposition':'reserved import, not implemented by profile'} for m in contract['imports'] if m['entryPoint'] in p.api.RESERVED for i,v in enumerate([m['return']]+m['parameters']) if v['type'].rstrip('*') == t['type']]
        fields = []
        for field in model['fields']:
            leaf = use_graph.associate(field['declared'],field['canonical'],nominal,model,representations)
            if leaf and t['type'] in nominal[leaf['leaf']]:
                fields.append(dict(field,leafStorage=leaf))
        managed_fields = []
        all_types = {v['id']:v for v in contract['types']}
        related_fields = {(f['record'],f['name']):f for f in fields}
        for parent in layout['types']:
            observed = {f['name']:f for f in all_types[parent['id']].get('fields',[])}
            for field in parent['fields']:
                managed_leaf, relationships = use_graph.managed_leaf(observed[field['name']]['type'],all_types)
                key = (parent['native'],field.get('member'))
                if managed_leaf != t['type'] and key not in related_fields:
                    continue
                if field.get('member') == '$self':
                    p.require(parent['native'] == 'ImGuiKeyChord' and model['typedefs']['ImGuiKeyChord']['canonical'] == 'int','unapproved enum-associated scalar wrapper')
                    managed_fields.append({'parent':parent['id'],'field':field['name'],'native':'ImGuiKeyChord','disposition':'signed-int scalar wrapper; key/modifier bit combination, not constants enum backing'})
                    continue
                p.require(key in related_fields,'independent managed/native field join omission: '+str(key))
                native_field = related_fields[key]
                managed_fields.append({'parent':parent['id'],'field':field['name'],'managed':observed[field['name']]['type'],'managedRelationships':relationships,'nativeRecord':key[0],'nativeMember':key[1],'declared':native_field['declared'],'canonical':native_field['canonical'],'leafStorage':native_field['leafStorage']})
        unique_fields = {(f['record'],f['name']):f for f in fields}
        p.require(len(unique_fields) == len(fields),'duplicate native field use')
        exception = next((e for e in exceptions if e['type'] == t['type']),None)
        if exception:
            p.require(set(exception['nativeConstants']) == {v['name'] for v in model['enums'][exception['native']]['constants']},'exception constants reverse omission')
            p.require(t['representation'] == 'unsigned int','24-exception backing changed')
            p.require(exception.get('storageAlias',exception['native']) == t['storage'],'exception storage join changed')
        result.append({'index':index,'type':t['type'],'native':t['native'],'kind':kind,'nativeRepresentation':t['representation'],'width':4,'managedRepresentation':'System.Int32','constantsDeclaration':t['constantsDeclaration'],'constantsRepresentation':declared_rep,'constantsSource':declared['source'],'constantsImportDisposition':('direct true-enum parameter/result' if t['constantsDeclaration'] == t['storage'] and uses else 'not imported as this constants-declaration type; storage typedef uses are reported separately'),'storage':t['storage'],'storageRepresentation':model['typedefs'].get(t['storage'],{}).get('canonical',t['representation']),'imports':uses,'reservedManagedImports':reserved,'fields':fields,'managedFields':managed_fields,'values':named,'combinations':combos,'domainPolicy':t['domainPolicy'],'layoutException':exception is not None,'disposition':'typed imported/storage use' if uses or fields else 'non-imported constants declaration; no actual imported/storage use','typedExport':'profile_enum_'+str(index),'rawBackingExport':'profile_enum_raw_'+str(index),'representationExport':'profile_enum_rep_'+str(index),'constantsExport':'profile_enum_constants_'+str(index)})
    p.require(sum(t['layoutException'] for t in result) == 24,'exception join incomplete')
    matrix = {'schema':'playground_imgui.profile-enum-matrix','schemaVersion':1,'types':result,'excluded':policy['excluded'],'typedConclusion':'named values and listed legitimate combinations transported by actual native storage types; not exhaustive engine semantics','rawConclusion':'underlying integer bit transport only, not reserved-value engine support','rawPatterns':[0,2147483647,2147483648,4294967295]}
    matrix['useCompleteness'] = use_graph.verify(matrix,reverse,contract,layout,model,nominal)
    matrix['useCensusSha256'] = p.digest(p.encoded(reverse))
    return matrix


def generate(matrix):
    lines = ['// Supplemental enum transport only; includes real imgui.cpp exactly once.','#ifndef PURR_COMBINED_CORE_INCLUDED','#include "imgui.cpp"','#endif','#include "purr_abi.h"','#include <stdio.h>','template<class T, bool = __is_enum(T)> struct ProfileRep { typedef T type; };','template<class T> struct ProfileRep<T,true> { typedef __underlying_type(T) type; };','template<class T> struct ProfileElement { typedef T type; };','template<class T> struct ProfileElement<T*> : ProfileElement<T> {};','template<class T> struct ProfileElement<T&> : ProfileElement<T> {};','template<class T> struct ProfileElement<T&&> : ProfileElement<T> {};','template<class T> struct ProfileElement<const T> : ProfileElement<T> {};','template<class T> struct ProfileElement<volatile T> : ProfileElement<T> {};','template<class T, size_t N> struct ProfileElement<T[N]> : ProfileElement<T> {};','template<class T, size_t N> struct ProfileElement<const T[N]> : ProfileElement<T> {};','template<class T, size_t N> struct ProfileElement<volatile T[N]> : ProfileElement<T> {};','template<class T> struct ProfileElement<const volatile T> : ProfileElement<T> {};','template<class T> struct ProfileElement<ImVector<T>> : ProfileElement<T> {};','template<class T> struct ProfileElement<ImSpan<T>> : ProfileElement<T> {};']
    checks = []
    observations = []
    exports = []
    for t in matrix['types']:
        i, native, storage = t['index'], t['native'], t['storage']
        lines += [f'static_assert(sizeof({native}) == 4 && __is_same(ProfileRep<{native}>::type, {t["nativeRepresentation"]}), "enum representation");',f'static_assert(sizeof({storage}) == 4 && __is_same(ProfileRep<{storage}>::type, {t["storageRepresentation"]}), "actual storage representation");',f'static_assert(__is_same(__underlying_type({t["constantsDeclaration"]}), {t["constantsRepresentation"]}), "associated constants representation");',f'__attribute__((noinline)) static {storage} profile_typed_{i}({storage} value) {{ volatile {storage} slot = value; return slot; }}',f'PURR_API int32_t {t["typedExport"]}({storage} value) {{ return static_cast<int32_t>(profile_typed_{i}(value)); }}',f'PURR_API uint32_t {t["rawBackingExport"]}(uint32_t bits) {{ volatile ProfileRep<{native}>::type slot = purr::bits<ProfileRep<{native}>::type>(bits); ProfileRep<{native}>::type value = slot; return purr::bits<uint32_t>(value); }}']
        lines += [f'__attribute__((noinline)) static {t["constantsDeclaration"]} profile_constants_typed_{i}({t["constantsDeclaration"]} value) {{ volatile {t["constantsDeclaration"]} slot = value; return slot; }}',f'PURR_API int32_t {t["constantsExport"]}({t["constantsDeclaration"]} value) {{ return static_cast<int32_t>(profile_constants_typed_{i}(value)); }}']
        lines += [f'PURR_API uint32_t {t["representationExport"]}() {{ return sizeof({native}) | (uint32_t(static_cast<ProfileRep<{native}>::type>(-1) < static_cast<ProfileRep<{native}>::type>(0)) << 8) | (uint32_t(static_cast<ProfileRep<{storage}>::type>(-1) < static_cast<ProfileRep<{storage}>::type>(0)) << 9); }}']
        exports += [t['typedExport'],t['rawBackingExport'],t['representationExport'],t['constantsExport']]
        for v in t['values']:
            lines += [f'static_assert(static_cast<int64_t>({v["native"]}) == INT64_C({v["value"]}), "native named value");']
            checks += [f'if ({t["typedExport"]}(static_cast<{storage}>({v["native"]})) != INT64_C({v["value"]})) return 1;',f'if ({t["constantsExport"]}(static_cast<{t["constantsDeclaration"]}>({v["native"]})) != INT64_C({v["value"]})) return 4;']
        for combo in t['combinations']:
            checks += [f'if ({t["typedExport"]}(static_cast<{storage}>({combo["nativeExpression"]})) != {combo["value"]}) return 2;']
        for bits in matrix['rawPatterns']:
            checks += [f'if ({t["rawBackingExport"]}(UINT32_C({bits})) != UINT32_C({bits})) return 3;']
        for j,f in enumerate(t['fields']):
            field = f'ProfileElement<decltype((({f["record"]}*)0)->{f["name"]})>::type'
            leaf = f['leafStorage']
            lines += [f'// {f["record"]}.{f["name"]}: {f["declared"]}',f'static_assert(__is_same({field}, {leaf["leaf"]}) && sizeof({field}) == 4 && __is_same(ProfileRep<{field}>::type, {leaf["representation"]}), "native field actual leaf storage");']
            observations += [f'printf("[0,%d,%d,%zu,%d],", {i}, {j}, sizeof({field}), static_cast<ProfileRep<{field}>::type>(-1) < static_cast<ProfileRep<{field}>::type>(0));']
        for j,use in enumerate(t['imports']):
            leaf = use['leafStorage']
            actual = f'ProfileElement<{use["native"]}>::type'
            lines += [f'// {use["entryPoint"]} position {use["position"]}: {use["native"]}',f'static_assert(__is_same({actual}, {leaf["leaf"]}) && sizeof({actual}) == 4 && __is_same(ProfileRep<{actual}>::type, {leaf["representation"]}), "native import actual leaf storage");']
            observations += [f'printf("[1,%d,%d,%zu,%d],", {i}, {j}, sizeof({actual}), static_cast<ProfileRep<{actual}>::type>(-1) < static_cast<ProfileRep<{actual}>::type>(0));']
    lines += ['#ifdef PROFILE_ENUM_MAIN','int main() {']+checks+['puts("{\\"useRepresentations\\":[");']+observations+['puts("null],\\"passed\\":true}");','return 0;','}','#endif']
    return ('\n'.join(lines)+'\n').encode(),exports


def compare_observations(matrix, native):
    expected = {(kind,t['index'],j):(4,int(use['leafStorage']['representation'] == 'int')) for t in matrix['types'] for kind,items in ((0,t['fields']),(1,t['imports'])) for j,use in enumerate(items)}
    rows = native['useRepresentations']
    p.require(native['passed'] and rows[-1] is None,'native enum-use observations malformed')
    rows = rows[:-1]
    observed = {tuple(row[:3]):tuple(row[3:]) for row in rows}
    p.require(len(rows) == len(observed) and observed == expected,'native enum-use observation omission/representation mutation')
    return len(observed)


def checked_census(selected, enum_output):
    """Rebuild from full raw compiler/contract inputs, never trust a producer's use list."""
    selected = safe_inputs.selected(selected)
    enum_output = safe_inputs.selected(enum_output)
    source = layout_gate.verify_source(selected)
    raw = native_model.read_ast(selected/'ast-model/ast.json')
    for folder in ('local','local-settings','local-preview'):
        extra = native_model.read_ast(enum_output/folder/'ast.json')
        raw['inner'].extend(extra['inner'] if extra.get('kind') == 'TranslationUnitDecl' else [extra])
    policy = load_policy()
    contract = p.api.load(p.SCRATCH/'binding-contract.json')
    layout = p.api.load(p.LAYOUT/'mapping.json')
    model = p.api.load(enum_output/'harvest.json')
    nominal = use_graph.roots(policy,contract,layout,model)
    reverse = use_graph.census(policy,contract,layout,p.api.load(selected/'ast-model/declarations.json'),use_graph.raw_fields(raw,source),p.api.load(selected/'selected-mapping.json'),nominal)
    p.require(reverse == p.api.load(enum_output/'census.json'),'reverse census omission/mutation')
    return reverse


def run(selected, output, targets, zig='zig'):
    output = p.new_output(output, [selected])
    safe_inputs.fixed_inputs()
    selected = safe_inputs.selected(selected)
    selected = p.checked(selected,'directory')
    source = layout_gate.verify_source(selected)
    policy = load_policy()
    contract = p.api.load(p.SCRATCH/'binding-contract.json')
    layout = p.api.load(p.LAYOUT/'mapping.json')
    mapping = p.api.load(selected/'selected-mapping.json')
    files = [output/n for n in ('harvest.json','census.json','matrix.json','enum-fixture.cpp','supplemental-exports.json','results.json')]
    for name in ('aliases','local','local-settings','local-preview'):
        files += p.declarations.output_files(output/name)
    for rid in targets:
        files += [output/rid/n for n in ('enum-test','enum-test.exe','enum-test.pdb','build.log','build.command.json','run.log','run.command.json')]
    p.prepare(files,[output])
    header = p.api.load(selected/'ast-model/ast.json')
    local = native_model.extract(source,output/'local','#include "imgui.cpp"\n',zig,ast_filter='ImGuiDockRequest')
    header['inner'].extend(local['inner'])
    for folder, name in (('local-settings','ImGuiDockNodeSettings'),('local-preview','ImGuiDockPreviewData')):
        local = native_model.extract(source,output/folder,'#include "imgui.cpp"\n',zig,ast_filter=name)
        header['inner'].extend(local['inner'] if local.get('kind') == 'TranslationUnitDecl' else [local])
    body = '#include "imgui.cpp"\n'
    for i,t in enumerate(policy['types']):
        body += f'using ProfileRepresentation{i} = '+(t['native'] if t['kind']=='typedef' else '__underlying_type('+t['native']+')')+';\n'
        body += f'using ProfileConstantRepresentation{i} = __underlying_type({t["constantsDeclaration"]});\n'
    aliases = native_model.extract(source,output/'aliases',body,zig,ast_filter='Profile')
    header['inner'].extend(aliases['inner'])
    model = native_model.harvest(header,source)
    (output/'harvest.json').write_bytes(p.encoded(model))
    nominal = use_graph.roots(policy,contract,layout,model)
    reverse = use_graph.census(policy,contract,layout,p.api.load(selected/'ast-model/declarations.json'),use_graph.raw_fields(header,source),mapping,nominal)
    (output/'census.json').write_bytes(p.encoded(reverse))
    matrix = validate(policy,contract,layout,model,mapping,reverse)
    generated,exports = generate(matrix)
    (output/'matrix.json').write_bytes(p.encoded(matrix))
    (output/'enum-fixture.cpp').write_bytes(generated)
    (output/'supplemental-exports.json').write_bytes(p.encoded({'classification':'test-only typed enum / raw underlying integer transport, never substitute engine imports','exports':exports}))
    lock = p.builder.read_json(p.NATIVE/'source.lock.json')
    config = p.builder.verify_config_pin(lock)
    results = []
    for rid in targets:
        directory = output/rid
        exe = directory/('enum-test.exe' if rid == 'win-x64' else 'enum-test')
        inputs = [str(output/'enum-fixture.cpp')]+[str(source/n) for n in lock['build']['sourceFiles'] if n not in ('imgui.cpp','src/PlaygroundBrutalAdapter.cpp')]
        command = p.compile_command(source,config,lock,rid,zig)+['-DPROFILE_ENUM_MAIN=1']+inputs+['-o',str(exe)]
        p.api_check.logged(command,directory/'build.log')
        if rid == 'osx-arm64':
            p.api_check.logged([str(exe)],directory/'run.log',30)
            compare_observations(matrix,p.api.load(directory/'run.log'))
        results.append({'rid':rid,'typedNativeMatrixCompiled':True,'executed':rid == 'osx-arm64','types':len(matrix['types']),'namedValues':sum(len(t['values']) for t in matrix['types']),'rawBitsConclusion':matrix['rawConclusion'],'artifactSha256':p.digest(exe.read_bytes()),'matrixSha256':p.digest(p.encoded(matrix)),'useCompleteness':matrix['useCompleteness'],'censusSha256':p.digest(p.encoded(reverse)),'compilerInputsSha256':{str(f.relative_to(output)):p.digest(f.read_bytes()) for f in [output/folder/'ast.json' for folder in ('local','local-settings','local-preview','aliases')]},'sourceRecordSha256':p.digest((selected/'source-record.json').read_bytes())})
    (output/'results.json').write_bytes(p.encoded(results))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--selected',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args = parser.parse_args()
    run(args.selected,args.output,['osx-arm64','linux-x64','win-x64'])
