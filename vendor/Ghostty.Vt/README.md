# Ghostty.Vt (vendored)

This directory is a **vendored, owned** copy of the managed `Ghostty.Vt` binding from
[`libghostty-vt-dotnet`](https://github.com/deblasis/libghostty-vt-dotnet), the unofficial
.NET bindings for **libghostty-vt** — the standalone VT engine extracted from
[Ghostty](https://github.com/ghostty-org/ghostty).

purrtty delegates all terminal emulation to this engine. We vendor the binding (rather than
consume the NuGet package) so we can extend it at the source: configurable scrollback,
selection (gesture / per-row / format), default cursor style & blink, and batched cell reads.

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

`native/libghostty-vt.dylib` is built from pinned upstream ghostty for **osx-arm64** with:

```bash
export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"   # ghostty needs zig 0.15
cd /path/to/ghostty
zig build -Demit-lib-vt
# → zig-out/lib/libghostty-vt.dylib
```

Only the current host platform (macOS arm64) is vendored for now. Multi-RID packaging
(`win-x64`, `linux-x64`) is follow-up work. We **pin** the C library; we do **not** fork it.
All purrtty changes live in the managed binding (`src/`).

The library is loaded by `src/Native/NativeLibraryResolver.cs` (a purrtty addition) which
resolves the native next to the consuming assembly — necessary inside KSA's plugin
`AssemblyLoadContext`.

## License

The binding is **MIT licensed**. The upstream license is preserved verbatim in
[`LICENSE`](./LICENSE). See also the repo-root `THIRD-PARTY-NOTICES.md`.
