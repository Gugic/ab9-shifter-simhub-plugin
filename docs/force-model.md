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
- *The lockout gate*: a compact band of **flat** force with a short face at each end and free
  travel beyond, guarding whichever gap the placement dial points it at (the default resolves to
  the traditional 7/R gap on 7+R and 6+R, and to nothing elsewhere). Flat because gradients ring.
  One-way by default because the over-centre version it replaced *refunded* energy (see below):
  the constant sign comes from the resolved direction — `TowardHigh` is the classic gate,
  `TowardLow` the same gate facing the other way. Its width × force is the **toll** a crossing
  must pay, at any speed. Placement is stated in map gaps, so `MirrorColumns` relocates the gate
  with the gears — the same rule that moves 6+R's missing slot.

  **Both directions** cannot be a position-only field: a conservative field refunds one crossing
  whatever it charges the other, which is the flick-through measured on the over-centre gate,
  derived instead of felt. The Both gate therefore takes its sign from an **edge-flip side
  latch**: force pushes back toward the side the lever entered from, and the side re-derives only
  while the lever is *outside* the band — so it can flip only after a complete crossing, at a
  position where the band's own faces already have the force at zero. A retreat re-derives the
  same side (nothing refunded), full-band hysteresis means tremor on an edge has nothing to relay
  (the force there is zero), and the latch updates at every depth so a dive under the band owes
  the return toll on surfacing.

  **The hard modes** pin the band to 100% — through `EffectiveGain`, the 10% polarity cap
  included, never around it — and hand the key to a bound action instead of the hand: release
  drops the band the same tick (a release, which the time shaping passes instantly by design),
  and the guarded gears are *refused* while the gate is armed, through the grind's own
  `allowEngage` path (see the invariants: refusal blocks a new latch and nothing else). Arming
  over a lever inside the band **holds fire until the lever is clear**, where the shape is zero
  by construction — with the attack off by default, nothing else would soften a band
  materialising under the hand. The auto re-arm variant closes the gate itself when the released
  crossing completes — the side latch landing opposite the grant — and never before.

  Its faces live **inside** the declared band, so the gate never reaches past its own width, and
  they are capped at half the width so a flat core always survives. They used to overhang the band
  by a whole bite distance, which ate the entire clearance the gate is positioned with and started
  the toll on top of the 5/6 column — felt as a hard bump exactly where the hand expects to be
  resting on a column.

  **It places itself** (`GateGeometry.LockoutCentre`): its inner face begins exactly where the
  approach-side column's band ends — the column the paying crossing comes from, with the wider of
  that column's exit and free bands as clearance, because Gap1's approach is an edge column whose
  free band is wider than the exit dial. The midpoint version left ~8700 counts of dead travel
  between 5/6 and the gate, and that gap was a usability trap — the hand stops where the gate
  stops it, assumes it has arrived at a column, and finds that pushing fore or aft neither engages
  a gear nor explains why. With the gate against the column, meeting it means the column is
  directly behind you, and releasing lets the guide park you on it. A Both gate sits on the gap's
  midpoint instead — ownership hands over there whatever the gate does, so anchoring it to either
  column would leave the other direction's return a free strip ending in a selectable column. The
  width is clamped to the room available so an extreme setting cannot swallow either column's
  band, and an impossible placement is repaired and *reported* (`LockoutPlacementRepaired`), never
  obeyed blindly and never silent.

- *The slot lockout* (`LockoutPlacement.Slot`): one gear's mouth given a toll of its own, spending
  the profile's single lockout on a slot instead of a gap (crests then stay at their midpoints).
  Both push-through shapes are **bands whose edges land where the detent is doing nothing
  unusual**, so no single count of depth steps the stroke: the entry fight is spent entirely
  before the crossover begins — the point where a gear starts to feel taken must not move, and the
  snick arrives whole — and the exit toll is a band between the crossover and the seat, so a
  seated gear rests in a free region rather than under a permanent extra load pressing it into the
  stop (which is also what makes arming a hard exit over a seated gear step-free). The honest side
  effect is the one-way toll's own character: entering crosses the exit band too and is assisted,
  leaving costs. The hard entry mode is the grind's balk re-keyed — the detent becomes a border,
  rendered by the identical muted curve — and where the grind and the lockout overlap the taller
  wall wins, **max not sum**: one border, one attack, one yield floor.

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

**The window must cover the whole flip band, not the boundary alone.** `Pick` biases each boundary by
`DetentHysteresis` toward whichever column is held, so the flip can land anywhere in a band that wide,
and the window is exactly that band.

**The hysteresis shrank because of it.** `DetentHysteresis` was 1500 and load-bearing: the boundary was a
cliff, so a wide band was all that stopped the lever chattering between two opposite full-scale forces.
With the field zeroed there, a flip costs nothing, so it is now 400 - and since the window has to cover
it, that turns an ordinary divider's dead strip from 3000 counts into 800.

**The window is spent by the time the tunnel is left.** This is the part that took two goes.

The window pays for a handover, and a handover is the only discontinuity this field has. Applied at
*every* depth, it therefore holes the slot walls as well as the tunnel - and a latched gear's wall is
the entire enforcement of the absolute lock, so those holes are places the lever can be parked at gear
depth with nothing pushing it home. Measured, replaying `trace-20260807-053305`: latched in fifth, lever
held at full forward deflection and dragged the width of the gate, the lateral force read **exactly 0
DI** around x≈10 900 and again across x≈52 000, and stayed under 500 DI for **1.8 seconds** at the first
of them. A hand hunting sideways finds every one and settles in it. Reported from the rig as shifting
between "distinct half slots" that do not change gear - which is precisely what the lock promises cannot
exist.

The fix is not to narrow the window but to **fade it out with depth**, and that is only sound because the
handover is gone by then. `GuideColumn` now changes hands **only in the tunnel**; out of it, the pick is
simply the one already held. So below `ChannelHalfEnter` there is nothing left to pay for, and `Relief`
rides `SlotConfinementFactor` - the same band, the same shape as the plateau's own rise - reaching 1
by the channel's exit band and staying there. The window is at full strength through every depth a
handover can still happen at, and gone through every depth a wall has to hold.

