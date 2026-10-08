"""Closed identity-keyed storage probe generator and independent managed comparison."""
from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path

from patch_source import HERE, LayoutError, digest, reject_symlinks

CONTRACT_SHA256 = '20e417b6312f8d28ba282bd0dc641e7b2dbea4b5d378b40c42a50c390b0af89e'
INPUT_PINS = {
    'mapping.json': '1787bebf0b12fde5e60d239ac86a744e388894d3c4ff8f2402e854e8fdf00863',
    'bitfields.json': 'e973d49b7aa045bf9444329886627f1f56610a1ad672eb12d181a937e39787af',
    'enum-transport.json': '66a7ee88f8c56d85962f7600e87fa2b61e0db16b0f4809c6a7aa8e5346b476a2',
}
NO_NATIVE = {'open-generic', 'void', 'managed-delegate', 'managed-runtime-helper', 'managed-only-helper'}
FIELD_SKIP = NO_NATIVE | {'managed-static', 'raw-bitfield-unsafe'}
# Waivers apply to reviewed properties, not every observation of a record.
TYPE_CONFLICT_PROPERTIES = {
    'opaque-placeholder': {'sizeBytes', 'arrayStrideBytes', 'nativeAlignofVsHostEmbedding'},
    'bitfield-record-stride-conflict': {'sizeBytes', 'arrayStrideBytes'},
}


def type_conflict_category(disposition: str, prop: str) -> str | None:
    return disposition if prop in TYPE_CONFLICT_PROPERTIES.get(disposition, ()) else None


def load_inputs(contract_path: Path) -> tuple[dict, dict, dict]:
    data = contract_path.read_bytes()
    if digest(data) != CONTRACT_SHA256:
        raise LayoutError('selected managed contract hash mismatch')
    contract = json.loads(data)
    mapping = read_pinned('mapping.json')
    bits = read_pinned('bitfields.json')
    read_pinned('enum-transport.json')
    validate_mapping(contract, mapping, bits)
    return contract, mapping, bits


def read_pinned(name: str) -> dict:
    path = HERE / name
    reject_symlinks(path)
    data = path.read_bytes()
    if digest(data) != INPUT_PINS[name]:
        raise LayoutError('closed mapping/exception manifest changed without review: ' + name)
    return json.loads(data)


def unique(records: list, key: str) -> dict:
    result = {r[key]: r for r in records}
    if len(result) != len(records):
        raise LayoutError('duplicate ' + key)
    return result


def validate_mapping(contract: dict, mapping: dict, bits: dict) -> None:
    if mapping['contractSha256'] != CONTRACT_SHA256:
        raise LayoutError('mapping contract pin mismatch')
    ct, mt = unique(contract['types'], 'id'), unique(mapping['types'], 'id')
    if ct.keys() != mt.keys():
        raise LayoutError('type mapping omission/addition')
    bkeys = {(b['type'], b['field']) for b in bits['members']}
    if len(bkeys) != 47 or len(bits['members']) != 47:
        raise LayoutError('47 original bitfields required')
    seen_bits = set()
    for id, t in ct.items():
        m = mt[id]
        cf, mf = unique(t.get('fields', []), 'name'), unique(m['fields'], 'name')
        if cf.keys() != mf.keys():
            raise LayoutError('field mapping omission/addition: ' + id)
        if not m['disposition'] or (m['native'] is None) != (m['disposition'] in NO_NATIVE):
            raise LayoutError('unclassified type: ' + id)
        if m['disposition'] == 'inline-array' and m['length'] != t['inlineArray']['length']:
            raise LayoutError('inline array count changed: ' + id)
        for name, f in cf.items():
            v = mf[name]
            if not v['disposition']:
                raise LayoutError('unclassified field: ' + id + '.' + name)
            if f['isStatic'] != (v['disposition'] == 'managed-static'):
                raise LayoutError('static/instance disposition changed')
            if v['disposition'] not in FIELD_SKIP and 'member' not in v:
                raise LayoutError('native member missing')
            if v['disposition'] in ('raw-bitfield-unsafe', 'promoted-ordinary'):
                seen_bits.add((id, name))
            if 'fixedBuffer' in f and v.get('expectedArrayLength') != f['fixedBuffer']['length']:
                raise LayoutError('fixed buffer count changed')
            ft = ct[f['type']]
            if 'inlineArray' in ft and v.get('expectedArrayLength') != ft['inlineArray']['length']:
                raise LayoutError('array member count changed')
    if seen_bits != bkeys:
        raise LayoutError('bitfield mapping omission/addition')
    if mapping != read_pinned('mapping.json') or bits != read_pinned('bitfields.json'):
        raise LayoutError('closed mapping/bitfield mutation requires review')
    for b in bits['members']:
        if b['disposition'] != next(f for f in mt[b['type']]['fields'] if f['name'] == b['field'])['disposition']:
            raise LayoutError('bitfield disposition differs')
        if not 1 <= b['originalWidthBits'] <= 32 or b['maskEvidence'] != 'pending-native-accessor-owner':
            raise LayoutError('invalid bitfield evidence')


