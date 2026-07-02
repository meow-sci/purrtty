# AUDIO_PLAN.md — Terminal audio in purrTTY (escape stream → FMOD)

## Context

purrTTY renders real terminals inside KSA but is silent: no bell, no sounds, no way for a TUI
(e.g. a gatOS flight computer) to play audio. This document is a feasibility-validated build plan
for **terminal-protocol-driven audio**: programs running inside a purrTTY session emit escape
sequences; purrTTY decodes them and plays the audio **from in-memory bytes through the game's own
FMOD instance** — no asset files, no temp files, no out-of-band channel.

The plan is grounded in four research passes (2026-07-02): a survey of every known terminal audio
protocol, an analysis of KSA's decompiled FMOD audio subsystem, the working `byo-music` mod in the
unscience repo, and purrTTY's existing escape-sequence extension surface.

Headline: **GREEN — pure C#, no native rebuild, no game-asset files.** Every hard sub-problem is
already solved somewhere in the stack:

1. The interception mechanism exists (`OscSidecar` pre-scan tee; the retired OSC 1010 RPC proved
   the pattern; unknown OSC/APC sequences are harmlessly discarded by libghostty-vt).
2. The event plumbing template exists (`ITerminalSurface.Bell` — fully wired, currently
   unconsumed).
3. The FMOD in-memory recipe is proven **inside KSA's own code** (`GameAudio.CreateFmodSound`
   uses `Mode.OpenMemory` + `CreateSoundExInfo.Length`), and `GameAudio.System` is a public
   static the mod can call directly.

---

## Part 1 — Terminal audio landscape: is there a standard?

**No.** There is no shipped, standard, or de-facto protocol for transmitting arbitrary audio data
(PCM/MP3) through a terminal escape stream. What ships today, by capability tier
(T0 beep · T1 synthesized notes · T2 named/preconfigured sounds · T3 arbitrary audio data):

