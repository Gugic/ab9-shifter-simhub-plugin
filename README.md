# AB9 Active Shifter

[![CI](https://github.com/Gugic/ab9-shifter-simhub-plugin/actions/workflows/ci.yml/badge.svg)](https://github.com/Gugic/ab9-shifter-simhub-plugin/actions/workflows/ci.yml)

An alternative to the MOZA AB9's own shifter mode. This SimHub plugin renders the shift gate
itself in force feedback — including the **configurable lockout** (push-through or hotkey-released, guarding 7th and reverse out of the box) that
the stock firmware has no setting for — and plays a **much wider range of telemetry effects**
through the lever: a clutch grind that can refuse the gear, engine vibration, a rev limiter,
ABS and traction control, curbs. Choose **direct vJoy buttons** or **native SimHub Control Mapper
roles** for the selected gear, and configure keyboard or controller bindings to suit your game.

Main brings the profile, pattern and live monitor together; Geometry, Feel and Effects open as focused editors.

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

This guide follows current `main`. Control Mapper output and the updated mode labels are
newer than v0.14.0; that release uses direct vJoy output.

On a supported AB9, initial setup happens inside the plugin. Install it, open **AB9 Shifter**
in SimHub's sidebar, and work through **Setup** once. After **Finish setup**, the plugin opens
**Main**; the same rig controls remain in **Options**. Restarting SimHub or disconnecting the
base does not send you through setup again.

### 1. Install

You need **SimHub**, .NET Framework 4.8, and a DirectInput force-feedback flight stick. The
**MOZA AB9** is the tested base; its onboard controls require firmware **1.1.5.2 or newer**.
Other DirectInput FFB sticks use the generic mode and require their own polarity measurement.
For gear output, choose either SimHub's **Control Mapper** or **vJoy** in step 3.

1. Download the zip from the [latest release](https://github.com/Gugic/ab9-shifter-simhub-plugin/releases/latest).
2. **Close SimHub**, then copy `AB9ActiveShifter.dll` from the zip into
   `C:\Program Files (x86)\SimHub\`. SimHub locks the DLL while running.
3. Start SimHub and enable **AB9 Active Shifter** under **Settings → Plugins**. Open
   **AB9 Shifter** in the sidebar.

A fresh install starts with forces off, the 10% polarity cap in place, and the **Setup** page.
Building from source instead: [DEVELOPMENT.md](DEVELOPMENT.md).

### 2. Choose the mode and prepare the base

The **OPERATING MODE** choice describes who drives the base. It is separate from the gear
**Output** choice in the next step.

| Mode | What it does | Preparation |
| --- | --- | --- |
| **Moza AB9** | Plugin gate, profiles and telemetry effects, with the basic spring, damper, friction and inertia processed onboard | Connect an AB9 on firmware **1.1.5.2+**, then use **Prepare base** |
| **Generic FFB stick** | The same plugin gate and profiles, with base effects sent through DirectInput | Configure the stick in its own software and follow **BEFORE YOU START** |
| **Moza AB9 native H-Pattern** | MOZA's firmware shifter and physical AB9 buttons; plugin profiles, tuning and output are disabled | Configure the firmware shifter in **Moza Pit House / AZOM**; no plugin polarity calibration is needed |

**For Moza AB9:** close Cockpit, Pit House and AZOM's AB9 connection so the configuration port
is free. Select **Moza AB9**, then click **Prepare base** under **1 · Prepare the base**. This
selects flight mode and DirectInput feedback, applies the profile's onboard settings, verifies
them, and leaves plugin output off. You do not need to enter the Cockpit values by hand.
If the base was just connected or its port was busy, use **Refresh base** and try again.

![First-run Setup with Moza AB9 selected: Prepare base and the direct vJoy output](docs/img/setup-ab9.png)

**For Generic FFB stick:** follow the numbered **BEFORE YOU START** checklist. Select
DirectInput feedback and turn off built-in centring and other background effects in the
stick's own configuration app, then close applications holding it exclusively. Under
**2 · Base and output → Base device identity**, enter its USB **Vendor id** and **Product id**
in hexadecimal. The default `346E / 1000` identifies the AB9. For an AB9 using this mode,
see the [manual preparation steps](#manual-ab9-preparation-for-generic-mode).

Selecting a mode saves a preference and turns plugin output off; it does not change the
base's firmware mode by itself. All modes can be selected while disconnected, but onboard
configuration requires a verified, connected AB9 with supported firmware. **Base is not found**
means you need to connect and power on the selected device before continuing.

Both virtual modes use **one set of profiles and the same percentages**. Only the provider of
Cockpit-compatible base effects changes; the gate, detents, lockouts, software stability and
telemetry effects continue through DirectInput in both modes. Equal percentages do not imply
equal physical strength. [Native configuration details](docs/native-ab9.md).

### 3. Choose how gears reach the game

Open **2 · Base and output** and select **Output**. After setup, these controls live in
**Options → Base and output**.

| Output | Setup |
| --- | --- |
| **vJoy (direct)** | Install [vJoy](https://sourceforge.net/projects/vjoystick/), create a virtual device with **14 buttons** in Configure vJoy, then select it under **Device**. Use **Refresh** after changing its configuration. The picker shows button counts and ownership. |
| **SimHub Control Mapper (native)** | Enable SimHub's Control Mapper feature, configure its keyboard or controller output, and assign existing roles to the plugin's gear mappings. Follow the steps below. |

Fourteen vJoy buttons cover every pattern. An eight-button device covers H patterns but cannot
send sequential buttons 9–10 or PRND buttons 11–14. If Control Mapper also uses vJoy, give it a
different virtual device so the two outputs do not compete for ownership.

For **SimHub Control Mapper (native)**:

1. Read the feature status. If needed, click **Enable Control Mapper and restart SimHub** or
   **Restart SimHub to load Control Mapper**. **Controls and events** is a different feature.
   If automatic enabling is unavailable, enable Control Mapper in SimHub's **Add/remove features**
   and restart.
2. Click **Configure Control Mapper**, then **Assign roles**. Configure roles under **Keyboard**
   for simulated keys, or use its vJoy/Arduino bridge output for controller buttons.
3. Return to the plugin, click **Refresh roles**, and assign roles under **H-pattern mappings**,
   **Sequential mappings** and **PRND mappings**. All three groups are shown together. Typing
   a name here does not create a role; blank mappings send nothing.
4. Leave **H-pattern neutral (optional)** blank unless the game needs an explicit neutral key.
   PRND's N is different: it has its own held role, just like P, R and D.

The output choice and assignments belong to the rig, so switching or importing profiles keeps
them. See [gear-output details](docs/tuning.md#setup-and-gear-output) for role examples and
connection behavior.

### 4. Measure polarity and finish setup

Both **Moza AB9** and **Generic FFB stick** require this: the plugin drives their gate through
DirectInput, and effect direction can differ by axis and effect family.

![First-run polarity calibration, with the default 10 percent probe and Finish setup still disabled](docs/img/setup-polarity.png)

1. Under **3 · Measure polarity**, take your hands off the stick and click **Measure polarity**.
   Leave the lever free while the short probes run. The action opens the base for calibration;
   you do not need to enable the shifter first.
2. Wait for a conclusive result for all four combinations: push and spring on each axis.
   The plugin saves the measured signs and lifts its **10% force cap** automatically. In
   **Moza AB9** mode it temporarily clears onboard resistance for the probes, then restores
   the profile's base effects after successful calibration. Plugin output stays off.
3. Click **Finish setup** to open **Main**. This becomes available when the base is present,
   polarity is confirmed, and the chosen output is ready. For Control Mapper, that means
   its feature is loaded and at least one assigned role exists for the current pattern;
   still check every required binding in the game.

If a probe says the stick **barely moved**, leave the cap in place. Check that nothing is
resisting the lever and, in generic mode, that built-in centring is off. Only then raise
**Calibration force (%)** from its 10% default and try again. Do not tick the manual
**Polarity confirmed - remove the 10% force cap** override to bypass an inconclusive result.

Calibration belongs to the rig, not a profile. You do not repeat it for each preset. To
revisit these controls later, open **Options → Polarity calibration**; **Measure again**
reveals the controls, then **Measure polarity** starts a new measurement.
**Options → MAINTENANCE → Review setup** reopens the checklist
without deleting the saved calibration.

![Successful calibration unlocks Finish setup](docs/img/setup-complete.png)

### 5. Choose a profile and enable the shifter

**Main** brings the connection status, **Profile**, **Pattern**, master switch and **Live gate**
together. Pick a preset for the pattern you want, open **Feel…**, and check **Overall gain (%)**
before enabling force. Start low and raise it gradually with a hand on the lever.

![Main: profile and pattern beside the live gate, with Geometry, Feel and Effects editors](docs/img/main.png)

The screenshots show example settings, not recommended starting strengths.

There are eight presets: **7+R lockout**, **7+R lockout (short throw, loose)**, **5+R**, **5+R wide**,
**Sequential**, **Sequential (stiff, short)**, **Automatic (PRND)** and
**Truck 6-gear (low-range lockout)**. They are marked `(Preset)` in the picker. Changing a
profile dial automatically creates an editable copy and keeps the preset available; use
**Duplicate** to make a copy deliberately. [Preset details](docs/tuning.md#presets-and-why-your-profile-just-renamed-itself).

Turn on **Shifter enabled** when ready. This is the master switch for both the base and gear
output. **Release all forces (free stick)** releases the plugin's DirectInput forces while
keeping the connections; onboard AB9 resistance can remain. Turning the master off clears
buttons and releases both devices.

While the master is off, **Stopped**, **Base: not connected** and **vJoy: not active** describe
the engine's released connections. They do not mean that the calibration was lost; Options
shows the separate base-detection status.

Settings apply as you edit them. Software settings apply on the next FFB tick; onboard effects
and hardware gains apply after about **500 ms** without another edit. A successful ordinary
update preserves the master switch while briefly pausing output for verification. Mode changes,
setup, calibration and failed writes leave output off. There is no Apply button.

### 6. Bind and check the game output

For **vJoy (direct)**, bind the virtual controller's buttons:

| Pattern | Buttons |
| --- | --- |
| H pattern | **1–7** for forward gears; **8** for reverse in every pattern that has it |
| Truck six-slot gate | **1–6**; no reverse |
| Sequential | **9** for up, **10** for down |
| Automatic PRND | **11**, **12**, **13**, **14** for P, R, N, D |

Check the vJoy device in Windows Game Controllers (`joy.cpl`): a held H gear or PRND position
holds its button; sequential produces a pulse. H-pattern neutral releases the gear buttons.

For **SimHub Control Mapper (native)**, bind the configured keys or controller buttons in the
game. Check direct gear selection and return to neutral, plus every sequential or PRND mapping
you use. The plugin knowing a role exists does not prove its external output is configured.

In the two virtual modes, do **not** bind the AB9's own axes as game controls. If a game takes
the base exclusively, use [HidHide](https://github.com/nefarius/HidHide): allow `SimHubWPF.exe`
first, then hide the AB9's *HID-compliant game controller* interface (`MI_02`) from the game.
Leave its `MI_00` configuration port visible. The game only needs your chosen gear output.
For **Moza AB9 native H-Pattern**, make the physical AB9 visible instead and bind its own buttons.

### Where settings live after setup

| Screen | What belongs there |
| --- | --- |
| **Main** | Status, profile management, pattern, live gate, master/free-stick switches, automatic profile switching and sharing |
| **Geometry…** | Pattern size, travel, corridors, mouths, detection, lockout position/direction/width; the live monitor stays above the scrolling controls |
| **Feel…** | Overall gain, base effects, wall/detent/lockout strengths and software stability; **Base-driven effects** identifies onboard processing in Moza AB9 mode |
| **Effects…** | SimHub's native effect editor, effect gain/mute, clutch behavior and feedback routed to **Lever** |
| **Options** | Operating mode, base/output, calibration, pedals, hotkeys, diagnostics and traces, maintenance, updates and About |

**Geometry…**, **Feel…** and **Effects…** open separate editors from Main. **Done** closes the
editor; changes have already applied. The available controls follow the selected pattern.
Opening Feel reads native AB9 settings once; it does not keep polling them while you drive.
Use **Options → Refresh base** for another read.

### Manual AB9 preparation for generic mode

<details>
<summary>Use this for Generic FFB stick, including AB9 firmware older than 1.1.5.2</summary>

Supported AB9s using **Moza AB9 → Prepare base** can skip this procedure.

In **MOZA Pit House**, set **AB9 Mode** to **Flight Simulation Base**. Firmware shifter mode
runs its own gate and does not provide the flight axes the plugin needs.

![Pit House: AB9 Mode set to Flight Simulation Base](docs/img/pit-house-mode.png)

In **MOZA Cockpit**, select DirectInput feedback and configure these basic settings:

| Setting | Value for generic mode |
| --- | --- |
| Force Feedback Mode | **DirectInput** |
| Spring, Damper, Inertia, Friction | **0%** |
| Maximum Torque Output | **100%** |
| Overall Force Feedback Intensity | **100%** |
| Game Force Feedback Gain | **100%** |

**Spring 0% matters:** the AB9 ignores DirectInput's request to disable its built-in centring.
Leaving it on makes the hardware fight the plugin's gate. Turn the other background effects
off too so generic mode owns them. Then **exit Cockpit completely** and close Pit House's
tuning page before returning to the plugin. Continue with output and polarity measurement above.

</details>

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
force (90% in the shipped profiles) times its width, and both are adjustable.

That is what the lockout is *for*: without one there is nothing between 5th and reverse but empty
travel, and a rushed downshift can find it. The whole design follows from wanting a barrier that a
hurried hand cannot get through by accident and a deliberate one can.

The lockout is **configurable** per profile: put it between any two columns or on a single slot's
mouth (a reverse lockout, say), point it either way or both, or turn it off. Beyond the
push-through toll there are two **hard modes**: the gate runs at full force and locked gears do
not register at all until a bound key releases it — one stays released until pressed again, the
other re-arms itself once the crossing completes, like a collar seating behind the shift. The
automatic's selector lane can carry a lockout of its own between chosen positions (P–R for an
out-of-park gate, R–N for a reverse guard), force-only — the selector always reports where the
lever really is.

Two rules make it feel mechanical rather than like a set of forces. A gear can only be left
**through the neutral tunnel** — leaning sideways, or shoving through a wall, will not hand you a
different gear, it just pushes you back into the one you are in. And pushing into a gear slightly
off-column is guided onto the slot by its **tapered mouth** rather than dead-ending against the
divider, the way a real gate's chamfered entry feeds the lever in.

An optional **neutral spring** (off by default) pulls the lever toward the 3/4 column while in
neutral, fading out with depth — dial it up and a released lever drifts home across the notches,
the way a real H lever rests at the 3/4 gate.

## Patterns

Six, selectable per **profile** on Main:

| Pattern | |
| --- | --- |
| **7+R** | The full gate above, with the lockout |
| **6+R** | The slot where 7 would sit genuinely does not exist — the wall over it never opens, so the lever cannot enter it at all. The stock firmware's 6+R leaves the seven-gear gate rendered with that slot merely inert, which is no guard against the misshift that choosing six gears is meant to prevent |
| **5+R** | Three wider columns, no lockout by default. Shipped twice - narrowed to 60% of the stick so a shift is about the reach a 7+R asks for, and *wide* at the full sweep |
| **Sequential** | A sprung fore/aft lever: one shift per stroke, with a click you can tune |
| **Automatic (P R N D)** | A selector lane: four fixed positions in a line, a button held at whichever one the lever is in. No neutral to come back through and no gear to engage — the lever is always somewhere |
| **Truck 6** | Three wider columns, six plain slots on buttons 1–6, no reverse anywhere — what each button means is your game's business. Made for Eaton-Fuller-style boxes: add the lockout between the first two columns (the shipped truck preset does exactly that) and you have a low-range gate |

**How wide the pattern stands is a dial too.** By default the columns are spread over the whole
stick, which is right for 7+R and a lot of reach for the three-column patterns — 5+R and the truck
6 put half as many columns across the same travel, so each shift crosses half again the distance.
*Pattern width* in Geometry squeezes the pattern in from both sides, keeping its middle
where it is, with the live gate above the slider showing it happen. Around 67% gives a
three-column pattern the same reach a 7+R has.

Narrowing leaves bare travel outside the outermost columns, with no gear in it and — because the
neutral tunnel is deliberately free — nothing to stop the lever sliding into it. *Wall at the
pattern edge*, under the same slider, is that edge: one-way, inward only, zero everywhere inside
the pattern, and it renders nothing at all at full width.

A profile stores every dial together with its pattern, so each pattern keeps its own tuning and
switching between them is one dropdown. *Next profile* and *Previous profile* are bindable actions
if you would rather not use the dropdown.

**A profile can also claim cars.** Open **Automatic profile switching** on Main and enter one
vehicle ID per line. **Add last vehicle** uses the ID the game last reported. A matching vehicle
activates that profile when the car changes; leave the list empty for manual selection.
**Sharing and button mapping** is a separate, compact help section.

**Profiles can be exported and imported**, so a tune can be shared as a file. What travels is the
tuning only: your measured polarity, your device and vJoy numbers, your loop rate and your car
mappings stay as they are on your machine. An import always *adds* a profile, numbering the name if
it is taken, so someone else's file can never land on top of yours — and it always arrives with
virtual forces off, with every value range-checked on the way in. In Moza AB9 mode, the
imported profile's onboard base settings apply automatically while virtual output stays off.

## Game effects

With a game running, **Main → Effects…** plays telemetry through the lever: **gear grind on a
clutchless shift** — push into a gear with the clutch up and the box rattles against a firm balk
wall, louder the harder you force it, and optionally the gear refuses to register until the
clutch goes down, like a blocking synchro ring — plus engine vibration that tracks the revs, a
rev-limiter buzz, ABS and traction-control buzzes, a curb-and-bump rattle read out of the car's
vertical acceleration, a gear-shift confirmation pulse, and a custom effect driven by any SimHub
property (which puts ShakeIt's exported effect groups on the lever).

Effects ride on top of the gate without changing its geometry. Ordinary effects fall silent
within half a second of stale or inactive telemetry; a running **Test** can play without a game.
Check the selected profile's enabled rows before driving: presets can include enabled effects.
The grind uses either the game's clutch reading or a pedal bound under **Options → Pedals**.

![Effects editor: profile gain, clutch behavior and SimHub's native effect rows](docs/img/effects.png)

## Tuning

- **Feel** — master gain, base effects, wall and detent strengths, lockout resistance, attack,
  rebound absorption and software stability. Force curves come from the actual force model,
  with the live stick position shown; each slider supports undo.
- **Effects** — SimHub's telemetry-effect editor, including gain, frequency, filters and groups.
- **Geometry** — pattern size, travel, corridor and mouth shapes, lockout placement and widths,
  and gear detection. The live monitor stays above its controls while you scroll.
- **Main** — the live gate beside the profile, pattern and force controls. **Options → Diagnostics**
  holds the trace recorder and loop rate.

Software changes apply on the next FFB tick. Moza AB9's seven onboard dials apply after
500 ms without another edit, pausing output until the latest settings are verified. An enabled
session resumes after a successful update; turning it off or pressing panic always takes
precedence. No Apply button or restart is needed.

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
| `StatusMessage` | the same working status shown on Main |

Events: `GearEngaged`, `GearReleased`, `LockoutEngaged`, `LockoutReleased`. A `LockoutEngaged`
property says whether a hard-mode lockout is currently armed (always true when no hard mode is
configured).

### Actions you can bind to a button

| Action | What it does |
| --- | --- |
| `NextProfile` | Move one step around the profile cycle. **This is the one to bind** if you want a single button that swaps between, say, an H gate and a sequential lever. |
| `PreviousProfile` | The same, backwards. Only worth binding once three or more profiles are in the cycle. |
| `ToggleShifterFFB` | Turn the shifter's force feedback on and off. |
| `ReleaseAllGears` | Drop every held gear button and stop output — the panic button. |
| `ToggleLockout` | Release or re-engage a hard-mode lockout — the one-button key. Does nothing in push-through mode. |
| `EngageLockout` / `ReleaseLockout` | The same as an explicit pair, for a two-position switch that a toggle would fall out of step with. |

**Bind them in Options → Hotkeys**, under **PROFILE HOTKEYS** or **FORCES AND LOCKOUT HOTKEYS**.
Click the row, press the wheel
button or key, done. SimHub's own **Controls and events** page shows the same bindings if you
prefer to manage them all in one place — they are the same actions either way, listed there as
`AB9ShifterPlugin.NextProfile` and so on.

Which profiles `NextProfile` walks through is set under **Options → Hotkeys → PROFILE HOTKEYS** —
tick the ones you want in the ring, or tick none and it walks through all of them.
Switching releases any held gear and clears a sequential pulse in flight before the new gate is
applied, so it is safe to press while driving.

## Updating the plugin

The plugin checks this repository's latest stable GitHub release on startup and every six
hours. Under **Options → UPDATES**, **Check now** checks immediately; **Check for updates
automatically** turns scheduled checks on or off. During first-run setup, the same **UPDATES**
section is farther down the **Setup** page and works before calibration is complete.

When a newer release is available, a banner above the tabs offers **Install update**, **Open
release notes**, and **Dismiss**. Install downloads and verifies the DLL, then offers **Restart
SimHub** to load it when you are ready. Profiles and calibration stay saved. Dismiss hides that
version's banner; the Options tab still shows it, and a newer release appears again.

The updater requires the release's standalone `AB9ActiveShifter.dll` asset and its GitHub
SHA-256 checksum. If the release has no installable asset, use Open release notes to install
manually. If SimHub cannot write to its install folder, run it as administrator or copy the
release DLL there with SimHub closed. Offline checks leave the plugin running and can be retried.

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
or pick a different device under **Options → Base and output → Device**, with **vJoy (direct)** selected, which shows every device
vJoy reports along with its button count and whether anything already holds it.

**The stick fights you everywhere, or drifts to the stops** — check polarity and background
centring. Under **Options → Polarity calibration**, use **Measure again**, then **Measure polarity**
to check whether the gate's forces are pushing the wrong way.
To isolate the plugin's forces, turn on **Release all forces (free stick)** on Main. In
**Moza AB9** mode, the **Base-driven effects** in Feel can still provide onboard resistance;
check **Base spring (%)** there. In generic mode, re-check that centring is off in the base's
own software.

**A wall buzzes, or kicks back like ABS** — see [docs/tuning.md](docs/tuning.md). Short answer:
adjust *wall bite distance* first, then *wall attack*.

**Everything feels dead, especially the lockout and the detents** — those are constant forces.
Confirm *Measure polarity* reported a result for both push axes rather than "barely moved", and
that overall gain is not near zero.

**Control Mapper sends nothing** — check its feature status in **Base and output**. Use
**Enable Control Mapper and restart SimHub** if disabled, then **Configure Control Mapper**
to configure its output and check that each assigned role still exists. An enabled mapper
without roles needs configuration. Press **Refresh roles** after editing its configuration. For keyboard output,
check the game's keyboard bindings and neutral behavior; for controller output, inspect its
vJoy or bridge device in `joy.cpl`.

**Direct vJoy gears do not register in the game** — check `joy.cpl`: the vJoy device should light button *i*
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
