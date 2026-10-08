# Full portable qualification (opt-in, not runtime promotion)

This runner consumes the accepted combined API/profile/manual/accessor implementation.
It does not replace BRUTAL, repair unsafe raw aliases, change shipped native assets, or
install native build hooks into ordinary .NET projects. Keep the Host/Checks folders
as siblings. All output destinations must be fresh physical paths outside consumed inputs.

## Trust and portable execution

A trusted maintainer builds `qualification.zip`, `extract_bundle.py` and
`build-result.json`. Retain the **manifest SHA and extractor SHA independently** of
an untrusted archive/extractor (for example in the reviewed maintainer handoff). Verify
the extractor bytes before executing it. Supplying a newly computed hash of an untrusted
manifest is not authentication. This is integrity relative to the trusted source-owned
build recipe/compiler, **not protection against a malicious maintainer or compiler**.
No private managed game DLLs are included. The operator supplies the frozen contributors
and other ordinary runtime dependencies locally.

The standalone extractor uses Python's standard library only. It validates the complete
archive before creating the destination: anchor, strict schema, file hashes/membership,
portable names, duplicate/case aliases, traversal/absolute paths and regular file kinds.
The runtime consumer needs Python 3.9+, the .NET 10 SDK/NuGet restore and a real supported
graphics session, but **not Zig, an Apple SDK, producer caches or producer absolute paths**.
Absolute paths inside compiler/debug provenance are historical only, not runtime inputs.

For `run`, `guard`, `test`, and `ordinary`, invoke `run.py` from the selected extracted bundle's own `source/ImGuiPlayground/native/full/qualification` directory. Before importing any bundle-owned Python helper, that entrypoint checks the independently retained anchor, strict manifest root and file-pin shape, safe paths, complete file/directory membership and every member byte hash. It rejects a selected bundle whose source tree is not the executing entrypoint's tree. This boundary does not defend against a malicious entrypoint/bootstrap already executing or concurrent filesystem swaps.

From a directory containing the independently verified extractor and archive:

```sh
# Substitute the independently retained manifest SHA, never a hash refreshed from
# an untrusted bundle. Use physical paths (on macOS, not /var aliases).
ANCHOR='<independently retained manifest SHA-256>'
python3 extract_bundle.py --archive qualification.zip --anchor "$ANCHOR" --output '/physical/path/extracted qualification'
RUNNER='/physical/path/extracted qualification/source/ImGuiPlayground/native/full/qualification/run.py'
BUNDLE='/physical/path/extracted qualification'
python3 "$RUNNER" run --bundle "$BUNDLE" --anchor "$ANCHOR" --selection '/physical/path/current/dll' --output '/physical/path/full-run'
python3 "$RUNNER" test --bundle "$BUNDLE" --anchor "$ANCHOR" --selection '/physical/path/current/dll' --run-output '/physical/path/full-run' --output '/physical/path/full-negatives'
python3 "$RUNNER" ordinary --bundle "$BUNDLE" --anchor "$ANCHOR" --selection '/physical/path/current/dll' --output '/physical/path/ordinary-gates'
```

Selection precedence is `KSAFolder`, then explicit `--selection`, then `KSA_DLL_DIR`.
Unset a conflicting `KSAFolder`; there is no installation or stale-bin fallback.
On Windows use `python` and PowerShell variables with the same arguments; a complete
negative run needs symlink creation privileges (Developer Mode/admin). Privilege/display
failures are failures, not silently passed or skipped gates. Windows x64/Linux x64
native/CLR/graphics execution remains **user-owned PENDING**. Cross-build and publish
results are not execution. Manual desktop clipboard/DPI/focus/resize checks remain separate.

## What the consumer actually validates

`run` first verifies the externally anchored complete bundle. It performs an actual
Checks -> Host project-reference build with isolated `--artifacts-path`; it does not
reuse a managed harness or producer bin. Six fresh staged processes run in order:

1. `accessor` — fixture, fresh allocator audit before ImGui allocations;
2. `profile` — fixture, actual CLR layouts and all five aggregate/reference-return
   families, actual enum storage and 2,706 real typed/raw enum transports;
3. `manual` — fixture, 23 fixed formats, two genuine target `va_list` calls and three
   dynamic callback bridge lifetimes;
4. `composition` — fixture, the accepted same-image shared context/platform/storage/
   allocator composition with exact callback/user-data restoration;
5. `renderer` — **production** image, existing synchronous ordinary Checks code;
6. `capture` — **production** image, actual ordinary Host `Main(["--capture", path])`.

