"""Execute the actual built Checks -> Host boundary in fresh disposable processes."""
import json
import platform
import shutil


def verified_build(build, tool):
    manifest = tool.read(build / 'manifest.json')
    tool.require(manifest['schema'] == 'purr.combined.milestone1' and manifest['version'] == 1, 'combined build schema')
    tool.require(manifest['sourceCommit'] == tool.COMMIT and manifest['helperIdentity'] == tool.ACCESSOR_ID and manifest['configSha256'] == tool.digest(tool.NATIVE / 'include/playground_imgui_config.h'), 'combined profile/config/helper identity')
    tool.require(tool.read(build / 'source-freeze.json') == {str(f.relative_to(tool.ROOT)): tool.digest(f) for f in tool.source_files()}, 'combined source freeze drift')
    for path, expected in manifest['evidenceHashes'].items():
        tool.physical(path)
        tool.require(tool.digest(path) == expected, 'contributing evidence drift: ' + path)
    p, enums, layout = tool.dependencies()
    inputs = {k: tool.tree(v) for k, v in manifest['inputs'].items()}
    layout.verify_source(inputs['selected'])
    rows = tool.inventory(p, enums, inputs['selected'], inputs['enums'])
    tool.require(tool.read(build / 'exports.json') == rows, 'combined producer export inventory drift')
    tool.require([t['rid'] for t in manifest['targets']] == tool.RIDS, 'combined target inventory')
    for target in manifest['targets']:
        tool.require([a['kind'] for a in target['artifacts']] == ['production', 'fixture'], 'artifact kind inventory')
        for artifact in target['artifacts']:
            path = tool.physical(artifact['path'])
            tool.require(path == build / target['rid'] / artifact['kind'] / p.builder.read_json(tool.NATIVE / 'source.lock.json')['targets'][target['rid']]['file'], 'wrong artifact selection')
            tool.require(tool.digest(path) == artifact['sha256'], 'combined artifact drift')
    return manifest


