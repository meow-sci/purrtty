# Host layer

The host layer (`KSA.Kfs.Host`, `KSA.Kfs.Host.Services`, `KSA.Kfs.Host.Views`) sits between the
kVM and the game: it owns one VM per computer, decides when ticks run, turns game state into
message bytes, and turns service calls into work for the game. Checked against KSA build
2026.11.25.5949.

## Computers

- [HostComputer](/host/host-computer.md) — HostComputer is the base host-side computer that owns one KvmMachine, loads images, runs one tick at a time, drains the VM's service requests, keeps the console log and publishes thread-safe snapshots.
- [VehicleComputer](/host/vehicle-computer.md) — VehicleComputer is the HostComputer subclass a vehicle carries; it schedules ticks against simulation time, publishes the host messages, latches thruster pulses, owns burn handles and the service override, and queues service requests for the game.
- [BenchMachine and FakeVehicle](/host/bench-machine-and-fake-vehicle.md) — The two off-vehicle hosts — BenchMachine, the workbench's free-running computer with zeroed sensors and no vehicle, and FakeVehicle, a test harness that runs a real VehicleComputer against a generated layout and scripted samples.

## Time and control

- [Tick grid and timing](/host/tick-grid-and-timing.md) — How a vehicle computer maps simulation time onto tick indices with TickGrid, catches up or skips overdue ticks, honours a wall-clock deadline, reports overruns and tells the game when to wake it next.
- [Ownership and control modes](/host/ownership-and-control-modes.md) — How an image's claims and publications decide whether kFS merely steers the stock flight computer (pilot mode) or drives the actuators itself (full control), which images are refused, and how control is taken, lost and handed back.

## Program ↔ game channels

- [I/O layout generation](/host/io-layout-generation.md) — How LayoutGenerator turns a vehicle's thruster, engine and gimbal counts into the memory-mapped command block layout (vehicles get no sensor fields; only the bench layout has them), what limits apply, and how the blocks are written and the command block latched each tick.
- [Host sampling and leaves](/host/host-sampling-and-leaves.md) — The data a sampler hands the host each dispatch (HostSample with NavTruth, FlightDataValues, VehiclePropsValues and HostLeafValues), every field of the seven host messages with its type and offset, and when the host asks for each part.
- [Message bus (host side)](/host/message-bus.md) — How the host packs its seven published messages into slot bytes and delivers them to a program each tick, how program publications come back, what BusTracker monitors, and which bus features are not active in this build.
- [Service request handling (host side)](/host/service-request-handling.md) — The complete host-side path of a kFS service call — from the VM's SYSCALL to ServiceRequest, the hard-coded host and vehicle switches, the queue the game drains, synchronous results, rejection, burn handles, the service override, the console log and traces.

## Observation

- [Snapshots and performance data](/host/snapshots-and-perf.md) — The immutable MachineSnapshot a HostComputer publishes for other threads and everything inside it — task, function, trap, NaN and last-run records, performance series and reports — plus the tick recorder and the RAM view helpers.
- [Host view models (KSA.Kfs.Host.Views)](/host/views.md) — Every renderer-neutral view model in KSA.Kfs.Host.Views — bus monitor, disassembly, profiler, oscilloscope, compile report, NaN view, definitions, diagnostic manual, budget bars, line costs and layout helpers — with the data each draws and where that data comes from.

## Extending

- [Host extension surface](/host/host-extension-surface.md) — Where a mod can attach to the kFS host layer — the few patch-free hooks, the Harmony patch targets with signatures and visibility, the four ways to deliver variable-length host data to a program, and every size limit that constrains a design.
