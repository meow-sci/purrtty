#!/usr/bin/env python3
"""Quiet, no-sleep integrity/mutation tests; uses only owned scratch and real evidence."""
from __future__ import annotations

import argparse
import copy
import json
import shutil
import tempfile
from functools import partial
from pathlib import Path

import generate as storage  # pyright: ignore[reportMissingImports]
import patch_source
import run as builder  # pyright: ignore[reportMissingImports]
from patch_source import (
    HERE,
    LayoutError,
    apply_steps,
    digest,
    prepare_source,
    read_manifest,
)


def rejects(action, message: str) -> None:
    try:
        action()
    except LayoutError:
        return
    raise AssertionError('negative fixture unexpectedly passed: ' + message)


def checks(contract_path: Path, archive: Path, evidence: Path, scratch: Path) -> dict:
    contract, mapping, bits = storage.load_inputs(contract_path)
    manifest = read_manifest()
    names = []
    alignment_mutations = []
    scratch.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='layout-test-', dir=scratch) as tmp:
        root = Path(tmp)
        pristine = root / 'pristine'
        base_record = prepare_source(archive, pristine, patched=False)
        original = {name: (pristine / name).read_text(encoding='utf-8-sig') for name in ('imgui.h', 'imgui_internal.h')}
        storage.verify_original_bitfields(original, bits)
        patched = root / 'patched'
        record = prepare_source(archive, patched)
        assert {name for name, sha in record['sourceFiles'].items() if sha != record['pristineFiles'][name]} == {'imgui.h', 'imgui_internal.h'}
        builder.verify_source_tree(patched, record['sourceFiles'])
        names.append('verified-new-extraction-and-exact-patches')
        rejects(lambda: prepare_source(archive, patched), 'repeated output tree')
        names.append('repeated-output-tree')
        bad_archive = root / 'bad.tar.gz'
        bad_archive.write_bytes(b'not the pinned archive')
        rejects(lambda: prepare_source(bad_archive, root / 'bad-source'), 'wrong archive hash')
        names.append('wrong-archive-hash')
        for name, mutate in [
            ('wrong-patch-hash', lambda m: m['patches'][0].update(sha256='0' * 64)),
            ('wrong-preimage', lambda m: m['patches'][0].update(preimageSha256='0' * 64)),
            ('wrong-postimage', lambda m: m['patches'][0].update(postimageSha256='0' * 64)),
            ('wrong-order', lambda m: m['patches'].reverse()),
            ('repeated-step', lambda m: m['patches'].insert(1, copy.deepcopy(m['patches'][0]))),
            ('omitted-step', lambda m: m['patches'].pop()),
        ]:
            dest = root / name
            shutil.copytree(pristine, dest)
            changed = copy.deepcopy(manifest)
            mutate(changed)
            rejects(partial(apply_steps, dest, changed), name)
            names.append(name)
        rejects(lambda: apply_steps(patched, manifest), 'already patched preimage')
        names.append('already-applied-preimage')
        for name, text in [('missing-hunk', 'absent literal'), ('repeated-hunk', '    ')]:
            fixtures = root / name
            shutil.copytree(HERE / 'patches', fixtures / 'patches')
            entry = copy.deepcopy(manifest)
            first = entry['patches'][0]
            p = fixtures / first['file']
            body = json.loads(p.read_bytes())
            body['old'] = text
            p.write_text(json.dumps(body))
            first['sha256'] = digest(p.read_bytes())
            dest = root / (name + '-source')
            shutil.copytree(pristine, dest)
            rejects(partial(apply_steps, dest, entry, fixtures), name)
            names.append(name)
        # Every symlink class is rejected, including ancestor paths and source headers.
        linked_archive = root / 'archive-link'
        linked_archive.symlink_to(archive.resolve())
        rejects(lambda: prepare_source(linked_archive, root / 'linked-out'), 'archive symlink')
        linked_parent = root / 'parent-link'
        linked_parent.symlink_to(root, target_is_directory=True)
        rejects(lambda: prepare_source(archive, linked_parent / 'out'), 'destination parent symlink')
        (patched / 'imgui.h').unlink()
        (patched / 'imgui.h').symlink_to(pristine / 'imgui.h')
        rejects(lambda: builder.verify_source_tree(patched, record['sourceFiles']), 'source header symlink')
        rejects(lambda: apply_steps(patched, manifest), 'source symlink')
        names.extend(['archive-symlink', 'destination-ancestor-symlink', 'source-header-symlink'])
        patch_link_dir = root / 'patch-links'
        shutil.copytree(HERE / 'patches', patch_link_dir / 'patches')
        first = patch_link_dir / manifest['patches'][0]['file']
        first.unlink()
        first.symlink_to(HERE / manifest['patches'][0]['file'])
        rejects(lambda: apply_steps(pristine, manifest, patch_link_dir), 'patch symlink')
        names.append('patch-symlink')
        # An internally substituted pristine header is not allowed even if it compiles.
        (patched / 'imgui.h').unlink()
        shutil.copyfile(pristine / 'imgui.h', patched / 'imgui.h')
        rejects(lambda: builder.verify_source_tree(patched, record['sourceFiles']), 'wrong header preimage')
        names.append('pristine-header-substitution')
        builder.verify_source_tree(pristine, base_record['sourceFiles'])
        for path in ('../escape', '/absolute', 'a/../escape', './relative', 'a//b'):
            rejects(partial(patch_source.relative_path, path), 'path traversal')
        names.append('path-traversal')
        # Test top-level manifest pin without changing any owned source file.
        fixture = root / 'manifest-fixture'
        fixture.mkdir()
        (fixture / 'patch-manifest.json').write_text('{}')
        saved = patch_source.HERE
        try:
            patch_source.HERE = fixture
            rejects(read_manifest, 'top-level manifest hash')
        finally:
            patch_source.HERE = saved
        names.append('manifest-hash')
        saved = storage.HERE
        try:
            storage.HERE = fixture
            for filename in storage.INPUT_PINS:
                (fixture / filename).write_text('{}')
                rejects(partial(storage.read_pinned, filename), 'closed map/bitfield/enum hash')
        finally:
            storage.HERE = saved
        names.append('closed-map-bitfield-enum-hashes')
        for name, mutate in [
            ('mapping-type-omission', lambda m: m['types'].pop()),
            ('mapping-field-omission', lambda m: m['types'][0]['fields'].pop()),
            ('mapping-addition', lambda m: m['types'].append(copy.deepcopy(m['types'][0]))),
            ('mapping-member-mutation', lambda m: m['types'][0]['fields'][0].update(member='wrong')),
            ('mapping-disposition-mutation', lambda m: m['types'][0].update(disposition='opaque-placeholder')),
            ('inline-array-count-mutation', lambda m: next(t for t in m['types'] if t['disposition'] == 'inline-array').update(length=999)),
            ('fixed-array-count-mutation', lambda m: next(f for t in m['types'] for f in t['fields'] if 'expectedArrayLength' in f).update(expectedArrayLength=999)),
        ]:
            mutated = copy.deepcopy(mapping)
            mutate(mutated)
            rejects(partial(storage.validate_mapping, contract, mutated, bits), name)
            names.append(name)
        for name, mutate in [
            ('bitfield-omission', lambda b: b['members'].pop()),
            ('bitfield-width-mutation', lambda b: b['members'][0].update(originalWidthBits=2)),
            ('bitfield-signedness-mutation', lambda b: b['members'][0].update(signed=True)),
        ]:
            changed = copy.deepcopy(bits)
            mutate(changed)
            rejects(partial(storage.validate_mapping, contract, mapping, changed), name)
            names.append(name)
        changed = copy.deepcopy(original)
        changed['imgui_internal.h'] += '\nstruct Added { int Unmapped : 2; };\n'
        rejects(lambda: storage.verify_original_bitfields(changed, bits), 'upstream bitfield reverse omission')
        names.append('upstream-bitfield-reverse-omission')
        left = storage.generate(contract, mapping, root / 'gen-a', patched=True)
        right = storage.generate(contract, mapping, root / 'gen-b', patched=True)
        assert left == right
        for name in ('probe.cpp', 'interface.json', 'original_bitfields.inc'):
            assert (root / 'gen-a' / name).read_bytes() == (root / 'gen-b' / name).read_bytes()
        names.append('byte-identical-generation')
        native = json.loads((evidence / 'osx-arm64' / 'native.json').read_bytes())
        interface = json.loads((evidence / 'interface.json').read_bytes())
        valid = storage.compare(contract, mapping, interface, native)
        assert valid['ordinaryStorageParity'] and valid['rawAliasesSafe'] is False
        assert len(valid['enumSignednessTransport']) == 24 and len(valid['rawBitfieldAliases']) == 44
        context = next(t for t in mapping['types'] if t['id'] == 'Brutal.ImGui::Brutal.ImGuiApi.ImGuiContext')
        assert len(context['fields']) == 318
        for suffix, clr_stride in [('ImGuiStyleVarInfo', 6), ('ImFontAtlasRectEntry', 7)]:
            conflict = next(c for c in valid['knownConflicts'] if c['identity'].endswith('.' + suffix) and c['property'] == 'arrayStrideBytes')
            assert conflict['native'] == 4 and conflict['managed'] == clr_stride
        assert len([c for c in valid['knownConflicts'] if c.get('property') == 'nativeAlignofVsHostEmbedding']) == 13
        assert (evidence / 'osx-arm64' / 'native.json').stat().st_size > 32768
        names.append('real-native-untruncated-evidence')
        for name, mutate in [
            ('native-type-omission', lambda n: n['entries'].pop(0)),
            ('native-field-omission', lambda n: n['entries'].pop(1)),
            ('native-extra-record', lambda n: n['entries'].append(n['entries'][0])),
            ('native-array-endpoint-mutation', lambda n: next(r for r in n['entries'] if r[5] > 1).__setitem__(6, 999)),
            ('native-bitfield-omission', lambda n: n['bitfieldDeclaredTypes'].pop()),
            ('native-bitfield-sign-mutation', lambda n: n['bitfieldDeclaredTypes'][0].__setitem__(2, 1)),
            ('native-enum-value-omission', lambda n: n['enumValues'].pop()),
            ('native-base-subobject-mutation', lambda n: n['baseSubobject'].update(offsetBytes=8)),
            ('native-dock-boundary-mutation', lambda n: n['dockBoolBoundary'].update(IsVisibleByte=201)),
        ]:
            changed = copy.deepcopy(native)
            mutate(changed)
            rejects(partial(storage.compare, contract, mapping, interface, changed), name)
            names.append(name)
        changed = copy.deepcopy(native)
        changed_interface = copy.deepcopy(interface)
        removed = changed['entries'].pop(1)
        changed_interface['entryKeys'].remove(removed[:2])
        rejects(lambda: storage.compare(contract, mapping, changed_interface, changed), 'producer omits from interface too')
        names.append('producer-and-interface-reverse-omission')
        type_index = next(i for i, t in enumerate(mapping['types']) if t['native'] == 'ImGuiTableColumnSortSpecs')
        field_index = next(i for i, f in enumerate(mapping['types'][type_index]['fields']) if f['name'] == 'SortDirection')
        for name, key, column, value in [
            ('native-size-mutation', (0, -1), 2, 2),
            ('native-alignment-mutation', (0, -1), 3, 2),
            ('native-offset-mutation', (0, 0), 4, 1),
            ('enum-width-with-same-enclosing-size', (type_index, field_index), 2, 1),
        ]:
            changed = copy.deepcopy(native)
            next(r for r in changed['entries'] if tuple(r[:2]) == key)[column] = value
            assert storage.compare(contract, mapping, interface, changed)['unexpectedMismatches'], name
            names.append(name)
        for native_type in ('ImGuiStyleVarInfo', 'ImFontAtlasRectEntry'):
            ti = next(i for i, t in enumerate(mapping['types']) if t['native'] == native_type)
            changed = copy.deepcopy(native)
            row = next(r for r in changed['entries'] if r[:2] == [ti, -1])
            assert row[2] == 4 and row[3] == 4
            row[3] = 1
            result = storage.compare(contract, mapping, interface, changed)
            assert result['ordinaryStorageParity'] is False
            assert result['unexpectedMismatches'] == [{
                'identity': mapping['types'][ti]['id'], 'property': 'nativeAlignofVsHostEmbedding',
                'native': 1, 'managed': 4, 'classification': 'unexpected',
            }]
            assert result['knownConflicts'] == valid['knownConflicts']
            assertion = f'static_assert(alignof(LayoutT{ti}) == 4, "align vs host embedding {ti}");'
            assert assertion in (root / 'gen-a' / 'probe.cpp').read_text()
            alignment_mutations.append({'nativeType': native_type, 'ordinaryStorageParity': result['ordinaryStorageParity'],
                'unexpectedMismatches': result['unexpectedMismatches'], 'knownConflictsUnchanged': True})
            names.append('stride-conflict-alignment-mutation-' + native_type)
        for rid in ('osx-arm64', 'linux-x64', 'win-x64'):
            build = json.loads((evidence / rid / 'build-record.json').read_bytes())
            assert build['staticOrdinaryParity'] is True
            assert build['executed'] == (rid == 'osx-arm64')
        names.append('execution-vs-cross-target-static-boundary')
        host_build = json.loads((evidence / 'osx-arm64' / 'build-record.json').read_bytes())
        driver = str((evidence / 'probe.cpp').resolve())
        command = host_build['command'][:host_build['command'].index(driver)]
        for case in ('header-preimage', 'packing', 'alignment-ImGuiStyleVarInfo', 'alignment-ImFontAtlasRectEntry'):
            mutated_source = root / ('compile-' + case)
            shutil.copytree(evidence / 'source', mutated_source)
            changed_command = [str(mutated_source.resolve()) if arg == str((evidence / 'source').resolve()) else arg for arg in command]
            case_driver = driver
            compile_mode = ['-fsyntax-only']
            if case == 'header-preimage':
                shutil.copyfile(pristine / 'imgui.h', mutated_source / 'imgui.h')
                type_id = next(i for i, t in enumerate(mapping['types']) if t['native'] == 'ImGuiSortDirection')
                diagnostic = 'type size ' + str(type_id)
            elif case == 'packing':
                config_index = changed_command.index('-include') + 1
                config = root / 'wrong-pack.h'
                config.write_text(Path(changed_command[config_index]).read_text() + '\n#pragma pack(1)\n')
                changed_command[config_index] = str(config.resolve())
                diagnostic = 'static assertion failed'
            else:
                native_type = case.removeprefix('alignment-')
                type_id = next(i for i, t in enumerate(mapping['types']) if t['native'] == native_type)
                header = mutated_source / 'imgui_internal.h'
                declaration = 'struct ' + native_type + '\n{'
                text = header.read_text()
                assert text.count(declaration) == 1
                header.write_text(text.replace(declaration, 'struct __attribute__((packed)) ' + native_type + '\n{', 1))
                fixture_driver = root / ('compile-' + case + '.cpp')
                fixture_driver.write_text(Path(driver).read_text()
                    + f'\nstatic_assert(sizeof({native_type}) == 4, "fixture size unchanged");\n'
                    + f'static_assert(alignof({native_type}) == 1, "fixture alignment changed");\n')
                case_driver = str(fixture_driver.resolve())
                # Without the generated parity gate, prove this is a real, isolated
                # alignment change with native size4, not an unrelated compile error.
                builder.logged(changed_command + ['-c', case_driver, '-o', str((scratch / ('compile-positive-' + case + '.o')).resolve())],
                               scratch / ('compile-positive-' + case + '.log'), 120)
                compile_mode = ['-c', '-o', str((scratch / ('compile-negative-' + case + '.o')).resolve())]
                diagnostic = 'align vs host embedding ' + str(type_id)
            log = scratch / ('compile-negative-' + case + '.log')
            rejects(partial(builder.logged, changed_command + ['-DLAYOUT_ENFORCE_MANAGED=1', *compile_mode, case_driver], log, 120), 'compiler mutation ' + case)
            diagnostics = log.read_text()
            assert diagnostic in diagnostics, 'negative compile failed for an unrelated reason: ' + case
            if case.startswith('alignment-'):
                assert diagnostics.count('error: static assertion failed') == 1
                assert "expression evaluates to '1 == 4'" in diagnostics
            names.append('actual-compiler-' + case + '-rejection')
    return {'passed': len(names), 'tests': names, 'nativeArtifact': str(evidence),
            'alignmentMutations': alignment_mutations, 'quiet': True, 'sleepCalls': 0}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--scratch', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    result = checks(args.contract, args.archive, args.evidence, args.scratch)
    args.report.write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    main()
