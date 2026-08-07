# Tuning guide

The dials are spread over the plugin's **Setup**, **Feel**, **Effects** and **Geometry** tabs —
feel lives on Feel, the telemetry buzzes on Effects, and the positions the gate is built from on
Geometry. All of them apply on the next FFB tick, so nothing needs restarting. Forces are percentages of what the base can produce *before* the master
gain, so raising overall gain lifts the whole gate together and keeps tuned ratios intact.

Run **Measure polarity** first. Until it succeeds, gain is capped at 10% and everything feels
light — that is the safety cap doing its job, not a tuning problem. The Feel, Effects, Geometry and
Monitor tabs stay hidden until polarity is measured and a vJoy device is available, so if this
guide describes a tab you cannot see, that is why.

## What the Feel tab shows you while you turn a dial

Four things worth knowing before working through the rest of this guide, because they answer
questions that used to need a trip to the rig:

- **Force curves.** Each section draws the shape its dials produce — the slot detent against push
  depth, the tunnel's fore/aft wall, the lateral field across the whole gate, and the corridor a
  slot mouth opens. Each is sampled from the force code itself, not redrawn, so what you see is
  what the gate renders. A dot tracks your actual stick position on the curve when the base is
  connected.
- **Effective values.** Two dials are silently bounded by the geometry rather than by their
  slider: *wall bite distance* (the room between columns) and *tunnel free depth* (the state
  band). Both now print what the gate will actually use, and say so when the slider is asking for
  more than it can have. See the bite symptom below — that gap is what it feels like.
- **Undo per slider**, and a marker on every dial changed since the profile was opened, so an
  experiment can be walked back one dial at a time instead of by resetting the tab.
- **Raw counts or percent of column spacing.** The lateral dials can be read either way. Percent
  is the more portable view, because column spacing changes with the pattern — a slot width tuned
  on 7+R is a different fraction of the room on 5+R, which has three columns instead of four.

## Start here

| Dial | Default | What it does |
| --- | --- | --- |
| Overall gain | 25% | Master scale. This is a 12 Nm base; raise it slowly. |
| Gate wall, between columns | 90% | The fore/aft wall that stops you entering a gear you are not lined up with. |
| Slot wall, once in a gear | 90% | The sideways walls of a slot. |
| Lockout gate, guarding 7/R | 70% | The push-through toll before 7 and R. Sits against the 5/6 column; width sets the toll with it. |
| Neutral spring toward 3/4 | 0% (off) | The home spring: pulls the lever along the channel toward the 3/4 column, where a real H lever rests. Around 25–30% a released lever walks home past the humps; fades out with depth so a held gear feels nothing. Follows the mirror flags. |
| Wall attack | 0 ms (off) | Smooths contact and freezes force while you press and hold still. Applies to the lockout too. |
| Wall friction | 15% | The gate surfaces' own grip: drag equal to this share of whatever force you are pressed against. Zero in free travel by construction, so it costs no lightness. Note: for lean-flutter the effective fix is MOZA Cockpit's Damper at ~15% (zero-latency, at the servo); this dial is the software-side supplement. |
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
layout section). The **sequential stroke** section (Feel tab, sequential only) owns the stroke
itself: **actuation throw** is the distance from centre to the firing line, in the sequential
hand's own units — shorten it for a quicker shift, and it moves the re-arm line with it so a
short throw cannot machine-gun (it is the same stored fact as the Geometry tab's engage depth,
which measures from the end of travel instead). **Shift click kick** is what makes the click
*hit*: a 25 ms burst in the stroke's direction the instant the shift registers, which then
throws the lever onto the end-stop — raise the kick for a sharper mechanism, the **end-stop
wall** for a harder landing, and use detent resist/hold for the lean of the stroke around them.
The spring reaches full resistance exactly at the threshold and the click moves with it, so a
short throw stays progressive rather than becoming a wall. The landing past the click and the
end-stop are both measured from the firing point, so the whole stroke shortens as one thing.

Keep one **profile** per pattern you actually use — every dial, the pattern included, is stored
per profile, so switching is one dropdown.

