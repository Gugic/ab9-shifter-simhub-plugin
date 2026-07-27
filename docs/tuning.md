# Tuning guide

Every dial is on the plugin's **Feel** or **Geometry** tab and applies on the next FFB tick —
nothing needs restarting. Forces are percentages of what the base can produce *before* the master
gain, so raising overall gain lifts the whole gate together and keeps tuned ratios intact.

Run **Measure polarity** first. Until it succeeds, gain is capped at 10% and everything feels
light — that is the safety cap doing its job, not a tuning problem.

## Start here

| Dial | Default | What it does |
| --- | --- | --- |
| Overall gain | 25% | Master scale. This is a 12 Nm base; raise it slowly. |
| Gate wall, between columns | 90% | The fore/aft wall that stops you entering a gear you are not lined up with. |
| Slot wall, once in a gear | 90% | The sideways walls of a slot. |
| Lockout gate, guarding 7/R | 70% | The push-through toll before 7 and R. Sits against the 5/6 column; width sets the toll with it. |
| Wall attack | 0 ms (off) | Smooths contact and freezes force while you press and hold still. Applies to the lockout too. |
| Slot mouth | Square | Shape of the divider ends where they meet the tunnel. Square is the plain notch and changes nothing. |
| Wall bite distance | 600 counts | How far into a wall force takes to reach full. **The most important stability dial.** |
| Neutral tunnel depth | 2600 counts | The state band: where "in the tunnel" ends and the lateral field's rise lives. Must exceed your fore/aft slop while sliding sideways, or you spend your time in the transition band instead. Measured on real hands: p50 1848, p90 3215. |
| Tunnel depth, free corridor | 2600 counts | Where the tunnel's fore/aft centring force begins. Ships equal to the state band, so the tunnel is simply free; dial to zero for the rail gate. Capped at the state band. |
| Seated hold | 55% | What keeps a gear engaged against the base's own centring. |

## Patterns

The **pattern** lives on the Setup tab, per profile: 7+R (lockout), 6+R (no 7th slot — its divider
just continues across), 5+R (three wider columns, no lockout), or Sequential. Forward gears map to
vJoy buttons 1..N and **reverse is always button 8**, whatever the pattern — so one set of game
bindings covers every pattern and switching profiles never needs a rebind. (Reverse used to be the
highest gear of the pattern, which put 5+R's R on button 6 — read by a game bound for 7+R as sixth
gear, at speed.)

**Sequential** turns the fore/aft axis into a sprung lever: push past the engage threshold and it
fires one press of button 9 (up) or 10 (down) — above every gear button, so a game still carrying
H-pattern bindings cannot read a shift pulse as a gear — re-armed when the lever comes home. Its feel is set
by dials that pull double duty: **detent resist** is the push-out resistance (rises to full at the
threshold), **detent hold** is what remains past the click, **slot wall** sets the lateral rail,
and the pulse length sits next to the pattern selector. Swap up/down with **MirrorSlots** (gear
layout section). The engage/release depths on the Geometry tab set where it fires and re-arms —
they measure from the **ends of travel**, so raising them shortens the throw (engage 26000 fires
about 6800 counts from centre). The spring reaches full resistance exactly at the threshold and
the click moves with it, so a short throw stays progressive rather than becoming a wall. Keep the
release depth a couple of thousand counts above the engage depth for a clean re-arm. The stroke
also has its own bottom: a **sequential stroke** section (Feel tab, sequential only) sets how much
landing remains past the click and the end-stop wall behind it, both measured from the firing
point so the whole stroke shortens as one thing.

Keep one **profile** per pattern you actually use — every dial, the pattern included, is stored
per profile, so switching is one dropdown.

## The rail gate recipe

The native shifter mode's topology — one axis guided everywhere, no free 2D space, nothing to
float across or lean on — reached by closing both free corridors
(see [force-model.md](force-model.md), "The rail gate"):

| Dial | Rail value | Why |
| --- | --- | --- |
| Slot width, free corridor | **0** | The column becomes a rail: any lateral displacement is pulled straight back to the column line. |
| Tunnel depth, free corridor | **0** | The tunnel becomes a rail: any fore/aft wander meets immediate centring, hardening between columns so a push there resolves sideways into a column instead of finding a wall to lean on. |
| Slot wall, once in a gear | **50–60% to start** | The rail's stiffness. The interior equilibrium is back, so this has a hunt ceiling — raise until a *middle* column trembles, then back off. The outer columns cannot hunt and tolerate more. |
| Gate wall, between columns | **70–80% to start** | The tunnel rail's stiffness between columns. Too high reads as the tunnel grabbing your fore/aft wander. |
| Wall attack | **10–20 ms, on** | Load-bearing for rails: static hold is what lets a railed lever sit quietly under a leaning hand. |
| Wall absorption | keep high (~55–65%) | The other half of the hunt ceiling. |

