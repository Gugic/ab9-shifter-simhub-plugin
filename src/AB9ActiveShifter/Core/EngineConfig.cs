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
        /// <summary>Where the Monitor tab starts shading the lockout. Display only: the barrier
        /// itself sits at the midpoint between the 5/6 and 7/R columns.</summary>
        public int LockoutStart = 48000;
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

        /// <summary>Light pull onto the nearest column while sliding along the channel.</summary>
        public int ColumnDetentForcePct = 12;

        /// <summary>Humps between the ordinary columns, felt as you slide across the gate.</summary>
        public int BarrierForcePct = 15;

        /// <summary>The lockout gate before 7/R: a flat fight at this force, snapping over at its centre.</summary>
        public int LockoutForcePct = 70;

        /// <summary>
        /// Half-width of the lockout gate. The lockout is a dot on the neutral channel, not a
        /// zone: the walls own the rest of the box, so it only needs to guard the crossing into
        /// the 7/R column. Flat force across each side of this band - the fight - with an
        /// over-centre release in the middle and free travel beyond.
        /// </summary>
        public int LockoutHalfWidth = 2200;

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

        /// <summary>How quickly the soft column detent reaches full strength.</summary>
        public int DetentRamp = 2500;

        /// <summary>Distance from a barrier's crest to its peak force.</summary>
        public int BarrierWidth = 2500;

        /// <summary>How far past a column the gate wall takes to close, so it arrives smoothly.</summary>
        public int WallBlend = 1500;

        /// <summary>No wall force within this distance of target, to stop the stick dithering.</summary>
        public int WallDeadBand = 120;

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
                LockoutStart,
                DetentHysteresis,
                MirrorColumns,
                MirrorSlots);
        }
    }
}
