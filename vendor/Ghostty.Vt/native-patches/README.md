# native-patches — the purrtty libghostty-vt patch set

`git format-patch` output of every local change the vendored native is built with, on top of the
pinned upstream commit (see the provenance table in [`../README.md`](../README.md)). **This folder
is the source of truth for the patches** — the ghostty checkout and its `purrtty/vt-video-fixes`
branch are disposable conveniences. Re-apply with `git am *.patch` on the pinned (or a newer)
upstream commit; the apply-on-clean-upstream path is verified (byte-identical tree to the branch).

| Patch | Drop when |
|---|---|
| `0001` untrack placement pins on replace/eviction | upstreamed (the leak: every same-id kitty re-display / eviction leaked a tracked pin — gotcha 36) |
| `0002` route zlib decompression around zig 0.15.2 std flate | zig ships fixes for ziglang/zig #25032 + #25035 **and** the pinned ghostty builds on that zig (verify with `../../../purrTTY.Terminal.Tests/Assets/zig-flate-decompress-repro.zig` against the candidate zig), or upstream reworks `decompressZlib` — gotcha 34 |
| `0003` APC bulk lane + kitty frame presize | upstreamed (pure performance: ~14× on the video consumption path; safe to drop at the cost of speed) |

Each patch carries its own zig tests; `zig build test-lib-vt` after applying is the first gate,
the purrTTY suite (`ZlibRealFrame_DecodesToGroundTruth` especially) is the second. After any
conflict resolution or upstream rebase, regenerate these files from the new base so the next bump
starts clean (command in ../README.md "Upgrading the native pin").
