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

**1. Lateral, in the neutral channel.** A guide pulling toward the nearest column, plus one
barrier per gap.

The guide does two jobs with one force. Sliding along the channel it is a light detent that parks
the stick on a column. As the stick is pushed *out* of the channel toward a gear it strengthens
into a **funnel** (`ColumnFunnelForcePct`), scaled by `FunnelDepthFactor` over the channel's
hysteresis band — the tapered mouth a real gate has. Without it an off-column push is a dead end:
the gate wall holds, no gear arrives, and nothing tells the hand which way to move. It is
**flat-bottomed** across the column's full width, for the same reason slots are corridors — a pull
to a centre line is an equilibrium, and this one would sit exactly where the hand is trying to
hold still.

Which column the guide pulls toward switches at the **barrier crests**, not at the geometric
midpoints. For ordinary gaps those coincide; for the lockout gap they do not, and a midpoint
boundary would keep pulling the stick back toward the main section for thousands of counts after
it had fought through the gate — dragging it straight back in.

The barriers themselves:

- *Ordinary humps* between columns 1–3: `strength · u · exp(0.5 − 0.5u²)` where `u` is distance
  from the crest over the hump width. Zero at the crest, peaking at the width, fading beyond —
  smooth everywhere, so there is no step to chatter against, and it releases once you are through.
- *The lockout gate* guarding 7/R: a compact band of **flat, one-way** force pushing back toward
  the main gears the entire way across, with a short face at each end and free travel beyond.
  Flat because gradients ring. One-way because the over-centre version it replaced *refunded*
  energy (see below). Its width × force is the **toll** a crossing must pay, at any speed. It
  follows `MirrorColumns`, guarding whichever gap 7/R actually lives behind.

  Its faces live **inside** the declared band, so the gate never reaches past its own width, and
  they are capped at half the width so a flat core always survives. They used to overhang the band
  by a whole bite distance, which ate the entire clearance the gate is positioned with and started
  the toll on top of the 5/6 column — felt as a hard bump exactly where the hand expects to be
  resting on a column.

  **It places itself** (`GateGeometry.LockoutCentre`): its inner face begins exactly where the
  last main-section column's band ends, rather than at the midpoint of the gap. The midpoint
  version left ~8700 counts of dead travel between 5/6 and the gate, and that gap was a usability
  trap — the hand stops where the gate stops it, assumes it has arrived at a column, and finds
  that pushing fore or aft neither engages a gear nor explains why. With the gate against the
  column, meeting it means 5/6 is directly behind you, and releasing lets the guide park you on
  it. The width is clamped to the room available so an extreme setting cannot swallow either
  column's band.

### Slot mouths

The end of each divider, where it meets the tunnel, is selectable: **Square** (the plain rectangular
notch, and the default, so the setting is inert until chosen), **Rounded** (filleted on both flanks)
and **Angled** (chamfered on one flank only, toward the next sequential gear).

All three are the same mechanism - the slot's corridor edge widens toward the tunnel and narrows to
the slot's own width further down - and the mechanism only ever **removes** force. A chamfered divider
end does not push a lever toward the next gear; it stops holding it back, and the hand's own lateral
pressure does the rest. That is what makes it safe: nothing can push outward, so there is no positive
feedback, and the only gradient introduced is the flank's own slope.

`MouthSlopeMax` (0.5 lateral counts per count of depth, not a user dial) is the whole stability
argument in one number: the flank's force gradient is at most half the wall face however the dials are
set. Rounded's opening is additionally scaled by 2/pi, because a raised cosine's steepest point is
pi/2 times its average and the cap is meant to bound the steepest point. The profile is a raised
cosine rather than a circular fillet deliberately - a true circle's flank goes **vertical** where it
meets the slot wall, an unbounded gradient at exactly the depth a hand dwells.

**Reach is what decides whether the feature exists at all.** The first design confined the shaping to
the channel's hysteresis band, 1000 counts deep, and adversarial review killed it with arithmetic: the
base answers in 3-4 ms, in which a lever being shifted covers 1500-2000 counts, so **not one corrected
force sample landed inside the patch** - the assist arrived after the lever had gone. Its peak was 946
DI at gain 100 and 197 DI at the default gain, the latter equal to the composer's own
"not worth tracking" floor. Reaching several thousand counts down the slot instead spans the whole
withdrawal stroke and several round trips. Measured on the live settings, Angled now removes up to
5875 DI (7 Nm) of confinement across 4900 counts of lateral freedom - about 30x the authority, and 22%
of the way to the next column instead of 2.3%.

