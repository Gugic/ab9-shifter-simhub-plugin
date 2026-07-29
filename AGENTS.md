# AB9 Active Shifter — working notes for agents

A SimHub plugin that turns a **MOZA AB9 flight base** into an **H-pattern shifter** (7+R with a
push-through lockout, 6+R with no 7th slot, 5+R) or a **sequential lever**, selectable per named
profile. It renders the gate with DirectInput force feedback, detects the slotted gear from stick
position, and holds a vJoy button per gear (or pulses up/down buttons in sequential) so any game
sees a normal shifter.

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

197 tests, all green, none touching I/O — `Core/` plus the settings POCO's derived-dial
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
  ShifterProfiles.cs       ProfileStore (named settings + active), legacy migration, cloning
  DefaultProfiles.cs       The three profiles a fresh install writes out, as deltas from bare
                           defaults (see "Saved settings" below)
  ProfileTransfer.cs       One profile as a shareable file: what travels, and what is refused
  PluginInfo.cs            The build's version string, for the UI and for exported files
  Core/                    Pure, no I/O, fully unit-tested
    EngineConfig.cs        Immutable per-tick config snapshot + every default value
    GateGeometry.cs        Column targets, hysteresis bands, gear map, unit conversions
    GateStateMachine.cs    Neutral / Traveling / Engaged with hysteresis and resync
    SequentialStateMachine.cs One shift per stroke, engage/release hysteresis, resync
    ForceComposer.cs       The gate itself: position+velocity -> forces. The heart.
    EffectComposer.cs      Telemetry -> vibration carriers + the clutch grind decision
    TelemetryState.cs      Immutable game-telemetry snapshot, data thread -> engine thread
    PolarityCalibrator.cs  Measures effect polarity on hardware
    ShifterEngine.cs       The 1 kHz thread, phases, watchdog, reconnect, config swap
    VelocityEstimator.cs   Position -> speed across a 4 ms window; per-tick differences alias
    TraceRecorder.cs       Per-tick ring buffer -> CSV, so a feel complaint can be replayed
  Device/                  DirectInput and Win32
    FfbDevice.cs           Open by VID/PID, exclusive+background, poll
    EffectSet.cs           The five effects; one force write per tick, fault handling
    NativeMethods.cs       timeBeginPeriod + high-resolution waitable timer
  Output/VJoyGearOutput.cs vJoy behind IGearOutput (the wrapper is x86-only)
  UI/                      SettingsControl.xaml (Setup/Feel/Effects/Geometry/Monitor) + GateVisualizer
tests/AB9ActiveShifter.Tests/
  ForceComposerTests.cs    Force shape, stability properties, polarity, clamps
  EffectComposerTests.cs   Carrier amplitudes and gain cap, staleness cut, grind conditions
  GateStateMachineTests.cs Transitions, hysteresis, lockout traces
  PolarityCalibratorTests.cs Two-axis stick model incl. this unit's mixed inversion pattern
  VelocityEstimatorTests.cs  Feeds the measured stale-then-jump report stream, demands a steady answer
  SequentialTests.cs       One-shift-per-stroke, re-arm, mirror, spring shape, click kick
  SettingsMappingTests.cs  ShifterSettings' derived dials (SeqThrow moves both threshold lines)
  DefaultProfilesTests.cs  The shipped profiles: forces off, cap on, store coherent
  ProfileTransferTests.cs  Round trip, machine facts kept, every clamp on the import path
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
  `GateGeometry.HandoverClearance` is the window; it spans the **hull of both boundary rules** (crest in
  the tunnel, midpoint below), because keying it on the rule in force at the current depth moves the
  same reversal onto the depth axis — 2403 DI from one single axis count of fore/aft. Do not reimplement
  it as a limit on the guide's *reach*: a reach belongs to whichever column owns the field, so the
  latched and position-picked branches then disagree by the full pin force exactly where a flat plateau
  had made them identical (10000 DI of invented history dependence, measured). A shared scalar cannot,
  because the field is `F_old(history) × Relief(x)`. Three tests pin this:
  `NoSingleCountOfDriftEverStepsTheLateralField`, `NoSingleCountOfDepthEverStepsTheLateralField`,
  `TheReliefWindowCannotInventHistoryDependence`.
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
- **The guide's column boundaries are crests in the tunnel and midpoints below it.** Crests at depth
  turn the lockout's own wall into a conveyor toward 7/R and the toll is never paid. Positional, not
  historical, so a cold start resolves identically.
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

**Shipped profiles.** A fresh install starts from `DefaultProfiles.cs`, not from bare defaults:
`ReadCommonSettings` finding nothing is the first-start signal, the factory returns the three
tuned profiles, and `Init` writes them straight out so they are ordinary settings from the second
start on. They are written as *deltas* from a bare `ShifterSettings`, so the tuning reads as
tuning and a dial that gains a better default inherits it. This used to be a JSON file copied in
by `install.ps1`; it is code now because renaming a setting silently drops its value from a JSON
and breaks the build here. To refresh after a retune, run `tools\Show-ProfileDeltas.ps1` (SimHub
stopped) and paste its output — **except** `Enabled` and `PolarityConfirmed`, which must stay at
their defaults: forces ship off, and polarity is a per-unit measured fact the 10% cap exists to
guard. `DefaultProfilesTests` fails if either creeps in.

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