| Protocol | Tier | Syntax | Adoption |
|---|---|---|---|
| **BEL** | T0/T2 | single byte `0x07` | Universal. What plays is terminal config (mintty `BellFile`, kitty `bell_path`, Windows Terminal `bellSound` all allow custom sound files) |
| **DECPS** (VT520 "Play Sound") | T1 | `CSI Pvol;Pdur;Pnote ,~` — vol 0–7, dur in 1/32 s (0–255), note 0–25 chromatic from C5 | mintty, Windows Terminal (triangle-wave synth), Contour, RLogin, ANSICON. **Not xterm** (xterm only has bell-volume DECSWBV/DECSMBV) |
| **ANSI Music** (BBS MML) | T1 | `CSI M`/`CSI N`/`CSI \|` + GW-BASIC PLAY string, terminated by `SO` (0x0E) | SyncTERM, Qodem, retro BBS scene only. `CSI M` conflicts with ECMA-48 DL |
| **kitty OSC 99** notifications | T2 | `OSC 99 ; …s=<named sound>… ST` | kitty ≥ 0.36; named sounds only (`error`, `info`, …) |
| **mintty OSC 440** | T2 | `OSC 440 ; sound[:opts] BEL` — plays a named local WAV | mintty only, marked experimental |
| **Terminology** media | T2½ | `ESC } is/ic… \0` — media by **path/URL**, not payload | Terminology only |
| **kitty "Terminal Audio Protocol" draft** | **T3** | `ESC _ A <k=v,…> ; <base64> ESC \` — APC, kitty-graphics-shaped | **Unmerged draft** — kovidgoyal/kitty discussion #9507 (active, Feb 2026) + sopyb spec gist. Raw s16le LPCM baseline, `m=`-chunked, `a=t/p/d/q` actions |

Verified negatives: iTerm2 OSC 1337 has no audio subcommand (`File=` downloads but never plays);
DomTerm sanitizes `<audio>` out of its HTML injection; notcurses declared sound out of scope;
mpv/timg play video-in-terminal audio **out-of-band** through the OS, never through the tty. The
only shipped audio-bytes-in-the-stream prior art is the experimental `ctx` terminal (8 kHz μ-law,
undocumented).

**Design conclusion:** we implement a custom protocol, but we don't design it from scratch — we
adopt the envelope of the **kitty `ESC _ A` draft** (itself the proven kitty-graphics envelope:
key=value controls, chunked base64, ids, actions). That costs nothing proprietary, reuses a vetted
design, and leaves the door open to converging with kitty if the draft ever lands. purrTTY already
consumes kitty-graphics-shaped APC traffic at video rates, so the shape is battle-tested here.

Cheap compatibility wins worth taking along the way: **BEL → audible bell** (universal, zero
protocol work — the event already exists in purrTTY) and, as an optional retro stretch, **DECPS**
notes.

---

## Part 2 — KSA audio: how the game plays sound (FMOD analysis)

Decompiled sources: `ksa-game-assemblies\current\decomp\` (findings re-verified against the
`ksa-game-assemblies_2026.6.8.4680` snapshot — identical).

### The binding — `Brutal.FmodApi` (FMOD Core, custom source-generated)

- Not the stock `fmod.cs`. One giant static class `Fmod`
  (`decomp\Brutal.FmodApi\Fmod.cs`, ~6.8k lines) of `[LibraryImport("fmod", EntryPoint="FMOD5_…")]`
  extension methods in `TryXxx` (returns `Result`) / throwing `Xxx` pairs.
- Handle types are 1-field readonly structs over `nint`: `FmodSystem`, `Sound`, `Channel`,
  `ChannelGroup`, `Dsp`.
- `CreateSoundExInfo` (224-byte explicit layout): ctor auto-sets `CbSize`; fields incl. `Length`,
  `Format`, `NumChannels`, `DefaultFrequency`, `PcmReadCallback`.
- `Mode` flags incl. `OpenMemory = 0x800`, `OpenMemoryPoint`, `OpenRaw`, `OpenUser`,
  `CreateStream`, `LoopNormal/LoopOff`, `_2d/_3d`.
- Byte buffers pass through `Brutal.Strings.RefString8`, which implicitly converts from
  `ReadOnlySpan<byte>`/`byte[]` — the "name or data" argument of `CreateSound` **is** the buffer.
- FMOD **Studio** bindings exist (`Brutal.FmodApi.Studio`) but the game does not use them — no
  banks, no events. Everything is Core. **No bank/event system to conflict with.**

### The manager — `KSA.GameAudio` (static class)

- **`public static FmodSystem System { get; private set; }`** — the game's FMOD system is
  publicly reachable by a mod. No service locator needed.
- Init in `GameAudio.OnApplicationStart()`, called from `Program.cs:653` **before** the renderer
  and long before mods load: `SystemCreate` → `Init(1024, InitFlags._3dRightHanded, 0)` (default
  thread-safe mode) → stream buffer 32 KiB → custom managed file callbacks.
- Creates 4 Core `ChannelGroup`s — `Master`, `Sfx`, `Ui`, `Music` (`ChannelGroupType` enum) —
  publicly accessible via **`GameAudio.GetChannelGroup(ChannelGroupType)`**.
  ⚠ The enum groups are **siblings** under FMOD's system master, not parented under the game's
  "Master" group — routing into `Sfx` picks up the SFX slider but *not* the game's Master slider.
- Volume sliders (`GameSettings.Current.Audio.{Master,Sfx,Ui,Music}Volume`, 0–100) map through a
  −80 dB→0 dB curve in `GameAudio.ApplySettings()` onto the groups; re-applied on settings change.
- **Per-frame pump:** `GameAudio.UpdateAudio(dt)` — first line `System.Update()` — is called from
  `Program.PostRender` (`Program.cs:2055`) on the **main game/render thread**, every frame. A mod
  never pumps, closes, or releases the system.
- Play idiom used everywhere in game code: `TryPlaySound(sound, group, paused: true, out channel)`
  → configure volume/pitch → unpause (avoids a first-buffer pop).

### The in-memory recipe — already proven in engine code

`GameAudio.CreateFmodSound` (`GameAudio.cs:241`) has two branches; the non-streaming one is our
recipe verbatim:

```csharp
LoadFileMemory(path, out byte* buffer, out int length);
var ex = new CreateSoundExInfo { Length = (uint)length };
result = System.TryCreateSound(new ReadOnlySpan<byte>(buffer, length),
                               mode | Mode.OpenMemory, in ex, out sound);
