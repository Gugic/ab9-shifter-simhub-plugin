# Measured hardware facts

Everything here was measured on the actual hardware, not read from a datasheet. The unit is a
**MOZA AB9 force feedback flight base**, 12 Nm, in **flight mode**, where it enumerates as an
ordinary two-axis DirectInput force feedback joystick: **VID `0x346E`, PID `0x1000`**.

Numbers from a different base or firmware may differ. What should not differ is the *method* —
if you doubt one of these, re-measure it with a scratchpad probe while SimHub is stopped, and
update this file with what you find.

## Timing — the constraint the whole design bends around

| Quantity | Measured | Notes |
| --- | --- | --- |
| One `SetParameters` force write | **1.0 ms** (p50), up to ~2 ms | Hard-quantised to the USB frame clock. Not reducible. |
| Two writes back to back (X then Y) | **2.0 ms** | They serialise on the pipe; there is no batching. |
| `Poll` + `GetCurrentState` | ~1 µs | Effectively free, served from the driver's cache. |
| Fresh input reports | **~940 Hz** | The stick already reports about every millisecond — *when the write pipe is idle*. See the next row. |
| Distinct positions under write contention | **~500 Hz** | Measured from trace-20260726-034848: with force writes in flight every tick, alternate 1 kHz polls return an unchanged snapshot on **both** axes — a smooth ~17 000 count/s sweep polls as deltas of −34, −1, −36, −2, … |
| Position → torque round trip | **3–4 ms floor** | ~1 ms report age + 1.0 ms write + ~1 ms firmware application. |
| Hand speeds at the lever | lean ≤ **~3 700** counts/s; deliberate strokes **15 000–430 000** | From the 2026-07-27 traces, via `VelocityEstimator`: a hand holding steadily against force micro-reverses at up to ~3 700 counts/s (p99 2 889); ordinary slides run 15–45 k, full shift strokes 100 k+. This gap is what the yield's deadband (10 000) sits inside — see force-model.md. |

Consequences, all of which are baked into the current design:

- The loop runs at **1 kHz** and issues **at most one constant-force write per tick**. When both
  axes want the pipe they alternate, so each gets ~500 Hz; a single hot axis — which is what
  contact with one wall looks like — gets the full 1 kHz.
- Raising the loop rate further buys nothing: reads are already fresh every millisecond and the
  write pipe is the bound.
- **Never difference adjacent polls for velocity.** Under write contention half of them repeat,
  so the per-tick difference alternates ~2:1 at 250–500 Hz. Anything that keys force on that
  estimate renders the alternation as force texture — the rebound absorber at 59% produced a
  25–50% wall-force ripple felt as grinding. `Core/VelocityEstimator.cs` differences positions
  across a 4 ms window, an exact null for a 2 ms report clock.
- **The 3–4 ms round trip cannot be engineered away.** It is why stability comes from force
  *shape* rather than from rate or damping, and why a fast flick covers 1500–2000 axis counts
  inside the latency window no matter how fast the loop runs.
- **Vibration carriers render cleanly up to roughly 100–130 Hz.** One force write per tick at
  1 kHz gives ≈10 samples per cycle at 100 Hz; under two-axis contention each axis gets ~500 Hz,
  still ≈8 samples at 60 Hz. The telemetry effects' frequency dials stop at 120 Hz for this
  reason — above that the carrier degrades into aliasing rather than pitch.
