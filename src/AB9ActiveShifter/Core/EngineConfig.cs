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

        // Telemetry-driven effects: vibration carriers summed onto the composed forces, and
        // the clutch grind. All off by default - they are additions to the gate, not part of
        // it - and every one dies when telemetry goes stale. Volumes are shares of the fixed
        // budgets in EffectComposer, scaled by the same effective gain as the gate.

        /// <summary>Continuous engine vibration whose pitch follows the revs.</summary>
        public bool FxEngineEnabled;
        public int FxEngineGainPct = 25;

        /// <summary>Carrier frequency as a multiple of engine revolutions: 1 = once per rev.</summary>
        public double FxEngineOrder = 1.0;

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

        /// <summary>Clutch positions below this percentage count as "clutch up".</summary>
        public int GrindClutchThresholdPct = 25;

        /// <summary>No grind below this speed, so garage shuffling stays quiet. Zero = always.</summary>
        public int GrindMinSpeedKmh;

        /// <summary>Whether a grinding shift is also balked: no registration, resist-only detent.</summary>
        public bool GrindRejectsGear = true;

        // Firmware effect polarity, measured per axis and per effect family. The AB9 does not
        // treat them alike - constant force and spring can disagree on the same axis - so these
        // are four independent facts, not one.
        public bool InvertConstantX;
        public bool InvertConstantY;
        public bool InvertSpringX;
        public bool InvertSpringY;

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
        /// </summary>
        public int CalibrationForcePct = 25;

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
        public int ColumnEdgeExit = 5000;
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

        /// <summary>Speeds below this are treated as leaning, in axis counts per second.</summary>
        public int YieldVelocityDeadband = 1500;

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
                ColumnEdgeExit,
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
