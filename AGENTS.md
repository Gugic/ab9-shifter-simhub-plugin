# AB9 Active Shifter — working notes for agents

A SimHub plugin that turns a **MOZA AB9 flight base** into an **H-pattern shifter** (7+R with a
push-through lockout, 6+R with no 7th slot, 5+R), a **sequential lever**, or an **automatic's PRND
selector**, selectable per named profile. It renders the gate with DirectInput force feedback,
detects the slotted gear from stick position, and holds a vJoy button per gear (or pulses up/down
buttons in sequential, or holds one per PRND position) so any game sees a normal shifter.

It is unofficial and unaffiliated — see *Naming, and the disclaimers* under Conventions before
writing anything user-facing.

This file is the orientation. These documents hold the detail:

| Document | Read it when |
| --- | --- |
| [docs/hardware.md](docs/hardware.md) | You need a measured fact about the base, the USB path, or MOZA's software. **Read before theorising about timing, polarity, or effect strength.** |
| [docs/force-model.md](docs/force-model.md) | You are changing how the gate feels. Records every approach tried and why it failed, so it is not retried. |
| [docs/architecture.md](docs/architecture.md) | You are changing code structure, threading, or lifecycle. |
| [docs/tuning.md](docs/tuning.md) | A human reports a feel problem and you need symptom → dial. |
| [DEVELOPMENT.md](DEVELOPMENT.md) | The human contributor's entry point: build, test, deploy, release, and a condensed architecture overview. Mirrors the build commands below — change both. |

## The one paragraph that matters

Nearly every hard problem in this project has been the same problem: **a stiff virtual wall
rendered through a delayed loop is unstable.** The base is 12 Nm, the position-to-torque round
trip has a hard floor of 3–4 ms, and no amount of damping or loop rate fixes a force gradient
that is too steep for that delay. The gate is therefore built out of shapes chosen for
stability — flat plateaus, free corridors, one-way tolls — not out of stiffness turned up until
it feels right. If you find yourself about to raise a stiffness to fix a feel complaint, read
[docs/force-model.md](docs/force-model.md) first.

## Build, test, deploy

```bash
dotnet build
```

```bash
dotnet test tests/AB9ActiveShifter.Tests
```

360 tests, all green, none touching I/O — `Core/` plus the settings POCO's derived-dial
arithmetic. Keep them that way — they are the only automated check on force arithmetic, and a
sign error here drives a 12 Nm base the wrong way.

CI runs exactly this plus `dotnet format whitespace --verify-no-changes`, on every push and
pull request. It builds against the stubs in `build/refs` because a hosted runner has no
SimHub; if you change how the plugin uses SimHub's API, add the member to the matching stub in
the same commit or CI goes red while your local build stays green.

Deploy needs SimHub stopped, because it locks the DLL:

```bash
powershell -File install.ps1
```

Or by hand, which is what an agent usually wants (note the output path has **no** `net48`
segment):

```bash
powershell -Command "Stop-Process -Name SimHubWPF -Force -ErrorAction SilentlyContinue; Start-Sleep 2; Copy-Item src\AB9ActiveShifter\bin\Debug\AB9ActiveShifter.dll 'C:\Program Files (x86)\SimHub\' -Force; Start-Process 'C:\Program Files (x86)\SimHub\SimHubWPF.exe'"
```

Then confirm it actually came up — a load failure is silent from the outside:

```bash
powershell -Command "Get-Content 'C:\Program Files (x86)\SimHub\Logs\SimHub.txt' -Tail 40 | Select-String AB9"
```

Healthy startup logs `Opened 'MOZA AB9 FFB Base' exclusive+background` and `vJoy device 1
acquired (8 buttons)`. A transient `DIERR_NOTEXCLUSIVEACQUIRED` followed by a successful retry
is normal — something else grabbed the device briefly.

## Where things live

