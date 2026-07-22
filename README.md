# AB9 Active Shifter

A SimHub plugin that turns a **MOZA AB9 force feedback base in flight mode** into a proper
**7+R H-pattern shifter** — including the **push-through lockout** that the base's own
shifter mode does not have — and publishes the selected gear as **vJoy buttons** so any
game can bind it like a real shifter.

The stick is not read as a joystick. The plugin renders the gate with force feedback:
walls between the columns, a soft channel to slide along, a detent that snicks into each
slot, and a heavy spring-loaded gate guarding 7th and reverse.

## The gate

```
   1     3     5     7
   |     |     |     |
   +-----+-----+--|--+     <- neutral channel;  | = lockout
   |     |     |     |
   2     4     6     R
```

Four columns: **1/2, 3/4, 5/6, 7/R**, reverse bottom-right. Sliding right along the
neutral channel you meet the lockout: a force that ramps up and holds at a set share of
the base's output. Push through it and the 7/R column behaves exactly like the others.
Release the stick while still in the lockout zone and it springs back toward the main
gears. Once slotted in 7 or R, the column wall holds the stick and the lockout stops
pushing, so shifting between 7 and R is normal.

Lockout force defaults to 70% of the plugin's overall gain and is adjustable.

## Requirements

- SimHub (developed against 9.11.21)
- MOZA AB9 base with MOZA Pit House
- [vJoy](https://sourceforge.net/projects/vjoystick/) with a device exposing **at least 8 buttons**
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

1. **MOZA Pit House**: put the base in **flight mode** and set its **Spring to 0**. The
   base's own centring will fight the gate otherwise.
2. **Close MOZA Cockpit** and any Pit House live-tuning page. They hold the stick
   exclusively; the plugin cannot open it while they do.
3. **Disable Steam Input** for the AB9, or close Steam.
4. **Create a vJoy device** with at least 8 buttons in `vJoyConf`.
5. In your game, bind gears **1–7 and reverse to vJoy buttons 1–8**. Do **not** bind the
   AB9's axes in the game.

## Polarity — do this first

Some AB9 firmware revisions apply DirectInput effects backwards, which would turn a
centring spring into one that throws the stick at its stops. Until this is measured, the
plugin **caps its force output at 10%**.

On the **Setup** tab, press **Measure polarity**, take your hands off the stick, and wait
about four seconds. The plugin pushes the stick briefly each way and watches which way it
actually moves, then sets the invert flags itself and lifts the force cap.

It measures rather than asking because there is nothing useful for a hand to report: the
base holds itself centred, so a correct centring spring and the base's own centring feel
identical, and an inverted one just feels like a weaker hold. Each effect family is probed
in both directions and scored on whether it moved the way it was commanded, which cancels
any resting bias and still gives the right answer for an inverted spring — that case
accelerates away from its anchor and drives both probes the same way. A probe stops the
moment its direction is certain, so an inverted spring never reaches the stops.

If the result says the stick **barely moved**, the cap stays on deliberately — an
unmeasured direction is exactly what the cap is for. Check that the base's own Spring is 0
in Pit House and that nothing is holding the stick, then raise *Calibration force*.

Raise the overall gain slowly afterwards — this is a 12 Nm base.

## Tuning

- **Feel** — overall gain, lockout force, wall stiffness, channel guide and wall, neutral
  detent, damping, and the three slot-detent forces (resistance, pull, seated hold).
- **Geometry** — where the lockout starts and how abrupt it is, plus the enter/exit
  hysteresis bands for the channel and columns, engage and release depth.
- **Monitor** — live gate drawing with the stick position and the shaded lockout band.

Changes apply on the next FFB tick; nothing needs restarting.

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

**The stick fights the gate or drifts to the stops** — the base's own spring is still on
(Pit House → Spring 0), or polarity is inverted (run *Measure polarity*).

**Gears do not register in the game** — check `joy.cpl`: the vJoy device should light
button *i* while gear *i* is held. If it does, the binding is the problem, not the plugin.

## Licence

MIT — see `LICENSE`. Attribution and the clean-room note for BonusFFB are in `NOTICE.md`.