**Switching without the dropdown.** *Next profile* and *Previous profile* are actions, so they
bind to a wheel button or a key in SimHub's own **Controls** page like any other. Which profiles
they walk through is on the Setup tab under *Profile hotkeys*: tick the ones you want, or tick
none and they walk through all of them. Switching releases any held gear and clears a sequential
pulse in flight before the new gate is applied, so it is safe to press while driving — which is
the point, if you keep an H profile and a sequential one for different cars.

## Telemetry effects (the Effects tab)

Vibration driven by the game, on top of the gate: all off by default, all silenced within half a
second of the game pausing or closing, all scaled by the overall gain (the 10% polarity cap
included). Each row is enable + volume + frequency:

| Effect | Fires when | Notes |
| --- | --- | --- |
| Gear grind | Pushing into a gear with the clutch up while the engine turns | The headline. With **rejection** on, the gear also refuses to register and the slot becomes a **balk wall** — the entry resistance with `Balk wall (%)` stacked on top, a border the lever grinds against, louder the harder it is forced — until the clutch goes down, then the gear thunks straight in. H patterns only; an engaged gear never grinds; sequential is exempt (dog boxes shift clutchless by design). |
| Engine vibration | Whenever the engine turns | Pitch scales with the revs, anchored by **frequency at 1000 rpm** — set what idle should feel like; 17 is once per revolution, 34 ≈ a four-cylinder's firing pulses. Capped at 130 Hz. Keep the volume low — it never stops. |
| Rev limiter | Revs ≥ the redline percentage | Silent when the game reports no redline. |
| ABS / TC | The game's own ABS-active / TC-active flags | Different default pitches (44 / 60 Hz) so both firing in one corner stay distinguishable. |
| Curbs and bumps | Rapid shake in the car's vertical acceleration | No surface data needed: a baseline tracker follows sustained load (corners, braking) so only the shake plays, with a ~150 ms ring-down that keeps a rumble strip's rhythm. **Full volume at (G)** is the sensitivity — lower it to make gentle curbs louder. Silent in games that report no acceleration; the ShakeIt bridge below is the per-wheel alternative. |
| Clutch bite point | The clutch crosses the bite point, either way | Tells the hand where the drivetrain connects. Silent while the pedal moves without crossing, so riding the clutch stays quiet. Set the point itself on the **Setup** tab — it is a property of the car, not of the pedals. |
| Gear shift pulse | The game's reported gear changes | Confirms what the game *accepted* — useful in sequential and with paddle cars. |
| Custom property | Any SimHub property, 0–100 → volume | Try `DataCorePlugin.GameData.Throttle` to hear it work. The real use: a ShakeIt Bass Shakers effect group with *Export property* enabled puts road rumble, wheel slip and impacts on the lever with all of ShakeIt's own tuning. |

Symptoms:

| Symptom | Dial |
| --- | --- |
| Grind never fires | The game must report the clutch pedal. Watch the `Clutch` property in SimHub: if a pressed pedal reads low, lower the **clutch pressed above** threshold. Check the engine is running and any speed floor. |
| Grind fires in the garage / pit lane | Raise **only grind above (km/h)**. |
| A gear registers despite grinding | **Reject the gear while grinding** is off, or the game itself needs no clutch — the rejection is ours, not the game's. |
| The grind feels like a lean, not a border | Raise **balk wall (%)** — it stacks on the entry resistance while a shift is rejected. It only acts with rejection on. |
| Effects feel weak | They share the overall gain; check polarity is confirmed (the 10% cap mutes effects too) before raising per-effect volumes. |
| A buzz outlives the game | It cannot, by design (500 ms staleness cut). If you feel one, it is the gate — record a trace. |

## The clutch (Setup tab)

Where the clutch reading comes from, and the one number that describes your car rather than your
hardware.

**Read from** is *the game's telemetry* by default, which needs no setup and is right whenever the
game reports the pedal at all. Switch to *the pedal itself* for either of two reasons:

- the game reports no clutch, so the grind has nothing to key on;
- the grind feels late — telemetry arrives at the game's update rate, tens of milliseconds old
  against a shift that is over in a couple of hundred.

The pedal is opened **non-exclusively**, so the game keeps reading it exactly as before. Press
*Press the clutch to bind it*, hold still a moment, then press the clutch fully and let it back up:
which axis, which direction it travels and how much slack it has at rest are all measured from that
one press. A pedal whose axis falls when pressed is detected and handled — nothing needs typing,
and there is no invert box to get wrong. The binding describes your rig, so it never travels in a
shared profile.