NativeMemory.Free(buffer);   // ← freed immediately: OpenMemory COPIES into FMOD's buffer
```

- `Mode.OpenMemory` + `CreateSoundExInfo.Length` + the byte span: MP3/WAV/OGG/FLAC container
  formats are **auto-detected** by FMOD.
- Headerless PCM: add `Mode.OpenRaw` and set `Format` (e.g. `PCM16`), `NumChannels`,
  `DefaultFrequency`.
- Because `OpenMemory` copies, the source `byte[]` may be GC'd immediately after the call
  (`OpenMemoryPoint` would require pinning for the sound's lifetime — avoid).
- `PcmReadCallback`/`OpenUser` (procedural streaming) are bound but unused by the game — available
  for a future streaming phase.
- There is **no public "play these bytes" helper** — every game entry point wants a
  `SoundBehavior`/asset. A mod playing raw bytes calls Core directly:
  `System.TryCreateSound(...)` + `System.TryPlaySound(...)`.

### The `byo-music` precedent (unscience repo)

`unscience\byo-music` proved a mod can produce audible audio in-game, but via the **asset
pipeline**, not FMOD interop: it ships an `Assets.xml` declaring a `MusicPlaylist` over
`Music/Sabotage.mp3` (`StreamFromDisk="true"`) and calls
`ModLibrary.Get<MusicPlayList>(id).PlayMusic(out _)`. All FMOD work happens inside `GameAudio`.
Lessons for us:

- It validates init-timing and the game's willingness to play mod-supplied compressed audio
  through its mixer, volume sliders included.
- The asset route requires **files on disk declared at load time** — useless for audio that
  arrives as bytes over an escape stream at runtime. We must use the direct Core path instead.
- Gotcha found there: the decompiled `SoundReference` binds XML attribute `ChannelId`, not
  `Channel` — asset-XML channel routing may be version-skewed. Irrelevant for us (we route via
  `GetChannelGroup` in code), but a reminder not to trust asset XML for this feature.

### Rules a mod must follow (threading & lifecycle)

1. Call FMOD only from the **main game/render thread** (StarMap hooks qualify) — interleaves
   cleanly with the game's own `System.Update()` pump.
2. Never call `System.Update()` / `Close()` / `Release()` — the game owns them.
3. Audio is safe from `[StarMapAllModsLoaded]` onward (FMOD init precedes mod loading).
4. **You own the `Sound` handles you create** — `sound.TryRelease()` when done; releasing while a
   channel plays cuts it. `Channel`s free themselves when playback ends.
5. Raw-Core playback bypasses the game's time-warp muting (`Universe.SimulationSpeed > 10` stops
   built-in SFX). Terminal audio is UI-adjacent, so we deliberately **don't** replicate the check
   (v1); revisit if it feels wrong at high warp.

---

## Part 3 — purrTTY integration points (what exists today)

### The byte path and threads

- PTY read thread (`ConPtyOutputPump.ReadOutputLoop` / `UnixPtyOutputPump`) →
  `TerminalSession.OnProcessDataReceived` → `GhosttyTerminalSurface.Write` (thread-safe,
  **enqueue-only** into `_inbox`, 24 MiB cap + backpressure + CAN+ST drop-heal).
- Tick/render thread: `TerminalMod.OnAfterUi` → controller → `TerminalWindow.Render`/`DrainSessions`
  → `GhosttyTerminalSurface.BuildFrame()` — swaps the inbox and feeds the span to
  `_terminal.VTWrite` (the single P/Invoke into libghostty-vt). **All parsing happens on the
  render thread** — the same thread the game pumps FMOD on. No cross-thread marshalling needed
  anywhere in this plan.

### `OscSidecar` — the proven pre-scan tee

`purrTTY.Terminal\Ghostty\OscSidecar.cs`: a managed state machine
(`Ground → Esc → Osc → OscEsc`) fed **the exact same span** immediately before `VTWrite`
(`BuildFrame`, `_osc.Feed(span)`). Handles OSC 1 (icon) + OSC 52 (clipboard), 64 KB payload guard,
CAN/SUB aborts, vectorized ESC fast-skip so bulk text costs ~nothing. The engine independently
parses and discards OSCs it doesn't know — the tee sees them without any native hook. The retired
OSC 1010 JSON-RPC ran on exactly this mechanism (`docs\RPC_TERM_SEQUENCES.md`), so the lane is
proven and currently vacant.

### Bell — plumbed, unconsumed

The native binding registers an `OnBell` callback; `GhosttyTerminalSurface` latches
`_bellPending` and raises `ITerminalSurface.Bell` after `VTWrite` in `BuildFrame`. **No subscriber
exists anywhere.** This is both a free first feature (audible bell) and the reference
implementation for every new "out" event this plan adds (latch during parse → raise after
`VTWrite`).

### Kitty APC — engine-internal; unknown APCs are discarded

Kitty graphics (`ESC _ G … ST`) is parsed and stored entirely inside libghostty-vt; the checked-in
native patches (pin-leak fix, zlib route-around, APC bulk lane) add **no observability hook**. The
binding exposes no generic OSC/APC callback (ords 1–8 only: WritePty, Bell, Enquiry, Xtversion,
Title, Size, ColorScheme, DeviceAttributes). A non-`G` APC like `ESC _ A …` is consumed by the
native parser and **silently discarded** — which means a C#-side APC pre-scan sidecar can claim
`ESC _ A` without any risk of grid corruption and without a native patch.

### Event routing backend → GameMod

One seam: `ITerminalSurface` events (raised on the tick thread in `BuildFrame`) + `TerminalFrame`
data. Frontend attaches per-session handlers via `SessionManager.SessionConfigurator` →
`TerminalWindow.WireSession` (2D) / `InWorldTerminalRenderer.WireSession` (in-world). GameMod
(`TerminalMod`) owns the `GhosttyTerminalController` and today injects callbacks *into* it; no
per-surface event currently bubbles *up* to GameMod. The audio events add that last hop:
surface event → WireSession → controller-level event → `TerminalMod` subscription. FMOD-facing
code lives in **purrTTY.GameMod** (the layer that references KSA game DLLs); `purrTTY.Terminal`
stays renderer- and engine-neutral (events carry only ids + plain bytes).

---

## Feasibility verdict — GREEN

| Leg | Status |
|---|---|
| Intercept custom escape sequences in pure C# | ✅ `OscSidecar` pattern; engine discards unknown OSC/APC; no native rebuild |
| Carry events across the seam without engine types | ✅ Bell template; events are ids + `byte[]` + plain enums |
| Reach GameMod on the right thread | ✅ everything already runs on the render thread, where the game pumps FMOD |
| Play arbitrary in-memory bytes through the game mixer | ✅ `GameAudio.System.TryCreateSound(bytes, Mode.OpenMemory, …)` — recipe proven in engine code; volume sliders apply via `GetChannelGroup` |
| Client-side emitters (gatOS) | ✅ trivial — write escape bytes to stdout |

What we must build: an **APC audio sidecar** (state machine + chunk reassembly), the **event
plumbing** (one new surface event family + forwarding), a **`TerminalAudioService`** in GameMod
(clip store + FMOD calls + limits), the **bell sound**, and **client tooling/demos**.

---

## Protocol spec — purrTTY audio v1 (`ESC _ A`, draft-aligned)

Aligned with the kitty "Terminal Audio Protocol" draft (kovidgoyal/kitty discussion #9507 +
sopyb's spec gist): same envelope, same chunking discipline, same action verbs. purrTTY
extensions (container formats, channel-group routing) are additive keys. **Task before freezing:
re-read #9507 end-to-end and match key names where the draft is unambiguous** — divergences are
contained entirely inside the sidecar, so late renames are cheap.

### Envelope

```
ESC _ A <key=value,key=value,…> ; <base64 payload> ESC \
```

APC with discriminator `A` (kitty graphics uses `G`), terminated by ST (`ESC \`). CAN/SUB abort
mid-sequence. The native parser sees and discards these; the sidecar tees them.

### Chunking (kitty-graphics discipline, draft-refined)

- Base64 payload ≤ 4096 chars per APC message; larger clips split across messages.
- First chunk carries all control keys + `m=1`; continuation chunks carry only `m=1`; final chunk
  `m=0` (single-message transmit may omit `m`).
- Per the draft's streaming refinement, non-final chunks encode a multiple of 3 raw bytes so each
  chunk base64-decodes independently (no carry state in the decoder).
- No interleaving of different transmissions on one terminal.

### Actions and keys

| Action | Meaning | Keys |
|---|---|---|
| `a=t` | Transmit a clip (and optionally play it) | `i=` clip id (u32, client-chosen, required) · `f=` format (below) · `r=` sample rate, `c=` channels (pcm only) · `p=1` autoplay on completion · `l=1` loop · `v=` volume 0–100 (default 100) · `g=` group (`sfx`\|`music`\|`ui`, default `sfx`; purrTTY extension) · `m=` chunking |
| `a=p` | Control a stored clip | `i=` id (required) · `o=` operation (`play` default \| `pause` \| `resume` \| `stop`) · `l=`, `v=`, `g=` as above (apply on `play`) |
| `a=d` | Delete clip: stop channels, release FMOD `Sound`, free the id | `i=` id; **no `i=` = delete all clips of this terminal** (panic reset) |
| `a=q` | Capability query | terminal replies on the PTY: `ESC _ A a=q;OK,proto=1,fmt=pcm:wav:mp3:ogg:flac ESC \` |

Unknown keys are ignored (forward compatibility); malformed key strings or bad base64 abort the
transmission for that id.

### Formats (`f=`)

- `pcm` — raw s16le, requires `r=` (44100 or 48000) and `c=` (1|2). The draft's baseline;
  played via `Mode.OpenRaw` + `Format=PCM16`.
- `wav`, `mp3`, `ogg`, `flac` — complete container files, FMOD auto-detects
  (purrTTY extension; this is the practical path for gatOS sound effects and music).

### Detection

- `a=q` round-trip for spec-clean detection (reply goes out the existing `PtyReply` lane).
- **`PURRTTY_AUDIO=1`** env var injected into launched shells via the existing
  `WellKnownShellEnvironment` mechanism — the zero-round-trip path gatOS will actually use.

### Considered alternative: custom OSC number (rejected for transport, kept as fallback)

A custom OSC (e.g. the vacated 1010 lane) through the existing `OscSidecar` would be ~30 lines to
wire and is the right shape for *control* messages, but: OSC payloads are capped at 64 KB in the
sidecar, OSC is the wrong container for bulk binary by convention, and it would create a fourth
proprietary dialect precisely when the ecosystem is converging on `ESC _ A`. If the APC sidecar
turns out to be more trouble than expected, the fallback is the same protocol keys inside
`OSC 9800 ; <k=v…> ; <base64> ST` — nothing else in the plan changes.

---

## End-to-end data flow (target)

```
gatOS TUI / any CLI:  printf '\x1b_Aa=t,i=7,f=wav,p=1;<base64>\x1b\\'
        │  (PTY write path, unchanged)
