"""Bounded integration inventories/receipts and actual entrypoint preflight negatives."""
import copy
import os
import shutil
import sys


def run(args, tool):
    consumer = tool.load('combined_consumer_test', tool.HERE / 'managed.py')
    manifest = consumer.verified_build(args.build, tool)
    p, _, _ = tool.dependencies()
    output = args.output
    output.mkdir(parents=True)
    results = []

    def reject(name, action, diagnostic):
        try:
            action()
        except (ValueError, OSError) as error:
            tool.require(diagnostic in str(error), 'wrong negative: ' + name + ': ' + str(error))
            results.append({'name': name, 'rejection': str(error)})
        else:
            raise ValueError('combined: negative accepted: ' + name)

    rows = tool.read(args.build / 'exports.json')
    for fixture in (False, True):
        names = [r['name'] for r in rows if fixture or r['surface'] == 'production']
        tool.export_match(names, rows, fixture)
        for name, mutant in [('missing', names[1:]), ('extra', names + ['not_owned']), ('duplicate', names + [names[0]])]:
            reject(('fixture-' if fixture else 'production-') + name, lambda mutant=mutant, fixture=fixture: tool.export_match(mutant, rows, fixture), 'missing/extra/duplicate')
    production = [r['name'] for r in rows if r['surface'] == 'production']
    leaked = production + [next(r['name'] for r in rows if r['surface'] == 'fixture')]
    reject('fixture-leakage', lambda: tool.export_match(leaked, rows, False), 'fixture-leaking')
    mutant = copy.deepcopy(rows)
    a = next(r for r in mutant if r['surface'] == 'production')
    b = next(r for r in mutant if r['surface'] == 'fixture')
    a['surface'], b['surface'] = b['surface'], a['surface']
    reject('misclassified-surface', lambda: tool.validate_inventory(mutant), 'misclassified fixture/production')
    mutant = copy.deepcopy(rows)
    next(r for r in mutant if r['kind'] == 'writable-pointer-slot')['kind'] = 'function'
    reject('misclassified-slot', lambda: tool.validate_inventory(mutant), 'misclassified export kind')
    reject('duplicate-producer', lambda: tool.validate_inventory(rows + [rows[0]]), 'duplicate inventory')
    for field, value in [('sourceCommit', '0' * 40), ('configSha256', '0' * 64), ('helperIdentity', '0' * 64)]:
        folder = output / ('wrong-' + field)
        folder.mkdir()
        bad = copy.deepcopy(manifest)
        bad[field] = value
        tool.write(folder / 'manifest.json', bad)
        reject('wrong-' + field, lambda folder=folder: consumer.verified_build(folder, tool), 'profile/config/helper identity')
    folder = output / 'wrong-source'
    folder.mkdir()
    tool.write(folder / 'manifest.json', manifest)
    tool.write(folder / 'source-freeze.json', {})
    reject('wrong-component-source', lambda: consumer.verified_build(folder, tool), 'source freeze drift')

    # Real subprocess entrypoints: checked copied controlled inputs, marker must never run.
    copy_root = output / 'input-cases'
    copy_root.mkdir()
    for name in ('ImGuiPlayground', 'ImGuiPlayground.Checks'):
        shutil.copytree(tool.ROOT / name, copy_root / name, ignore=shutil.ignore_patterns('.tmp', 'bin', 'obj', '__pycache__'))
    copied = copy_root / 'ImGuiPlayground'
    scratch = copied / '.tmp/native'
    shutil.copytree(tool.SCRATCH / 'source', scratch / 'source')
    for name in ('binding-contract.json', 'imgui-' + tool.COMMIT + '.tar.gz'):
        shutil.copyfile(tool.SCRATCH / name, scratch / name)
    marker = output / 'must-not-run'
    canary = output / 'canary.py'
    canary.write_text('from pathlib import Path\nPath(' + repr(str(marker)) + ').write_text("executed")\n')
    compiler = output / 'compiler-marker'
    compiler.write_text('#!/bin/sh\nprintf executed > ' + str(marker) + '\nexit 77\n')
    compiler.chmod(0o755)
    entry = copied / 'native/full/combined/run.py'
    command = [sys.executable, str(entry), 'build']
    command += [arg for key, value in manifest['inputs'].items() for arg in ('--' + key, value)]
    command += ['--zig', str(compiler)]
    env = {k: v for k, v in os.environ.items() if k.casefold() not in ('ksafolder', 'ksa_dll_dir')}
    env['PYTHONDONTWRITEBYTECODE'] = '1'
    cases = [copied / 'native/full/profile/enums.py', copied / 'native/full/accessors/fixture.cpp', copied / 'native/include/playground_imgui_config.h', scratch / 'binding-contract.json', copied / 'native/full/combined/managed.py']
    for index, path in enumerate(cases):
        original = path.read_bytes()
        for kind in ('symlink', 'dangling', 'hardlink'):
            path.unlink()
            if kind == 'hardlink':
                os.link(canary, path)
            else:
                path.symlink_to(canary if kind == 'symlink' else output / 'missing-canary')
            destination = scratch / 'combined' / (str(index) + '-' + kind)
            try:
                result = p.process.run(command + ['--output', str(destination)], capture_output=True, text=True, env=env, timeout=30)
                diagnostic = 'nonregular/shared input' if kind == 'hardlink' else 'redirect:'
                tool.require(result.returncode != 0 and diagnostic in result.stderr, 'entrypoint wrong rejection: ' + str(path))
                tool.require(not marker.exists() and not destination.exists(), 'preflight ran compiler/import or wrote output')
                (output / (str(index) + '-' + kind + '.log')).write_text(result.stdout + result.stderr)
                results.append({'name': str(path.relative_to(copy_root)) + ':' + kind, 'rejection': diagnostic, 'markerAbsent': True, 'destinationAbsent': True})
            finally:
                path.unlink()
                path.write_bytes(original)
    for name, destination, diagnostic in [('traversal', scratch / 'combined/../escape', 'parent traversal'), ('input-overlap', scratch / 'source' / tool.COMMIT / 'new-output', 'outside owned scratch'), ('existing', output, 'outside owned scratch')]:
        result = p.process.run(command + ['--output', str(destination)], capture_output=True, text=True, env=env, timeout=30)
        tool.require(result.returncode != 0 and diagnostic in result.stderr and not marker.exists(), 'wrong output negative: ' + name)
        (output / (name + '.log')).write_text(result.stdout + result.stderr)
        results.append({'name': name, 'rejection': diagnostic, 'markerAbsent': True})
    # Positive controls prove both marker mechanisms are functional, not vacuous absences.
    result = p.process.run([sys.executable, str(canary)], capture_output=True, text=True, timeout=30)
    tool.require(result.returncode == 0 and marker.read_text() == 'executed', 'import marker positive control')
    marker.unlink()
    result = p.process.run([str(compiler)], capture_output=True, text=True, timeout=30)
    tool.require(result.returncode == 77 and marker.read_text() == 'executed', 'compiler marker positive control')
    marker.unlink()
    results += [{'name': 'import-marker-positive', 'passed': True}, {'name': 'compiler-marker-positive', 'passed': True}]
    tool.write(output / 'results.json', {'schema': 'purr.combined.negatives.v1', 'checks': len(results), 'skipped': 0, 'results': results, 'compiledOwnershipAndKindNegatives': str(args.build / 'manifest.json')})
