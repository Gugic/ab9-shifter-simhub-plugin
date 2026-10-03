# Architecture

## Shape of the thing

One SimHub plugin assembly, `AB9ActiveShifter.dll`, plus a test project. The split that matters:

- **`Core/`** is pure — no DirectInput, no vJoy, no Win32, no clock it does not own. Everything
  worth testing lives here.
- **`Device/`** and **`Output/`** own hardware I/O. The vJoy wrapper is a 32-bit native
  DLL that test runners cannot load, which is why gear output sits behind `IGearOutput` and why
  `Core/` must stay clean.
- **`UI/`** binds directly to `ShifterSettings` and never talks to the device.
- **`Updates/`** checks GitHub and replaces the plugin DLL, entirely away from the force loop.
  `ReleaseInfo` is the pure parser and version policy tested without I/O.
- **`Effects/`** hosts SimHub's native ShakeIt editor and sources. Its output manager copies
  strength/frequency envelopes into a pure `NativeEffectFrame`; it owns no device handle.

## Threading

**AB9 configuration** is an optional CDC worker, separate from the force and telemetry
threads. The rig chooses Generic FFB Stick, AB9-native or AB9 H-pattern. Both virtual modes
run the same gate and selected gear output; AB9-native replaces only the basic DirectInput base effects
with onboard settings. Firmware H-pattern releases the virtual engine entirely.

The worker uses exact USB identity, re-checks firmware at each session, reads every write back
and releases the COM port afterward. Both AB9 choices require firmware 1.1.5.2 or newer.
Profiles are shared between virtual modes. The selected mode routes the same base-effect
percentages to DirectInput or onboard controls; switching modes never clones or retunes a
profile. Firmware H-pattern blocks profile activation and virtual output.

The initial read-only mode check is reserved before forces or gear presses can start, then dispatched so its
completion can safely notify bound settings. A failed read preserves a generic setup; a known
firmware H-pattern mode or an uncertain mode after a failed write blocks virtual output on that
AB9. Other FFB sticks remain independent. The UI reads native settings once when Feel opens,
or when the user requests Refresh AB9 in Options. Its status timer only renders the cached snapshot;
opening Main/Options or leaving an editor open does not poll the configuration port. Startup
checks and the fresh checks/readbacks required by a configuration transaction remain in place.
Output ownership may be reserved while the check is pending, with every button clear.

Every write pauses virtual output first, using the existing teardown ordering, then mutes
hardware torque while configuring the base. Torque is restored last. A pure
`NativeSettingsDebounce` holds the latest copied values for the seven onboard dials until
500 ms after the last edit; busy reads and in-flight writes do not lose a newer edit. The
UI dispatcher drains the queue, while the CDC work stays on its separate worker. A batch
keeps one pause until its final requested values pass readback.

`NativeWritePause` preserves the user's enabled switch during ordinary tuning and profile
activation, without leaving output running during configuration. Only a successful final
readback may resume an already-enabled session. Off and panic revoke resume immediately,
including when their dispatcher update has not arrived yet. Setup, mode changes, calibration,
imports and failures leave virtual output off. Mode/profile identity changes cancel queued
outgoing edits; profile activation and list edits wait for a current write to finish. Setup
recipes and failure behavior are detailed in [native-ab9.md](native-ab9.md).

**One background thread, `AB9ShifterFFB`, owns every DirectInput, effect, and vJoy call.** No
exceptions except `FfbDevice.StopForces()`, which the watchdog may call to kill output when the
loop has stopped ticking, and which swallows everything because the device may already be gone.

The thread maintains selected output ownership before attempting the base. The base runs
`SearchDevice → OpenDevice → Run`, with 1/2/5 s backoff on failure — **except when
the fault says another application has taken the base**, where it stands down instead. Exclusive
access goes to the foreground app, so reopening is not a repair; it pulls the device out from under
the game and crashes it. `DeviceFaults.Classify` reads the HRESULT, `HandleDeviceLoss` releases and
sets `_yielded`, and `ReadyToReclaim` waits for SimHub to report no game running before looking
again (5/15/30 s between looks, and the first look one whole wait after the loss so a game still
starting up is not mistaken for one that has gone). Any config change clears the stand-down, which
is what makes re-picking a device in Options the deliberate "take it back anyway".

