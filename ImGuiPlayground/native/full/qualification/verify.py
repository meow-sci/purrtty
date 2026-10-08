"""Portable structural/measurement verification. Imported only after runner preflight."""
import ast
import hashlib
import importlib
import importlib.util
import json
import re
import struct
import sys


def require(value, message):
    if not value:
        raise ValueError('full verification: ' + message)


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError('selected verifier module unavailable')
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def modules(source):
    full = source / 'ImGuiPlayground/native/full'
    sys.path.insert(0, str(full / 'profile'))
    profile = importlib.import_module('selected_profile')
    enums = importlib.import_module('enums')
    return profile, enums


# Keep declarations, callbacks, ref-return properties and every exclusion. Only
# observational values and runtime trace identities are removed from this view.
OBSERVATIONS = {'measurement', 'runtimeOffset', 'storageWidth', 'marshalerMeasurement', 'marshalerOffset', 'overlaps'}
TRACES = {'host', 'assemblies', 'metadataToken', 'moduleMvid', 'assemblyQualifiedName', 'assembly', 'resolutionSource'}


def declarative(value):
    if isinstance(value, dict):
        return {k: declarative(v) for k, v in value.items() if k not in OBSERVATIONS | TRACES}
    if isinstance(value, list):
        return [declarative(v) for v in value]
    if isinstance(value, str) and ', Version=' in value:
        return value.split(', Version=')[0]
    return value


def measurement_shapes(reference, actual, path='contract'):
    """Required raw observations cannot disappear or switch to unmeasured."""
    if isinstance(reference, dict):
        require(isinstance(actual, dict), 'object shape: ' + path)
        for key, expected in reference.items():
            if key in TRACES or key in ('marshalerMeasurement', 'marshalerOffset', 'overlaps'):
                continue
            require(key in actual, 'missing required evidence: ' + path + '/' + key)
            observed = actual[key]
            if key in OBSERVATIONS:
                require(isinstance(observed, dict) and set(observed) == set(expected), 'measurement keys: ' + path + '/' + key)
                require(observed['status'] == expected['status'], 'measurement status: ' + path + '/' + key)
                if expected['status'] == 'measured':
                    for metric, value in observed.items():
                        if metric in ('sizeBytes', 'arrayStrideBytes', 'embeddingOffsetAfterBytePrefix'):
                            require(type(value) is int and value > 0, 'invalid measurement: ' + path + '/' + metric)
                        if metric == 'offsetBytes':
                            require(type(value) is int and value >= 0, 'invalid field offset: ' + path)
                else:
                    require(observed == expected, 'unapproved unavailable evidence: ' + path)
            else:
                measurement_shapes(expected, observed, path + '/' + key)
    elif isinstance(reference, list):
        require(isinstance(actual, list) and len(reference) == len(actual), 'list coverage: ' + path)
        for index, expected in enumerate(reference):
            measurement_shapes(expected, actual[index], path + '/' + str(index))


def exported_names(data, rid):
    """Read actual exported-name tables without nm/objdump on the consuming host."""
    def unpack(fmt, at):
        return struct.unpack_from('<' + fmt, data, at)
    def string(at):
        require(0 <= at < len(data), 'binary string bounds')
        return data[at:data.index(b'\0', at)].decode('ascii')
    names = []
    if rid == 'osx-arm64':
        require(unpack('II', 0) == (0xfeedfacf, 0x0100000c), 'Mach-O target')
        at = 32
        for _ in range(unpack('I', 16)[0]):
            command, size = unpack('II', at)
            if command == 2:
                symoff, count, stroff, _ = unpack('IIII', at + 8)
                for index in range(count):
                    name, kind, section, _, _ = unpack('IBBHQ', symoff + index * 16)
                    if kind & 0xe0 or kind & 0x0e != 0x0e or not kind & 1 or kind & 0x10 or section == 0:
                        continue
                    names.append(string(stroff + name).removeprefix('_'))
            require(size >= 8, 'Mach-O command bounds')
            at += size
        names = [n for n in names if n not in ('__dso_handle', '_mh_dylib_header')]
    elif rid == 'linux-x64':
        require(data[:6] == b'\x7fELF\x02\x01' and unpack('H', 18)[0] == 62, 'ELF target')
        shoff = unpack('Q', 40)[0]
        size, count = unpack('HH', 58)
        sections = [unpack('IIQQQQIIQQ', shoff + i * size) for i in range(count)]
        for sec in sections:
            if sec[1] != 11:
                continue
            strings = sections[sec[6]][4]
            for at in range(sec[4], sec[4] + sec[5], sec[9]):
                name, info, visibility, index, _, _ = unpack('IBBHQQ', at)
                if index and info >> 4 in (1, 2) and visibility & 3 in (0, 3):
                    names.append(string(strings + name))
    elif rid == 'win-x64':
        pe = unpack('I', 0x3c)[0]
        require(data[:2] == b'MZ' and data[pe:pe + 4] == b'PE\0\0' and unpack('H', pe + 4)[0] == 0x8664, 'PE target')
        count, optional_size = unpack('H', pe + 6)[0], unpack('H', pe + 20)[0]
        optional = pe + 24
        require(unpack('H', optional)[0] == 0x20b, 'PE64 target')
        sections = [unpack('IIII', optional + optional_size + i * 40 + 8) for i in range(count)]
        def offset(rva):
            for virtual_size, start, raw_size, raw in sections:
                if start <= rva < start + max(virtual_size, raw_size):
                    return raw + rva - start
            raise ValueError('full verification: PE RVA outside sections')
        export = offset(unpack('I', optional + 112)[0])
        count, table = unpack('I', export + 24)[0], unpack('I', export + 32)[0]
        names = [string(offset(unpack('I', offset(table) + i * 4)[0])) for i in range(count)]
    else:
        raise ValueError('full verification: unknown RID')
    require(len(names) == len(set(names)), 'duplicate binary export')
    return sorted(names)