GhosttyTerminalSurface.Write → _inbox                       [PTY read thread]
        │  (tick)
BuildFrame:  _apcAudio.Feed(span)  ──►  frames ESC _ A … ST, reassembles chunks,
             _osc.Feed(span)            validates keys, queues completed AudioCommand
             _terminal.VTWrite(span)    (native parser independently discards the APC)
             … after VTWrite: raise AudioRequested(AudioCommand) + Bell   [render thread]
        │
ITerminalSurface.AudioRequested  (renderer-neutral: ids, byte[], enums — no engine types)
        │
TerminalWindow.WireSession / InWorldTerminalRenderer.WireSession   [purrTTY.Display]
        │  forward with terminal name
GhosttyTerminalController.AudioRequested (terminalName, AudioCommand)
        │
TerminalMod (GameMod) → TerminalAudioService                       [purrTTY.GameMod]
   clip store: (terminal, id) → FMOD Sound     limits: size/count/channels
   GameAudio.System.TryCreateSound(bytes, OpenMemory[|OpenRaw], exInfo)
   GameAudio.System.TryPlaySound(sound, GetChannelGroup(g), paused:true) → set volume → unpause
        │
game mixer (Sfx/Music/Ui sliders apply) → speakers            [same render thread; game pumps System.Update()]
```

Seam property preserved: **no KSA/FMOD/ImGui type crosses `ITerminalSurface`** — the seam carries
plain command records; all FMOD types live in GameMod.

---

## Work breakdown by layer

### 1. Backend — `ApcAudioSidecar` (`purrTTY.Terminal/Ghostty/ApcAudioSidecar.cs`)

A sibling of `OscSidecar`, same architecture, framing `ESC _ A … ESC \` instead of OSC:

- States: `Ground → Esc → Apc(discriminator?) → ApcAudio(keys) → ApcAudioPayload → ApcEsc`.
  Non-`A` APCs (kitty `G`!) must be skipped cheaply — after the discriminator byte, if not `A`,
  fast-forward to ST without accumulating (kitty video pushes MiB/s through this path; reuse the
  vectorized `IndexOf(Esc)` skip idiom).
- Handles: split-across-`Feed`-calls state, CAN/SUB abort, the surface's CAN+ST drop-heal, key
  parsing (`a`,`i`,`f`,`r`,`c`,`p`,`l`,`v`,`g`,`m`,`o`), incremental base64 decode into a pooled
  buffer (chunks are independently decodable — no carry), per-clip reassembly keyed by `i=`.
- Limits enforced here (before bytes ever reach the frontend): max assembled clip
  (default 4 MiB), max concurrent reassembly 1 (protocol forbids interleave), oversize → abort +
  log via `purrTTY.Logging`.
- Output: queues completed `AudioCommand` records; `GhosttyTerminalSurface.BuildFrame` drains the
  queue **after** `VTWrite` and raises the new event (same deferred pattern as `_bellPending`).
- `a=q` replies write to the existing PTY-reply lane (`GhosttyTerminalSurface` already owns
  `PtyReply` emission for DA/DSR).

### 2. Seam — command types + surface event (`purrTTY.Terminal`)

```csharp
// purrTTY.Terminal/Audio/AudioCommand.cs  (renderer-neutral)
public enum AudioAction { Transmit, Play, Pause, Resume, Stop, Delete, DeleteAll }
public enum AudioFormat { Pcm, Wav, Mp3, Ogg, Flac }
public enum AudioGroup  { Sfx, Music, Ui }
public sealed record AudioCommand(
    AudioAction Action, uint ClipId, AudioFormat Format,
    byte[]? Data,                      // Transmit only — complete reassembled clip
    int SampleRate, int Channels,      // Pcm only
    bool Loop, float Volume,           // 0..1
    AudioGroup Group, bool Autoplay);
