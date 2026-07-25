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
        public int ChannelHalfEnter = 1400;
        public int ChannelHalfExit = 2400;
        public int ColumnEdgeEnter = 2600;
        public int ColumnEdgeExit = 5000;
        public int ColumnInnerHalfEnter = 1200;
        public int ColumnInnerHalfExit = 2400;
        public int EngageDepth = 4000;
        public int ReleaseDepth = 8000;
        public int DetentHysteresis = 1500;
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
        /// Half-width of the free corridor inside a slot. A real shifter slot has width: you feel
        /// its walls, not a pull toward its centre line. Modelling it as a restoring force instead
        /// puts an equilibrium point in the middle of the slot, and a stiff restoring force about
        /// an interior equilibrium is an oscillator - the stick overshoots, gets pushed back, and
        /// rings. The outer columns never showed it because their force is one-sided against the
        /// end of travel, which cannot hunt. Inside this corridor there is no lateral force at
        /// all, so there is nothing to oscillate about.
        /// </summary>
        public int SlotHalfWidth = 1100;

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
                MirrorSlots);
        }
    }
}