What to feel for, in order: a railed **middle gear trembling** → lower the slot wall (never
damping); the **tunnel fighting fore/aft wobble** while sliding → lower the gate wall or lengthen
the bite; **notches too faint** between columns → raise barrier force — the notch feel is the
barrier humps, unchanged by the rails.

## Symptom → dial

**The lockout rejects the lever hard, unlike the walls, and sets it oscillating.**
Fixed twice over. It was the only force exempted from **wall attack**, so it alone arrived raw — that
exemption is gone, since a crossing takes tens of milliseconds and the attack lasts fifteen, so the
toll is unaffected. And its faces used to overhang its band by a whole bite distance, which put the
onset of the toll on top of the 5/6 column; they are inside the band now.

**A wall buzzes or vibrates while I lean on it.**
Raise **wall bite distance** until it stops, and no further. Past its bite a wall is flat and cannot
oscillate; all trouble lives on the bite itself. If a longer bite makes the wall feel spongy before
the buzz clears, leave the bite moderate and raise **wall attack** to 10–25 ms instead — that
freezes force while you hold still, which is what quiets a sustained press.

The diagnostic signature, reported from the stick and worth recognising: *"it starts slow as I touch
the wall, grows as I press harder, then reduces and stops while I am still far from pushing through."*
That is the hand walking up the **bite** and arriving on the flat top — ringing where the gradient is,
silence where it is flat. It is the bite, not the wall's strength.

Note that **wall attack is the only way to get the static hold at all** — at 0 ms the whole time-shaping
stage is bypassed, static friction included. If you are chasing the lightest possible shifter and are
reluctant to turn it on: static hold is **not damping**. It freezes force updates while the lever is
nearly still and never opposes motion, so it costs nothing in throw speed or lightness, and the slot
detent is deliberately exempt so the snick still arrives whole.

**With a long bite I can push to gear depth *between* columns, and no gear registers.**
The bite's hidden upper bound. The bite is spent three times over between two columns: the slot
wall's face rises over one bite, the handover window's relief flank falls over another, and the
fore/aft wall's rise stretches over it too. The space between two corridors is fixed — at a slot
width of 2400 it is about 8500 counts a side — so by a bite of ~4000 the face and the flank meet in
the middle and the divider has **no full-strength plateau left at all**: the "wall" between gears
is two ramps meeting at a point, soft enough to hold a lever inside at depth. The state machine is
right not to call that a gear; the geometry should never have allowed the lever there. Keep the
bite at or below ~3000 with the default slot width, which still leaves ~1500 counts of solid
divider. Raising **slot width** eats the same budget from the other end.

**Pressing toward a wall grinds instantly — not a bounce or a buzz, but like pushing the lever
against a running gear.**
Fixed structurally, and worth recognising because it is *not* the wall-face oscillation above: it
needs no build-up, it starts the moment the lever moves under pressure, and it scales with **wall
absorption** rather than with the bite. The absorber keys on a speed estimate, and under write
contention the device only delivers distinct positions at ~500 Hz — differencing adjacent 1 kHz
polls turned a smooth pull into a 2:1 speed sawtooth, and the absorber rendered it as a 25–50%
force ripple at 250–500 Hz. Two fixes landed together: velocity is now measured across a 4 ms
window, and the absorber's scale cuts instantly but recovers over `YieldRecoveryMs`
(EngineConfig-only, default 20 ms). If a texture like this ever returns, suspect anything newly
keyed on per-tick velocity before touching the feel dials.

**Touching a wall kicks back at me, like ABS.**
The bite is too short — force is arriving as a step and landing late. Lengthen the bite, or add
**wall attack** (10–25 ms), which winds contact up over milliseconds instead of striking.

**Corners are the worst part.**
Expected: corners are where both axes' walls land at once, and the fore/aft wall's height is also
changing with sideways motion over the **gate wall blend width**. Widen the blend, or use **wall
attack** — it is the only control that covers force change regardless of which gradient caused it.

