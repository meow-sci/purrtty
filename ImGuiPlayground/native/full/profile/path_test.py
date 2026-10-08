#!/usr/bin/env python3
"""Actual entrypoint/canary regressions for immutable inputs and early redirects."""
from __future__ import annotations

import argparse
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


def snapshot(folder):
    return {str(f.relative_to(folder)):p.digest(f.read_bytes()) for f in folder.rglob('*') if f.is_file()}


def run(selected, enums, output, selection):
    output = p.new_output(output,[selected,enums,selection])
    selected = p.safe_inputs.selected(selected)
    enums = p.safe_inputs.selected(enums)
    before = {str(folder):snapshot(folder) for folder in (selected,enums)}
    p.prepare([output/'report.json'],[output])
    tool_marker = output/'tool-invoked'
    code_marker = output/'redirect-executed'
    bin_dir = output/'canary-bin'
    bin_dir.mkdir()
    for name in ('zig','dotnet','xcrun','nm','objdump'):
        path = bin_dir/name
        path.write_text('#!/bin/sh\nprintf invoked > "$PROFILE_TOOL_MARKER"\nexit 97\n')
        path.chmod(0o755)
    environment = dict(os.environ,PATH=str(bin_dir)+os.pathsep+os.environ['PATH'],PROFILE_TOOL_MARKER=str(tool_marker),PROFILE_CODE_MARKER=str(code_marker),PYTHONDONTWRITEBYTECODE='1')
    passed = []
    def command(script, destination, selected_input=selected, enum_input=enums):
        result = [sys.executable,str(p.HERE/script),'--output',str(destination)]
        if script != 'selected_profile.py':
            result += ['--selected',str(selected_input)]
        if script in ('managed.py','self_test.py'):
            result += ['--enums',str(enum_input)]
        if script == 'managed.py':
            result += ['--ksa-folder',str(selection)]
        return result
    def reject(name, cmd, destination=None, expected='input'):
        result = p.process.run(cmd,env=environment,capture_output=True,text=True,timeout=30)
        (output/(str(len(passed))+'.log')).write_text(result.stdout+result.stderr)
        p.require(result.returncode != 0 and expected in result.stderr, 'entrypoint did not reject for the intended boundary: '+name)
        p.require(not tool_marker.exists() and not code_marker.exists(),'early process/module execution: '+name)
        if destination:
            p.require(not destination.exists(),'destination created before rejection: '+name)
        for folder in (selected,enums):
            p.require(snapshot(folder) == before[str(folder)],'protected bytes/membership changed: '+name)
        passed.append(name)
    scripts = ('selected_profile.py','enums.py','layout_gate.py','managed.py','self_test.py')
    for script in scripts:
        for folder in (selected,selected/'source',selected/'ast-model',selected/'generated',enums):
            destination = folder/('must-not-create-'+script)
            reject(script+' fresh nested '+str(folder.relative_to(p.SCRATCH)),command(script,destination),destination)
        for kind,target in (('redirect',selected/'source'),('dangling',output/'missing'),('in-root',enums)):
            link = output/(script+'-'+kind)
            link.symlink_to(target)
            reject(script+' output '+kind,command(script,link/'child'),expected='redirect')
    for script in scripts[1:]:
        alias = output/(script+'-selected-input')
        alias.symlink_to(selected)
        destination = output/(script+'-safe-sibling')
        reject(script+' selected root redirect',command(script,destination,alias),destination,expected='input redirect')
    for script in ('managed.py','self_test.py'):
        alias = output/(script+'-enum-input')
        alias.symlink_to(enums)
        destination = output/(script+'-safe-enum-sibling')
        reject(script+' enum input redirect',command(script,destination,selected,alias),destination,expected='input redirect')
    # A repository-shaped disposable copy exercises imports BEFORE argument parsing.
    # No dependency or original component file is replaced in the working copy.
    sandbox = output/'import-sandbox'
    native = sandbox/'ImGuiPlayground/native'
    for folder in ('profile','api','layout'):
        shutil.copytree(p.HERE.parent/folder,native/'full'/folder)
    for name in ('__init__.py','build.py','process_utils.py'):
        shutil.copyfile(p.NATIVE/name,native/name)
    foreign = output/'foreign.py'
    foreign.write_text('import os\nfrom pathlib import Path\nPath(os.environ["PROFILE_CODE_MARKER"]).write_text("executed")\nraise RuntimeError("foreign module executed")\n')
    for script in scripts:
        for relative in ('full/profile/input_paths.py','full/profile/selected_profile.py','full/profile/native_model.py','full/api/generate.py','full/api/output_paths.py','full/layout/generate.py','build.py','process_utils.py'):
            if relative == 'full/profile/'+script:
                continue  # The chosen Python entrypoint itself is the trust root.
            path = native/relative
            saved = path.read_bytes()
            for kind in ('symlink','dangling','hardlink'):
                path.unlink()
                if kind == 'hardlink':
                    os.link(foreign,path)
                else:
                    path.symlink_to(foreign if kind == 'symlink' else output/'missing-module')
                cmd = [sys.executable,str(native/'full/profile'/script),'--help']
                reject(script+' module '+relative+' '+kind,cmd,expected='input')
                path.unlink()
                path.write_bytes(saved)
    # Selected data leaf redirects must be rejected, not read and accepted by hash.
    leaf_copy = output/'selected-leaf-copy'
    shutil.copytree(selected,leaf_copy)
    for leaf in ('profile.json','source-record.json','ast-model/ast.json','source/imgui.h'):
        path = leaf_copy/leaf
        saved = path.read_bytes()
        private_target = output/('private-'+leaf.replace('/','-'))
        private_target.write_bytes(saved)
        for kind in ('symlink','dangling','hardlink'):
            path.unlink()
            if kind == 'hardlink':
                os.link(private_target,path)
            else:
                path.symlink_to(selected/leaf if kind == 'symlink' else output/'missing-data')
            for script in scripts[1:]:
                destination = output/(script+'-'+kind+'-'+leaf.replace('/','-'))
                reject(script+' data '+leaf+' '+kind,command(script,destination,leaf_copy),destination,expected='input')
            path.unlink()
            path.write_bytes(saved)
    p.require(p.new_output(output/'safe-future-sibling',[selected,enums]) == output/'safe-future-sibling','safe sibling rejected')
    passed.append('safe sibling output remains accepted')
    (output/'report.json').write_bytes(p.encoded({'schema':'playground_imgui.profile-input-path-tests','schemaVersion':1,'passed':passed,'count':len(passed),'skipped':[],'compilerCanaryExecuted':False,'redirectModuleExecuted':False,'protectedBytesAndMembershipPreserved':True}))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--selected',type=Path,required=True)
    parser.add_argument('--enums',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--ksa-folder',type=Path,required=True)
    args = parser.parse_args()
    run(args.selected,args.enums,args.output,args.ksa_folder)
