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
| Native commit (pinned) | `7092b39445bebfd3178f562eb9e5fa9a95a32332` (1.3.2-dev) |
| Vendored on | 2026-06-07 |

The managed sources under `src/` were copied from the upstream `src/Ghostty.Vt/`, retargeted
to `net10.0` (purrtty's TFM), with purrtty-specific additions clearly marked in-file as
"purrtty addition".

## Native library

The binding loads the **shared library** via P/Invoke (`NativeLibraryResolver` resolves
`libghostty-vt.dylib` on macOS, `ghostty-vt.dll` on Windows, `libghostty-vt.so` on Linux). The
native binaries are gitignored — build them from pinned upstream ghostty (zig 0.15) and drop them
in `native/`.

> ⚠️ Use the **shared** library only. ghostty's build also emits `ghostty-vt-static.lib` (a static
> archive) and `ghostty-vt.lib` (an import lib) under `zig-out/lib/` — **neither can be loaded at
> runtime** by a managed plugin. The DLL is `zig-out/bin/ghostty-vt.dll`.

**macOS (osx-arm64)** → `native/libghostty-vt.dylib`:

```bash
export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"
cd /path/to/ghostty
zig build -Demit-lib-vt                       # → zig-out/lib/libghostty-vt.dylib
```

**Windows (win-x64)** → `native/ghostty-vt.dll`:

```powershell
$env:PATH = "C:\zig-x86_64-windows-0.15.2;$env:PATH"
cd C:\path\to\ghostty
zig build -Demit-lib-vt -Dtarget=x86_64-windows-gnu -Doptimize=ReleaseFast   # → zig-out/bin/ghostty-vt.dll
```

The `gnu` target is required (it compiles ghostty-vt's `highway`/`simdutf` C++ SIMD deps; the default
`native-native-msvc` target fails to build them). Build at **baseline cpu** — do **not** pass
`-mcpu native`: a host-tuned binary is non-portable and was observed to access-violate inside
`vt_write`. The SIMD libs runtime-dispatch to AVX2/etc. regardless, so baseline costs little.

osx-arm64 + win-x64 are vendored today; linux-x64 / full multi-RID packaging is follow-up work.
We **pin** the C library; we do **not** fork it. All purrtty changes live in the managed binding (`src/`).

The library is loaded by `src/Native/NativeLibraryResolver.cs` (a purrtty addition) which
resolves the native next to the consuming assembly — necessary inside KSA's plugin
`AssemblyLoadContext`.

## License

The binding is **MIT licensed**. The upstream license is preserved verbatim in
[`LICENSE`](./LICENSE). See also the repo-root `THIRD-PARTY-NOTICES.md`.