def verify_original_bitfields(headers: dict[str, str], bits: dict) -> None:
    # Ignore upstream block-comment-only historical CustomRect declarations.
    pattern = r'\b([A-Za-z_]\w*(?:[ \t]+[A-Za-z_]\w*)?)[ \t]+(\w+)\s*:\s*(\d+)\s*;'
    actual = Counter((header, typ, member, int(width)) for header, text in headers.items()
                     for typ, member, width in re.findall(pattern, re.sub(r'//[^\n]*', '', re.sub(r'/\*.*?\*/', '', text, flags=re.S))))
    expected = Counter((b['source'], b['declaredType'], b['field'], b['originalWidthBits']) for b in bits['members'])
    if actual != expected:
        raise LayoutError('original bitfield declaration omission/mutation')
    for b in bits['members']:
        record = re.search(r'struct\s+(?:IMGUI_API\s+)?' + re.escape(b['nativeType']) + r'\s*\{(.*?)^\};', headers[b['source']], re.M | re.S)
        if record is None or record[1].count(b['declaration']) != 1:
            raise LayoutError('exact original declaration missing/ambiguous: ' + b['nativeType'] + '.' + b['field'])


def generate(contract: dict, mapping: dict, output: Path, *, patched: bool) -> dict:
    """One driver includes actual imgui.cpp exactly once, including CPP-local types.

    STB declarations use the same configured upstream headers/namespace as their
    real owners; no duplicated upstream records. Header paths come only from the
    caller's verified source tree. Output is streamed, with no manifest buffer.
    """
    output.mkdir(parents=True, exist_ok=True)
    lines = ['#include "imgui.cpp"', '#include "imstb_rectpack.h"',
             'namespace ImStb {', '#include "imstb_textedit.h"', '}',
             '#include "probe_support.h"']
    entries, assertions, expected = [], [], []
    ct = {t['id']: t for t in contract['types']}
    def array_element_width(t: dict) -> int | None:
        managed = ct[t['id']]
        if 'inlineArray' in managed:
            return ct[managed['inlineArray']['elementType']]['measurement']['sizeBytes']
        if t['disposition'] == 'fixed-buffer':
            parent = ct[t['arraySource']['type']]
            field = next(f for f in parent['fields'] if f['name'] == t['arraySource']['field'])
            return field['fixedBuffer']['elementStorageWidth']['sizeBytes']
        if 'arrayElementType' in t:
            return ct[t['arrayElementType']]['measurement']['sizeBytes']
        return None

    for ti, t in enumerate(mapping['types']):
        if not t['native']:
            continue
        alias = f'LayoutT{ti}'
        lines.append(f'using {alias} = {t["native"]};')
        entries.append(f'LayoutType<{alias}>({ti})')
        expected.append((ti, -1))
        managed = ct[t['id']]
        measurement = managed['measurement']
        if measurement['status'] == 'measured':
            if type_conflict_category(t['disposition'], 'sizeBytes') is None:
                assertions.append(f'static_assert(sizeof({alias}) == {measurement["sizeBytes"]}, "type size {ti}");')
            if 'embeddingOffsetAfterBytePrefix' in measurement and type_conflict_category(t['disposition'], 'nativeAlignofVsHostEmbedding') is None:
                assertions.append(f'static_assert(alignof({alias}) == {measurement["embeddingOffsetAfterBytePrefix"]}, "align vs host embedding {ti}");')
        if 'length' in t:
            assertions.append(f'static_assert(LayoutShape<{alias}>::count == {t["length"]}, "type array count {ti}");')
            assertions.append(f'static_assert(LayoutShape<{alias}>::stride == {array_element_width(t)}, "type array stride {ti}");')
        if t['disposition'] == 'concrete-vector':
            # Actual vector element, including pointer-wrapper and nested range cases.
            entries.append(f'LayoutType<typename LayoutRemovePointer<decltype({alias}::Data)>::type>({ti} + 10000)')
            expected.append((ti + 10000, -1))
        fields = {f['name']: f for f in managed.get('fields', [])}
        for fi, f in enumerate(t['fields']):
            disp = f['disposition']
            if disp in FIELD_SKIP or not patched and disp == 'promoted-ordinary':
                continue
            member = f['member']
            if member == '$self':
                typ, offset = alias, '0'
            elif member.startswith('['):
                typ, offset = f'decltype((({alias}*)nullptr)[0]{member})', f'{member[1:-1]} * sizeof((({alias}*)nullptr)[0][0])'
                typ = f'typename LayoutRemoveReference<{typ}>::type'
            else:
                typ = f'decltype({alias}::{member})'
                offset = '-1' if disp == 'native-static/managed-instance-conflict' else f'offsetof({alias}, {member})'
            entries.append(f'LayoutField<{typ}>({ti}, {fi}, {offset})')
            expected.append((ti, fi))
            mf = fields[f['name']]
            if disp != 'native-static/managed-instance-conflict':
                assertions.append(f'static_assert({offset} == {mf["runtimeOffset"]["offsetBytes"]}, "field offset {ti}/{fi}");')
                if disp != 'opaque-placeholder-member':
                    assertions.append(f'static_assert(sizeof({typ}) == {mf["storageWidth"]["sizeBytes"]}, "field width {ti}/{fi}");')
            if 'expectedArrayLength' in f:
                assertions.append(f'static_assert(LayoutShape<{typ}>::count == {f["expectedArrayLength"]}, "array count {ti}/{fi}");')
    bits = read_pinned('bitfields.json')
    enum_transport = read_pinned('enum-transport.json')
    enumrows = []
    for ei, enum in enumerate(enum_transport['exceptions']):
        for vi, name in enumerate(enum['nativeConstants']):
            enumrows.append(f'{{{ei}, {vi}, (int64_t){name}}}')
    lines.extend(['static const int64_t LayoutEnumValues[][3] = {', ',\n'.join(enumrows), '};'])
    bitrows, bitinterface = [], []
    for bi, b in enumerate(bits['members']):
        typ = f'decltype({b["nativeType"]}::{b["nativeMember"]})'
        lines.append(f'static_assert(LayoutSigned<{typ}>::value == {int(b["signed"])}, "bitfield declared signedness {bi}");')
        bitrows.append(f'{{{bi}, sizeof({typ}), LayoutSigned<{typ}>::value}}')
        bitinterface.append(f'PLAYGROUND_ORIGINAL_BITFIELD({bi}, {b["nativeType"]}, {b["nativeMember"]}, {b["originalWidthBits"]}, {int(b["signed"])}, {int(b["disposition"] == "promoted-ordinary")})')
    lines.extend(['static const int64_t LayoutBitfieldDeclaredTypes[][3] = {', ',\n'.join(bitrows), '};'])
    (output / 'original_bitfields.inc').write_text('// Require PLAYGROUND_ORIGINAL_BITFIELD(index, type, member, original_bits, signed, promoted).\n' + '\n'.join(bitinterface) + '\n')
    lines.extend(['#if defined(LAYOUT_ENFORCE_MANAGED)', *assertions, '#endif',
                  'extern "C" {',
                  'extern const LayoutEntry playground_layout_entries[] = {', ',\n'.join(entries), '};',
                  'extern const size_t playground_layout_entry_count = sizeof(playground_layout_entries)/sizeof(LayoutEntry);', '}'])
    derived = next((i, t) for i, t in enumerate(mapping['types']) if t['disposition'] == 'derived-with-base')
    lines.extend([f'static constexpr int LayoutDerivedId = {derived[0]};',
                  'static int64_t LayoutDerivedOffset(int64_t field, ImGuiViewportP& object) { switch (field) {'])
    for fi, f in enumerate(derived[1]['fields']):
        lines.append(f'case {fi}: return (const char*)&object.{f["member"]} - (const char*)&object;')
    lines.extend(['default: return -1; }}', '#include "probe_runtime.inc"'])
    driver = '\n'.join(lines) + '\n'
    (output / 'probe.cpp').write_text(driver)
    interface = {'schema': 'playground_imgui.storage-probe-interface', 'version': 1,
                 'contractSha256': CONTRACT_SHA256, 'mappingSha256': digest((HERE / 'mapping.json').read_bytes()),
                 'bitfieldsSha256': digest((HERE / 'bitfields.json').read_bytes()),
                 'enumTransportSha256': digest((HERE / 'enum-transport.json').read_bytes()),
                 'driverSha256': digest(driver.encode()), 'patched': patched,
                 'entryKeys': expected, 'tableColumns': ['typeId', 'fieldId', 'sizeBytes', 'alignof', 'offsetBytes', 'arrayCount', 'elementStrideBytes', 'signedness'],
                 'signedness': {'-1': 'non-integral', '0': 'unsigned', '1': 'signed'},
                 'vectorElementTypeIdBias': 10000,
                 'arrayOffsetFormula': 'For every native array: element i is at parentOffset + i * elementStrideBytes, 0 <= i < arrayCount; count * stride == sizeof(array) is checked.',
                 'memberAlignmentMeaning': 'alignof(decltype(member)), not effective placement alignment',
                 'runtimeDerivedOffsets': 'Constructed ImGuiViewportP member/base addresses replace compiler offsetof evidence on execution.'}
    (output / 'interface.json').write_text(json.dumps(interface, indent=2) + '\n')
    return interface


