# AB9 Active Shifter

[![CI](https://github.com/Gugic/ab9-shifter-simhub-plugin/actions/workflows/ci.yml/badge.svg)](https://github.com/Gugic/ab9-shifter-simhub-plugin/actions/workflows/ci.yml)

An alternative to the MOZA AB9's own shifter mode. This SimHub plugin renders the shift gate
itself in force feedback — including the **push-through lockout** guarding 7th and reverse that
the stock firmware has no setting for — and plays a **much wider range of telemetry effects**
through the lever: a clutch grind that can refuse the gear, engine vibration, a rev limiter,
ABS and traction control, curbs. The selected gear comes out as **vJoy buttons**, so any game
binds it like an ordinary shifter.

![The plugin's Setup tab, with the profile and pattern pickers](docs/img/setup-tab.png)

## Read this first

**It drives a 12 Nm active device, and the risk is yours.** The AB9 is a servo strong enough to
hurt a wrist and to slam its own stops. This plugin computes forces in software, several
milliseconds of USB away from that motor, and a force rendered through that delay can go unstable
and oscillate on its own — most of this project's design exists to keep that from happening, which
is also an admission that it can. A bug, an unlucky combination of settings, or a stalled loop can
make the base shake, kick, or drive to a stop with no warning. Treat it as the powerful machine it
is: keep your face and your free hand clear, start with the gain low, and know where the base's
power switch is before you enable anything. You run it at your own risk. Nobody is liable for
injury, or for damage to your hardware or anything attached to it, and there is no warranty of any
kind — see [LICENSE](LICENSE).

**Unofficial.** Not affiliated with, endorsed by, or supported by MOZA, SimHub or vJoy. "MOZA" and
"AB9" appear here only to say which hardware this works with. Do not take a problem caused by this
plugin to MOZA's support — a base running it is being driven by third-party software they did not
write.

**Early software.** It is in active development and is nowhere near polished. It has been built
and tuned against exactly one base on one firmware revision, so behaviour on yours is genuinely
untested; defaults, dial names and saved settings can still change between versions. Expect rough
edges, and read [docs/tuning.md](docs/tuning.md) when something feels wrong before assuming it is
meant to feel that way.

## Setup

### 1. What you need

- **SimHub** — developed against 9.11.21
- A **MOZA AB9** base on firmware **1.1.3.4 or newer** — developed and tested against **1.1.5.2**
- **MOZA Pit House** and **MOZA Cockpit**, for the one-time base configuration below
- [**vJoy**](https://sourceforge.net/projects/vjoystick/) with a device exposing at least
  **10 buttons** — 1–8 carry the H patterns, 9 and 10 the sequential up/down. The Setup tab lists
  the devices vJoy reports with their button counts, so you can check this without guessing; the
  gate itself works without vJoy, you just get no gear output
- .NET Framework 4.8, already present if SimHub runs

### 2. Put the base in flight mode — MOZA Pit House

The AB9 has two firmware modes, and the switch lives in **Pit House**, under **AB9 Mode**. Set it
to **Flight Simulation Base**.

![Pit House: AB9 Mode set to Flight Simulation Base](docs/img/pit-house-mode.png)

That is what makes the base enumerate as the two-axis DirectInput force feedback joystick this
plugin opens. In *Shifter* mode the base runs its own gate in firmware and does not present the
axes at all, so the plugin has nothing to read.

The **Shifter Mode** dropdown greyed out underneath is the stock feature this plugin replaces —
fixed layouts and no lockout. It is not effect-less: it plays engine-rpm vibration and a shift
effect of its own. What it has no notion of is the rest of a game's telemetry — the rev limiter,
ABS, traction control, curbs and the clutch grind below are all things this plugin adds. None of
it has any bearing on anything once the base is in flight mode.

### 3. Set up the force feedback — MOZA Cockpit

**Also not optional**, and it is a different app from the last step. The AB9 self-centres in
firmware, and DirectInput's request to switch that off is ignored — measured, across five
configurations. Cockpit's **Spring** is the only place it can be turned off; Pit House has no
Spring setting in flight mode at all. Skip this and the base fights the gate everywhere with its
own centring.

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
reason.

### 4. Install the plugin

Download the zip from the [latest
release](https://github.com/Gugic/ab9-shifter-simhub-plugin/releases/latest), **close SimHub**, and
copy `AB9ActiveShifter.dll` out of it into `C:\Program Files (x86)\SimHub\`. SimHub locks the DLL
while it runs, so the copy fails if you skip that.

Start SimHub and enable **AB9 Active Shifter** under *Settings → Plugins*.

That is the whole install. The first start writes out three working profiles — **7+R lockout**,
**5+R** and **Sequential**, each holding its own complete tuning — so you begin from a gate that
was tuned on real hardware rather than from bare defaults. They are ordinary settings from then on:
edit them, delete them, add your own. Forces are off and the force cap is on, as they should be on
a base nobody has measured yet.

Building it yourself instead, and the `install.ps1` script that does all of the above in one step:
[DEVELOPMENT.md](DEVELOPMENT.md).

### 5. Measure polarity

Some AB9 firmware revisions apply DirectInput effects backwards, which would turn a centring
force into one that throws the stick at its stops. Until this is measured the plugin **caps its
force output at 10%**, and **only the Setup tab is shown** — there is no point offering force
dials before it is known which way the base pushes. This step is what unlocks the shifter.

The other tabs appear once polarity is measured *and* a vJoy device is available. Everything
needed to satisfy both is on the Setup tab, including the vJoy picker and the base's vendor and
product ids, so the gate can never hide the control that opens it.

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
raise *Calibration force* and run it again. It defaults to 10%, which is enough on this base — a
probe stops as soon as its direction is certain, so what it needs is a movement that can be read,
not a large one.

Once measured, the whole section collapses to its result and a **Measure again** button. Polarity
is a property of the base rather than of a profile, so it only wants remeasuring if the hardware
changes or the gate starts pushing the wrong way.

### 6. Switch the forces on

The shifter **starts off**. Enabling it takes the base exclusively and begins applying force, so
do it deliberately: put a hand on the stick, then tick *Shifter force feedback enabled* on the
Setup tab.

Raise the overall gain slowly from there. This is a 12 Nm base.

### 7. Bind the gears in your game

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

**Profiles can be exported and imported**, so a tune can be shared as a file. What travels is the
tuning only: your measured polarity, your device and vJoy numbers and your loop rate stay as they
are on your machine. An import always *adds* a profile, numbering the name if it is taken, so
someone else's file can never land on top of yours — and it always arrives with forces off, with
every value range-checked on the way in.

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
  columns are, how far a push must travel to engage a gear, the loop rate, and
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

These are the bounds on output, not a guarantee — *Read this first* above is the part that
actually matters. The base can produce 12 Nm, so output is bounded on every path:

- Force is capped at 10% until polarity has been measured.
- A watchdog stops all output if the FFB loop stalls for more than a second.
- Shutdown, device loss, and disabling always release **buttons first, then forces, then the
  device** — so a gear can never stay stuck down.
- If SimHub exits or crashes, dropping the exclusive DirectInput handle makes the driver discard
  the effects.

## Troubleshooting

**"No device with VID 346E / PID 1000 found"** — the base is off, in a different mode, or another
program has it. The message lists what was detected.

**"The stick is held exclusively by another program"** — MOZA Cockpit or a Pit House tuning page,
occasionally a game. Close them; the plugin retries automatically.

**"vJoy device 1 is owned by another program"** — the message names the owning process. Close it,
or pick a different device from the **vJoy output** list on the Setup tab, which shows every device
vJoy reports along with its button count and whether anything already holds it.

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

## Documentation

| | |
| --- | --- |
| [docs/tuning.md](docs/tuning.md) | Every dial, and symptom → dial when something feels wrong |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Building, testing, deploying, and how the code fits together |
| [docs/hardware.md](docs/hardware.md) | Measured facts about the base, the USB path, and MOZA's software |
| [docs/force-model.md](docs/force-model.md) | How the gate is built, and every approach that was tried and rejected |
| [docs/architecture.md](docs/architecture.md) | Threading, lifecycle, effect handling, safety |
| [AGENTS.md](AGENTS.md) | Contributor and agent orientation, with the invariants |

## Licence

MIT — see [LICENSE](LICENSE), whose second paragraph is the warranty and liability disclaimer
behind *Read this first*. Attribution, trademark notices and the clean-room note for BonusFFB are
in [NOTICE.md](NOTICE.md).
