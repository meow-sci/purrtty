# Ghostty.Vt (vendored)

This directory is a **vendored, owned** copy of the managed `Ghostty.Vt` binding from
[`libghostty-vt-dotnet`](https://github.com/deblasis/libghostty-vt-dotnet), the unofficial
.NET bindings for **libghostty-vt** — the standalone VT engine extracted from
[Ghostty](https://github.com/ghostty-org/ghostty).

purrtty delegates all terminal emulation to this engine. We vendor the binding (rather than
consume the NuGet package) so we can extend it at the source: configurable scrollback,
selection (gesture / per-row / format), default cursor style & blink, and the render-hot frame
read path in `src/RenderState.FrameReader.cs` — dirty-flag consumption
(`ghostty_render_state_set` / `ghostty_render_state_row_set`), a forward-only
`RenderFrameReader` (one reused cells handle, ~2 native calls per cell), `RawCell` (managed
bit-decode of the packed `page.Cell` u64), UTF-8 grapheme reads, and `RawCellLayout.Validate()`
(runtime cross-check of the decode against `ghostty_cell_get`; **must pass after any native pin
bump** — it runs once at startup and as a test).

## Provenance

| Item | Value |
|---|---|
| Binding upstream | `deblasis/libghostty-vt-dotnet` |
| Binding commit | `68e8b3e75d612e2e658d18d9e9982b1f857f50f0` |
| Native engine | `ghostty-org/ghostty` |
| Native base commit (pinned) | `c22df09da` (main, 2026-07-02: "libghostty: fix utf-8 grapheme length overflow") |
| **Native local patches** | branch `purrtty/vt-video-fixes` in the local ghostty checkout: `ac3fee170` (kitty: untrack placement pins on replace/eviction — the per-frame video pin leak, gotcha 36), `bb9c398bf` (kitty: route zlib image decompression around the zig 0.15.2 std flate bugs, ziglang/zig #25032/#25035 — the `o=z` heap corruption, gotcha 34), `d10bcee6d` (terminal: bulk lane for APC string payloads + kitty frame presize — the per-byte VTWrite parse of video payloads; ~14× consumption-path throughput, gatOS perf plan P7). **Rebuild from that branch, not bare upstream**, until they land upstream. |
| Built with | zig 0.15.2, all three RIDs cross-compiled from one Windows host |
| Vendored on | 2026-07-02 |
| Previous pin | `7092b39445bebfd3178f562eb9e5fa9a95a32332` (1.3.2-dev, vendored 2026-06-07) |

The managed sources under `src/` were copied from the upstream `src/Ghostty.Vt/`, retargeted
to `net10.0` (purrtty's TFM), with purrtty-specific additions marked in-file as
"purrtty addition" and behavioral fixes to upstream code marked "purrtty fix".

### Divergence from upstream (a fork in all but name)

The vendored copy has diverged substantially and deliberately; treat upstream as a
reference, **never** re-vendor wholesale (it would revert the fixes below). Grep for
`purrtty addition` / `purrtty fix` to find every divergence in-file. The load-bearing fixes:

- **Encoders return owned bytes** (`KeyEncoder`/`MouseEncoder.Encode`): upstream returned a
  span into a dead stackalloc — a use-after-scope the caller read as clobbered stack.
- **`KeyEvent.Text` keeps a persistent pin**: the native event stores the raw utf8 pointer
  and reads it at encode time; upstream's pin ended at the setter's return (GC could move or
  collect the array before the read). `Text = null` also clears the native pointer now.
- **Enquiry/Xtversion reply pins** (`Terminal.PinReply`): same lifetime class — the native
  side reads the returned buffer after the managed callback returns.
- **`Paste.Encode` copies to a mutable scratch**: `ghostty_paste_encode` rewrites its input
  in place, and the caller's span may point at read-only memory (u8 literals).
- **`MouseEvent.Modifiers` marshals as u16**; double-free guards on `KeyEvent`/`MouseEvent`
  re-dispose; `MaxScrollback` plumbed (upstream hardcoded 1000 **bytes** — effectively none).
- **Kitty graphics binding is restored** (`KittyGraphics.cs` + `KittyImage*`/`KittyPlacementLayer`
  enums + the `ghostty_kitty_graphics_*` P/Invokes). It was deleted as dead code in 91fedcf and
  rewritten clean against the pinned headers (`src/terminal/c/kitty_graphics.zig`): a reusable
  `KittyPlacementCursor` (Reset/MoveNext/Current using `placement_render_info`, plus GetImage /
  CopyImageData / **HashImageData** — an FNV-1a-64 content hash over the engine's stored payload,
  no copy, the change signal for video-style same-id re-transmits; the payload *length* is not
  usable, see repo gotcha 33). Read-only — the engine parses/stores graphics inside VTWrite;
  there is no command-injection API. Enums (`KittyImageFormat`/`Compression`/`PlacementLayer`)
  are 0-based C enums — re-verify against the headers on a pin bump.
  **Fixed native bug (pre-2026-07-02 pins):** committing a kitty `o=z` (zlib) payload of
  compressible data memory-corrupted/segfaulted inside `VTWrite` — a zig 0.15.2 std flate bug,
  patched in the `purrtty/vt-video-fixes` native (gotcha 34). `ZlibRealFrame_DecodesToGroundTruth`
  is the standing regression gate; it must stay green on every pin bump.
- **`KeyEvent.UnshiftedCodepoint` is bound and set for printable keys.** The kitty keyboard
  protocol encoder builds `CSI <code>;<mods>u` from it (no `key.codepoint()` fallback, unlike
  legacy) and emits **nothing** without it — so a Ctrl/Alt+letter dropped silently for any app
  that enabled the protocol. See repo-root CLAUDE.md gotcha 3.

**Pruned surface:** binding modules purrtty never calls were removed rather than carried as
freight (OSC/SGR parsers, `Formatter`, `Focus`, `SizeReport`, `BuildInfo`,
`Sys`, batch getters, terminal color/pwd/title getters & setters, and enums that had drifted
from the pinned headers). Restore from upstream (then re-verify against the pinned headers)
if a feature needs one of them — drifted enums especially must be regenerated from the pin,
not copied back as-is. The deliberately-kept "fully populated" alternates
(`RenderStateRowEnumerator`/`RenderStateCellEnumerator`, `GridRef.GetCell/GetStyle/...`)
stay, per the navigation notes in the repo-root CLAUDE.md.

> Note: `TerminalOptions.MaxScrollback` is in **bytes**. The upstream C header comment says
> "number of lines" — the header is wrong (ghostty's `Screen.zig` documents bytes); don't
> "fix" the binding doc to match the header on a pin bump.

## Native library

The binding loads the **shared library** via P/Invoke (`NativeLibraryResolver` resolves
`libghostty-vt.dylib` on macOS, `ghostty-vt.dll` on Windows, `libghostty-vt.so` on Linux). The
prebuilt binaries are **checked into source control**, one per RID:

```
native/osx-arm64/libghostty-vt.dylib
native/win-x64/ghostty-vt.dll
native/linux-x64/libghostty-vt.so
```

The csproj copies **all of them, flat,** into every build output regardless of host OS (the
filenames are platform-distinct, so they coexist), and `NativeLibraryResolver` picks the one
matching the running OS. A build from any host therefore yields one platform-agnostic mod that
runs on macOS, Windows, and Linux — which is what lets a single Linux CI job produce the release
for every platform. If two RIDs of the same OS are ever added (osx-x64, linux-arm64, ...) the
flat names collide; switch the output to the `runtimes/<rid>/native` layout, which the resolver
already probes.

> ⚠️ Use the **shared** library only. ghostty's build also emits `ghostty-vt-static.lib` (a static
> archive) and `ghostty-vt.lib` (an import lib) under `zig-out/lib/` — **neither can be loaded at
> runtime** by a managed plugin. The Windows DLL is `zig-out/bin/ghostty-vt.dll`.

### Rebuilding (required after every pin bump)

All three are **cross-compiled from a single host** (any OS) with zig 0.15, from the pinned
ghostty commit, at `ReleaseFast` with explicit baseline targets. Do **not** pass `-mcpu native`:
a host-tuned binary is non-portable and was observed to access-violate inside `vt_write`; the
SIMD deps (`highway`/`simdutf`) runtime-dispatch to AVX2/etc. regardless, so baseline costs
little. Windows must use the `gnu` ABI (the default `msvc` target fails to compile those C++
deps). The Linux build pins glibc 2.31 (max versioned symbol actually used: 2.29 ≈ Ubuntu
20.04/Debian 11); zig statically links its own libc++, so glibc is the only runtime version
dependency. The Linux `.so` keeps DWARF debug info even at `ReleaseFast` (~7.6 MB) — strip it
before vendoring; the build's `-Dstrip` flag does not reach the vt shared lib.

```bash
export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"   # macOS host shown; any host with zig 0.15 works
cd /path/to/ghostty   # at the pinned commit

zig build -Demit-lib-vt -Dtarget=aarch64-macos -Doptimize=ReleaseFast
cp -L zig-out/lib/libghostty-vt.dylib <purrtty>/vendor/Ghostty.Vt/native/osx-arm64/   # -L: it's a symlink

zig build -Demit-lib-vt -Dtarget=x86_64-windows-gnu -Doptimize=ReleaseFast
cp zig-out/bin/ghostty-vt.dll <purrtty>/vendor/Ghostty.Vt/native/win-x64/

zig build -Demit-lib-vt -Dtarget=x86_64-linux-gnu.2.31 -Doptimize=ReleaseFast
llvm-strip --strip-debug -o <purrtty>/vendor/Ghostty.Vt/native/linux-x64/libghostty-vt.so \
    zig-out/lib/libghostty-vt.so.0.1.0           # llvm-strip: e.g. /opt/homebrew/opt/llvm@21/bin
```

After a pin bump: update the pinned commit in this README, rebuild all three, and run the
purrTTY.Terminal.Tests suite — `RawCellLayout.Validate()` fails loudly if the native cell
bit-layout changed.

We **pin** the C library **plus the two local patches above** (the `purrtty/vt-video-fixes`
branch); everything else lives in the managed binding (`src/`). Re-apply/rebase those patches on
any future pin bump — building bare upstream re-introduces the video pin leak and the `o=z`
corruption — and re-run `ZlibRealFrame_DecodesToGroundTruth` plus the full purrTTY.Terminal.Tests
suite as the gate.

The library is loaded by `src/Native/NativeLibraryResolver.cs` (a purrtty addition) which
resolves the native next to the consuming assembly — necessary inside KSA's plugin
`AssemblyLoadContext`.

## License

The binding is **MIT licensed**. The upstream license is preserved verbatim in
[`LICENSE`](./LICENSE). See also the repo-root `THIRD-PARTY-NOTICES.md`.
