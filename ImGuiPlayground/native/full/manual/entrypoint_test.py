"""Actual subprocess entrypoint regressions in disposable copied-input trees."""
import json
import os
import shutil
import sys
from pathlib import Path


def run(output, paths, process):
    root = output / 'entrypoint-regressions'
    root.mkdir()
    original_root = paths.PROJECT.parent
    inputs = paths.component_inputs()
    for parent in (paths.PROJECT, paths.PROJECT / 'native', paths.PROJECT / 'native/full', paths.PROJECT / 'native/full/layout'):
        initializer = parent / '__init__.py'
        if initializer.is_file():
            inputs.append(initializer)
    contents = {p.relative_to(original_root): p.read_bytes() for p in inputs}
    records = []

    def execute(entrypoint, label, mutate, expected='redirect'):
        directory = root / str(len(records))
        sandbox = directory / 'copy'
        sandbox.mkdir(parents=True)
        for relative, data in contents.items():
            destination = sandbox / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
        project = sandbox / 'ImGuiPlayground'
        component = project / 'native/full/manual'
        marker = directory / 'CANARY-EXECUTED'
        canary = directory / 'canary.py'
        canary.write_text('from pathlib import Path\nPath(' + repr(str(marker)) + ').write_text("executed")\n')
        compiler = directory / 'compiler-canary'
        compiler.write_text('#!' + sys.executable + '\n' + canary.read_text())
        compiler.chmod(0o755)
        extra = mutate(sandbox, component, canary)
        script = component / entrypoint
        command = [sys.executable, str(script), '--output', 'must-not-exist']
        if entrypoint == 'check.py':
            command += ['--rid', 'osx-arm64', '--zig', str(compiler)]
        command += extra or []
        env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1')
        for name in ('KSAFolder', 'KSA_DLL_DIR'):
            env.pop(name, None)
        result = process.run(command, capture_output=True, text=True, env=env, cwd=sandbox, timeout=20)
        (directory / 'command.json').write_text(json.dumps(command, indent=2) + '\n')
        (directory / 'diagnostic.log').write_text(result.stdout + result.stderr)
        if result.returncode == 0 or expected not in result.stderr or marker.exists():
            raise AssertionError('entrypoint regression failed: ' + label)
        if (project / '.tmp/native/full-manual/must-not-exist').exists():
            raise AssertionError('output created before input rejection: ' + label)
        records.append({'entrypoint': entrypoint, 'case': label, 'exitCode': result.returncode,
                        'canaryExecuted': False, 'outputCreated': False})

    # Modules are redirected to executable canaries. Data/source leaves redirect
    # to identical bytes: content/hash checks alone would not reject them.
    leaves = [p.relative_to(original_root) for p in inputs]
    leaves += [Path('ImGuiPlayground/__init__.py'), Path('ImGuiPlayground/native/full/__init__.py'),
               Path('ImGuiPlayground/native/full/layout/__init__.py')]
    for entrypoint in ('check.py', 'self_test.py'):
        for leaf in sorted(set(leaves)):
            if leaf == Path('ImGuiPlayground/native/full/manual') / entrypoint:
                continue  # Explicitly selected script code must run to check itself.
            for dangling in (False, True):
                def mutate(sandbox, component, canary, leaf=leaf, dangling=dangling):
                    target = sandbox / leaf
                    if target.suffix == '.py':
                        redirect = canary
                    else:
                        redirect = canary.parent / 'original-input'
                        redirect.write_bytes(target.read_bytes())
                    if target.exists():
                        target.unlink()
                    target.symlink_to(canary.parent / 'missing' if dangling else redirect)
                execute(entrypoint, str(leaf) + ('/dangling' if dangling else '/redirect'), mutate)
        # A trusted entrypoint launched through a redirect checks its own path;
        # arbitrary replacement interpreter code is not a sandboxed input.
        def script_redirect(sandbox, component, canary, entrypoint=entrypoint):
            source = component / entrypoint
            clean = canary.parent / 'trusted-entrypoint.py'
            shutil.move(source, clean)
            source.symlink_to(clean)
        execute(entrypoint, 'entrypoint-leaf-redirect', script_redirect)
        for relative in ('ImGuiPlayground', 'ImGuiPlayground/native/full/manual', 'ImGuiPlayground/.tmp/native'):
            def directory_redirect(sandbox, component, canary, relative=relative):
                source = sandbox / relative
                real = canary.parent / 'real-directory'
                shutil.move(source, real)
                source.symlink_to(real, target_is_directory=True)
            execute(entrypoint, 'ancestor/' + relative, directory_redirect)
        for dangling in (False, True):
            def output_redirect(sandbox, component, canary, dangling=dangling):
                target = canary.parent / 'output-target'
                if not dangling:
                    target.mkdir()
                (sandbox / 'ImGuiPlayground/.tmp/native/full-manual').symlink_to(target, target_is_directory=True)
            execute(entrypoint, 'output-root/' + str(dangling), output_redirect)

    for dangling in (False, True):
        for name in paths.MANAGED_NAMES:
            def managed_redirect(sandbox, component, canary, dangling=dangling, name=name):
                selected = canary.parent / 'managed'
                selected.mkdir()
                for dependency in paths.MANAGED_NAMES:
                    (selected / (dependency + '.dll')).write_bytes(b'selected input')
                target = selected / (name + '.dll')
                target.unlink()
                target.symlink_to(canary.parent / 'missing' if dangling else canary)
                return ['--runtime', '--ksa', str(selected)]
            execute('check.py', 'selected/' + name + '/' + str(dangling), managed_redirect)
    for relative in ('.tmp/native', '.tmp/native/full-manual'):
        def overlap(sandbox, component, canary, relative=relative):
            selected = sandbox / 'ImGuiPlayground' / relative
            selected.mkdir(exist_ok=True)
            for name in paths.MANAGED_NAMES:
                (selected / (name + '.dll')).write_bytes(b'protected selected input')
            return ['--runtime', '--ksa', str(selected)]
        execute('check.py', 'output-under-selected/' + relative, overlap, expected='output/input overlap')
    (root / 'results.json').write_text(json.dumps({'count': len(records), 'skipped': 0, 'records': records}, indent=2) + '\n')
    return records