```
src/AB9ActiveShifter/
  AB9ShifterPlugin.cs      SimHub shell: IPlugin/IDataPlugin/IWPFSettingsV2/IReusable,
                           properties, events, actions, profile management, settings load/save,
                           DataUpdate -> TelemetryState for the effects
  ShifterSettings.cs       Persisted POCO (INotifyPropertyChanged) -> ToEngineConfig()
  ShifterProfiles.cs       ProfileStore (named settings + active + the rig's own facts), legacy
                           migration, cloning, the preset fork
  DefaultProfiles.cs       The five presets every install carries, as deltas from bare defaults,
                           plus the reserved name prefix that marks them (see "Shipped
                           profiles" below)
  ProfileTransfer.cs       One profile as a shareable file: what travels, and what is refused
  PluginInfo.cs            The build's version string, for the UI and for exported files
  Core/                    Pure, no I/O, fully unit-tested
    EngineConfig.cs        Immutable per-tick config snapshot + every default value
    GateGeometry.cs        Column targets, hysteresis bands, gear map, unit conversions
    GateStateMachine.cs    Neutral / Traveling / Engaged with hysteresis and resync
    SequentialStateMachine.cs One shift per stroke, engage/release hysteresis, resync
    PrndLane.cs            Where an automatic's four positions are, what they are called, and
                           which button each holds. Not GateGeometry with one column - see its
                           header for why that was refused
    PrndStateMachine.cs    Which position is held. No neutral, no travelling, no debounce
    ForceComposer.cs       The gate itself: position+velocity -> forces. The heart.
    EffectComposer.cs      Telemetry -> vibration carriers + the clutch grind decision
    TelemetryState.cs      Immutable game-telemetry snapshot, data thread -> engine thread
    ClutchTypes.cs         Where the clutch is read from, and how it decides the grind
    AxisCalibration.cs     A pedal's measured travel/direction/slack -> 0..100, SimHub's scale
    AxisCapture.cs         "Press the pedal you want": three-phase auto-detect, no I/O, no timers
    PedalDeviceInfo.cs     One controller as the pedal picker shows it
    PolarityCalibrator.cs  Measures effect polarity on hardware
    ShifterEngine.cs       The 1 kHz thread, phases, watchdog, reconnect, config swap
    RetryBackoff.cs        "Do not try that again yet" - the gate on I/O the tick can attempt
                           and fail. A throttled log is not one; see its header
    VelocityEstimator.cs   Position -> speed across a 4 ms window; per-tick differences alias
    TraceRecorder.cs       Per-tick ring buffer -> CSV, so a feel complaint can be replayed
    VJoyDeviceInfo.cs      One vJoy device as the picker shows it, and the sentence describing it
  Device/                  DirectInput and Win32
    FfbDevice.cs           Open by VID/PID, exclusive+background, poll
    PedalDevice.cs         The clutch pedal's own handle. NON-exclusive by design - the game
                           needs those pedals too - and read-only; it never creates an effect
    EffectSet.cs           The five effects; one force write per tick, fault handling
    NativeMethods.cs       timeBeginPeriod + high-resolution waitable timer
  Output/VJoyGearOutput.cs vJoy behind IGearOutput (the wrapper is x86-only)
  Output/VJoyDeviceProbe.cs Enumerates vJoy devices for the picker. The one vJoy caller off the
                           engine thread, and query-only - read its comment before adding another
  UI/                      SettingsControl.xaml (Setup/Feel/Effects/Geometry/Monitor)
    GateVisualizer.cs      The gate plan view with the live stick position, on Monitor and again
                           at the top of Geometry. Draws the gate's real free space, the mouths
                           and the engage/release notches, so every geometry dial moves something
    ForceGraphVisualizerBase.cs Shared scaffolding for every visualization: axes, labels, the
                           33 ms snapshot poll, and the redraw-only-if-moved gate
    DetentCurveVisualizer.cs      The whole stroke into a gear against depth from centre, the
                                  landing and the end-stop included; the sequential lever's own
                                  spring when that is the pattern
    GateWallCurveVisualizer.cs    The neutral tunnel's fore/aft wall against depth
    SlidingAcrossGateVisualizer.cs Lateral field across the whole gate width
    SlotMouthVisualizer.cs        The corridor a mouth opens as the slot is approached
    PrndLaneVisualizer.cs         The selector lane's force across the whole of travel
    InverseBooleanToVisibilityConverter.cs  The negation the raw/percent toggle needs
tests/AB9ActiveShifter.Tests/
  ForceComposerTests.cs    Force shape, stability properties, polarity, clamps
  EffectComposerTests.cs   Carrier amplitudes and gain cap, staleness cut, grind conditions
  GateStateMachineTests.cs Transitions, hysteresis, lockout traces
  PolarityCalibratorTests.cs Two-axis stick model incl. this unit's mixed inversion pattern
  RetryBackoffTests.cs     A failing device open cannot be attempted once per tick, counted
  VelocityEstimatorTests.cs  Feeds the measured stale-then-jump report stream, demands a steady answer
  SequentialTests.cs       One-shift-per-stroke, re-arm, mirror, spring shape, click kick
  PrndTests.cs             The selector: always in exactly one position, buttons above every
                           other range, and no axis count anywhere that steps the lane's force
  SettingsMappingTests.cs  ShifterSettings' derived dials (ThrowFromCentre moves both threshold
                           lines)
  SlotEndStopTests.cs      The bottom of an H slot: the default changes nothing, the landing is
                           free, the wall only pushes home, and no axis count steps the stroke
  DefaultProfilesTests.cs  The shipped profiles: forces off, cap on, store coherent, and the
                           three H presets one tune differing only in where the slot ends
  ProfileTransferTests.cs  Round trip, machine facts kept, every clamp on the import path
  AxisCaptureTests.cs      A pedal wired backwards, one that rests at full scale, a silent device
  ClutchModeTests.cs       Threshold mode is byte-identical to what shipped; the bite-point pulse
  ProfileCycleTests.cs     What a bound Next/Previous hotkey walks through, incl. stale names
  ProfileStoreTests.cs     Car-model matching, and the gate that keeps it off the telemetry path
  PresetProfileTests.cs    The reserved prefix holding on every path a name arrives by, and the
                           fork that keeps the settings object a slider is mid-drag on
  MachineFactsTests.cs     That switching profiles cannot change what the rig measured, and that
                           moving those facts leaves every tuned dial alone
  ProfileSwitchTransitionTests.cs  Easing the new gate in, and the confirmation thump's count
  ForceOutputHealthTests.cs  What the base's status flags mean, and what it takes to convict
  VJoyDeviceInfoTests.cs   What the device picker says, including the too-few-buttons trap
build/refs/                Reference-only stubs of SimHub's assemblies, so the plugin builds
                           on a machine with no SimHub. Read build/refs/README.md before
                           touching one - a wrong signature builds green and throws on the rig
tools/Verify-StubBuild.ps1 Proves a stub-built DLL binds against the real SimHub. Needs a
                           local SimHub install; run it before tagging a release
tools/Show-ProfileDeltas.ps1 Turns a tuned settings file back into DefaultProfiles.cs
                           assignments, for refreshing what a fresh install starts with
.github/workflows/         ci.yml (format, build, test, artifact on every push and PR) and
                           release.yml (manual, versioned, tags and publishes)
```