def export_inventory(bundle, read):
    full = bundle / 'source/ImGuiPlayground/native/full'
    contract = read(bundle / 'evidence/managed-contract.json')
    manual = read(full / 'manual/manifest.json')
    manual_imports = [r['name'] for r in manual['imports']]
    dynamic = [r['name'] for r in manual['dynamicExports']]
    groups = {'api': [r['entryPoint'] for r in contract['imports'] if r['entryPoint'] not in manual_imports], 'manual': manual_imports, 'dynamic': dynamic}
    def header(name):
        return re.findall(r'PURR_ACCESSOR_EXPORT\s+.+?\b(purr_\w+)\([^;]*\);', (full / 'accessors' / name).read_text())
    groups['helpers'] = header('accessors.h')
    tree = ast.parse((full / 'profile/managed.py').read_text())
    assignments = [n for n in tree.body if isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'SUPPLEMENTAL' for t in n.targets)]
    require(len(assignments) == 1, 'profile fixture producer inventory')
    supplements = ast.literal_eval(assignments[0].value)
    matrix = read(bundle / 'evidence/matrix.json')
    groups['profileFixture'] = supplements + [row[key] for row in matrix['types'] for key in ('typedExport', 'rawBackingExport', 'representationExport', 'constantsExport')]
    groups['manualFixture'] = [r['name'] for r in manual['fixtureExports']]
    groups['accessorFixture'] = header('fixture.h')
    require([len(groups[k]) for k in ('api', 'manual', 'dynamic', 'helpers', 'profileFixture', 'manualFixture', 'accessorFixture')] == [1130, 16, 6, 23, 346, 6, 19], 'producer surface inventories')
    rows = [{'name': name, 'component': component, 'surface': 'fixture' if component.endswith('Fixture') else 'production', 'kind': 'writable-pointer-slot' if name.startswith('Platform_') else 'function'} for component, names in groups.items() for name in names]
    require(len(rows) == len({r['name'] for r in rows}), 'overlapping producer exports')
    return rows


def validate_exports(rows, expected):
    require(isinstance(rows, list) and len(rows) == len({r['name'] for r in rows}), 'duplicate export inventory')
    require({r['name']: r for r in rows} == {r['name']: r for r in expected}, 'exact producer export/classification/kind inventory')


def integer_rows(rows, width, label):
    require(isinstance(rows, list) and all(isinstance(r, list) and len(r) == width and all(type(v) is int for v in r) for r in rows), 'malformed ' + label)


def verify_native(native, expected):
    require(set(native) == {'schema', 'version', 'pointerBytes', 'entries', 'baseSubobject', 'rectPackContext', 'dockBoolBoundary', 'bitfieldDeclaredTypes', 'enumValues', 'promotionDomainsPassed'}, 'native root schema/unknown evidence')
    require(type(native['version']) is int and native['version'] == 1 and type(native['pointerBytes']) is int and native['pointerBytes'] == 8, 'native version/pointer schema')
    for group in ('baseSubobject', 'rectPackContext', 'dockBoolBoundary'):
        for key, value in native[group].items():
            if key not in ('type', 'base'):
                require(type(value) is int, 'native integer property: ' + group + '/' + key)
    integer_rows(native['entries'], 8, 'native rows')
    integer_rows(native['bitfieldDeclaredTypes'], 3, 'bitfield rows')
    integer_rows(native['enumValues'], 3, 'native enum rows')
    for key in ('entries', 'bitfieldDeclaredTypes', 'enumValues'):
        require(native[key] == expected[key], 'target compiler/runtime ' + key + ' omission/duplicate/representation/alignment/stride mismatch')
    require(native['rectPackContext'] == expected['rectPackContext'], 'actual configured STB shape mismatch')