Two clamps keep it honest. The opening can never reach the neighbouring column's territory, and on a
flank facing the lockout it must leave room for the wall's **face** as well as its corridor - keeping
only the corridor out of the gate's band is not enough, because widening the corridor moves where the
face begins, so the force inside the band changes and the toll's size starts depending on the mouth
setting. Angled sidesteps this entirely by returning no bias across the lockout gap.

### The handover window

A field that pushes toward the **nearest** column has a boundary between every pair of columns, and at
that boundary the force reverses. With the guide saturating at a flat plateau all the way up to it, the
reversal was a step of **twice the plateau in a single tick**. Measured, by replaying a real 25 110-tick
hardware trace through the composer at the user's own settings - worst lateral force change from 100
counts of sideways drift:

| depth | before | after |
| --- | --- | --- |
| ≤ 1400, the tunnel proper | 423 DI (the lockout's face - correct) | 423 |
| 1600 | 1 558 | 457 |
| 2400 | 7 706 | 447 |
| 3200 | 13 853 | 553 |
| ≥ 4000 | **20 000 - a clamped ±12 Nm reversal** | 424 |

Four instances are visible in the raw trace with **zero** depth change and 55-104 counts of drift
reversing the force by up to 2838 DI. It was felt as the notches kicking while sliding along the tunnel,
and reported as the lever being "pushed and pulled in random directions".

The fix is `GateGeometry.HandoverClearance`: the lateral field is **multiplied** by a window that is
zero across every position the guide pick can change hands at, with one wall face of flank either side.
A handover then always happens where the force is already zero, so there is nothing to reverse.

Three details are load-bearing, and each was established by refuting a version that lacked it.

**It is a multiplier, not a reach.** The obvious shape - truncate the plateau at a distance measured
from the guide column - was built and measured, and it *invents* history dependence. The reach becomes a
property of *which* column owns the field, and the latched column and the position-picked one can
differ. A flat plateau made that handover free, because wherever both columns lie on the same side of
the lever both saturate to the identical value; a truncated one does not. Measured: **10 000 DI at one
physical position, selected by whether the lever had once dipped into the tunnel**, and a latched gear
left with no push-back at all over three quarters of the axis. A shared scalar cannot do either, because
the field becomes `F_old(history) × Relief(x)` - any two histories the old field made equal stay equal.
`TheReliefWindowCannotInventHistoryDependence` pins it.

**The window spans both boundary rules, not the one in force at this depth.** `GuideColumn` uses the
barrier crest in the tunnel and the plain midpoint below it, and at the lockout gap those are thousands
of counts apart. A window keyed on `InChannel(y)` leaves the other rule's flip window on a live part of
the field, and then **one single count of fore/aft movement reverses the guide - measured at 2403 DI** -
right where the fore/aft wall's own deadband leaves the lever freest. That is the original bug moved
from the x axis to the depth axis, and it is the natural thing to write, so it is recorded in the
rejected table below. Taking the hull of both rules makes the window a function of x alone.

**The hysteresis shrank because of it.** `DetentHysteresis` was 1500 and load-bearing: the boundary was a
cliff, so a wide band was all that stopped the lever chattering between two opposite full-scale forces.
With the field zeroed there, a flip costs nothing, so it is now 400 - and since the window has to cover
it, that turns an ordinary divider's dead strip from 3000 counts into 800.

The price, stated plainly: at the **lockout gap only**, where the two rules genuinely disagree over
thousands of counts, a latched 5/6 dragged toward 7/R at gear depth goes slack across the gate's
doorway instead of being shepherded home. The gear cannot change there - the lock is absolute and
positional - and the gate's own force is deliberately faded at depth anyway, because a plate has its
gate cut into the tunnel and not into the slots. `OnlyTheLockoutGapLosesItsWallToTheHandoverWindow`
pins that this is confined to that one gap.

### One lateral field

The lateral force is computed **once**, by a single function of position and the guide column, and
both states call it. It used to be computed twice - a funnel-plus-confinement in the tunnel, a slot
wall once a column was latched - and the two disagreed by a **measured 4924 DI units, nearly six
newton-metres, at the same physical position**. Which one the lever got depended on the latch, and
because the channel bands are hysteretic, that depended on how it had arrived. Travelling from one
slot to another around a divider end is exactly the manoeuvre that crosses the boundary, and that is
exactly where it rang, while the deep walls - where the two branches happened to agree and both went
flat - stayed calm. `TheLateralFieldDoesNotDependOnTheLatch` pins it at zero now.

**Nothing below the channel varies with depth.** Below the exit band a column can be latched, and
there the lateral field is a function of x alone - the guide plateau has reached the slot wall, the
barriers have faded out, and only an opt-in mouth shape moves anything. This boundary was learned the
hard way: an earlier version carried the plateau's rise on past the exit, which gave the slot walls a
cross-gradient of about 2 DI per count of depth where they had none, so the wall grew under the hand as
the lever was pushed in. The deep walls were untouched and stayed calm; the guides leading down to each
gear rang, and that is exactly where it was felt. Pinned by
`BelowTheChannelTheLateralFieldHasNoDepthTermAtAll`.

The guide's rise is therefore one straight line from the tunnel detent to the slot wall, finishing at
the exit band, which also bounds its slope by construction at pin force over the exit width - at or
under the wall's own face. That is why there is no separate funnel strength: a waypoint would either
make one leg steeper than the walls or land exactly on this line.

**One stiffness.** Every lateral force's face length is derived from its plateau, so plateau over
face is always pin force over the wall's bite. A gentler force gets a *shorter face* rather than a
steeper one. This retired the steepest gradient in the gate: the funnel's ramp was a free parameter,
and at the bottom of its range it produced 13.3 DI per count against a wall face of 3.8 - three and a
half times the wall, existing only in the mouth, which is the one region the lever crosses on every
shift.

**Depth spans and lateral spans are not interchangeable.** The plateau's depth ramp uses the
channel's own width; wiring it to the wall's bite instead meant a long bite pushed the slot wall's
full strength tens of thousands of counts down the slot, so the wall went missing exactly where a
gear is held. Caught by `ASlotWallStillCannotBleedIntoTheNextColumn`.

**The watershed changes with depth.** In the tunnel the guide's column boundaries are the barrier
crests, so fighting through the lockout gate hands the lever to 7/R instead of dragging it back.
Below the tunnel they are the plain midpoints. That is not cosmetic: with crest boundaries at depth,
a lever dragged out of 5/6 crosses the gate's crest, the guide adopts 7/R, and the wall that was
holding it in **reverses into a conveyor** pushing it toward 7 at full pin force for thousands of
counts - pull out of 5, drag right at depth, drop into 7, no toll at all. It has to be positional
rather than historical so a cold start at that position resolves the same way. Pinned by
`TheLockoutCannotBeConveyedPastAtDepth`.

Lateral confinement is a fact about **depth**, not about the state machine's latch. Below the
channel the guide hands over to the nearest column's slot wall at full strength
(`SlotConfinementFactor`, fading in over the wall's bite), so there is nowhere at gear depth the
lever can travel sideways freely. This is not a refinement — while confinement depended on the
latch, overpowering one slot wall dropped the latch, the neutral field took over, and that field
had no lateral wall down there at all. The gate gave way completely and the lever could be walked
along the top or bottom of the pattern through every gear in turn, with the guide adopting each
column it passed and helping it along.

For the same reason the barriers work the other way round: humps and the lockout gate **fade out**
with depth. A real plate has its gate cut into the tunnel, not into the slots. Leaving them on
below the channel had the lockout shoving back toward the main gears while the slot wall pushed on
toward 7/R, cancelling to almost nothing in exactly the region that should be solid plate. Nothing
is lost, because reaching that depth between columns means overpowering the full gate wall first.

**2. Lateral, once in a column** — the vertical guide. A **free corridor** (`SlotHalfWidth`) with a
firm wall on each side, *not* a pull toward the centre line. Barriers are a neutral-channel affair
and stay out: once committed to a gear there is nothing left to push through.

The face gets the **full bite distance**, same as a gate wall. It used to be squeezed into the
state machine's lateral exit band — roughly a fifth of the configured bite — so that a lean could
not drop the gear while the wall was still building. That squeeze made slot walls several times
steeper than any wall a hand found stable, and it showed: the channel walls were calm while the
slots oscillated at the same settings. The gear lock (below) removed the reason for it. The only
bound left keeps a wall from bleeding into the neighbouring column.

**3. Fore/aft** — the horizontal guide. In the neutral channel, a wall whose height is blended by
lateral distance from a column (`ChannelBlockFactor`): nearly open when lined up with a column so
a gear can be taken, a full wall between columns. The channel is a corridor too, free within its
own width. Once in a column this gives way to the **slot detent**: resist on the way in, flip over
centre to pull the stick home (the snick), then settle to a seated hold strong enough to beat the
base's own centring (~90% of full force at full deflection — hence `DetentHoldPct` = 55).

## The rail gate

Trying MOZA's native shifter mode on the same base produced one load-bearing observation: **the
native gate never has free 2D space.** At every moment exactly one axis is unlocked — in a column
the lever moves only fore/aft with zero lateral play; in the neutral tunnel only sideways with
zero fore/aft play — and a push between two columns is never met with a wall to lean on, it is
resolved sideways into one column or the other. Nothing can float, so nothing can accelerate
across a gap and land on a face, and the whole class of wall-contact instabilities has no room to
exist.

That topology is a **special case of this gate, not a different one**: it is the same field with
the two free corridors closed. `SlotHalfWidth = 0` rails each column; `ChannelFreeDepth = 0`
rails the tunnel. Everything else carries over unchanged — the watershed that resolves a push
into the nearest column is the lateral guide field it always was, the notches are the barrier
humps, the lockout keeps guarding 7/R (the one thing the native mode does not have), and the
absorber, attack and static hold keep doing their jobs. Reopening the corridors is moving two
sliders back, which is why this is a configuration and not a fork.

One limit is honest and structural: the native mode renders its rails **in firmware at zero
delay**, which is why they can be clamp-stiff. Ours are rendered through the 3–4 ms round trip,
so a rail is a *groove* with a stiffness ceiling — the interior equilibrium that corridors were
invented to remove comes back the moment a corridor closes, and a rail turned too stiff hunts
around its line exactly the way the middle columns once shook. What has changed since that
lesson was learned: the loop is 1 kHz, the absorber works during motion (see below), and static
hold freezes a lever at rest — a railed lever *at* its line feels zero force, which is quiet
ground, unlike a lever leaning on a wall face. The retreat dial for a trembling rail is the
rail's own strength (pin force for a column, gate wall for the tunnel), never damping.

## Other patterns

**Missing slots (6+R).** A slot that holds no gear is rendered by never opening its mouth: the
fore/aft wall's block factor is keyed on push direction as well as position, and a column whose
slot is empty that way stays at full wall however well lined up the lever is — the divider simply
continues across. Keying a force on direction is safe in exactly one place, and this is it: the
fore/aft force crosses zero at the channel centre, so the switch between the two directions'
factors happens where there is no force to step. The mouth shaping skips a missing slot for the
same reason, and the state machine refuses to latch it, so map, wall and logic agree. 5+R needs
none of this — it is simply three columns spread over the full axis, with no lockout and every
barrier crest at its gap's midpoint.

**Sequential.** No gate at all: the lever is railed to the lateral centre and sprung home
fore/aft. The "spring" is not a DirectInput spring — those cannot hold a lever on this base (see
the effect-strength section) — but the usual saturating constant-force profile, made deliberately
shallow: full resistance is only reached at the shift threshold itself, a gradient of well under
1 DI per count, which no delay can destabilise. Crossing the threshold drops the resistance to a
lighter hold — the click — and the drop passes the time shaping instantly like any release, while
the force always points home so the lever returns on its own. The return assist keeps the snick's
milder yield floor: absorbing the return would defeat it. One shift fires per stroke, re-armed
only by coming back through the release threshold, and the buttons are timed pulses rather than
held gears — with a 20 ms guaranteed gap on a re-fire, because an off-and-on inside one tick
reads to a game's input poll as one continuous press.

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

The absorber's scale is **one-way in time**: it cuts to the speed's target instantly but climbs
back at a fixed rate (`YieldRecoveryMs`). This exists because the speed it keys on is an estimate,
and the estimate carries the device's report quantisation — under write contention distinct
positions arrive at only ~500 Hz (see [hardware.md](hardware.md)), so adjacent-tick differencing
alternates ~2:1 even during a perfectly smooth pull. At 59% absorption that swept the scale across
its whole blend range at 250–500 Hz and rippled the wall force by 25–50%, which a hand reads not
as vibration but as **grinding — the lever meshing against a running gear — the instant it moves
under pressure**. Two fixes compose: `VelocityEstimator` differences positions across a 4 ms
window (an exact null for the 2 ms report clock), and the slewed recovery bounds whatever ripple
survives to `full-scale / YieldRecoveryMs` per millisecond. Replaying the trace that reported it:
adjacent-tick up-down force reversals ≥ 250 DI fell from 1293 to 14, and those 14 are genuine
direction reversals, where restoring full force instantly is the contract. The slew costs nothing
a hand can feel — the same-direction test already restores full force the moment the wall is
resisting again, so slewed recovery can only ever *deepen* absorption, never soften a press.

**Time shaping (`WallAttackMs`, off by default).** The wall in time instead of in space, with three
behaviours. It applies to every force a hand can lean on, the lockout included; the slot detent is
the one exception, because the snick is a deliberate transient that has to arrive whole to read as a
mechanism seating.

- *Attack* — force may only grow at a bounded rate, so contact winds up like a real surface rather
  than landing as a delay-late blow.
- *Static hold* — pressed against the same wall and effectively still, small force deviations are
  **frozen** rather than tracked. This is static friction, and it is the only quiet answer for a
  hand resting on a wall's face, where the gradient is too steep for any damping.
- *Release* — any drop, sign flip, or let-go passes **instantly**, so a retreating stick is never
  chased by stale force.

The lockout was exempted at first, on the theory that slewing a crossing hands a fast flick a
discount. The arithmetic does not support that and a test now encodes it: the band is thousands of
counts wide, so even a violent flick spends tens of milliseconds inside it while the attack lasts
fifteen or twenty — the toll survives essentially intact. What the exemption did cost was real, and
felt: the lockout was left as the one force in the gate still arriving raw, so it rejected the lever
hard where every wall had learned not to, and rang.

The static hold band is **proportional** to the force already being applied, not a fixed figure. A
band wide enough to steady a full-strength wall would swallow a light guide force whole, and freezing
the gentle pull along the channel makes sliding across the gate feel notchy and sticky.

Damping joins after all of this and keeps full bandwidth.

Software **velocity damping** rounds it out, computed from the axis readings because the device's
own damper is far too weak to settle anything here.

## The gear lock

A latched gear is released **only** by bringing the stick back through the neutral channel. Nothing
sideways changes gear: not a lean, not a wall briefly overpowered, not a diagonal drag. A real gate
behaves the same way, and it buys three things at once:

- **No accidental shifts.** A thin wall that gets pushed through no longer hands over a gear; the
  slot walls simply keep shoving the lever back toward the gear it is in.
- **A calmer slot wall.** The wall no longer has to reach full strength inside the lateral exit
  band, because that band no longer decides anything — which is what fixed the slot oscillation.
- **No mid-lean force swap.** The old release switched the whole force field from slot walls to
  channel walls while the hand was still pushing, which was itself a jolt and a source of ringing.

**There is no lateral escape at all** — not a generous one, not a fault threshold. This is the
crucial part, and it is a design decision rather than a tuning one: **force cannot enforce a gate.**
A hand beats 12 Nm, so the walls will always be pushable, and any distance at which the latch gave
way would be a distance at which the rest of the pattern came back and could capture the lever into
a gear it was never driven into. Making the lock absolute means pushing sideways can accomplish
nothing whatsoever except being pushed back — which is exactly the guarantee a mechanical gate
gives, and it is a guarantee about *logic*, not about strength.

`Resync` remains the only way to adopt a position: startup, and a geometry change under the running
loop. Everything else goes through the tunnel.

A consequence worth stating plainly: a determined hand can still drag the lever diagonally across
the plate and arrive in the tunnel somewhere it did not set off from — including past the lockout,
since barriers fade at depth. Nothing can prevent that, because nothing can out-push a hand. What
the lock guarantees is that the excursion is resisted the whole way, achieves no gear change, and is
never *helped*: no gear can appear that the lever was not deliberately driven into through the
tunnel.

Startup and geometry changes still adopt whatever gear the stick is sitting in (`Resync`), which is
correct there and clears any pending fault.

## Where the remaining gradients are

Both kinds of wall now share `WallRamp`, so the one gradient a human dial controls really is the
one they are feeling. What the bite still does not cover:

- **Corners have a lateral gradient** — near a column edge with Y pressed, the fore/aft wall's
  height swings from guide to full wall over the `WallBlend` distance, driven by *sideways* motion.
  Corners are where both axes' walls land at once, and they are the worst case for everything.
- **The funnel is a gradient too**, though a gentle one (roughly a tenth of a wall face's slope at
  default settings), and it acts only while entering a gear off-column.

Time shaping is the tool for both, because it acts on force change regardless of which spatial
gradient produced it.

## Rejected approaches

Kept permanently. Each line is a thing that was built, felt on hardware, and abandoned.

| Approach | Symptom that ruled it out |
| --- | --- |
| **Spring effects for the gate walls** | Walls felt like nothing. Root cause is arithmetic, not tuning: ~0.3 DI/count ceiling. Whole gate rebuilt on constant forces. |
| **Pull toward a slot's centre line** | Middle columns shook violently when seated; outer columns fine. Interior equilibrium = oscillator. Replaced by corridors. |
| **A guide plateau held flat up to the column boundary** | The boundary reverses the force, so a flat plateau makes the reversal a step of 2 × plateau in one tick - measured at 20000 DI, a clamped ±12 Nm, from 100 counts of drift, and felt as the notches kicking while sliding the tunnel. Replaced by the handover window. |
| **A guide reach measured from the guide column** | The natural fix, and it invents history dependence: the reach belongs to *which* column owns the field, so the latched branch and the position-picked branch disagree by the full pin force wherever both columns lie on the same side of the lever - exactly where a flat plateau had made them identical. Measured 10000 DI at one position selected by whether the lever once dipped into the tunnel, plus no push-back over 75% of the axis at gear depth. Use a positional multiplier, which both branches share. |
| **A handover window keyed on `InChannel(y)`** | Moves the same reversal onto the depth axis. The crest and midpoint rules are thousands of counts apart at the lockout gap, so the other rule's flip window stays live: **2403 DI from one single axis count of fore/aft movement**, where the fore/aft wall's deadband leaves the lever freest. The window must span the hull of both rules. |
| **A wide detent hysteresis as the cure for boundary chatter** | It only ever hid the cliff; 1500 counts of it bought a 3000-count dead strip once the field was zeroed at the boundary. Zero the field instead and the hysteresis can be small. |
| **Device damper / friction / inertia to settle walls** | Condition effects are near-decorative on this base. Replaced by software velocity damping. |
| **MOZA Cockpit Natural Damping** | Stiffens the lever, oscillation unchanged. Damping cannot rescue a gradient this steep behind this much delay. |
| **Raising the loop rate (400 Hz → 1 kHz)** | Buzz changed *pitch* and nothing else. Proved the limit cycle is set by geometry, not by delay distance. Kept anyway — it doubled the stable gradient range. |
| **Rebound absorption alone** | Turning it toward 0 made oscillation slower *and stronger* — it drains the pump but does not stop it. Kept as one of four mechanisms. |
| **A near-step wall face (~250 counts)** | Deep leaning went calm, but the face became a step: contact landed as a hammer blow, threw the stick out, hand pushed back in, repeat — felt exactly like ABS kicking, worst at corners. |
| **Long wall face (up to 6000 counts)** | Quieted the vibration but went spongy, and *still* bit occasionally — because slot walls are internally clamped and corner blend gradients are not covered by the bite at all. |
| **Over-centre lockout** (resist to the crest, assist after) | A fast flick sailed through for nearly nothing: ballistic crossings pay on the near side and are **refunded** on the far side, at any loop rate. Replaced by a one-way toll. |
| **Software compensation for the firmware centring curve** | Abandoned — the coefficient measurement it needed was contaminated by too short a settle time, and configuring MOZA Cockpit made it unnecessary. |
| **Lockout as a wide zone across the gate** | Unnecessary once the walls were firm, and it dragged the stick around half the channel. The lockout only needs to guard the *crossing*; keeping the stick in line is the walls' job. |
| **Lockout at the midpoint of its gap** | Left ~8700 counts of dead travel between 5/6 and the gate. The hand stops at the gate, assumes it has reached a column, and then fore/aft neither engages nor explains itself. The gate now sits against the column's band. |
| **Column boundary at the geometric midpoint** | With the gate off-centre, this pulled the stick back toward the main section for thousands of counts *after* it had paid the toll — straight back into the gate. Boundaries are the barrier crests instead. |
| **Lateral confinement that depended on the latch** | Overpowering one slot wall dropped the latch, and the neutral field that took over had no lateral wall at gear depth — so the lever could be walked sideways from gear to gear along the top or bottom of the pattern. Confinement follows depth now. |
| **Barriers acting at gear depth** | The lockout pushed back toward the main gears while the slot wall pushed on toward 7/R; the two cancelled to ~2000 of 10000 in the one region that should feel like solid plate. Barriers fade out as the slot walls fade in. |
| **Releasing a gear on lateral exit** | Made gears fall out under a firm lean, swapped the whole force field mid-lean, and forced the slot wall's face into a fifth of its bite — which is what made the slots oscillate while the channel stayed calm. A gear now leaves only through the tunnel. |
| **Exempting the lockout from time shaping** | Left it as the only force still arriving raw, so it rejected the lever hard where every wall had learned not to, and rang. The flick-discount worry it was guarding against does not survive arithmetic: crossing takes tens of milliseconds, the attack lasts fifteen. |
| **Lockout faces overhanging the band** | Ate the clearance the gate is placed with and put the onset of the toll on top of the 5/6 column, as a hard bump where a hand expects a resting place. The faces are inside the band now. |
| **A fixed static-hold band** | Wide enough to steady a full-strength wall meant swallowing a light guide force whole, making a slide across the gate notchy. The band is proportional to the force being applied. |
| **Computing the lateral force in two branches** | The tunnel's field and the in-column field disagreed by 4924 measured DI at the same position, selected by the latch and therefore by history. The mouth rang; deep walls did not, because there the two agreed. One function now, called by both. |
| **A depth term below the channel exit** | The guide's rise continued past the exit, giving every slot wall a ~2 DI/count cross-gradient it had never had - the wall grew as the lever was pushed in. Deep walls unchanged and calm, guides ringing. Everything depth-dependent now finishes at the exit band. |
| **A separate funnel strength** (`ColumnFunnelForcePct`) | A waypoint on the guide's rise. Placed anywhere but on the straight line it made one leg steeper than the wall face; placed on the line it was redundant. Deleted; the detent sets the shallow end and the slot wall the deep one. |
| **A separate ramp for the lateral guide** (`DetentRamp`) | A free parameter on a gradient. At its floor it made the funnel 13.3 DI/count against a 3.8 wall face - the steepest thing in the gate, in the region crossed on every shift. Faces are derived from plateaus at the wall's stiffness now, and the dial is deleted. |
| **Using the wall's bite as the plateau's depth span** | They are different axes. A long bite delayed the slot wall's full strength by tens of thousands of counts of depth, so the wall vanished where a gear is held. The depth span is the channel's own width. |
| **Crest watersheds below the tunnel** | Opened a complete lockout bypass: past the gate's off-centre crest at gear depth the guide adopts 7/R and conveys the lever toward 7 at full pin force, toll unpaid. Midpoints below the tunnel. |
| **Mouth shaping confined to the channel band** | 1000 counts deep, against a 1500-2000 count round-trip distance: zero corrected samples landed inside it at shift speed. Peak 946 DI at gain 100, 197 DI at the default - the latter equal to the static-hold floor, i.e. a mode that did nothing. The shaping spans the withdrawal stroke instead. |
| **A circular fillet for the rounded mouth** | Its flank goes vertical where it meets the slot wall - an unbounded gradient at exactly the depth a hand dwells. A raised cosine leaves at zero slope on both ends. |
| **A separate "lockout shading starts at" setting** | A second copy of the gate's position, which did nothing once the gate moved itself, and drifted from the truth. The Monitor tab asks the geometry. |
| **Adjacent-tick velocity differencing** | Under write contention distinct positions arrive at ~500 Hz, so half the 1 kHz polls repeat and the per-tick difference alternates ~2:1 — a smooth 17000 count/s pull read as 10000↔25000. Invisible until something keyed force on speed. Positions are differenced across a 4 ms window now. |
| **An absorber that follows the speed estimate both ways** | The estimate's ripple swept the yield scale across its blend range at 250–500 Hz: a 25–50% force ripple felt as *grinding against a running gear* the moment the lever moved under pressure — instantly, needing no oscillation to start. Cuts stay instant; recovery is slewed over `YieldRecoveryMs`. More EMA smoothing instead was considered and rejected: smoothing is phase lag at every frequency, and lag is force given back after the launch the yield exists to catch, while the window nulls the one artifact frequency outright. |

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