```

- `ITerminalSurface`: add `event Action<AudioCommand> AudioRequested;` (docs mirror `Bell`).
- `GhosttyTerminalSurface`: own the sidecar, wire feed + drain, re-raise.

### 3. Frontend forwarding (`purrTTY.Display`)

- `TerminalWindow.WireSession` and `InWorldTerminalRenderer.WireSession`: subscribe
  `AudioRequested`, forward as `(terminalName, command)` to a new
  `GhosttyTerminalController.AudioRequested` event (mirrors how `ClipboardRequested` is consumed,
  continued one hop up).
- Also forward `Bell` the same way (`BellRequested(terminalName)`).
- Terminal teardown: controller already knows when windows/in-world instances close — raise
  `TerminalClosed(terminalName)` (or reuse an existing close path if one exists at implementation
  time) so the audio service can release that terminal's clips.

### 4. GameMod — `TerminalAudioService` (`purrTTY.GameMod/Audio/TerminalAudioService.cs`)

The only FMOD-aware code. Constructed in `TerminalMod.InitializeTerminal`, subscribed to the
controller events, torn down in `Unload`.

- **Clip store:** `Dictionary<(string terminal, uint id), ClipEntry>` where `ClipEntry` holds the
  FMOD `Sound`, byte size, format, and live `Channel`s. On `Transmit`:
  `GameAudio.System.TryCreateSound(data, Mode.OpenMemory | Mode._2d | loopMode [| Mode.OpenRaw + exInfo.Format/NumChannels/DefaultFrequency for pcm], in exInfo, out sound)` —
  data is GC-free immediately after (OpenMemory copies). Replacing an existing id: stop + release
  old, create new.
- **Play:** `TryPlaySound(sound, GameAudio.GetChannelGroup(mapped group), paused:true, out ch)` →
  `ch.TrySetVolume(v)` → `ch.TrySetPaused(false)` (the game's own anti-pop idiom). Track channels
  per clip; `Pause`/`Resume`/`Stop` via `TrySetPaused`/`TryStop`. Poll `TryIsPlaying` during the
  per-frame tick (service gets a `Tick()` from `TerminalMod.OnAfterUi`) to prune finished channels
  and release `Sound`s pending deletion.
- **Master-slider parity:** route into the mapped category group (its slider applies); because the
  enum groups are siblings of Master, also multiply the game's master factor into channel volume
  (read `GameSettings.Current.Audio.MasterVolume` through the same mapping the game uses) — or
  accept the category slider only for v1 and note it. Decide at implementation; start simple.
- **Limits:** per terminal — max 16 stored clips / 16 MiB total (reject new transmits over cap,
  log), max 8 concurrent channels (stop oldest). Global kill switch + master volume multiplier in
  purrTTY's config (wherever global settings live — verify at implementation).
- **Lifecycle:** `TerminalClosed` → stop + release all that terminal's entries; `Unload` →
  release everything. Never touch `System.Update/Close/Release`.

### 5. Bell (`purrTTY.GameMod`, first shippable slice)

- Synthesize the beep once at init: ~120 ms 880 Hz sine with exponential decay, s16le 48 kHz mono,
  created via `OpenMemory | OpenRaw` (no asset file, no decoder) — or load a user-supplied WAV
  path from config if set.
- `BellRequested` → play into the `Ui` group, rate-limited (max ~4/s per terminal — BEL storms
  from `cat /dev/urandom` are real).
- Config: `BellEnabled` (default on), `BellVolume`.

### 6. Client tooling & demos

- `scripts/audio/purr-audio.ps1` + `purr-audio.sh`: encode a file → chunked `ESC _ A` emitter
  (`purr-audio play clip.mp3`, `purr-audio bell`, `purr-audio query`). Doubles as the manual test
  rig.
- gatOS integration (separate repo): a tiny emitter class in gatOS's TUI toolkit + `PURRTTY_AUDIO`
  detection. Out of scope here; the escape grammar above is the contract.

---

## Phasing

| Phase | Deliverable | Proves |
|---|---|---|
| **P1 — Bell** | BEL → audible beep via FMOD (synthesized, Ui group, rate-limited, config toggle). Wires the entire event chain end-to-end: surface event → WireSession → controller → GameMod → FMOD. | The full plumbing with a trivial payload; immediate daily-driver value |
| **P2 — Core protocol** | `ApcAudioSidecar` + `AudioCommand` seam + `TerminalAudioService`: `a=t` (wav/mp3/ogg/flac + pcm), chunk reassembly, `p=1` autoplay, `a=p o=play/stop`, `a=d`, limits, teardown. Demo scripts. | Arbitrary in-memory audio, escape stream → game mixer |
| **P3 — Controls & detection** | `l=`/`v=`/`g=` keys honored, pause/resume, `a=q` reply, `PURRTTY_AUDIO` env var, per-terminal delete-all, draft-#9507 key-name reconciliation pass. | Protocol completeness for gatOS |
| **P4 — Stretch (pick by demand)** | (a) **Spatial in-world audio**: clips from an in-world terminal play `Mode._3d` positioned at the quad (per-frame `Set3DAttributes` from the instance anchor — uniquely cool in KSA). (b) **Streaming PCM** for long/endless audio via `PcmReadCallback`/`OpenUser` ring buffer (the draft's `t=` transfer + jitter keys). (c) **DECPS / ANSI Music** retro synth (CSI pre-scan + note→PCM synthesis). | Differentiators |

Each phase is independently shippable; P1 alone is a visible feature.

---

## Limits & safety

- Assembled clip cap 4 MiB (config), per-terminal store 16 clips / 16 MiB, 8 concurrent channels,
  bell rate limit. Oversize/overflow → abort + one log line, never crash the parse.
- The 24 MiB surface inbox + backpressure already bound the transmit burst size; a 4 MiB clip is
  well inside what the kitty video path pushes per second today.
- Malformed base64 / unknown action → drop that transmission only; sidecar returns to `Ground`
  (CAN/SUB/ST all recover).
- Audio arrives from *anything* running in the shell — `cat`-ing a malicious file must at worst
  play noise. No paths, no file I/O, no shell-out anywhere in the pipeline; bytes go only to
  FMOD's decoder. FMOD codec parsing of hostile input is the residual risk — mitigated by the
  size cap and by formats being decode-only (same exposure class the kitty image decoder already
  accepted; noted in gotchas when implemented).
- Global `AudioEnabled` kill switch.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Draft #9507 changes under us | Divergence is contained in the sidecar's key parser; do the reconciliation pass in P3; version via `a=q` reply (`proto=1`) |
| ConPTY strips APC for plain local Windows shells | Same constraint kitty graphics already lives with in purrTTY — audio works wherever kitty graphics works today (gatOS lanes). Document; no new work |
| Double-parse cost (native walks the APC bytes too) | Engine discard path is cheap (bulk lane); clips are tiny vs. the video streams already flowing |
| FMOD decode failure on odd files | `TryCreateSound` returns `Result` — log + drop, reply error if `q` requests it |
| `Sound` released while playing (audible cut) | Deletion defers release until channels report not-playing (tick prune) |
| Master slider not applying (sibling groups) | Multiply master factor into channel volume, or accept category-only for v1 (documented) |
| Bell storms | Rate limit in P1 |

## Verification

- **purrTTY.Terminal.Tests** (quiet, no fixed sleeps, per docs/build-and-test.md):
  sidecar unit tests — framing (single/chunked/split-across-feeds), interleaved kitty `G` APC
  passthrough at bulk sizes, CAN/SUB/drop-heal recovery, bad base64, oversize abort, key parsing,
  independent chunk decode; surface-level: write escape bytes → `BuildFrame` → `AudioRequested`
  payload equality; `a=q` reply bytes on the `PtyReply` lane.
- **purrTTY.Display.Tests:** none required (pure forwarding); add only if forwarding grows logic.
- **GameMod (manual, in-game checklist):** P1 `printf '\a'` beeps; P2 `purr-audio play test.wav`
  from pwsh + GameConsoleShell + in-world terminal; volume sliders affect playback; limits kick
  in (transmit 5 MiB → rejected + logged); terminal close / mod unload silence everything;
  `/verify`-style session against a gatOS TUI once the client emitter exists.

## Touch list (primary files)

```
purrTTY.Terminal/Ghostty/ApcAudioSidecar.cs        NEW  — framing + reassembly + limits
purrTTY.Terminal/Audio/AudioCommand.cs             NEW  — seam types
purrTTY.Terminal/ITerminalSurface.cs               +AudioRequested event
purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs +sidecar feed/drain, event raise, a=q reply
purrTTY.Display/Ghostty/TerminalWindow.cs          +WireSession forwarding (audio, bell)
purrTTY.Display/Ghostty/InWorldTerminalRenderer.cs +WireSession forwarding
purrTTY.Display/Ghostty/GhosttyTerminalController.cs +AudioRequested/BellRequested/TerminalClosed
purrTTY.GameMod/Audio/TerminalAudioService.cs      NEW  — FMOD clip store + playback + limits
purrTTY.GameMod/TerminalMod.cs                     +service init/tick/teardown, config
purrTTY.Terminal.Tests/…                           NEW  — sidecar + surface tests
scripts/audio/purr-audio.{ps1,sh}                  NEW  — demo/test emitters
```

## Docs to update on implementation (CLAUDE.md mandate)

- `CLAUDE.md` — feature status + architecture note (audio events on the seam).
- `docs/code-navigation.md` — new files above.
- `docs/gotchas.md` — sibling-channel-group volume quirk; Sound-release-while-playing;
  APC-audio vs kitty-G coexistence in the sidecar; ConPTY APC caveat.
- `docs/how-to.md` — "play audio from a shell/TUI" recipe + protocol reference.
- `vendor/Ghostty.Vt/README.md` — **no changes** (no binding/native divergence — worth stating).