Two things fall out of freezing the pick. The **crest-versus-midpoint duality disappears**: the pick is
crest-only now, so the window no longer has to span the hull of two rules, and at the lockout gap it
shrinks from 3 323 counts to 801. And the **lockout bypass that duality existed to close is closed more
cheaply**: a live pick at depth read a lever just past the gate as being in 7/R's territory and turned
5/6's wall into a conveyor toward 7, so the toll was never paid. A frozen pick cannot - the lever keeps
whatever column it left the tunnel with, and it can only have left the tunnel where the tunnel's own
crests put it.

The residual: at depths inside the hysteresis band the window is partly faded while the pick is already
frozen, which costs nothing, and a lever that is *Neutral* below the tunnel keeps a frozen pick that may
disagree with the tunnel's on the way back up. That disagreement is bounded by the light detent, exactly
like the latch handover it resembles, and after the ownership fix below the only way to be Neutral at
depth at all is a missing slot. `TheHandoverWindowIsSpentByTheTimeTheTunnelIsLeft` and
`ALatchedGearKeepsItsWallAcrossTheWholeGate` pin both halves.

### Every position has an owning column

`ColumnAt` decides which column a push out of the tunnel selects. It used to be a tight band -
`ColumnInnerHalfEnter` either side of the target - and outside it a push selected **nothing**.

That is a narrower promise than the force can keep, in two separate ways. The fore/aft wall opens over
`ColumnFreeHalfWidth` and then blends shut across `WallBlend`, so between the two lies an annulus where
the gate is passable and there is no gear to be selected. And past that annulus the wall is only 12 Nm,
which a hand simply beats. Measured, `trace-20260807-053318`: two pushes to **full deflection**, held
896 ms and 616 ms, roughly 2 400 counts right of the 5/6 column - resting on the lockout gate's entry
face, in a strip belonging to nothing - state `Neutral`, gear 0. The lever shoved fully home and the game
told nothing at all.

The fore/aft force at those positions, from the same trace, shows how little the wall had to say about it:

| offset from the column | wall closed | fy seen | ticks |
| --- | --- | --- | --- |
| 1 250 | 3% | 2 282-3 411 | 9 |
| 1 750 | 35% | 5 099-6 100 | 224 |
| 2 250 | 67% | 7 398-10 000 | 572 |
| 2 750 | 99% | 9 959-10 000 | 183 |
| 3 250 | 100% | 10 000 | 28 |

The hand went through it at full scale. So `ColumnAt` now returns the column x is **nearest**, boundaries
at the gap midpoints, never `None`. That is deliberately the same ownership `ChannelBlockFactor` already
uses to decide where the wall opens, and the same one `GuideColumn` falls back to - one rule, so the
force and the state machine cannot disagree about whose slot the lever is heading for, and no dial can be
typed to separate them.

Widening capture is not a licence to select the wrong gear: reaching gear depth between two columns still
means beating a fully closed wall, and doing so now lands in the nearer slot rather than in silence. A
silent non-shift is the worst answer available here. What a slot *holds* is still a fact of the gear map
alone, so 6+R's missing 7 refuses across the whole of its column's territory rather than across the old
band.

One geometry repair goes with it. Ownership hands the locked column over at the gap's midpoint, so
`PlaceLockout` now clamps the gate's crest to stay on the main section's side of that midpoint -
otherwise a wide clearance dial could leave positions that belong to 7/R but sit short of the gate, and a
push there would select 7/R with the toll unpaid. `TheWallAndTheStateMachineAgreeAboutWhoseSlotThisIs`,
`APushAllTheWayHomeAlwaysSelectsAGear` and `StoppingShortOfTheGatesCrestCannotReachTheLockedColumn` pin
the three halves of this.

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

### The bottom of a slot, and what a short throw actually is

The slot detent above has no end. Past the engage line `DetentMagnitude` returns `-DetentHoldPct`
and keeps returning it, so the lever is pulled deeper until it meets the base's **mechanical**
stop. That is why, for a long time, there was no such thing as a short throw here: `EngageDepth`
moves the line a gear *registers* at, and nothing else. Raise it and the gear clicks in early and
the lever is then dragged the remaining travel anyway. A seated gear always sat at full
deflection. The sequential lever has had a bottom since it shipped — `SeqOvertravel` then
`SeqStopForcePct` — and the H slots simply never got one.

`SlotStopForcePct` gives them one, and the shape past the engage line is a **corridor, not a pull
toward a point**:

| Depth from centre | Force |
| --- | --- |
| up to the seat | the detent as before — resist, crossover, snick, hold |
| seat → seat + one wall bite | the seated hold fading linearly to zero |
| … → seat + landing | **nothing at all** |
| beyond | the end-stop wall, rising over the wall bite, toward neutral |

The free landing is the whole stability argument and it is the same rule `SlotHalfWidth` and
`ChannelFreeDepth` follow. A hold pulling in against a wall pushing out is a restoring force about
an interior equilibrium, and this project has paid for that lesson twice — it is why slots are
corridors and not centre lines. With a free landing the gear's resting place is a *stretch of
travel*, so there is no gradient at rest for the loop's delay to pump. It is only sound because
**the base does not self-centre** once MOZA Cockpit's Spring is at 0 ([hardware.md](hardware.md)):
nothing pushes the lever back out of the landing, so nothing has to hold it in. On a base still
centring in firmware this shape would let the gear crawl back out, and the old
pull-to-the-mechanical-stop is the right answer there — which is what `SlotStopForcePct` = 0, the
default, still is.

The fade is never shorter than the wall bite whatever the landing is set to. Zero landing would
ask a full-strength hold to reach nothing within a count or two of the seat, which is a bang and
not a face — the fore/aft twin of the tunnel-band cliff `MinBandSpan` exists to prevent. The floor
is *visible* rather than silent: `StrokeStopDepth` reports where the wall really begins, the Feel
tab prints it beside the throw, and the gate plan view draws it.

