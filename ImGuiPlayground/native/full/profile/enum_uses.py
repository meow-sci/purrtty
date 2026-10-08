"""Enum-use leaf decomposition and independent reverse census.

Records are graph boundaries: their fields are censused once, not recursively
expanded through every Context/Window pointer cycle. Only nominal enum typedefs
and the reviewed KeyChord scalar wrapper confer enum semantics, never plain int.
"""
from __future__ import annotations

import importlib
import re
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
KEY = 'Brutal.ImGui::Brutal.ImGuiApi.ImGuiKey'
CHORD = 'Brutal.ImGui::Brutal.ImGuiApi.ImGuiKeyChord'


def roots(policy, contract, layout, model):
    result = {}
    for row in policy['types']:
        result.setdefault(row['storage'], []).append(row['type'])
        if row['native'] != row['storage']:
            result.setdefault(row['native'], []).append(row['type'])
    # Exact closed scalar-wrapper relationship, independently checked in the graph.
    c = next(t for t in contract['types'] if t['id'] == CHORD)
    m = next(t for t in layout['types'] if t['id'] == CHORD)
    p.require(m['native'] == 'ImGuiKeyChord' and len(m['fields']) == 1 and m['fields'][0]['member'] == '$self' and c['fields'][0]['type'] == KEY, 'KeyChord wrapper relationship changed')
    p.require(model['typedefs']['ImGuiKeyChord']['canonical'] == 'int', 'KeyChord is signed int, not substituted ImGuiKey enum')
    result['ImGuiKeyChord'] = [KEY]
    return result


def native_leaf(spelling):
    """Finite recursive type-expression grammar; unknown enum-containing forms fail."""
    text = spelling.strip()
    path = []
    while True:
        grouped = re.fullmatch(r'(.+?)\s*\(\s*([*&]+)\s*\)((?:\[[^][]+\])+)',text)
        if grouped:
            for marker in (['&&'] if grouped.group(2) == '&&' else list(grouped.group(2))):
                path.append({'kind':'pointer' if marker == '*' else 'reference','spelling':marker})
            text = grouped.group(1).strip()+grouped.group(3)
        elif text.endswith((' const',' volatile')):
            text, cv = text.rsplit(' ', 1)
            path.append({'kind':'cv', 'qualifier':cv})
        elif text.endswith(('&&','&','*')):
            suffix = '&&' if text.endswith('&&') else text[-1]
            text = text[:-len(suffix)].strip()
            path.append({'kind':'pointer' if suffix == '*' else 'reference', 'spelling':suffix})
        elif re.search(r'\[[^][]+\]$', text):
            at = text.rindex('[')
            path.append({'kind':'array', 'extent':text[at+1:-1]})
            text = text[:at].strip()
        elif text.startswith(('const ','volatile ')):
            cv, text = text.split(' ', 1)
            path.append({'kind':'cv', 'qualifier':cv})
        elif text.startswith(('ImVector<','ImSpan<')) and text.endswith('>'):
            at = text.index('<')
            path.append({'kind':'container-element', 'container':text[:at]})
            text = text[at+1:-1].strip()
        else:
            return text, path


def associate(spelling, canonical, nominal, model, representations):
    leaf, path = native_leaf(spelling)
    if leaf not in nominal:
        # Independent token discovery prevents silently excluding an unknown nesting.
        p.require(not set(re.findall(r'[A-Za-z_]\w*', spelling)) & nominal.keys(), 'unsupported enum relationship: '+spelling)
        return None
    canonical_leaf, _ = native_leaf(canonical)
    representation = model['typedefs'].get(leaf, {}).get('canonical')
    if representation is None:
        representation = representations[leaf]
    return {'leaf':leaf, 'canonicalLeaf':canonical_leaf, 'representation':representation, 'relationships':path, 'scalarWrapper':leaf == 'ImGuiKeyChord'}


def raw_fields(ast, source):
    """Reverse AST census is separate from native_model.harvest and join filters."""
    native_model = importlib.import_module('native_model')
    texts = {name:(source/name).read_text() for name in ('imgui.h','imgui_internal.h','imgui.cpp')}
    result = []
    def visit(node, scope=()):
        kind = node.get('kind')
        if kind == 'FieldDecl':
            location = native_model.source_location(node, texts)
            if location:
                result.append({'record':'::'.join(scope), 'name':node['name'], 'declared':node['type']['qualType'], 'canonical':node['type'].get('desugaredQualType',node['type']['qualType']), 'bitfield':node.get('isBitfield',False), 'source':location})
        if kind in ('NamespaceDecl','CXXRecordDecl') and node.get('name'):
            scope += (node['name'],)
        for child in node.get('inner',[]):
            visit(child,scope)
    visit(ast)
    return result