def run(args, tool):
    tool.require(platform.system() == 'Darwin' and platform.machine() == 'arm64', 'macOS arm64 runtime only; foreign execution pending')
    manifest = verified_build(args.build, tool)
    p, _, _ = tool.dependencies()
    pins = tool.read(tool.FULL / 'api/mapping.json')['selectedAssemblies']
    for name, pin in pins.items():
        tool.require(tool.digest(args.managed_directory / (name + '.dll')) == pin['sha256'], 'selected contributor drift: ' + name)
    output = args.output
    output.mkdir(parents=True)
    build_command = ['dotnet', 'build', str(tool.ROOT / 'ImGuiPlayground.Checks/ImGuiPlayground.Checks.csproj'), '--artifacts-path', str(output / 'ordinary-artifacts'), '-c', 'Release', '--nologo', '-v:minimal', '-p:KSAFolder=' + str(args.managed_directory), '-p:TreatWarningsAsErrors=true']
    tool.logged(p.process, build_command, output, 'ordinary-project-build')
    built = output / 'ordinary-artifacts/bin/ImGuiPlayground.Checks/release'
    tool.require((built / 'ImGuiPlayground.Checks.dll').is_file(), 'real Checks project output missing')
    stage = output / 'stage'
    shutil.copytree(built, stage)
    native_record = next(a for t in manifest['targets'] if t['rid'] == 'osx-arm64' for a in t['artifacts'] if a['kind'] == 'fixture')
    native = tool.Path(native_record['path'])
    # Only this disposable copy replaces the ordinary prototype asset.
    shutil.copyfile(native, stage / 'libimgui.dylib')
    inputs = {k: tool.Path(v) for k, v in manifest['inputs'].items()}
    evidence = {'matrix.json': inputs['enums'] / 'matrix.json', 'accessors.json': inputs['accessors'] / 'manifest.json', 'binding-contract.json': tool.SCRATCH / 'binding-contract.json', 'native-layout-comparison.json': inputs['layout'] / 'osx-arm64/comparison.json', 'exports.json': args.build / 'exports.json'}
    for name, path in evidence.items():
        shutil.copyfile(path, stage / name)
    assemblies = {}
    for name, pin in pins.items():
        for folder in (args.managed_directory, built, stage):
            tool.require(tool.digest(folder / (name + '.dll')) == pin['sha256'], 'selected/built/staged contributor drift: ' + name)
        assemblies[name] = dict(pin, selected=str(args.managed_directory / (name + '.dll')), built=str(built / (name + '.dll')))
    for name in ('ImGuiPlayground.Checks', 'ImGuiPlayground'):
        assemblies[name] = {'sha256': tool.digest(built / (name + '.dll')), 'selected': str(built / (name + '.dll')), 'built': str(built / (name + '.dll'))}
    fixture = output / 'fixture-file.bin'
    fixture.write_bytes(b'combined accessor real native file-size fixture\n')
    receipt = {'schema': 'purr.combined.stage.v1', 'assemblies': assemblies, 'selectedDirectory': str(args.managed_directory), 'sourceNative': str(native), 'fixtureFile': str(fixture), 'files': {name: tool.digest(stage / name) for name in [*evidence, 'libimgui.dylib']}, 'buildManifestSha256': tool.digest(args.build / 'manifest.json')}
    receipt_path = output / 'stage-receipt.json'
    tool.write(receipt_path, receipt)
    results = []
    for mode in ('accessor', 'profile', 'manual', 'composition'):
        report = output / (mode + '-report.json')
        command = ['dotnet', str(stage / 'ImGuiPlayground.Checks.dll'), '--combined-components', mode, str(receipt_path), str(report)]
        tool.logged(p.process, command, output, mode, timeout=120)
        result = tool.read(report)
        tool.require(result['status'] == 'passed' and result['sourceSha256'] == result['stagedSha256'] == native_record['sha256'], 'combined runtime artifact identity')
        results.append({'mode': mode, 'processId': result['processId'], 'reportSha256': tool.digest(report)})
    tool.require(len({r['processId'] for r in results}) == len(results), 'component process isolation')
    # Real entrypoint rejects identity/staging errors before native load/unsafe access.
    negatives = []
    for name in ('contributor', 'native', 'matrix'):
        mutant = json.loads(json.dumps(receipt))
        if name == 'contributor':
            mutant['assemblies']['Brutal.ImGui']['sha256'] = '0' * 64
            diagnostic = 'selected/built/staged/loaded contributor: Brutal.ImGui'
        else:
            mutant['files']['libimgui.dylib' if name == 'native' else 'matrix.json'] = '0' * 64
            diagnostic = 'staged evidence hash:'
        changed = output / (name + '-negative.json')
        tool.write(changed, mutant)
        report = output / (name + '-must-not-report.json')
        tool.logged(p.process, ['dotnet', str(stage / 'ImGuiPlayground.Checks.dll'), '--combined-components', 'accessor', str(changed), str(report)], output, name + '-negative', timeout=60, reject=diagnostic)
        tool.require(not report.exists(), 'negative reached success report')
        negatives.append(name)
    # Wrong artifact selected with an internally consistent receipt still lacks component exports.
    wrong = output / 'production-stage'
    shutil.copytree(stage, wrong)
    production = next(a for t in manifest['targets'] if t['rid'] == 'osx-arm64' for a in t['artifacts'] if a['kind'] == 'production')
    shutil.copyfile(production['path'], wrong / 'libimgui.dylib')
    mutant = json.loads(json.dumps(receipt))
    mutant['sourceNative'] = production['path']
    mutant['files']['libimgui.dylib'] = production['sha256']
    tool.write(output / 'production-negative.json', mutant)
    tool.logged(p.process, ['dotnet', str(wrong / 'ImGuiPlayground.Checks.dll'), '--combined-components', 'accessor', str(output / 'production-negative.json'), str(output / 'production-must-not-report.json')], output, 'production-negative', timeout=60, reject='Unable to find an entry point')
    negatives.append('actual-production-instead-of-fixture')
    failure_stage = output / 'accessor-failure-stage'
    shutil.copytree(stage, failure_stage)
    bad_accessor = tool.read(failure_stage / 'accessors.json')
    bad_accessor['identity'] = '0' * 64
    tool.write(failure_stage / 'accessors.json', bad_accessor)
    failure_receipt = json.loads(json.dumps(receipt))
    failure_receipt['files']['accessors.json'] = tool.digest(failure_stage / 'accessors.json')
    tool.write(output / 'accessor-failure.json', failure_receipt)
    tool.logged(p.process, ['dotnet', str(failure_stage / 'ImGuiPlayground.Checks.dll'), '--combined-components', 'accessor', str(output / 'accessor-failure.json'), str(output / 'accessor-failure-must-not-report.json')], output, 'accessor-failure', timeout=60, reject='allocator callbacks restored before propagation')
    negatives.append('accessor-contract-failure-restores-allocator')
    tool.write(output / 'results.json', {'schema': 'purr.combined.managed.v1', 'buildManifestSha256': tool.digest(args.build / 'manifest.json'), 'actualProjectReferenceBuild': True, 'oneImageSha256': native_record['sha256'], 'componentProcesses': results, 'negatives': negatives, 'foreignExecution': False, 'rendererQualification': False})