Everything is measured from the seat, exactly like the sequential stroke, so shortening the throw
moves the whole slot together rather than changing its shape. And at the shipped geometry the
landing already reaches the end of travel, so turning the stop on at a default throw cannot
conjure a wall — it only stops pressing the gear home.

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

## The home spring

A real H lever rests at the 3/4 gate: release it anywhere in neutral and it drifts home.
`HomeSpringPct` (default 0, off) renders that as a lateral pull toward the column holding gears
3 and 4 — gear-column 1, asked of the map, so the mirror flags relocate home with the gears and
a three-column gate is symmetric either way.

It is the one sanctioned pull-toward-a-place in the channel, and every part of its shape exists
to dodge the oscillator that concept normally is:

- **Dead across the home column's own width** (`ColumnFreeHalfWidth`), so the equilibrium is a
  flat region rather than a point — the same trick that lets the column detent guide without
  giving the lever a centre line to hunt around. A lever parked at home feels nothing.
- **One face, one stiffness**: it rises over a face scaled so its slope equals the slot wall's,
  with one deliberate difference from `GuideFace` — no upper clamp on the face length, because a
  spring set *stronger* than the pin force must get a longer face at the same slope, never a
  steeper one. Beyond the face it is a flat plateau to the ends of travel, and flat cannot ring.
- **Fades out with depth** exactly like the humps (`1 − SlotConfinementFactor`), applied in both
  branches through the same expression, so a held gear feels no side pull and no state-machine
  step can reappear.
- **Continuous in x, anchored to one fixed column** — unlike the nearest-column guide it never
  reverses at a handover boundary, so it needs no relief window and cannot interact with the
  pick's hysteresis. `TheHomeSpringNeverStepsTheField` sweeps the whole channel one count at a
  time with everything on to pin this.

It still has the rail gate's honest limit: an interior equilibrium rendered through 3–4 ms of
delay has a hunt ceiling, so a lever that trembles parked at home wants the spring lowered,
never damping raised. At the default detent (12%) and humps (15%), a spring around 25–30%
out-pulls both and the released lever walks home across the notches; below that it reads as a
lean toward home rather than a return. Crossing the lockout outbound the spring adds to the
toll, and in the 7/R channel it keeps tugging toward the main gears — which is the truck-like
behaviour the lockout's one-way force already sketched, now extended across the whole channel.

## How wide the pattern stands

The columns used to be spread over the whole axis by construction — `_targets[i] = i × 65535 /
(n−1)` — so the outer two sat exactly at the ends of travel and the spacing was whatever the column
count made it: 21845 counts on the four-column patterns, **32767 on the three-column ones**. That is
the whole of why 5+R and the truck 6 read as sprawling. It is not a softer gate, it is half again
the reach for every shift, and no force dial can shorten a distance.

`PatternWidthPct` is the distance dial. The span is `65535 × width/100`, the pattern is **centred**
in the axis rather than anchored, and the targets are laid out inside it. At 100 the arithmetic is
byte-for-byte what it always was, which is the entire migration story: every saved profile that
predates the dial renders unchanged, and `TheDefaultWidthLeavesEveryColumnExactlyWhereItAlwaysWas`
pins that for all four H patterns. About **67%** gives a three-column pattern a four-column reach.

Centred, not anchored, and rounded away from zero rather than with .NET's default banker's rule.
The axis is odd, so centring always leaves a spare half-count; sending it to the ends means the
middle column of a three-column gate lands on the identical count at every width. Narrowing 5+R
must not move the column a hand rests on — that column is also the home spring's anchor.

Three things follow, and two of them are the reason this is written down.

**The outer columns stop being one-sided against the end of travel.** That property is quoted in
this project as the reason the outer columns were fine while the middle ones shook: a column at the
axis end has travel on one side only, so its guide cannot be a restoring force about an interior
equilibrium. Narrowed, it can — there is now bare axis past it, and the lateral field pins the
lever back from both sides exactly as it does for an inner column. This is not a new failure mode,
it is the *existing* one applied to two more columns, and the stabiliser stack the inner columns
already run on is what covers it. If a narrowed outer column hunts, it wants what an inner one
wants: lower force, not more damping.

**Nothing else rescales.** Slot corridors, wall bites, column doorways and the lockout's width are
all still the raw counts they were set to, so each becomes a larger share of a narrower gate. That
is deliberate — a dial that silently rescaled a tune would make every stored count mean something
different depending on a second dial — but it means the geometric ceilings tighten as the pattern
narrows. `WallRampCeiling` is computed from `ColumnSpacing/2 − corridor − hysteresis`, so it is the
first to bite, and the Feel tab already prints it. `MinPatternWidthPct` (25) is where a four-column
gate has 5461 counts between columns, which a shipped 2400-count corridor and its wall no longer
fit inside; below that the answer stops being "narrow" and starts being "broken", so the geometry
clamps and the slider stops at 30.

**Every position still belongs to a column.** The bare axis outside the pattern is not a hole:
`ColumnAt` is nearest-column and never `None`, so a push out there lands in the outermost column's
slot rather than in nothing at all. That invariant exists because a silent non-shift — the lever
shoved fully home with the game told nothing — is the worst answer this gate can give, and it is
swept across the whole axis at a narrow width by `EveryPositionInTheGateStillBelongsToAColumn`.

## Other patterns

**Missing slots (6+R).** A slot that holds no gear is rendered by never opening its mouth: the
fore/aft wall's block factor is keyed on push direction as well as position, and a column whose
slot is empty that way stays at full wall however well lined up the lever is — the divider simply
continues across. Keying a force on direction is safe in exactly one place, and this is it: the
fore/aft force crosses zero at the channel centre, so the switch between the two directions'
factors happens where there is no force to step. The mouth shaping skips a missing slot for the
same reason, and the state machine refuses to latch it, so map, wall and logic agree. 5+R needs
none of this — it is simply three columns spread over the full axis, with no lockout by default
and every barrier crest at its gap's midpoint.

