# Development

Building, testing and deploying the plugin, and enough of the architecture to know where to put a
change. Users installing a release do not need any of this — [README.md](README.md) covers that.

Before changing behaviour, read **[AGENTS.md](AGENTS.md)**. Its invariants section is the part
that matters: every entry is there because breaking it caused a bug on real hardware, and a sign
error in this code drives a 12 Nm base the wrong way.

## What you need

- **.NET SDK 8** — it builds a `net48` target, so no separate targeting pack is required
- **SimHub**, to run it. Not needed to build (see [Building without SimHub](#building-without-simhub))
- **vJoy**, or SimHub's built-in **Control Mapper** with configured output roles, to see gears
  come out. Direct vJoy uses 14 buttons to cover all patterns (1-8 gears, 9-10 sequential
  up/down, 11-14 the PRND positions)
- An **AB9**, to feel anything. The tests need none of the above

## Build and test

```bash
dotnet build
```

```bash
dotnet test tests/AB9ActiveShifter.Tests
```

The suite covers `Core/` plus the settings POCO's derived-dial arithmetic, and touches no I/O.
It also tests the pure GitHub release parser and version/asset policy in `Updates/ReleaseInfo.cs`.
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

**SimHub keeps ten rolling backups** of that file in the same folder under
`_Backups\AB9ShifterPlugin.GeneralSettings_b1.json` … `_b10.json`, and restores from the newest one
when the primary is missing. Deleting the settings file therefore does **not** give you a machine
with no settings — measured: delete it, restart, and the plugin comes up with the old profiles and
never logs the first-start line. To genuinely test a first start, take the backups too:

```bash
powershell -Command "Stop-Process -Name SimHubWPF -Force; Start-Sleep 3; Remove-Item 'C:\Program Files (x86)\SimHub\PluginsData\Common\AB9ShifterPlugin.GeneralSettings.json','C:\Program Files (x86)\SimHub\PluginsData\Common\_Backups\AB9ShifterPlugin.GeneralSettings_b*.json' -Force; Start-Process 'C:\Program Files (x86)\SimHub\SimHubWPF.exe'"
```

A real first start logs, at `Init`:

```
[AB9Shifter] No saved settings; installed the shipped profiles and made '(Preset) 7+R lockout' active.
[AB9Shifter] Plugin is disabled in settings; engine not started.
```

Those two lines together are the check: the profiles arrived, and nothing is applying force. If the
first line is absent, the settings were restored and whatever you concluded from the run is about
the old ones.

**The presets every install carries** are in `DefaultProfiles.cs`, written as differences from a
bare `ShifterSettings` so the tuning reads as tuning. `EnsurePresets` rebuilds them on *every*
start, not just the first, so a retune here reaches installs that already exist. That is safe
without a migration step because a preset's name carries a reserved prefix — `(Preset) ` — that
`ProfileStore.UniqueName` strips off every name a user can supply, so presets and local profiles
cannot collide and nothing already in a settings file is ever touched. Editing a preset forks it
into a local profile instead; see *Shipped profiles* in `AGENTS.md` for why the fork renames the
live object rather than cloning it.

To refresh them after retuning on the rig, stop SimHub and turn the saved file back into
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

The Effects tab uses SimHub's public ShakeIt classes, an undocumented integration surface.
`ShakeItStubs.cs` mirrors their exact signatures, including generic declaring types. The real
build also references SimHub's bundled `GongSolutions.WPF.DragDrop`, which the native profile
implements. Check the editor and profile reload in the installed SimHub after an upgrade;
successful token binding alone cannot verify initialization or WPF behavior.

To prove a stub-built DLL still binds, on a machine that has SimHub:

```bash
powershell -File tools\Verify-StubBuild.ps1
```

It loads the DLL with the real assemblies on the resolve path and asks the JIT to prepare every
method, which resolves every external token without executing anything. CI cannot run it, so it is
the one release gate that stays manual — run it before tagging.

## How the code fits together

One background thread owns everything with a device handle. It runs at 1 kHz, and each tick reads
the stick, decides the gear, submits the selected output, composes forces and ships one write per axis. The UI and
SimHub's property system only ever read a snapshot; nothing else touches DirectInput or vJoy.

```
src/AB9ActiveShifter/
  AB9ShifterPlugin.cs      SimHub shell: lifecycle, properties, events, actions, profiles,
                           settings load/save, DataUpdate -> TelemetryState
  AB9ShifterPlugin.Native.cs Three operating modes and verified AB9 onboard configuration
  ShifterSettings.cs       Persisted POCO -> ToEngineConfig()
  ShifterProfiles.cs       Named profiles, legacy migration, cloning, the preset fork
  DefaultProfiles.cs       The five presets, as deltas from bare defaults, and their reserved
                           name prefix
  ProfileTransfer.cs       Export/import of one profile as a shareable file, with validation
  NativeEffectsData.cs     Validates a native tune before SimHub deserializes it
  Effects/                 Native ShakeIt service/editor, Lever output adapter and four sources
  PluginInfo.cs            The build's version string
  Core/                    Pure, no I/O, fully unit-tested
    Ab9NativeProtocol.cs   CDC frame codec, parameters and firmware eligibility
    Ab9NativeSettings.cs   Native read/write snapshots and validated configuration plans
    NativeProfilePolicy.cs Operating-mode and virtual engine eligibility
    NativeSettingsDebounce.cs Latest onboard tune after a 500 ms quiet period
    NativeWritePause.cs    Checked output suspension; off/panic always cancel resume
    BaseEffectComposer.cs  Optional generic spring/friction/inertia, capped and polarity-aware
    OperatingMode.cs       Rig-wide effect provider; shared profiles keep the same percentages
    EngineConfig.cs        Immutable per-tick config snapshot + every default value
    GateGeometry.cs        Column targets, hysteresis bands, gear map, unit conversions
    GateStateMachine.cs    Neutral / Traveling / Engaged
    SequentialStateMachine.cs One shift per stroke
    PrndLane.cs            Where an automatic's four positions are, and which button each holds
    PrndStateMachine.cs    Which position is held. Always exactly one
    ForceComposer.cs       Position + velocity -> forces. The heart
    EffectComposer.cs      Telemetry -> vibration carriers + the clutch grind decision
    NativeEffectMixer.cs   Native tone envelopes -> independent, budgeted 1 kHz carriers
    ShifterEngine.cs       The 1 kHz thread, phases, watchdog, reconnect, config swap
    GearOutputConfig.cs    Output choice, per-pattern role mappings and change detection (pure)
    DeviceFault.cs         A DirectInput HRESULT as gone / taken by another app / unknown
    VelocityEstimator.cs   Position -> speed across a 4 ms window
    PolarityCalibrator.cs  Measures effect polarity on hardware
    TraceRecorder.cs       Per-tick ring buffer -> CSV; keeps the LAST two minutes, so it can
                           be left running through a session and still hold the failure
  Device/                  DirectInput and Win32
    Ab9NativeDevice.cs     Separate CDC worker, exact AB9 discovery and checked transactions
  Output/VJoyGearOutput.cs vJoy behind IGearOutput (the wrapper is x86-only)
  Output/VJoyDeviceProbe.cs Enumerates vJoy devices for the device/output picker (query-only)
  Output/ControlMapperGearOutput.cs Held native roles behind IGearOutput, including optional H neutral
  Output/IControlMapperRoles.cs Role API boundary, faked in I/O-free output tests
  Output/SimHubControlMapperRoles.cs Public SimHub role API; one interface owns press and release
  Updates/                 ReleaseInfo (pure policy), UpdateService (background GitHub checks),
                           UpdateInstaller (validated, atomic DLL replacement)
  UI/                      SettingsControl.xaml (first-run Setup, Main/Options, tuning modals), the
                           GateVisualizer plan view and the Feel modal's force-curve graphs, all
                           on ForceGraphVisualizerBase and each sampling ForceComposer itself
    SettingsControl.Updates.cs App update preferences, shared banner and install/restart actions
    SettingsControl.Native.cs Native setup actions, status and control availability
    SettingsControl.Outputs.cs Output selector, pattern-specific native role pickers and readiness
tests/AB9ActiveShifter.Tests/
  ControlMapperOutputTests.cs Role lifetimes, cleanup, mapping validation and rig-owned settings
  NativeSettingsDebounceTests.cs Pending edits survive busy reads; latest tune wins each batch
  NativeWritePauseTests.cs Ordinary updates preserve Enabled; failures/off/panic cannot resume
  OperatingModeTests.cs    Shared providers, calibration caps and store migration
  BaseEffectComposerTests.cs Generic typed effects and separate spring-polarity safety
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

**Options → Diagnostics** → the trace recorder writes every tick to CSV, which is how a complaint like "it buzzes
coming off the lockout" becomes a frequency and an amplitude instead of an adjective. It keeps
the **last** two minutes and never stops itself, so for a fault that arrives at an unknown time
the move is to start it, drive, and stop it once the fault has happened.

## CI and releases

The in-plugin updater uses the latest stable release from this repository. Keep publishing
`AB9ActiveShifter.dll` as a standalone release asset: installation checks GitHub's asset size
and SHA-256 digest and the DLL's assembly name/version. A ZIP-only release can be announced
but requires manual installation. Release versions come from the workflow's `-p:Version`;
build metadata is ignored for update comparison, and a stable release can replace a preview
of the same numeric version. For a local development deployment, stamp an appropriate version
explicitly to avoid the bare `0.1.0` development default advertising an older published build.

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

## Working on it

Changes reach `main` through a pull request:

```bash
git switch -c profile-export
```

```bash
gh pr create --title "..." --body-file <file>
```

`gh pr create -b "..."` is awkward in this shell — the same quoting problem that makes
`git commit -m` unusable for a multi-line message here — so write the description to a file. Rebase
on `main` rather than merging it back in; the history is linear and worth keeping that way.

Squash-merge, so the PR description becomes the commit message on `main`. That means writing it as
one: what changed and why, with the measurement behind it, and how it was verified — including
whether `Verify-StubBuild.ps1` was run and whether the change was felt on the rig. CI has to be
green first.

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
  fights you everywhere after a change, tick *Release all forces (free stick)* in Options —
  anything still resisting is the hardware, not your code.
- Ad-hoc hardware probes belong in a scratch project outside this repo, run with SimHub stopped so
  the device is free.
