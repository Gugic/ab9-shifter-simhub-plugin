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
        public int TickHz = 400;

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
        public int LockoutStart = 48000;
        public int LockoutRamp = 2500;
        public int DetentHysteresis = 1500;
        public int MinEngageTicks = 2;

        // Forces (DirectInput units, before the overall gain)
        public int NeutralDetentCoeff = 600;
        public int WallCoeff = 8000;
        public int ChannelGuideCoeff = 600;
        public int ChannelWallCoeff = 9500;
        public int DamperCoeff = 800;
        public int SpringDeadBand = 150;
        public int ChannelDeadBand = 430;
        public int DetentResistMax = 2200;
        public int DetentPullMax = 3000;
        public int DetentHold = 1600;

        /// <summary>Lockout plateau force, as a percentage of the plugin's overall gain.</summary>
        public int LockoutForcePct = 70;

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
