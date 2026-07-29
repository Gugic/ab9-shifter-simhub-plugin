# Development

Building, testing and deploying the plugin, and enough of the architecture to know where to put a
change. Users installing a release do not need any of this — [README.md](README.md) covers that.

Before changing behaviour, read **[AGENTS.md](AGENTS.md)**. Its invariants section is the part
that matters: every entry is there because breaking it caused a bug on real hardware, and a sign
error in this code drives a 12 Nm base the wrong way.

## What you need

- **.NET SDK 8** — it builds a `net48` target, so no separate targeting pack is required
- **SimHub**, to run it. Not needed to build (see [Building without SimHub](#building-without-simhub))
- **vJoy** with a device of at least 10 buttons, to see gears come out
- An **AB9**, to feel anything. The tests need none of the above

## Build and test

```bash
dotnet build
```

```bash
dotnet test tests/AB9ActiveShifter.Tests
```

The suite covers `Core/` plus the settings POCO's derived-dial arithmetic, and touches no I/O.
Keep it that way — it is the only automated check on the force arithmetic. `Core/` is deliberately
I/O-free for a second reason as well: the vJoy wrapper is a 32-bit native DLL that test runners
cannot load, so anything worth testing must not reach it.

If SimHub lives somewhere other than `C:\Program Files (x86)\SimHub\`, copy
`Directory.Build.props.user.example` to `Directory.Build.props.user` and set the path.

Formatting is checked in CI, so run it before pushing:

```bash
dotnet format whitespace --verify-no-changes
```

## Deploy to SimHub

SimHub locks the DLL, so it has to be stopped first. The script does the whole cycle — build, stop,
copy (elevating if needed), restart:

```bash
powershell -File install.ps1
```

By hand, which is usually what you want mid-iteration. Note the output path has **no** `net48`
segment:

```bash
powershell -Command "Stop-Process -Name SimHubWPF -Force -ErrorAction SilentlyContinue; Start-Sleep 2; Copy-Item src\AB9ActiveShifter\bin\Debug\AB9ActiveShifter.dll 'C:\Program Files (x86)\SimHub\' -Force; Start-Process 'C:\Program Files (x86)\SimHub\SimHubWPF.exe'"
```

A load failure is silent from the outside, so confirm it actually came up:

```bash
powershell -Command "Get-Content 'C:\Program Files (x86)\SimHub\Logs\SimHub.txt' -Tail 40 | Select-String AB9"
```

Healthy startup logs `Opened 'MOZA AB9 FFB Base' exclusive+background` and `vJoy device 1 acquired`.
A transient `DIERR_NOTEXCLUSIVEACQUIRED` followed by a successful retry is normal — something else
grabbed the device briefly.

**Saved settings** live at
`C:\Program Files (x86)\SimHub\PluginsData\Common\AB9ShifterPlugin.GeneralSettings.json`, and are
rewritten when SimHub exits — so edit that file only while SimHub is stopped. Changing a default in
`EngineConfig.cs` does **not** change a user who already has that key saved; patch the JSON too, and
say so in the commit.

**The profiles a fresh install starts with** are in `DefaultProfiles.cs`, written as differences
from a bare `ShifterSettings` so the tuning reads as tuning. The plugin writes them out on the
first start — when `ReadCommonSettings` finds nothing — and they are ordinary settings from then
on. To refresh them after retuning on the rig, stop SimHub and turn the saved file back into
assignments:

```bash
powershell -File tools\Show-ProfileDeltas.ps1
```

It prints paste-ready C# per profile. Two lines from its output must **not** be pasted:
`Enabled` and `PolarityConfirmed` stay at their defaults, because forces ship off and the 10% cap
guards a base nobody has measured. `DefaultProfilesTests` fails if either creeps in.

## Building without SimHub

The plugin references nine assemblies that ship inside SimHub's install folder. They are not ours
to redistribute, and a CI runner has none of them, so the build falls back automatically to the
reference stubs in [build/refs](build/refs) when `$(SimHubDir)SimHub.Plugins.dll` is absent. Force
either way with `-p:UseSimHubStubs=true` or `=false`.

The stubs declare only the API surface this plugin actually uses, and their signatures were taken
by reflecting over the real assemblies. That fidelity is load-bearing: the compiler bakes the
difference between a field read and a property call, and between one enum constant and another,
straight into the IL. A stub that merely looks right produces a DLL that builds green in CI and
throws on the rig. Read [build/refs/README.md](build/refs/README.md) before touching one.

To prove a stub-built DLL still binds, on a machine that has SimHub:

```bash
powershell -File tools\Verify-StubBuild.ps1
```

It loads the DLL with the real assemblies on the resolve path and asks the JIT to prepare every
method, which resolves every external token without executing anything. CI cannot run it, so it is
the one release gate that stays manual — run it before tagging.

## How the code fits together

One background thread owns everything with a device handle. It runs at 1 kHz, and each tick reads
the stick, decides the gear, writes vJoy, composes forces and ships one write per axis. The UI and
SimHub's property system only ever read a snapshot; nothing else touches DirectInput or vJoy.

```
src/AB9ActiveShifter/
  AB9ShifterPlugin.cs      SimHub shell: lifecycle, properties, events, actions, profiles,
                           settings load/save, DataUpdate -> TelemetryState
  ShifterSettings.cs       Persisted POCO -> ToEngineConfig()
  ShifterProfiles.cs       Named profiles, legacy migration, cloning
  DefaultProfiles.cs       What a fresh install starts with, as deltas from bare defaults
  ProfileTransfer.cs       Export/import of one profile as a shareable file, with validation
  PluginInfo.cs            The build's version string
  Core/                    Pure, no I/O, fully unit-tested
    EngineConfig.cs        Immutable per-tick config snapshot + every default value
    GateGeometry.cs        Column targets, hysteresis bands, gear map, unit conversions
    GateStateMachine.cs    Neutral / Traveling / Engaged
    SequentialStateMachine.cs One shift per stroke
    ForceComposer.cs       Position + velocity -> forces. The heart
    EffectComposer.cs      Telemetry -> vibration carriers + the clutch grind decision
    ShifterEngine.cs       The 1 kHz thread, phases, watchdog, reconnect, config swap
    VelocityEstimator.cs   Position -> speed across a 4 ms window
    PolarityCalibrator.cs  Measures effect polarity on hardware
    TraceRecorder.cs       Per-tick ring buffer -> CSV, so a feel complaint can be replayed
  Device/                  DirectInput and Win32
  Output/VJoyGearOutput.cs vJoy behind IGearOutput (the wrapper is x86-only)
  UI/                      SettingsControl.xaml (Setup/Feel/Effects/Geometry/Monitor)
tests/AB9ActiveShifter.Tests/
build/refs/                Reference-only stubs of SimHub's assemblies
tools/Verify-StubBuild.ps1 Proves a stub-built DLL binds against the real SimHub
tools/Show-ProfileDeltas.ps1 Turns a tuned settings file back into DefaultProfiles.cs assignments
```

[docs/architecture.md](docs/architecture.md) has the detail: threading, lifecycle, effect handling
and the safety ordering. [AGENTS.md](AGENTS.md) has the full code map and the invariants.

### The one paragraph that will save you a week

Nearly every hard problem here has been the same problem: **a stiff virtual wall rendered through a
delayed loop is unstable.** The base is 12 Nm, the position-to-torque round trip has a hard floor of
3–4 ms, and no amount of damping or loop rate fixes a force gradient too steep for that delay. The
gate is therefore built out of shapes chosen for stability — flat plateaus, free corridors, one-way
tolls — not out of stiffness turned up until it feels right. If you are about to raise a stiffness
to fix a feel complaint, read [docs/force-model.md](docs/force-model.md) first: it records every
approach that has already been tried and why it failed, so it is not tried again.

### Verifying a feel change

Arithmetic does not settle a feel question. The human at the stick is the instrument: land the
change, deploy it, and say what to try and what to look for. Do not conclude a feel problem is
fixed without that.

`Monitor` → the trace recorder writes every tick to CSV, which is how a complaint like "it buzzes
coming off the lockout" becomes a frequency and an amplitude instead of an adjective.

## CI and releases

`.github/workflows/ci.yml` runs on every push and pull request: format check, build against the
stubs, tests, and the DLL uploaded as an artifact. If you change how the plugin uses SimHub's API,
add the member to the matching stub **in the same commit** — otherwise CI goes red while your local
build stays green.

`.github/workflows/release.yml` is manual (`workflow_dispatch`). Give it a version like `0.9.0` or
`1.0.0-rc1` and it validates the number, refuses one that is already tagged, builds with the
version stamped into the assembly, confirms the stamp arrived, packages the DLL with the notices,
tags the commit and publishes a GitHub Release.

Before running it: `tools\Verify-StubBuild.ps1` on a machine with SimHub, and refresh
`DefaultProfiles.cs` if the tuning has moved (see *Saved settings* above).

## Conventions

**Commits.** The subject is imperative and says the *why*, not the file list. The body is prose
recording the reasoning and any measurement behind the change, because the measurements are the
expensive part and this history is the only place several of them live. No attribution trailers.

**Documentation is part of the change, not a follow-up.** AGENTS.md carries a table of what to
update alongside what: a changed default, a new dial, a measured hardware fact, a renamed file.
Record failed approaches too — most of the cost in this project has been re-deriving that something
does not work.

**Hardware claims get measured, not assumed**, and the number goes in
[docs/hardware.md](docs/hardware.md) with how it was measured. Several plausible assumptions here
turned out to be false; that file has a section for them.

## Safety while developing

You are iterating on software that drives a 12 Nm servo, usually with a hand on it.

- Forces start **off**, and overall gain is capped at 10% until polarity has been measured. Do not
  add a path around that cap.
- Test a force change at low gain first, and keep the base's power switch reachable.
- A build that fails to load is silent; a build that loads with a sign error is not. If the stick
  fights you everywhere after a change, tick *Release all forces (free stick)* on the Setup tab —
  anything still resisting is the hardware, not your code.
- Ad-hoc hardware probes belong in a scratch project outside this repo, run with SimHub stopped so
  the device is free.
