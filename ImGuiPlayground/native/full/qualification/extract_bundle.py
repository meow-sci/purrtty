#!/usr/bin/env python3
"""Standalone trusted stdlib-only extractor. Obtain/verify this script separately."""
import argparse
import hashlib
import json
import os
import re
import stat
import sys
import zipfile
from pathlib import Path, PurePosixPath


def require(value, message):
    if not value:
        raise ValueError('qualification extraction: ' + message)


def physical(raw, missing=False):
    path = Path(raw)
    require('..' not in path.parts, 'parent traversal')
    path = path.absolute()
    for item in reversed((path, *path.parents)):
        try:
            info = item.lstat()
        except FileNotFoundError:
            if missing:
                continue
            raise
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0), 'redirect')
        require(stat.S_ISDIR(info.st_mode) or (item == path and stat.S_ISREG(info.st_mode) and info.st_nlink == 1), 'shared/nonregular input')
    return path


def relative(name):
    path = PurePosixPath(name)
    require(name not in ('', '.') and '\\' not in name and ':' not in name and not path.is_absolute() and '..' not in path.parts and str(path) == name, 'unsafe bundle relative path')
    # Source-owned bundle names are ASCII and must remain distinct on all targets.
    require(re.fullmatch(r'[A-Za-z0-9._ /-]+', name) is not None, 'nonportable archive member')
    reserved = {'CON', 'PRN', 'AUX', 'NUL'} | {prefix + str(n) for prefix in ('COM', 'LPT') for n in range(1, 10)}
    require(all(p.rstrip(' .') == p and p.split('.')[0].upper() not in reserved for p in path.parts), 'reserved/aliased archive member')


def unique(items):
    obj = {}
    for key, value in items:
        require(key not in obj, 'duplicate JSON key')
        obj[key] = value
    return obj


def validate_manifest(record):
    require(set(record) == {'schema', 'version', 'files', 'targets', 'sourceProfile', 'toolchain', 'managedPins', 'productionExports', 'fixtureExports', 'trust'}, 'archive manifest schema keys')
    require(record['schema'] == 'purr.full-bundle' and type(record['version']) is int and record['version'] == 1, 'archive manifest schema/version')
    require(record['targets'] == ['osx-arm64', 'linux-x64', 'win-x64'], 'manifest target inventory')
    require(record['trust'] == 'independently retain this manifest SHA; integrity relative to trusted maintainer builder, not authenticity against a malicious builder', 'manifest trust scope')
    require(record['sourceProfile'] == {'sourceCommit': '031a18c417158427217bc5890e0ec0cb7e7b4b63', 'configSha256': '18e6ca90823b36a891eca1c182eba6deb8a35d64431d03db3b91477a1e5049f3', 'patchManifestSha256': '060599ff6f13e95c5df83610b26c336c3e2ba44e46cde67f9806f24427eaf36d'}, 'manifest source/config/patch profile')
    require(type(record['productionExports']) is int and record['productionExports'] == 1175 and type(record['fixtureExports']) is int and record['fixtureExports'] == 1546, 'manifest surface inventory')
    compiler = record['toolchain']
    require(set(compiler) == {'zigVersion', 'compilerSha256', 'targetTriples'} and compiler['zigVersion'] == '0.17.0' and re.fullmatch('[0-9a-f]{64}', compiler['compilerSha256']) is not None, 'manifest compiler identity')
    require(compiler['targetTriples'] == {'osx-arm64': 'aarch64-macos.11.0', 'linux-x64': 'x86_64-linux-gnu.2.17', 'win-x64': 'x86_64-windows-gnu'}, 'manifest compiler targets')
    require(record['managedPins'] == {'Brutal.Core.Common': {'moduleMvid': 'd0a4e4ad-480c-4cc8-a9bf-687b26cb8963', 'sha256': '4cbae1473d6a3759345f1eec8e62e6c7da1e19f8d10db8d13c005203ec590de4'}, 'Brutal.Core.Numerics': {'moduleMvid': 'e4190ee6-8291-49aa-9721-88111e32f2ba', 'sha256': 'a217a6098116a1895e965b3a1d4fdee931e59a7a55f326c7d710b0d7eeccd17a'}, 'Brutal.ImGui': {'moduleMvid': 'be72af36-f552-460e-ab1c-cc0018ebd574', 'sha256': 'b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c'}}, 'manifest contributor identities')
    require(isinstance(record['files'], dict) and 0 < len(record['files']) < 20000 and len(record['files']) == len({name.casefold() for name in record['files']}), 'manifest file inventory')
    for name, pin in record['files'].items():
        relative(name)
        require(set(pin) == {'bytes', 'sha256', 'executable'} and type(pin['bytes']) is int and 0 <= pin['bytes'] <= 128 * 1024 * 1024 and type(pin['executable']) is bool and re.fullmatch('[0-9a-f]{64}', pin['sha256']) is not None, 'archive file schema')
        require(not name.endswith('.dll') or name.startswith('native/') or '/runtimes/' in name, 'private managed DLL in archive')
        require(not any(str(parent) in record['files'] for parent in PurePosixPath(name).parents), 'archive file/directory conflict')