- Pacing a 1 ms tick needs a high-resolution waitable timer
  (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`). `Thread.Sleep(1)` overshoots the entire budget;
  spinning burns a core. Both fallbacks remain for Windows builds without the flag.

## Effect strength — why the gate is built from constant forces

A DirectInput **spring** produces coefficient × displacement ÷ 10000. At the *maximum* coefficient
of 10000 that is about **0.305 DI units per axis count**:

- 500 counts past a wall → ~1.5% of full force. Felt as nothing.
- Reaching 70% force needs roughly **23000 counts** — a third of full travel.

So a spring cannot make a wall on this hardware at any setting. BonusFFB works around this by
reflecting the spring's anchor past the target each tick (`offset = target + (pos − target) ×
−1.3`), multiplying delivered force ~2.3× → ~0.7 DI/count. Better, still nowhere near a wall.
That trick's real virtue is different and worth understanding: it keeps the fast loop *inside the
firmware*, where a spring is a genuine zero-latency passive element and cannot ring. It buys
stability by construction and pays for it with a hard ceiling on stiffness.

This project takes the other trade: **every wall is a shaped constant force**, which reaches its
plateau in a few hundred counts and can be made properly firm, at the cost of having to solve
stability in software. See [force-model.md](force-model.md) for how.

The other condition effects — **damper, friction, inertia** — are similarly weak here. They are
close to decorative; the device damper is still set (`DamperCoeff`) but does no real work.
Velocity damping is computed in software from the axis readings instead.

## Effect polarity — four independent facts

Some AB9 firmware applies DirectInput effects backwards. On this unit it is **not** a single
global inversion — it differs by axis *and* by effect family:

| | X (left/right) | Y (fore/aft) |
| --- | --- | --- |
| Constant force | **inverted** | correct |
| Spring | correct | **inverted** |

Hence four flags (`InvertConstantX/Y`, `InvertSpringX/Y`) and a calibration that measures all
four. Until they are confirmed, overall gain is capped at 10%.

Polarity is **measured, never asked.** A human cannot report it: the base holds itself centred,
so a correct centring spring and the base's own centring feel identical, while an inverted one
just feels like a weaker hold. The calibrator pushes each way on each axis for each family and
scores whether the stick moved the commanded direction. Two details were needed to make it work:

- **Measure deflection from a per-probe origin**, not from a single resting baseline. With
  centring effectively off the stick does not return between probes, so a fixed baseline reports
  "inconclusive" on a correctly configured base.
- **Score each probe against its expected direction, then sum.** Subtracting the two probes
  cancels out for an inverted spring, which accelerates away from its anchor and therefore drives
  both probes the same way — it scored as "correct" or as zero.

A probe aborts as soon as its direction is certain (12000 counts of deflection), so an inverted
spring never reaches the stops.

## Self-centring — and the MOZA Cockpit settings that actually control it

**The AB9 self-centres in firmware, and `DIPROP_AUTOCENTER` is ignored.** Verified across five
configurations. The plugin still asks (harmless, logged on failure), but the request does nothing.

The centring is non-linear and strong:

- Near centre: ~1100 force units per 1000 counts.
- Far out: ~300 per 1000 counts.
- At full deflection the base drags the stick home with roughly **90% of its available force.**

That last number is why `DetentHoldPct` defaults to 55 and why a light seated hold simply loses
the argument and the gear falls back out.

The real control is **MOZA Cockpit** — *not* Pit House, which has no Spring setting in flight
mode. Required, once:

**The configuration is split across MOZA's two apps, and both are required.**

The firmware mode switch is in **Pit House**, under **AB9 Mode**: set it to **Flight Simulation
Base** rather than *Shifter*. That is what makes the base enumerate as the two-axis DirectInput
FFB joystick this plugin opens — in *Shifter* mode it runs its own gate in firmware and does not
present the axes at all. (The *Shifter Mode* dropdown below it, greyed out in flight mode, is the
stock 7+R this project exists to replace: fixed layouts, no lockout.)

Everything else is **Cockpit's** *Basic Settings* page, named exactly as that page names them:

| Setting | Value |
| --- | --- |
| Force Feedback Mode | **DirectInput** |
| Spring | **0%** |
| Damper | **15%** (recommended — see below) |
| Maximum Torque Output | 100% |
| Overall Force Feedback Intensity | 100% |
| Game Force Feedback Gain | 100% |
| Inertia, Friction | 0% |
| Firmware | 1.1.3.4 or newer; developed and tested against **1.1.5.2** |

The damping control on this page is called **Damper**, not "Natural Damping" — this file said the
latter until a screenshot of the actual page settled it (2026-07-28).

Then **fully exit Cockpit** — it holds the device exclusively while open, and the plugin cannot
acquire it. This was the fix for what looked for a long time like a plugin bug: the base fighting
the gate everywhere with its own centring spring.

**Damper** in Cockpit is zero-latency physical damping, applied at the servo loop —
ahead of the USB round trip, which is what makes it categorically different from anything this
plugin can render. Two verdicts from hardware, and both stand:

- It does **not** fix the wall-face buzz (the steep-gradient oscillation): tried early, the
  lever stiffened and the buzz stayed — damping cannot rescue a gradient that steep through any
  path.
- It **does** settle the lean-hunt (the residual 10–20 Hz hand-coupled hunt on faces, left over
  once the yield relay was fixed): **~15% is the user-verified setting** (2026-07-28). Software
  dissipation for that mode was tried the same night — wall friction at 15% of engaged force,
  ~17× the delay's negative damping on paper — and did not help, because everything the plugin
  renders arrives 3–4 ms late, a large fraction of a 17 Hz cycle. The firmware damper acts with
  no delay at all. Recommend ~15% Damper as part of setup for anyone chasing the last
  bit of lean calm.

## Exclusive access

The plugin takes the device `Exclusive | Background` — exclusive is required to create FFB
effects, background so forces stay live while the *game* has focus rather than SimHub.

Things that will take it away: **MOZA Cockpit**, a **Pit House** live-tuning page, and
occasionally a game. The failure surfaces as `DIERR_OTHERAPPHASPRIO` /
`DIERR_NOTEXCLUSIVEACQUIRED`; the engine backs off (1/2/5 s) and retries, so a transient grab
recovers on its own.

**Steam Input is not one of them, on this rig.** It was listed as a required setup step from the
start, inherited from BonusFFB's issue tracker rather than measured here, and in use with Steam
running normally it has never taken the device or disturbed the gate. It is off the setup
instructions and out of the checklist: an instruction that costs a user a step and buys nothing is
worse than no instruction. If it turns out to matter it will be for one game's controller
configuration rather than as a global setting, which is where to look before restoring it here.

A useful safety property falls out of this: if SimHub exits or crashes, dropping the exclusive
handle makes the driver discard the effects, so forces cannot outlive the process.

## Host environment

| | |
| --- | --- |
| SimHub | 9.11.21, `C:\Program Files (x86)\SimHub\` |
| Host process | `SimHubWPF.exe` — **32-bit x86**, .NET Framework 4.8 |
| Plugins | net48 DLLs dropped in the SimHub install root |
| Settings | `PluginsData\Common\AB9ShifterPlugin.GeneralSettings.json` (rewritten on exit) |
| Logs | `Logs\SimHub.txt` |

Assemblies referenced from the SimHub root with `Private=false` (never NuGet, never copied):
`SimHub.Plugins`, `GameReaderCommon`, `SimHub.Logging`, `log4net`, `SharpDX`,
`SharpDX.DirectInput` 4.2.0, `vJoyInterfaceWrap` 2.2.2, `MahApps.Metro`.

vJoy 2.2.2, device 1 — needs **10 buttons** (8 for the gears, 9/10 for the sequential
up/down pulses, kept above the gear range so no game binding can mean two things; this rig's
device exposes 32). Gear *i* holds button *i*, reverse
is gear 8. The bundled `vJoyInterface.dll` is 32-bit, matching the host; this is why `Output/` is
behind an interface and never touched by tests.

Note that SimHub **rebuilds plugins at game change**, so the engine must survive it — hence
`IReusable`, with real teardown only in `FinalizePlugin()`.

## MOZA's serial protocol (AZOM), for reference

[AZOM](https://github.com/giantorth/AZOM) reverse-engineered MOZA's wire protocol. Facts only —
it is GPL-3.0, so **no code may be copied into this MIT project**; a clean-room reimplementation
from the protocol facts is fine.

Frames on the base's serial port (COM12 on this machine): `7E len group dev cmd val chk`, with the
checksum seeded at `0x0D`. Commands seen: `0x5D` input mode, `0xAF00` spring, `0xA900` max torque.
AZOM also presence-spoofs Pit House via a CoAP stub.

This is a possible future convenience — the plugin could set Spring = 0 and DirectInput mode
itself instead of asking the user to visit Cockpit. Two known traps: AZOM does not cover flight
base mode, and it **re-pushes Shifter mode and Spring = 50 when a profile is applied**, which
would silently undo the setup mid-session.

## Disproven — do not rebuild these

Assumptions that looked reasonable, cost real time, and are false:

- ~~Pit House flight mode has a Spring setting~~ → it does not; **MOZA Cockpit** is the control.
  Pit House still matters, though — it owns the **AB9 Mode** switch that puts the base into
  flight mode in the first place. The split is: Pit House sets the mode, Cockpit sets the forces.
- ~~`DIPROP_AUTOCENTER` disables the base's centring~~ → ignored by firmware.
- ~~The plugin disables the base's autocentring itself~~ → it cannot; the README claimed this for
  weeks and it misdirected debugging more than once.
- ~~Spring effects can make the gate walls firm~~ → capped at ~0.3 DI/count, arithmetically
  impossible.
- ~~The device damper/friction can settle an oscillating wall~~ → far too weak; software velocity
  damping replaced it.
- ~~The loop rate limits wall stability~~ → 400 Hz → 1 kHz raised the buzz's *pitch* and nothing
  else.
- ~~Software could compensate for the firmware centring curve~~ → abandoned; the measurement it
  depended on was contaminated by too short a settle time, and configuring Cockpit made it moot.

One coding hazard worth remembering because it produced a completely silent failure: the constant
force rate limiter once seeded its "last write" timestamp to `long.MinValue`, so `now - last`
overflowed negative, the first write never fired, and **every constant force in the gate was
dead** while looking perfectly healthy in logs and UI. Explicit `primed` booleans replaced the
sentinel. Symptom to recognise: walls and detents feel absent while calibration still visibly
moves the stick.
