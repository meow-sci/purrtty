#!/usr/bin/env python3
"""Explicit disposable managed shape harness, never ordinary bin or production native assets."""
from __future__ import annotations
import argparse
import os
import shutil
from pathlib import Path
import importlib
import sys
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

SUPPLEMENTAL = ['profile_fixture_create','profile_fixture_destroy','profile_fixture_object','profile_fixture_sort_prepare','profile_fixture_observe','profile_fixture_key_event']


def run(selected, enums, output, selection, zig='zig'):
    output = p.new_output(output, [selected, enums, selection])
    safe_inputs.fixed_inputs()
    selected = safe_inputs.selected(selected)
    enums = safe_inputs.selected(enums)
    selection = safe_inputs.managed_selection(selection)
    selected = p.checked(selected, 'directory')
    selection = Path(selection).absolute()
    pins = p.api.load(p.API/'mapping.json')['selectedAssemblies']
    for name, pin in pins.items():
        p.require(p.digest((selection/(name+'.dll')).read_bytes()) == pin['sha256'], 'selected contributor drift: '+name)
    enums = p.checked(enums,'directory')
    enum_tool = importlib.import_module('enums')
    matrix = p.api.load(enums/'matrix.json')
    expected_matrix = enum_tool.validate(enum_tool.load_policy(),p.api.load(p.SCRATCH/'binding-contract.json'),p.api.load(p.LAYOUT/'mapping.json'),p.api.load(enums/'harvest.json'),p.api.load(selected/'selected-mapping.json'),enum_tool.checked_census(selected,enums))
    p.require(matrix == expected_matrix,'enum join manifest drift')
    enum_source, enum_exports = enum_tool.generate(matrix)
    p.require(enum_source == (enums/'enum-fixture.cpp').read_bytes(), 'enum fixture/manifest mismatch')
    p.require(all(r['sourceRecordSha256'] == p.digest((selected/'source-record.json').read_bytes()) for r in p.api.load(enums/'results.json')), 'enum source identity mismatch')
    manifest = p.api.load(selected/'profile.json')
    source = importlib.import_module('layout_gate').verify_source(selected)
    receipt = p.api.load(selected/'source-record.json')
    actual = {str(f.relative_to(source)):p.digest(f.read_bytes()) for f in sorted(source.rglob('*')) if f.is_file()}
    p.require(actual == receipt['sourceFiles'], 'prepared source changed')
    lock = p.builder.read_json(p.NATIVE/'source.lock.json')
    config = p.builder.verify_config_pin(lock)
    p.require(manifest['configSha256'] == p.digest(config.read_bytes()), 'config selection changed')
    files = [output/n for n in ('libprofile-fixture.dylib','fixture-build.log','fixture-build.command.json','symbols.log','supplemental-exports.json','Harness.cs','Harness.csproj','NuGet.Config','build.log','build.command.json','identities.json','run.log','run.command.json','managed-report.json')]
    p.preflight(files,[output,output/'build',output/'stage',output/'obj'])
    p.prepare(files,[output])
    command = p.compile_command(source,config,lock,'osx-arm64',zig)
    inputs = [str(source/n) for n in lock['build']['sourceFiles'] if n not in ('imgui.cpp','src/PlaygroundBrutalAdapter.cpp')] + [str(enums/'enum-fixture.cpp'),str(selected/'generated/wrappers.cpp'),str(p.HERE/'managed_fixture.cpp')]
    native = output/'libprofile-fixture.dylib'
    p.api_check.logged(command+['-dynamiclib']+inputs+['-o',str(native)],output/'fixture-build.log')
    names = p.api_check.export_names(native,'osx-arm64',output)
    expected = sorted([r['entryPoint'] for r in p.api.load(selected/'selected-mapping.json')['mappings']]+SUPPLEMENTAL+enum_exports+['__dso_handle','_mh_dylib_header'])
    p.require(sorted(names) == expected,'supplemental symbol inventory mismatch')
    (output/'supplemental-exports.json').write_bytes(p.encoded({'classification':'test-only instrumentation; production exports remain exactly1130','exports':SUPPLEMENTAL+enum_exports,'artifactSha256':p.digest(native.read_bytes())}))
    (output/'Harness.cs').write_bytes((p.HERE/'Harness.cs.in').read_bytes())
    checks = p.PROJECT.parent/'ImGuiPlayground.Checks'
    references = ['Brutal.ImGui','Brutal.Core.Common','Brutal.Core.Numerics','Brutal.Core.Strings']
    # Selection is supplied explicitly. KSAFolder precedence is resolved only by the CLI.
    from xml.sax.saxutils import escape
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImportDirectoryBuildProps>false</ImportDirectoryBuildProps><ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets></PropertyGroup><ItemGroup>'
    for f in [output/'Harness.cs',checks/'FullProfileAbiChecks.cs',checks/'FullEnumAbiChecks.cs']:
        project += '<Compile Include="'+escape(str(f))+'" />'
    for name in references:
        project += '<Reference Include="'+name+'"><HintPath>'+escape(str(selection/(name+'.dll')))+'</HintPath><Private>true</Private></Reference>'
    project += '<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0"/><PackageReference Include="Microsoft.Extensions.ObjectPool" Version="11.0.0-rc.1.26425.128"/></ItemGroup></Project>'
    (output/'Harness.csproj').write_text(project)
    shutil.copyfile(p.PROJECT/'NuGet.Config',output/'NuGet.Config')
    p.api_check.logged(['dotnet','build',str(output/'Harness.csproj'),'-c','Release','-o',str(output/'build'),'-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false'],output/'build.log')
    for name, pin in pins.items():
        p.require(p.digest((output/'build'/(name+'.dll')).read_bytes()) == pin['sha256'], 'built contributor drift')
    shutil.copytree(output/'build',output/'stage')
    staged_native = output/'stage/libimgui.dylib'
    shutil.copyfile(native,staged_native)
    staged_matrix = output/'stage/matrix.json'
    shutil.copyfile(enums/'matrix.json',staged_matrix)
    identity = {'matrixSha256':p.digest(staged_matrix.read_bytes()),'schema':'playground_imgui.profile-runtime-identities','schemaVersion':1,'assemblies':pins,'selectedDirectory':str(selection),'nativeSha256':p.digest(native.read_bytes()),'selectedBuiltStagedMatch':True}
    for name, pin in pins.items():
        p.require(p.digest((selection/(name+'.dll')).read_bytes()) == p.digest((output/'stage'/(name+'.dll')).read_bytes()) == pin['sha256'],'selected/staged contributor drift')
    (output/'identities.json').write_bytes(p.encoded(identity))
    p.api_check.logged(['dotnet',str(output/'stage/Harness.dll'),str(staged_native),str(output/'identities.json'),str(output/'managed-report.json'),str(staged_matrix)],output/'run.log',30)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--selected',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--enums',type=Path,required=True)
    # Match the existing explicit MSBuild selection contract (case-sensitive spelling).
    parser.add_argument('--ksa-folder',default=os.environ.get('KSAFolder') or os.environ.get('KSA_DLL_DIR'))  # noqa: SIM112
    args = parser.parse_args()
    if not args.ksa_folder:
        parser.error('explicit --ksa-folder/KSAFolder/KSA_DLL_DIR required')
    run(args.selected,args.enums,args.output,args.ksa_folder)
