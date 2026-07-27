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

**The latch is an absolute lock.** Once a column is latched, `StepTraveling` and `StepEngaged`
ignore X entirely — no lateral distance, however large, changes or drops the gear. The only route to
another gear is back through the neutral channel, exactly as a real gate works. There is
deliberately no fault threshold: force cannot enforce a gate (a hand beats 12 Nm), so any distance
at which the latch gave way would be a distance at which the rest of the pattern came back and could
capture the lever into a gear it was never driven into. See the gear lock in
[force-model.md](force-model.md).

`Resync` is therefore the only way to adopt a position — startup, and a geometry change under the
running loop, where the engine rebuilds the state machine.

Gear numbering is `GateGeometry.GearFor(column, direction)`, 1..N with N = reverse — 8 for 7+R, 7
for 6+R, 6 for 5+R, so the vJoy buttons stay contiguous whatever the pattern. A slot that holds no
gear (6+R's missing 7) is simply a slot the map sends to 0: `SlotExists` follows the map, the wall
over it never opens, and the state machine refuses to latch it. Because the hole lives in the map,
`MirrorColumns` and `MirrorSlots` relocate it along with the gears. Both flags relabel the map
**only** — geometry never moves. See the invariants in [../AGENTS.md](../AGENTS.md) for why.

## Patterns and the sequential mode

`GatePattern` selects the topology. The H patterns (7+R, 6+R, 5+R) all run the same gate engine —
`GateGeometry` derives column count (three for 5+R, spread over the full axis), the gear map, and
whether a lockout gap exists (5+R has none: every barrier crest is then its gap's midpoint, so no
watershed is displaced by a gate that exerts nothing).

Sequential bypasses the gate: `SequentialStateMachine` fires one shift per stroke using the same
engage/release hysteresis pair on the Y axis, re-armed only by coming back inside the release
threshold, and `ForceComposer.ComposeSequential` renders the lever railed to the lateral centre
and sprung home fore/aft with a click at each threshold — through the same yield/attack/damping
pipeline and the same single polarity application. Shifts are **pulsed** vJoy buttons (1 = up,
2 = down, `SeqPulseMs` long), pressed *before* the tick's forces like every other button. Re-firing
a button that is still down releases it and delays the next press by 20 ms, because an off-and-on
inside one tick reads to a game's input poll as one continuous press. Pattern switches clear any
pulse in flight along with the held gear.

## Profiles

The settings file now holds a `ProfileStore` — a list of named `ShifterSettings` plus which one is
active — instead of one flat settings object. Each profile carries everything, the pattern
included, so each pattern keeps its own tuning. A pre-profile settings file deserialises into an
empty store (its properties do not match), which is the migration signal: the plugin re-reads the
file as flat settings and wraps them as profile "Default", so nothing tuned is lost. Switching
profiles is an ordinary config swap in the engine — the state machines rebuild and resync, a held
gear that the new geometry disowns is released, and a sequential pulse in flight is cleared.
Profile duplication copies by reflection over public read/write properties, so new dials are
included automatically and no event subscriptions ride along.

Every dial change **autosaves the store**, debounced two seconds after the last edit. SimHub only
calls `End` (the old save point) on a clean exit, and the deploy script force-kills the process —
without the autosave, everything tuned since the last profile switch died with it, which was
reported from the settings page as "settings won't save".

## SimHub surface

Properties: `CurrentGear`, `GearIndex`, `InGear`, `GateState`, `GateColumn`, `StickX`, `StickY`,
`DeviceConnected`, `DeviceName`, `VJoyConnected`, `LoopHz`, `StatusMessage`.
Events: `GearEngaged`, `GearReleased`. Actions: `ToggleShifterFFB`, `ReleaseAllGears`.

`LoopHz` is measured from real tick intervals, not echoed from the setting — it is the honest check
that the loop is keeping up.

UI tabs: **Setup** (profile & pattern, status, enable, free stick, pre-flight checklist, polarity
calibration, manual overrides, gear layout), **Feel** (master gain, gate walls, sliding across the
gate, slot detent), **Geometry** (force shaping, hysteresis bands, vJoy device, loop rate, resets),
**Monitor** (live drawing of the configured pattern — missing slots left blank, the lockout shaded
where the geometry puts it, or the sequential track).

## Build

SDK-style `net48`, AnyCPU, WPF enabled, `Microsoft.NETFramework.ReferenceAssemblies` for offline
builds without a targeting pack. All SimHub assemblies referenced by `HintPath` into the install
directory with `Private=false`. Override the SimHub path by copying
`Directory.Build.props.user.example` to `Directory.Build.props.user`.

Build output is `src/AB9ActiveShifter/bin/<Config>/` — note there is **no** `net48` subdirectory,
which trips up copy commands written from habit.
