"""Exact declaration-only patches; never edit the pristine cache or reuse an output tree."""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import tarfile
import tempfile
from pathlib import Path, PurePosixPath

HERE = Path(__file__).resolve().parent
MANIFEST_SHA256 = '060599ff6f13e95c5df83610b26c336c3e2ba44e46cde67f9806f24427eaf36d'
ORDER = ('01-keymods', '02-stack-datatype', '03-active-mouse', '04-dock-boundary', '05-sort-forward', '06-sort-definition')

class LayoutError(ValueError):
    pass

def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def reject_symlinks(path: Path) -> None:
    for p in (path, *path.parents):
        if p.is_symlink():
            raise LayoutError(f'symlink not allowed: {p}')

def relative_path(name: str) -> PurePosixPath:
    p = PurePosixPath(name)
    if p.is_absolute() or '..' in p.parts or str(p) != name or not p.parts:
        raise LayoutError(f'unsafe relative path: {name}')
    return p

def read_manifest() -> dict:
    path = HERE / 'patch-manifest.json'
    reject_symlinks(path)
    data = path.read_bytes()
    if digest(data) != MANIFEST_SHA256:
        raise LayoutError('patch manifest hash mismatch')
    return json.loads(data)

def apply_steps(source: Path, manifest: dict, patch_dir: Path = HERE) -> None:
    """Internal exact-hunk engine; production callers must use prepare_source()."""
    if tuple(p['id'] for p in manifest['patches']) != ORDER:
        raise LayoutError('patch order/membership mismatch')
    for entry in manifest['patches']:
        patch_path = patch_dir / relative_path(entry['file'])
        reject_symlinks(patch_path)
        data = patch_path.read_bytes()
        if digest(data) != entry['sha256']:
            raise LayoutError('patch hash mismatch: ' + entry['id'])
        patch = json.loads(data)
        if patch['path'] != entry['path'] or patch['path'] not in ('imgui.h', 'imgui_internal.h'):
            raise LayoutError('patch path mismatch')
        path = source / relative_path(patch['path'])
        reject_symlinks(path)
        before = path.read_bytes()
        if digest(before) != entry['preimageSha256']:
            raise LayoutError('preimage hash mismatch: ' + entry['id'])
        old, new = patch['old'].encode(), patch['new'].encode()
        if not old or before.count(old) != 1 or old == new:
            raise LayoutError('hunk must occur exactly once: ' + entry['id'])
        after = before.replace(old, new, 1)
        if digest(after) != entry['postimageSha256']:
            raise LayoutError('postimage hash mismatch: ' + entry['id'])
        path.write_bytes(after)

def prepare_source(archive: Path, destination: Path, *, patched: bool = True) -> dict:
    """Extract verified archive into new scratch tree, optionally apply ordered patches.

    The destination must not exist, and ancestors/archive/patches cannot be symlinks.
    Nothing is published on failure. No network, cache writes or fuzzy patch tool.
    """
    archive, destination = archive.absolute(), destination.absolute()
    reject_symlinks(archive)
    reject_symlinks(destination)
    if destination.exists():
        raise LayoutError('destination already exists; repeated application forbidden')
    manifest = read_manifest()
    if digest(archive.read_bytes()) != manifest['archiveSha256']:
        raise LayoutError('source archive hash mismatch')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.layout-source-', dir=destination.parent) as tmp:
        root = Path(tmp)
        prefix = 'imgui-' + manifest['sourceCommit']
        seen = set()
        with tarfile.open(archive, 'r:gz') as tar:
            for member in tar.getmembers():
                name = member.name.rstrip('/')
                parts = relative_path(name).parts
                if parts[0] != prefix or name in seen or not (member.isdir() or member.isfile()):
                    raise LayoutError('unexpected archive entry: ' + name)
                seen.add(name)
                if len(parts) == 1:
                    if not member.isdir():
                        raise LayoutError('archive root is not directory')
                    continue
                p = root.joinpath(*parts[1:])
                if member.isdir():
                    p.mkdir(parents=True, exist_ok=True)
                else:
                    p.parent.mkdir(parents=True, exist_ok=True)
                    stream = tar.extractfile(member)
                    if stream is None:
                        raise LayoutError('missing archive stream')
                    with stream, p.open('xb') as out:
                        shutil.copyfileobj(stream, out)
        pristine = {str(p.relative_to(root)): digest(p.read_bytes()) for p in sorted(root.rglob('*')) if p.is_file()}
        if patched:
            apply_steps(root, manifest)
        final = {str(p.relative_to(root)): digest(p.read_bytes()) for p in sorted(root.rglob('*')) if p.is_file()}
        # Rename the complete verified tree only after all steps succeed.
        os.rename(root, destination)
    return {'archiveSha256': manifest['archiveSha256'], 'patchManifestSha256': MANIFEST_SHA256,
            'patched': patched, 'pristineFiles': pristine, 'sourceFiles': final}