**The truck 6.** The 5+R gate with the reverse branch removed from the gear map: six plain slots
on buttons 1–6, nothing anywhere returning gear 8, and every downstream fact — slot existence,
walls, mouths, state machine — derived from that one change. It exists for Eaton-Fuller-style
boxes: give it the configurable lockout between the first two columns, paying on the way down
into the low range, and the shipped `Truck 6-gear (low-range lockout)` preset is exactly that.
The game decides what each button means; the pattern makes no reverse claim at all.

**Sequential.** No gate at all: the lever is railed to the lateral centre and sprung home
fore/aft. The "spring" is not a DirectInput spring — those cannot hold a lever on this base (see
the effect-strength section) — but the usual saturating constant-force profile, made deliberately
shallow: full resistance is only reached at the shift threshold itself, a gradient of well under
1 DI per count, which no delay can destabilise. Crossing the threshold drops the resistance to a
lighter hold — the click — and the drop passes the time shaping instantly like any release, while
the force always points home so the lever returns on its own. The return assist keeps the snick's
milder yield floor: absorbing the return would defeat it. Past the click, `SeqOvertravel` counts
of landing and then an **end-stop wall** (`SeqStopForcePct`, rising over the wall bite) give the
stroke its own bottom — without it the lever sailed on to the hardware stop through twenty
thousand counts of nothing. The stop is measured from the firing line, so shortening the throw
with `EngageDepth` moves the whole stroke together, and it takes the walls' full absorption
rather than the return spring's mild one, because being banged against is its job. It is a wall
toward centre, never a pocket, so releasing inside it still sends the lever home. One shift fires
per stroke, re-armed only by coming back through the release threshold, and the buttons are timed
pulses rather than held gears — with a 20 ms guaranteed gap on a re-fire, because an off-and-on
inside one tick reads to a game's input poll as one continuous press.

The spatial drop alone read as shallow — its size is `DetentResistPct − DetentHoldPct`, and both
ends of that difference are load-bearing for other jobs (stroke weight; the return spring), so it
cannot be made to *hit* without wrecking them. The hit is therefore a separate, **time-keyed**
element: `SeqClickPct`, a 25 ms burst in the stroke's own direction fired the tick the shift
registers — the mechanism's stored energy letting go as the dogs drop in — which then throws the
lever onto the end-stop wall, and the burst plus the landing are the thunk. Time-keyed rather
than a spatial over-centre for the same reason the lockout is one-way: an over-centre pocket
refunds energy, and here it has a worse failure — a lever released inside a pocket is pulled
*deeper*, and a sequential lever must always come home to re-arm. A burst cannot hold the lever
anywhere: 25 ms later it is gone whatever the hand did, and the spring profile beneath it stays
everywhere-restoring. It joins the composition beside the telemetry carrier, after the yield and
the attack — it assists motion by definition, so the absorber would eat it, and a 15 ms attack
would blunt most of a 25 ms hit — and inside the final clamp, the polarity signs, and the
effective gain with its 10% polarity cap. The actuation point itself is a dial in the sequential
frame (`SeqThrow` on the Feel tab: counts from centre to the firing line, the same stored fact as
`EngageDepth` re-expressed); moving it moves the re-arm line with it, keeping the hysteresis gap,
because shortening only the firing line would eventually let a lever resting on the threshold
machine-gun shifts.

**The PRND selector** replaces the gate with a single lane: four fixed positions, evenly spaced,
`PrndLaneHalfLength` either side of centre, with a vJoy button held at whichever one the lever is
in (P 11, R 12, N 13, D 14 — above the gears and above the sequential pulses, and following the
*label* so `MirrorSlots` turns the lane round without costing a rebind). Laterally it is the
sequential rail. Fore and aft it is three things:

| Where | Force |
| --- | --- |
| within `PrndNotchHalfWidth` of a position | **nothing** |
| between the notch and the crest beside it | a raised cosine hump, zero at both ends |
| past either end of the lane | a wall, rising over the wall bite, back down the lane |

Both of the hump's zeros are load-bearing, and one of them is the whole reason it is shaped this
way. The force is measured from the **nearest** position, and every nearest-anything field flips at
the midpoint — on the lateral axis that flip is a step of twice the plateau, and paying for it took
the entire handover-window mechanism above. Here it costs nothing, because the force is already at
zero where the flip happens. A pull toward the nearest position, which is the obvious way to write
a detent, would put that reversal straight back at full detent strength, three times along the
lane. The zero at the notch edge is the ordinary corridor rule: a selected position has to be a
region the lever rests in, not a point it is pulled to.

Raised cosine rather than a half sine because a sine leaves at full slope where it meets free
space, so both the notch edge and the crest would be corners — the two places a hand actually
dwells. The cost is peaking at π/2 times its average, which is what `PrndNotchHalfWidthCeiling`
sizes the span for: the hump is never given less than π wall bites to rise in, so its steepest
point is exactly the stiffness of a wall face. That is the rounded slot mouth's 2/π factor, arrived
at from the other end.

There is no neutral, no travelling and no engage debounce. A selector lever is always in exactly
one position, so `PrndStateMachine` holds an index and only ever hands it to another; the chatter
protection is the crest hysteresis, which — unlike a tick count — works for a hand resting on a
boundary indefinitely, the case that actually happens.