def extract(archive_path, anchor, destination):
    archive_path = physical(archive_path)
    destination = physical(destination, missing=True)
    require(not destination.exists(), 'fresh output required')
    require(not archive_path.is_relative_to(destination) and not destination.is_relative_to(archive_path), 'output overlaps archive input')
    for parent in destination.parents:
        for marker in ('bundle.json', 'source-record.json', 'profile.json'):
            require(not (parent / marker).exists() and not (parent / marker).is_symlink(), 'output inside published input tree')
    require(re.fullmatch('[0-9a-f]{64}', anchor) is not None, 'independently supplied anchor required')
    with zipfile.ZipFile(archive_path) as archive:
        entries = archive.infolist()
        names = [e.filename for e in entries]
        require(len(names) <= 20000 and len(names) == len({n.casefold() for n in names}), 'duplicate/case-alias archive member')
        require(sum(e.file_size for e in entries) <= 1024 * 1024 * 1024, 'archive expanded-size bound')
        for entry in entries:
            relative(entry.filename)
            require(not entry.is_dir() and stat.S_ISREG(entry.external_attr >> 16), 'link/device/directory archive member')
        require('bundle.json' in names, 'archive manifest missing')
        content = archive.read('bundle.json')
        require(hashlib.sha256(content).hexdigest() == anchor, 'independent archive manifest anchor mismatch')
        require(len(content) <= 128 * 1024 * 1024, 'manifest JSON bound')
        def constant(value):
            raise ValueError('qualification extraction: nonfinite JSON: ' + value)
        record = json.loads(content, object_pairs_hook=unique, parse_constant=constant)
        validate_manifest(record)
        require(set(names) == set(record['files']) | {'bundle.json'}, 'archive/manifest membership mismatch')
        # Retain validated bytes until all records/CRCs/hashes pass, so rejection is
        # before mkdir/writes rather than a partly extracted tree.
        blobs = {}
        for entry in entries:
            blob = archive.read(entry)
            if entry.filename != 'bundle.json':
                pin = record['files'][entry.filename]
                require(set(pin) == {'bytes', 'sha256', 'executable'} and type(pin['bytes']) is int and type(pin['executable']) is bool, 'archive file schema')
                require(len(blob) == pin['bytes'] and hashlib.sha256(blob).hexdigest() == pin['sha256'], 'archive content hash')
            blobs[entry.filename] = blob
    destination.mkdir(parents=True)
    for name, blob in blobs.items():
        path = destination / name
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open('xb') as stream:
            stream.write(blob)
        os.chmod(path, 0o755 if record['files'].get(name, {}).get('executable', False) else 0o644)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--anchor', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        extract(args.archive, args.anchor, args.output)
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
