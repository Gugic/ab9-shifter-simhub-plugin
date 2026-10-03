# Native AB9 configuration

Generic virtual profiles render their gate through DirectInput and send gears through vJoy.
They remain usable with other FFB flight sticks; device ids and polarity still belong to the
rig. An **AB9 native** profile instead stores the base's own settings and uses its firmware gate
and physical gear buttons. Virtual force geometry, telemetry effects, calibration and vJoy are
disabled for that profile. Native engine/shift vibration streaming is not implemented here.
Bind the AB9's own gear buttons in the game. If HidHide hides the base for virtual use, make
it visible to the game when using its native gate.

## Setup and profiles

On **Setup**, **AB9 NATIVE SETUP** shows native controls only after a successful read from a
live `usbser` interface with VID `346E`, PID `1000`, and firmware **1.1.5.2 or newer**. AB6,
wheelbases, unknown devices, unknown firmware and older firmware are excluded. The selected
virtual device ids must also identify the AB9. Multiple connected AB9s are refused rather than
choosing one arbitrarily. Close Cockpit, Pit House and AZOM's AB9 connection to release the COM
port. The serial port is released after each read or write transaction.
The initial mode check runs before virtual output starts; the open Setup page refreshes
hardware status every five seconds. Profile switches and profile-list edits wait for writes
to finish.

**Set up native H pattern** performs setup and creates a marked native profile in one action:
7+R layout 1, torque 25%, overall intensity and game gain 100%, spring 50%, damper 15%, inertia
and friction 0%, mechanical resistance 50%. These are conservative starting settings, pending
feel verification on the rig. If a native profile is already selected, its saved values are used.

**Set up virtual gate** selects flight mode, DirectInput, Spring 0%, Damper 15%, Inertia and
Friction 0%, and torque/intensity/game gain 100%. It returns to the previous virtual profile
when possible. Forces remain off: measure polarity, then deliberately enable the virtual engine.
The existing 10% unconfirmed-polarity cap still applies to every virtual force.

Native profiles carry **Maximum Torque Output (%)**, **Overall Force Feedback Intensity (%)**,
**Spring (%)**, **Damper (%)**, **Inertia (%)**, **Friction (%)**, **Game Force Feedback Gain (%)**,
**Gear Shift Mechanical Resistance (%)**, and **Native layout**. Edits autosave as a profile
draft. **Apply native profile** writes them; selecting an existing eligible native profile also
applies it. Layouts are the ten documented MOZA layouts, including sequential.

The dropdown marks these profiles **[AB9 native]**. They cannot activate without a compatible
AB9 selected and already in native shifter mode. Hotkey cycling skips unavailable native
profiles; vehicle auto-selection obeys the same check. A saved native profile without eligible
hardware stays a saved, unavailable profile and never falls back to driving another stick.

Native exports use profile format 2, which older plugin builds refuse. Generic exports keep
format 1. Imports are range checked and never send native writes or arm virtual output. A native
import without eligible hardware is saved without activation. Firmware, port and connected state
are runtime facts and never travel in a profile. The native tuning dials do travel and clone
independently; they are intentionally per profile, unlike measured polarity and device ids.

## Protocol evidence

Independent implementation from protocol facts; no AZOM implementation code was copied. Sources:
[AZOM's AB9 protocol notes](https://github.com/giantorth/AZOM/blob/main/docs/protocol/devices/ab9-shifter.md),
[frame format](https://github.com/giantorth/AZOM/blob/main/docs/protocol/wire/frame-format.md),
[checksum/escaping](https://github.com/giantorth/AZOM/blob/main/docs/protocol/wire/checksum.md),
and the installed Cockpit `as23_parameter.db` / DeviceSdk command metadata (read only).

Transport: 115200 baud, 8N1, AB9 CDC interface `MI_00`, target device `12`; replies use `21`.
Frame: `7E N group device payload checksum`. Zero-length replies are valid. Checksum starts at
`0D` and includes preceding wire bytes. Body/checksum `7E` bytes are doubled on wire. Discovery
uses the exact USB registry identity and live COM inventory, then a group `09` presence reply.

| Parameter | Command | Width | Values |
| --- | --- | --- | --- |
| Input mode | `5D` | 1 byte | Flight `0`, native shifter `1` |
| Force Feedback Mode | `85` | BE16 | Telemetry `0`, DirectInput `1`, both `2` |
| Maximum Torque Output | `A9` | BE16 | 0–100% |
| Overall Force Feedback Intensity | `AE` | BE16 | 0–100% |
| Spring | `AF` | BE16 | 0–100% |
| Damper | `B0` | BE16 | 0–100% |
| Inertia | `B1` | BE16 | 0–100% |
| Friction | `B2` | BE16 | 0–100% |
| Game Force Feedback Gain | `99` | BE16 | UI/support limited to 0–100% |
| Native layout | `D3` | BE16 | 0–9 |
| Mechanical resistance | `D6` | BE16 | 0–100% |

Reads use group `1E` and writes `1F`. Mode/layout/game gain reads are short; Cockpit pads the
other reads with two zero bytes. Writes are `command high low`, except input mode which is
`5D value`. A bare `9F` ACK proves no value: each write requires a fresh matching `9E` readback.
Firmware query is `7E 04 04 12 00 00 00 00 A5`. The rig replied `84 21 01 01 02 05`; Cockpit's
firmware screen showed **1.1.5.2**, establishing major/minor/build/patch wire order.

The plugin's own read-only probe on 2026-10-03 found COM11, firmware 1.1.5.2, flight mode,
FFB mode 2, torque 100%, overall intensity 70%, spring 50%, damper 30%, inertia/friction 10%,
game gain 100%, layout 0 and mechanical resistance 60%. The seven basic dials matched the
provided Cockpit screenshot. An earlier probe that day saw intensity 100% and spring 0%.
This verifies discovery and the full read path, but not writes or how native defaults feel.
Cockpit's SDK names
DirectInput mode 1 and torque percentage 0–100; its SQLite database's broader torque bound of
5000 is not used as a percentage limit.

Cockpit's **Conservative / Aggressive** selector is a thermal control strategy, according to
its SDK metadata (`C0`, BE16). That selector is not implemented here; setup preserves the
base's existing thermal policy. No temperature-protection commands are sent.

## Threading and failure ordering

CDC work runs on a separate worker under a semaphore, never on the 1 kHz force loop or SimHub
telemetry thread. Settings are copied and fully validated before a transaction. Firmware and
mode are re-read after opening the exact AB9 port, before any write.

Every write action first disables virtual output through the existing teardown: buttons off,
forces off, unacquire. Hardware torque is then set to 0 and read back, the other settings are
written/read back, the mode is written/read back, and requested torque is restored last. A
failure stops the transaction and reports partial configuration; no automatic rollback or
virtual restart occurs. Uncertain mode blocks virtual output on the selected AB9 until a fresh
read establishes its mode. This restriction never blocks an unrelated FFB stick.
