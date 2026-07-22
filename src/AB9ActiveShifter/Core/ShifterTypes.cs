using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>Gate columns, left to right. C4 is the lockout-protected 7/R column.</summary>
    public enum Column
    {
        None = -1,
        C1 = 0,
        C2 = 1,
        C3 = 2,
        C4 = 3
    }

    /// <summary>Which end of a column a gear sits at. Fwd is stick-away (low Y).</summary>
    public enum ShiftDir
    {
        None = 0,
        Fwd = 1,
        Back = 2
    }

    public enum GateState
    {
        /// <summary>In the horizontal neutral channel, no gear selected.</summary>
        Neutral,

        /// <summary>Out of the channel, moving along a latched column, not yet deep enough.</summary>
        Traveling,

        /// <summary>Seated in a gear; the vJoy button is held.</summary>
        Engaged
    }

    public enum EnginePhase
    {
        Stopped,
        SearchDevice,
        OpenDevice,
        Run,
        Faulted
    }

    /// <summary>
    /// One DirectInput condition, in DirectInput units (positions and offsets ±10000,
    /// coefficients and saturations 0..10000). Compared by value so the effect layer can
    /// skip redundant device writes.
    /// </summary>
    public struct SpringPreset : IEquatable<SpringPreset>
    {
        public int Offset;
        public int PositiveCoefficient;
        public int NegativeCoefficient;
        public int PositiveSaturation;
        public int NegativeSaturation;
        public int DeadBand;

        public static readonly SpringPreset Off = new SpringPreset
        {
            Offset = 0,
            PositiveCoefficient = 0,
            NegativeCoefficient = 0,
            PositiveSaturation = 10000,
            NegativeSaturation = 10000,
            DeadBand = 0
        };

        /// <summary>Symmetric spring pulling toward <paramref name="offset"/>.</summary>
        public static SpringPreset Centering(int offset, int coefficient, int deadBand)
        {
            return new SpringPreset
            {
                Offset = offset,
                PositiveCoefficient = coefficient,
                NegativeCoefficient = coefficient,
                PositiveSaturation = 10000,
                NegativeSaturation = 10000,
                DeadBand = deadBand
            };
        }

        public bool Equals(SpringPreset other)
        {
            return Offset == other.Offset
                && PositiveCoefficient == other.PositiveCoefficient
                && NegativeCoefficient == other.NegativeCoefficient
                && PositiveSaturation == other.PositiveSaturation
                && NegativeSaturation == other.NegativeSaturation
                && DeadBand == other.DeadBand;
        }

        public override bool Equals(object obj)
        {
            return obj is SpringPreset && Equals((SpringPreset)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Offset;
                h = (h * 397) ^ PositiveCoefficient;
                h = (h * 397) ^ NegativeCoefficient;
                h = (h * 397) ^ PositiveSaturation;
                h = (h * 397) ^ NegativeSaturation;
                h = (h * 397) ^ DeadBand;
                return h;
            }
        }
    }

    /// <summary>Everything the device layer needs for one tick.</summary>
    public struct ForceFrame
    {
        public SpringPreset SpringX;
        public SpringPreset SpringY;

        /// <summary>Lockout push, ±10000. Negative pushes toward -X (left, out of the 7/R column).</summary>
        public int ConstantX;

        /// <summary>Gate detent, ±10000. Positive pushes toward +Y (back).</summary>
        public int ConstantY;

        /// <summary>Anti-oscillation damping, 0..10000. Carried per frame so a gain change
        /// can be applied without recreating the effect.</summary>
        public int DamperCoefficient;
    }

    /// <summary>Result of one state machine step.</summary>
    public struct StateTransition
    {
        public GateState State;
        public Column Column;
        public ShiftDir Direction;

        /// <summary>0 = neutral, 1..8 where 8 is reverse.</summary>
        public int Gear;

        public bool GearChanged;
        public int PreviousGear;
    }

    /// <summary>Immutable copy of engine state, safe to read from the UI and property delegates.</summary>
    public sealed class EngineSnapshot
    {
        public EnginePhase Phase = EnginePhase.Stopped;
        public bool DeviceConnected;
        public bool VJoyConnected;
        public int RawX = GateGeometry.AxisCenter;
        public int RawY = GateGeometry.AxisCenter;
        public int X = GateGeometry.AxisCenter;
        public int Y = GateGeometry.AxisCenter;
        public GateState State = GateState.Neutral;
        public Column Column = Column.None;
        public int Gear;
        public string GearLabel = "N";
        public double LoopHz;
        public string StatusMessage = "Stopped";
        public string DeviceName = "";
    }
}
