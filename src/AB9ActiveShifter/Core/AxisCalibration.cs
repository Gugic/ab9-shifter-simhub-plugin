using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// What a pedal's raw axis means, once someone has pressed it once. Holds the travel that was
    /// measured, the direction it travels in, and the slack at each end, and turns a raw reading
    /// into the same 0..100 scale SimHub reports a clutch on - so a pedal read directly and a
    /// pedal read from the game arrive at <see cref="TelemetryState.Clutch"/> in identical units
    /// and nothing downstream can tell which one it got.
    /// <para>
    /// Pure and no-I/O, like everything in Core: the arithmetic is testable with no pedal set
    /// anywhere near the test runner, which matters because a wrong sign here silently inverts a
    /// clutch and makes the grind fire exactly when it should not.
    /// </para>
    /// </summary>
    public sealed class AxisCalibration
    {
        /// <summary>Raw reading at the released end of travel.</summary>
        public int RawMin;

        /// <summary>Raw reading at the pressed end of travel.</summary>
        public int RawMax;

        /// <summary>
        /// Slack at the released end, in scaled units (0..65535), swallowed so a pedal that rests
        /// noisily still reads as exactly zero.
        /// </summary>
        public int DeadzoneLow;

        /// <summary>Slack at the pressed end, so the last millimetre still reaches 100.</summary>
        public int DeadzoneHigh = ScaledMax;

        /// <summary>
        /// True when the axis falls as the pedal is pressed. Measured, never asked: half of all
        /// pedal sets read one way and half the other, and a user cannot be expected to know.
        /// </summary>
        public bool Invert;

        /// <summary>The scale the intermediate arithmetic works in, matching a 16-bit axis.</summary>
        public const int ScaledMax = 65535;

        /// <summary>An uncalibrated axis: passes its raw value through so movement is visible.</summary>
        public bool IsCalibrated { get { return RawMax > RawMin; } }

        public AxisCalibration Clone()
        {
            return new AxisCalibration
            {
                RawMin = RawMin,
                RawMax = RawMax,
                DeadzoneLow = DeadzoneLow,
                DeadzoneHigh = DeadzoneHigh,
                Invert = Invert
            };
        }

        /// <summary>
        /// Raw reading to 0..100 with 100 fully pressed - SimHub's clutch convention, chosen so
        /// the value can be dropped straight into <see cref="TelemetryState.Clutch"/>.
        /// </summary>
        public double ToPercent(int raw)
        {
            return Scale(raw) * 100.0 / ScaledMax;
        }

        /// <summary>
        /// The intermediate 0..65535 form, kept separate because a UI bar wants the fine scale
        /// and the deadbands are expressed in it.
        /// </summary>
        public int Scale(int raw)
        {
            int scaled;

            if (!IsCalibrated)
            {
                // Nothing measured yet. Passing the raw value through means a freshly picked axis
                // shows movement in the UI instead of sitting dead against a bogus upper bound,
                // which is the difference between "wrong axis" and "broken feature" to a user.
                scaled = GateGeometry.Clamp(raw, 0, ScaledMax);
            }
            else if (raw <= RawMin)
            {
                scaled = 0;
            }
            else if (raw >= RawMax)
            {
                scaled = ScaledMax;
            }
            else
            {
                scaled = (int)((long)(raw - RawMin) * ScaledMax / (RawMax - RawMin));
            }

            if (Invert) scaled = ScaledMax - scaled;

            // Deadzones apply after inversion, so they always mean "released end" and "pressed
            // end" rather than "low raw" and "high raw". Getting that backwards would put the
            // resting slack at the wrong end of an inverted pedal and leave it reading 100.
            if (scaled <= DeadzoneLow) return 0;
            if (scaled >= DeadzoneHigh) return ScaledMax;
            return scaled;
        }
    }
}