**A middle gear shakes while seated; the outer gears are fine.**
Widen **slot width (free corridor)**. A narrow corridor leaves the stick balanced on a point it can
hunt around. Outer columns never do this because their wall only pushes one way, against the end of
travel. In the rail gate (slot width zero) the equilibrium is the design — there the fix is
lowering **slot wall** strength instead.

**It oscillates around the wall ends, where one slot meets the tunnel, though deep walls are calm.**
Fixed, and it was two measured faults rather than a tuning problem. The lateral force was computed by
two different branches - one for the tunnel, one for once a gear was latched - and they disagreed by
about six newton-metres at the same physical position, with the channel's hysteresis deciding which
one you got. Going around a divider end is the manoeuvre that crosses that boundary. It is now one
function called by both, provably identical. Second, the funnel had its own ramp dial, and at its
floor that made it the steepest gradient in the gate at three and a half times the wall face, present
only in the mouth. Every lateral force now rises at the wall's own stiffness, and the dial is gone.

**Sliding along neutral, the notches kick the lever sideways as I pass them.**
Fixed, and it was the largest discontinuity the gate has ever had. The guide pushes toward the nearest
column, so at every boundary between two columns the force reverses — and because it held its full
plateau flat right up to that boundary, the reversal was a step of twice the plateau in a single tick.
Measured from a recorded trace at real settings: 20 000 DI, a clamped ±12 Nm, from 100 counts of drift.
The field is now faded to zero across every position the guide can change hands at, so a handover
happens where there is no force to reverse; the same sweep now measures 553 DI worst case anywhere.

Two things make it worse if they are set wrong, and both are now defaults. **Neutral tunnel depth**
below your actual fore/aft slop puts you inside the transition band while you slide, where the plateau
is ramping up — measured, 65% of one recording's sliding time was in that band at the old 1400. And a
large **detent hysteresis** widens the dead strip at each divider, because the window must cover it; it
used to be 1500 and only ever existed to hide the cliff that is now gone.

**The guides leading down to each gear oscillate, though the deep walls are calm.**
Fixed. The guide's rise from the tunnel's light pull to the full slot wall used to continue past the
channel's exit band, which gave every slot wall a cross-gradient it had never had - the wall grew
stronger under the hand as the lever was pushed in. Below the exit band nothing varies with depth now.
If it persists with the mouth shape on Square, tell me; if only with Rounded or Angled, that is the
mouth's own flank and *Mouth opening* is the dial.

**A slot wall oscillates half-way into a gear, though the channel walls are calm.**
This was a real bug, now fixed: the slot wall's face was squeezed into the state machine's lateral
exit band, about a fifth of the bite you had set, so it was far steeper than the channel walls you
were comparing it against. Both kinds of wall now use the full **wall bite distance**. If it still
shakes, the bite is genuinely too short for both.

**I pushed through the lockout but no gear engaged, and I got shoved back unexpectedly.**
Also fixed. The gate used to sit at the middle of the gap, thousands of counts right of the 5/6
column, so stopping at the gate left you nowhere near a slot. It now sits directly against the 5/6
column's band, and the **funnel** steers an off-column push into the slot instead of just blocking
it. If you turned **pull into a column** down to 0, the funnel still works — it is a separate dial.

**I can drag the lever sideways from gear to gear along the top or bottom of the gate.**
You can still *move* it — no wall can out-push a hand on a 12 Nm base — but it can no longer
accomplish anything. The gear stays the gear you are in, at any lateral distance, and the slot wall
pushes back the whole way; the only route to another gear is back through the tunnel. Two things had
to change for that: lateral confinement now belongs to the depth rather than to the state machine's
latch (overpowering one wall used to leave no lateral wall at all, and the guide then helped you
along to each column you passed), and the lateral release is gone entirely, because any distance at
which it fired was a distance at which the rest of the pattern came back and could capture you.

**A gear changed without my going through neutral.**
It cannot any more: a latched gear is released only by returning through the tunnel. If you see it
happen, that is a fault being reported, and worth telling me about.

**A bounce off a wall builds instead of dying.**
Raise **rebound absorption**. Full force while you lean, less on the throw-out, so each bounce
returns less energy than the last. Turning it *down* makes oscillation slower and stronger.

**The gear falls back out on its own.**
Raise **seated hold**. At full deflection the base drags the stick home with ~90% of its available
force, and a light hold simply loses.

