# OS_ANALYSIS — Embedding a real, minimal operating system inside purrTTY

*Research date: 2026-06-11. All external claims carry source links; load-bearing ones were
independently re-verified. Companion to `LIBGHOSTTY_ANALYSIS.md` (the terminal-emulator seam this
builds on).*

---

## 0. TL;DR and recommendation

**Build it as a QEMU microVM subprocess running Alpine Linux (apk), with two host↔guest channels
over slirp user-mode networking: SSH for terminal sessions (dropbear in guest, SSH.NET on host)
and a C#-implemented 9P2000.L file server that the guest mounts at `/sim` for live vehicle
telemetry ("sensors as files").**

- Real kernel, real fork/exec, real package manager, real pipes/jobs/pagers/editors — the entire
  stated goal — with **zero custom guest binaries** for the MVP.
- Works on Windows (WHPX accel, TCG fallback) and Linux (KVM) hosts identically; macOS (HVF) free
  for development.
- Cold boot ~0.4–1 s accelerated, ~5–30 s under TCG; guest RAM 192–256 MB; ~+150–250 MB mod dist.
- Persistence = one qcow2 overlay per savegame on top of a pristine shipped base image.
- Effort: realistic for one person + AI agents. MVP ≈ 6–10 weeks part-time across 4 phases, with a
  **half-day de-risking spike** that validates the two novel pieces (9p-over-TCP synthetic files,
  SSH session into purrTTY tab) before committing.

Everything else researched — LKL, container2wasm, CheerpX, Blink, libriscv, WASIX, Unikraft, OSv,
UML, proot, WSL2 — fails at least one hard requirement (§5–§8, decision matrix §9). The single
honorable mention is **container2wasm + wasmtime-dotnet** (§4): the only true *in-process* option,
kept as a documented fallback if "no external process" ever becomes a hard constraint.

---

## 1. Requirements (restated as testable constraints)

| # | Requirement | Hard? |
|---|---|---|
| R1 | Real off-the-shelf package manager (apt/apk-class, real distro repos) | Hard |
| R2 | Real POSIX userland: shells, job control, pipes, pagers, editors — *unmodified binaries* | Hard |
| R3 | Minimal footprint: no general-purpose daemons, small RAM, fast startup | Hard |
| R4 | Host = Windows **and** Linux (game + mod run on both) | Hard |
| R5 | Game integration: vehicle data exposed as device/file-like streams into the OS | Hard (the point) |
| R6 | Persistence of OS state across game sessions | Hard |
| R7 | Networking via host stack, unprivileged, bridge-ish | Soft |
| R8 | Buildable/maintainable by one person + AI agents | Hard |
| R9 | Debian/apt ecosystem preferred | Preference |
| R10 | Multiple OS instances (or the believable fiction of them) | Soft |

**The litmus test that kills most candidates is R1+R2 together**: `apt`/`apk`, `dpkg` maintainer
scripts, shell pipelines, and job control all require *real* `fork()` + `execve()` + per-process
address spaces + TTYs + signals. Any architecture without a real MMU-backed process model cannot
run a package manager, full stop. That single fact sorts the whole landscape:

```
                       runs unmodified Linux userland incl. fork/exec?
                          ┌────────────yes────────────┐         no
                          │                           │          │
                 full-system virtualization    usermode emulators │
                 (QEMU/KVM/WHPX, UML, c2w)     (Blink, qemu-user) │
                          │                           │          │
                 cross-platform? QEMU yes      Windows? none     LKL, libriscv, WASIX,
                 UML no, c2w yes-but-slow                        Unikraft, OSv, fake-C#-unix
```

---

## 2. Why this is a virtualization problem

Three architecture classes were evaluated:

