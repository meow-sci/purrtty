#!/usr/bin/env python3
"""Offline checks for release metadata, publishing, and rolling retention."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('retention', ROOT / 'scripts/release-retention.py')
retention = importlib.util.module_from_spec(spec)
spec.loader.exec_module(retention)


def release(number, tag=None, **flags):
    return dict(id=number, tag_name=tag or f'v1.{number}.0',
                published_at='2026-09-05T00:00:00Z',
                prerelease=False, draft=False) | flags


class ReleasePolicyTests(unittest.TestCase):
    def metadata(self, ref, event='push', attempt='1', run_number='123', success=True):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'output'
            env = dict(os.environ, GITHUB_REF=ref, GITHUB_EVENT_NAME=event,
                       GITHUB_RUN_ID='12345', GITHUB_RUN_ATTEMPT=attempt,
                       GITHUB_RUN_NUMBER=run_number, GITHUB_OUTPUT=str(output))
            result = subprocess.run(['bash', 'scripts/release-metadata.sh'], cwd=ROOT,
                                    env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode == 0, success, result.stderr)
            return dict(line.split('=', 1) for line in output.read_text().splitlines()) if output.exists() else {}

    def test_exact_versions_for_publishing_branches(self):
        for branch in ('main', 'feature/new-ux', 'feature/nested/some-work',
                       'feature/$(echo-untrusted)'):
            feature = branch.startswith('feature/')
            for event in ('push', 'workflow_dispatch'):
                for run_number, attempt in (('123', '1'), ('124', '1'), ('123', '2')):
                    with self.subTest(branch=branch, event=event, run=run_number, attempt=attempt):
                        version = f'1.{run_number}.0' + ('-beta' if feature else '')
                        data = self.metadata('refs/heads/' + branch, event,
                                             attempt=attempt, run_number=run_number)
                        self.assertEqual(data, {
                            'publish': 'true', 'version': version, 'modversion': version,
                            'tag': 'v' + version, 'title': 'purrTTY ' + version,
                            'prerelease': str(feature).lower(),
                            'channel': 'feature' if feature else 'main',
                        })

    def test_invalid_run_numbers(self):
        for branch in ('main', 'feature/test'):
            for invalid in ('', '0', '01', '-1', '1.2', '$(echo-untrusted)'):
                with self.subTest(branch=branch, run=invalid):
                    self.assertEqual(self.metadata('refs/heads/' + branch,
                                                   run_number=invalid, success=False), {})

    def test_only_branch_push_or_dispatch_can_publish(self):
        for branch in ('release/2.3.4', 'fix/test', 'chore/test', 'other/test'):
            for event in ('push', 'workflow_dispatch'):
                self.assertEqual(self.metadata('refs/heads/' + branch, event), {'publish': 'false'})
        for ref, event in [('refs/pull/42/merge', 'pull_request'),
                           ('refs/heads/main', 'pull_request'),
                           ('refs/heads/feature/test', 'pull_request'),
                           ('refs/tags/main', 'workflow_dispatch'),
                           ('refs/tags/feature/test', 'push'),
                           ('refs/heads/main', 'schedule')]:
            with self.subTest(ref=ref, event=event):
                self.assertEqual(self.metadata(ref, event), {'publish': 'false'})

    def test_retention_all_pages_and_channel_isolation(self):
        for channel, keep, suffix in (('main', 10, ''), ('feature', 5, '-beta')):
            with self.subTest(channel=channel):
                releases = [release(n, f'v1.{n}.0{suffix}', prerelease=channel == 'feature')
                            for n in range(1, 251)]
                pages = [releases[:100], releases[100:200], releases[200:]]
                pages[2] += [
                    release(1000, f'v1.1000.0{suffix}', prerelease=channel == 'feature', draft=True),
                    release(1001, f'v1.1001.0{suffix}', prerelease=channel != 'feature'),
                    release(1002, 'v1.1002.0-beta', prerelease=False),
                    release(1003, 'v1.1003.0', prerelease=True),
                    release(1004, 'v1.2.3'), release(1005, 'v2026.09.21.123'),
                    release(1006, 'tip-legacy', prerelease=True),
                    release(1007, 'feature-legacy', prerelease=True),
                    release(1008, 'v1.1008.0-beta.1', prerelease=True),
                    release(1009, f'v2.1009.0{suffix}', prerelease=channel == 'feature'),
                ]
                expected = [f'v1.{n}.0{suffix}' for n in range(250 - keep, 0, -1)]
                self.assertEqual(retention.stale_tags(pages, channel, keep), expected)
                # Exercise the paginated JSON -> newline-separated tag interface, too.
                result = subprocess.run(
                    [sys.executable, 'scripts/release-retention.py', '--channel', channel, '--keep', str(keep)],
                    cwd=ROOT, input=json.dumps(pages), text=True, capture_output=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(result.stdout.splitlines(), expected)

    def test_retention_uses_publication_time(self):
        for channel, keep, suffix in (('main', 10, ''), ('feature', 5, '-beta')):
            releases = [release(n, f'v1.{n}.0{suffix}', prerelease=channel == 'feature',
                                created_at=f'2026-09-{n:02}T00:00:00Z',
                                published_at=f'2026-09-{keep+2-n:02}T00:00:00Z')
                        for n in range(1, keep + 2)]
            self.assertEqual(retention.stale_tags([releases], channel, keep),
                             [f'v1.{keep+1}.0{suffix}'])

    def test_legacy_stable_releases_count_and_eventually_expire(self):
        legacy = [release(n, tag, published_at=f'2026-07-0{n}T00:00:00Z')
                  for n, tag in enumerate(('v1.0.0', 'v1.0.1', 'v1.1.0'), start=1)]
        for new_count in range(11):
            current = [release(n) for n in range(100, 100 + new_count)]
            with self.subTest(new_count=new_count):
                self.assertEqual(retention.stale_tags([legacy, current], 'main', 10),
                                 [r['tag_name'] for r in reversed(legacy[:max(0, new_count - 7)])])
        self.assertEqual(retention.stale_tags([legacy], 'feature', 1), [])
        protected = [dict(r, draft=True) for r in legacy]
        protected += [dict(r, prerelease=True) for r in legacy]
        self.assertEqual(retention.stale_tags([protected], 'main', 1), [])

    def test_retention_boundaries_and_invalid_arguments(self):
        for channel, keep, suffix in (('main', 10, ''), ('feature', 5, '-beta')):
            for count in (0, keep - 1, keep):
                releases = [release(n, f'v1.{n}.0{suffix}', prerelease=channel == 'feature')
                            for n in range(1, count + 1)]
                self.assertEqual(retention.stale_tags([releases], channel, keep), [])
            self.assertEqual(retention.stale_tags([], channel, keep), [])
            for invalid in (0, -1):
                with self.assertRaises(ValueError):
                    retention.stale_tags([], channel, invalid)
        with self.assertRaises(ValueError):
            retention.stale_tags([], 'tip', 5)

    def publish(self, metadata, exists, create_status=0):
        # Exercise the real publisher with a recording CLI; never contact GitHub.
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            gh = folder / 'gh'
            gh.write_text('#!/usr/bin/env python3\n'
                          'import json, os, sys\n'
                          'with open(os.environ["GH_CALLS"], "a") as log:\n'
                          '    log.write(json.dumps(sys.argv[1:]) + "\\n")\n'
                          'if sys.argv[1:3] == ["release", "view"]:\n'
                          '    sys.exit(0 if os.environ["GH_EXISTS"] == "true" else 1)\n'
                          'if sys.argv[1:3] == ["release", "create"]:\n'
                          '    sys.exit(int(os.environ["GH_CREATE_STATUS"]))\n'
                          'sys.exit(99)\n')
            gh.chmod(0o755)
            log = folder / 'calls.jsonl'
            env = dict(os.environ, PATH=str(folder) + os.pathsep + os.environ['PATH'],
                       GH_CALLS=str(log), GH_EXISTS=str(exists).lower(),
                       GH_CREATE_STATUS=str(create_status),
                       GITHUB_REPOSITORY='example/repo', GITHUB_SHA='abc123',
                       PURRTTY_DIST_DIR=str(folder), VERSION=metadata['version'], TAG=metadata['tag'],
                       TITLE=metadata['title'], PRERELEASE=metadata['prerelease'])
            result = subprocess.run(['bash', 'scripts/publish-release.sh'], cwd=ROOT,
                                    env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0 if exists else create_status, result.stderr)
            calls = [json.loads(line) for line in log.read_text().splitlines()]
            if not exists:
                self.assertEqual(calls[-1][3], str(folder / f'purrTTY-{metadata["version"]}.zip'))
            return calls

    def test_stable_and_beta_publish_arguments(self):
        for branch in ('main', 'feature/test'):
            with self.subTest(branch=branch):
                data = self.metadata('refs/heads/' + branch)
                calls = self.publish(data, exists=False)
                self.assertEqual(calls[0], ['release', 'view', data['tag'], '--repo', 'example/repo'])
                create = calls[-1]
                self.assertEqual(create[:3], ['release', 'create', data['tag']])
                for flag, value in (('--repo', 'example/repo'), ('--target', 'abc123'),
                                    ('--title', data['title'])):
                    self.assertEqual(create[create.index(flag) + 1], value)
                self.assertIn('--generate-notes', create)
                self.assertIn('delete the old `purrTTY/` folder', create[create.index('--notes') + 1])
                self.assertEqual('--prerelease' in create, branch != 'main')
                self.assertEqual('--latest=false' in create, branch != 'main')
                self.assertEqual([call[1] for call in calls], ['view', 'create'])

    def test_reruns_preserve_existing_releases(self):
        for branch in ('main', 'feature/test'):
            data = self.metadata('refs/heads/' + branch, attempt='2')
            self.assertEqual(self.publish(data, exists=True),
                             [['release', 'view', data['tag'], '--repo', 'example/repo']])

    def test_publish_failure_is_not_swallowed(self):
        calls = self.publish(self.metadata('refs/heads/main'), exists=False, create_status=1)
        self.assertEqual([call[1] for call in calls], ['view', 'create'])


if __name__ == '__main__':
    unittest.main()
