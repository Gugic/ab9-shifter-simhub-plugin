# AB9 Active Shifter — working notes for agents

A SimHub plugin that turns a **MOZA AB9 flight base** into a **7+R H-pattern shifter** with a
push-through lockout. It renders the gate with DirectInput force feedback, detects the slotted
gear from stick position, and holds a vJoy button per gear so any game sees a normal shifter.

This file is the orientation. The four documents under `docs/` hold the detail:

| Document | Read it when |
| --- | --- |
| [docs/hardware.md](docs/hardware.md) | You need a measured fact about the base, the USB path, or MOZA's software. **Read before theorising about timing, polarity, or effect strength.** |
| [docs/force-model.md](docs/force-model.md) | You are changing how the gate feels. Records every approach tried and why it failed, so it is not retried. |
| [docs/architecture.md](docs/architecture.md) | You are changing code structure, threading, or lifecycle. |
| [docs/tuning.md](docs/tuning.md) | A human reports a feel problem and you need symptom → dial. |

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

113 tests, all green, all `Core/`-only. Keep them that way — they are the only automated check
on force arithmetic, and a sign error here drives a 12 Nm base the wrong way.

Deploy needs SimHub stopped, because it locks the DLL:

```bash
pwsh -File install.ps1
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
                           properties, events, actions, settings load/save
  ShifterSettings.cs       Persisted POCO (INotifyPropertyChanged) -> ToEngineConfig()
  Core/                    Pure, no I/O, fully unit-tested
    EngineConfig.cs        Immutable per-tick config snapshot + every default value
    GateGeometry.cs        Column targets, hysteresis bands, gear map, unit conversions
    GateStateMachine.cs    Neutral / Traveling / Engaged with hysteresis and resync
    ForceComposer.cs       The gate itself: position+velocity -> forces. The heart.
    PolarityCalibrator.cs  Measures effect polarity on hardware
    ShifterEngine.cs       The 1 kHz thread, phases, watchdog, reconnect, config swap
  Device/                  DirectInput and Win32
    FfbDevice.cs           Open by VID/PID, exclusive+background, poll
    EffectSet.cs           The five effects; one force write per tick, fault handling
    NativeMethods.cs       timeBeginPeriod + high-resolution waitable timer
  Output/VJoyGearOutput.cs vJoy behind IGearOutput (the wrapper is x86-only)
  UI/                      SettingsControl.xaml (Setup/Feel/Geometry/Monitor) + GateVisualizer
tests/AB9ActiveShifter.Tests/
  ForceComposerTests.cs    Force shape, stability properties, polarity, clamps
  GateStateMachineTests.cs Transitions, hysteresis, lockout traces
  PolarityCalibratorTests.cs Two-axis stick model incl. this unit's mixed inversion pattern
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
- Polarity is **four independent facts** (constant/spring × X/Y). This unit inverts constant
  force on X only and spring on Y only. One global flag cannot describe that.
- **Overall gain is capped at 10% until polarity is confirmed.** That cap is the safety story
  for an unmeasured base; do not add a path around it.
- Damping joins **after** the yield and the time shaping, and is never slewed. It opposes motion
  by construction, so it is never the assisting force the yield exists to soften, and rate
  limiting the stabiliser would defeat it.
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
- **A latched gear is released only by returning through the neutral channel — absolutely.** No
  lateral distance changes or drops it, and there is deliberately no fault threshold. Force cannot
  enforce a gate: a hand beats 12 Nm, so any distance at which the latch gave way would be a
  distance at which the rest of the pattern came back and could capture the lever into a gear it
  was never driven into. Do not reintroduce a lateral release — it is also what frees the slot
  wall's face from its old exit-band squeeze, so restoring one would mean re-clamping that ramp.
- **The lateral field must not read the state machine.** It is one function of position and the
  guide column, called by both branches; `TheLateralFieldDoesNotDependOnTheLatch` sweeps every
  column, direction, position and depth and demands exact equality. Computing it per-branch produced
  a 4924 DI (≈6 Nm) step at the same position, selected by history through the hysteretic channel
  bands, and that step was the mouth oscillation.
- **One stiffness for every lateral force.** Faces are derived from plateaus (`GuideFace`), so a
  gentler force gets a shorter face, never a steeper one. Never give a lateral force its own ramp
  dial — that is what made the funnel 3.5× the wall face.
- **Depth spans are not lateral spans.** Do not reach for `WallRamp` when you need a depth distance.
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
- Slots and the neutral channel are **corridors with walls**, not pulls toward a centre line. A
  restoring force about an interior equilibrium is an oscillator; that is what made the middle
  columns shake while the outer ones (one-sided against the end of travel) were fine.

**Safety ordering**

- Gear change: **buttons before forces.** A game must see the gear at least as early as the hand
  feels it.
- Shutdown, device loss, disable, `FinalizePlugin`: **buttons off → stop forces → unacquire**, in
  that order, always. A gear must never stay stuck down.
- The watchdog (500 ms timer, 1 s staleness) calls `EmergencyStop`. `StopForces` is the only
  device method callable off the engine thread, and it swallows everything.

## Conventions

**Commits.** Subject is imperative and says the *why*, not the file list — "Make the lockout
one-way, because an over-centre gate refunds a flick". Body is prose that records the reasoning
and any measurement behind the change, because the measurements are the expensive part and this
history is the only place several of them live. End with the `Co-Authored-By` trailer.

**PowerShell and git.** `git commit -m @'…'@` does not work in this environment — the quotes are
parsed as pathspecs. Write the message to a scratchpad file and use `git commit -F <file>`.

**Saved settings.** SimHub keeps them at
`C:\Program Files (x86)\SimHub\PluginsData\Common\AB9ShifterPlugin.GeneralSettings.json`. Edit it
**only while SimHub is stopped** — it is rewritten on exit. Changing a default in
`EngineConfig.cs` does not change a user who already has that key saved; patch the JSON too, and
say so.

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
| Files added, moved, or renamed | the code map above |
| Setup steps, requirements, or anything a user does once | `README.md` |
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

Still open: end-to-end vJoy verification in a game, a safety soak (forced-hang watchdog test and
reconnect cycling), and telemetry-driven effects — grind and synchro — which are deliberately out
of scope until the mechanical gate is finished. `DataUpdate` is empty and reserved for them.