`Core/` stays free of I/O deliberately: the vJoy wrapper is a 32-bit native DLL that test
runners cannot load, so anything worth testing must not touch it.

## Invariants — breaking these has already caused a bug once

**Force and polarity**

- Walls are **constant forces, never springs.** A DirectInput spring on this base tops out
  around 0.3 DI units per axis count, which cannot produce a wall at any coefficient. A test
  pins this; if it fails, the gate has silently gone soft again.
- Compose in the gate's own frame and apply the four measured polarity signs **once, at the end
  of `Compose`.** The yield and shaping stages compare force sign against velocity sign; doing
  the flip earlier makes them compare unlike things.
- Polarity is **per axis, not one global flag** — this unit inverts constant force on X and not
  on Y. Only the two constant-force flags exist, because every wall is a constant force and every
  frame ships both springs as `Off`. The calibration still probes all four (constant/spring × X/Y)
  and all four must read conclusively before the cap lifts: the spring probes are a device sanity
  check, not settings. The measured pattern on this unit is genuinely mixed — spring inverted on Y
  where constant force is not — so if a spring ever drives the gate again it needs its own flags
  back, not a reuse of the constant ones.
- **Overall gain is capped at 10% until polarity is confirmed.** That cap is the safety story
  for an unmeasured base; do not add a path around it.
- Damping joins **after** the yield and the time shaping, and is never slewed. It opposes motion
  by construction, so it is never the assisting force the yield exists to soften, and rate
  limiting the stabiliser would defeat it.
- **Wall friction joins beside damping, and its normal load is the shaped gate force — never the
  carrier.** It is a share of the force currently applied on that axis, so it is exactly zero in
  free travel (the lightness rule survives it), it winds up with the attack instead of stepping,
  and it is viscous below its knee so it cannot be a relay at tremor speed. It exists because the
  sub-deadband band otherwise has no dissipation at all — no cut is allowed there, and that band
  hunted on the face gradient (17.7 Hz, traced) the moment the yield relay was fixed. Pinned by
  `FrictionIsZeroEverywhereTheLeverIsFree` and `FrictionIsContinuousThroughZeroVelocity`.
- **Telemetry vibration joins at the same point, and never passes through the stabilisers.** A
  carrier is keyed on time, not position, so it cannot form the loop the yield and attack
  stabilise — and the yield would chop a zero-mean carrier every half cycle (the grinding-bug
  texture, made deliberately). It stays inside the final clamp and the polarity signs, its
  budget is capped (3000/4500/5000 DI in `EffectComposer`) and scaled by the effective gain, the
  10% polarity cap included. **Stale telemetry (>500 ms) silences every effect the same tick** —
  a hung game must not leave a buzz running. **The grind never touches geometry**: rejection is
  `allowEngage` into the state machine plus the balk-wall detent (entry resistance +
  `GrindWallPct`, no crossover, attack-shaped like the wall it has become), never a moved or
  closed wall (see the rejected table in docs/force-model.md).
- **Velocity is never an adjacent-tick difference, and the absorber's scale is one-way in time.**
  Under write contention the device delivers distinct positions at only ~500 Hz, so per-tick
  differencing alternates ~2:1 and anything keying force on it renders a 250–500 Hz ripple —
  measured at 25–50% of the wall force, felt as grinding against a running gear. Positions are
  differenced across a 4 ms window (`VelocityEstimator`), and the yield cuts instantly but
  recovers over `YieldRecoveryMs` — recovery slew only ever deepens absorption, because the
  same-direction test already restores full force the instant the wall resists. Pinned by
  `AStaleThenJumpStreamReadsAsItsTrueMeanSpeed`, `AnAliasedSpeedEstimateCannotGrindTheWall`,
  and `TheAbsorberCutsInstantlyAndRecoversSlowly`.
- **The yield's deadband classifies lean against launch, and inside it the force is one
  continuous value.** The deadband sits above the measured speed of a hand adjusting a lean
  (~3700 counts/s) and below deliberate strokes — at tremor level it fired a fresh cut on every
  micro-reversal and the absorber became a relay oscillator (26 Hz chatter in a slot, a 12 Hz
  rebound off the lockout, both traced). Sub-deadband ticks get the HELD scale — never a fresh
  cut, never an instant restore, whichever way tremor points — because restoring whole on an
  estimate dip strobes a held cut at the report rate. The static hold's stillness test is a
  separate tremor-scale constant, not this deadband: sharing it would freeze real slow retreats
  into force steps. Pinned by `AHandsTremorNeverTripsTheAbsorber`,
  `TheLockoutHoldsWholeAgainstALeaningHand`, and `AnEstimateDipBelowTheDeadbandKeepsTheHeldCut`.
- Time shaping (wall attack) applies to **everything a hand can lean on, the lockout included.**
  The slot detent is the one exception — the snick must arrive whole. Do not exempt the lockout
  again: it was tried, on the theory that slewing a crossing discounts a flick, and the arithmetic
  refutes it (crossing takes tens of ms, the attack lasts ~15) while the cost was the lockout being
  the only force still arriving raw, rejecting the lever hard and ringing.
- The static hold band is **proportional** to the force being applied. A fixed band sized for a
  full-strength wall swallows a light guide force whole and makes sliding across the gate notchy.

**Geometry and state**