Every process checks selected -> built -> staged -> actual loaded contributor SHA/MVID/
location, exports and remeasures its **own** CLR contract, executes its target-native
layout and enum probes, and validates evidence before the sole Host owner opens ImGui.
The friend-only Host seam delegates to its existing cached owner/resolver. No second
ImGui resolver, unload or ownership transfer is introduced. Actual exported addresses
are attributed to that staged image, and production rejects all fixture exports.
Renderer/capture additionally check selected/built/staged/loaded managed GLFW, the
ordinary pinned native GLFW/font assets, and the actual loaded GLFW image.

Portable closure comparison deliberately excludes host provenance and measured values,
then separately requires complete measurement keys/statuses and compares actual values.
It is **not equality to a macOS whole-contract hash**. Independent producer inventories
close 1,146 imports, six dynamics, 23 helpers and 371 fixture-only exports (1,175 production,
1,546 fixture). The helper identity remains
`e86ced3fb638c94c285378697c77e6e723bd75864c337e2b557449b4e4677f72`.

All-target compiler tables cover 3,102 storage rows, 47 bitfield declared-type rows,
143 promoted enum rows, alignment/array count/element stride and configured STB capacity.
The exact LLVM grammar accepts explicit zero rows and rejects omissions/unknown
initializers. Those cross-target constants are static evidence. The consuming host must
execute its own pinned probes and compare them to those constants. Layout comparison
retains the precise 57 classified conflicts, 44 unsafe raw aliases, CLR 6/7/13-byte and
Size1/static-member hazards. No broader waiver or repaired-raw-binding claim is made.
Native helpers continue to use native member access; C# bit masks remain test oracles.

`test` requires the `--run-output` from a successful `run` on the same target and bundle. It selects `process-accessor/actual-contract.json` only after checking run status/RID/anchor, the accessor report hash, its guard report and the guard's actual-contract hash/RID. It re-runs the verifier against that target-local CLR contract and fresh target-native probes before creating test output. `evidence/managed-contract.json` remains the producer/declarative reference; it is never substituted for runtime measurements or relabelled as a foreign target.

`test` exercises schema/omission/duplicate/signature/callback/ref-return/kind/layout/
alignment/stride/representation/export-classification mutations, strict LLVM zero and
unsupported-initializer parsing, archive metadata and actual pre-import canaries,
redirected source/library/config/cache/initializer inputs, protected outputs and actual
CLR child identity/wrong-image negatives. A guard negative must leave `native-open.marker`
and the success report absent. Canary execution has a positive control. Reports retain
the rejection diagnostics, counts and whether actual child negatives ran.

`ordinary` uses extracted sibling source under space-containing paths and hostile parent
props/targets/package/NuGet files, runs actual unchanged shipped renderer/input/PNG checks,
then the existing three-RID package/republish/stale-file/hash/architecture/negative checks.
It checks explicit selection failure/precedence, local restore boundary, copied-source
immutability and unchanged production assets/fonts. It does not substitute full native
artifacts into ordinary bin or runtimes.

Successful commands are quiet. Retain `results.json`, every `*.command.json`/`*.log`, each
`process-*/report.json`, current CLR JSON, probe observations/guard report, stage receipt
and capture PNG. Missing reports, timeouts, crashes, dependency failures and absent
platform runs are never passes. Processes have bounded process-tree cleanup. Input
redirect guards are not a sandbox against hostile concurrent filesystem mutation or a
compromised trusted SDK/bootstrap environment.

## Trusted maintainer build

First run the documented [combined producers/build](../combined/README.md), with fresh
outputs matching the current source freeze. Then, from the sibling workspace root:

```sh
python3 ImGuiPlayground/native/full/qualification/run.py build --combined '/physical/path/current-combined-build' --output '/physical/path/new-qualification-build'
```

This validates the combined compiler/export/kind/dependency/object-ownership receipts,
copies the source/evidence/native artifacts and all-target probe executables, and derives
complete per-target compiler tables from the same pinned selected source/config. It
records Zig 0.17.0 executable identity and target triples. Independently retain the new
anchor; never patch an old bundle or refresh its anchor in place after source changes.
The six upstream declaration patches, source lock/config, original managed DLLs,
production runtime pins/fonts and root repository boundary remain unchanged.

Current macOS automated production/fixture and ordinary gates have been exercised;
final frozen evidence and independent review disposition belong in the maintainer
handoff. This is representative semantic/renderer proof, not exhaustive execution of
all widget/internal endpoint/domain behavior or manual desktop integration.
