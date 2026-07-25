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
| Funnel into the slot | 40% | Steers an off-column push into the slot instead of blocking it. |
| Wall bite distance | 600 counts | How far into a wall force takes to reach full. **The most important stability dial.** |
| Seated hold | 55% | What keeps a gear engaged against the base's own centring. |

## Symptom → dial

**A wall buzzes or vibrates while I lean on it.**
Raise **wall bite distance** until it stops, and no further. Past its bite a wall is flat and cannot
oscillate; all trouble lives on the bite itself. If a longer bite makes the wall feel spongy before
the buzz clears, leave the bite moderate and raise **wall attack** to 10–25 ms instead — that
freezes force while you hold still, which is what quiets a sustained press.

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
travel.

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

**Geometry tab.** *Wall deadband* (120) stops dithering when already on target. *Column pull ramp*
(2500) shapes the soft neutral detent. The enter/exit pairs are hysteresis bands — the exit value
must always be the looser one. The lockout gate has no position dial: it places
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
