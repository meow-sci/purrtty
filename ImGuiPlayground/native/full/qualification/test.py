"""Strict evidence mutations, archive/input boundaries and actual unsafe-child negatives."""
import ast
import copy
import hashlib
import json
import os
import shutil
import stat
import sys
import zipfile
from types import SimpleNamespace
from typing import Any, cast


def restore_canary_input(path, content, mode):
    path.unlink(missing_ok=True)
    path.write_bytes(content)
    os.chmod(path, mode)


def same_length_marker_canary(content, function, parameter):
    lines = content.splitlines(keepends=True)
    prefix = ('def ' + function + '(').encode()
    header = next((index for index, line in enumerate(lines) if line.startswith(prefix)), None)
    if header is None:
        raise ValueError('canary function header missing: ' + function)
    old = (parameter + '):').encode()
    new = (parameter + "=open('M','w')):").encode()
    if lines[header].count(old) != 1:
        raise ValueError('canary parameter signature mismatch: ' + function)
    lines[header] = lines[header].replace(old, new, 1)
    remaining = len(new) - len(old)
    for index, line in enumerate(lines):
        if index == header or remaining == 0:
            continue
        ending = b'\\r\\n' if line.endswith(b'\\r\\n') else b'\\n' if line.endswith(b'\\n') else b''
        body = line[:-len(ending)] if ending else line
        if not body.strip():
            removable = min(remaining, len(body))
            if removable:
                lines[index] = body[:-removable] + ending
                remaining -= removable
            if remaining and not body and ending:
                lines[index] = b''
                remaining -= len(ending)
        elif body.lstrip().startswith(b'#'):
            comment = len(body) - len(body.lstrip())
            removable = min(remaining, max(0, len(body) - comment - 1))
            if removable:
                lines[index] = body[:-removable] + ending
                remaining -= removable
    if remaining:
        raise ValueError('not enough blank/comment bytes for same-length canary: ' + function)
    mutated = b''.join(lines)
    if len(mutated) != len(content) or hashlib.sha256(mutated).digest() == hashlib.sha256(content).digest():
        raise ValueError('canary did not preserve size and change digest: ' + function)
    ast.parse(mutated.decode('utf-8'))
    return mutated


def target_local_contract(run_output, anchor, rid, tool):
    run_output = tool.physical(run_output, missing=True)
    tool.require(run_output.is_dir(), 'actual target run-output missing')
    run_output = tool.tree(run_output)
    run_path = run_output / 'results.json'
    tool.physical(run_path, missing=True)
    tool.require(run_path.is_file(), 'actual target run result missing')
    run_value = tool.read(run_path)
    tool.require(type(run_value) is dict, 'actual target run schema/status')
    run = cast(dict[str, Any], run_value)
    tool.require(run.get('schema') == 'purr.full-target-run.v1' and run.get('status') == 'passed', 'actual target run schema/status')
    tool.require(run.get('rid') == rid, 'actual target run target RID mismatch')
    tool.require(run.get('bundleManifestSha256') == anchor, 'actual target run bundle anchor mismatch')
    processes_value = run.get('processes')
    tool.require(type(processes_value) is list, 'actual target run process inventory')
    processes = cast(list[Any], processes_value)
    accessors = [item for item in processes if isinstance(item, dict) and item.get('mode') == 'accessor']
    tool.require(len(accessors) == 1, 'actual target accessor process record')
    process = accessors[0]
    process_dir = run_output / 'process-accessor'
    report_path = process_dir / 'report.json'
    contract_path = process_dir / 'actual-contract.json'
    guard_path = run_output / 'guard-process-accessor/guard.json'
    for path, label in ((report_path, 'accessor report'), (contract_path, 'actual contract'), (guard_path, 'accessor guard report')):
        tool.physical(path, missing=True)
        tool.require(path.is_file(), 'actual target ' + label + ' missing')
    report_hash = process.get('reportSha256')
    tool.require(isinstance(report_hash, str) and len(report_hash) == 64 and all(c in '0123456789abcdef' for c in report_hash), 'actual target accessor report hash schema')
    tool.require(tool.digest(report_path) == report_hash, 'actual target accessor report hash mismatch')
    report_value = tool.read(report_path)
    tool.require(type(report_value) is dict, 'actual target accessor report schema/status')
    report = cast(dict[str, Any], report_value)
    tool.require(report.get('schema') == 'purr.full-process.v1' and report.get('status') == 'passed' and report.get('mode') == 'accessor', 'actual target accessor report schema/status')
    tool.require(report.get('processId') == process.get('pid'), 'actual target accessor process identity')
    guard_value = report.get('guard')
    guard_file_value = tool.read(guard_path)
    tool.require(type(guard_value) is dict and type(guard_file_value) is dict, 'actual target accessor guard report schema')
    guard = cast(dict[str, Any], guard_value)
    guard_file = cast(dict[str, Any], guard_file_value)
    tool.require(guard == guard_file, 'actual target accessor guard report mismatch')
    tool.require(guard.get('schema') == 'purr.full-verification.v1' and guard.get('status') == 'passed' and guard.get('rid') == rid, 'actual target accessor guard schema/status/RID')
    tool.require(guard.get('bundleManifestSha256') == anchor, 'actual target accessor guard bundle anchor mismatch')
    tool.require(guard.get('artifactKind') == 'fixture' and guard.get('nativeArtifactSha256') == report.get('nativeSha256') == process.get('nativeSha256'), 'actual target accessor artifact identity')
    contract_value = tool.read(contract_path)
    tool.require(type(contract_value) is dict, 'actual target contract RID mismatch')
    contract = cast(dict[str, Any], contract_value)
    host = contract.get('host')
    tool.require(type(host) is dict and cast(dict[str, Any], host).get('rid') == rid, 'actual target contract RID mismatch')
    contract_hash = tool.digest(contract_path)
    tool.require(guard.get('actualContractSha256') == contract_hash, 'actual target contract hash mismatch')
    return contract, contract_hash