**The lane's lockout.** One chosen gap — P–R for an out-of-park interlock, R–N for a reverse
guard, N–D — can carry a gate band that **replaces** that gap's cosine hump, the exact precedent
of the H gate's own dispatch. It is centred on the gap's crest and its width is clamped to end
before both neighbouring notch edges (`PrndLockoutHalfWidthCeiling`, reported on the Feel tab):
a position stays a free region whatever is asked for. The crest deliberately carries the band's
flat core — that *is* the toll — and the nearest-position flip there is free for the band because
it is one continuous function of the crest offset, not a nearest-field. The gap is label-relative,
so `MirrorSlots` moves the lockout with P, R, N and D, the same rule that keeps each position's
button on its label; direction and the hard modes run the same machinery as the H gate, edge-flip
latch and hold-fire arming included. The one deliberate difference: **the lane's lockout never
touches the selector's state machine, hard mode included** — force is its whole answer, because a
blocked handover would report a position the lever is not in.

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
no force inside. This is why the outer columns were always stable while the middle ones shook �
an outer column's force is one-sided against the end of travel, and one-sided force cannot hunt.
That last part holds only at full pattern width; narrowed, an outer column has axis on both sides
and is an interior equilibrium like any other. See *How wide the pattern stands*.

**Rebound absorption (`WallYieldPct`).** Non-conservative rendering: a force that resists motion,
or acts on a stick that is holding still, passes at **full** strength, but a force accelerating the
stick along its existing motion is scaled toward a floor as speed grows. A bounce therefore
returns less energy than the push stored, which starves the ring at its source. This is also how
real gates behave — they are friction-damped and do not fling the lever back. The slot detent gets
a much milder floor, because the snick is *supposed* to do positive work.

The velocity deadband (`YieldVelocityDeadband`) is the absorber's **lean-or-launch classifier**,
and where it sits is load-bearing. It shipped at 1500 counts/s — sensor-noise thinking — and that
is *tremor* level: a hand genuinely holding against force measures up to ~3700 counts/s of
micro-reversal (traced 2026-07-27), so every reversal of a lean crossed the deadband, and each
crossing fired a fresh cut. The cut steps the force by the yield fraction, the step kicks the
lever, the kick grows the next reversal, and the absorber becomes a **relay oscillator**: a hand
leaning with anything between the floor and the full wall has no equilibrium, because the force
flips between two values across zero velocity. Measured on real traces as 26 Hz chatter at
8155 DI peak-to-peak leaning in a slot, and a 12 Hz rebound off the lockout with the force
flip-flopping 8000↔3200 and the lever spat back out of the band in 20000-count swings — of the
52 cuts in that recording, 22 fired below 10000 counts/s, including the entire early growth
phase. The deadband now sits at 10000: above the measured lean envelope with margin, below
deliberate strokes (15000 and up) and far below wall launches (100000 and up), which cross it
within a millisecond of flight and are still caught.

Inside the deadband the force is **one continuous value in velocity — the held scale — regardless
of sign**. Not full force: the speed estimate ripples under the report quantisation, so a launch's
estimate can dip below any deadband for a tick, and restoring the wall whole on that tick would
flip the force across the entire yield span at the report rate — the grinding texture, reopened.
Not a fresh cut either: that is the relay. The held scale does both jobs — a lean without a
recent bounce has a scale of one and feels a solid wall through every tremor reversal, and a
caught launch keeps its cut through estimate dips, climbing back only at the recovery slew.
Pinned by `AHandsTremorNeverTripsTheAbsorber`, `TheLockoutHoldsWholeAgainstALeaningHand`, and
`AnEstimateDipBelowTheDeadbandKeepsTheHeldCut`.

Killing the relay exposed what had been underneath it. The trace taken immediately after: the big
12 Hz rebound was gone, and in its place a 17.7 Hz, 8000-count cycle riding the lockout's **entry
face** — with the relay dead, a lean weaker than the toll no longer chatters on the flat core; it
is walked down to the face, where its equilibrium sits on a 2.7 DI/count gradient. And that band
now had *no dissipation at all*: below the deadband nothing may cut (leaning must be solid), the
static hold only guards a hand already settled, and damping was at zero. A face gradient, the
loop's delay, and a hand's own 10–20 Hz neuromuscular loop hunted exactly as the first paragraph
of this file says gradients do. That residue is wall friction's job, below.

**Wall friction (`WallFrictionPct`).** Kinetic friction at the gate surfaces: a force opposing
motion, capped at a share — the mu — of the wall force **currently applied on that axis**, viscous
below a saturation knee (8000 counts/s) and Coulomb-flat above it. Because it scales with the
engaged force it is *exactly zero* in free travel, the corridors and the channel, which is what
distinguishes it from damping and why it does not violate the lightest-possible-lever rule: it
costs nothing anywhere the lever is free, and on a face it supplies the dissipation the delay
steals. At the default 15% it is roughly seventeen times the delay's negative damping
(k·τ ≈ 0.011 DI per count/s at the shipped stiffness), which is what lets a lean settle onto a
face instead of orbiting it. The knee is what keeps it off the relay list: a Coulomb sign-flip at
tremor speed would be the yield's disease reintroduced, so through zero velocity friction passes
through zero force, continuously. It takes the **shaped** force as its normal load on purpose —
the attack ramps a wall in, so friction winds up with it rather than arriving as its own step; a
yielded wall grips proportionally less; and a carrier is not a load, so vibration never generates
drag. This is also the honest render: the yield is a real gate's restitution asymmetry, the
static hold its stiction, and this is its kinetic friction — the third of the three things
"mechanical gates are friction-damped" actually means. Pinned by
`FrictionIsZeroEverywhereTheLeverIsFree`, `FrictionOpposesMotionAsAShareOfTheEngagedForce`, and
`FrictionIsContinuousThroughZeroVelocity`.

The hardware verdict on the lean-hunt, recorded so nobody chases it through this dial again:
friction at the default 15% did **not** settle it. What did is **MOZA Cockpit's Damper
at ~15%** — physical damping applied at the servo loop, ahead of the USB round trip. The
arithmetic that predicted friction would work assumed the dissipation arrives in phase; at
17.7 Hz a 3–4 ms rendering delay is 20–25° of the cycle, and a hand-coupled hunt feeds on
exactly that lag. The general lesson is the one this file already teaches: anything the
software renders is late, and lateness converts even a dissipative term into less than it
looks. The friction mechanism stays — it is honest, free in lightness, and the right shape for
whatever residual it does absorb — but the *cure* for lean-hunt on this base is the firmware
damper (see [hardware.md](hardware.md)).

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
  hand resting on a wall's face, where the gradient is too steep for any damping. Its stillness
  test is a tremor-scale constant of its own (4000 counts/s), deliberately **not** the yield's
  deadband: the two answer different questions — "is the hand at rest" versus "is this a launch" —
  and while they shared one field, raising the yield's threshold to hand-adjustment speed would
  have let the freeze span real slow retreats, quantising the face into 20%-band force steps on
  the way out.
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

