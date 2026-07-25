# Architecture

## Shape of the thing

One SimHub plugin assembly, `AB9ActiveShifter.dll`, plus a test project. The split that matters:

- **`Core/`** is pure — no DirectInput, no vJoy, no Win32, no clock it does not own. Everything
  worth testing lives here.
- **`Device/`** and **`Output/`** are the only places with I/O. The vJoy wrapper is a 32-bit native
  DLL that test runners cannot load, which is why gear output sits behind `IGearOutput` and why
  `Core/` must stay clean.
- **`UI/`** binds directly to `ShifterSettings` and never talks to the device.

## Threading

**One background thread, `AB9ShifterFFB`, owns every DirectInput, effect, and vJoy call.** No
exceptions except `FfbDevice.StopForces()`, which the watchdog may call to kill output when the
loop has stopped ticking, and which swallows everything because the device may already be gone.

The thread runs `SearchDevice → OpenDevice → Run`, with 1/2/5 s backoff on failure. Each tick:

1. Poll position (cheap, and first, so every computation runs on data <1 ms old).
2. If calibration is active, run that instead and return.
3. State machine update.
4. On a gear change: **vJoy buttons first**, then raise the event.
5. Update the velocity estimate (EMA, smoothing 0.45, with `dt` sanity guards).
6. Compose forces, passing position, velocity, and the real elapsed time since the last
   composition (the attack shaping needs true `dt`, clamped so a stalled tick cannot dump a whole
   attack at once).
7. Apply — at most one constant-force write.
8. Publish a snapshot periodically, or immediately on a gear change.

Pacing uses a high-resolution waitable timer, with `timeBeginPeriod(1)` and a sleep/spin fallback.

The UI and SimHub properties read a **volatile snapshot object** and never touch the device. Config
changes set a dirty flag; the loop swaps in a whole new immutable `EngineConfig` at a tick boundary,
so a tick never sees a half-applied configuration. Dragging a *force* slider rebuilds only the
composer — the state machine is left alone unless the geometry actually moved, so tuning cannot
knock out a gear you are currently holding.

## Effect handling

Five effects are created once after acquire + reset and then only mutated — never stopped and
restarted — via `SetParameters(TypeSpecificParameters | NoRestart)`:

`springX`, `springY`, `constantX`, `constantY`, `damper`.

The springs exist but the gate does not use them (see [force-model.md](force-model.md)); a test
pins that. Retry logic: one retry without `NoRestart`, then three strikes fault the set and the
engine reopens the device.

**Write scheduling is the interesting part.** A write costs 1.0 ms on the USB frame clock, so:

- At most **one** constant-force write per `Apply`.
- If both axes are dirty, they alternate (`_lastContendedWriteWasY`), giving ~500 Hz each; a single
  hot axis gets the full 1 kHz.
- A write is skipped when the value is unchanged, or when it differs by less than
  `ConstantDeadband` (30) — except **zero always lands**, so releases are never deferred.
- Priming is tracked with explicit booleans. It used to be a `long.MinValue` timestamp sentinel,
  which overflowed and killed every constant force in the gate silently; see the note at the end of
  [hardware.md](hardware.md).

## Lifecycle

SimHub **rebuilds plugins at game change**, so the engine must survive it:

| Call | What it does |
| --- | --- |
| `Init` (first) | Load settings, attach properties/events/actions, start the engine if enabled |
| `Init` (repeat) | `ApplyConfig` only — do not tear the engine down |
| `End` | Save settings only |
| `FinalizePlugin` (`IReusable`) | The real teardown |
| `ProcessExit` hook | Backstop |

`DataUpdate` is deliberately empty, reserved for telemetry-driven effects (grind, synchro) that are
out of scope until the mechanical gate is finished.

## Safety

The base can produce 12 Nm, so every path is bounded:

- **Gain capped at 10%** until polarity is measured. Do not add a route around this.
- **Watchdog**: a 500 ms timer trips on >1 s of heartbeat staleness and calls `EmergencyStop`.
- **Ordering, always**: buttons off → stop forces → unacquire. Applies to shutdown, disable, device
  loss, and finalisation alike. A gear must never stay stuck down.
- **Process death** drops the exclusive DirectInput handle, and the driver discards the effects.
- Every composed force is clamped to ±10000 after summing, and a test sweeps a hostile config
  across the whole axis range to prove nothing escapes.
- `FreeStick` zeroes everything, as an escape hatch and as a way to prove whether resistance is
  coming from the plugin at all.

## State machine

`Neutral` / `Traveling(column, direction)` / `Engaged(column, direction)`, with enter/exit
hysteresis on every boundary (the exit band is always the looser one).

- Neutral → Traveling when the stick leaves the channel while over a column; the column and
  direction are latched at that moment.
- Traveling → Engaged past `EngageDepth` for `MinEngageTicks` (2 ticks = 2 ms; it filters
  single-tick spikes) → button down.
- Engaged → Traveling past `ReleaseDepth` → **button up immediately**.
- Traveling → Neutral on re-entering the channel.

**The latch is a lock.** Once a column is latched, sideways motion never changes it — the only
route to another gear is back through the neutral channel, exactly as a real gate works. See the
gear lock in [force-model.md](force-model.md) for what that buys.

The fault path: if X escapes the latched column by more than half a column spacing
(`EscapedColumn`), that is a fault, not a shift. The gear drops, the state goes Neutral, and an
`_awaitChannel` flag blocks any new latch until the stick has been seen in the channel. Without
that flag the very next tick would latch whatever column the stick had landed in, making the fault
path a back door into the diagonal shift the lock forbids. `Resync` — used at startup and after a
geometry change — deliberately does adopt the gear the stick is sitting in, and clears the flag.

Gear numbering is `GearOf(column, direction)`, 1–8 with 8 = reverse. `MirrorColumns` and
`MirrorSlots` relabel that map **only** — geometry never moves. See the invariants in
[../AGENTS.md](../AGENTS.md) for why.

## SimHub surface

Properties: `CurrentGear`, `GearIndex`, `InGear`, `GateState`, `GateColumn`, `StickX`, `StickY`,
`DeviceConnected`, `DeviceName`, `VJoyConnected`, `LoopHz`, `StatusMessage`.
Events: `GearEngaged`, `GearReleased`. Actions: `ToggleShifterFFB`, `ReleaseAllGears`.

`LoopHz` is measured from real tick intervals, not echoed from the setting — it is the honest check
that the loop is keeping up.

UI tabs: **Setup** (status, enable, free stick, pre-flight checklist, polarity calibration, manual
overrides, gear layout), **Feel** (master gain, gate walls, sliding across the gate, slot detent),
**Geometry** (force shaping, hysteresis bands, vJoy device, loop rate, resets), **Monitor** (live
gate drawing).

## Build

SDK-style `net48`, AnyCPU, WPF enabled, `Microsoft.NETFramework.ReferenceAssemblies` for offline
builds without a targeting pack. All SimHub assemblies referenced by `HintPath` into the install
directory with `Private=false`. Override the SimHub path by copying
`Directory.Build.props.user.example` to `Directory.Build.props.user`.

Build output is `src/AB9ActiveShifter/bin/<Config>/` — note there is **no** `net48` subdirectory,
which trips up copy commands written from habit.