def run(args, tool):
    if args.bundle is None or args.anchor is None or args.run_output is None:
        raise ValueError('test requires --bundle, --anchor and actual target --run-output')
    tool.verify_bundle(args.bundle, args.anchor, require_bundled_source=True)
    rid = tool.host_rid()
    reference, actual_contract_hash = target_local_contract(args.run_output, args.anchor, rid, tool)
    bundled_reference_path = args.bundle / 'evidence/managed-contract.json'
    bundled_reference_value = tool.read(bundled_reference_path)
    tool.require(type(bundled_reference_value) is dict, 'bundled producer contract schema')
    bundled_reference = cast(dict[str, Any], bundled_reference_value)
    tool.require((args.run_output / 'process-accessor/actual-contract.json').read_bytes() != bundled_reference_path.read_bytes(), 'actual target contract must be target-local evidence, not bundled producer reference')
    args.output.mkdir(parents=True)
    verifier = tool.module('qualification_test_verifier', tool.HERE / 'verify.py')
    suffix = '.exe' if rid == 'win-x64' else ''
    for label, probe in [('native', 'layout-probe'), ('enum', 'enum-probe')]:
        text = tool.command([str(args.bundle / 'native' / rid / (probe + suffix))], args.output, label + '-probe', timeout=60)
        (args.output / (label + '.json')).write_text(text)
    native, enums = tool.read(args.output / 'native.json'), tool.read(args.output / 'enum.json')
    def validate(contract=reference, observed=native, enum_observed=enums):
        return verifier.validate(args.bundle, contract, observed, enum_observed, rid, tool.read)
    validate()
    results = [{'name': 'target-local-actual-contract-positive', 'passed': True, 'rid': rid, 'actualContractSha256': actual_contract_hash, 'bundledReferenceHostRid': bundled_reference['host']['rid']}, {'name': 'complete-target-local-evidence-positive', 'passed': True}]
    def reject(name, operation, diagnostic):
        try:
            operation()
        except (ValueError, KeyError, TypeError, RuntimeError) as error:
            tool.require(diagnostic in str(error), 'wrong negative diagnostic: ' + name + ': ' + str(error))
            results.append({'name': name, 'diagnostic': str(error)})
        else:
            raise ValueError('qualification: negative accepted: ' + name)

    run_record = tool.read(args.run_output / 'results.json')
    accessor_report = tool.read(args.run_output / 'process-accessor/report.json')
    accessor_guard = tool.read(args.run_output / 'guard-process-accessor/guard.json')
    actual_contract_bytes = (args.run_output / 'process-accessor/actual-contract.json').read_bytes()

    def run_input(name, *, include_record=True, include_contract=True, mutate=None):
        folder = args.output / ('run-input-' + name)
        folder.mkdir()
        process_dir = folder / 'process-accessor'
        guard_dir = folder / 'guard-process-accessor'
        process_dir.mkdir()
        guard_dir.mkdir()
        local_record = copy.deepcopy(run_record)
        local_report = copy.deepcopy(accessor_report)
        local_guard = copy.deepcopy(accessor_guard)
        contract_bytes = actual_contract_bytes
        if mutate is not None:
            contract_bytes = mutate(local_record, local_report, local_guard, contract_bytes)
        report_path = process_dir / 'report.json'
        tool.write(report_path, local_report)
        tool.write(guard_dir / 'guard.json', local_guard)
        if include_record:
            next(item for item in local_record['processes'] if item['mode'] == 'accessor')['reportSha256'] = tool.digest(report_path)
            tool.write(folder / 'results.json', local_record)
        if include_contract:
            (process_dir / 'actual-contract.json').write_bytes(contract_bytes)
        return folder

    cli_env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1')
    def reject_test_cli(name, run_path, diagnostic):
        destination = args.output / ('must-not-create-' + name)
        command = [sys.executable, str(tool.HERE / 'run.py'), 'test', '--bundle', str(args.bundle), '--anchor', args.anchor, '--selection', str(args.selection), '--run-output', str(run_path), '--output', str(destination)]
        result = tool.processes().run(command, capture_output=True, text=True, env=cli_env, timeout=180)
        output = (result.stdout or '') + (result.stderr or '')
        (args.output / (name + '.cli.log')).write_text(output)
        tool.require(result.returncode != 0 and diagnostic in output and not destination.exists(), 'actual test CLI provenance rejection failed: ' + name + ': ' + output)
        results.append({'name': name, 'diagnostic': diagnostic, 'outputAbsent': True})

    missing_record = args.output / 'run-input-missing-result'
    missing_record.mkdir()
    reject_test_cli('run-output-missing-result', missing_record, 'actual target run result missing')
    reject_test_cli('run-output-missing-contract', run_input('missing-contract', include_contract=False), 'actual target actual contract missing')

    def wrong_run_anchor(record, report, guard, contract_bytes):
        record['bundleManifestSha256'] = '0' * 64
        return contract_bytes

    def wrong_run_rid(record, report, guard, contract_bytes):
        record['rid'] = 'win-x64' if rid != 'win-x64' else 'linux-x64'
        return contract_bytes

    def changed_contract(record, report, guard, contract_bytes):
        return contract_bytes + b' '

    def changed_contract_hash(record, report, guard, contract_bytes):
        report['guard']['actualContractSha256'] = '0' * 64
        guard['actualContractSha256'] = '0' * 64
        return contract_bytes

    reject_test_cli('run-output-wrong-anchor', run_input('wrong-anchor', mutate=wrong_run_anchor), 'actual target run bundle anchor mismatch')
    reject_test_cli('run-output-wrong-rid', run_input('wrong-rid', mutate=wrong_run_rid), 'actual target run target RID mismatch')
    reject_test_cli('run-output-mutated-contract-bytes', run_input('mutated-contract-bytes', mutate=changed_contract), 'actual target contract hash mismatch')
    reject_test_cli('run-output-wrong-contract-hash', run_input('wrong-contract-hash', mutate=changed_contract_hash), 'actual target contract hash mismatch')

    expected_exports = verifier.export_inventory(args.bundle, tool.read)
    for change in ('missing', 'duplicate', 'extra', 'kind', 'classification'):
        rows = copy.deepcopy(expected_exports)
        if change == 'missing':
            rows.pop()
        elif change == 'duplicate':
            rows.append(rows[0])
        elif change == 'extra':
            rows.append(dict(rows[0], name='unknown_export'))
        elif change == 'kind':
            rows[0]['kind'] = 'writable-pointer-slot'
        else:
            rows[0]['component'] = 'helpers'
        reject('export-inventory-' + change, lambda rows=rows: verifier.validate_exports(rows, expected_exports), 'export')
    value = copy.deepcopy(reference)
    value['imports'][0]['isStatic'] = 1
    reject('boolean-not-an-integer-alias', lambda: validate(value), 'declarative closure')
    for group in ('imports', 'dynamicExports', 'types'):
        for change in ('missing', 'duplicate', 'extra'):
            value = copy.deepcopy(reference)
            if change == 'missing':
                value[group].pop()
            elif change == 'duplicate':
                value[group].append(copy.deepcopy(value[group][0]))
            else:
                value[group].append({'unknown': 'not-a-closed-declaration'})
            reject(group + '-' + change, lambda value=value: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    value['imports'][0]['dllImport']['callingConvention'] = 'StdCall'
    reject('wrong-import-convention', lambda: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    value['imports'][0]['return']['type'] = 'System.Private.CoreLib::System.Double'
    reject('wrong-return-signature', lambda: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    next(t for t in value['types'] if t['kind'] == 'delegate')['invoke']['return']['type'] = 'System.Private.CoreLib::System.Int64'
    reject('wrong-callback-signature', lambda: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    next(t for t in value['types'] if t.get('refReturnProperties'))['refReturnProperties'].pop()
    reject('omitted-ref-return-property', lambda: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    next(t for t in value['types'] if t.get('fields'))['fields'].pop()
    reject('omitted-field', lambda: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    next(t for t in value['types'] if t['kind'] == 'enum')['scalar']['signedness'] = 'unsigned'
    reject('wrong-enum-representation', lambda: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    next(t for t in value['types'] if t['kind'] == 'value')['kind'] = 'reference'
    reject('changed-type-kind', lambda: validate(value), 'declarative closure')
    value = copy.deepcopy(reference)
    next(t for t in value['types'] if t['measurement']['status'] == 'measured')['measurement'] = {'status': 'unavailable', 'reason': 'removed evidence'}
    reject('unmeasured-required-type', lambda: validate(value), 'measurement keys')
    value = copy.deepcopy(reference)
    next(f for t in value['types'] for f in t.get('fields', []) if f['runtimeOffset']['status'] == 'measured')['runtimeOffset']['status'] = 'unavailable'
    reject('unmeasured-required-field', lambda: validate(value), 'measurement status')
    value = copy.deepcopy(reference)
    next(f for t in value['types'] for f in t.get('fields', []) if f['storageWidth']['status'] == 'measured')['storageWidth'].pop('sizeBytes')
    reject('omitted-storage-width', lambda: validate(value), 'measurement keys')
    value = copy.deepcopy(reference)
    next(f for t in value['types'] for f in t.get('fields', []))['nativeDisposition'] = 'ignored'
    reject('unknown-field-disposition', lambda: validate(value), 'declarative closure')
    for metric, label in [('offsetBytes', 'runtimeOffset'), ('sizeBytes', 'storageWidth')]:
        value = copy.deepcopy(reference)
        vertex = next(t for t in value['types'] if t['id'] == 'Brutal.ImGui::Brutal.ImGuiApi.ImDrawVert')
        vertex['fields'][0][label][metric] += 1
        reject('actual-CLR-' + label, lambda value=value: validate(value), 'actual native/CLR layout mismatch')
    for group in ('entries', 'bitfieldDeclaredTypes', 'enumValues'):
        for change in ('missing', 'duplicate', 'extra'):
            value = copy.deepcopy(native)
            if change == 'missing':
                value[group].pop()
            else:
                value[group].append(value[group][0] if change == 'duplicate' else [999] * len(value[group][0]))
            reject('native-' + group + '-' + change, lambda value=value: validate(observed=value), 'target compiler/runtime')
    for column, name in [(2, 'size'), (3, 'alignment'), (4, 'offset'), (5, 'array-count'), (6, 'element-stride'), (7, 'signedness')]:
        value = copy.deepcopy(native)
        value['entries'][0][column] += 1
        reject('native-' + name, lambda value=value: validate(observed=value), 'target compiler/runtime')
    value = copy.deepcopy(native)
    value['version'] = True
    reject('native-boolean-schema-version', lambda: validate(observed=value), 'native version/pointer schema')
    value = copy.deepcopy(enums)
    value['useRepresentations'][0][0] = float(value['useRepresentations'][0][0])
    reject('enum-float-integer-alias', lambda: validate(enum_observed=value), 'malformed enum observations')
    value = copy.deepcopy(native)
    value['ignored'] = True
    reject('native-unknown-root-key', lambda: validate(observed=value), 'native root schema')
    value = copy.deepcopy(native)
    value['promotionDomainsPassed'] = False
    reject('native-contradictory-promotion', lambda: validate(observed=value), 'promotion domain checks')
    for change in ('missing', 'duplicate', 'representation'):
        value = copy.deepcopy(enums)
        if change == 'missing':
            del value['useRepresentations'][0]
        elif change == 'duplicate':
            value['useRepresentations'].insert(0, value['useRepresentations'][0])
        else:
            value['useRepresentations'][0][3] = 8
        reject('enum-use-' + change, lambda value=value: validate(enum_observed=value), 'enum-use observation')
    for name, content in [('duplicate-key', '{"a":1,"a":2}'), ('truncated', '{"a":'), ('nonfinite', '{"a":NaN}')]:
        path = args.output / (name + '.json')
        path.write_text(content)
        reject(name, lambda path=path: tool.read(path), 'duplicate JSON key' if name == 'duplicate-key' else 'nonfinite JSON' if name == 'nonfinite' else 'Expecting value')
    zero = '@test = constant [2 x [3 x i64]] [[3 x i64] zeroinitializer, [3 x i64] [i64 1, i64 -2, i64 3]], align 8'
    tool.require(tool.llvm_table(zero, 'test', 3) == [[0, 0, 0], [1, -2, 3]], 'LLVM mixed zero parser positive')
    results.append({'name': 'LLVM-mixed-zero-positive', 'passed': True})
    for label, text in [('unsupported', zero.replace('zeroinitializer', 'undef')), ('omitted', zero.replace('[2 x [3 x i64]]', '[3 x [3 x i64]]')), ('duplicate-table', zero + '\n' + zero), ('operator', zero.replace('i64 1', 'i64 add (i64 1, i64 0)'))]:
        reject('LLVM-' + label, lambda text=text: tool.llvm_table(text, 'test', 3), 'target LLVM table')

    manifest_validator = tool.module('test_manifest', tool.HERE / 'extract_bundle.py')
    manifest = tool.read(args.bundle / 'bundle.json')
    for change, diagnostic in [('extra-key', 'schema keys'), ('wrong-profile', 'source/config/patch'), ('boolean-version', 'schema/version'), ('missing-contributor', 'contributor identities'), ('wrong-compiler', 'compiler identity')]:
        value = copy.deepcopy(manifest)
        if change == 'extra-key':
            value['claim'] = 'passed'
        elif change == 'wrong-profile':
            value['sourceProfile']['sourceCommit'] = '0' * 40
        elif change == 'boolean-version':
            value['version'] = True
        elif change == 'missing-contributor':
            value['managedPins'].pop('Brutal.ImGui')
        else:
            value['toolchain']['zigVersion'] = 'unknown'
        reject('manifest-' + change, lambda value=value: manifest_validator.validate_manifest(value), diagnostic)
    malformed_manifest = copy.deepcopy(manifest)
    malformed_manifest['sourceProfile']['configSha256'] = '0' * 64
    blob = json.dumps(malformed_manifest).encode()
    archive = args.output / 'wrong-profile.zip'
    with zipfile.ZipFile(archive, 'w') as z:
        item = zipfile.ZipInfo('bundle.json')
        item.create_system = 3
        item.external_attr = (stat.S_IFREG | 0o644) << 16
        z.writestr(item, blob)
    destination = args.output / 'must-not-extract-profile'
    reject('archive-wrong-profile', lambda: tool.extract(SimpleNamespace(archive=archive, output=destination, anchor=hashlib.sha256(blob).hexdigest())), 'source/config/patch')
    tool.require(not destination.exists(), 'malformed profile archive created destination')
    # Archive metadata is fully rejected before any extraction writes.
    for index, (name, mode) in enumerate([('../escape', stat.S_IFREG), ('/absolute', stat.S_IFREG), ('link', stat.S_IFLNK), ('device', stat.S_IFCHR), ('duplicate', stat.S_IFREG)]):
        archive = args.output / ('bad-' + str(index) + '.zip')
        with zipfile.ZipFile(archive, 'w') as z:
            item = zipfile.ZipInfo(name)
            item.create_system = 3
            item.external_attr = (mode | 0o644) << 16
            z.writestr(item, b'bad')
            if name == 'duplicate':
                second = zipfile.ZipInfo('DUPLICATE')
                second.create_system = 3
                second.external_attr = (mode | 0o644) << 16
                z.writestr(second, b'bad')
        destination = args.output / ('must-not-extract-' + str(index))
        reject('archive-' + str(index), lambda archive=archive, destination=destination: tool.extract(SimpleNamespace(archive=archive, output=destination, anchor=args.anchor)), 'archive member' if mode != stat.S_IFREG or name == 'duplicate' else 'unsafe bundle relative path')
        tool.require(not destination.exists(), 'rejected archive wrote destination')

    # Exercise redirected local inputs and integrity-before-import on actual CLI routes.
    controlled_bundle = args.output / 'controlled-bundle'
    shutil.copytree(args.bundle, controlled_bundle)
    marker = args.output / 'must-not-import'
    canary = args.output / 'canary.py'
    canary.write_text('from pathlib import Path\nPath(' + repr(str(marker)) + ').write_text("executed")\n')
    entry = controlled_bundle / 'source/ImGuiPlayground/native/full/qualification/run.py'
    source_root = controlled_bundle / 'source'
    protected_paths = [source_root / 'ImGuiPlayground/native/full/qualification/verify.py', source_root / 'ImGuiPlayground/native/full/layout/generate.py', source_root / 'ImGuiPlayground/native/include/playground_imgui_config.h', source_root / 'ImGuiPlayground/native/full/qualification/__init__.py', source_root / 'ImGuiPlayground/native/full/qualification/__pycache__/canary.pyc', controlled_bundle / 'native/osx-arm64/production/libimgui.dylib']
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1')
    for index, path in enumerate(protected_paths):
        existed = path.exists()
        parent_created = not path.parent.exists()
        path.parent.mkdir(parents=True, exist_ok=True)
        if not existed:
            path.write_bytes(b'controlled empty cache/initializer fixture')
        original = path.read_bytes()
        original_mode = stat.S_IMODE(path.stat().st_mode)
        try:
            for kind in ('symlink', 'dangling', 'hardlink'):
                path.unlink(missing_ok=True)
                if kind == 'hardlink':
                    os.link(canary, path)
                else:
                    path.symlink_to(canary if kind == 'symlink' else args.output / 'missing-canary')
                destination = args.output / ('must-not-create-' + str(index) + kind)
                try:
                    result = tool.processes().run([sys.executable, str(entry), 'run', '--bundle', str(controlled_bundle), '--anchor', args.anchor, '--selection', str(args.selection), '--output', str(destination)], capture_output=True, text=True, env=env, timeout=30)
                    diagnostic = 'nonregular/shared path' if kind == 'hardlink' else 'redirect:'
                    stderr = result.stderr or ''
                    tool.require(result.returncode != 0 and diagnostic in stderr and not destination.exists() and not marker.exists(), 'actual redirected-input guard failed')
                    results.append({'name': 'entrypoint-' + str(index) + '-' + kind, 'diagnostic': diagnostic, 'destinationAndMarkerAbsent': True})
                finally:
                    restore_canary_input(path, original, original_mode)
        finally:
            if not existed:
                path.unlink(missing_ok=True)
            if parent_created and path.parent.exists():
                path.parent.rmdir()
    for helper, mode, function, parameter in [('extract_bundle.py', 'run', 'extract', 'destination'), ('test.py', 'test', 'run', 'tool'), ('ordinary.py', 'ordinary', 'run', 'tool')]:
        mutated_bundle = args.output / ('canary-bundle-' + helper.removesuffix('.py'))
        shutil.copytree(args.bundle, mutated_bundle)
        helper_path = mutated_bundle / 'source/ImGuiPlayground/native/full/qualification' / helper
        original = helper_path.read_bytes()
        mutated = same_length_marker_canary(original, function, parameter)
        tool.require(len(mutated) == len(original) and hashlib.sha256(mutated).digest() != hashlib.sha256(original).digest(), 'same-size helper mutation did not change only content: ' + helper)
        helper_path.write_bytes(mutated)
        canary_cwd = args.output / ('positive-control-' + helper.removesuffix('.py'))
        canary_cwd.mkdir()
        helper_marker = canary_cwd / 'M'
        positive = tool.processes().run([sys.executable, '-c', 'import runpy,sys; runpy.run_path(sys.argv[1], run_name="qualification_canary")', str(helper_path)], cwd=str(canary_cwd), capture_output=True, text=True, env=env, timeout=30)
        positive_output = (positive.stdout or '') + (positive.stderr or '')
        (args.output / (helper.removesuffix('.py') + '-canary-positive.cli.log')).write_text(positive_output)
        tool.require(positive.returncode == 0 and helper_marker.is_file(), 'same-size helper canary positive control failed: ' + helper)
        helper_marker.unlink()
        destination = args.output / ('must-not-create-' + helper.removesuffix('.py'))
        command = [sys.executable, str(mutated_bundle / 'source/ImGuiPlayground/native/full/qualification/run.py'), mode, '--bundle', str(mutated_bundle), '--anchor', args.anchor, '--selection', str(args.selection), '--output', str(destination)]
        if mode == 'test':
            command.extend(['--run-output', str(args.run_output)])
        result = tool.processes().run(command, cwd=str(canary_cwd), capture_output=True, text=True, env=env, timeout=60)
        diagnostic = 'bundle file hash: source/ImGuiPlayground/native/full/qualification/' + helper
        output = (result.stdout or '') + (result.stderr or '')
        (args.output / (helper.removesuffix('.py') + '-canary.cli.log')).write_text(output)
        tool.require(result.returncode != 0 and diagnostic in output and not destination.exists() and not helper_marker.exists(), 'helper imported before bundle hash rejection: ' + helper)
        results.append({'name': 'preimport-' + helper.removesuffix('.py'), 'diagnostic': diagnostic, 'sameLengthMemberMutation': True, 'positiveControlExecuted': True, 'destinationAndMarkerAbsent': True})

    # A valid selected bundle cannot authorize helpers from a different local source tree.
    unselected_source = args.output / 'unselected-runner-source'
    shutil.copytree(args.bundle, unselected_source)
    local_test = unselected_source / 'source/ImGuiPlayground/native/full/qualification/test.py'
    local_marker = args.output / 'must-not-import-unselected-test'
    with local_test.open('ab') as stream:
        stream.write(('\nfrom pathlib import Path\nPath(' + repr(str(local_marker)) + ').write_text("executed")\n').encode())
    unselected_output = args.output / 'must-not-create-unselected-source'
    unselected_command = [sys.executable, str(unselected_source / 'source/ImGuiPlayground/native/full/qualification/run.py'), 'test', '--bundle', str(args.bundle), '--anchor', args.anchor, '--selection', str(args.selection), '--run-output', str(args.run_output), '--output', str(unselected_output)]
    unselected_result = tool.processes().run(unselected_command, capture_output=True, text=True, env=env, timeout=60)
    unselected_text = (unselected_result.stdout or '') + (unselected_result.stderr or '')
    (args.output / 'unselected-source.cli.log').write_text(unselected_text)
    tool.require(unselected_result.returncode != 0 and 'runner source differs from anchored selected bundle' in unselected_text and not unselected_output.exists() and not local_marker.exists(), 'runner accepted a different local source tree')
    results.append({'name': 'unselected-local-source', 'diagnostic': 'runner source differs from anchored selected bundle', 'destinationAndMarkerAbsent': True})

    tool.processes().run([sys.executable, str(canary)], check=True, timeout=30)
    tool.require(marker.read_text() == 'executed', 'canary positive control')
    marker.unlink()
    results.append({'name': 'canary-positive', 'passed': True})
    for label, destination in [('inside-bundle', controlled_bundle / 'new-output'), ('ancestor-of-bundle', controlled_bundle.parent), ('inside-selection', args.selection / 'new-output')]:
        result = tool.processes().run([sys.executable, str(entry), 'run', '--bundle', str(controlled_bundle), '--anchor', args.anchor, '--selection', str(args.selection), '--output', str(destination)], capture_output=True, text=True, env=env, timeout=30)
        stderr = result.stderr or ''
        tool.require(result.returncode != 0 and ('overlaps protected input' in stderr or 'fresh output required' in stderr), 'nested/ancestor output accepted')
        results.append({'name': label, 'diagnostic': stderr.strip()})
    if args.run_output is not None:
        original = tool.read(args.run_output / 'renderer-stage.json')
        for mode in ('wrong-anchor', 'wrong-target', 'wrong-built', 'wrong-selected-contributor', 'wrong-built-contributor', 'wrong-staged-contributor', 'wrong-glfw', 'fixture-as-production'):
            stage = args.output / ('stage-' + mode)
            shutil.copytree(original['stageDirectory'], stage)
            receipt = copy.deepcopy(original)
            receipt['stageDirectory'] = str(stage)
            anchor = args.anchor
            expected = ''
            if mode == 'wrong-anchor':
                anchor = '0' * 64
                expected = 'independent manifest anchor mismatch'
            elif mode == 'wrong-target':
                receipt['rid'] = 'win-x64' if rid != 'win-x64' else 'linux-x64'
                expected = 'actual process target'
            elif mode == 'wrong-built':
                receipt['managedHashes']['ImGuiPlayground.dll'] = '0' * 64
                expected = 'actual built Host/Checks boundary'
            elif mode in ('wrong-selected-contributor', 'wrong-built-contributor'):
                source = args.output / ('input-' + mode)
                shutil.copytree(stage, source)
                with (source / 'Brutal.Core.Common.dll').open('ab') as stream:
                    stream.write(b'controlled trailing identity mutation')
                receipt['selectedDirectory' if mode == 'wrong-selected-contributor' else 'builtDirectory'] = str(source)
                expected = 'selected/built/staged/loaded contributor'
            elif mode == 'wrong-glfw':
                glfw_name = {'osx-arm64': 'libglfw.dylib', 'linux-x64': 'libglfw.so', 'win-x64': 'glfw3.dll'}[rid]
                with (stage / glfw_name).open('ab') as stream:
                    stream.write(b'controlled trailing identity mutation')
                expected = 'ordinary pinned GLFW stage/source identity'
            elif mode == 'wrong-staged-contributor':
                with (stage / 'Brutal.Core.Numerics.dll').open('ab') as stream:
                    stream.write(b'controlled trailing identity mutation')
                expected = 'selected/built/staged/loaded contributor'
            else:
                shutil.copyfile(args.bundle / 'native' / rid / 'fixture' / tool.RIDS[rid][2], stage / tool.RIDS[rid][2])
                expected = 'selected/staged native artifact mismatch'
            receipt_path = args.output / (mode + '.json')
            tool.write(receipt_path, receipt)
            child_output = args.output / ('child-' + mode)
            tool.command(['dotnet', str(stage / 'ImGuiPlayground.Checks.dll'), '--full-qualification', 'renderer', str(args.bundle), anchor, sys.executable, str(receipt_path), str(child_output)], args.output, mode, timeout=180, reject=expected)
            tool.require(not (child_output / 'native-open.marker').exists() and not (child_output / 'report.json').exists(), 'negative reached native owner')
            results.append({'name': mode, 'diagnostic': expected, 'nativeOwnerMarkerAbsent': True})
    tool.verify_bundle(args.bundle, args.anchor)
    tool.write(args.output / 'results.json', {'schema': 'purr.full-negative-tests.v1', 'count': len(results), 'skipped': [], 'cases': results, 'actualChildNegatives': args.run_output is not None})