Each tick:

1. Poll position (cheap, and first, so every computation runs on data <1 ms old).
2. If calibration is active, run that instead and return.
3. Update the velocity estimate (4 ms window + EMA, with `dt` sanity guards).
4. Step the telemetry effects: read the current telemetry snapshot, judge its freshness, and
   compute this tick's vibration and grind decision. The grind's "pushing into a slot" fact is
   read off the state machine *before* its update — last tick's state, one millisecond old — so
   this tick's engage decision can depend on the answer.
5. State machine update (with the grind's `allowEngage` refusal, if any).
6. On a gear change: **submit the selected output first**, then raise the event.
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

Calibration is queued before the engine starts, so acquisition cannot briefly run a normal
profile. Pending acquisition and the gaps between individual probes remain part of one active
calibration. Completion or cancellation holds all DirectInput forces and selected gear output off
until a deliberate restart. Successful AB9-native setup then reapplies the shared base-effect
percentages while virtual output stays off. Navigating away cancels an unfinished measurement.

## Effect handling

The constant forces and two calibration springs are created after acquire + reset; damper,
friction and inertia are optional condition effects where supported. Parameters use
`SetParameters(TypeSpecificParameters | NoRestart)`, with a retry without `NoRestart` before
repeated failures fault the effect set. Effects start at zero; ordinary frames supply their
requested coefficients, while calibration frames remain unmodified.

`ForceComposer` continues to render every gate wall as constant force, with both spring fields
off. `BaseEffectComposer` adds Generic FFB Stick's optional global spring, friction and inertia
after the gate is composed. The spring uses separately measured signs on each axis and is
suppressed unless its own polarity confirmation is present. Its center is DirectInput offset
zero, not a moving gate anchor. These effects share the effective gain and 10% unconfirmed cap,
are zero in free-stick mode, and are excluded during calibration. AB9-native suppresses these
DirectInput base effects, including the legacy device damper, without removing software wall
damping, wall friction, home spring or telemetry forces. Coefficients are written only when
changed, and all created effects participate in download checks, stop and disposal.

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

`ShifterSettings.Enabled` is Main's **Shifter enabled** master switch, controlling both base
and output ownership. `NativeProfilePolicy.CanOwnGearOutput` permits either virtual mode even
when the base is missing, but rejects selected or observed AB9 firmware H-pattern mode.
The shell stamps `EngineConfig.VirtualDeviceEnabled` with the stricter existing force/setup
eligibility. A pending native check or unavailable AB9-native snapshot can therefore keep an
output-only loop alive without touching DirectInput or publishing any gear/neutral role.
A failed read alone no longer clears the user's master request; verified firmware H-pattern
still does. Verified native reads resume an output-only loop when base eligibility returns;
an ordinary status refresh does not disturb an already eligible running or yielded base.

Before a native write the shell synchronously stops the base thread with output retention,
then starts an output-only loop. This keeps the selected reservation while the write/readback
is outstanding. The ordinary verified resume policy still decides whether forces may return.
Master off, panic, shutdown and firmware H-pattern fully disconnect output; base loss and
temporary native configuration clear its buttons without relinquishing it.

`DataUpdate` publishes the immutable `TelemetryState` used by the clutch protection, then steps
SimHub's native ShakeIt host. The native output manager copies each active tone into a second
immutable snapshot. Native profile swaps and serialization use a short lock on the dispatcher;
the data callback uses `Monitor.TryEnter` and publishes silence if busy. The 1 kHz loop takes
neither that lock nor any native object. It still runs with no game connected.

## Plugin updates

`UpdateService` is shared across `End`/`Init` at game change, like the engine, and is cancelled
on `FinalizePlugin` after the engine's normal teardown. A thread-pool timer checks the latest
stable release at startup and every six hours; manual checks share the same operation gate.
Network requests have deadlines, bounded response sizes and no work on the FFB or telemetry
threads. Immutable `UpdateState` snapshots notify the settings page through its dispatcher;
the page unsubscribes while unloaded and reattaches when navigated back to.

`ProfileStore.CheckUpdatesAutomatically` and `DismissedUpdateVersion` are app preferences,
outside `ShifterSettings` and profile transfer. Dismissal applies to one release version and
only the banner; Options retains the update actions and notes. Turning automatic checking off
stops the timer; Check now still works. A failed check preserves any previously fetched release
and shows the failure, without stopping the shifter.

`ReleaseInfo` accepts stable numeric versions, ignores build metadata when comparing, and
never offers a downgrade. Only the exact repository's matching standalone DLL asset is
installable, with a valid size and GitHub SHA-256 digest. `UpdateInstaller` stages beside the
installed DLL, checks the download size, checksum and assembly manifest name/version without
executing it, then uses `File.Replace` to atomically install it and keep `.previous` as backup.
A failed validation does not replace the DLL, and a failed swap leaves the original in place.
Only the DLL changes; settings and hardware state are untouched during installation.

The current image continues running until the user selects Restart SimHub. The plugin saves
the store and calls the public `PluginManager.RequestApplicationExit(true)` hook, so SimHub's
normal finalisation releases buttons and forces in their existing order. The signature was
reflected from the installed SimHub assembly and added to the reference stub too. Backup cleanup
runs only when the update service is first created, not on every game change. A second install
is refused until restart. Windows replacement of a loaded image was verified using scratch
copies; the automated tests continue to touch no files, network or hardware.

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

Its buttons (11–14) go out through `IGearOutput.SetGear` rather than `SetButton`, which is what
gives a position the same release-before-press, the same watchdog clear and the same shutdown
ordering a gear gets — `GearCount` therefore bounds what that method may press, not what a gear is.
The one thing the engine must ask per pattern is what should currently be held, and `ShifterEngine`
has exactly one answer for it (`CurrentHeldButton`), used by all four places that push the truth
back to the output: a rebuilt gate, a finished calibration, a profile switch, and output arriving late.

**vJoy is retried for as long as it is missing, and the retry does not live in `TryOpenDevice`.**
The connect used to be attempted only there, and the loop stops calling that method the moment the
base opens — so at a cold boot, where SimHub starts with the machine and the vJoy device is a second
or two behind it, the base won the race, the phase went to `Run`, and vJoy was never asked again.
The gate rendered perfectly and no game was ever told what gear it was in, until someone re-picked
the device in Options by hand — which worked only because re-picking it is a config change,
and a config change used to reopen everything. `WatchGearOutput` now runs each tick beside `WatchForceOutput`,
gated by a `RetryBackoff` (1/2/5/15 s) because this is I/O the tick can attempt and fail, and it
pushes `CurrentHeldButton` out the instant it succeeds: a device that arrives late must be told the
gear it missed, or the game sees neutral until the next shift — in PRND, possibly for the session.

## Gear output backends

`ShifterEngine` holds `IGearOutput`; the SimHub shell supplies its factory. Direct vJoy remains
the default, including for old saved settings. Native Control Mapper uses
`PluginManager.GetControlMapperInterface()` and its public `StartRole` / `StopRole` methods,
with role lists from the same interface. No controller is registered or acquired by this path.
Control Mapper's own configuration decides whether those roles produce keys, vJoy buttons,
Arduino bridge buttons, or internal SimHub controls.

Feature readiness uses the public `PluginManager.GetPlugin<ControlMapperPlugin>()`, not an
empty role list or the presence of Controls and events. UI and output connection share the
same availability/mapping policy, distinguishing a missing feature from an enabled mapper
without roles. `ShowPluginUI<ControlMapperPlugin>()` opens its native configuration page.

The SDK has no public activation method. `ControlMapperFeatureSettings` checks the public host
model's `MainModel.EnabledPlugins` collection for the exact Control Mapper class and its public
`IsEnabled` setter. On an explicit enable/restart click only, it sets that feature and calls
`RequestApplicationExit(true)`; SimHub persists the model through its normal exit, including
its own settings backups. No private activation method or JSON write is used. An unavailable
host shape falls back to Add/remove features, kiosk lock blocks the action, and a throwing
restart request restores the previous setting. Other feature flags, mapper output settings,
and role definitions are untouched. The host shape was reflected from the installed SimHub;
it remains outside the plugin SDK and is guarded rather than treated as a permanent contract.

`ControlMapperGearOutput` keeps held H/PRND roles and sequential button lifetimes behind the
same interface as vJoy. It owns one `ControlMapperInterface` for its lifetime: SimHub keys
presses by an owner id, and a new interface cannot stop the previous owner's presses. Blank
roles are intentional no-ops. Shared roles count active logical buttons, so releasing one does
not cancel another. Failed presses are retained for cleanup, since a call can throw after
submitting; failed releases prevent pressing a replacement gear. Cleanup attempts releases
even after the output reports disconnected. The optional role at index 0 is H neutral only;
`ReleaseAll`, including calibration and the watchdog, clears it too.

The settings UI always exposes H-pattern, sequential and PRND mappings as separate groups.
`GearOutputConfig` masks runtime mappings by the current pattern and compares active output
configuration. Editing another pattern's assignments retains the current output and its
held roles; switching pattern or editing a role it uses releases and replaces the output.
Output mode, role assignments, and vJoy id are machine facts, excluded from shared profiles.
Changing backend, active mapping, or native pattern disconnects the old output before creating
the new one and republishing the held gear. Changing output does not reopen the AB9. Reconnect
uses the existing 1/2/5/15 s backoff and republishes the current state, except during calibration.
`OutputConnected` and `OutputError` describe either backend; the existing `VJoyConnected`
property continues to mean direct vJoy specifically.

`GearOutputConnection` owns that lifecycle and uses a fakeable `IGearOutput` boundary. It polls
acquisition while the base is absent and checks cached ownership once per second while connected.
Its deadlines reset when the engine clock restarts, without replacing a retained output.
vJoy checks actual driver ownership and invalidates its cached connection after failed button
writes or resets. Recovery republishes a held gear only when the engine has a usable base sample;
otherwise every button remains clear, including H neutral and PRND. Successful base reopen
resyncs all state machines and publishes the current position. Base reconnect neither replaces
the output instance nor resets its acquisition backoff. Native role output uses the same policy.

Ordering at the engine remains release-before-press and output requests before force writes.
Control Mapper queues roles for its own worker, so request ordering does **not** establish that
the game's key/button arrived before the force. End-to-end timing, keyboard neutral semantics,
and cleanup on game/profile/SimHub lifecycle changes still need verification on the rig.
Readiness establishes that the configured roles exist, not that the selected external device
is connected or the game accepted them; those are checked in Control Mapper and in the game.

The output choice is independent of `ProfileStore.SelectedOperatingMode`. Both Generic FFB
Stick and AB9-native run either backend; firmware H-pattern stops the engine and hides plugin
output settings. Native configuration clears buttons and releases the base before the CDC
transaction while retaining output ownership, then resumes presses and forces only through the
checked `NativeWritePause` path. Calibration
never publishes H neutral while probes run.

## Telemetry effects and the grind

`NativeEffectsService` embeds SimHub's `EffectsListMain`, with the native add/group/calibration
toolbar, response filters, live previews, tests, frequency, priority and channel assignment.
`AB9EffectOutputManager` is a public `MotorsWithFrequencyOutputManagerBase` adapter with one
logical **Lever** channel. Native sources compute the envelopes; the pure `NativeEffectMixer`
keeps a separate sine phase for each tone and renders them at 1 kHz. It allocates nothing during
a tick and keeps the measured 4–130 Hz range. Native frequency modulation, including its
**White noise** frequency randomization, is preserved. Sound banks are not lever outputs.

`EffectComposer` remains the immediate clutch/grind decision and bite-crossing counter on the
engine thread. Four source containers expose clutch grind, clutch bite point, rev limiter and
the legacy custom-property bridge inside native rows. Grind protection runs every tick; its
vibration envelope is sampled by ShakeIt at the game's data rate. Legacy carrier code and
fields remain for old-profile migration and arithmetic regression tests, but the plugin always
selects native rendering and stays silent if its native host cannot load.

Unchecked `Environment.TickCount` subtraction checks both snapshots. A stale native frame or a
profile-epoch mismatch silences the mixer; stale/inactive telemetry silences ordinary effects
within 500 ms. An explicitly requested native **Test** can play without a game, while still
requiring fresh native frames, an armed shifter, the effective gain cap and the vibration budget.
The vibration joins the fore/aft force after yield/attack and before the final clamp/polarity
signs. Native high-priority tones suppress ordinary tones on this one channel.

Each named shifter profile stores its native tree and master gain/mute in `NativeEffectsJson`.
A null field seeds nine rows from that profile's old dials, silently, once. Native edits are
observed every 250 ms and use the existing preset-fork/autosave path; expansion and selection
state are excluded, and a preset fork keeps the live native profile/editor as well as the
settings object. JSON property ordering does not count as an edit. Reset Effects rebuilds every
row. Export/import carries the whole tree; `NativeEffectsData` strips output managers and
restricts container/filter/output/settings types before native deserialization. The profile
picker outside Effects is the only picker. The service survives per-game plugin reconstruction.
Native calibration is saved through the public host's `SaveSettings` at End/Finalize. That also
writes an auxiliary native settings snapshot, which the from-device host ignores on reload;
the shifter profile's `NativeEffectsJson` remains the sole authority for effect tuning.

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
`DeviceConnected`, `DeviceName`, `VJoyConnected`, `OutputConnected`, `OutputError`, `LoopHz`, `StatusMessage`, `LockoutEngaged`.
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

Setup is a first-run view. `ProfileStore.SetupCompleted` keeps a configured rig on Main
through disconnects and restarts; existing measured rigs adopt the completed state. Main holds
the setup summary, profiles, pattern, force enable/free-stick and live monitor. Geometry, Feel
and Effects open as modal windows. Geometry keeps a second live monitor outside its scroller.
Options holds the mode switch, device/output settings, recalibration, pedals, hotkeys,
diagnostics, resets, updates and About. Main uses one level of disclosure for automatic
profile switching and sharing help; vehicle IDs never lengthen the action buttons. Firmware
H-pattern exposes only mode and device status.

`SettingsControl.Outputs` displays only mappings used by the active profile's pattern. First-run
completion checks that backend's setup readiness; it never requires vJoy when Control Mapper is
selected. Main reports the selected output and any runtime error, while periodic role/device
checks update readiness without rebuilding an open picker. Completed setup persists through
output loss. Firmware H-pattern skips both output probes entirely.

All editor panels are created with the settings control so slider indexing, reset/undo state
and namescope bindings remain intact when a panel is temporarily hosted by a modal. The existing
ShakeIt editor instance moves with its panel; changing presentation does not recreate a profile.
Geometry resets dimensions and placement, Feel resets strengths and response, and neither
resets measured polarity or machine identity. Calibration and complete resets live in Options.

All profiles share plugin geometry, extra effects and base-effect percentages between virtual
modes. In AB9-native, onboard changes apply automatically after the quiet period. Profile
import validates tuning and preserves machine facts, then activates the new profile with
virtual output disabled; its onboard values still synchronize automatically. Mode choice is
a rig preference and never travels in a shared profile.

## Build

SDK-style `net48`, AnyCPU, WPF enabled, `Microsoft.NETFramework.ReferenceAssemblies` for offline
builds without a targeting pack. All SimHub assemblies referenced by `HintPath` into the install
directory with `Private=false`. Override the SimHub path by copying
`Directory.Build.props.user.example` to `Directory.Build.props.user`.

Build output is `src/AB9ActiveShifter/bin/<Config>/` — note there is **no** `net48` subdirectory,
which trips up copy commands written from habit.