- Positions stay in **device coordinates end to end.** Layout preferences (`MirrorColumns`,
  `MirrorSlots`) relabel the *gear map* only. Mirroring the readings instead would put every
  force anchor on the wrong side of the gate and turn holds into repellers.
- **A missing slot is a fact of the gear map, nowhere else.** `GearFor` returns 0 for it,
  `SlotExists` follows the map, and everything derives from that one answer: the wall over it
  never opens (the block factor is direction-keyed, safe only because the fore/aft force crosses
  zero at the channel centre), the mouth shaping skips it, and the state machine refuses to latch
  it. Because the hole lives in the map, mirroring relocates it with the gears. Do not encode a
  missing slot anywhere as a position — it would stay behind when the layout flips.
- **A latched gear is released only by returning through the neutral channel — absolutely.** No
  lateral distance changes or drops it, and there is deliberately no fault threshold. Force cannot
  enforce a gate: a hand beats 12 Nm, so any distance at which the latch gave way would be a
  distance at which the rest of the pattern came back and could capture the lever into a gear it
  was never driven into. Do not reintroduce a lateral release — it is also what frees the slot
  wall's face from its old exit-band squeeze, so restoring one would mean re-clamping that ramp.
- **The lateral field is faded to zero wherever the guide can change hands, and that fade is a
  MULTIPLIER on position alone.** A nearest-column field reverses at every column boundary, so a flat
  plateau held up to the boundary makes the reversal a step of twice the plateau — measured at 20000 DI,
  a clamped ±12 Nm, from 100 counts of drift, and felt as the notches kicking while sliding the tunnel.
  `GateGeometry.HandoverClearance` is the window: the barrier crests, ± the hysteresis `Pick` biases
  them by. Do not reimplement it as a limit on the guide's *reach*: a reach belongs to whichever column
  owns the field, so the latched and position-picked branches then disagree by the full pin force
  exactly where a flat plateau had made them identical (10000 DI of invented history dependence,
  measured). A shared scalar cannot, because the field is `F_old(history) × Relief(position)`. Three
  tests pin this: `NoSingleCountOfDriftEverStepsTheLateralField`,
  `NoSingleCountOfDepthEverStepsTheLateralField`, `TheReliefWindowCannotInventHistoryDependence`.
- **The guide column changes hands only in the tunnel, and the window is faded out with depth.** The
  window pays for a handover; applied at every depth it also holes the slot walls, and a latched gear's
  wall is the whole enforcement of the absolute lock. Measured: a gear held at full deflection had
  **exactly 0 DI** of lateral wall at each gap, and under 500 DI for 1.8 s at one of them — felt on the
  rig as extra half-slots that do not change gear. `GuideColumn` therefore returns the column already
  held whenever the lever is out of the tunnel, and `Relief` rides `SlotConfinementFactor`, so the
  window is whole through every depth a flip can still happen at and gone through every depth a wall
  has to hold. Do not make the fade discontinuous in depth and do not make the pick live again below
  the tunnel: the first moves the old reversal onto the depth axis (2403 DI from one axis count), the
  second needs a second boundary rule and brings the holes back. Freezing also closes the lockout
  bypass that the crest/midpoint split existed for. `TheHandoverWindowIsSpentByTheTimeTheTunnelIsLeft`
  and `ALatchedGearKeepsItsWallAcrossTheWholeGate` pin it.
- **Every position in the gate belongs to a column, and it is the one the wall opened for.**
  `ColumnAt` is the nearest column, boundaries at the gap midpoints, never `None` — the same ownership
  `ChannelBlockFactor` measures the wall's mouth from. A narrower capture band leaves an annulus where
  the gate is passable and there is no gear to select, and past it the wall is only 12 Nm, which a hand
  beats: measured as two pushes to **full deflection**, 896 ms and 616 ms, ~2400 counts off the column,
  state `Neutral`, gear 0 — the lever shoved fully home and the game told nothing. A silent non-shift is
  the worst answer this gate can give. What a slot *holds* is still a fact of the gear map alone, so a
  missing slot refuses across the whole of its column's territory. `PlaceLockout` clamps the gate's
  crest to the main section's side of its gap midpoint for the same reason — otherwise ownership could
  hand a lever 7/R short of the gate, with the toll unpaid.
- **The lateral field must not read the state machine.** It is one function of position and the
  guide column, called by both branches; `TheLateralFieldDoesNotDependOnTheLatch` sweeps every
  column, direction, position and depth and demands exact equality. Computing it per-branch produced
  a 4924 DI (≈6 Nm) step at the same position, selected by history through the hysteretic channel
  bands, and that step was the mouth oscillation.
- **One stiffness for every lateral force.** Faces are derived from plateaus (`GuideFace`), so a
  gentler force gets a shorter face, never a steeper one. Never give a lateral force its own ramp
  dial — that is what made the funnel 3.5× the wall face.
- **Depth spans are not lateral spans.** Do not reach for `WallRamp` when you need a depth distance.
- **A mouth shape may only ever remove force.** The shapes move the corridor's edge; nothing pushes
  outward, which is why they cannot self-excite. `TheMouthOnlyEverRemovesForce` sweeps the gate and
  demands the shaped force is never larger nor opposite in sign to the square one. `MouthSlopeMax`
  bounds every flank at half the wall face and is not a user dial.
