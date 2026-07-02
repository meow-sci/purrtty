# Test assets

## `gatos-frame-{a,b}.{kitty,png}` — real gatOS screen-stream frames

Two live KSA viewport captures produced by the gatOS `/sim/display` screen stream
(gatOS repo, branch `feature/display`, STREAM_PLAN.md §11 tier-1/2 debug dump,
2026-07-02, 320×180 `rgba-zlib`):

- the `.kitty` file is the **exact, byte-for-byte Kitty graphics unit** gatOS's
  `KittyEncoder` publishes on `/sim/display/stream` for one frame — the full
  in-place video wrapper: `ESC 7` · `ESC [H` · delete (`a=d,d=I,i=1`) ·
  chunked transmit+display (`a=T,q=2,f=32,o=z,i=1,p=1,s=320,v=180,C=1,m=…`) ·
  `ESC 8`;
- the sibling `.png` holds the **ground-truth pixels of the same frame**
  (encoded host-side from the same BGRA buffer, before any Kitty encoding).

Both pairs passed gatOS's strict offline protocol validation
(`KittyStrict`/`KittyDumpPairTests`: grammar, chunking, m= sequencing, escape
budget, and exact pixel round-trip vs the PNG), so any purrTTY-side test failure
against them localizes a bug to purrTTY/libghostty, not the producer.

How `KittyScreenStreamAssetTests` uses them:

- the **PNGs** are the ground truth: the tests build gatOS-shaped **raw `f=32`**
  video units from their pixels (frames a and b are ~4 s apart, so the pair
  drives the equal-length delete-and-retransmit-same-id "video" path);
- the **`.kitty` files** (`o=z` zlib units) are kept as the byte-exact repro of
  the pinned libghostty-vt native memory corruption on compressed payloads
  (gotcha 34) — fed only by the `[Explicit]` crash-repro test, to be re-run on
  every native pin bump.

`zig-flate-decompress-repro.zig` is the **standalone ~15-line isolation** of that
native bug (gotcha 34): ghostty's `decompressZlib` verbatim against zig 0.15.2's
`std.compress.flate.Decompress` — no ghostty, no purrtty. Feed it any zlib stream
whose decompressed output exceeds ~64 KiB with back-references (e.g. zlib of
230 KB of zeros): `zig run zig-flate-decompress-repro.zig -- payload.bin` panics
at `unreachable` in `std.Io.Writer.rebase` (Debug) / corrupts silently
(ReleaseFast). Zig upstream: ziglang/zig #25032 / #25035 (open). Use it to test
candidate zig versions before rebuilding the native.
