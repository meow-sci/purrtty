#!/usr/bin/env python3
"""Quiet deterministic replay and fail-closed generator mutations (no sleeps)."""
from __future__ import annotations

import argparse
import copy
import importlib
import sys
from pathlib import Path

sys.dont_write_bytecode = True
generate = importlib.import_module('generate')
output_paths = generate.output_paths
HERE = Path(__file__).resolve().parent


def output_files(output):
    return [output/name for name in ('malformed.json', 'negative-cli.log', 'self-test.json',
                                    'failed-output/wrappers.cpp', 'failed-output/coverage.json')]


def run(contract_path, model_path, output):
    output = output_paths.checked(output, 'directory')
    files = output_files(output)
    protected = [contract_path, model_path, HERE/'mapping.json']
    output_paths.preflight(files, [output], protected)
    contract = generate.load(contract_path)
    model = generate.load(model_path)
    mapping = generate.load(HERE/'mapping.json')
    baseline, coverage = generate.generate(contract, model, mapping)
    replay, second_coverage = generate.generate(copy.deepcopy(contract), copy.deepcopy(model), copy.deepcopy(mapping))
    assert baseline == replay and generate.encoded(coverage) == generate.encoded(second_coverage)
    assert len(coverage['implemented']) == 1130 and len(coverage['reserved']) == 16
    assert len(coverage['dynamicExports']) == 6
    assert sum(m['returnConversion']=='aggregate-fieldwise-return' for m in coverage['implemented']) == 54
    assert sum(m['returnConversion']=='stable-native-reference-address' for m in coverage['implemented']) == 6
    assert sum(bool(m['callbackContracts']) for m in coverage['implemented']) == 16
    pointers = [b for m in coverage['implemented'] for b in m['boolPointerParameters']]
    assert len(pointers) == 21 and sum(b['direction'] == 'out' for b in pointers) == 4
    assert baseline.count(b'purr::bool_output(') == 4 and baseline.count(b'purr::bool_inout(') == 17
    assert b'BoolCells' not in baseline
    failures = []

    def reject(label, mutate, which='contract'):
        value = copy.deepcopy({'contract':contract, 'model':model, 'mapping':mapping}[which])
        mutate(value)
        try:
            generate.generate(value if which=='contract' else contract,
                              value if which=='model' else model,
                              value if which=='mapping' else mapping)
        except (generate.ContractError, KeyError, TypeError, ValueError):
            failures.append(label)
        else:
            raise AssertionError('mutation was accepted: '+label)

    reject('unsupported contract schema', lambda c:c.update(schemaVersion=99))
    reject('missing import', lambda c:c['imports'].pop())
    reject('duplicate import', lambda c:c['imports'].append(c['imports'][0]))
    reject('renamed import', lambda c:c['imports'][0].update(entryPoint='UnknownOperation'))
    reject('changed return signature', lambda c:c['imports'][0]['return'].update(type='System.Private.CoreLib::System.IntPtr'))
    reject('pointer-to-value signature mutation', lambda c:c['imports'][0]['parameters'][0].update(type='System.Private.CoreLib::System.Byte'))
    reject('unknown parameter marshalling', lambda c:c['imports'][0]['parameters'][0]['attributes'].append({'type':'UnsupportedMarshal'}))
    reject('changed calling convention', lambda c:c['imports'][0]['dllImport'].update(callingConvention='StdCall'))
    reject('selected DLL provenance mutation', lambda c:c['assemblies'][0].update(sha256='0'*64))
    reject('unsupported pointer width', lambda c:c['host'].update(pointerSizeBytes=4))
    reject('duplicate structural graph node', lambda c:c['types'].append(c['types'][0]))
    reject('missing owned mapping', lambda m:m['mappings'].pop(), 'mapping')
    reject('duplicate owned mapping', lambda m:m['mappings'].append(m['mappings'][0]), 'mapping')
    reject('unsupported native model schema', lambda d:d.update(schemaVersion=99), 'model')
    target_key = generate.declaration_key(mapping['mappings'][0]['native'])
    target_index = next(i for i,d in enumerate(model['declarations']) if generate.declaration_key(d)==target_key)
    reject('missing native target', lambda d:d['declarations'].pop(target_index), 'model')
    reject('ambiguous duplicate native target', lambda d:d['declarations'].append(d['declarations'][target_index]), 'model')
    reject('changed native signature', lambda d:d['declarations'][target_index].update(signature='void ()'), 'model')
    reject('changed native callback typedef', lambda d:d['callbackTypes'].update(ImDrawCallback='void (*)(void *)'), 'model')
    reject('changed native enum underlying type', lambda d:d['enumUnderlyingTypes'].update(ImGuiSortDirection={'spelling':'int','canonical':'int'}), 'model')
    reject('changed header provenance', lambda d:d['headers'].update({'imgui.h':'0'*64}), 'model')
    callback_index = next(i for i,m in enumerate(mapping['mappings']) if m['callbackContracts'])
    reject('missing opaque callback mapping', lambda m:m['mappings'][callback_index].update(callbackContracts=[]), 'mapping')
    reject('changed conversion recipe', lambda m:m['mappings'][0]['evidence']['conversions'][0].update(rule='erase-output'), 'mapping')
    bool_index = next(i for i,m in enumerate(mapping['mappings']) if m['boolPointerParameters'])
    out_index = next(i for i,m in enumerate(mapping['mappings']) if any(b['direction']=='out' for b in m['boolPointerParameters']))
    reject('missing Bool8 direction', lambda m:m['mappings'][bool_index].update(boolPointerParameters=[]), 'mapping')
    reject('duplicate Bool8 direction', lambda m:m['mappings'][bool_index]['boolPointerParameters'].append(m['mappings'][bool_index]['boolPointerParameters'][0]), 'mapping')
    reject('unsupported Bool8 direction', lambda m:m['mappings'][bool_index]['boolPointerParameters'][0].update(direction='guess'), 'mapping')
    reject('output-only changed to input access', lambda m:m['mappings'][out_index]['boolPointerParameters'][0].update(direction='inout'), 'mapping')

    def change_bool_policy(m, field, value):
        row = m['mappings'][bool_index]
        # Change stored evidence too: the supported-policy gate itself, not
        # merely stale duplicate evidence, must reject these mutations.
        row['boolPointerParameters'][0][field] = value
        row['evidence']['boolPointerParameters'][0][field] = value

    reject('unsupported Bool8 alias policy', lambda m:change_bool_policy(m, 'aliasPolicy', 'temporary shadow cells'), 'mapping')
    reject('unsupported Bool8 shared-write policy', lambda m:change_bool_policy(m, 'sharedWritePolicy', 'arbitrary noncanonical callback bytes'), 'mapping')

    # Full provenance changes, but the portable ABI structural projection does
    # not compare framework hashes/MVIDs or CLR measurements across target hosts.
    portable = copy.deepcopy(contract)
    portable['host'].update(rid='win-x64', architecture='X64', runtime='.NET 10 other-host')
    for assembly in portable['assemblies']:
        if assembly['resolutionSource'] != 'selected-directory':
            assembly['sha256'] = 'f'*64
            assembly['moduleMvid'] = 'different-runtime-module'
    for node in portable['types']:
        node['measurement'] = {'status':'unavailable', 'reason':'different host'}
        node['marshalerMeasurement'] = {'status':'unavailable'}
        node['moduleMvid'] = 'different-trace-token'
        for field in node.get('fields', []):
            field['runtimeOffset'] = {'status':'unavailable'}
            field['marshalerOffset'] = {'status':'unavailable'}
            field['storageWidth'] = {'status':'unavailable'}
    portable_source, _ = generate.generate(portable, model, mapping)
    assert portable_source == baseline

    output_paths.prepare(files, [output], protected)
    bad_path = output/'malformed.json'
    for label, content in [('malformed JSON', '{'), ('duplicate JSON keys', '{"schema":1,"schema":2}')]:
        bad_path.write_text(content)
        try:
            generate.load(bad_path)
        except (generate.ContractError, ValueError):
            failures.append(label)
        else:
            raise AssertionError('malformed JSON accepted')
    # Test the real CLI failure path and ensure it does not replace prior output.
    destination = output/'failed-output'
    (destination/'wrappers.cpp').write_bytes(baseline)
    native = HERE.parents[1]
    sys.path.insert(0, str(native.parents[1]))
    process_utils = importlib.import_module('ImGuiPlayground.native.process_utils')
    command = [sys.executable, str(HERE/'generate.py'), '--contract', str(bad_path),
               '--declarations', str(model_path), '--output', str(destination)]
    result = process_utils.run(command, capture_output=True, text=True, timeout=30, check=False)
    assert result.returncode != 0 and (destination/'wrappers.cpp').read_bytes() == baseline
    (output/'negative-cli.log').write_text(result.stderr)
    failures.append('real CLI rejects malformed input without output replacement')
    evidence = {'schema':'playground_imgui.api-generator-tests', 'schemaVersion':1,
                'negativeTestsPassed':failures, 'deterministicReplay':True,
                'portableProjectionIgnoresHostObservations':True,
                'wrapperSha256':generate.digest(baseline), 'inputContractSha256':generate.digest(contract_path.read_bytes()),
                'nativeExecution':'separate check.py --runtime; these are generator tests only'}
    (output/'self-test.json').write_bytes(generate.encoded(evidence))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--declarations', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        run(args.contract, args.declarations, args.output)
    except (generate.ContractError, ValueError, OSError) as error:
        parser.exit(1, 'API self-test failed: '+str(error)+'\n')


if __name__ == '__main__':
    main()