- **The guide's column boundaries are the barrier crests, and they are only consulted in the tunnel.**
  A live pick at depth turns the lockout's own wall into a conveyor toward 7/R and the toll is never
  paid — which is why the boundaries used to be midpoints below the tunnel, and why they no longer need
  to be. Out of the tunnel the pick is simply the one already held; the fallback for a pick that does
  not exist yet is `ColumnAt`, so a cold start is pushed toward the column that is about to claim it.
- Barriers fade out with depth as the slot walls fade in, and they are applied in **both** branches —
  anything indexed on the state machine puts the step back.
- **The lockout gate positions itself** against the last main-section column
  (`GateGeometry.LockoutCentre`), and the lateral guide's column boundaries are the barrier
  crests, not the geometric midpoints. Anything that needs to know where the lockout is must ask
  the geometry — a second copy of that position silently drifts (the Monitor tab's shading used
  to be exactly that bug).
- Slots and the neutral channel are **corridors with walls** by default, not pulls toward a
  centre line. A restoring force about an interior equilibrium is an oscillator; that is what made
  the middle columns shake while the outer ones (one-sided against the end of travel) were fine.
  Both free widths are dials (`SlotHalfWidth`, `ChannelFreeDepth`) and **zero is a supported
  setting** — the rail gate, the native shifter-mode topology, one axis guided everywhere (see
  docs/force-model.md). Closing a corridor brings the interior equilibrium back, so rails are
  only stable at moderate strengths with the full stabiliser stack; a railed column that trembles
  wants its force lowered, never damping raised. `ChannelFreeDepth` is clamped to
  `ChannelHalfEnter` from above — a force deadband wider than the state band would be walls the
  state machine believes exist and the hand never meets. The one sanctioned pull-toward-a-place
  is the **home spring** (`HomeSpringPct`, default 0): dead across the home column's width so
  the equilibrium is a region not a point, one-stiffness face with no upper clamp (a spring
  stronger than the pin gets a longer face, never a steeper one), flat plateau beyond, faded
  with depth like the humps, anchored to the map's gear-column 1 so mirroring moves it. It has
  the rail's hunt ceiling: a lever trembling at home wants the spring lowered, never damping.
- **A slot's bottom is a corridor too, and the same rule applies fore/aft.** `SlotStopForcePct`
  gives an H slot an end of its own instead of running on to the base's mechanical stop — which is
  the whole of what a short throw is, because `EngageDepth` alone only moves where a gear
  *registers* and the seated hold then drags the lever the rest of the way anyway. Past the seat
  the hold fades out over one wall bite, the landing carries **no fore/aft force at all**, and only
  then does the wall rise. Do not "simplify" that into a hold pressing the lever against the stop:
  that is a restoring force about an interior equilibrium, the thing corridors exist to avoid. It
  is only sound because the base does not self-centre with Cockpit's Spring at 0, so nothing pushes
  the lever out of the landing — which is also why the default is off. The landing is floored at
  the wall bite for the same reason the tunnel pair has `MinBandSpan`: a full-strength hold removed
  across one axis count is a bang, not a face. The floor is reported by `StrokeStopDepth` rather
  than applied silently. Pinned by `TheLandingIsFreeSoASeatedGearRestsInARegionNotOnAPoint`,
  `TheLandingCanNeverBeShorterThanTheWallBite` and `NoSingleCountOfDepthEverStepsTheSlotForce`.
- **A PRND detent is zero at its position AND zero at the crest beside it.** The lane's force is
  measured from the *nearest* position, and every nearest-anything field flips at the midpoint —
  which on the lateral axis was a step of twice the plateau and cost `HandoverClearance` to pay
  for. Here it is free instead of relieved, because the raised cosine is already at nothing where
  the flip happens. Do not replace it with a pull toward the nearest position, however much
  simpler that reads: it puts the reversal straight back, at full detent strength, three times
  along the lane. The notch either side is the same free-region rule slots follow, and
  `PrndNotchHalfWidthCeiling` keeps the hump beside it at least π wall bites wide so its steepest
  point is the wall's own stiffness (a raised cosine peaks at π/2 times its average — the rounded
  mouth's 2/π factor, from the other end). Pinned by `NoSingleCountOfTheLaneEverStepsTheForce`,
  `TheDetentIsNothingAtEveryPositionAndNothingAtEveryCrest` and `TheNotchCannotBeWidenedIntoAStep`.
- **A selector is always in exactly one position, and its buttons live above every other range.**
  There is no neutral to fall into: `PrndStateMachine` holds an index from the first reading and
  only ever hands it to another, so a game can never see a moment with nothing selected. P/R/N/D
  take buttons 11–14, above the gears (1–8) and the sequential pulses (9–10), and the button
  follows the **label** rather than the slot, so `MirrorSlots` turns the lane round without costing
  a rebind — the same rule that pins reverse to button 8 in every H pattern. `VJoyGearOutput`
  carries them on the ordinary `SetGear` path deliberately: that is what gives a position the same
  release-before-press, the same watchdog clear and the same shutdown ordering a gear gets.
- **A band a force ramps across gets a width, not just an ordering.** `GateGeometry` repairs
  inverted enter/exit pairs, and for a pure state band `enter + 1` is the right repair. The neutral
  tunnel pair is not a pure state band — `GuidePlateau` and `SlotConfinementFactor` ramp force
  across it — so `+ 1` turns a typo into a full-scale cliff: measured at 0 DI on one axis count and
  10000 DI on the next, a ±12 Nm square wave at the report rate from sensor dither alone. Its floor
  is `GateGeometry.MinBandSpan`, and the two dials that produce it sit adjacent in the UI differing
  by one word, so this *will* be typed wrong again. Pinned by
  `AnInvertedTunnelPairCannotBecomeAForceCliff`. Before clamping any new pair, ask whether a force
  ramps across it.