def validate(bundle, contract, native, enum_observations, rid, read):
    """No toolchain/cache access. Independently reconstruct joins from bundle inputs."""
    evidence = bundle / 'evidence'
    reference = read(evidence / 'managed-contract.json')
    validate_exports(read(evidence / 'exports.json'), export_inventory(bundle, read))
    accessors = read(evidence / 'accessors.json')
    identity = hashlib.sha256(json.dumps({k: v for k, v in accessors.items() if k != 'identity'}, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    require(identity == accessors['identity'] == 'e86ced3fb638c94c285378697c77e6e723bd75864c337e2b557449b4e4677f72', 'closed helper identity/inventory')
    for name, expected_hash in accessors['componentContractHashes'].items():
        require(hashlib.sha256((bundle / 'source/ImGuiPlayground/native/full/accessors' / name).read_bytes()).hexdigest() == expected_hash, 'helper production source identity')
    require(set(contract) == set(reference), 'managed root schema/unknown evidence')
    require(contract['schema'] == 'playground_imgui.managed-binding-contract' and contract['schemaVersion'] == 1 and contract['exporterVersion'] == 1, 'managed schema/version')
    require(contract['host']['rid'] == rid and contract['host']['pointerSizeBytes'] == 8 and contract['host']['endianness'] == 'little', 'actual CLR target')
    require(json.dumps(declarative(contract), sort_keys=True) == json.dumps(declarative(reference), sort_keys=True), 'exact declarative closure/signature/callback/ref-return/enum/exclusion mismatch')
    measurement_shapes(reference, contract)
    p, enums = modules(bundle / 'source')
    mapping = read(evidence / 'selected-mapping.json')
    p.api.validate_contract(contract, mapping)
    declarations = read(evidence / 'declarations.json')
    wrappers, coverage = p.api.generate(contract, declarations, mapping)
    require(wrappers == (evidence / 'wrappers.cpp').read_bytes(), 'actual signatures/generated forwarding drift')
    frozen_coverage = read(evidence / 'coverage.json')
    for key, value in coverage.items():
        if key not in ('inputContractSha256', 'mappingSha256', 'generatorSha256', 'commonHeaderSha256', 'wrapperSha256'):
            require(value == frozen_coverage[key], 'callback/aggregate/Bool8/signature coverage: ' + key)
    layout = read(bundle / 'source/ImGuiPlayground/native/full/layout/mapping.json')
    bits = read(bundle / 'source/ImGuiPlayground/native/full/layout/bitfields.json')
    p.storage.validate_mapping(contract, layout, bits)
    expected = read(bundle / 'native' / rid / 'static-layout.json')
    verify_native(native, expected)
    interface = read(evidence / 'interface.json')
    require(len(interface['entryKeys']) == len({tuple(v) for v in interface['entryKeys']}), 'duplicate interface row')
    require(interface['schema'] == 'playground_imgui.storage-probe-interface' and interface['version'] == 1 and interface['patched'] is True, 'layout interface profile')
    comparison = p.storage.compare(contract, layout, interface, native)
    require(not comparison['unexpectedMismatches'] and comparison['ordinaryStorageParity'] and not comparison['rawAliasesSafe'], 'actual native/CLR layout mismatch')
    # Exact property-specific conflict identities, not a blanket alignment waiver.
    expected_conflicts = read(evidence / 'conflicts.json')
    require(comparison['knownConflicts'] == expected_conflicts, 'classified conflict set or property changed')
    policy = enums.load_policy()
    model = read(evidence / 'harvest.json')
    nominal = enums.use_graph.roots(policy, contract, layout, model)
    raw = read(evidence / 'raw-fields.json')
    reverse = enums.use_graph.census(policy, contract, layout, declarations, raw, mapping, nominal)
    require(reverse == read(evidence / 'census.json'), 'independent enum reverse-census omission/mutation')
    matrix = enums.validate(policy, contract, layout, model, mapping, reverse)
    require(matrix == read(evidence / 'matrix.json'), 'complete enum storage/use/value join mismatch')
    require(set(enum_observations) == {'useRepresentations', 'passed'} and enum_observations['passed'] is True, 'enum observation schema')
    require(isinstance(enum_observations['useRepresentations'], list) and enum_observations['useRepresentations'] and enum_observations['useRepresentations'][-1] is None, 'enum observation termination')
    integer_rows(enum_observations['useRepresentations'][:-1], 5, 'enum observations')
    observed = enums.compare_observations(matrix, enum_observations)
    manual = load_module('qualification_manual', bundle / 'source/ImGuiPlayground/native/full/manual/contract.py')
    manual_manifest = read(bundle / 'source/ImGuiPlayground/native/full/manual/manifest.json')
    manual.validate(manual_manifest, contract, {name: path.read_bytes() for name, path in manual.source_paths().items()})
    return {'schema': 'purr.full-verification.v1', 'rid': rid, 'imports': len(contract['imports']), 'dynamicExports': len(contract['dynamicExports']), 'types': len(contract['types']), 'fields': sum(len(t.get('fields', [])) for t in contract['types']), 'nativeEntries': len(native['entries']), 'layoutComparisons': comparison['comparisonCount'], 'knownConflicts': comparison['knownConflicts'], 'rawAliases': comparison['rawBitfieldAliases'], 'enumObservations': observed, 'portableApiProjectionSha256': mapping['portableProjectionSha256'], 'allEndpointSemanticsClaimed': False}
