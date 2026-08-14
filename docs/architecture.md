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
3. Update the velocity estimate (4 ms window + EMA, with `dt` sanity guards).
4. Step the telemetry effects: read the current telemetry snapshot, judge its freshness, and
   compute this tick's vibration and grind decision. The grind's "pushing into a slot" fact is
   read off the state machine *before* its update — last tick's state, one millisecond old — so
   this tick's engage decision can depend on the answer.
5. State machine update (with the grind's `allowEngage` refusal, if any).
6. On a gear change: **vJoy buttons first**, then raise the event.
7. Compose forces, passing position, velocity, the real elapsed time since the last composition
   (the attack shaping needs true `dt`, clamped so a stalled tick cannot dump a whole attack at
   once), and the effects' vibration and detent-mute.
8. Apply — at most one constant-force write.
9. Publish a snapshot periodically, or immediately on a gear change.

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

**A second failure mode is invisible to that path**, because it produces no write errors at all:
the base can throw the effects away while keeping the handle valid and accepting every write. A
once-a-second poll of `GetForceFeedbackState` watches for it (`ShifterEngine.WatchForceOutput`),
and on a confirmed `EffectsGone` recreates the effects on the handle already held —
`RebuildEffects`, one attempt, no reopen and deliberately **no change to the gear buttons**: the
lever has not moved and the game's idea of the current gear is still right, so clearing it would
turn a loss of feel into a loss of drive. *Confirmed* is load-bearing: this base sets the Empty
flag while producing force, so the flag is only believed when `EffectSet.AnyStillDownloaded()`
agrees with it. See [hardware.md](hardware.md) for the measurement. Recovery restores the status
line as well as logging — a fault sentence left in that field outlives the fault and hides every
status after it.

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

`DataUpdate` feeds the telemetry effects: it builds an immutable `TelemetryState` snapshot (rpm,
clutch, speed, gear string, ABS/TC flags, heave G, the sampled custom property) and hands it to the engine
through one volatile reference — no locks, one small allocation, nothing else on SimHub's critical
path. The FFB loop still deliberately does not *run* off it, because the gate must work with no
game running.

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

Gear numbering is `GateGeometry.GearFor(column, direction)`: forward gears 1..N, and **reverse
always 8**, whatever the pattern. The buttons are deliberately not contiguous — reverse used to
compact down to the pattern's highest gear (8/7/6), and that put 5+R's reverse on button 6, which
a game still carrying 7+R bindings read as "engage sixth", at speed. A fixed reverse button means
one set of game bindings survives switching patterns. A slot that holds no gear (6+R's missing 7)
is simply a slot the map sends to 0: `SlotExists` follows the map, the wall over it never opens,
and the state machine refuses to latch it. Because the hole lives in the map, `MirrorColumns` and
`MirrorSlots` relocate it along with the gears. Both flags relabel the map **only** — geometry
never moves. See the invariants in [../AGENTS.md](../AGENTS.md) for why.

## Patterns and the sequential mode

`GatePattern` selects the topology. The H patterns (7+R, 6+R, 5+R) all run the same gate engine —
`GateGeometry` derives column count (three for 5+R, spread over the full axis), the gear map, and
whether a lockout gap exists (5+R has none: every barrier crest is then its gap's midpoint, so no
watershed is displaced by a gate that exerts nothing).

Sequential bypasses the gate: `SequentialStateMachine` fires one shift per stroke using the same
engage/release hysteresis pair on the Y axis, re-armed only by coming back inside the release
threshold, and `ForceComposer.ComposeSequential` renders the lever railed to the lateral centre
and sprung home fore/aft with a click at each threshold — through the same yield/attack/damping
pipeline and the same single polarity application. Shifts are **pulsed** vJoy buttons (9 = up,
10 = down, `SeqPulseMs` long — deliberately above every gear button, so a game still carrying
H-pattern bindings cannot read a shift pulse as "engage 1st"), pressed *before* the tick's forces
like every other button. Re-firing
a button that is still down releases it and delays the next press by 20 ms, because an off-and-on
inside one tick reads to a game's input poll as one continuous press. Pattern switches clear any
pulse in flight along with the held gear.

PRND bypasses the gate too, and further: there is no neutral, no travelling and no engage debounce,
because a selector lever is always in exactly one position. `PrndLane` owns where the four sit and
which button each holds — its own class rather than a `GateGeometry` with one column, because that
would have meant a channel that means nothing, a lockout that cannot exist and a gear map with no
reverse in it, four special cases in the middle of the gate to save forty lines. `PrndStateMachine`
holds an index and hands it on at the crests with the same hysteresis bias `GateGeometry.Pick`
uses; `ForceComposer.ComposePrnd` renders the sequential rail laterally and the lane's detents fore
and aft, through the same pipeline and the same single polarity application.

Its buttons (11–14) go out through `VJoyGearOutput.SetGear` rather than `SetButton`, which is what
gives a position the same release-before-press, the same watchdog clear and the same shutdown
ordering a gear gets — `GearCount` therefore bounds what that method may press, not what a gear is.
The one thing the engine must ask per pattern is what should currently be held, and `ShifterEngine`
has exactly one answer for it (`CurrentHeldButton`), used by all three places that push the truth
back to vJoy: a rebuilt gate, a finished calibration, and a profile switch.

## Telemetry effects and the grind

`EffectComposer` lives on the engine thread and keeps the carrier phases; `TelemetryState` is
written whole by SimHub's data thread and read whole by the tick, so there is nothing to lock.
Freshness is judged from the snapshot's `Environment.TickCount` capture stamp (unchecked
subtraction, wrap-safe): anything older than 500 ms — game paused, hung, or gone — silences every
effect the same tick. The vibration is summed into the composed fore/aft force after the yield
and attack stages and inside the final clamp and polarity signs; the reasoning lives in
[force-model.md](force-model.md), "The vibration channel and the grind".

The grind is the one effect with mechanical consequences, and it touches exactly two things:
`GateStateMachine.Update` takes an `allowEngage` flag that refuses the Traveling→Engaged
transition (the debounce counter holds at zero, so engagement after the clutch goes down still
takes the full `MinEngageTicks`), and `ForceComposer` renders the slot detent as the balk wall
while balked — entry resistance plus `GrindWallPct`, no crossover, attack-shaped and
full-absorbed like the wall it has become. Geometry is never touched at runtime, an engaged gear is never dropped, and everything
else — buttons before forces, the release path, the watchdog — is unchanged. Both flags are
plumbed per tick, so a settings change or telemetry loss reverts on the next millisecond.

### The clutch pedal, and what a failing open costs

Reading the clutch off its own device puts a second DirectInput handle on the engine thread, held
non-exclusively because the game is reading those pedals too. Two things about it are paced rather
than done every tick, and only one of them was paced from the start.

The **poll** runs once every `PedalPollEveryTicks` (10) — an ankle does not need a kilohertz, and
every poll is time the gate is not getting.

The **open** is now gated by a `RetryBackoff` on the same 1/2/5 s schedule the base's own reconnect
uses. It was not, and the cost was not small: opening a DirectInput device that is not there fails
after roughly **12 ms**, so while the bound pedals were missing — unplugged, or a saved binding for
hardware the machine no longer has — the loop ran at **81 Hz instead of 990**. Every stability
argument in this project is made from that loop rate. What made it hard to see is that the *log*
had been throttled to thirty seconds from the beginning: the failure was paid for a thousand times
a second and mentioned once in thirty thousand, so the only symptom was the number on the Monitor
tab. `ClosePedals` resets the backoff, because closing is always a deliberate transition and the
next attempt should be immediate; picking a different device in the picker resets it too.

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

A UI-lifetime fact that cost a real bug: **SimHub keeps the settings control alive across page
navigation** — leaving the plugin page fires `Unloaded`, returning fires `Loaded`, and the
constructor runs once ever. Anything the constructor subscribes and `Unloaded` unsubscribes must
be re-subscribed in `Loaded`, or the first navigation away disconnects it permanently. The
profile-changed handler was exactly that: Duplicate kept creating and activating profiles the
combo never showed, while the dials stayed bound to the previously active profile's object.

Every dial change **autosaves the store**, debounced two seconds after the last edit. SimHub only
calls `End` (the old save point) on a clean exit, and the deploy script force-kills the process —
without the autosave, everything tuned since the last profile switch died with it, which was
reported from the settings page as "settings won't save".

## SimHub surface

Properties: `CurrentGear`, `GearIndex`, `InGear`, `GateState`, `GateColumn`, `StickX`, `StickY`,
`DeviceConnected`, `DeviceName`, `VJoyConnected`, `LoopHz`, `StatusMessage`, `LockoutEngaged`.
Events: `GearEngaged`, `GearReleased`, `LockoutEngaged`, `LockoutReleased`.
Actions: `ToggleShifterFFB`, `ReleaseAllGears`, `NextProfile`, `PreviousProfile`,
`ToggleLockout`, `EngageLockout`, `ReleaseLockout`.

`LoopHz` is measured from real tick intervals, not echoed from the setting — it is the honest check
that the loop is keeping up.

The hard lockout's engaged state is engine runtime, not a setting: a volatile level set from
SimHub's action thread and read once per tick (the free-stick shape), so a keypress cannot fork a
preset or churn the debounced save. It re-engages on every start and every gate-moving or
mode-changing config swap; the composer consumes it beside `muteDetent`, and the refusal reaches
the state machine through the grind's own `allowEngage` argument, one tick stale like the grind.

UI tabs: **Setup** (profile & pattern, status, enable with the lockout's keys, free stick,
pre-flight checklist, polarity calibration, manual overrides, gear layout), **Feel** (master gain,
gate walls, sliding across the gate with the lockout's position, direction and mode, the PRND
lane with its own lockout block, slot detent), **Effects** (the telemetry effects: grind, engine
vibration, limiter, ABS/TC, curbs, shift pulse, custom property — each with enable, volume and
frequency), **Geometry** (force shaping, hysteresis bands, vJoy device, loop rate, resets),
**Monitor** (live drawing of the configured pattern — missing slots left blank, the lockout
shaded where the geometry puts it and dimmed while a hard gate is released, or the sequential
track).

## Build

SDK-style `net48`, AnyCPU, WPF enabled, `Microsoft.NETFramework.ReferenceAssemblies` for offline
builds without a targeting pack. All SimHub assemblies referenced by `HintPath` into the install
directory with `Private=false`. Override the SimHub path by copying
`Directory.Build.props.user.example` to `Directory.Build.props.user`.

Build output is `src/AB9ActiveShifter/bin/<Config>/` — note there is **no** `net48` subdirectory,
which trips up copy commands written from habit.