**Safety ordering**

- Gear change: **buttons before forces.** A game must see the gear at least as early as the hand
  feels it. Sequential pulses obey the same order, and re-firing a button that is still down
  inserts a ≥20 ms released gap first — an off-and-on inside one tick reads to a game's input
  poll as one continuous press. A pattern or profile switch clears any pulse in flight along
  with the held gear.
- Shutdown, device loss, disable, `FinalizePlugin`: **buttons off → stop forces → unacquire**, in
  that order, always. A gear must never stay stuck down.
- The watchdog (500 ms timer, 1 s staleness) calls `EmergencyStop`. `StopForces` is the only
  device method callable off the engine thread, and it swallows everything.
- **A repair keyed on this base's status flags needs a second source, and never touches the gear.**
  The base sets `DIGFFS_EMPTY` while producing force — measured, held over forty minutes with no
  other fault flag — and `DIGFFS_STOPPED` at rest, which is why `Idle` is not a fault. Since
  `EffectsGone` now *rebuilds* the effects, a detector believing the flag alone would destroy
  working force once a second; `Classify` therefore takes the device's claim and
  `EffectSet.AnyStillDownloaded()` as separate arguments and needs both. `RebuildEffects` leaves
  the held gear alone: the lever has not moved, so dropping the button would turn a loss of feel
  into a loss of drive. And a fault must **put the status back on recovery** — writing only the
  fault sentence left it outliving the fault and hiding every status after it.
- **A picture of the gate samples `ForceComposer`, never a copy of its arithmetic.** The Feel tab's
  curves and the gate plan view both exist to answer "what will this dial actually do", so a
  drawing that re-derives the shape is the one place it can quietly stop matching the gate — and it
  would mislead precisely when someone is using it to diagnose a feel problem. That covers plan
  geometry as much as force: `GateVisualizer` gets each slot's corridor from
  `SlotCorridorHalfWidthAt` rather than re-deriving the mouth, and asks `GateGeometry` where the
  lockout is rather than keeping a second copy of the position (the Monitor tab's shading used to
  be exactly that bug). Every one of them builds its **own** `ForceComposer` from
  `Settings.ToEngineConfig()`, never the live engine's: it must render with the plugin disabled,
  when no engine thread exists, and reaching into the running one would be a cross-thread read of
  engine state. Where a graph needs an internal shape, make the method public with a comment
  saying a graph calls it — `Saturating`, `LateralGuide`, `BarrierForceIn`, `DetentMagnitude` and
  `SlotCorridorHalfWidthAt` are all public for exactly that reason. One copy already drifted into
  the codebase and was removed; do not reintroduce one because it is only three lines.
- **The tuning tabs are hidden until polarity is measured and a vJoy device is available**, and
  everything needed to satisfy both conditions lives on the Setup tab *by construction* — the vJoy
  picker and the base's vendor and product ids among them. Moving one of those behind the gate
  would lock a user out of the control that opens it: a base that enumerates differently cannot be
  calibrated, and the ids that would fix it would be on a tab that calibration is what reveals.
  Before putting anything on Feel, Effects, Geometry or Monitor, ask whether a user could need it
  in order to finish setup.
- **The pedals are opened NON-exclusively, and nothing but the base is ever taken exclusive.**
  The base is exclusive because creating force feedback effects requires it. A pedal set is not:
  the game is reading those pedals too, and an exclusive grab would silently take the clutch away
  from whatever is being driven. `PedalDevice` never creates an effect and never writes — it is a
  reader. The same rule covers the picker: `PedalDevice.Enumerate` opens each device briefly,
  sets no cooperative level, and disposes it.
- **Any I/O the tick can attempt and fail needs a `RetryBackoff`, and a throttled log is not one.**
  Opening a DirectInput device that is not there costs milliseconds — measured at roughly 12 of
  them — and the tick has one. The clutch pedal's open ran unguarded on every tick while the device
  was missing, which is what an unplugged pedal set or a saved binding for hardware this machine no
  longer has looks like. Measured: **81 Hz against the 990 the same rig runs otherwise**, and every
  stability argument in this project is made from that loop rate. Its log line *was* throttled, to
  thirty seconds, and that is precisely what hid it — the cost was paid a thousand times a second
  and mentioned once in thirty thousand. The poll beside it had been rate-limited from the start
  (`PedalPollEveryTicks`) with a comment saying why; the open never was. When adding anything the
  tick can retry, gate the attempt, not the message.
- **The clutch enters the system at exactly one place, and it is a number, not a source.**
  `TelemetryState.Clutch` is written once per tick and read once, by `EffectComposer`. A directly
  read pedal is substituted into a scratch snapshot the engine owns (`CopyFromWithClutch`) — never
  into the published one, which the data thread is still writing and other readers expect whole —
  and it allocates nothing, because this is the 1 kHz path. Anything else wanting the clutch reads
  that one field rather than asking where it came from.
- **The bite point is a property of the car, not of the pedals.** Travel, direction and slack are
  measured by `AxisCapture`; the bite point cannot be, so it is a setting. Do not try to infer it
  from a press — nothing in the pedal's motion marks it. It is deliberately not owned by the
  grind: it is the one point on a clutch's travel that means anything mechanically, so a second
  effect that wants it asks `ClutchBitePointPct` rather than growing its own dial.