### The bite has a ceiling the slider does not show

`SlotRamp` clamps the configured `WallRamp` to the room the geometry actually has: half of what is
left of a column's half-spacing once the corridor and the detent hysteresis are taken out. The
halving is deliberate — the rising face and the handover window's relief flank are the same length
and must both fit, either side of the window — and without it an extreme bite makes the two
overlap, so the wall never reaches full strength at all rather than merely reaching it late.

The consequence is that **a bite and a slot width are spending the same budget**, and past a point
the divider between two gears has no full-strength plateau left: it becomes two ramps meeting at a
point, soft enough to hold a lever between columns at gear depth. That is the "no gear registers"
symptom in [tuning.md](tuning.md), and the state machine is right to refuse it — the geometry
should never have let the lever there.

None of this is new behaviour; the clamp has always been there. What was new is that nothing
*said* so, and a slider that goes to 6000 while the gate renders 4061 is a slider that lies. The
Feel tab now prints the effective bite beside the dial and marks when it has been capped down. If
you are changing either dial, the useful question is not "what did I ask for" but "what is the gate
rendering, and is there any flat divider left".

### A repaired band is a ramp span, and one axis count is a cliff

The neutral tunnel's `ChannelHalfEnter` / `ChannelHalfExit` pair is two things at once: hysteresis
for the state machine, and the span the guide plateau ramps its force across (`GuidePlateau`, and
`SlotConfinementFactor` the same way). `GateGeometry` has always repaired an inverted pair by
clamping exit to `enter + 1`, which is correct for the first job and catastrophic for the second.

Measured, from a trace recorded on a night the base died three times — the pair had been typed the
wrong way round, enter 7200 against exit 5200:

| depth | guide plateau |
| --- | --- |
| 7200 | 0 DI |
| 7201 | 10000 DI |

Full scale across **one axis count in 65535**. Park the lever there and a single count of sensor
dither commands a ±12 Nm square wave at the report rate, for as long as it sits. It is the most
violent thing this codebase has ever been able to ask the base for, and it was reachable by typing
one number into the wrong box — the two dials sit adjacent on the Geometry tab and differ by one
word.

The repair is now `enter + GateGeometry.MinBandSpan` (1000 counts), which bounds the gradient at 10
DI per axis count. `AnInvertedTunnelPairCannotBecomeAForceCliff` pins it, and fails with a step of
thousands if the clamp goes back to `+ 1`.

The floor is not only a rescue, and that is worth knowing before reading a stored profile as what
runs. It is applied unconditionally — `Math.Max(exit, enter + MinBandSpan)` — so a *valid* pair
narrower than 1000 counts is widened too. The four shipped H profiles are exactly that case: they
carry the rig's measured 3268/4051, a gap of 783, and the geometry renders 3268/4268. Their tunnel
gradient is therefore 10 DI per axis count, sitting on the floor, against the 3.8 of the older
2600/5200 gate these profiles used to ship with. That is the tune that gets driven and it is not a
complaint — but if the tunnel edge ever reads as abrupt, `ChannelHalfExit` above 4268 is the dial,
and anything below it does nothing at all.

The other two pairs deliberately keep the ordering-only clamp: no force ramps across them, and the
shipped Sequential profile runs a 500-count release gap on purpose. The general rule this leaves
behind: **before clamping a band to "just enough to be ordered", check whether a force ramps across
it.** Ordering is a state-machine property; a ramp needs a width.

## Switching gates without a bang

Applying a new gate on the tick the profile changes puts its full force on the lever wherever
the lever happens to be. Switch pattern while sitting in first — hard forward, hard left — and a
sequential lever wants that stick at centre, so the whole force arrives as a step through the
3–4 ms round trip. Reported from the rig as a *vile oscillation*, and it is the textbook version
of the one problem this project keeps having.

**The fix is deliberately not a centring force.** A restoring force about an interior equilibrium
is an oscillator — the rule the whole gate is shaped around — so curing an oscillation by adding
one would be self-defeating. Three stages instead:

| Stage | Force | Why |
| --- | --- | --- |
| Settle | **zero** | The base self-centres in firmware at ~90% of available force at full deflection ([hardware.md](hardware.md)). It needs no help getting the lever home, and anything we applied would be fighting it. |
| Ramp | 0 → 1 over **350 ms** | Time shaping, the one tool that acts on force change regardless of which spatial gradient produced it. The new gate winds in instead of switching on. |
| Confirm | full, plus a pulse train | One thump per profile number, so which profile arrived can be counted by hand. |

The settle has an **800 ms timeout, and that is the common case rather than a fallback**: a hand
resting on the lever holds it out of the band indefinitely, and waiting for it would mean a
profile switch that silently never took effect. Arriving firm beats not arriving.

Only a change of *geometry* starts this. Dragging a force slider does not come through the
rebuild path, so live dials stay live — which is the whole point of having them.

The scale multiplies the finished frame's constant forces only. Springs are untouched because
every frame ships them `Off`, and the damper is untouched because it opposes motion by
construction: winding a stabiliser in alongside the force it stabilises is backwards, and would
leave the ramp least damped exactly where it is changing fastest.

`ProfileSwitchTransitionTests` pins the shape — zero throughout the settle, monotonic ramp, the
timeout, the pulse count, and that pulses never play while the ramp is still winding.

## The vibration channel and the grind

