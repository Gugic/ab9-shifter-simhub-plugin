namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Immutable snapshot of the settings the FFB loop runs against. The engine swaps whole
    /// instances of this rather than reading live settings, so a tick always sees a
    /// consistent configuration.
    /// </summary>
    public sealed class EngineConfig
    {
        // Device
        public int VendorId = 0x346E;
        public int ProductId = 0x1000;
        public uint VJoyDeviceId = 1;

        /// <summary>
        /// Loop rate. Measured on this base: reads are free and fresh at ~1 kHz, and one
        /// SetParameters write costs 1.0 ms on the USB frame clock. At 1 kHz with one write
        /// per tick, the axis being rendered gets a fresh force every millisecond - the write
        /// pipe itself paces the loop when busy.
        /// </summary>
        public int TickHz = 1000;

        /// <summary>
        /// Which shift pattern to render. The H patterns share the gate engine and differ only
        /// in geometry (column count, which slots hold gears, whether a lockout exists);
        /// Sequential replaces the gate with a sprung fore/aft lever that pulses up/down
        /// buttons. See <see cref="GatePattern"/>.
        /// </summary>
        public GatePattern Pattern = GatePattern.H7R;

        /// <summary>
        /// How long a sequential shift holds its vJoy button down, in milliseconds. Long
        /// enough for a game polling at 60 Hz to see it several times; short enough that
        /// banging through gears releases each press before the next stroke fires.
        /// </summary>
        public int SeqPulseMs = 120;

        /// <summary>
        /// How much stroke remains past the sequential click before the end-stop wall begins,
        /// in axis counts. Measured from the firing point, so shortening the throw with
        /// EngageDepth moves the whole stroke together: centre to click, this much landing,
        /// then the wall. Without the stop the lever sailed on to the hardware stop through
        /// twenty thousand counts of nothing, which is what made the stroke feel endless.
        /// </summary>
        public int SeqOvertravel = 2500;

        /// <summary>
        /// The end-stop wall at the bottom of a sequential stroke, as a percentage of full
        /// scale. Rises over the wall bite past the overtravel, on top of the click's hold,
        /// and always toward centre - a wall, not a pocket, so releasing anywhere still
        /// returns the lever home. It gets the walls' full rebound absorption rather than
        /// the return spring's mild one, because banging shifts against it is its job.
        /// </summary>
        public int SeqStopForcePct = 90;

        /// <summary>
        /// The click's kick: a 25 ms burst of force fired the instant a sequential shift
        /// registers, in the direction of the stroke, as a percentage of full scale. This is
        /// the mechanism's stored energy letting go - the dogs dropping in - and it is
        /// rendered in TIME, not in space, deliberately. A spatial over-centre (force
        /// reversing past the threshold) was already rejected for the lockout because it
        /// refunds a flick, and here it has a worse failure: a lever released inside an
        /// over-centre pocket is pulled deeper, and a sequential lever must always come home
        /// to re-arm. A time-keyed burst cannot hold the lever anywhere - 25 ms later it is
        /// gone whatever the hand did - so the spring profile stays everywhere-restoring.
        /// It joins the composition beside the telemetry carrier, after the yield and the
        /// attack: the kick assists the stroke by definition, so the absorber would eat it,
        /// and a 15 ms attack would blunt most of a 25 ms hit. Scaled by the effective gain,
        /// the 10% polarity cap included, and clamped with everything else.
        /// </summary>
        public int SeqClickPct = 60;

        // Telemetry-driven effects: vibration carriers summed onto the composed forces, and
        // the clutch grind. All off by default - they are additions to the gate, not part of
        // it - and every one dies when telemetry goes stale. Volumes are shares of the fixed
        // budgets in EffectComposer, scaled by the same effective gain as the gate.

        /// <summary>Continuous engine vibration whose pitch follows the revs.</summary>
        public bool FxEngineEnabled;
        public int FxEngineGainPct = 25;

        /// <summary>
        /// The engine carrier's pitch anchor: its frequency at 1000 rpm, in Hz, scaling
        /// linearly with the revs from there (capped at 130 Hz, the renderable ceiling).
        /// Directly settable so the idle buzz is a number rather than an abstract order -
        /// 17 here is once per revolution; firing orders are multiples.
        /// </summary>
        public int FxEngineFreqAt1000Rpm = 17;

        /// <summary>Buzz when the revs reach the limiter.</summary>
        public bool FxLimiterEnabled;
        public int FxLimiterGainPct = 45;
        public int FxLimiterFreqHz = 55;

        /// <summary>Where the limiter buzz starts, as a percentage of the reported redline.</summary>
        public int FxLimiterFromPct = 96;

        /// <summary>Buzz while the game reports ABS actively pulsing.</summary>
        public bool FxAbsEnabled;
        public int FxAbsGainPct = 40;
        public int FxAbsFreqHz = 44;

        /// <summary>Buzz while traction control is cutting in.</summary>
        public bool FxTcEnabled;
        public int FxTcGainPct = 35;
        public int FxTcFreqHz = 60;

        /// <summary>
        /// Curbs and bumps, read out of the vertical acceleration - no game-specific surface
        /// data needed. A curb is a rapid shake in heave, which sustained load is not: a
        /// baseline tracker follows the slow part and only the shake drives the carrier.
        /// </summary>
        public bool FxCurbsEnabled;
        public int FxCurbsGainPct = 45;
        public int FxCurbsFreqHz = 40;

        /// <summary>Vertical shake, in G, at which the curb rattle reaches full volume.</summary>
        public double FxCurbsFullAtG = 1.0;

        /// <summary>One pulse when the game's own gear changes - confirmation up the lever.</summary>
        public bool FxShiftEnabled;
        public int FxShiftGainPct = 45;
        public int FxShiftFreqHz = 44;
        public int FxShiftDurationMs = 80;

        /// <summary>
        /// Volume follows a user-chosen SimHub property (0..100). The property name itself
        /// lives in ShifterSettings - the engine only ever sees the sampled value.
        /// </summary>
        public bool FxCustomEnabled;
        public int FxCustomGainPct = 30;
        public int FxCustomFreqHz = 44;

        /// <summary>
        /// Gear grind on a clutchless shift: pushing into a slot with the clutch above ground
        /// while the engine turns rattles the lever, and with <see cref="GrindRejectsGear"/>
        /// the gear refuses to register until the clutch goes down. H patterns only.
        /// </summary>
        public bool GrindEnabled;
        public int GrindGainPct = 60;
        public int GrindFreqHz = 33;

        /// <summary>
        /// The balk wall: while a grinding shift is being rejected, this much force stacks on
        /// top of the entry resistance and stays, so the slot is a border the lever visibly
        /// cannot pass - a blocking synchro ring - rather than a light lean. Only acts while
        /// <see cref="GrindRejectsGear"/> is balking the shift; zero leaves the old
        /// resistance-only feel. Takes the wall attack like every wall, because while balked
        /// there is no snick to exempt.
        /// </summary>
        public int GrindWallPct = 70;

        /// <summary>Clutch positions below this percentage count as "clutch up".</summary>
        public int GrindClutchThresholdPct = 25;

        /// <summary>No grind below this speed, so garage shuffling stays quiet. Zero = always.</summary>
        public int GrindMinSpeedKmh;

        /// <summary>Whether a grinding shift is also balked: no registration, resist-only detent.</summary>
        public bool GrindRejectsGear = true;

        /// <summary>
        /// How the clutch decides the grind. <see cref="GrindClutchMode.Threshold"/> is the
        /// original behaviour and the default: one line, grinding on the up side of it. It has
        /// the virtue of being unambiguous, and the vice of a real clutch not working that way.
        /// <see cref="GrindClutchMode.Progressive"/> instead scales the grind across the pedal's
        /// travel from the bite point upward, so feathering it out feathers the grind out.
        /// </summary>
        public GrindClutchMode GrindClutchMode = GrindClutchMode.Threshold;

        /// <summary>
        /// Where the clutch starts to bite, as a percentage of pedal travel. A property of the
        /// car rather than of the pedals, so it cannot be measured from the hardware the way
        /// travel and direction can - it is set, not calibrated.
        /// <para>
        /// Deliberately not grind-specific despite the grind being its first consumer: it is the
        /// one point on a clutch's travel that means anything mechanically, so anything else that
        /// wants to know where the drivetrain starts to connect asks this and not a second dial.
        /// </para>
        /// </summary>
        public int ClutchBitePointPct = 25;

        /// <summary>
        /// A short pulse as the clutch passes its bite point, in either direction, so the lever
        /// tells the hand where the engagement point is. Off by default like every carrier.
        /// </summary>
        public bool FxBiteEnabled;
        public int FxBiteGainPct = 35;
        public int FxBiteFreqHz = 50;
        public int FxBiteDurationMs = 60;

        /// <summary>
        /// How many times to thump the lever after a profile switch, so the hand can count which
        /// profile arrived without looking at a screen. Set by the plugin to the profile's own
        /// position in the store - one for the first, two for the second - and zero to say nothing.
        /// Not a dial anyone types: the count IS the answer.
        /// </summary>
        public int ProfileConfirmPulses;

        /// <summary>
        /// Where the clutch reading comes from. The game's own telemetry by default - it needs no
        /// setup and no second device handle.
        /// </summary>
        public ClutchSource ClutchSource = ClutchSource.GameTelemetry;

        /// <summary>
        /// The controller the clutch pedal lives on, as a DirectInput instance id. A machine
        /// fact, like the base's own ids and the measured polarity: it describes this rig and
        /// never travels in a shared profile.
        /// </summary>
        public string PedalDeviceId;

        /// <summary>Index into the fixed axis order of <c>PedalDevice</c>; -1 when unbound.</summary>
        public int PedalAxisIndex = -1;

        /// <summary>
        /// The pedal's measured travel, direction and slack. Machine fact, as above. Null until
        /// something has been captured, which is what makes the pedal source unusable until then.
        /// </summary>
        public AxisCalibration PedalCalibration;

        // Firmware effect polarity, measured per axis. The AB9 does not treat the axes alike -
        // this unit inverts constant force on X and not on Y - so these are two independent
        // facts, not one flag.
        //
        // Spring polarity is measured too, but has nowhere to apply: every wall in this gate is
        // a constant force (a DirectInput spring cannot make a wall on this base at any
        // coefficient - see docs/force-model.md), so every frame ships SpringX/SpringY as Off.
        // The spring probes survive as a device sanity check that gates the force cap, not as
        // settings. Reinstate the flags here if a spring ever drives the gate again.
        public bool InvertConstantX;
        public bool InvertConstantY;

        /// <summary>
        /// Gear layout preference: which end of the gate is first gear. These relabel the gear map
        /// only. They deliberately do not flip the axis readings, because spring offsets are sent
        /// to the device in its own coordinates - mirroring the readings alone would put every
        /// anchor on the wrong side and turn the gate springs into repellers.
        /// </summary>
        public bool MirrorColumns;
        public bool MirrorSlots;

        public bool PolarityConfirmed;

        /// <summary>Master force scale, 0..100. Hard-capped until the polarity wizard has run.</summary>
        public int OverallGainPct = 25;

        /// <summary>Ceiling applied to <see cref="OverallGainPct"/> while polarity is unconfirmed.</summary>
        public const int UnconfirmedGainCapPct = 10;

        /// <summary>
        /// Force used by the polarity measurement, as a percentage of full scale. Not subject to
        /// the unconfirmed-polarity cap: this is the measurement that lifts that cap, and it needs
        /// enough authority to visibly move the stick. Bounded well below full scale.
        /// <para>
        /// 10% is measured to be enough on this base - a probe stops the moment its direction is
        /// certain, so what it needs is a movement the estimator can call, not a large one. It was
        /// 25%, which worked and was simply more force than the job requires on an unmeasured base
        /// that might be about to push the wrong way.
        /// </para>
        /// </summary>
        public int CalibrationForcePct = 10;

        // Geometry (raw axis counts)

        /// <summary>
        /// How deep the neutral tunnel is free: no lateral guide, no fore/aft wall, nothing that
        /// varies with depth. It therefore has to be at least as deep as a hand's fore/aft slop
        /// while sliding sideways, or the hand spends its time in the transition band instead.
        ///
        /// Measured, from a recorded 25110-tick trace of exactly that movement: while sliding
        /// laterally in neutral the wander is p50 1848, p75 2639, p90 3215, max 4215 counts. At the
        /// 1400 this used to be, 65% of sliding samples were past it - the band designed to be
        /// crossed on the way to a gear was in fact where the hand lived, and every cross-gradient
        /// in it was being felt as the lever being pushed sideways for no visible reason. 2600 puts
        /// three quarters of ordinary sliding back on genuinely flat ground.
        /// </summary>
        public int ChannelHalfEnter = 2600;

        /// <summary>
        /// How far out of the tunnel counts as committed to a slot. Also the whole budget for the
        /// lateral field's one transition - free to slide across, then held in a slot - so the span
        /// between this and <see cref="ChannelHalfEnter"/> is what keeps that transition's slope down
        /// at the wall face rather than above it. Kept at 2600 counts wide when the enter band moved,
        /// so the slope is unchanged.
        /// </summary>
        public int ChannelHalfExit = 5200;
        public int ColumnEdgeEnter = 2600;
        public int ColumnInnerHalfEnter = 1200;
        public int ColumnInnerHalfExit = 2400;
        public int EngageDepth = 4000;
        public int ReleaseDepth = 8000;
        /// <summary>
        /// How far past a boundary the lateral guide keeps hold of the column it came from.
        ///
        /// It used to be 1500, and it was load-bearing: the boundary was a cliff - the guide's force
        /// reversed from one saturated plateau to the other across it - so a wide band was all that
        /// stopped the stick chattering between two opposite full-scale forces. Now that the field is
        /// faded to zero across every position the pick can flip at, that job is gone: a flip inside
        /// the window costs nothing because there is no force there to change.
        ///
        /// So it is deliberately small, because the window has to cover it and the window is dead
        /// space. At 400 an ordinary divider's dead strip is 800 counts rather than 3000, which is
        /// most of the lateral guidance the relief would otherwise have cost.
        /// </summary>
        public int DetentHysteresis = 400;
        public int MinEngageTicks = 2;

        // Wall strengths, as a percentage of full scale before the overall gain. These are
        // constant forces, not spring coefficients: a spring cannot produce a usable wall on
        // this hardware (see ForceComposer).

        /// <summary>Vertical guide: how firmly the stick is held on a column once in one.</summary>
        public int ColumnPinForcePct = 90;

        /// <summary>Horizontal guide: the gate wall between columns.</summary>
        public int ChannelWallForcePct = 90;

        /// <summary>Residual fore/aft resistance while lined up with a column. Keep this low.</summary>
        public int ChannelGuideForcePct = 5;

        /// <summary>
        /// Light pull onto the nearest column while sliding along the channel. It is the shallow end
        /// of one straight line that rises to the slot wall by the channel's exit, so it also sets
        /// how firmly an off-column entry is steered into its slot on the way in.
        /// </summary>
        public int ColumnDetentForcePct = 12;

        /// <summary>Humps between the ordinary columns, felt as you slide across the gate.</summary>
        public int BarrierForcePct = 15;

        /// <summary>
        /// The neutral spring: a lateral pull toward the 3/4 column while in the channel, so a
        /// released lever drifts home the way a real H lever rests at the 3/4 gate. Zero - the
        /// default - is off entirely.
        ///
        /// A constant-force render like everything else (a DirectInput spring cannot reach
        /// usable strength on this base), and shaped to dodge the oscillator trap that pulls
        /// toward a line fall into: no force at all across the home column's own width, so the
        /// equilibrium is a flat region rather than a point; one wall-stiffness face; a flat
        /// plateau everywhere beyond. It fades out with depth exactly like the humps, so a held
        /// gear feels no sideways pull, and it is continuous in x - anchored to one fixed
        /// column - so unlike the nearest-column guide it has no handover to relieve. Around
        /// 25-30% it out-pulls the default detent and humps and the lever self-returns from
        /// anywhere in the channel; like every pull toward a place, raise it in moderation.
        /// </summary>
        public int HomeSpringPct = 0;

        /// <summary>The lockout gate before 7/R: a flat one-way fight at this force, all the way across.</summary>
        public int LockoutForcePct = 70;

        /// <summary>
        /// Half-width of the lockout gate. The lockout is a dot on the neutral channel, not a
        /// zone: the walls own the rest of the box, so it only needs to guard the crossing into
        /// the 7/R column. Flat one-way force across the band, free travel beyond. The width is
        /// also the gate's energy budget - force times band is the toll a flick must pay to get
        /// through - so if fast slams still sneak past, widen this or raise the force.
        ///
        /// Geometry, not just feel: the gate is positioned against the last main-section column
        /// and the width decides how far past it the band reaches. See GateGeometry.LockoutCentre.
        /// </summary>
        public int LockoutHalfWidth = 2200;

        /// <summary>
        /// Shape of the slot mouths. Square is the rectangular notch the gate has always had, and
        /// stays the default so the setting does nothing until it is chosen.
        /// </summary>
        public SlotMouthShape MouthShape = SlotMouthShape.Square;

        /// <summary>
        /// How far down the slot the mouth shaping reaches, from the edge of the tunnel.
        ///
        /// This is the dial that decides whether the feature does anything at all, and it is why
        /// the first design of it was thrown away. Shaping confined to the channel's own hysteresis
        /// band gave a patch 1000 counts deep, and a lever covers 1500-2000 counts inside the 3-4 ms
        /// the base takes to answer - so at shift speed not one corrected force sample landed inside
        /// the patch, and the assist arrived after the lever had gone. Reaching several thousand
        /// counts down the slot instead means the shaping spans the whole withdrawal stroke and
        /// several round trips, which is the difference between a feature and a rounding error.
        /// </summary>
        public int MouthDepth = 5000;

        /// <summary>
        /// How much of the safe opening to use, as a percentage. Expressed against a derived
        /// maximum rather than in counts so it cannot be set into a geometry that reaches the
        /// neighbouring column's band or the lockout's - the trap the slot corridor fell into by
        /// being silently clamped from what the user set to less than half of it.
        /// </summary>
        public int MouthOpenPct = 100;

        /// <summary>
        /// Steepest mouth flank, in lateral counts per count of depth. Not a user dial: it is the
        /// whole stability argument for the feature in one number. The flank is a cross-gradient,
        /// and at this value its force gradient is at most half the wall face however the other
        /// dials are set.
        /// </summary>
        public const double MouthSlopeMax = 0.5;

        // Force shaping (axis counts)

        /// <summary>
        /// How far into a wall the force takes to reach its plateau - the wall's face, and the
        /// dial that sets its character. The plateau past the face is flat and cannot
        /// oscillate; the face is a gradient rendered through USB delay, which no software
        /// damping can stabilise, so the face is always a compromise. Too short and it is a
        /// step: contact lands as a delay-late blow and a light sustained press vibrates
        /// against it. Too long and the wall goes spongy and hosts the old wide buzz. The
        /// 1 kHz loop roughly doubled the stable range compared to where this project started,
        /// so a middle value now holds firm and stays quiet - found by feel on the hardware.
        /// </summary>
        public int WallRamp = 600;

        /// <summary>Distance from a barrier's crest to its peak force.</summary>
        public int BarrierWidth = 2500;

        /// <summary>
        /// How far sideways the fore/aft gate wall takes to go from open, on a column, to solid
        /// between columns - the mouth of each slot as felt from the tunnel. Narrow and the gate
        /// snaps shut as the stick leaves a column; wide and every entry feels vague. Being a
        /// sideways gradient in a fore/aft force, it is also what a corner is made of, and the
        /// one dial that softens corners without touching the walls themselves.
        /// </summary>
        public int WallBlend = 1500;

        /// <summary>
        /// How much of a wall's force is given up when it is accelerating the stick along the
        /// direction it is already moving - the rebound - as a percentage. Forces resisting
        /// motion, and forces on a stick that is holding still, are never reduced.
        ///
        /// This is what makes the walls stable. A position-to-force loop over USB carries
        /// 5-10 ms of delay, and a stiff wall rendered through delay acts as negative damping:
        /// each overshoot returns with interest, and the wall rings. Returning less energy on
        /// the way out than was stored on the way in starves that cycle at the source. It is
        /// also how a real gate behaves - mechanical gates are friction-damped and do not
        /// fling the lever back.
        /// </summary>
        public int WallYieldPct = 45;

        /// <summary>
        /// Milliseconds for a wall's force to build to full scale once contact begins. Zero
        /// turns time shaping off entirely. This is the hammer fix: a flat wall is calm to
        /// lean on, but its face is a step, and a step delivered several milliseconds late
        /// lands as a blow - the stick is thrown back out, the hand brings it back, and it
        /// fires again, felt as ABS-like kicking. Nothing mechanical is a true step; real
        /// contact winds up over milliseconds. So the wall stays flat in position but becomes
        /// progressive in time: force may only grow this fast, release stays instant so a
        /// retreating stick is never chased, and a hand holding still against the wall gets a
        /// frozen force rather than one that tracks every sensor count - static friction, the
        /// piece that stops a light sustained press from vibrating on the wall's face.
        ///
        /// Off by default: a well-chosen bite usually settles the walls on its own. Reach for
        /// this if a short bite kicks on contact, or if corners hammer - that is where both
        /// axes' walls land at once.
        /// </summary>
        public int WallAttackMs = 0;

        /// <summary>
        /// Speeds below this are treated as leaning, in axis counts per second. This is the
        /// absorber's lean-or-launch classifier, and it must sit above the speed of a hand
        /// adjusting its lean, not merely above sensor noise. It shipped at 1500 - tremor
        /// level - and a leaning hand crosses tremor level with every micro-reversal, so each
        /// one fired a fresh cut, each cut kicked the lever, and each kick grew the next
        /// reversal: the absorber became a relay oscillator. Measured on real traces as a
        /// 26 Hz, 8155 DI peak-to-peak chatter leaning in a slot and a 12 Hz, 20000-count
        /// rebound being spat back off the lockout. The measured envelope of a hand genuinely
        /// holding against force tops out near 3700 counts/s; deliberate strokes run 15000 and
        /// up; wall launches 100000 and up. 10000 clears the first with margin and still
        /// catches the last within a millisecond of flight.
        /// </summary>
        public int YieldVelocityDeadband = 10000;

        /// <summary>How quickly the yield reaches full effect above the deadband, in counts per second.</summary>
        public int YieldVelocityBlend = 12000;

        /// <summary>
        /// Milliseconds for the absorber to hand back force it has cut. Cuts are instant -
        /// catching the launch is the whole job - but recovery is slewed, because the speed the
        /// yield keys on is an estimate that ripples under the device's ~500 Hz report
        /// quantisation, and an absorber that follows the estimate both ways renders the ripple
        /// as force texture: measured at 25-50% of the wall force at 250-500 Hz on a real
        /// trace, and felt as the lever grinding against a running gear the moment it moved
        /// under pressure. Slewed recovery costs nothing a hand can feel - the same-direction
        /// test already restores full force the instant the wall resists again, so this only
        /// ever deepens absorption while the wall assists. Zero makes recovery instant again.
        /// </summary>
        public int YieldRecoveryMs = 20;

        /// <summary>
        /// Half-width of the free corridor inside a slot. A real shifter slot has width: you feel
        /// its walls, not a pull toward its centre line. Modelling it as a restoring force instead
        /// puts an equilibrium point in the middle of the slot, and a stiff restoring force about
        /// an interior equilibrium is an oscillator - the stick overshoots, gets pushed back, and
        /// rings. The outer columns never showed it because their force is one-sided against the
        /// end of travel, which cannot hunt. Inside this corridor there is no lateral force at
        /// all, so there is nothing to oscillate about.
        ///
        /// Zero is a legitimate setting, not a degenerate one: it turns the slot into a rail -
        /// the lever pulled straight onto the column line, the native shifter-mode topology.
        /// The interior equilibrium comes back with it, so a rail is only stable at moderate
        /// pin force; a railed gear that trembles wants the slot wall lowered, not damping.
        /// See docs/force-model.md, "The rail gate".
        /// </summary>
        public int SlotHalfWidth = 1100;

        /// <summary>
        /// How deep the neutral tunnel is free of fore/aft force - the fore/aft twin of
        /// <see cref="SlotHalfWidth"/>, and deliberately a separate dial from
        /// <see cref="ChannelHalfEnter"/>: that band is where the state machine's hysteresis and
        /// the lateral field's one depth transition live, and it must stay wide enough to cover a
        /// hand's fore/aft wander (measured p90 3215 counts). This dial only sets where the
        /// tunnel's centring force begins, and is clamped to the enter band from above. At the
        /// default the two coincide and the tunnel is the corridor it has always been. At zero
        /// the tunnel becomes a rail - no free fore/aft space anywhere - and together with a zero
        /// slot width the lever is guided on exactly one axis at every point of the gate.
        /// </summary>
        public int ChannelFreeDepth = 2600;

        /// <summary>
        /// Kinetic friction at the walls, as a percentage of the wall force currently being
        /// applied on that axis - the gate surfaces' own mu. Viscous below its saturation
        /// speed so it is continuous through zero velocity (a Coulomb sign-flip at tremor
        /// would be a relay, the exact disease the yield deadband cures), Coulomb-flat above.
        ///
        /// This is the dissipation for the band the other stabilisers cannot reach. Below the
        /// yield deadband nothing may cut (leaning must be solid); global damping costs throw
        /// lightness everywhere; the static hold only guards a hand already settled. What was
        /// left was a face gradient, the loop's delay, and a hand - and that hunts: with the
        /// yield relay fixed, the lockout trace still showed a 17.7 Hz, 8000-count cycle
        /// riding the entry face, the residual of the same instability. Friction scaled by
        /// the engaged force is zero in free travel, the corridors and the channel - it costs
        /// nothing in lightness - and on a face it is ~17x the delay's negative damping at
        /// this default, which is what lets a lean settle instead of hunt. The honest render,
        /// too: real gates are friction-damped exactly like this.
        /// </summary>
        public int WallFrictionPct = 15;

        /// <summary>
        /// Velocity damping, as a percentage of full force at <see cref="DampingReferenceSpeed"/>.
        /// This is what stops a stiff wall oscillating. It is computed here from the axis
        /// readings rather than asked of the device, because a damper is a condition effect and
        /// conditions are far too weak on this base to settle anything.
        /// </summary>
        public int DampingPct = 25;

        /// <summary>Speed at which damping reaches its full percentage, in axis counts per second.</summary>
        public int DampingReferenceSpeed = 120000;

        /// <summary>The device's own damper condition effect. Largely decorative on this base.</summary>
        public int DamperCoeff = 800;

        // Slot detent, on the same percent-of-full-force scale as the walls so the two can be
        // compared at a glance.

        /// <summary>Resistance felt on the way into a slot.</summary>
        public int DetentResistPct = 22;

        /// <summary>The pull over centre that seats the gear - the snick.</summary>
        public int DetentPullPct = 35;

        /// <summary>
        /// What keeps a gear engaged. This has to out-pull whatever the base does on its own:
        /// an AB9 that is still self-centring drags the stick home with most of its available
        /// force at full deflection, and a light hold simply loses that argument.
        /// </summary>
        public int DetentHoldPct = 55;

        /// <summary>
        /// The wall at the bottom of an H slot, as a percentage of full scale - the fore/aft twin
        /// of the sequential lever's <see cref="SeqStopForcePct"/>, and the whole of what makes a
        /// short throw possible.
        /// <para>
        /// Zero - the default - is the gate as it has always been: past the engage line the seated
        /// hold simply keeps pulling, so the lever runs on to the base's own mechanical stop and a
        /// seated gear always sits at full deflection whatever <see cref="EngageDepth"/> says.
        /// Shortening the throw with that dial alone therefore only makes the gear <em>register</em>
        /// earlier; the travel is unchanged. Above zero the slot gets a bottom of its own and the
        /// throw becomes the number it claims to be.
        /// </para>
        /// </summary>
        public int SlotStopForcePct;

        /// <summary>
        /// How much stroke remains past the engage line before that end-stop begins, in axis
        /// counts. Measured from the engage line, exactly like <see cref="SeqOvertravel"/>, so
        /// shortening the throw moves the whole slot together: centre to the engage line, this
        /// much landing, then the wall.
        /// <para>
        /// It is a free landing rather than a held one. The seated hold fades out over one wall
        /// bite past the engage line and the rest of this span carries no fore/aft force at all,
        /// which is what keeps the seat a <em>region</em> instead of a point - the same reason
        /// <see cref="SlotHalfWidth"/> and <see cref="ChannelFreeDepth"/> are corridors and not
        /// pulls toward a centre line. A lever left anywhere in that landing stays there, because
        /// the base does not self-centre once MOZA Cockpit's Spring is at 0 (see docs/hardware.md).
        /// </para>
        /// <para>
        /// The default is the room a default-throw gate has past its engage line, so at the shipped
        /// geometry the wall would begin exactly at the end of travel and can never be met. Nothing
        /// about the stop is felt until both this and the force above are set deliberately.
        /// </para>
        /// </summary>
        public int SlotOvertravel = 4000;

        /// <summary>The gain actually applied, after the unconfirmed-polarity safety cap.</summary>
        public double EffectiveGain
        {
            get
            {
                int pct = OverallGainPct;
                if (!PolarityConfirmed && pct > UnconfirmedGainCapPct) pct = UnconfirmedGainCapPct;
                return GateGeometry.Clamp(pct, 0, 100) / 100.0;
            }
        }

        /// <summary>All forces off, for checking the stick moves freely.</summary>
        public bool FreeStick;

        public GateGeometry BuildGeometry()
        {
            return new GateGeometry(
                ChannelHalfEnter,
                ChannelHalfExit,
                ColumnEdgeEnter,
                ColumnInnerHalfEnter,
                ColumnInnerHalfExit,
                EngageDepth,
                ReleaseDepth,
                LockoutHalfWidth,
                DetentHysteresis,
                MirrorColumns,
                MirrorSlots,
                Pattern);
        }
    }
}
