# The force model

How the gate is built, why each shape was chosen, and — at the end — every approach that was
tried and rejected. That last section is the most valuable part of this file: most of the cost of
this project has been re-deriving that something does not work.

All of this lives in `Core/ForceComposer.cs`, which is pure and fully unit-tested.

## Units and frame

- **Axis counts**: 0–65535 per axis, centre 32767. X is left/right, Y is fore/aft (low = forward).
- **DI force units**: ±10000 full scale.
- Wall and detent strengths are configured as **percentages of full scale**, then scaled by the
  master gain, so raising overall force keeps every tuned ratio intact.
- Composition happens in the **gate's own frame**. The four measured polarity signs are applied
  once, at the very end. The yield and shaping stages compare force sign against velocity sign, so
  flipping earlier would make them compare unlike things.

## The gate, in three kinds of force

```
   1     3     5     7
   |     |     |     |
   +-----+--+--+--#--+     + column   -- neutral channel
   |     |     |     |     #  lockout gate      - ordinary hump
   2     4     6     R
```

Four columns at X = 0, 21845, 43690, 65535. Gears: (C1 fwd/back) = 1/2, then 3/4, 5/6, and
(C4 fwd/back) = 7/**R**.

**1. Lateral, in the neutral channel.** A light detent pulling toward the nearest column
(hysteresis on which one, so it does not chatter at a midpoint), plus one barrier per gap:

- *Ordinary humps* between columns 1–3: `strength · u · exp(0.5 − 0.5u²)` where `u` is distance
  from the crest over the hump width. Zero at the crest, peaking at the width, fading beyond —
  smooth everywhere, so there is no step to chatter against, and it releases once you are through.
- *The lockout gate* guarding 7/R: a compact band of **flat, one-way** force pushing back toward
  the main gears the entire way across, with a short face at each end and free travel beyond.
  Flat because gradients ring. One-way because the over-centre version it replaced *refunded*
  energy (see below). Its width × force is the **toll** a crossing must pay, at any speed. It
  follows `MirrorColumns`, guarding whichever gap 7/R actually lives behind.

**2. Lateral, once in a column** — the vertical guide. A **free corridor** (`SlotHalfWidth`) with a
firm wall on each side, *not* a pull toward the centre line. The ramp is clamped to reach full
strength before the state machine's gear-exit band, so a firm lean cannot drop the gear while the
wall is still building. Barriers are a neutral-channel affair and stay out: once committed to a
gear there is nothing left to push through.

**3. Fore/aft** — the horizontal guide. In the neutral channel, a wall whose height is blended by
lateral distance from a column (`ChannelBlockFactor`): nearly open when lined up with a column so
a gear can be taken, a full wall between columns. The channel is a corridor too, free within its
own width. Once in a column this gives way to the **slot detent**: resist on the way in, flip over
centre to pull the stick home (the snick), then settle to a seated hold strong enough to beat the
base's own centring (~90% of full force at full deflection — hence `DetentHoldPct` = 55).

## The four stabilising mechanisms

A stiff wall rendered through a 3–4 ms delay is unstable — the delayed force acts as *negative*
damping, so each overshoot returns with interest. Four independent mechanisms address it. They
compose; none alone was sufficient.

**Flat plateaus.** Past its short face a wall is a constant force. A flat force has **no gradient
for the delay to pump**, so leaning deep on a wall is unconditionally calm. This is the reason the
very first constant-force lockout was the one part of the early gate that never rang, and it is
now how every wall behaves.

**Free corridors.** A restoring force about an interior equilibrium is an oscillator: the stick
overshoots, gets pushed back, and hunts. Slots and the neutral channel therefore have *width* with
no force inside. This is why the outer columns were always stable while the middle ones shook —
an outer column's force is one-sided against the end of travel, and one-sided force cannot hunt.

**Rebound absorption (`WallYieldPct`).** Non-conservative rendering: a force that resists motion,
or acts on a stick that is holding still, passes at **full** strength, but a force accelerating the
stick along its existing motion is scaled toward a floor as speed grows. A bounce therefore
returns less energy than the push stored, which starves the ring at its source. This is also how
real gates behave — they are friction-damped and do not fling the lever back. Deadband and blend
in velocity keep sensor jitter from softening a wall being leant on. The slot detent gets a much
milder floor, because the snick is *supposed* to do positive work.

**Time shaping (`WallAttackMs`, off by default).** The wall in time instead of in space, with three
behaviours:

- *Attack* — force may only grow at a bounded rate, so contact winds up like a real surface rather
  than landing as a delay-late blow.
- *Static hold* — pressed against the same wall and effectively still, small force deviations are
  **frozen** rather than tracked. This is static friction, and it is the only quiet answer for a
  hand resting on a wall's face, where the gradient is too steep for any damping.
- *Release* — any drop, sign flip, or let-go passes **instantly**, so a retreating stick is never
  chased by stale force.

Applied to **walls only**. Crossings (lockout, humps, detents) exist to charge for passage; slewing
them would hand a fast flick a discount. Damping joins after all of this and keeps full bandwidth.

Software **velocity damping** rounds it out, computed from the axis readings because the device's
own damper is far too weak to settle anything here.

## Where the remaining gradients are

The wall face (`WallRamp`) is the only gradient a human dial controls, and two others are not
covered by it. If a feel complaint survives tuning the bite, suspect these:

- **Slot walls are clamped short** — the face must finish before `ColumnInnerHalfExit` (~1150
  counts), regardless of what `WallRamp` says. So the walls felt *in gear* can be steeper than the
  slider implies.
- **Corners have a lateral gradient** — near a column edge with Y pressed, the fore/aft wall's
  height swings from guide to full wall over the `WallBlend` distance, driven by *sideways* motion.
  Corners are where both axes' walls land at once, and they are the worst case for everything.

Time shaping is the tool for both, because it acts on force change regardless of which spatial
gradient produced it.

## Rejected approaches

Kept permanently. Each line is a thing that was built, felt on hardware, and abandoned.

| Approach | Symptom that ruled it out |
| --- | --- |
| **Spring effects for the gate walls** | Walls felt like nothing. Root cause is arithmetic, not tuning: ~0.3 DI/count ceiling. Whole gate rebuilt on constant forces. |
| **Pull toward a slot's centre line** | Middle columns shook violently when seated; outer columns fine. Interior equilibrium = oscillator. Replaced by corridors. |
| **Device damper / friction / inertia to settle walls** | Condition effects are near-decorative on this base. Replaced by software velocity damping. |
| **MOZA Cockpit Natural Damping** | Stiffens the lever, oscillation unchanged. Damping cannot rescue a gradient this steep behind this much delay. |
| **Raising the loop rate (400 Hz → 1 kHz)** | Buzz changed *pitch* and nothing else. Proved the limit cycle is set by geometry, not by delay distance. Kept anyway — it doubled the stable gradient range. |
| **Rebound absorption alone** | Turning it toward 0 made oscillation slower *and stronger* — it drains the pump but does not stop it. Kept as one of four mechanisms. |
| **A near-step wall face (~250 counts)** | Deep leaning went calm, but the face became a step: contact landed as a hammer blow, threw the stick out, hand pushed back in, repeat — felt exactly like ABS kicking, worst at corners. |
| **Long wall face (up to 6000 counts)** | Quieted the vibration but went spongy, and *still* bit occasionally — because slot walls are internally clamped and corner blend gradients are not covered by the bite at all. |
| **Over-centre lockout** (resist to the crest, assist after) | A fast flick sailed through for nearly nothing: ballistic crossings pay on the near side and are **refunded** on the far side, at any loop rate. Replaced by a one-way toll. |
| **Software compensation for the firmware centring curve** | Abandoned — the coefficient measurement it needed was contaminated by too short a settle time, and configuring MOZA Cockpit made it unnecessary. |
| **Lockout as a wide zone across the gate** | Unnecessary once the walls were firm, and it dragged the stick around half the channel. The lockout only needs to guard the *crossing*; keeping the stick in line is the walls' job. |

The shape of the whole search, in one sentence: **soft gradient = stable but mush; stiff gradient =
buzz; pure step = hammer.** Every fix that worked moved the problem out of the position gradient
entirely — into flatness, into corridors, into energy asymmetry, or into time.

## What BonusFFB does differently

Worth knowing, since it solves the same problem with the opposite trade. Read for concepts only —
it is GPL-3.0 and this project is MIT, so **no code may be copied**; everything here is a
clean-room reimplementation.

Its H-pattern and truck modules build the gate from **firmware springs** whose anchors are re-aimed
every tick, reflected ~1.3× past the target for ~2.3× felt stiffness. The fast loop therefore lives
in the firmware, where a spring is genuinely passive and cannot ring — stable by construction,
capped by the spring ceiling. It *also* uses constant forces that ramp over ~8000 counts as
"supplemental" strength, which is the delayed-gradient oscillator at five times the length of our
worst version, and its own source comments say max-coefficient damper and friction are "required to
curb this implementation from violently oscillating".

So it contains both halves of the problem, split across two effect families: springs that are
stable but cannot be strong, and constant-force ramps that are strong but oscillate. This project
occupies the missing quadrant — constant force for strength, shape for stability.

One idea there is worth stealing later: the truck module's end-of-slot click is a **one-shot 25 ms
ramp-force effect**, fired and forgotten. A transient played open-loop cannot oscillate by
construction, which would suit the snick well.