The telemetry effects (`Core/EffectComposer.cs`) are the one family of force that is neither a
wall nor a guide: **zero-mean carriers** — sine for the engine / limiter / ABS / TC / curbs /
shift-pulse / custom-property effects, a square wave with per-half-cycle amplitude jitter for
the grind —
summed onto the fore/aft force **after the yield and the attack, before the clamp and the
polarity signs** (the same joining point as damping, and for a mirror-image reason).

That placement is both safe and required:

- A carrier is keyed on **time, not position**. Moving the stick does not change what the carrier
  will do next, so it cannot form the position→force→position loop that makes gradients through
  delay unstable — there is nothing for the delay to pump. This is the entire stability argument,
  and it is why no new stabiliser was needed.
- Passing a carrier *through* the stabilisers would not make it safer, it would erase it. The
  attack is a slew (15 ms is most of a cycle at 44 Hz), and the yield keys on force-against-
  velocity sign, which a zero-mean carrier flips every half cycle — the yield would chop it
  exactly the way the aliased velocity estimate once chopped the wall force. That artifact was
  the grinding bug; the grind effect produces the texture deliberately, on demand, instead.
- The amplitudes live inside a fixed budget: 3000 DI per ordinary effect, 4500 for the grind,
  the sum clamped at 5000, everything scaled by the same effective gain as the gate — the 10%
  unconfirmed-polarity cap included, because a symmetric carrier has no polarity but 12 Nm of
  anything needs the cap. The final ±10000 clamp still rules the composed total.
- **Staleness is a safety property.** Telemetry older than 500 ms, or a game that is not
  running, silences every effect the same tick. A paused or hung game must not leave a buzz
  running against the hand.
- Renderable pitch: with one force write per tick at 1 kHz (≈500 Hz per axis when both are hot),
  carriers render cleanly up to roughly 100–130 Hz. The dials stop there.

**The grind** is the first telemetry effect with mechanical consequences. Conditions, all at
once: effect enabled, an H-pattern lever currently *Traveling* into a slot, fresh telemetry,
clutch below its threshold, engine turning, and the car above the optional speed floor. An
engaged gear never grinds — meshed dogs cannot be balked — and sequential is exempt because
clutchless shifting is what a dog box is for.

With **rejection** on, a grinding shift is also balked, in two coordinated moves: the state
machine refuses the Traveling→Engaged transition (`allowEngage`), and the slot detent becomes
the **balk wall** — the entry resistance with `GrindWallPct` stacked on top, rising and then
simply staying, with no crossover, no snick and no hold, so the slot is a border the lever
grinds against rather than a lean, the way a blocking synchro ring stops the lever a third of
the way in. The grind's loudness follows depth — forcing the lever against the balk presses the
teeth together harder — and while balked the detent is treated as the wall it has become: it
takes the attack shaping and the walls' full rebound absorption (the snick's exemptions exist
to protect a transient that cannot occur while balked, and return the moment the clutch unmutes
it). Press the clutch mid-push and the normal profile returns instantly — the pull arrives
whole, like the snick it is — and the gear registers after the standard debounce. The lever
*can* still be forced to the bottom of a slot it will never own; see the rejected table for why
the wall is not closed over it.

## Rejected approaches

Kept permanently. Each line is a thing that was built, felt on hardware, and abandoned.