**Bite point** is where the clutch starts to pick up, as a percentage of travel. It cannot be
measured from the pedal — nothing in the motion marks it — so set it where the car actually bites.
Two things use it: the bite-point pulse on the Effects tab, and the grind's *fade* mode.

| Symptom | Dial |
| --- | --- |
| The grind is all-or-nothing, and feathering the clutch does nothing | Effects → **How the clutch decides** → *Fade across the pedal from the bite point*. Then set the bite point honestly, because that is where the fade ends. |
| The grind fades out too early or too late | The **bite point**, not the threshold — the threshold belongs to the other mode and is ignored while fading. |
| The bite-point pulse fires while I ride the clutch | It cannot: it is edge-triggered on the crossing. If you feel something there it is the grind or the engine hum. |
| The pedal reads 0% however hard I press | Wrong axis, or the pedals were rebound to a different device. Re-run the capture; the reading under the button shows the live value while you press. |

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

**The lever chatters rhythmically while I hold it against force — held in a gear, or leaning on
the lockout, which pumps the lever back out in kicks.**
Fixed structurally, in two layers, and worth telling apart from the bite oscillation above: it is
slower (12–26 Hz on the traces that caught it), it needs a hand leaning on the lever to sustain
it, and it happens even on *flat* force — the lockout's core has no gradient at all, and it
chattered anyway. The first layer was the rebound absorber's velocity deadband sitting at
sensor-noise level, below hand tremor, so every micro-reversal of a lean fired a fresh cut: the
force stepped between full and the yield floor across zero velocity like a relay, and each step
kicked the lever into a bigger reversal — up to 20000-count swings off the lockout. The deadband
now sits above the measured envelope of a leaning hand and below deliberate strokes, so a lean
feels one continuous force whichever way tremor points. The second layer surfaced the moment the
first was fixed: a smaller, faster hunt (17.7 Hz on the follow-up trace) riding the *face*,
because the sub-deadband band then had no dissipation at all — no cut is allowed there, damping
was zero, and the static hold only guards a hand already settled. That is what **wall friction**
was built for: drag proportional to the engaged force, zero in free travel — and the hardware
verdict is that it was *not enough*, because everything the plugin renders arrives 3–4 ms late,
a large slice of a 17 Hz cycle. What settles the lean-hunt on this base is **MOZA Cockpit's
Damper at ~15%** — real damping at the servo loop, ahead of the delay, and free of the
throw-weight cost software damping has (see hardware.md). If a flutter while leaning returns:
Cockpit damper first, wall friction second, software damping last.

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

The Feel tab now shows this rather than leaving it to be discovered on the rig. Under *Wall bite
distance* it prints the **effective** bite — what the gate actually renders after the room between
columns has had its say — and says "capped down from N" when the slider is asking for more than
the geometry allows. If the two numbers differ, the slider has been lying to you, and this symptom
is what that feels like. The **Gate Walls** graph beside it draws the resulting curve, so a
divider with no plateau left is visible as a shape rather than inferred from a number.

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

**The lever trembles resting at the 3/4 column with the neutral spring on.**
The home spring is a pull toward a place, so it has the rail gate's hunt ceiling: an interior
equilibrium rendered through delay is only stable at moderate strength. Lower the **neutral
spring**, never raise damping. The spring is deliberately dead across the home column's own
width — a lever parked there sits on flat ground — so trembling means the plateau just outside
that dead zone is too strong for the loop's delay, exactly like a railed column that trembles.

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
large **column handover width** widens the dead strip at each divider, because the window must cover it; it
used to be 1500 and only ever existed to hide the cliff that is now gone. (It is called
`DetentHysteresis` in the settings file and in the code — a name left over from a job it no longer
does. It has nothing to do with the slot detents.)

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
That is the pull onto the column growing as you leave the tunnel. There is no separate funnel dial —
one stiffness serves every lateral force — so lower **pull into a column** if it is too pushy. If
entries near a column edge dead-end against the wall instead, open the **slot mouths**: set the mouth
shape to angled and raise its reach and opening.

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
the gentle centring onto the nearest column while in neutral, which grows into the full slot wall as
you push out of the tunnel toward a gear. It does not act across a column's own width, so there is no
centre line to hunt around.
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