def compare(contract: dict, mapping: dict, interface: dict, native: dict) -> dict:
    """Fail reverse omissions, retain every expected conflict and unexpected mismatch."""
    ct = {t['id']: t for t in contract['types']}
    rows = {(r[0], r[1]): r for r in native['entries']}
    if native.get('schema') != 'playground_imgui.native-storage-measurements' or native.get('version') != 1 or native.get('pointerBytes') != 8:
        raise LayoutError('native schema/architecture mismatch')
    for row in rows.values():
        if len(row) != 8 or row[2] <= 0 or row[3] <= 0 or row[3] & (row[3] - 1):
            raise LayoutError('invalid native measurement shape/alignment')
        if row[5] and (row[5] < 0 or row[6] <= 0 or row[5] * row[6] != row[2]):
            raise LayoutError('native array count/stride/endpoint inconsistency')
    required = set()
    for ti, t in enumerate(mapping['types']):
        if t['native']:
            required.add((ti, -1))
            if t['disposition'] == 'concrete-vector':
                required.add((ti + 10000, -1))
            for fi, f in enumerate(t['fields']):
                if f['disposition'] not in FIELD_SKIP and (interface['patched'] or f['disposition'] != 'promoted-ordinary'):
                    required.add((ti, fi))
    if len(rows) != len(native['entries']) or rows.keys() != required or {tuple(k) for k in interface['entryKeys']} != required:
        raise LayoutError('native/interface measurement omission/addition/duplicate')
    conflicts, unexpected, comparisons = [], [], 0
    if native.get('baseSubobject') != {'type': 'ImGuiViewportP', 'base': 'ImGuiViewport', 'offsetBytes': 0, 'sizeBytes': 104, 'alignment': 8}:
        raise LayoutError('constructed viewport base evidence missing/mutated')
    if interface['patched'] and native.get('dockBoolBoundary') != {'IsVisibleByte': 204, 'WantHiddenTabBarToggleByte': 205}:
        raise LayoutError('dock allocation boundary evidence missing/mutated')
    if interface['patched'] and native.get('promotionDomainsPassed') is not True:
        raise LayoutError('promotion domain checks missing/failed')
    bit_manifest = read_pinned('bitfields.json')
    native_bits = native.get('bitfieldDeclaredTypes', [])
    if len(native_bits) != len(bit_manifest['members']) or [row[0] for row in native_bits] != list(range(47)):
        raise LayoutError('bitfield declared-type evidence omitted/mutated')
    for index, row in enumerate(native_bits):
        bit = bit_manifest['members'][index]
        if row[2] != int(bit['signed']):
            raise LayoutError('bitfield declared signedness mismatch')

    def check(identity: str, prop: str, actual: int, managed: int, category: str | None = None):
        nonlocal comparisons
        comparisons += 1
        if actual != managed:
            (conflicts if category else unexpected).append({'identity': identity, 'property': prop,
                'native': actual, 'managed': managed, 'classification': category or 'unexpected'})

    for ti, t in enumerate(mapping['types']):
        if not t['native']:
            continue
        managed = ct[t['id']]
        row = rows[ti, -1]
        if managed['measurement']['status'] == 'measured':
            check(t['id'], 'sizeBytes', row[2], managed['measurement']['sizeBytes'],
                  type_conflict_category(t['disposition'], 'sizeBytes'))
            if 'arrayStrideBytes' in managed['measurement']:
                check(t['id'], 'arrayStrideBytes', row[2], managed['measurement']['arrayStrideBytes'],
                      type_conflict_category(t['disposition'], 'arrayStrideBytes'))
            if 'embeddingOffsetAfterBytePrefix' in managed['measurement']:
                check(t['id'], 'nativeAlignofVsHostEmbedding', row[3], managed['measurement']['embeddingOffsetAfterBytePrefix'],
                      type_conflict_category(t['disposition'], 'nativeAlignofVsHostEmbedding'))
        if 'length' in t:
            check(t['id'], 'arrayCount', row[5], t['length'])
            if 'inlineArray' in managed:
                element_width = ct[managed['inlineArray']['elementType']]['measurement']['sizeBytes']
            elif t['disposition'] == 'fixed-buffer':
                parent = ct[t['arraySource']['type']]
                field = next(f for f in parent['fields'] if f['name'] == t['arraySource']['field'])
                element_width = field['fixedBuffer']['elementStorageWidth']['sizeBytes']
            else:
                element_width = ct[t['arrayElementType']]['measurement']['sizeBytes']
            check(t['id'], 'elementStrideBytes', row[6], element_width)
        if t['disposition'] == 'concrete-vector':
            arg = ct[managed['genericArguments'][0]]
            erow = rows[ti + 10000, -1]
            argmap = next(x for x in mapping['types'] if x['id'] == arg['id'])
            check(t['id'], 'elementStrideBytes', erow[2], arg['measurement']['arrayStrideBytes'],
                  'native-indexing-required' if type_conflict_category(argmap['disposition'], 'arrayStrideBytes') else None)
        fields = {f['name']: f for f in managed.get('fields', [])}
        for fi, f in enumerate(t['fields']):
            if (ti, fi) not in rows:
                continue
            r, m = rows[ti, fi], fields[f['name']]
            identity = t['id'] + '.' + f['name']
            if f['disposition'] == 'native-static/managed-instance-conflict':
                conflicts.append({'identity': identity, 'classification': f['disposition'], 'native': 'static; no instance offset', 'managed': m['runtimeOffset']['offsetBytes']})
            else:
                check(identity, 'offsetBytes', r[4], m['runtimeOffset']['offsetBytes'])
                check(identity, 'storageWidth', r[2], m['storageWidth']['sizeBytes'],
                      'opaque-placeholder-member' if f['disposition'] == 'opaque-placeholder-member' else None)
            if 'expectedArrayLength' in f:
                check(identity, 'arrayCount', r[5], f['expectedArrayLength'])
    transport = read_pinned('enum-transport.json')['exceptions']
    enum_values = {(v[0], v[1]): v[2] for v in native.get('enumValues', [])}
    expected_keys = {(ei, vi) for ei, enum in enumerate(transport) for vi in range(len(enum['nativeConstants']))}
    if len(enum_values) != len(native.get('enumValues', [])) or enum_values.keys() != expected_keys:
        raise LayoutError('enum value reverse omission/duplicate')
    exceptions = {e['type']: (i, e) for i, e in enumerate(transport)}
    signedness_records, seen = [], set()
    for ti, t in enumerate(mapping['types']):
        managed = ct[t['id']]
        signedness = managed.get('scalar', {}).get('signedness')
        if not t['native'] or signedness not in ('signed', 'unsigned') or rows[ti, -1][7] == int(signedness == 'signed'):
            continue
        if t['id'] not in exceptions:
            if interface['patched']:
                raise LayoutError('unclassified native/managed signedness difference: ' + t['id'])
            signedness_records.append({'type': t['id'], 'native': t['native'], 'status': 'pristine patch-candidate difference', 'nativeSigned': rows[ti, -1][7] == 1, 'managedSigned': signedness == 'signed'})
            continue
        ei, entry = exceptions[t['id']]
        seen.add(t['id'])
        field_uses = []
        for fi_type, parent in enumerate(mapping['types']):
            cf = {f['name']: f for f in ct[parent['id']].get('fields', [])}
            for fi, field in enumerate(parent['fields']):
                if cf[field['name']]['type'] == t['id'] and (fi_type, fi) in rows:
                    field_uses.append({'type': parent['id'], 'field': field['name'],
                        'nativeStorageExpression': f'decltype({parent["native"]}::{field["member"]})',
                        'nativeSizeBytes': rows[fi_type, fi][2], 'nativeSignedness': rows[fi_type, fi][7],
                        'managedSizeBytes': cf[field['name']]['storageWidth']['sizeBytes']})
        signedness_records.append({**entry, 'nativeSizeBytes': rows[ti, -1][2],
            'nativeMeasuredSignedness': rows[ti, -1][7], 'managedSizeBytes': managed['measurement']['sizeBytes'],
            'managedDeclaredValues': managed['enumValues'],
            'nativeDeclaredValues': [{'name': name, 'value': enum_values[ei, vi]} for vi, name in enumerate(entry['nativeConstants'])],
            'managedImportUses': [imp['entryPoint'] for imp in contract['imports']
                if any(p['type'] == t['id'] for p in [imp['return'], *imp['parameters']])],
            'actualMemberStorage': field_uses})
    if seen != exceptions.keys():
        raise LayoutError('enum signedness exception disappeared without review')
    return {'schema': 'playground_imgui.storage-comparison', 'version': 1,
            'measuredEntryCount': len(rows), 'comparisonCount': comparisons,
            'typeCount': len(mapping['types']), 'fieldCount': sum(len(t['fields']) for t in mapping['types']),
            'typeDispositions': dict(Counter(t['disposition'] for t in mapping['types'])),
            'fieldDispositions': dict(Counter(f['disposition'] for t in mapping['types'] for f in t['fields'])),
            'knownConflicts': conflicts, 'unexpectedMismatches': unexpected,
            'enumSignednessTransport': signedness_records,
            'alignmentMeaning': 'Native alignof and host CLR byte-prefix embedding are independent observations; equality is not a cross-runtime theorem.',
            'rawBitfieldAliases': [{'type': b['type'], 'field': b['field'], 'originalWidthBits': b['originalWidthBits'],
                'requiredRoute': 'native member access; native indexing also required for stride-conflict records'}
                for b in bit_manifest['members'] if b['disposition'] == 'raw-bitfield-unsafe'],
            'originalBitfieldCount': 47, 'promotedOrdinaryCount': 3,
            'ordinaryStorageParity': not unexpected, 'rawAliasesSafe': False,
            'bitfieldMasksAndSemantics': 'pending-native-accessor-owner', 'safeHelpers': 'pending-other-component'}