def managed_leaf(type_id, types):
    path = []
    seen = set()
    while type_id in types and type_id not in seen:
        seen.add(type_id)
        t = types[type_id]
        if t.get('elementType'):
            path.append(t['kind'])
            type_id = t['elementType']
        elif t.get('genericDefinition') in ('Brutal.ImGui::Brutal.ImGuiApi.ImVector`1','Brutal.Core.Common::Brutal.Pointers.Ptr`1'):
            p.require(len(t['genericArguments']) == 1,'unsupported managed container arity')
            path.append(t['genericDefinition'])
            type_id = t['genericArguments'][0]
        elif type_id == CHORD:
            path.append('ImGuiKeyChord signed scalar wrapper')
            type_id = KEY
        else:
            break
    return type_id, path


def census(policy, contract, layout, declarations, raw, mapping, nominal):
    """Independent lexical reverse inventory of ALL inputs, not generated matrix rows.

This walks raw AST fields, all selected declarations and the complete managed
contract/type graph. Token membership finds candidates regardless of nesting;
only the separate structural join decides how to reach their actual leaf type.
"""
    def tokens(text):
        return sorted(set(re.findall(r'[A-Za-z_]\w*',text)) & nominal.keys())
    fields = []
    for f in raw:
        associated = tokens(f['declared'])
        fields.append(dict(f, associatedStorage=associated, disposition='enum-related leaf requires join' if associated else 'no nominal enum leaf; record members are separate census edges, plain integral storage has no inferred enum domain'))
    declaration_rows = []
    declared = {}
    for d in declarations['declarations']:
        key = p.api.declaration_key(d)
        declared[key] = d
        values = [('return',d['signature'].split('(')[0].strip())]+[(str(i),v['type']) for i,v in enumerate(d['parameters'])]
        for position, spelling in values:
            declaration_rows.append({'declaration':list(key),'position':position,'native':spelling,'associatedStorage':tokens(spelling),'disposition':'enum-related declaration' if tokens(spelling) else 'no nominal enum leaf at this declaration position'})
    imports = []
    for m in mapping['mappings']:
        d = declared[p.api.declaration_key(m['native'])]
        p.require(d == m['native'],'mapping/declaration identity mismatch in reverse census')
        values = [('return',d['signature'].split('(')[0].strip(),m['managedReturn'])]+[(str(i+int(m['memberTarget'])),v['type'],m['managedParameters'][i+int(m['memberTarget'])]) for i,v in enumerate(d['parameters'])]
        for position, spelling, managed in values:
            imports.append({'entryPoint':m['entryPoint'],'position':position,'native':spelling,'managed':managed,'associatedStorage':tokens(spelling),'source':{'file':d['file'],'line':d['line']},'disposition':'enum-related import requires join' if tokens(spelling) else 'no nominal enum leaf; native record fields are censused separately'})
    types = {t['id']:t for t in contract['types']}
    enum_ids = {t['type'] for t in policy['types']}
    managed = []
    for owner in contract['types']:
        for f in owner.get('fields',[]):
            leaf, path = managed_leaf(f['type'],types)
            managed.append({'owner':owner['id'],'field':f['name'],'type':f['type'],'leaf':leaf,'relationships':path,'disposition':'enum-associated managed field' if leaf in enum_ids else 'nonnative TypeCode helper' if leaf == 'System.Private.CoreLib::System.TypeCode' else 'record boundary or no managed enum leaf; native nominal aliases may still associate erased Int32 storage'})
    for m in contract['imports']:
        for i,v in enumerate([m['return']]+m['parameters']):
            leaf,path = managed_leaf(v['type'],types)
            managed.append({'entryPoint':m['entryPoint'],'position':'return' if i == 0 else str(i-1),'type':v['type'],'leaf':leaf,'relationships':path,'disposition':'reserved manual import' if m['entryPoint'] in p.api.RESERVED else 'enum-associated managed import' if leaf in enum_ids else 'record boundary or no managed enum leaf'})
    return {'schema':'playground_imgui.enum-use-census','schemaVersion':1,'fields':fields,'declarations':declaration_rows,'imports':imports,'managed':managed,'relationships':['cv','pointer','reference','array','ImVector element','ImSpan element','reviewed ImGuiKeyChord signed scalar wrapper'],'recordBoundary':'Each record field is an independent graph edge; cyclic object pointers do not recursively duplicate every member at every call site. Opaque/excluded layout records remain their accepted layout dispositions; unassociated int is never guessed to be an enum.'}


