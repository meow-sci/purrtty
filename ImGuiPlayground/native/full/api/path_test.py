#!/usr/bin/env python3
"""Quiet existing-redirect/protected-input regressions for every API writer."""
from __future__ import annotations

import argparse
import contextlib
import hashlib
import importlib
import io
import os
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
generate = importlib.import_module('generate')
declarations = importlib.import_module('declarations')
check = importlib.import_module('check')
self_test = importlib.import_module('self_test')
paths = generate.output_paths
HERE = Path(__file__).resolve().parent


def snapshot(directory):
    result = {}
    for folder, dirs, files in os.walk(directory, followlinks=False):
        for name in dirs + files:
            item = Path(folder)/name
            key = str(item.relative_to(directory))
            if item.is_symlink():
                result[key] = ['link', os.readlink(item)]
            elif item.is_dir():
                result[key] = ['directory']
            else:
                result[key] = ['file', hashlib.sha256(item.read_bytes()).hexdigest()]
    return result


def run(contract, model, output):
    output = paths.checked(output, 'directory')
    report = output/'path-test.json'
    paths.prepare([report], [output], [contract, model])
    results = []
    skipped = []
    with tempfile.TemporaryDirectory(prefix='path-fixture-', dir=output) as temporary:
        base = Path(temporary)
        protected = base/'protected'
        protected.mkdir()
        marker = protected/'marker'
        marker.write_bytes(b'protected marker must remain unchanged\n')
        link_capable = True
        try:
            (base/'capability-link').symlink_to(marker)
            (base/'capability-link').unlink()
        except OSError as error:
            if os.name != 'nt':
                raise
            link_capable = False
            skipped.append('Windows symlink capability unavailable: '+str(error))

        def new_project():
            project = base/('project-'+str(len(results)))
            project.mkdir()
            return project

        def invoke(writer, target):
            if writer == 'generate':
                argv = ['generate.py', '--contract', str(contract), '--declarations', str(model), '--output', str(target)]
                with patch.object(sys, 'argv', argv):
                    generate.main()
            elif writer == 'declarations':
                declarations.produce(check.SCRATCH/'source'/check.builder.read_json(check.NATIVE/'source.lock.json')['upstream']['sourceCommit'], target)
            elif writer == 'self_test':
                self_test.run(contract, model, target)
            else:
                check.run_checks(['osx-arm64', 'linux-x64', 'win-x64'], runtime=True)

        def rejected(writer, project, target, label):
            before = snapshot(base)
            errors = io.StringIO()
            # An invalid write plan must stop before tool/compiler invocation.
            with patch.object(paths, 'PROJECT_DIR', project), contextlib.redirect_stderr(errors), patch.object(declarations.process_utils, 'run', side_effect=AssertionError('tool invoked before write-path rejection')):
                try:
                    invoke(writer, target)
                except paths.OutputPathError:
                    pass
                except SystemExit as error:
                    assert error.code == 1 and ('output' in errors.getvalue() or 'protected input' in errors.getvalue())
                else:
                    raise AssertionError('unsafe path accepted: '+label)
            assert snapshot(base) == before, 'partial/temp/protected writes: '+label
            results.append({'writer':writer, 'case':label, 'protectedMarkerPreserved':True, 'noPartialWrites':True, 'noToolInvocation':True})

        writers = ('generate', 'declarations', 'self_test', 'check')
        if link_capable:
            # Test all public writers with root redirects, including dangling
            # redirects, without replacing this real project's live scratch.
            for writer in writers:
                for level in ('.tmp', '.tmp/native', '.tmp/native/full-api'):
                    for dangling in (False, True):
                        project = new_project()
                        redirected = project/level
                        redirected.parent.mkdir(parents=True, exist_ok=True)
                        redirected.symlink_to(protected/'missing-directory' if dangling else protected, target_is_directory=True)
                        root = project/'.tmp/native/full-api'
                        rejected(writer, project, root/'output', 'root '+level+(' dangling' if dangling else ' existing'))

        # Every declared artifact leaf, including Windows .lib/.pdb sidecars and
        # compiler outputs, is tested before any preceding artifact is touched.
        for writer in writers:
            with patch.object(paths, 'PROJECT_DIR', base/'plan-only'):
                root = paths.root()
                relative = {
                    'generate':[Path('output/wrappers.cpp'), Path('output/coverage.json')],
                    'declarations':[p.relative_to(root) for p in declarations.output_files(root/'output')],
                    'self_test':[p.relative_to(root) for p in self_test.output_files(root/'output')],
                    'check':[p.relative_to(root) for p in check.output_files(root, ['osx-arm64', 'linux-x64', 'win-x64'], True)]
                }[writer]
            for leaf in relative:
                for kind in (('symlink', 'dangling', 'hardlink') if link_capable else ('hardlink',)):
                    project = new_project()
                    root = project/'.tmp/native/full-api'
                    destination = root/leaf
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    if kind == 'hardlink':
                        os.link(marker, destination)
                    else:
                        destination.symlink_to(protected/'missing-file' if kind == 'dangling' else marker)
                    rejected(writer, project, root/'output', 'artifact '+str(leaf)+' '+kind)
                    # Drop our hardlink so subsequent cases do not share it.
                    if kind == 'hardlink':
                        destination.unlink()
            if link_capable:
                for dangling in (False, True):
                    project = new_project()
                    root = project/'.tmp/native/full-api'
                    root.mkdir(parents=True)
                    ancestor = root/('osx-arm64' if writer == 'check' else 'ancestor')
                    ancestor.symlink_to(protected/'missing-directory' if dangling else protected, target_is_directory=True)
                    rejected(writer, project, ancestor/'output', 'ancestor'+(' dangling' if dangling else ' existing'))
                project = new_project()
                root = project/'.tmp/native/full-api'
                root.mkdir(parents=True)
                destination = root/('osx-arm64' if writer == 'check' else 'output')
                destination.symlink_to(protected, target_is_directory=True)
                rejected(writer, project, destination, 'destination directory redirect')
                project = new_project()
                root = project/'.tmp/native/full-api'
                inside = root/'inside'
                inside.mkdir(parents=True)
                destination = root/('osx-arm64' if writer == 'check' else 'output')
                destination.symlink_to(inside, target_is_directory=True)
                rejected(writer, project, destination, 'redirect to another directory inside the allowed root')

        # Actual CLIs run in bounded process groups, against redirects inside the
        # real allowed subtree. No simulated project-root override is used here.
        if link_capable:
            for writer in ('generate', 'declarations', 'self_test'):
                for leaf in (False, True):
                    destination = base/('cli-'+writer+str(leaf))
                    if leaf:
                        destination.mkdir()
                        name = {'generate':'coverage.json', 'declarations':'declarations.json', 'self_test':'self-test.json'}[writer]
                        (destination/name).symlink_to(marker)
                    else:
                        destination.symlink_to(protected, target_is_directory=True)
                    command = [sys.executable, str(HERE/(writer+'.py')), '--output', str(destination)]
                    if writer == 'declarations':
                        source = check.SCRATCH/'source'/check.builder.read_json(check.NATIVE/'source.lock.json')['upstream']['sourceCommit']
                        command += ['--source', str(source)]
                    else:
                        command += ['--contract', str(contract), '--declarations', str(model)]
                    before = snapshot(base)
                    run_result = declarations.process_utils.run(command, capture_output=True, text=True, timeout=30, check=False)
                    assert run_result.returncode != 0 and 'redirects are forbidden' in run_result.stderr
                    assert snapshot(base) == before
                    results.append({'writer':writer+' CLI', 'case':'leaf redirect' if leaf else 'directory redirect', 'protectedMarkerPreserved':True, 'noPartialWrites':True})

        # Narrow output contract excludes all pristine/config/contract locations.
        for requested in (paths.PROJECT_DIR, paths.PROJECT_DIR/'native/full/api', check.SCRATCH/'source', check.SCRATCH/'binding-contract.json', check.SCRATCH/'redirect-output', paths.root()/'..'/'source'):
            before = snapshot(base)
            try:
                paths.prepare([requested/'must-not-write'])
            except paths.OutputPathError:
                pass
            else:
                raise AssertionError('protected/out-of-bound output accepted')
            assert snapshot(base) == before
            results.append({'writer':'boundary', 'case':str(requested), 'noPartialWrites':True})
        # Even a regular input located inside the allowed subtree cannot become
        # an output artifact of that same invocation.
        for writer, filename in (('generate', 'coverage.json'), ('self_test', 'malformed.json')):
            destination = base/('protected-input-'+writer)
            destination.mkdir()
            source = destination/filename
            source.write_bytes(b'input marker')
            command = [sys.executable, str(HERE/(writer+'.py')), '--contract', str(source) if writer=='self_test' else str(contract),
                       '--declarations', str(model), '--output', str(destination)]
            if writer == 'generate':
                command += ['--mapping', str(source)]
            before = snapshot(base)
            result = declarations.process_utils.run(command, capture_output=True, text=True, timeout=30, check=False)
            assert result.returncode != 0 and 'protected input' in result.stderr
            assert snapshot(base) == before
            results.append({'writer':writer+' CLI', 'case':'regular protected input collision', 'protectedMarkerPreserved':True, 'noPartialWrites':True})
        assert marker.read_bytes() == b'protected marker must remain unchanged\n'
    paths.prepare([report], protected=[contract, model])
    report.write_bytes(generate.encoded({'schema':'playground_imgui.api-path-tests', 'schemaVersion':1,
                                        'hostPlatform':sys.platform, 'passedCount':len(results),
                                        'passed':results, 'skipped':skipped,
                                        'scope':'existing redirects only; no concurrent filesystem-swap claim'}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--declarations', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    run(args.contract, args.declarations, args.output)


if __name__ == '__main__':
    main()
