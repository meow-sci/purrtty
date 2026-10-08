#!/usr/bin/env python3
"""Quiet fail-closed contract/path/process-tree mutation tests; no native stubs."""
import argparse
import copy
import importlib
import importlib.util
import json
import os
import stat
import subprocess
import sys
import time
from pathlib import Path

sys.dont_write_bytecode = True
# Only stdlib code runs before the bootstrap helper and entrypoint are checked.
# Keep this small guard in both entrypoints: importing paths.py to validate itself
# would execute a redirected file before the boundary could reject it.
for candidate in (Path(__file__).absolute(), Path(__file__).absolute().with_name('paths.py')):
    for item in reversed((candidate, *candidate.parents)):
        info = item.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0):
            raise ValueError('redirect forbidden before import: ' + str(item))
        if item != candidate and not stat.S_ISDIR(info.st_mode):
            raise ValueError('non-directory import ancestor: ' + str(item))
        if item == candidate and (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1):
            raise ValueError('non-regular/shared import leaf: ' + str(item))
HERE = Path(__file__).absolute().parent
spec = importlib.util.spec_from_file_location('purr_manual_paths', HERE / 'paths.py')
if spec is None or spec.loader is None:
    raise ValueError('manual path bootstrap unavailable')
paths = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = paths
spec.loader.exec_module(paths)
paths.component_inputs()
sys.path.insert(0, str(HERE.parents[3]))
contract = paths.load_module('purr_manual_contract', HERE / 'contract.py')
entrypoint_test = paths.load_module('purr_manual_entrypoint_test', HERE / 'entrypoint_test.py')
process = importlib.import_module('ImGuiPlayground.native.process_utils')


def run(name):
    manifest = contract.load(paths)
    binding = json.loads((HERE.parents[2] / '.tmp/native/binding-contract.json').read_text())
    files = {name: path.read_bytes() for name, path in contract.source_paths().items()}
    output = paths.new_tree(name)
    passed = []

    def rejected(label, action):
        try:
            action()
        except (ValueError, KeyError):
            passed.append(label)
        else:
            raise AssertionError('negative accepted: ' + label)

    for group in ('imports', 'dynamicExports', 'fixtureExports'):
        for i, entry in enumerate(manifest[group]):
            for mutation in ('missing', 'duplicate', 'signature', 'unknown'):
                altered = copy.deepcopy(manifest)
                if mutation == 'missing':
                    del altered[group][i]
                elif mutation == 'duplicate':
                    altered[group].append(copy.deepcopy(entry))
                elif mutation == 'unknown':
                    altered[group][i]['name'] = 'UnexpectedExport'
                else:
                    key = 'declaration' if group == 'fixtureExports' else 'nativeDeclaration'
                    altered[group][i][key] += ' altered'
                rejected(group + '/' + entry['name'] + '/' + mutation, lambda altered=altered: contract.validate(altered, binding, files))
            if group != 'fixtureExports':
                altered = copy.deepcopy(manifest)
                altered[group][i]['managedDeclaration']['library'] = 'another-library'
                rejected(group + '/' + entry['name'] + '/managed-signature', lambda altered=altered: contract.validate(altered, binding, files))
    for file in files:
        changed = dict(files)
        changed[file] += b'\n/* deliberate source mutation */\n'
        rejected('source/' + file, lambda changed=changed: contract.validate(manifest, binding, changed))
    for mutation in ('missing', 'extra'):
        altered = copy.deepcopy(manifest)
        if mutation == 'missing':
            del altered['sourceSha256']['manual.cpp']
        else:
            altered['sourceSha256']['unknown.cpp'] = 'unowned'
        rejected('source-coverage/' + mutation, lambda altered=altered: contract.validate(altered, binding, files))
    for key in ('inputContractSha256', 'portableContractSha256', 'schema'):
        altered = copy.deepcopy(manifest)
        altered[key] = 'wrong'
        rejected(key, lambda altered=altered: contract.validate(altered, binding, files))
    for group in ('imports', 'dynamicExports', 'fixtureExports'):
        altered = copy.deepcopy(manifest)
        altered[group].append({'name': 'UninventoriedSupplement'})
        rejected('extra/' + group, lambda altered=altered: contract.validate(altered, binding, files))

    # No pre-existing root/ancestor/leaf redirect can be hidden by resolve().
    # Dedicated fresh tree prevents writes to protected contract/source inputs.
    for bad in ('../escape', '/absolute', '.', '', 'a/b', 'a\\b', '..', 'x' * 81):
        rejected('name/' + bad, lambda bad=bad: paths.new_tree(bad))
    rejected('existing-run', lambda: paths.new_tree(name))
    real = output / 'real'
    real.mkdir()
    (real / 'input').write_text('protected')
    for target in (real, output / 'missing'):
        for location in ('root', 'ancestor', 'leaf'):
            link = output / (location + ('-dangling' if not target.exists() else '-existing'))
            link.symlink_to(target, target_is_directory=True)
            candidate = link / 'child' if location != 'leaf' else link
            rejected('redirect/' + link.name, lambda candidate=candidate: paths.inspect(candidate))
    regular = output / 'regular'
    regular.write_text('not a directory')
    rejected('non-directory ancestor', lambda: paths.inspect(regular / 'child'))
    hardlink = output / 'hardlink'
    os.link(real / 'input', hardlink)
    rejected('shared leaf', lambda: paths.inspect(hardlink))
    contract.require((real / 'input').read_text() == 'protected', 'path test overwrote input')

    # Child starts its own long-running grandchild. Both block on pipe reads,
    # not fixed sleeps. Timeout must kill/reap the entire owned process group.
    script = output / 'process_tree.py'
    pidfile = output / 'grandchild.pid'
    script.write_text('''import os, subprocess, sys
child = subprocess.Popen([sys.executable, '-c', 'import os; os.read(0, 1)'], stdin=subprocess.PIPE)
with open(sys.argv[1], 'w') as f:
    f.write(str(child.pid))
    f.flush()
os.read(0, 1)
''')
    started = time.monotonic()
    try:
        with process.managed_process([sys.executable, str(script), str(pidfile)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE) as owned:
            owned.wait(timeout=2)
    except subprocess.TimeoutExpired:
        pass
    else:
        raise AssertionError('process-tree timeout not triggered')
    contract.require(time.monotonic() - started < 15, 'process-tree cleanup bound')
    contract.require(pidfile.is_file(), 'grandchild did not start')
    pid = int(pidfile.read_text())
    # A killed, orphaned zombie can remain until system reaping; it cannot run.
    status = process.run(['ps', '-p', str(pid), '-o', 'stat='], capture_output=True, text=True, timeout=5)
    contract.require(status.returncode != 0 or status.stdout.strip().startswith('Z'), 'grandchild still running')
    passed.append('shared process-group timeout killed parent and grandchild')
    entrypoint_cases = entrypoint_test.run(output, paths, process)
    passed += ['entrypoint/' + case['entrypoint'] + '/' + case['case'] for case in entrypoint_cases]
    report = {'schema': 'purr.manual.negative-checks.v1', 'passed': passed, 'count': len(passed), 'entrypointCases': len(entrypoint_cases), 'skipped': 0}
    (output / 'results.json').write_text(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    run(parser.parse_args().output)
