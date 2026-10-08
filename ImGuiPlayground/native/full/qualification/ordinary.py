"""Existing ordinary renderer/packaging gates from extracted sibling source folders."""
import os
import shutil


def run(args, tool):
    if args.bundle is None or args.anchor is None or args.selection is None:
        raise ValueError('ordinary requires extracted --bundle, --anchor and explicit selection')
    tool.verify_bundle(args.bundle, args.anchor)
    args.output.mkdir(parents=True)
    before = {str(p.relative_to(tool.ROOT)): tool.digest(p) for p in tool.source_files() if 'runtimes' in p.parts or 'fonts' in p.parts}
    parent = args.output / 'hostile parent with spaces'
    parent.mkdir()
    (parent / 'Directory.Build.props').write_text('<Project><Target Name="HostileProps" BeforeTargets="PrepareForBuild"><Error Text="hostile parent props imported" /></Target></Project>')
    (parent / 'Directory.Build.targets').write_text('<Project><Target Name="HostileTargets" BeforeTargets="PrepareForBuild"><Error Text="hostile parent targets imported" /></Target></Project>')
    (parent / 'Directory.Packages.props').write_text('<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>')
    (parent / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    work = parent / 'extracted siblings'
    work.mkdir()
    source = args.bundle / 'source'
    copied = {}
    for file in tool.source_files(source):
        dest = work / file.relative_to(source)
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(file, dest)
        copied[str(dest)] = tool.digest(file)
    checks = work / 'ImGuiPlayground.Checks/ImGuiPlayground.Checks.csproj'
    host = work / 'ImGuiPlayground/ImGuiPlayground.csproj'
    env = {k: v for k, v in os.environ.items() if k.casefold() not in ('ksafolder', 'ksa_dll_dir')}
    env.update(KSAFolder=str(args.selection), KSA_DLL_DIR=str(args.selection), PYTHONDONTWRITEBYTECODE='1')
    tool.command(['dotnet', 'build', str(checks), '--artifacts-path', str(args.output / 'ordinary-artifacts'), '-c', 'Release', '--nologo', '-v:minimal', '-p:TreatWarningsAsErrors=true'], args.output, 'ordinary-sibling-build', env=env)
    built = args.output / 'ordinary-artifacts/bin/ImGuiPlayground.Checks/release'
    tool.tree(built)
    # No native replacement here: these are the unchanged shipped assets.
    tool.command(['dotnet', str(built / 'ImGuiPlayground.Checks.dll')], args.output, 'ordinary-shipped-render-input', env=env, timeout=180)
    tool.command(['dotnet', str(built / 'ImGuiPlayground.Checks.dll'), '--packaging', str(host)], args.output, 'ordinary-all-target-packaging', env=env, timeout=600)
    clean_env = {k: v for k, v in env.items() if k.casefold() not in ('ksafolder', 'ksa_dll_dir')}
    tool.command(['dotnet', 'build', str(checks), '--artifacts-path', str(args.output / 'missing-selection-artifacts'), '--nologo', '-v:q'], args.output, 'missing-explicit-selection', env=clean_env, reject='Set KSA_DLL_DIR')
    conflicting = dict(env, KSA_DLL_DIR=str(args.output / 'not-an-installation'))
    property_text = tool.command(['dotnet', 'msbuild', str(host), '-nologo', '-getProperty:_PlaygroundKsaFolder'], args.output, 'selection-precedence', env=conflicting)
    tool.require(property_text.strip().rstrip('/\\') == str(args.selection).rstrip('/\\'), 'KSAFolder precedence changed')
    restore = tool.command(['dotnet', 'msbuild', str(host), '-nologo', '-getProperty:RestoreConfigFile'], args.output, 'source-nuget-boundary', env=env)
    tool.require(restore.strip() == str(work / 'ImGuiPlayground/NuGet.Config'), 'hostile parent NuGet selection')
    for path, expected in copied.items():
        tool.require(tool.digest(path) == expected, 'ordinary gate changed extracted source asset')
    after = {str(p.relative_to(tool.ROOT)): tool.digest(p) for p in tool.source_files() if 'runtimes' in p.parts or 'fonts' in p.parts}
    tool.require(before == after, 'ordinary gate changed original production assets/fonts')
    tool.verify_bundle(args.bundle, args.anchor)
    tool.write(args.output / 'results.json', {'schema': 'purr.full-ordinary.v1', 'status': 'passed', 'sourceBundleAnchor': args.anchor, 'siblingFoldersWithSpaces': True, 'hostileParentPropsTargetsPackagesNuget': True, 'existingRendererInputPngChecks': True, 'existingAllTargetPackagingAndRepublishChecks': True, 'missingSelectionRejected': True, 'selectionPrecedenceChecked': True, 'productionAssetsUnchanged': before, 'foreignExecution': False, 'privateDllsExported': False})
