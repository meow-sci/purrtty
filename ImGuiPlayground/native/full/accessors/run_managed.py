#!/usr/bin/env python3
"""Disposable C# accessor harness. One native owner; no resolver or production writes."""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import platform
import shutil
import sys
from pathlib import Path
from typing import Any
from xml.sax.saxutils import escape

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
runner = paths.load_module('purr_accessor_runner', HERE / 'run.py')
PROJECT = runner.PROJECT
PINS = {
    'Brutal.ImGui':'b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c',
    'Brutal.Core.Common':'4cbae1473d6a3759345f1eec8e62e6c7da1e19f8d10db8d13c005203ec590de4',
    'Brutal.Core.Numerics':'a217a6098116a1895e965b3a1d4fdee931e59a7a55f326c7d710b0d7eeccd17a',
}


def run(build: Path, output: Path, selected: Path) -> None:
    # All external inputs and the complete new output tree are checked before mkdir.
    paths.preflight_component(HERE)
    output=runner.new_output(output, protected=(build, selected))
    build=paths.input_tree(build)
    selected=paths.input_tree(selected)
    for p in [PROJECT/'NativeFieldAccessors.cs', PROJECT.parent/'ImGuiPlayground.Checks/FullAccessorChecks.cs',
              build/'manifest.json', build/'osx-arm64/build-record.json', build/'osx-arm64/libimgui.dylib']:
        paths.input_file(p)
    manifest=json.loads((build/'manifest.json').read_bytes())
    contract_identity=runner.digest(json.dumps({k:v for k,v in manifest.items() if k!='identity'},sort_keys=True,separators=(',',':')).encode())
    if contract_identity!=manifest['identity']:
        raise ValueError('manifest identity/content mismatch')
    record=json.loads((build/'osx-arm64/build-record.json').read_bytes())
    native=build/'osx-arm64/libimgui.dylib'
    if runner.digest(native.read_bytes()) != record['artifactSha256']:
        raise ValueError('native artifact hash changed')
    if manifest['identity'] != record['identity'] or manifest['identity'] not in (PROJECT/'NativeFieldAccessors.cs').read_text():
        raise ValueError('helper/native identity mismatch; explicit contract update required')
    selected_hashes={}
    for name,expected in PINS.items():
        path=selected/(name+'.dll')
        runner.reject_symlinks(path)
        selected_hashes[name]=runner.digest(path.read_bytes())
        if selected_hashes[name] != expected:
            raise ValueError('wrong explicitly selected ABI contributor: '+name)
    if platform.system()!='Darwin' or platform.machine()!='arm64':
        raise ValueError('this explicit local execution gate requires macOS arm64; foreign execution is pending')
    output.mkdir(parents=True)
    project=output/'harness'
    project.mkdir()
    sources=[PROJECT/'NativeFieldAccessors.cs',PROJECT.parent/'ImGuiPlayground.Checks/FullAccessorChecks.cs']
    references=['Brutal.ImGui','Brutal.Core.Common','Brutal.Core.Numerics','Brutal.Core.Strings']
    csproj='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>13.0</LangVersion>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
<ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets><ImportDirectoryPackagesProps>false</ImportDirectoryPackagesProps>
</PropertyGroup><ItemGroup><Compile Include="Program.cs" />
'''
    csproj+=''.join('<Compile Include="'+escape(str(p))+'" />\n' for p in sources)
    csproj+=''.join('<Reference Include="'+name+'"><HintPath>'+escape(str(selected/(name+'.dll')))+'</HintPath><Private>true</Private></Reference>\n' for name in references)
    csproj+='''<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.ObjectPool" Version="11.0.0-rc.1.26425.128" />
</ItemGroup></Project>'''
    (project/'AccessorHarness.csproj').write_text(csproj)
    (project/'Program.cs').write_text('''using System.Runtime.InteropServices;
using System.Text.Json;
// This disposable process is the SOLE native owner. No resolver, no host context.
nint library = NativeLibrary.Load(args[0]);
try {
    object report = FullAccessorChecks.Run(library, args[1], args[2], args[3]);
    File.WriteAllText(args[4], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
} finally { NativeLibrary.Free(library); }
''')
    dotnet=shutil.which('dotnet')
    if not dotnet:
        raise ValueError('dotnet unavailable')
    command=[dotnet,'build',str(project/'AccessorHarness.csproj'),'-c','Release','-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false','--nologo','-v:q']
    runner.logged(command,output/'managed-build.log',240)
    built=project/'bin/Release/net10.0'
    stage=output/'stage'
    shutil.copytree(built,stage)
    shutil.copy2(native,stage/'libaccessor-fixture.dylib')
    shutil.copy2(build/'manifest.json',stage/'manifest.json')
    fixture_file=output/'fixture-file.bin'
    fixture_file.write_bytes(b'accessor native file-size fixture\n')
    for name,expected in PINS.items():
        for directory in [selected,built,stage]:
            if runner.digest((directory/(name+'.dll')).read_bytes())!=expected:
                raise ValueError('selected/built/staged ABI identity changed')
    execute=[dotnet,str(stage/'AccessorHarness.dll'),str(stage/'libaccessor-fixture.dylib'),str(stage/'manifest.json'),str(selected),str(fixture_file),str(output/'managed-report.json')]
    runner.logged(execute,output/'managed-run.log',120)
    (output/'run-record.json').write_text(json.dumps({'schema':'purr.accessor-managed-run','version':1,'buildCommand':command,'executeCommand':execute,'selectedAbiHashes':selected_hashes,'nativeArtifactSha256':runner.digest((stage/'libaccessor-fixture.dylib').read_bytes()),'identity':manifest['identity'],'managedSourceHashes':{str(p.name):runner.digest(p.read_bytes()) for p in sources},'reportSha256':runner.digest((output/'managed-report.json').read_bytes()),'oneNativeOwner':True,'originalManagedDllUnchanged':True},indent=2)+'\n')


def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build',type=Path,required=True)
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--managed-directory',type=Path)
    args=parser.parse_args()
    # Same documented selection precedence; no game-path discovery/fallback.
    selected=os.environ.get('KSAFolder') or (str(args.managed_directory) if args.managed_directory else None) or os.environ.get('KSA_DLL_DIR')  # noqa: SIM112 - required existing case-sensitive selection alias
    if not selected:
        raise ValueError('explicit managed selection required')
    run(args.build,args.out,Path(selected))


if __name__=='__main__':
    try:
        main()
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr)
        sys.exit(1)