- **A fact about the rig is stored once, on the store — never per profile.** Measured polarity and
  the invert flags, the device and vJoy ids, `TickHz`, and the whole clutch pedal binding live in
  `ProfileStore.Machine` and are stamped onto whichever profile is activated
  (`ProfileTransfer.CopyMachineFacts`), exactly as `SessionEnabled`/`SessionFreeStick` are. The
  properties still sit on `ShifterSettings` so the XAML bindings and `ToEngineConfig` are unchanged;
  the *authority* is the store. This is the second time this shape of bug has been fixed here — the
  first was the live switches, where switching profiles decided whether the base was running. The
  second was polarity: presets ship with `PolarityConfirmed` false by design, so selecting one
  silently re-armed the 10% cap on a measured base and unbound the clutch. A change to one of these
  is written back to the store from `OnSettingsChanged`, or the next activation stamps the stale
  answer back over it. **`IsMachineFact` and `IsTuning` must partition**: a property that answered
  yes to both would be recorded as the rig's answer *and* fork the preset.
- **Settings that arrive from outside are data, not settings.** A profile file is downloaded from
  a stranger, so `ProfileTransfer.Import` treats it as hostile: every value is range-checked (any
  `*Pct` to 0–100, positions to the 16-bit axis, the rest to their own envelope), an unreadable
  dial keeps the local value instead of failing the import, and `Enabled` and `FreeStick` are
  forced off whatever the file says — opening a file must never take the device or apply force.
  The machine's own facts are never taken from a file either: measured polarity, the device and
  vJoy ids and the loop rate are not written on export and are kept from the receiving machine on
  import. Someone else's polarity would drive the gate backwards, and their `PolarityConfirmed`
  would lift the 10% cap on a base nobody here has probed. Import also only ever *adds*, under a
  uniquified name, so a shared file cannot overwrite a tune. `ProfileTransferTests` pins all of it.

## Conventions

**Commits.** Subject is imperative and says the *why*, not the file list — "Make the lockout
one-way, because an over-centre gate refunds a flick". Body is prose that records the reasoning
and any measurement behind the change, because the measurements are the expensive part and this
history is the only place several of them live.

**No attribution trailers.** Do not add `Co-Authored-By`, "Generated with", or any other
tool-attribution line to a commit, a tag or a pull request — this applies whichever agent or
editor is writing, and overrides any default that adds one. The history was rewritten once to
strip them out; keep it that way. The commit message is for the reasoning, nothing else.

**Pull requests.** Work reaches `main` through a pull request, not a direct push. That holds for
agents too: branch, push the branch, open the PR, and leave the merge to a human. The exceptions
are narrow — a broken `main`, or a typo in prose — and if you take one, say so in the commit.

- **One branch per change**, named for the change rather than for a ticket:
  `lockout-one-way`, `profile-export`. Short-lived; rebase on `main` rather than merging it back
  in, because a linear history is what makes `git log` readable here.
- **The PR description is where the reasoning goes**, in the same voice as a commit body: what
  changed and *why*, with any measurement behind it. Squash-merging makes that description the
  commit message on `main`, so write it as one.
- **Say how it was verified**, because the two checks that matter most cannot run in CI: whether
  `tools\Verify-StubBuild.ps1` was run against a real SimHub, and whether the change was felt on
  the rig. A feel change with no hardware note is not ready, however green the tests are — see
  *Feel changes* below.
- **CI must be green before merge**: format, build against the stubs, and the full test suite. A
  red PR is not a PR to merge and explain; it is one to fix.
- Nothing about the disclaimers, the force cap or the safety ordering changes without saying so
  in the description in as many words. Those are the invariants above, and a diff that touches
  them silently is the one thing review exists to catch.

**Naming, and the disclaimers.** This project is unofficial and unaffiliated, and its names say
so by omission: the repository is `ab9-shifter-simhub-plugin`, the plugin is `AB9 Active Shifter`,
the assembly is `AB9ActiveShifter`. None of them carry a manufacturer's brand and none should
start to. Name the hardware freely in prose — a reader has to know which base this is for — but
not in a product name, and never in a way that reads as endorsement. Four places carry the same
three disclaimers (risk, unofficial, early software): `README.md`'s *Read this first*,
`NOTICE.md`, the Setup tab's `ABOUT` section, and the notes block in
`.github/workflows/release.yml`. They are deliberately redundant, because each catches a reader
the others miss — change them together or they drift.

**PowerShell and git.** `git commit -m @'…'@` does not work in this environment — the quotes are
parsed as pathspecs. Write the message to a scratchpad file and use `git commit -F <file>`.

