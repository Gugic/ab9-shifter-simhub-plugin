# AB9 Active Shifter

A SimHub plugin that turns a **MOZA AB9 force feedback base in flight mode** into a proper
**H-pattern shifter** — including the **push-through lockout** that the base's own
shifter mode does not have — and publishes the selected gear as **vJoy buttons** so any
game can bind it like a real shifter.

The stick is not read as a joystick. The plugin renders the gate with force feedback:
walls between the columns, a channel to slide along, a detent that snicks into each slot,
and a one-way gate guarding 7th and reverse.

Four patterns, selectable per **profile** on the Setup tab: **7+R** (the full gate below),
**6+R** (the slot where 7 would sit simply does not exist), **5+R** (three wider columns, no
lockout), and **Sequential** (a sprung fore/aft lever that pulses button 9 for upshift and
button 10 for downshift). A profile stores every dial together with its pattern, so each
pattern keeps its own tuning and switching is one dropdown. Forward gears map to vJoy
buttons 1..N and reverse is always button 8, whatever the pattern, so one set of game
bindings covers every pattern; the sequential buttons sit above them all so no game binding
can ever mean two things. A vJoy device with 10+ buttons covers every pattern.

## The gate

```
   1     3     5     7
   |     |     |     |
   +-----+--+--+--#--+     +  column        -- neutral channel
   |     |     |     |     #  lockout gate   -  ordinary hump
   2     4     6     R
```

Four columns: **1/2, 3/4, 5/6, 7/R**, reverse bottom-right. Sliding along the neutral
channel there is a light hump between the ordinary columns, and immediately past 5/6 a
**lockout gate**: a compact band of flat force pushing back toward the main gears the whole
way across. Crossing it costs the same effort however fast you move the lever, so it cannot
be flicked through. Coming back out of 7/R is assisted, like a real range gate. Once slotted
in 7 or R the column behaves exactly like the others.

Two rules make the gate feel mechanical rather than like a set of forces. A gear can only be
left **through the neutral channel** — leaning sideways, or shoving through a wall, will not
hand you a different gear, it just pushes you back into the one you are in. And pushing into
a gear slightly off-column is **funnelled** onto the slot rather than blocked, the way the
tapered mouth of a real gate guides the lever in.

The toll is the gate's force (70% by default) times its width, and both are adjustable.

## Requirements

