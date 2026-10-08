"""Closed 16-import/6-dynamic contract; supplements are a distinct test-only set."""
import hashlib
import json
import re
from pathlib import Path

HERE = Path(__file__).absolute().parent
IMPORTS = ('BulletText', 'DebugLog', 'LabelText', 'LogText', 'SetItemTooltip', 'SetTooltip', 'Text', 'TextColored', 'TextDisabled', 'TextWrapped', 'TreeNode_1', 'TreeNode_2', 'TreeNodeEx_1', 'TreeNodeEx_2', 'TextAligned_internal', 'TextV')
BRIDGES = ('GetWindowFramebufferScale', 'GetWindowPos', 'GetWindowSize')
DYNAMIC = tuple(n for b in BRIDGES for n in ('Platform_' + b + '_ManagedFunctionPointer', 'Get_' + b + '_InteropPointer'))
SUPPLEMENTS = ('purr_manual_fixture_create', 'purr_manual_fixture_destroy', 'purr_manual_fixture_formats', 'purr_manual_fixture_textv', 'purr_manual_fixture_viewport', 'purr_manual_fixture_invoke')
CONTRACT_HASH = '20e417b6312f8d28ba282bd0dc641e7b2dbea4b5d378b40c42a50c390b0af89e'
PORTABLE_HASH = '3e3cd8793d90fcfd351ef14c9179446d56ec27c859fda78027a54c96804330eb'
SOURCES = {'abi_proof.cpp', 'manual.cpp', 'manual.h', 'fixture.cpp', 'fixture.h', 'native_test.cpp', '../../../../ImGuiPlayground.Checks/FullManualAbiChecks.cs', '../api/purr_abi.h'}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validate(manifest, binding, files):
    require(manifest['schema'] == 'purr.manual.contract.v1', 'schema')
    require(manifest['inputContractSha256'] == CONTRACT_HASH, 'host contract identity')
    require(manifest['portableContractSha256'] == PORTABLE_HASH, 'portable contract identity')
    imports = manifest['imports']
    require(sorted(x['name'] for x in imports) == sorted(IMPORTS), 'import coverage/duplicate')
    dynamic = manifest['dynamicExports']
    require(sorted(x['name'] for x in dynamic) == sorted(DYNAMIC), 'dynamic coverage/duplicate')
    supplements = manifest['fixtureExports']
    require(sorted(x['name'] for x in supplements) == sorted(SUPPLEMENTS), 'supplement coverage/duplicate')
    def declaration(header, name):
        candidates = [line for line in header.splitlines() if line.startswith('PURR_') and re.search(r'\b' + re.escape(name) + r'\b', line)]
        require(len(candidates) == 1, 'header declaration coverage: ' + name)
        return candidates[0]

    for entry in imports:
        require(entry['nativeDeclaration'] == declaration(files['manual.h'].decode(), entry['name']), 'native declaration: ' + entry['name'])
        actual = [x for x in binding['imports'] if x['entryPoint'] == entry['name']]
        require(len(actual) == 1 and entry['managedDeclaration'] == actual[0], 'signature: ' + entry['name'])
        require(entry['nativeDeclaration'] and entry['conversion'] and entry['lifetime'], 'incomplete import record')
    for entry in dynamic:
        require(entry['nativeDeclaration'] == declaration(files['manual.h'].decode(), entry['name']), 'native dynamic declaration: ' + entry['name'])
        actual = [x for x in binding['dynamicExports'] if x['name'] == entry['name']]
        require(len(actual) == 1 and entry['managedDeclaration'] == actual[0], 'dynamic signature: ' + entry['name'])
        require(entry['nativeDeclaration'] and entry['conversion'] and entry['lifetime'], 'incomplete dynamic record')
    for entry in supplements:
        require(entry['declaration'] == declaration(files['fixture.h'].decode(), entry['name']) and entry['production'] is False, 'fixture declaration: ' + entry['name'])
    require(set(manifest['sourceSha256']) == SOURCES, 'source coverage')
    for path, expected in manifest['sourceSha256'].items():
        require(digest(files[path]) == expected, 'source mutation: ' + path)
    source = files['manual.cpp'].decode()
    definitions = re.findall(r'^PURR_API\s+\w+\s+(\w+)\([^;]*?\)\s*\{', source, re.M)
    definitions += re.findall(r'^PURR_EXPORT PurrManagedViewportVector (\w+) = nullptr;', source, re.M)
    require(sorted(definitions) == sorted(IMPORTS + DYNAMIC), 'owned definitions')
    fixture = re.findall(r'^PURR_API\s+[\w *]+\s+(\w+)\([^;]*?\)\s*\{', files['fixture.cpp'].decode(), re.M)
    require(sorted(fixture) == sorted(SUPPLEMENTS), 'fixture definitions')
    return True


def source_paths():
    return {name: (HERE.parents[3] / 'ImGuiPlayground.Checks/FullManualAbiChecks.cs' if name.startswith('../../../../') else
                   HERE.parent / 'api/purr_abi.h' if name == '../api/purr_abi.h' else HERE / name)
            for name in SOURCES}


def load(paths):
    binding_path = paths.input_file(HERE.parents[2] / '.tmp/native/binding-contract.json')
    raw = binding_path.read_bytes()
    require(digest(raw) == CONTRACT_HASH, 'binding contract bytes')
    manifest = json.loads(paths.input_file(HERE / 'manifest.json').read_text())
    require(set(manifest['sourceSha256']) == SOURCES, 'source coverage before file reads')
    sources = source_paths()
    for path in sources.values():
        paths.input_file(path)
    files = {name: path.read_bytes() for name, path in sources.items()}
    validate(manifest, json.loads(raw), files)
    return manifest