**Saved settings.** SimHub keeps them at
`C:\Program Files (x86)\SimHub\PluginsData\Common\AB9ShifterPlugin.GeneralSettings.json`. Edit it
**only while SimHub is stopped** — it is rewritten on exit. Changing a default in
`EngineConfig.cs` does not change a user who already has that key saved; patch the JSON too, and
say so. **Deleting that file does not give you a machine with no settings**: SimHub keeps ten
rolling copies in `_Backups\` beside it and restores the newest when the primary is gone, so a
first-start test that only deletes the primary silently measures the old settings. See
DEVELOPMENT.md for the command that clears both and the two log lines that prove it worked.

**Shipped profiles.** The five tunes in `DefaultProfiles.cs` are **presets**: rebuilt by
`EnsurePresets` on *every* start, not just the first, so they are always present and always
current. They are written as *deltas* from a bare `ShifterSettings`, so the tuning reads as
tuning and a dial that gains a better default inherits it. This used to be a JSON file copied in
by `install.ps1`; it is code now because renaming a setting silently drops its value from a JSON
and breaks the build here. To refresh after a retune, run `tools\Show-ProfileDeltas.ps1` (SimHub
stopped) and paste its output — **except** `Enabled` and `PolarityConfirmed`, which must stay at
their defaults: forces ship off, and polarity is a per-unit measured fact the 10% cap exists to
guard. `DefaultProfilesTests` fails if either creeps in.

- **The name prefix is reserved, and that is the whole design.** A preset is named
  `(Preset) <bare name>`, and `ProfileStore.UniqueName` strips that prefix off every name a user
  can supply — add, rename, import all mint names through it. Because the two sets cannot collide,
  presets are rebuilt unconditionally with **no migration step and no risk to anything tuned
  here**: an install that predates them keeps every profile it had and simply gains the preset
  block at the end. Do not weaken the reservation. Without it a file from a stranger could arrive
  named `(Preset) 7+R lockout`, be treated as immutable, and be silently replaced on the next
  start — a shared file deleting a tune, the one thing `ProfileTransfer` exists to prevent.
- **A prefixed name this build does not recognise is left alone, not deleted.** `BareOf` checks
  against the names actually shipped, so a downgrade cannot eat a preset a later build added.
- **Editing a preset forks it, and the fork keeps the live `ShifterSettings` object.**
  `ForkPreset` renames the profile *around* the object and re-inserts a freshly built preset in
  its place. It is not a clone-and-swap because the settings page binds its `DataContext` to that
  object and a fork fires from the first change of a dial — very often the first pixel of a slider
  drag, whose remainder would then write into a profile no longer in the list. The replacement is
  built by the factory rather than copied, because by then the edit has already been applied and
  there is no pristine copy left in memory.
- **Only tuning forks.** `ProfileTransfer.IsTuning` is the test, and it is the same `NotShared`
  list a shared file refuses — one question, one answer. Polarity calibration writes its measured
  result through the same notification a slider does; a preset that forked itself when calibration
  finished would be an unasked-for copy holding the one flag that lifts the 10% cap.

**Hardware claims.** Measure, do not assume, and write the number down in
[docs/hardware.md](docs/hardware.md) with how it was measured. Several plausible assumptions in
this project turned out to be false (see that file's "disproven" section). Ad-hoc probes go in a
scratchpad project outside the repo, run with SimHub stopped so the device is free.

**Feel changes.** The human at the stick is the instrument for anything about feel. Land the
change, deploy it, and say what to try and what to look for — do not conclude a feel problem is
fixed from arithmetic alone.

## Keeping this documentation current

Treat docs as part of the change, not a follow-up. When you touch something in the left column,
update the right column **in the same commit**:

| Change | Also update |
| --- | --- |
| Any default in `EngineConfig.cs` / `ShifterSettings.cs` | `docs/tuning.md` if it is a dial a human turns; the XAML `ResetValue` for its slider; the saved JSON if the user has the old value |
| Force shape, stability mechanism, or a new dial | `docs/force-model.md` — including approaches you *rejected* and why |
| A measured hardware fact, or a claim proven false | `docs/hardware.md`, in the table or the "disproven" section |
| Threading, lifecycle, effect handling, safety ordering | `docs/architecture.md` and the invariants above |
| Files added, moved, or renamed | the code map above, and the short one in `DEVELOPMENT.md` |
| Setup steps, requirements, or anything a user does once | `README.md` |
| Build, test, deploy or release procedure | `DEVELOPMENT.md`, and the build section above |
| A dial added, renamed or removed | `DefaultProfiles.cs` if the shipped tuning names it, and check whether it should travel in `ProfileTransfer.NotShared` |
| **Any label, section or control in `SettingsControl.xaml`** | `docs/tuning.md`, which **quotes UI labels verbatim** so a human can read the two side by side. The XAML is the authority: when they disagree the doc is wrong, and a doc that names a dial the UI does not have sends someone hunting for a control that is not there. This has already happened — the doc said *"Slot wall, once in a gear"* long after the UI said *"Slot wall / lateral rail"*. A new control also needs a home on the right tab: everything required to finish setup must stay on Setup (see the tab-gating invariant) |
| The risk, non-affiliation or early-software wording | all four copies at once — see *Naming, and the disclaimers* |
| A new tuning symptom you diagnosed | the symptom table in `docs/tuning.md` |

Two rules that matter more than the table:

1. **Delete what is no longer true.** A stale doc is worse than no doc — the README claimed for
   weeks that the plugin disables the base's autocentring, which is false and sent debugging
   down the wrong path more than once.
2. **Record the failed attempts.** Most of the cost in this project has been re-deriving that
   something does not work. If you tried an approach and it failed, that belongs in
   `docs/force-model.md` permanently, with the symptom that ruled it out.

## Project state

Working and verified on hardware: device acquisition, polarity measurement, the 1 kHz loop, the
full gate (walls, corridors, barriers, one-way lockout, slot detents), gear detection, vJoy
output, the settings UI, and the reset/free-stick escapes.

Telemetry effects shipped in v1 form — the clutch grind with gear rejection, engine vibration,
rev limiter, ABS/TC, the shift pulse, and the custom-property bridge to ShakeIt — awaiting
verification against a real game. Still open: end-to-end vJoy verification in a game, a safety
soak (forced-hang watchdog test and reconnect cycling), and a synchro/rev-match model for the
grind (today it is a threshold on the clutch, not a speed-difference model).
