# AB9 Active Shifter

[![CI](https://github.com/Gugic/moza-ab9-simhub-plugin/actions/workflows/ci.yml/badge.svg)](https://github.com/Gugic/moza-ab9-simhub-plugin/actions/workflows/ci.yml)

An alternative to the MOZA AB9's own shifter mode. This SimHub plugin renders the shift gate
itself in force feedback — including the **push-through lockout** guarding 7th and reverse that
the stock firmware has no setting for — and plays a **much wider range of telemetry effects**
through the lever: a clutch grind that can refuse the gear, engine vibration, a rev limiter,
ABS and traction control, curbs. The selected gear comes out as **vJoy buttons**, so any game
binds it like an ordinary shifter.

![The plugin's Setup tab, with the profile and pattern pickers](docs/img/setup-tab.png)

## Setup

### 1. What you need

- **SimHub** — developed against 9.11.21
- A **MOZA AB9** base in flight-stick mode, on firmware **1.1.3.4 or newer** — developed and
  tested against **1.1.5.2**
- [**vJoy**](https://sourceforge.net/projects/vjoystick/) with a device exposing at least
  **10 buttons** — 1–8 carry the H patterns, 9 and 10 the sequential up/down
- .NET Framework 4.8, already present if SimHub runs

### 2. Configure the base in MOZA Cockpit

**This is not optional.** The AB9 self-centres in firmware, and DirectInput's request to switch
that off is ignored — measured, across five configurations. Cockpit is the only place it can be
turned off; Pit House has no Spring setting in flight mode at all. Skip this and the base fights
the gate everywhere with its own centring spring.

![MOZA Cockpit basic settings: DirectInput mode, Spring 0, Damper 15%](docs/img/moza-cockpit.png)

| Setting | Value |
| --- | --- |
| Force Feedback Mode | **DirectInput** |
| Spring | **0%** |
| Damper | **15%** |
| Maximum Torque Output | 100% |
| Overall Force Feedback Intensity | 100% |
| Game Force Feedback Gain | 100% |
| Inertia, Friction | 0% |

**Spring 0** is the one that matters most — it is the base's centring, and the gate cannot work
around it.

**Damper 15%** is optional but recommended. It is real damping applied in the base's own servo
loop, ahead of the USB round trip that everything this plugin renders has to cross, and it
settles the last bit of flutter a hand can provoke by leaning hard on a wall. It is the one kind
of damping that does not make the lever feel thick — the plugin's own damping dial is a last
resort by comparison.

Then **exit Cockpit completely**: it holds the stick exclusively while open, and the plugin
cannot acquire the device until it lets go. Close any Pit House live-tuning page for the same
reason, and disable Steam Input for the AB9 (or close Steam).

### 3. Install the plugin

```powershell
.\install.ps1
```

That builds it, stops SimHub, copies `AB9ActiveShifter.dll` into the SimHub folder (elevating if
needed), and restarts SimHub. Or take a prebuilt DLL from the [Releases
page](https://github.com/Gugic/moza-ab9-simhub-plugin/releases) and copy it in yourself with
SimHub closed.

Then enable **AB9 Active Shifter** under SimHub's *Settings → Plugins*.

On a machine with no saved settings yet, the installer also lays down
[the profiles this plugin was tuned with](presets/AB9ShifterPlugin.GeneralSettings.json), so you
start from a working gate rather than bare defaults — **7+R lockout**, **5+R**, and
**Sequential**, each holding its own complete tuning. It never overwrites settings you already
have. To add them by hand later, copy that file into
`C:\Program Files (x86)\SimHub\PluginsData\Common\` while SimHub is not running.

### 4. Measure polarity

Some AB9 firmware revisions apply DirectInput effects backwards, which would turn a centring
force into one that throws the stick at its stops. Until this is measured the plugin **caps its
force output at 10%**, so this step is what unlocks the shifter.

On the **Setup** tab press **Measure polarity**, take your hands off the stick, and wait about
ten seconds. The plugin pushes the stick briefly each way, on each axis, for each effect family,
and watches which way it actually moves.

It measures rather than asking because there is nothing useful for a hand to report: the base
holds itself centred, so a correct centring force and the base's own centring feel identical,
and an inverted one just feels like a weaker hold. Each probe is scored on whether the stick
moved the direction it was commanded, and summing the pair cancels any resting bias. A probe
stops the moment its direction is certain, so an inverted effect never reaches the stops.

Four probes run — a push and a spring on each axis — but only the two push results become
settings, because every wall in this gate is a push. The spring probes are a device check: all
four have to give a definite answer before the cap lifts, since a base that answers
unpredictably on either kind of effect is not one to trust at full force. This unit shows why
they are measured separately rather than assumed alike — its push is inverted left/right but
correct fore/aft, while its spring is the other way round.

If a probe reports the stick **barely moved**, the cap deliberately stays on: an unmeasured
direction is exactly the case it exists for. Check that nothing is touching the stick, then
raise *Calibration force* and run it again.

### 5. Switch the forces on

The shifter **starts off**. Enabling it takes the base exclusively and begins applying force, so
do it deliberately: put a hand on the stick, then tick *Shifter force feedback enabled* on the
Setup tab.

Raise the overall gain slowly from there. This is a 12 Nm base.

### 6. Bind the gears in your game

Bind gears **1–7 and reverse to vJoy buttons 1–8**, and the sequential up/down to **9 and 10**.
Do **not** bind the AB9's own axes in the game — the plugin is what reads them.

Reverse is always button 8 whatever the pattern, and the sequential buttons sit above every gear
button, so one set of bindings covers all four patterns and no binding can ever mean two things.

## The gate

```
   1     3     5     7
   |     |     |     |
   +-----+--+--+--#--+     +  column        -- neutral channel
   |     |     |     |     #  lockout gate   -  ordinary hump
   2     4     6     R
```

Four columns: **1/2, 3/4, 5/6, 7/R**, reverse bottom-right. The stick is not read as a joystick —
the plugin renders walls between the columns, a tunnel to slide along, and a detent that snicks
into each slot.

Sliding along the neutral tunnel there is a light hump between the ordinary columns, and
immediately past 5/6 the **lockout gate**: a compact band of flat force pushing back toward the
main gears the whole way across. Crossing it costs the same effort however fast you move the
lever, so it cannot be flicked through. Coming back out of 7/R is assisted, like a real range
gate. Once slotted in 7 or R the column behaves exactly like the others. The toll is the gate's
force (80% in the shipped profile) times its width, and both are adjustable.

Two rules make it feel mechanical rather than like a set of forces. A gear can only be left
**through the neutral tunnel** — leaning sideways, or shoving through a wall, will not hand you a
different gear, it just pushes you back into the one you are in. And pushing into a gear slightly
off-column is guided onto the slot by its **tapered mouth** rather than dead-ending against the
divider, the way a real gate's chamfered entry feeds the lever in.

An optional **neutral spring** (off by default) pulls the lever toward the 3/4 column while in
neutral, fading out with depth — dial it up and a released lever drifts home across the notches,
the way a real H lever rests at the 3/4 gate.

## Patterns

Four, selectable per **profile** on the Setup tab:

| Pattern | |
| --- | --- |
| **7+R** | The full gate above, with the lockout |
| **6+R** | The slot where 7 would sit simply does not exist — the wall over it never opens |
| **5+R** | Three wider columns, no lockout |
| **Sequential** | A sprung fore/aft lever: one shift per stroke, with a click you can tune |

A profile stores every dial together with its pattern, so each pattern keeps its own tuning and
switching between them is one dropdown.

## Game effects

With a game running, the **Effects** tab plays telemetry through the lever: **gear grind on a
clutchless shift** — push into a gear with the clutch up and the box rattles against a firm balk
wall, louder the harder you force it, and optionally the gear refuses to register until the
clutch goes down, like a blocking synchro ring — plus engine vibration that tracks the revs, a
rev-limiter buzz, ABS and traction-control buzzes, a curb-and-bump rattle read out of the car's
vertical acceleration, a gear-shift confirmation pulse, and a custom effect driven by any SimHub
property (which puts ShakeIt's exported effect groups on the lever).

Everything here is off by default, falls silent within half a second of the game pausing or
closing, and rides on top of the gate without touching its geometry. The grind needs a game that
reports the clutch pedal.

![The Effects tab: the clutch grind and its balk wall, engine vibration, rev limiter, ABS and traction control, curbs](docs/img/effects-tab.png)

## Tuning

- **Feel** — master gain; the gate and slot walls with their bite distance, attack, rebound
  absorption and friction; the lockout gate and the humps; the slot mouths; the three
  slot-detent forces (resistance, pull, seated hold).
- **Effects** — the telemetry effects above, each with volume and frequency.
- **Geometry** — the positions the gate is built from: how wide the neutral tunnel and the
  columns are, how far a push must travel to engage a gear, the vJoy device and loop rate, and
  scoped resets.
- **Monitor** — a live drawing of the gate with the stick position and the shaded lockout band,
  plus a trace recorder that logs every tick to CSV so a feel problem can be replayed.

![The Feel tab: master gain, the walls and their bite, the slot mouths, and the forces met sliding along the neutral tunnel](docs/img/feel-tab.png)

Changes apply on the next FFB tick; nothing needs restarting.

**[docs/tuning.md](docs/tuning.md) is the guide** — every dial, and a symptom-to-dial table for
when something feels wrong.

The first dial to know is **wall bite distance**. Past its bite a wall is a flat force and is
stable; all oscillation lives on the bite itself. Too short and contact kicks like ABS, too long
and the wall goes spongy. If neither end works, use **wall attack** instead.

## SimHub properties

Available for dashboards and formulas:

| Property | Meaning |
| --- | --- |
| `CurrentGear` | `N`, `1`…`7`, `R` |
| `GearIndex` | 0 for neutral, 1–8 (8 = reverse) |
| `InGear` | true while a gear is held |
| `GateState` | `Neutral`, `Traveling`, `Engaged` |
| `GateColumn` | `C1`…`C4`, or `None` |
| `StickX`, `StickY` | axis positions, 0–65535 |
| `DeviceConnected`, `VJoyConnected`, `DeviceName` | connection state |
| `LoopHz` | measured FFB loop rate |
| `StatusMessage` | the same text shown on the Setup tab |

Events: `GearEngaged`, `GearReleased`. Actions: `ToggleShifterFFB`, `ReleaseAllGears`.

## Safety

The base can produce 12 Nm, so output is bounded on every path:

- Force is capped at 10% until polarity has been measured.
- A watchdog stops all output if the FFB loop stalls for more than a second.
- Shutdown, device loss, and disabling always release **buttons first, then forces, then the
  device** — so a gear can never stay stuck down.
- If SimHub exits or crashes, dropping the exclusive DirectInput handle makes the driver discard
  the effects.

## Troubleshooting

**"No device with VID 346E / PID 1000 found"** — the base is off, in a different mode, or another
program has it. The message lists what was detected.

**"The stick is held exclusively by another program"** — MOZA Cockpit, a Pit House tuning page,
or Steam Input. Close them; the plugin retries automatically.

**"vJoy device 1 is owned by another program"** — the message names the owning process. Close it,
or pick a different vJoy device number on the Geometry tab.

**The stick fights you everywhere, or drifts to the stops** — polarity has not been measured, so
the gate's forces are pushing the opposite way. Run *Measure polarity*. To check whether the
resistance is coming from the plugin at all, tick *Release all forces (free stick)* on the Setup
tab: anything you still feel with that on is the hardware, so re-check Spring 0 in Cockpit.

**A wall buzzes, or kicks back like ABS** — see [docs/tuning.md](docs/tuning.md). Short answer:
adjust *wall bite distance* first, then *wall attack*.

**Everything feels dead, especially the lockout and the detents** — those are constant forces.
Confirm *Measure polarity* reported a result for both push axes rather than "barely moved", and
that overall gain is not near zero.

**Gears do not register in the game** — check `joy.cpl`: the vJoy device should light button *i*
while gear *i* is held. If it does, the binding is the problem, not the plugin.

## Building from source

```powershell
dotnet build src\AB9ActiveShifter\AB9ActiveShifter.csproj -c Release
dotnet test  tests\AB9ActiveShifter.Tests\AB9ActiveShifter.Tests.csproj
```

If SimHub is installed somewhere other than `C:\Program Files (x86)\SimHub\`, copy
`Directory.Build.props.user.example` to `Directory.Build.props.user` and set the path.

The plugin compiles against nine assemblies that live inside SimHub's install folder, so a
machine without SimHub — a CI runner, a fresh clone — falls back automatically to the reference
stubs in [build/refs](build/refs), which declare just the API surface this plugin uses. Nothing
about a local build changes if you have SimHub installed.

## Documentation

| | |
| --- | --- |
| [docs/tuning.md](docs/tuning.md) | Every dial, and symptom → dial when something feels wrong |
| [docs/hardware.md](docs/hardware.md) | Measured facts about the base, the USB path, and MOZA's software |
| [docs/force-model.md](docs/force-model.md) | How the gate is built, and every approach that was tried and rejected |
| [docs/architecture.md](docs/architecture.md) | Threading, lifecycle, effect handling, safety |
| [AGENTS.md](AGENTS.md) | Contributor and agent orientation, with the invariants |

## Licence

MIT — see `LICENSE`. Attribution and the clean-room note for BonusFFB are in `NOTICE.md`.
