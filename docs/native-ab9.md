# Native AB9 configuration

The mode switch belongs to the rig, while the gate tune belongs to a profile:

| Mode | Gate and gear output | Basic base effects |
| --- | --- | --- |
| **Generic FFB stick** | Plugin geometry, DirectInput forces and selected vJoy or Control Mapper output | DirectInput spring, damper, friction and inertia |
| **Moza AB9** | The same plugin geometry, forces, telemetry effects and selected gear output | Cockpit-compatible onboard settings |
| **Moza AB9 native H-Pattern** | MOZA's firmware gate and physical AB9 buttons | Managed outside the plugin; plugin tuning is unavailable |

Both virtual modes keep the plugin's additional effects: wall damping and friction, detents,
lockouts, home spring and game-driven feedback. Only the basic effects supported by Cockpit
change provider. DirectInput base effects are suppressed in Moza AB9 so they do not double
its onboard effects. Moza AB9 native H-Pattern releases the virtual engine and either plugin gear output entirely.
If HidHide hides the AB9, make it visible to the game to bind its firmware gear buttons.

## Setup and profiles

For the illustrated install and first-run steps, see [setup guide](setup.md).
The [tuning guide](tuning.md) maps the Main/Options workspace and its three editors.

First-run Setup gathers the mode, base identity, output device or roles and measured polarity. After
completion, Main shows the working shifter and Options retains those rig settings. A temporary
disconnect does not reset setup completion. Geometry, Feel and Effects open from Main; the
Geometry monitor remains above its scrolling controls.

The **Output** choice under **Base and output** selects **vJoy (direct)** or **SimHub Control
Mapper (native)** independently of the operating mode. It remains a rig preference across
shared-profile and provider changes. Both backends participate in calibration cleanup and
the pause/resume around onboard writes. Control Mapper dispatches roles asynchronously, so
the engine submits releases before stopping forces without claiming when the game sees them.

All three modes are selectable without connected hardware and with the master switch on or off.
Selection saves the rig preference and leaves plugin output off; it performs no hardware write.
**Moza AB9 native H-Pattern** is configured in **Moza Pit House / AZOM**, with the plugin disabled.
**Base is not found** warns on Setup/Options and Main when the selected base is disconnected.
No force or gear press is emitted without fresh input from an acquired base. The selected output
can remain acquired across a disconnect, with its buttons clear, when the master switch is on.

**Prepare base** in **Moza AB9** requires a successful read from a live `usbser` interface with VID `346E`,
PID `1000`, and firmware **1.1.5.2 or newer**. AB6, wheelbases, unknown firmware and older
firmware are excluded. Multiple connected AB9s are refused rather than choosing one arbitrarily.
Close Cockpit, Pit House and AZOM's AB9 connection to release the COM port. Each transaction
releases the port afterward. Generic FFB stick remains usable with other DirectInput bases.

Native settings are read once when Feel opens, with no periodic reads while driving or while
the editor stays open. Options' **Refresh base** action requests another read when needed. Status shows
the last checked snapshot; opening Main or Options alone does not access the configuration port.
Startup still checks the mode, and configuration changes still perform fresh checks and readback.

Moza AB9 preparation uses flight input mode and DirectInput feedback mode. Newly selecting this
provider waits for verified preparation before permitting virtual forces. Preparing calibration
neutralizes onboard conditions so the probes measure DirectInput alone. Calibration leaves
virtual forces off. Until polarity is confirmed, profile application also limits onboard
torque and leaves the onboard spring off; calibration uses its own bounded probe forces.

## One profile, two effect providers

Profiles and presets are shared. The same spring, damper, friction and inertia percentages
are sent either to DirectInput or to the AB9, depending on the rig's mode. Changing provider
does not clone profiles, retune percentages or create a second set of presets. The existing
device damper coefficient of 800 is represented as 8%; spring, friction and inertia default off.
Equal percentages are retained as requested, without claiming equal physical strength across
different hardware implementations.

Feel labels the onboard controls **Base-driven effects** and explains that the AB9 processes
them internally, avoiding the USB round trip. Hardware torque, overall intensity and game gain
are additional AB9-only controls; Generic FFB stick ignores those hardware settings. The rest
of the profile always uses the plugin's DirectInput gate and extra effects. Firmware H-pattern
exposes no plugin profiles or tuning.

The seven onboard dials apply automatically after 500 ms without another edit: spring,
damper, friction, inertia, torque, overall intensity and game gain. A drag sends the latest
values after the pause; it does not need an Apply button. Selecting a profile also applies
its onboard values. Ordinary edits and profile changes preserve the force-feedback toggle:
virtual output pauses during the transaction and resumes only after the latest values have
been read back successfully, if it was enabled and no later off or panic action cancelled it.
Free stick releases DirectInput effects, but onboard resistance can remain.

Imports are range checked and activate as a new profile with virtual output disabled.
In Moza AB9 mode, activation automatically applies the imported onboard values while keeping
virtual output off. Mode choice, firmware, connection state, port and measured polarity never
travel in a profile. Experimental native-only profiles from the earlier unshipped iteration
are discarded when adopting the three-mode store; existing generic profiles and presets are
retained.

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

Every write pauses virtual output through the existing teardown: buttons off, forces off,
unacquire. Hardware torque is then set to 0 and read back, the other settings and mode are
written/read back, and requested torque is restored last. `NativeSettingsDebounce` retains
only the latest copied tune after 500 ms of quiet. A busy read does not consume it; edits made
during a write become a follow-up transaction under the same output pause.

`NativeWritePause` separates that pause from the user's force-feedback toggle. Ordinary tuning
and profile activation can resume an already-enabled session only after the final readback.
Turning forces off or pressing panic cancels that permission immediately; completion never
restores an earlier enabled state over a newer off request. Setup, mode changes, calibration,
imports and failed transactions leave virtual output off. A failure discards the queued batch,
reports partial configuration and performs no automatic rollback. Uncertain mode blocks virtual
output on the selected AB9 until a fresh read establishes its mode. This restriction never
blocks an unrelated FFB stick.