1. **Kernel-as-library (LKL)** — links the Linux kernel into your process and exposes *syscalls to
   the host app*. It runs **no guest processes at all**: `fork`/`vfork`/`clone` are stubbed to
   `sys_ni_syscall` in [arch/lkl/include/asm/syscalls.h](https://github.com/lkl/linux/blob/master/arch/lkl/include/asm/syscalls.h),
   and the maintainer explicitly redirected the "run sh/make" use case to UML/VMs
   ([lkl/linux#492](https://github.com/lkl/linux/issues/492)). Disqualified by design, not maturity. Details §5.
2. **Usermode emulation / syscall translation** (Blink, qemu-user, proot, UML, gVisor) — runs real
   binaries but every credible implementation is POSIX/Linux-host-only. Disqualified by R4. Details §6.
3. **Full-system virtualization/emulation** (QEMU; container2wasm as the wasm-embedded variant) —
   boots a real kernel, satisfies R1/R2 by construction. The question reduces to *how* to run QEMU
   well on both hosts, and how to integrate it. That is Option A.

---

## 3. Option A (recommended): QEMU microVM subprocess

### 3.1 Architecture overview

```
KSA game process (C#)                                    QEMU subprocess
┌──────────────────────────────────────────┐             ┌─────────────────────────────┐
│ purrTTY.Display / purrTTY.Terminal        │             │ Alpine Linux guest           │
│   TerminalWindow tabs ◄── ITerminalSurface│             │  ┌────────────────────────┐  │
│                                           │   slirp     │  │ dropbear sshd :22       │  │
│ VmSessionBackend (new IProcessManager) ───┼──tcp 127.0.0.1:<p> ─►(hostfwd)            │  │
│   SSH.NET ShellStream per tab             │             │  │ ash/bash, apk, vim, ...│  │
│                                           │             │  └────────────────────────┘  │
│ SimFs: C# 9P2000.L server :5640 ◄─────────┼── guest→10.0.2.2:5640 ── mount -t 9p /sim  │
│   ▲ snapshot store (lock-free swap)       │             │                              │
│   │ sampled per-tick on game thread       │             │  qcow2 overlay (per save)    │
│ TerminalMod OnBeforeGui/OnAfterUi         │             │  ── backing ──► base.qcow2   │
└──────────────────────────────────────────┘             └─────────────────────────────┘
```

- One VM per game session (lazy-booted at first terminal open), N terminal tabs into it.
- All host↔guest traffic is plain TCP through QEMU's user-mode networking — **deliberately no
  virtio-9p, no virtiofs, no vsock**, because none of those exist on Windows QEMU hosts (§3.4,
  [qemu#974](https://gitlab.com/qemu-project/qemu/-/issues/974),
  [virtiofsd is Linux-only](https://gitlab.com/virtio-fs/virtiofsd),
  [vsock requires host-Linux vhost](https://wiki.qemu.org/Features/VirtioVsock)). One transport
  that behaves identically on every host mirrors purrTTY's renderer-neutral-seam philosophy.

### 3.2 Host acceleration matrix

| Host | Accelerator | Status | Expected cold boot (direct kernel boot, Alpine) |
|---|---|---|---|
| Linux | KVM | First-class | **~0.4–1 s** (measured ~400 ms by [exec-sandbox](https://github.com/dualeai/exec-sandbox); ~245 ms kernel-to-userspace in [Garzarella's measurements](https://stefano-garzarella.github.io/posts/2019-08-24-qemu-linux-boot-time/)) |
| Windows | WHPX | Works incl. **Windows Home**; mid-tier maturity | ~1–3 s (no published numbers; estimate) |
| macOS (dev) | HVF | First-class | ~0.4–1 s |
| Any | TCG (pure emulation) | Universal fallback | ~5–30 s; shell stays interactive after boot |

Windows specifics (all from the [QEMU WHPX docs](https://www.qemu.org/docs/master/system/whpx.html)
and [exec-sandbox's Windows roadmap](https://github.com/dualeai/exec-sandbox/issues/10) unless noted):

- WHPX requires the **"Windows Hypervisor Platform"** optional feature
  (`DISM /online /Enable-Feature /FeatureName:HypervisorPlatform`), available on Home (the full
  Hyper-V role is Pro-only, but WHP is not the Hyper-V role). Needs a **reboot** → the mod cannot
  silently self-provision; first-run UX must detect, explain, and offer TCG meanwhile.
- WHPX coexists with VBS/Core Isolation (it runs *on top of* the Hyper-V hypervisor — same
  mechanism VMware uses).
- Known WHPX wart list: no nested virt, MMIO emulation bugs, AMD-specific crashes with exotic CPU
  flags. Mitigations: plain `-cpu host` (no feature-flag soup), `-M q35,pic=off`, optional
  `-accel whpx,ssd=off`. **Pin QEMU ≥ 11.0** — the 2026-04 release specifically improved WHPX
  (removed slow GVA translation, reworked REP handling,
  [release notes](https://www.qemu.org/2026/04/22/qemu-11-0-0/)).
- HAXM is dead (Intel discontinued 2023, removed from QEMU) — do not plan for it.
- TCG fallback: ~8× slower than accel for boot-class workloads
  ([benchmark](https://blog.fluxcoil.net/posts/2025/02/emulation-performance-and-consumption/));
  interactive shells/TUIs are fine (terminal I/O is cheap), big `apk add`s become minutes. This is
  an acceptable *degraded mode*, and [exec-sandbox](https://github.com/dualeai/exec-sandbox) ships
  exactly this ladder (KVM → HVF → TCG) in production.

Accel selection: try in order `kvm` → `whpx` → `hvf` → `tcg` (QEMU accepts a fallback list:
`-accel whpx -accel tcg`). Surface the active accel in the perf HUD (gotcha 15 pattern).

### 3.3 Machine type, boot, and resume

- **Machine: `q35` + virtio-pci on all hosts.** Not `microvm`: it is KVM/Linux-centric in
  practice, has zero documented WHPX usage, and the firmware it saves costs single-digit
  milliseconds anyway ([microvm docs](https://www.qemu.org/docs/master/system/i386/microvm.html),
  [exec-sandbox's identical conclusion](https://github.com/dualeai/exec-sandbox/issues/10)).
- **Direct kernel boot** — skip GRUB entirely: `-kernel vmlinuz-virt -initrd initramfs-virt
  -append "console=ttyS0 root=/dev/vda rw quiet"`. (Or build a trimmed kernel with virtio
  built-in and drop the initrd; start with Alpine's stock `linux-virt` artifacts.)
- Sketch invocation:

```
qemu-system-x86_64
  -accel kvm -accel whpx -accel hvf -accel tcg      # first available wins
  -M q35,pic=off  -cpu host  -m 256 -smp 2
  -kernel vmlinuz-virt -initrd initramfs-virt
  -append "console=ttyS0 root=/dev/vda rw quiet"
  -drive file=<saveDir>/os-disk.qcow2,if=virtio,format=qcow2
  -netdev user,id=n0,hostfwd=tcp:127.0.0.1:<port>-:22
  -device virtio-net-pci,netdev=n0
  -device virtio-serial-pci
  -chardev socket,id=qga0,host=127.0.0.1,port=<port2>,server=on,wait=off
  -device virtserialport,chardev=qga0,name=org.qemu.guest_agent.0
  -display none -serial null -monitor none
```

- **Instant resume (later optimization):** `savevm`/`loadvm` internal qcow2 snapshots restore in
  ~100 ms for small-RAM guests ([exec-sandbox](https://github.com/dualeai/exec-sandbox),
  [snapshot internals](https://airbus-seclab.github.io/qemu_blog/snapshot.html)). **Caveat:
  correctness under WHPX is unverified anywhere** — treat as a Phase-4 experiment, not a plan
  dependency. A <3 s cold boot may make it unnecessary.
- Shutdown: QGA `guest-shutdown` (or `ssh poweroff`), hard-kill after a bounded wait. The
  qemu-guest-agent ([docs](https://www.qemu.org/docs/master/interop/qemu-ga.html), one `apk add`)
  is worth shipping for exactly three things: clean shutdown, boot-readiness probing before sshd
  is up, and first-boot provisioning. Everything interactive goes over SSH, not QGA.

### 3.4 Networking: slirp user-mode net

Confirmed from the [QEMU networking docs](https://www.qemu.org/docs/master/system/devices/net.html):

- Fully **unprivileged** on every host — no TAP drivers, no admin, no firewall rules (bind
  hostfwd to 127.0.0.1). Satisfies R7 exactly: outbound TCP/UDP NATs through the host's normal
  socket API ("bridge to host stack" in effect).
- Guest reaches the **host loopback at `10.0.2.2`** — this is how the guest mounts the 9p server
  and could reach any future host-side services. Host reaches guest only via `hostfwd` (SSH).
- apk/apt mirrors work out of the box (it's just outbound TCP). ICMP/ping doesn't, harmless.
- Game-design lever: `-netdev user,restrict=on` blocks all guest traffic except explicit
  `hostfwd`/`guestfwd` — an "offline ship computer" mode, or a paranoia mode that limits the guest
  to the 9p+SSH channels only. Note the default (unrestricted) lets the guest connect to *any*
  host-loopback service via 10.0.2.2; `restrict=on` + `guestfwd` for the 9p port is the
  tighter posture if that ever matters.

### 3.5 Terminal sessions: dropbear + SSH.NET (and why not serial consoles)

**The naive plan (virtio-serial consoles + getty) has a fatal UX flaw: resize.** Serial/virtconsole
has no window-size channel — the guest TTY never gets `TIOCSWINSZ`, so every full-screen TUI
renders at 80×24 regardless of the purrTTY window. The virtio spec has `VIRTIO_CONSOLE_F_SIZE` and
the Linux *driver* implements it, but **QEMU's device side never merged it** (patch series out of
tree since 2020, [v4 still being rebased 2025](https://www.mail-archive.com/qemu-devel@nongnu.org/msg1137505.html);
verified absent from `hw/char/virtio-console.c` master). All workarounds (`stty rows/cols`
injection, xterm `resize(1)`) are racy warts. Keep **one** virtconsole as an emergency/boot
console; do not build sessions on it.

**SSH gives resize + unlimited concurrent sessions for free:**

- Guest: [dropbear](https://pkgs.alpinelinux.org/package/edge/main/x86_64/dropbear) — 474 KiB
  installed, ~1–3 MB per connection, zero config (`dropbear -R -s -E -F`), key-only auth with
  `authorized_keys` baked into the base image or injected at first boot.
- Host: **SSH.NET (Renci.SshNet) ≥ 2025.1.0** — pure managed, and the decade-missing resize API
  finally shipped: `SshClient.CreateShellStream(term, cols, rows, w, h, bufsize)` +
  **`ShellStream.ChangeWindowSize(cols, rows, w, h)`**
  ([release 2025.1.0](https://github.com/sshnet/SSH.NET/releases/tag/2025.1.0), PR #1646 — verified
  on the release page). Fallbacks exist if it disappoints:
  [Tmds.Ssh](https://github.com/tmds/Tmds.Ssh) (`RemoteProcess.SetTerminalSize`), libssh2 P/Invoke,
  or spawning `ssh.exe` under the existing ConPTY backend.
- Host-key handling: pre-generate an ed25519 host key per savegame (or accept-and-pin on first
  connect via `HostKeyReceived`) — it's a loopback VM, not the open internet.

**purrTTY seam fit (this is the elegant part):** a guest session is just a new
`IProcessManager`-shaped backend — call it `VmSessionManager` — next to `ProcessManager` (ConPTY)
and `UnixProcessManager`:

| `IProcessManager` concept | SSH mapping |
|---|---|
| `StartAsync(options, cols, rows)` | ensure VM booted → `CreateShellStream(term, cols, rows, …)` |
| `Write(bytes)` | `ShellStream.Write` (keep the bounded-queue pattern of gotcha 20) |
| `DataReceived` | `ShellStream` reads → `Surface.Write` |
| `Resize(cols, rows)` | `ChangeWindowSize(cols, rows, …)` |
| exit/teardown | channel close events; VM keeps running for other tabs |

Nothing above `purrTTY.Terminal` changes: `TerminalWindow` tabs, theming, selection, the whole
frontend works unmodified. A "Spaceship OS" entry appears in the New Tab / New Window shell menus
alongside PowerShell/zsh/Game Console (`ShellMenuCache` already supports per-shell entries; VM
readiness is async, which matches the existing background-detection pattern).

### 3.6 The sensor filesystem: `/sim` over 9p — "sensors as files", literally

This is the user-facing soul of the project, and it needs **zero custom guest software**:

```
# guest side — that's the whole integration:
modprobe 9p
mount -t 9p -o trans=tcp,port=5640,version=9p2000.L,cache=none 10.0.2.2 /sim
```

The Linux kernel's 9p client speaks plain TCP (`trans=tcp`) to any 9P2000.L server — QEMU is not
involved in the transport at all ([kernel v9fs docs](https://docs.kernel.org/filesystems/9p.html);
[diod](https://github.com/chaos/diod) documents exactly this mount shape). The game implements the
server in C#. Verified specifics:

- **`cache=none` is the default and correct mode** — every guest `read()` becomes a `Tread` to the
  C# server, so data is always live (procfs semantics).
- **Blocking/streaming reads are supported**: the client waits without timeout
  (`io_wait_event_killable`), so a `Tread` against `/sim/.../stream` can stall until the next sim
  tick and then deliver — `tail -f`-style telemetry streams work. The server **must** answer
  `Tflush` promptly (that's how Ctrl-C on a blocked `cat` works,
  [flush(5)](https://9p.io/magic/man2html/5/flush)).
- **Kernel-version landmine (the one real gotcha):** Linux 6.8–6.10 had a netfslib regression that
  made size-0 synthetic files read empty; fixed in 6.11
  ([commit e3786b29c54c](https://github.com/torvalds/linux/commit/e3786b29c54cdae3490b07180a54e2461f42144c)).
  **Guest kernel must be ≥ 6.11** (Alpine 3.22+ `linux-virt` is 6.12 — fine). Belt-and-braces:
  report a generous fake size (4096) in `Tgetattr` instead of 0.
- Alpine `linux-virt` ships `9pnet.ko`/`9pnet_fd.ko`/`9p.ko` — confirmed
  ([package contents](https://pkgs.alpinelinux.org/contents?file=9pnet*&pkgname=linux-virt&branch=v3.22&arch=x86_64)).
  (Debian note: the *cloud* kernel disables `CONFIG_NET_9P`; use `linux-image-amd64` if Debian.)
- No reconnect in the kernel's TCP transport — a dropped connection kills the mount. Mitigation: a
  3-line guest inittab supervisor that remounts on failure; in practice the server lives exactly as
  long as the VM.

**No usable .NET 9p server exists** (exhaustive sweep: Sharp9P is a dead 2016 9P2000 codec;
NinePSharp v0.1.0 has no public source; nothing else) — **so we write one**, and that's genuinely
fine: a read-mostly 9P2000.L server is **~11 message handlers** over a trivial little-endian
framing (`Tversion Tattach Twalk Tlopen Tread Tgetattr Treaddir Tclunk Tflush Tstatfs` + optional
`Twrite`; everything else `Rlerror(EOPNOTSUPP)`). The wire format is `BinaryPrimitives`/
`Span<byte>` territory — squarely within this repo's demonstrated competence (see
`RenderState.FrameReader`). Reference: [diod's protocol.md](https://github.com/chaos/diod/blob/master/protocol.md)
is *the* 9P2000.L spec; [hugelgupf/p9](https://github.com/hugelgupf/p9) (Go, extracted from gVisor)
is the cleanest implementation to crib from. **Estimate: 3–8 days including conformance testing
against a live mount.**

**Proposed `/sim` namespace** (grounded in what the KSA sweep confirmed reachable, §10):

```
/sim/
├── time/ut                      # seconds, Universe.GetElapsedSimTime()
├── time/warp                    # SimulationSpeed
├── vessels/
│   ├── active -> by-id/<id>     # Program.ControlledVehicle
│   └── by-id/<vehicleId>/
│       ├── name  situation      # "Freefall" | "Landed" | ...
│       ├── position/{cci,lat,lon}
│       ├── velocity/{orbital,surface,inertial}      # GetSurfaceSpeed() etc.
│       ├── attitude/{quat,rates}                    # Body2Cci, BodyRates
│       ├── altitude/{barometric,radar}
│       ├── mass/{total,dry,propellant}
│       ├── orbit/{apoapsis,periapsis,ecc,inc,sma,period,...}   # OrbitData
│       ├── resources/{tanks/...,battery/charge}
│       ├── engines/<n>/{active,vac_thrust,isp}
│       └── stream                # blocking read: one NDJSON line per sim tick (rate-limited)
└── events                        # blocking read: SoI change, liftoff, landing, ... (diffed)
```

With that mounted, the *entire* unix toolbox becomes the game API surface — exactly the stated
goal: `watch -n1 cat altitude/radar`, `tail -f stream | jq .vel.surface`, awk pipelines, a
five-line shell script as a landing alarm. Writable control files (`echo 1 > engines/0/active` →
`EngineController.SetIsActive`, marshaled to the game thread) are a natural later phase — gate it
as a gameplay-design decision, not a technical one.

**Concurrency design:** sample telemetry **on the game thread** (`OnBeforeGui` — the KSA sweep
confirms vehicle state is main-thread; §10) into an immutable snapshot object, swap a reference;
9p server threads only ever read the latest snapshot, blocked stream-readers wake on swap. No
locks across the seam, same discipline as gotcha 1.

**Fallback** if the v9fs spike sours: a tiny guest FUSE daemon (C/musl via `zig cc`, the toolchain
already in this repo's build notes, or static Go) proxying the same host protocol — strictly more
moving parts (custom guest binary + lifecycle + reconnect), so 9p stays Plan A.

### 3.7 Persistence: qcow2 overlays, savegame-shaped

- Ship a pristine, compressed, **immutable** `base.qcow2` with the mod
  (`qemu-img convert -c -o compression_type=zstd`); each savegame gets
  `qemu-img create -f qcow2 -b base.qcow2 -F qcow2 save-<id>.qcow2` (`-F` is mandatory since QEMU
  6.1). Store a **bare relative backing filename** with overlay and base in the same directory —
  OS-neutral, survives moving the folder ([backing chains](https://libvirt.org/kbase/backing_chains.html)).
- Savegame semantics fall out for free: copy the overlay = branch the OS state with the save;
  delete = reset; `qemu-img rebase` migrates overlays onto a new shipped base after mod updates.
- Gotchas: never `qemu-img commit` (corrupts sibling saves' shared base); **QEMU image locking is
  not implemented on Windows** ([block drivers docs](https://www.qemu.org/docs/master/system/qemu-block-drivers.html))
  so the mod enforces one-VM-per-overlay itself (it already owns the VM lifecycle).
- Rejected alternative: tmpfs root + persist `/home` over 9p — that makes the C# 9p server
  load-bearing for *durability* (write path, fsync, crash consistency) instead of read-mostly
  telemetry. Overlays are simpler and savegame-shaped.

### 3.8 Guest image: distro choice and build pipeline

| Distro | Pkg mgr | Idle RAM | Disk | Verdict |
|---|---|---|---|---|
| **Alpine (virt kernel)** | apk | boots at 128 MB, comfy at 192–256 ([wiki](https://wiki.alpinelinux.org/wiki/Requirements)) | ~130 MB installed | **MVP choice.** Kernel 6.12 (9p-safe), 9p modules shipped, huge package repo, musl-tiny |
| **Debian minbase** (mmdebstrap, no systemd, busybox/runit init) | **apt** (R9!) | ~128–256 MB | 27 MB tarball w/ apt ([mmdebstrap(1)](https://manpages.debian.org/testing/mmdebstrap/mmdebstrap.1.en.html)), ~150 MB unpacked | **Phase-4 variant.** Fully viable; needs non-cloud kernel for 9p; apt is RAM-hungrier |
| OpenWrt x86-64 | apk (since 25.12, [replaced opkg](https://linuxiac.com/openwrt-25-12-released-with-apk-package-manager-replacing-opkg/)) | ~39–70 MB | ~16–128 MB | Tempting "IoT flavor", but router userland + tiny package universe — wrong toolbox |
| Tiny Core | tcz | 46 MB | ~28 MB | Own package format, bolt-on persistence — fails R1 spirit |
| Void (musl) | xbps | — | few hundred MB | No advantage over Alpine here |

On R9 (apt preference) honestly: **apt's ecosystem advantage doesn't bite at this scale** — for
"pagers, editors, tmux, jq, htop"-class tooling, Alpine's repos are complete, and apk is
dramatically lighter (smaller indexes, faster installs, less RAM). Recommendation: **Alpine for
MVP, keep the image-build script distro-parameterized**, and offer a Debian-minbase image as a
variant once the plumbing is proven — nothing in the architecture is Alpine-specific.

Image build: a script (dev machine or CI, can run in Docker on the Linux CI runner) using
[alpine-make-vm-image](https://github.com/alpinelinux/alpine-make-vm-image) or plain
apk-tools-static chroot: install `dropbear openssh-sftp-server qemu-guest-agent`, bake
`authorized_keys` + inittab (getty on emergency console, dropbear, 9p remount supervisor), strip
docs. Output: `base.qcow2` + `vmlinuz-virt` + `initramfs-virt`, checked into the mod dist like the
native libghostty blobs are today (same vendoring pattern, `vendor/guest-os/` or similar).

### 3.9 Multiple "computers" (R10)

A second full VM costs ~150–450 MB host RSS + a TCG penalty when unaccelerated — affordable
occasionally, wasteful as the default fiction. Better ladder:

1. **One VM, many sessions** — purrTTY tabs/windows already model this.
2. **One VM, many "computers"**: per-vessel users/chroots, or busybox `unshare` containers — to
   the player each looks like a separate machine (hostname, prompt, filesystem), at ~zero
   marginal cost. The `/sim` tree is already namespaced per vehicle.
3. **N real VMs** reserved for special cases (a deliberately air-gapped "ground station").

### 3.10 Packaging, size, licensing

- Windows QEMU: actual load-time closure is ~97 files / ~87 MB out of the 177 MB Weil installer
  ([measured](https://www.itayemi.com/blog/2024/02/21/compiling-qemu-for-windows/)); plus a few MB
  of BIOS blobs (skip the 60 MB EDK2 UEFI — direct kernel boot). Linux: prefer system qemu if
  present, bundle a fallback. **Realistic dist growth: +150–250 MB uncompressed, much less
  zipped.** A `--without-default-features` custom build can shrink further later.
- **GPLv2 compliance is the textbook easy case**: QEMU runs as a separate process communicating
  over argv/sockets — "mere aggregation" per the
  [GPL FAQ](https://www.gnu.org/licenses/gpl-faq.html#MereAggregation); the mod's license is
  unaffected. Obligations: ship GPLv2 text + QEMU notices (extend `THIRD-PARTY-NOTICES.md`, same
  as the Ghostty.Vt precedent) and provide **corresponding source for the exact binaries** —
  mirror the source tarball + build scripts in a repo/release you control
  ([SFLC guide](https://softwarefreedom.org/resources/2008/compliance-guide.html)). Same for the
  guest image's GPL components (kernel, busybox): mirror Alpine's sources for the pinned versions.
- Host RAM budget: guest 256 MB + QEMU overhead ≈ **~400–450 MB host RSS**
  ([overhead measurements](https://rwmj.wordpress.com/2013/02/13/what-is-the-overhead-of-qemukvm/))
  — acceptable next to a multi-GB game, and the VM only exists once a terminal is opened.

### 3.11 Risks (Option A)

| Risk | Severity | Mitigation |
|---|---|---|
| WHPX maturity/bugs (AMD crashes, MMIO) | Medium | QEMU ≥ 11.0, `-cpu host`, `q35,pic=off`, TCG fallback; validate on real Windows early (Phase 1 exit criterion) |
| WHP feature not enabled on player's Windows | Medium (UX) | Detect → explain → offer one-click DISM elevation or TCG meanwhile; TCG is *playable* for shell work |
| TCG too slow for heavy package ops | Low-Med | Expectation-setting in UI; preinstall the common toolbox in base image |
| v9fs synthetic-file edge cases on exact guest kernel | Low | **Half-day spike first**; fake-size getattr; FUSE fallback designed |
| savevm/loadvm under WHPX unverified | Low (optional feature) | Treat resume-from-snapshot as Phase-4 experiment; cold boot is fast enough |
| SSH.NET 2025.1.0 recency (`ChangeWindowSize`) | Low | Verified shipped; Tmds.Ssh / ssh.exe-under-ConPTY as drop-ins |
| Dist size +~200 MB | Low | Zip compression; optional download-on-first-use later |
| Anti-cheat / hypervisor interactions | Low (KSA is offline/single-player modding scene) | None needed today; note for future |

---

## 4. Option B: container2wasm + wasmtime-dotnet (the in-process alternative)

[container2wasm](https://github.com/ktock/container2wasm) converts a container image into one WASM
module that boots a **real Linux kernel on an emulated CPU** (Bochs-in-wasm for x86_64, a TinyEMU
fork for riscv64). It runs under **wasmtime**, and
[wasmtime-dotnet](https://github.com/bytecodealliance/wasmtime-dotnet) is actively maintained
(v44, May 2026) — so this genuinely runs **inside the game process**, pure cross-platform, no
subprocess, no shipped QEMU, stdio mapping naturally onto `ITerminalSurface`.

- **Pros:** only true in-process option; real kernel → real apk/apt/fork; no host accel
  dependencies, identical everywhere; Apache-2.0 converter (guest bundles GPL bits — shippable
  with notices); VM state could even live inside the .NET heap world.
- **Cons (disqualifying as primary):** the CPU is an **interpreter in wasm** — a reported ~16 s
  for a node hello-world on riscv64/alpine ([issue #75](https://github.com/container2wasm/container2wasm/issues/75));
  package operations would be minutes-class, always, on every host — i.e., it performs like
  Option A's *worst case* (TCG) as its *best case*. Plus: experimental project label, custom
  networking shim required (`c2w-net`), tens-to-hundreds of MB per image, and a wasm runtime
  pinned inside a game process.
- **Verdict:** documented fallback. Revisit only if "no external process" becomes a hard
  constraint (e.g., a platform that forbids spawning executables), or if c2w gains a JIT-class
  CPU. The host-side integration code (SSH/9p don't apply here, but the snapshot store and
  session seam do) largely survives a swap.

## 5. Option C: LKL — disqualified by design (and what it's still good for)

The full analysis (sources in §0 of the research): LKL compiles Linux as a library and exposes
**syscalls to the host application only**. It has no MMU-backed process model:
`fork`/`vfork`/`clone` are `sys_ni_syscall`
([syscalls.h](https://github.com/lkl/linux/blob/master/arch/lkl/include/asm/syscalls.h)); "tasks"
are host threads sharing one address space; there is no ELF loader. The maintainer explicitly
declined the use case: *"This is not a use-case that we plan to support with LKL. UML or VMs are
better suited"* ([#492](https://github.com/lkl/linux/issues/492)). Projects that bolted on a
loader (SGX-LKL, μKontainer) got *one* musl-linked process, no fork
([sgx-lkl#78](https://github.com/lsds/sgx-lkl/issues/78)), and are dormant. The 2025 MMU merge
([#551](https://github.com/lkl/linux/pull/551)) exists for Android-Binder fuzzing, not processes.
Separately, **Windows in-process embedding is blocked anyway**: Linux can't build LLP64, so
there's no native win-x64 `liblkl.dll` — only MSYS2-runtime or 32-bit builds
([lkl.txt](https://github.com/lkl/linux/blob/master/Documentation/lkl/lkl.txt)).

**Residual value to purrTTY:** none on the critical path. (Niche: `cptofs`-style ext4 image
manipulation from the host on Linux/macOS for image tooling — but qemu-img + guest-side tools
cover this.)

## 6. Option D: usermode emulators & per-OS natives — all fail R4 (Windows)

- **Blink** ([jart/blink](https://github.com/jart/blink)): gorgeous 221 KB x86-64-linux usermode
  emulator, real fork/exec, runs distro chroots — but Windows is Cygwin-only **with JIT disabled**
  ([#27](https://github.com/jart/blink/issues/27)). Best Linux/macOS-only fallback; fails R4.
- **User-Mode Linux**: a Linux process by construction ([docs](https://docs.kernel.org/virt/uml/user_mode_linux_howto_v2.html)). Fails R4.
- **proot/gVisor/qemu-user**: Linux-only mechanisms (ptrace/KVM). Fail R4.
- **WSL2 as the Windows half + native chroot as the Linux half**: heavyweight admin-gated install,
  two completely different codepaths and failure modes, no uniform device story. Rejected on R8
  as much as anything. (Could become an optional Windows *fast path* far in the future.)
- **Cygwin/MSYS2**: not Linux-binary-compatible at all — its own recompiled POSIX-ish world. Fails R1/R2.
- **Docker/Podman**: on Windows = WSL2/Hyper-V dependency; a game mod can't assume it. Rejected.

## 7. Option E: unikernels & wasm userlands — all fail the fork/apt test

- **Unikraft + app-elfloader**: runs unmodified ELFs, and v0.19 added `posix_spawn`/`vfork`/
  `execve` — but **plain `fork()` remains unsupported** (single address space,
  [multiprocess blog](https://unikraft.org/blog/2025-05-15-multiprocess)) → dpkg/apt break; and
  supported hosts are KVM/Firecracker — **Linux-only in practice**
  ([docs](https://unikraft.org/docs/concepts/compatibility)). Spectacular boot/RAM numbers, wrong
  platform matrix and wrong process model.
- **OSv**: single address space, no fork/exec by design ([LWN](https://lwn.net/Articles/610004/)). apt impossible.
- **WASIX/Wasmer**: bash/coreutils exist *recompiled to wasm*; the "package ecosystem" is the
  Wasmer registry — **no apt/apk, no unmodified binaries ever** ([docs](https://wasix.org/docs));
  no current .NET binding reaches WASIX. Fails R1/R2.
- **CheerpX/WebVM**: technically the most impressive "apt in wasm" (x86 JIT), but proprietary
  (redistribution = paid OEM license, [licensing](https://cheerpx.io/docs/licensing)) and
  browser-coupled (SharedArrayBuffer/COI; no standalone runtime). Fails R4-as-embedded + R8.
- **v86**: 32-bit only, inseparable from a JS host. Fails embedding shape.

## 8. Option F: fake it in C# (baseline strawman)

A synthetic VFS + busybox-ish utilities implemented managed-side (the `GameConsoleShell` lineage,
scaled up). Zero footprint, perfect integration, total control — and a treadmill that never ends:
every pipe, pager, editor, job-control corner, and "can I apt install X?" becomes *your* code.
It fails R1/R2 *by definition* and would consume the entire passion-project budget reimplementing
1% of what Alpine ships. Worth naming only to reject explicitly: **the whole point of R1/R2 is to
stop writing terminal userland by hand** — purrTTY already learned this lesson once by deleting
its bespoke VT emulator for libghostty-vt.

---

## 9. Decision matrix

| Option | R1 real pkgs | R2 real userland | R3 footprint | R4 Win+Linux | R5 device integration | R6 persist | R8 one-person scope | Verdict |
|---|---|---|---|---|---|---|---|---|
| **A. QEMU subprocess** | ✅ | ✅ | ✅ 256 MB / ~1 s accel | ✅ KVM/WHPX/TCG | ✅ 9p `/sim` + SSH | ✅ qcow2 | ✅ all known tech | **Build this** |
| B. container2wasm in-proc | ✅ | ✅ | ⚠️ interpreter-slow always | ✅ | ⚠️ custom shims | ⚠️ | ⚠️ experimental deps | Fallback |
| C. LKL | ❌ no processes | ❌ | ✅ | ❌ no win64 lib | — | — | — | Rejected |
| D. Blink/UML/proot/WSL2 | ✅ | ✅ | ✅ | ❌ | — | — | ⚠️ dual stacks | Rejected |
| E. Unikraft/OSv/WASIX/CheerpX | ❌ no fork / no apt | ⚠️ | ✅ | ❌/⚠️ | — | — | — | Rejected |
| F. Fake C# unix | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ infinite treadmill | Rejected |

---

## 10. KSA integration: what telemetry is actually reachable (verified against decompiled sources)

The decompiled-source sweep confirms everything the `/sim` tree needs is reachable from mod code
today, on the established purrTTY hook pattern (`[StarMapMod]`, `OnBeforeGui`/`OnAfterUi`,
Harmony where needed):

- **Enumeration:** `Program.ControlledVehicle` (active vessel);
  `Universe.CurrentSystem?.All.OfType<Vehicle>()` (all vessels). Stable `vehicle.Id` strings.
- **Per-frame state on `Vehicle`** (`thirdparty/ksa/KSA/Vehicle.cs`): `GetPositionCci()`,
  `GetVelocityCci()`, `GetBody2Cci()`, `BodyRates`, `AccelerationBody`, `TotalMass`/`InertMass`/
  `PropellantMass`, `GetBarometricAltitude()`/`GetRadarAltitude()`,
  `GetSurfaceSpeed()`/`GetInertialSpeed()`, `Situation` (Landed/Freefall/…), `NavBallData`
  (TWR, speed, altitude pre-computed).
- **Orbit** (`Orbit`/`OrbitData`): apoapsis/periapsis (radii, not altitudes — convert),
  eccentricity, inclination, LAN, AoP, SMA, period, time-at-periapsis.
- **Resources:** `vehicle.Parts.Modules.Get<Tank>()` / `Get<Battery>()` /
  `Get<EngineController>()`; mutable state via the `ModuleStateful` struct-of-arrays pattern.
- **Environment:** parent body id/radius/mass via `vehicle.Parent`; atmosphere pressure/density
  at altitude via `AtmosphereReference`.
- **Time:** `Universe.GetElapsedSimTime()`, `SimulationSpeed` (warp).
- **Threading rules:** read vehicle state on the **game thread** (`OnBeforeGui`); mutations that
  solvers depend on (battery writes etc.) belong in a `Priority.First` prefix on
  `Universe.ExecuteNextVehicleSolvers`; cross-thread work marshals via `GameThread.Scheduler`.
  → The sampler runs in `OnBeforeGui`, publishes an immutable snapshot; 9p server threads consume
  snapshots only. Control-file writes (later phase) marshal back via `GameThread.Scheduler`.
- **Events** (diff snapshots): SoI change, liftoff, landing/splashdown, atmosphere entry/exit,
  stable orbit, escape — feeding `/sim/events`.
- **Not reachable** (set expectations): per-nozzle live thrust, raw pilot stick input, internal
  aero forces — solver-internal. The `/sim` tree above deliberately contains none of these.

And the rendering end of the user's vision — purrTTY frames on cockpit screens / helmet HUDs — is
exactly what the renderer-neutral seam was built for: `TerminalFrame` is already
presentation-agnostic; a 3D-surface frontend is a frontend swap, orthogonal to everything in this
document.

---

## 11. MVP roadmap (one person + AI agents)

**Phase 0 — De-risking spike (~2–4 days).** Hand-build an Alpine qcow2 on the dev machine; launch
QEMU by hand (HVF); ① throwaway 20-line 9p server: `cat` a synthetic file, `tail` a blocking
stream, Ctrl-C a blocked read (Tflush), drop the TCP connection; ② SSH.NET shell into a purrTTY
tab with live resize. *Exit criterion: both channels demonstrably work end-to-end. This spike
validates every novel assumption in the plan for half a week of effort.*

**Phase 1 — VM lifecycle + sessions (~2–3 weeks).** `purrTTY.Vm` project: QEMU process manager
(accel ladder, port allocation, readiness probe, clean shutdown, crash surfacing),
`VmSessionManager : IProcessManager`-shaped backend over SSH.NET, "Spaceship OS" in the shell
menus, scripted image build, **Windows validation pass** (WHPX + TCG, WHP-missing UX). *Exit:
open a tab into the VM on Windows and Linux; `apk add htop` works; resize works.*

**Phase 2 — `/sim` sensor filesystem (~1–2 weeks).** C# 9P2000.L server (~11 handlers);
game-thread telemetry sampler + snapshot store; read-only tree + `stream`/`events` blocking
files; conformance tests in `purrTTY.Terminal.Tests` style (server is engine-free, testable
headless). *Exit: `watch cat /sim/vessels/active/altitude/radar` live in-game.*

**Phase 3 — Persistence + savegame integration (~1 week).** Overlay-per-save creation/copy/delete
wired to the save lifecycle hooks; base-image versioning + `qemu-img rebase` migration path;
single-writer enforcement.

**Phase 4 — Polish & options (~2+ weeks, à la carte).** Packaging/licensing (QEMU vendoring,
source mirrors, THIRD-PARTY-NOTICES); perf HUD additions (accel mode, VM RSS); writable control
files (gameplay-gated); Debian-minbase image variant (R9); savevm/loadvm instant-resume
experiment (WHPX validation); multi-"computer" fiction via guest containers; optional
download-on-first-use for the QEMU bundle.

**Total to a demoable MVP (end of Phase 2): ~5–7 weeks part-time.** Worst-case schedule risk is
Windows/WHPX quirk-chasing in Phase 1 — time-box it and lean on TCG, which is guaranteed to work.

---

## 12. Open questions to settle before Phase 1

1. **Alpine-first confirmed?** (R9 says apt-preferred; §3.8 argues Alpine-MVP + Debian-variant
   later. Decision needed before the image script is written — it's parameterized either way.)
2. **VM-per-game-session vs VM-per-savegame-world** — when does the VM boot/die relative to
   loading a save? (Recommendation: boot lazily on first terminal open, overlay selected by
   active save; shut down on save unload.)
3. **Guest network posture default**: open NAT (real apk mirrors, real internet from the in-game
   computer — fun, but immersion-breaking?) vs `restrict=on` + a curated package mirror. Could be
   a mod setting.
4. **Write access to the sim** (control files): pure-sensor MVP first; decide the gameplay rules
   for actuation separately.
5. **QEMU bundling vs download-on-first-use** — dist-size tolerance for the release zip.

---

## Appendix: primary sources (load-bearing subset)

- LKL: [lkl.txt](https://github.com/lkl/linux/blob/master/Documentation/lkl/lkl.txt) ·
  [no-fork stubs](https://github.com/lkl/linux/blob/master/arch/lkl/include/asm/syscalls.h) ·
  [maintainer on processes #492](https://github.com/lkl/linux/issues/492) ·
  [MMU-for-fuzzing #551](https://github.com/lkl/linux/pull/551) ·
  [SGX-LKL no-fork #78](https://github.com/lsds/sgx-lkl/issues/78)
- QEMU: [WHPX docs](https://www.qemu.org/docs/master/system/whpx.html) ·
  [QEMU 11.0 release](https://www.qemu.org/2026/04/22/qemu-11-0-0/) ·
  [exec-sandbox (existence proof + Windows roadmap)](https://github.com/dualeai/exec-sandbox) ·
  [boot-time measurements](https://stefano-garzarella.github.io/posts/2019-08-24-qemu-linux-boot-time/) ·
  [microvm docs](https://www.qemu.org/docs/master/system/i386/microvm.html) ·
  [slirp/user-net docs](https://www.qemu.org/docs/master/system/devices/net.html) ·
  [no 9p on Windows hosts #974](https://gitlab.com/qemu-project/qemu/-/issues/974) ·
  [vsock = Linux hosts](https://wiki.qemu.org/Features/VirtioVsock) ·
  [TCG vs accel benchmark](https://blog.fluxcoil.net/posts/2025/02/emulation-performance-and-consumption/) ·
  [Windows QEMU trimming](https://www.itayemi.com/blog/2024/02/21/compiling-qemu-for-windows/) ·
  [GPL mere aggregation](https://www.gnu.org/licenses/gpl-faq.html#MereAggregation)
- 9p: [kernel v9fs docs](https://docs.kernel.org/filesystems/9p.html) ·
  [diod protocol.md (9P2000.L spec)](https://github.com/chaos/diod/blob/master/protocol.md) ·
  [6.8–6.10 DIO regression fix](https://github.com/torvalds/linux/commit/e3786b29c54cdae3490b07180a54e2461f42144c) ·
  [flush(5)](https://9p.io/magic/man2html/5/flush) · [hugelgupf/p9](https://github.com/hugelgupf/p9)
- Terminals: [SSH.NET 2025.1.0 (ChangeWindowSize)](https://github.com/sshnet/SSH.NET/releases/tag/2025.1.0) ·
  [dropbear pkg](https://pkgs.alpinelinux.org/package/edge/main/x86_64/dropbear) ·
  [virtio-console resize never merged (v4 2025)](https://www.mail-archive.com/qemu-devel@nongnu.org/msg1137505.html)
- Guests: [Alpine requirements](https://wiki.alpinelinux.org/wiki/Requirements) ·
  [mmdebstrap(1)](https://manpages.debian.org/testing/mmdebstrap/mmdebstrap.1.en.html) ·
  [OpenWrt → apk](https://linuxiac.com/openwrt-25-12-released-with-apk-package-manager-replacing-opkg/)
- Exotics: [container2wasm](https://github.com/ktock/container2wasm) ·
  [wasmtime-dotnet](https://github.com/bytecodealliance/wasmtime-dotnet) ·
  [c2w perf report #75](https://github.com/container2wasm/container2wasm/issues/75) ·
  [CheerpX licensing](https://cheerpx.io/docs/licensing) ·
  [Blink Windows status #27](https://github.com/jart/blink/issues/27) ·
  [Unikraft multiprocess (no plain fork)](https://unikraft.org/blog/2025-05-15-multiprocess) ·
  [OSv no fork/exec](https://lwn.net/Articles/610004/) · [WASIX docs](https://wasix.org/docs)