**I can flick through the lockout too easily.**
The toll is **lockout force × lockout gate half-width** — widen the band or raise the force. Check
overall gain too: at 25% gain a 70% lockout is only ~2 Nm, which momentum beats easily.

**The wall ends at the tunnel feel square and catch the lever going past them.**
Feel → SLOT MOUTHS. *Rounded* fillets both flanks of every slot mouth, so the lever is eased past a
divider end instead of cornered on it. *Angled* chamfers one flank only, the side the next gear in
sequence lies on, so withdrawing with a little lateral pressure is carried that way - out of 2 toward
3, out of 5 toward 4, and so on. All three only ever remove force; none of them pushes.

*Mouth reach down the slot* (5000) is the one that matters. The base answers in 3-4 ms, in which a
lever being shifted travels 1500 counts or more, so shaping confined to a shorter stretch than that is
over before a single corrected force arrives - which is exactly why the first version of this feature
was thrown away. *Mouth opening* is a share of what the geometry safely allows, so it can never reach
the next column's slot or the lockout's band. Angled does nothing where there is no next gear (1, R)
or where the crossing would be the lockout (6 to 7), because a range gate does not help you across
itself.

**Entering a gear feels like it fights me sideways.**
That is the funnel. Lower **funnel into the slot** if it is too pushy; raise it if entries near a
column edge still dead-end against the wall.

**The shift does not feel like it seats itself.**
Raise **pull into the slot (the snick)**, and check **resistance entering the slot** is not so high
that it masks the flip over centre.

**Everything feels dead, especially the lockout and detents.**
Confirm **Measure polarity** reported a real result for both push axes rather than "barely moved",
and that overall gain is not near zero. Then tick **Release all forces (free stick)**: if the stick
is *still* stiff with that on, what you are feeling is the hardware, not the gate — check the MOZA
Cockpit settings in [hardware.md](hardware.md).

**The stick fights me everywhere or drifts to the stops.**
Polarity is wrong — forces are pushing the opposite way. Run **Measure polarity**.

## The rest of the dials

**Sliding across the gate.** *Humps between the other columns* (15%) is the light click between 1/2,
3/4, 5/6; *hump width* (2500) how far either side of a crest it peaks. *Pull into a column* (12%) is
the gentle centring onto the nearest column while in neutral, and *funnel into the slot* (40%) is
what that pull grows to as you push out of the channel toward a gear — the tapered mouth of the
gate. Neither acts across a column's own width, so there is no centre line to hunt around.
*Fore/aft drag while on a column* (5%) is residual resistance when lined up — keep it low or gears
get hard to take.

**Slot detent.** *Resistance entering the slot* (22%) → *pull into the slot* (35%) → *seated hold*
(55%). The profile resists, flips over centre, then settles.

**Damping** (25%) thickens the lever and calms free travel. It will not fix a buzz by itself — the
device's own damper is too weak to matter here, so this is computed in software from measured
velocity.

**Geometry tab.** *Gate wall fade width* (1500) is how far sideways the fore/aft wall takes to go
from open on a column to solid between columns — the mouth of each slot, seen from the tunnel, and
the sideways gradient that corners are made of. It is now the largest remaining gradient in the gate
at about 1.5× the wall face, so it is the one to widen if corners still feel harsh. The lateral guide
no longer has a ramp of its own: every sideways force rises at the wall's stiffness, so a gentler
force gets a shorter face instead of a steeper one. The enter/exit pairs are hysteresis bands — the
exit value must always be the looser one. The lockout gate has no position dial: it places
itself against the last main-section column, and the Monitor tab draws the band where it actually
is. *FFB loop rate* should stay at 1000; see [hardware.md](hardware.md) for
why higher buys nothing.

## Resets

The Geometry tab has scoped resets: **Forces**, **Geometry**, **Calibration**, **Everything**.
Measured polarity is deliberately *not* part of Forces or Geometry — it describes the hardware, not
a preference, and discarding it would silently re-arm the 10% force cap.

## If you are an agent changing a default

Changing a default in `EngineConfig.cs` and `ShifterSettings.cs` does **not** move a user who
already has that key saved. Also update the slider's `ResetValue` in the XAML, and patch
`PluginsData\Common\AB9ShifterPlugin.GeneralSettings.json` while SimHub is stopped — then say that
you did.
