# AB9 Active Shifter

[![CI](https://github.com/Gugic/ab9-shifter-simhub-plugin/actions/workflows/ci.yml/badge.svg)](https://github.com/Gugic/ab9-shifter-simhub-plugin/actions/workflows/ci.yml)

A SimHub plugin that turns a force-feedback flight stick into an H-pattern shifter,
sequential lever or automatic PRND selector. It renders the gate through force feedback
and sends gears to games through **vJoy** or **SimHub Control Mapper**.

The **MOZA AB9** is the tested base, with onboard setup and base effects on firmware
**1.1.5.2+**. Other DirectInput FFB sticks use the generic mode. Both virtual modes share
the same profiles and tuning percentages.

**Unofficial, early software.** Not affiliated with MOZA, SimHub or vJoy. An AB9 can produce
12 Nm; unexpected forces can injure you or damage equipment. Use at your own risk, start
with low gain, and read the [full safety notice](docs/safety.md) before enabling forces.

## Features

- **Six patterns:** 7+R, 6+R, 5+R, a six-slot truck gate, sequential and automatic PRND.
- **Configurable gates:** adjust width, travel, slot mouths, walls, detents and neutral centring.
- **Lockouts:** guard a gap or slot, in either direction, with push-through or hotkey release.
- **Game feedback:** clutch grind and gear rejection, engine vibration, rev limiter, ABS,
  traction control, road impacts and shift pulses through SimHub's effect editor.
- **Optional float shifting:** rev-matched clutchless shifts across all H patterns, with
  game targets, configured ratios or session learning. [Setup and limits](docs/usage.md#float-shifting).
- **Profiles:** eight presets, editable copies, import/export, hotkey cycling and automatic
  selection by vehicle.
- **One-time setup:** AB9 preparation, measured polarity and output configuration; Main then
  brings together the profile picker, live gate and Geometry, Feel and Effects editors.
- **Flexible rig controls:** onboard AB9 or generic DirectInput base effects, a mode for using
  MOZA's firmware H-pattern shifter, pedal input, hotkeys and diagnostic traces.

## Quickstart

You need **SimHub**, **.NET Framework 4.8**, a DirectInput FFB stick, and either **vJoy** or
**SimHub Control Mapper** for gear output.

1. **Install.** Download the zip from the [latest release](https://github.com/Gugic/ab9-shifter-simhub-plugin/releases/latest).
   Close SimHub, copy `AB9ActiveShifter.dll` into `C:\Program Files (x86)\SimHub\`, then restart.
   Enable **AB9 Active Shifter** under **Settings → Plugins** and open **AB9 Shifter**.
2. **Prepare the base.** In Setup, choose **Moza AB9** and click **Prepare base** after closing
   Cockpit, Pit House and AZOM's AB9 connection. For **Generic FFB stick**, follow its manual
   checklist and set the device identity. [Mode and preparation details](docs/setup.md#2-choose-the-mode-and-prepare-the-base).
3. **Choose output.** Select a direct vJoy device with **14 buttons**, or configure Control Mapper
   and assign its gear roles. [Output instructions](docs/setup.md#3-choose-how-gears-reach-the-game).
4. **Measure polarity.** Take your hands off the lever, click **Measure polarity**, and wait for
   all four probes to succeed. Then click **Finish setup**. Leave the 10% cap in place if measurement
   is inconclusive. [Calibration walkthrough](docs/setup.md#4-measure-polarity-and-finish-setup).
5. **Try a profile.** On Main, pick a preset, check **Feel → Overall gain** and start low. Turn on
   **Shifter enabled**, bind the selected output in your game and check every gear.
   [Bindings and checks](docs/setup.md#6-bind-and-check-the-game-output).

These docs follow current `main`; Control Mapper output, float shifting and the updated mode
labels are newer than v0.14.0, which uses direct vJoy output.

For screenshots and the full walkthrough, see the **[setup guide](docs/setup.md)**.
For everyday controls, see **[usage](docs/usage.md)**, **[tuning](docs/tuning.md)** and
**[troubleshooting](docs/troubleshooting.md)**. The **[documentation index](docs/README.md)**
also covers native AB9 configuration and development.

MIT — [license](LICENSE) · [notices and attribution](NOTICE.md).