def verify(matrix, reverse, contract, layout, model, nominal):
    """Require complete producer use sets against independent full reverse census."""
    # Even simultaneous producer matrix + harvest omissions cannot shrink the raw census.
    raw = [{k:v for k,v in f.items() if k not in ('associatedStorage','disposition')} for f in reverse['fields']]
    p.require(raw == model['fields'],'harvest field omission/mutation against raw AST census')
    for t in matrix['types']:
        expected_fields = {(f['record'],f['name']):f for f in reverse['fields'] if any(t['type'] in nominal[s] for s in f['associatedStorage'])}
        expected_imports = {(v['entryPoint'],v['position']):v for v in reverse['imports'] if any(t['type'] in nominal[s] for s in v['associatedStorage'])}
        fields = {(f['record'],f['name']):f for f in t['fields']}
        imports = {(v['entryPoint'],v['position']):v for v in t['imports']}
        p.require(len(fields) == len(t['fields']) and fields.keys() == expected_fields.keys(),'reverse enum field-use omission/duplicate: '+t['native'])
        p.require(len(imports) == len(t['imports']) and imports.keys() == expected_imports.keys(),'reverse enum import-use omission/duplicate: '+t['native'])
        for key,f in fields.items():
            r = expected_fields[key]
            p.require(all(f[k] == r[k] for k in ('declared','canonical','source','bitfield')),'reverse native field spelling mutation')
            leaf = f['leafStorage']
            p.require(r['associatedStorage'] == [leaf['leaf']], 'native field actual leaf substitution')
            actual_leaf, path = native_leaf(r['declared'])
            p.require(actual_leaf == leaf['leaf'] and path == leaf['relationships'], 'native field relationship mutation')
            p.require(leaf['representation'] == model['typedefs'].get(actual_leaf,{}).get('canonical',t['nativeRepresentation']), 'native field representation mutation')
        for key,v in imports.items():
            r = expected_imports[key]
            p.require(all(v[k] == r[k] for k in ('native','managed','source')),'reverse native import spelling mutation')
            leaf = v['leafStorage']
            p.require(r['associatedStorage'] == [leaf['leaf']], 'native import actual leaf substitution')
            actual_leaf, path = native_leaf(r['native'])
            p.require(actual_leaf == leaf['leaf'] and path == leaf['relationships'], 'native import relationship mutation')
            p.require(leaf['representation'] == model['typedefs'].get(actual_leaf,{}).get('canonical',t['nativeRepresentation']), 'native import representation mutation')
    # Reverse managed reachability (pointer/container/scalar wrapper), independent of
    # the producer's native leaf grammar. Erased Int32 vectors are recovered above.
    required = {(v['entryPoint'],v['position'],v['leaf']) for v in reverse['managed'] if v['disposition'] == 'enum-associated managed import'}
    observed = {(v['entryPoint'],v['position'],t['type']) for t in matrix['types'] for v in t['imports']}
    p.require(required <= observed,'managed enum/wrapper import closure omission')
    layout_rows = {t['id']:t for t in layout['types']}
    observed_fields = {(v['parent'],v['field']) for t in matrix['types'] for v in t['managedFields']}
    for v in reverse['managed']:
        if v['disposition'] == 'enum-associated managed field':
            p.require((v['owner'],v['field']) in observed_fields or layout_rows[v['owner']]['native'] is None,'managed enum/wrapper field closure omission')
    return {'nativeFieldEdges':len(reverse['fields']),'nativeDeclarationPositions':len(reverse['declarations']),'mappedImportPositions':len(reverse['imports']),'managedFieldAndImportEdges':len(reverse['managed']),'enumFieldUses':len({(f['record'],f['name']) for t in matrix['types'] for f in t['fields']}),'enumImportUses':len({(v['entryPoint'],v['position']) for t in matrix['types'] for v in t['imports']}),'undisposedUses':0}