- SimHub (developed against 9.11.21)
- MOZA AB9 base, configured once in **MOZA Cockpit** — see below
- [vJoy](https://sourceforge.net/projects/vjoystick/) with a device exposing **at least 10 buttons**
  (8 cover the H patterns alone; 9/10 are the sequential up/down)
- .NET Framework 4.8 (already present if SimHub runs)

## Install

```powershell
.\install.ps1
```

The script builds, stops SimHub, copies `AB9ActiveShifter.dll` into the SimHub folder
(elevating if needed), and restarts SimHub. Then enable **AB9 Active Shifter** under
SimHub's *Settings → Plugins*.

The shifter itself **starts switched off**. Enabling it takes the base exclusively and
begins applying force, so work through the checklist below first, then tick *Shifter force
feedback enabled* on the plugin's Setup tab with a hand on the stick.

To build without installing:

```powershell
dotnet build src\AB9ActiveShifter\AB9ActiveShifter.csproj -c Release
dotnet test  tests\AB9ActiveShifter.Tests\AB9ActiveShifter.Tests.csproj
```

If SimHub is installed somewhere other than `C:\Program Files (x86)\SimHub\`, copy
`Directory.Build.props.user.example` to `Directory.Build.props.user` and set the path.

## Before your first run

1. **Configure the base in MOZA Cockpit**, once (see the next section). This is not
   optional — without it the base fights the gate with its own centring spring.
2. **Fully exit MOZA Cockpit** and close any Pit House live-tuning page. They hold the
   stick exclusively; the plugin cannot open it while they do.
3. **Disable Steam Input** for the AB9, or close Steam.
4. **Create a vJoy device** with at least 10 buttons in `vJoyConf`.
5. **Run the polarity calibration** (below) before turning the force up.
6. In your game, bind gears **1–7 and reverse to vJoy buttons 1–8**. Do **not** bind the
   AB9's axes in the game.

## The MOZA Cockpit settings

The base's self-centring lives in **firmware**, and DirectInput's request to disable
autocentring is ignored — measured, across five configurations. **MOZA Cockpit** is the only
place it can be switched off, and Pit House has no Spring setting in flight mode at all.

In MOZA Cockpit, with firmware **1.1.3.4 or newer**:

| Setting | Value |
| --- | --- |
| FFB Mode | **DirectInput** |
| Spring | **0** |
| Base Force Model | **Flight Base** |
| Max Torque | 100% |
| Overall Intensity | 100% |
| Game FFB Gain | 100% |

Then **exit Cockpit completely**. To check the stick is genuinely free afterwards, tick
**Release all forces (free stick)** on the Setup tab — anything you still feel with that on
is the hardware, not the gate.

## Polarity — do this first

Some AB9 firmware revisions apply DirectInput effects backwards, which would turn a
centring spring into one that throws the stick at its stops. Until this is measured, the
plugin **caps its force output at 10%**.

On the **Setup** tab, press **Measure polarity**, take your hands off the stick, and wait
about ten seconds. The plugin pushes the stick briefly each way, on each axis, for each
effect family, and watches which way it actually moves. It sets the four sign flags from
what it measures and lifts the force cap.

It measures rather than asking because there is nothing useful for a hand to report: the
base holds itself centred, so a correct centring spring and the base's own centring feel
identical, and an inverted one just feels like a weaker hold. Each probe is scored on
whether the stick moved the direction it was commanded; summing the two probes cancels any
resting bias and still gives the right answer for an inverted spring, which accelerates
away from its anchor and drives both probes the same way. A probe stops the moment its
direction is certain, so an inverted spring never reaches the stops.

**All four are measured separately, because this base does not treat them alike.** On the
firmware this was developed against, constant force is inverted on the left/right axis but
correct fore/aft, while the spring is inverted fore/aft but correct left/right. One global
polarity flag cannot describe that.

If a probe reports the stick **barely moved**, the cap stays on deliberately — an
unmeasured direction is exactly what the cap is for. Check nothing is holding the stick,
then raise *Calibration force*.

Raise the overall gain slowly afterwards — this is a 12 Nm base.

## Tuning

- **Feel** — master gain; the gate and slot walls with their bite distance, attack, rebound
  absorption and damping; the lockout gate and the humps; the three slot-detent forces
  (resistance, pull, seated hold).
- **Geometry** — force shaping, the enter/exit hysteresis bands for the channel and columns,
  engage and release depth, vJoy device, loop rate, and scoped resets.
- **Monitor** — live gate drawing with the stick position and the shaded lockout band.

Changes apply on the next FFB tick; nothing needs restarting.

**[docs/tuning.md](docs/tuning.md) is the guide** — every dial, and a symptom-to-dial table
for when something feels wrong.

The first dial to know: **wall bite distance**. Past its bite a wall is a flat force and is
stable; all oscillation lives on the bite itself. Too short and contact kicks like ABS, too
long and the wall goes spongy. If neither end works, use **wall attack** instead.

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

- Force is capped at 10% until the polarity wizard has been run.
- A watchdog stops all output if the FFB loop stalls for more than a second.
- Shutdown, device loss, and disabling always release **buttons first, then forces, then
  the device** — so a gear can never stay stuck down.
- If SimHub exits or crashes, dropping the exclusive DirectInput handle makes the driver
  discard the effects.

## Troubleshooting

**"No device with VID 346E / PID 1000 found"** — the base is off, in a different mode, or
another program has it. The message lists what was detected.

**"The stick is held exclusively by another program"** — MOZA Cockpit, a Pit House tuning
page, or Steam Input. Close them; the plugin retries automatically.

**"vJoy device 1 is owned by another program"** — the message names the owning process.
Close it, or pick a different vJoy device number on the Geometry tab.

**The stick fights you everywhere, or drifts to the stops** — polarity has not been
measured, so the gate's forces are pushing the opposite way. Run *Measure polarity*. To
check whether resistance is coming from the plugin at all, tick *Release all forces*: if
the stick is still stiff with that on, it is not the gate — check the MOZA Cockpit settings
above.

**A wall buzzes, or kicks back like ABS** — see [docs/tuning.md](docs/tuning.md). Short
answer: adjust *wall bite distance* first, then *wall attack*.

**Everything feels dead, especially the lockout and the detents** — those are constant
forces. Confirm *Measure polarity* reported a result for both push axes rather than
"barely moved", and that overall gain is not near zero.

**Gears do not register in the game** — check `joy.cpl`: the vJoy device should light
button *i* while gear *i* is held. If it does, the binding is the problem, not the plugin.

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