| Approach | Symptom that ruled it out |
| --- | --- |
| **Spring effects for the gate walls** | Walls felt like nothing. Root cause is arithmetic, not tuning: ~0.3 DI/count ceiling. Whole gate rebuilt on constant forces. |
| **Pull toward a slot's centre line** | Middle columns shook violently when seated; outer columns fine. Interior equilibrium = oscillator. Replaced by corridors. |
| **A guide plateau held flat up to the column boundary** | The boundary reverses the force, so a flat plateau makes the reversal a step of 2 × plateau in one tick - measured at 20000 DI, a clamped ±12 Nm, from 100 counts of drift, and felt as the notches kicking while sliding the tunnel. Replaced by the handover window. |
| **A guide reach measured from the guide column** | The natural fix, and it invents history dependence: the reach belongs to *which* column owns the field, so the latched branch and the position-picked branch disagree by the full pin force wherever both columns lie on the same side of the lever - exactly where a flat plateau had made them identical. Measured 10000 DI at one position selected by whether the lever once dipped into the tunnel, plus no push-back over 75% of the axis at gear depth. Use a positional multiplier, which both branches share. |
| **A handover window keyed on `InChannel(y)`** | Moves the same reversal onto the depth axis. With the pick live at every depth and using a different rule down there, keying the window on `InChannel(y)` leaves the other rule's flip window on a live part of the field: **2403 DI from one single axis count of fore/aft movement**, where the fore/aft wall's deadband leaves the lever freest. Not to be confused with the fade that replaced it: that is continuous in depth, and it is only safe because freezing the pick outside the tunnel means there is no other rule and no flip left to price. |
| **A handover window applied at every depth** | It holes the slot walls as well as the tunnel, and a latched gear's wall is the whole enforcement of the absolute lock. Measured: a gear held at full deflection had **exactly 0 DI** of lateral wall at each gap, and under 500 DI for 1.8 s at one of them - felt as extra half-slots that do not change gear. Faded out with depth instead, over the band the plateau already rises across. |
| **A live guide pick below the tunnel** | Keeps a handover alive at depths where the plateau is the full slot wall, so the window cannot be faded out there - which is what forced the holes above. It also needs two boundary rules to stay safe, and therefore a window spanning both. The pick is frozen outside the tunnel now; one rule, and the window is a tunnel-only concern. |
| **A wide detent hysteresis as the cure for boundary chatter** | It only ever hid the cliff; 1500 counts of it bought a 3000-count dead strip once the field was zeroed at the boundary. Zero the field instead and the hysteresis can be small. |
| **Device damper / friction / inertia to settle walls** | Condition effects are near-decorative on this base. Replaced by software velocity damping. |
| **MOZA Cockpit Damper** *(as a wall-buzz fix)* | Stiffens the lever, buzz unchanged. Damping cannot rescue a gradient this steep behind this much delay. **Scope matters**: for the *lean-hunt* — the slower hand-coupled mode left after the yield relay was fixed — ~15% Damper is precisely what works, because it acts at the servo loop with zero delay. Rejected as a buzz cure, adopted as the lean-hunt cure; see hardware.md. |
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
| **Crest watersheds below the tunnel** | Opened a complete lockout bypass: past the gate's off-centre crest at gear depth the guide adopts 7/R and conveys the lever toward 7 at full pin force, toll unpaid. Midpoints below the tunnel closed it, at the cost of a second boundary rule; freezing the pick outside the tunnel closes it with no second rule at all, because the lever keeps whatever column the tunnel gave it. |
| **A column selection band narrower than the wall's mouth** | The wall opens over the column's free width and blends shut across `WallBlend`; capture wanted the lever inside `ColumnInnerHalfEnter`. Between them the gate is passable with no gear to select, and beyond them the wall is only 12 Nm. Measured: two pushes to **full deflection**, 896 ms and 616 ms, ~2400 counts off the column, state Neutral, gear 0 - the lever shoved home and the game told nothing. Every position belongs to the column it is nearest now. |
| **Mouth shaping confined to the channel band** | 1000 counts deep, against a 1500-2000 count round-trip distance: zero corrected samples landed inside it at shift speed. Peak 946 DI at gain 100, 197 DI at the default - the latter equal to the static-hold floor, i.e. a mode that did nothing. The shaping spans the withdrawal stroke instead. |
| **A circular fillet for the rounded mouth** | Its flank goes vertical where it meets the slot wall - an unbounded gradient at exactly the depth a hand dwells. A raised cosine leaves at zero slope on both ends. |
| **A separate "lockout shading starts at" setting** | A second copy of the gate's position, which did nothing once the gate moved itself, and drifted from the truth. The Monitor tab asks the geometry. |
| **Adjacent-tick velocity differencing** | Under write contention distinct positions arrive at ~500 Hz, so half the 1 kHz polls repeat and the per-tick difference alternates ~2:1 — a smooth 17000 count/s pull read as 10000↔25000. Invisible until something keyed force on speed. Positions are differenced across a 4 ms window now. |
| **Closing the slot wall dynamically while the grind balks a gear** | The honest render of a balk would be the wall refusing to open, but a wall that appears under a moving lever is a step of full wall force at whatever depth the lever happens to be, keyed on a 60 Hz telemetry bit — and "a missing slot is a fact of the gear map" exists precisely because holes encoded anywhere else go wrong. The balk is rendered as resist-only detent plus a refused latch instead; geometry never moves at runtime. |
| **An absorber that follows the speed estimate both ways** | The estimate's ripple swept the yield scale across its blend range at 250–500 Hz: a 25–50% force ripple felt as *grinding against a running gear* the moment the lever moved under pressure — instantly, needing no oscillation to start. Cuts stay instant; recovery is slewed over `YieldRecoveryMs`. More EMA smoothing instead was considered and rejected: smoothing is phase lag at every frequency, and lag is force given back after the launch the yield exists to catch, while the window nulls the one artifact frequency outright. |
| **A yield deadband at sensor-noise level** (1500 counts/s) | Tremor is bigger than sensor noise: a hand holding against force reverses at up to ~3700 counts/s, so every reversal of a lean fired a fresh cut and the absorber became a relay — no equilibrium for any lean between the floor and the wall, because the force flipped between two values across zero velocity. Traced as 26 Hz / 8155 DI chatter held in a slot and a 12 Hz rebound spat off the lockout in 20000-count swings. The deadband is a lean-or-launch classifier and sits above hand-adjustment speed (10000), not above noise. |
| **Restoring the wall whole inside the deadband** | The naive form of raising the deadband. The speed estimate dips below any threshold for a tick at the report rate, and returning full force on the dip strobes a held cut across the whole yield span at 250–500 Hz — the gear-teeth texture, reopened from the other side. Inside the deadband the force is the *held scale*, one continuous value in velocity, whichever way tremor points. |
| **A bidirectional toll as a position-only field** | Impossible, not merely hard: over one traversal of any position-only force, the work out is the negative of the work back, so both crossings cannot cost energy. The symmetric repeller variant is exactly the rejected over-centre gate (resist to the crest, fling after). The Both gate's sign rides the edge-flip side latch instead, which only flips where the band is already zero. |
| **Keying the Both gate's sign on the guide column** | The guide hands over at the crest — mid-band — so the sign would flip halfway across and refund the second half of every crossing: the over-centre gate rebuilt out of hysteresis. The side latch flips at the band's *edge*, where the force is zero, so a crossing pays in full. |
| **Refusal read off the live side latch** | An overpowered crossing flips the latch the moment the band is exited — refusal keyed on it would lift exactly when the fight was won. The permitted side is captured when the key turns and changed only by the next arming edge. |
| **Persisting the hard lockout's engaged flag in settings** | The session-flag lesson re-learned before the bug this time: a keypress would fork the active preset (IsTuning sees an ordinary property), churn the debounced save on every press, and stamp a stale answer back on the next activation. It is engine runtime state, like free stick, re-engaging on every start and gate-moving config change. |
| **A hard slot lockout that deepens the seated hold** | "Locked in gear" as a permanent extra load presses the gear into its stop all day and makes arming over a seated lever a full-strength step at depth, where the detent path has no attack. The exit toll is a band between crossover and seat instead: the seat stays a free region, arming there is force-free, and the toll is met on the way out. |
| **Blocking the PRND selector's state in hard mode** | A selector must always hold exactly one position and its buttons follow the lever; a blocked handover would report a position the lever is not in — a lie to the game with a transmission attached. The lane's lockout is force only in every mode. |
| **Summing the grind wall and the hard slot balk** | A border is not taller for having two reasons, and two stacked walls would mean two attacks and two yield floors fighting over one force. The muted stack takes the max of the two. |

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
