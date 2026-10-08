#!/usr/bin/env python3
"""Profile-specific deterministic, mutation, independent inventory and path negatives."""
from __future__ import annotations

import argparse
import copy
import importlib
import os
import shutil
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
enums = importlib.import_module('enums')
gate = importlib.import_module('layout_gate')


def run(selected, enum_output, output):
    output = p.new_output(output, [selected, enum_output])
    safe_inputs.fixed_inputs()
    selected = safe_inputs.selected(selected)
    enum_output = safe_inputs.selected(enum_output)
    selected = p.checked(selected,'directory')
    enum_output = p.checked(enum_output,'directory')
    source = gate.verify_source(selected)
    report_path = output/'report.json'
    files = [report_path]
    for rid in ('osx-arm64','linux-x64','win-x64'):
        for name in ('width','signedness','value','ItemFlagsStack','ShortcutField','ShortcutImport','nested-relations'):
            files += [output/rid/(name+s) for s in ('.cpp','.o','.log','.command.json')]
    p.prepare(files,[output])
    passed = []
    def reject(name, action):
        try:
            action()
        except (ValueError, RuntimeError, KeyError, TypeError):
            passed.append(name)
        else:
            raise AssertionError('negative unexpectedly accepted: '+name)
    contract = p.api.load(p.SCRATCH/'binding-contract.json')
    mapping = p.load_prior()
    selected_model = p.api.load(selected/'ast-model/declarations.json')
    original = p.SCRATCH/'source'/p.patch.read_manifest()['sourceCommit']
    old_headers = {n:(original/n).read_text() for n in mapping['headers']}
    new_headers = {n:(source/n).read_text() for n in mapping['headers']}
    def derive(model, prior=None):
        return p.derive(contract,mapping if prior is None else prior,model,old_headers,new_headers)
    first = derive(selected_model)
    second = derive(copy.deepcopy(selected_model))
    p.require(first == second and first[1] == (selected/'generated/wrappers.cpp').read_bytes(),'deterministic profile replay failed')
    passed.append('deterministic profile/mapping/wrapper replay')
    mapped_key = p.api.declaration_key(mapping['mappings'][0]['native'])
    ix = next(i for i,d in enumerate(selected_model['declarations']) if p.api.declaration_key(d)==mapped_key)
    for field,value in [('scope','Other'),('name','Other'),('signature','void ()'),('kind','FunctionDecl' if selected_model['declarations'][ix]['kind']!='FunctionDecl' else 'CXXMethodDecl'),('static',not selected_model['declarations'][ix]['static']),('line',1),('file','imgui.cpp'),('parameters',[])]:
        model = copy.deepcopy(selected_model)
        model['declarations'][ix][field] = value
        reject('selected declaration mutation '+field,lambda model=model:derive(model))
    for name,mutate in [
        ('callback',lambda m:m['callbackTypes'].__setitem__('ImDrawCallback','void (*)()')),
        ('sort width',lambda m:m['enumUnderlyingTypes'].__setitem__('ImGuiSortDirection',{'canonical':'unsigned char','spelling':'ImU8'})),
        ('config',lambda m:m.__setitem__('configSha256','0'*64)),
        ('header',lambda m:m['headers'].__setitem__('imgui.h','0'*64)),
        ('declaration omission',lambda m:m['declarations'].pop(ix)),
        ('declaration duplication',lambda m:m['declarations'].append(copy.deepcopy(m['declarations'][ix])))]:
        model = copy.deepcopy(selected_model)
        mutate(model)
        reject(name,lambda model=model:derive(model))
    prior = copy.deepcopy(mapping)
    prior['mappings'][0]['native']['name'] = 'DifferentApprovedLookingCallee'
    reject('prior mapping cannot re-bless another callee',lambda:derive(selected_model,prior))
    policy = enums.load_policy()
    layout = p.api.load(p.LAYOUT/'mapping.json')
    native = p.api.load(enum_output/'harvest.json')
    smap = p.api.load(selected/'selected-mapping.json')
    reverse = enums.checked_census(selected,enum_output)
    def validate(pol, model=native):
        return enums.validate(pol,contract,layout,model,smap,reverse)
    p.require(p.encoded(validate(policy)) == (enum_output/'matrix.json').read_bytes(),'deterministic enum join replay failed')
    passed.append('deterministic complete enum join replay')
    for name,mutate in [
        ('enum omission',lambda q:q['types'].pop()),
        ('enum duplication',lambda q:q['types'].append(q['types'][0])),
        ('enum value omission',lambda q:q['types'][0]['values'].pop()),
        ('enum value mutation',lambda q:q['types'][0]['values'][0].__setitem__('value',42)),
        ('enum signedness',lambda q:q['types'][0].__setitem__('representation','unsigned int')),
        ('enum width representation',lambda q:q['types'][0].__setitem__('representation','unsigned char')),
        ('enum domain',lambda q:q['types'][0].__setitem__('combinations',[['None','Closed']])),
        ('enum constant association',lambda q:q['types'][0]['values'][0].__setitem__('native','ImGuiDir_None'))]:
        pol = copy.deepcopy(policy)
        mutate(pol)
        reject(name,lambda pol=pol:validate(pol))
    for record,member in [('ImGuiDockRequest','Type'),('ImGuiTableColumnSortSpecs','SortDirection'),('ImGuiDockNodeSettings','Flags')]:
        model = copy.deepcopy(native)
        model['fields'] = [f for f in model['fields'] if (f['record'],f['name']) != (record,member)]
        reject('independent field omission '+record+'.'+member,lambda model=model:validate(policy,model))
    model = copy.deepcopy(native)
    model['enums']['ImGuiButtonFlagsPrivate_']['constants'].pop()
    reject('24-exception constant reverse omission',lambda model=model:validate(policy,model))
    model = copy.deepcopy(native)
    model['typedefs']['ImGuiButtonFlags']['canonical'] = 'unsigned int'
    reject('private unsigned backing cannot replace real signed typedef',lambda model=model:validate(policy,model))
    matrix = validate(policy)
    nominal = enums.use_graph.roots(policy,contract,layout,native)
    def verify_matrix(value):
        return enums.use_graph.verify(value,reverse,contract,layout,native,nominal)
    concrete = [
        ('ImGuiItemFlags','fields',('ImGuiContext','ItemFlagsStack')),
        ('ImGuiKey','fields',('ImGuiNextItemData','Shortcut')),
        ('ImGuiKey','imports',('SetNextItemShortcut','0')),
        ('ImGuiDir','imports',('FindBestWindowPosForPopupEx_internal','2')),
        ('ImGuiButtonFlags','imports',('MultiSelectItemHeader_internal','2'))]
    for enum_name,group,key in concrete:
        def find_use(value, enum_name=enum_name, group=group, key=key):
            row = next(t for t in value['types'] if t['native'] == enum_name)
            keys = ('record','name') if group == 'fields' else ('entryPoint','position')
            index = next(i for i,u in enumerate(row[group]) if tuple(u[k] for k in keys) == key)
            return row[group],index
        changed = copy.deepcopy(matrix)
        items,index = find_use(changed)
        items.pop(index)
        reject('reverse concrete use removal '+'.'.join(key),lambda changed=changed:verify_matrix(changed))
        changed = copy.deepcopy(matrix)
        items,index = find_use(changed)
        items[index]['leafStorage']['leaf'] = 'ImGuiKey' if key != ('FindBestWindowPosForPopupEx_internal','2') else 'ImGuiAxis'
        reject('actual leaf mutation '+'.'.join(key),lambda changed=changed:verify_matrix(changed))
    changed = copy.deepcopy(native)
    changed['fields'] = [f for f in changed['fields'] if (f['record'],f['name']) != ('ImGuiContext','ItemFlagsStack')]
    reject('erased Int32 vector harvest omission versus raw census',lambda:validate(policy,changed))
    changed = copy.deepcopy(native)
    changed['typedefs']['ImGuiKeyChord']['canonical'] = 'unsigned int'
    reject('KeyChord actual signed typedef mutation',lambda:validate(policy,changed))
    use_native = p.api.load(enum_output/'osx-arm64/run.log')
    enums.compare_observations(matrix,use_native)
    for name,edit in [('use observation omission',lambda v:v['useRepresentations'].pop(0)),('use observation signedness',lambda v:v['useRepresentations'][0].__setitem__(4,1-v['useRepresentations'][0][4]))]:
        changed = copy.deepcopy(use_native)
        edit(changed)
        reject(name,lambda changed=changed:enums.compare_observations(matrix,changed))
    relationships = [
        ('ImGuiKeyChord *','ImGuiKeyChord',['pointer']),
        ('const ImGuiKeyChord &','ImGuiKeyChord',['reference','cv']),
        ('ImGuiKeyChord *[3]','ImGuiKeyChord',['array','pointer']),
        ('ImVector<ImGuiKeyChord *>','ImGuiKeyChord',['container-element','pointer']),
        ('ImSpan<ImGuiItemFlags> &','ImGuiItemFlags',['reference','container-element']),
        ('ImGuiKeyChord (*)[2]','ImGuiKeyChord',['pointer','array']),
        ('ImVector<ImGuiKeyChord *> (&)[2]','ImGuiKeyChord',['reference','array','container-element','pointer'])]
    for spelling,leaf,kinds in relationships:
        observed_leaf,path = enums.use_graph.native_leaf(spelling)
        p.require(observed_leaf == leaf and [x['kind'] for x in path] == kinds,'nested native relationship omitted: '+spelling)
        passed.append('nested relation '+spelling)
    reject('unknown enum-containing relationship fails closed',lambda:enums.use_graph.associate('Unreviewed<ImGuiKeyChord>','Unreviewed<int>',nominal,native,{}))
    # New-directory policy makes every descendant absent at preflight. Existing roots,
    # redirects (including in-root/dangling), shared leaves and escapes fail before work.
    marker = output/'marker'
    marker.write_text('protected')
    existing = output/'existing'
    existing.mkdir()
    reject('existing directory',lambda:p.new_output(existing))
    reject('existing regular leaf',lambda:p.new_output(marker))
    for name,destination in [('redirect',existing),('dangling',output/'absent'),('file-redirect',marker)]:
        link = output/name
        link.symlink_to(destination)
        reject(name,lambda link=link:p.new_output(link))
        reject(name+' ancestor',lambda link=link:p.new_output(link/'child'))
    shared = output/'shared'
    os.link(marker,shared)
    reject('hardlinked output leaf',lambda:p.preflight([shared]))
    reject('outside owned subtree',lambda:p.new_output(p.SCRATCH/'unowned-output'))
    reject('parent traversal',lambda:p.new_output(output/'..'/'escape'))
    p.require(marker.read_text() == 'protected','path negative changed protected input')
    clone = output/'source-mutation'
    clone.mkdir()
    manifest = p.api.load(selected/'profile.json')
    for name in manifest['artifacts']:
        target = clone/name
        target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(selected/name,target)
    shutil.copyfile(selected/'profile.json',clone/'profile.json')
    shutil.copytree(source,clone/'source')
    changed = clone/'source/imgui_widgets.cpp'
    changed.write_bytes(changed.read_bytes()+b'\n// unapproved source edit\n')
    receipt = p.api.load(clone/'source-record.json')
    receipt['sourceFiles']['imgui_widgets.cpp'] = p.digest(changed.read_bytes())
    (clone/'source-record.json').write_bytes(p.encoded(receipt))
    manifest['sourceRecordSha256'] = p.digest(p.encoded(receipt))
    manifest['artifacts']['source-record.json'] = p.digest(p.encoded(receipt))
    (clone/'profile.json').write_bytes(p.encoded(manifest))
    reject('source edit rejected even with self-consistent changed receipts',lambda:gate.verify_source(clone))
    # Real compiler negatives for selected enum width, signedness and named value.
    text = (enum_output/'enum-fixture.cpp').read_text()
    mutations = {
        'width':('sizeof(ImGuiSortDirection) == 4','sizeof(ImGuiSortDirection) == 1'),
        'signedness':('ProfileRep<ImGuiSelectionRequestType>::type, unsigned int','ProfileRep<ImGuiSelectionRequestType>::type, int'),
        'value':('static_cast<int64_t>(ImGuiMouseCursor_None) == INT64_C(-1)','static_cast<int64_t>(ImGuiMouseCursor_None) == INT64_C(0)'),
        'ItemFlagsStack':('decltype(((ImGuiContext*)0)->ItemFlagsStack)>::type, ImGuiItemFlags','decltype(((ImGuiContext*)0)->ItemFlagsStack)>::type, ImGuiKey'),
        'ShortcutField':('decltype(((ImGuiNextItemData*)0)->Shortcut)>::type, ImGuiKeyChord','decltype(((ImGuiNextItemData*)0)->Shortcut)>::type, ImGuiKey'),
        'ShortcutImport':('// SetNextItemShortcut position 0: ImGuiKeyChord\nstatic_assert(__is_same(ProfileElement<ImGuiKeyChord>::type, ImGuiKeyChord)', '// SetNextItemShortcut position 0: ImGuiKeyChord\nstatic_assert(__is_same(ProfileElement<ImGuiKeyChord>::type, ImGuiKey)')}
    lock = p.builder.read_json(p.NATIVE/'source.lock.json')
    config = p.builder.verify_config_pin(lock)
    for rid in ('osx-arm64','linux-x64','win-x64'):
        for name,(old,new) in mutations.items():
            p.require(old in text,'negative anchor absent')
            path = output/rid/(name+'.cpp')
            path.write_text(text.replace(old,new,1))
            command = p.compile_command(source,config,lock,rid,'zig')+['-c',str(path),'-o',str(path.with_suffix('.o'))]
            path.with_suffix('.command.json').write_bytes(p.encoded(command))
            result = p.process.run(command,capture_output=True,text=True,timeout=120)
            path.with_suffix('.log').write_text(result.stdout+result.stderr)
            p.require(result.returncode != 0 and 'static assertion failed' in result.stderr,'compiler mutation accepted: '+name)
            passed.append(rid+' actual compiler '+name+' rejection')
        path = output/rid/'nested-relations.cpp'
        # Synthetic declaration-only positive controls, not engine inputs or exports.
        body = '#include "'+str(enum_output/'enum-fixture.cpp')+'"\n'
        for spelling,leaf,_ in relationships:
            body += 'static_assert(__is_same(ProfileElement<'+spelling+' >::type, '+leaf+'), "nested actual leaf");\n'
        path.write_text(body)
        command = p.compile_command(source,config,lock,rid,'zig')+['-c',str(path),'-o',str(path.with_suffix('.o'))]
        p.api_check.logged(command,path.with_suffix('.log'),120)
        passed.append(rid+' nested pointer/reference/array/container/wrapper compiler positive')
    (report_path).write_bytes(p.encoded({'schema':'playground_imgui.profile-self-tests','schemaVersion':1,'passed':passed,'count':len(passed),'skipped':[],'sourcePreserved':True}))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--selected',type=Path,required=True)
    parser.add_argument('--enums',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args = parser.parse_args()
    run(args.selected,args.enums,args.output)
