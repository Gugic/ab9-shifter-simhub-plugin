using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Pure geometry of the 7+R gate: where the columns are, how wide the bands are, and
    /// the hysteresis pairs that keep the state machine from chattering on a boundary.
    ///
    /// Axis units are raw DirectInput 0..65535 with 32767 at centre. X grows to the right,
    /// Y grows toward the player (so a gear at low Y is "forward").
    /// </summary>
    public sealed class GateGeometry
    {
        public const int AxisMin = 0;
        public const int AxisMax = 65535;
        public const int AxisCenter = 32767;
        public const int ColumnCount = 4;

        /// <summary>Full-scale force in DirectInput units.</summary>
        public const int ForceMax = 10000;

        private readonly int[] _targets;

        public int ChannelHalfEnter { get; private set; }
        public int ChannelHalfExit { get; private set; }
        public int ColumnEdgeEnter { get; private set; }
        public int ColumnEdgeExit { get; private set; }
        public int ColumnInnerHalfEnter { get; private set; }
        public int ColumnInnerHalfExit { get; private set; }
        public int EngageDepth { get; private set; }
        public int ReleaseDepth { get; private set; }
        public int LockoutStart { get; private set; }
        public int DetentHysteresis { get; private set; }

        public GateGeometry(
            int channelHalfEnter,
            int channelHalfExit,
            int columnEdgeEnter,
            int columnEdgeExit,
            int columnInnerHalfEnter,
            int columnInnerHalfExit,
            int engageDepth,
            int releaseDepth,
            int lockoutStart,
            int detentHysteresis)
        {
            // Exit bands must be looser than enter bands or the hysteresis inverts and
            // the state machine oscillates. Clamp rather than throw: these come from
            // user-editable settings and a bad value must not kill the FFB loop.
            ChannelHalfEnter = channelHalfEnter;
            ChannelHalfExit = Math.Max(channelHalfExit, channelHalfEnter + 1);
            ColumnEdgeEnter = columnEdgeEnter;
            ColumnEdgeExit = Math.Max(columnEdgeExit, columnEdgeEnter + 1);
            ColumnInnerHalfEnter = columnInnerHalfEnter;
            ColumnInnerHalfExit = Math.Max(columnInnerHalfExit, columnInnerHalfEnter + 1);
            EngageDepth = engageDepth;
            ReleaseDepth = Math.Max(releaseDepth, engageDepth + 1);
            LockoutStart = lockoutStart;
            DetentHysteresis = detentHysteresis;

            _targets = new int[ColumnCount];
            for (int i = 0; i < ColumnCount; i++)
            {
                _targets[i] = (int)Math.Round(i * (double)AxisMax / (ColumnCount - 1));
            }
        }

        public int ColumnTarget(Column c)
        {
            return c == Column.None ? AxisCenter : _targets[(int)c];
        }

        /// <summary>Converts a raw axis reading to the DirectInput ±10000 force/position scale.</summary>
        public static int AxisToDi(int axis)
        {
            double di = (axis - (AxisMax / 2.0)) * (2.0 * ForceMax / AxisMax);
            return Clamp((int)Math.Round(di), -ForceMax, ForceMax);
        }

        /// <summary>Inverse of <see cref="AxisToDi"/>.</summary>
        public static int DiToAxis(int di)
        {
            double axis = (di * (AxisMax / (2.0 * ForceMax))) + (AxisMax / 2.0);
            return Clamp((int)Math.Round(axis), AxisMin, AxisMax);
        }

        public static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>Which column x is inside, using the tight enter bands. None if between columns.</summary>
        public Column ColumnAt(int x)
        {
            if (x <= ColumnEdgeEnter) return Column.C1;
            if (x >= AxisMax - ColumnEdgeEnter) return Column.C4;
            if (Math.Abs(x - _targets[1]) <= ColumnInnerHalfEnter) return Column.C2;
            if (Math.Abs(x - _targets[2]) <= ColumnInnerHalfEnter) return Column.C3;
            return Column.None;
        }

        /// <summary>Whether x is still within the loose exit band of an already-latched column.</summary>
        public bool StillInColumn(Column c, int x)
        {
            switch (c)
            {
                case Column.C1: return x < ColumnEdgeExit;
                case Column.C4: return x > AxisMax - ColumnEdgeExit;
                case Column.C2: return Math.Abs(x - _targets[1]) < ColumnInnerHalfExit;
                case Column.C3: return Math.Abs(x - _targets[2]) < ColumnInnerHalfExit;
                default: return false;
            }
        }

        public bool InChannel(int y)
        {
            return Math.Abs(y - AxisCenter) <= ChannelHalfEnter;
        }

        public bool OutOfChannel(int y)
        {
            return Math.Abs(y - AxisCenter) >= ChannelHalfExit;
        }

        public ShiftDir DirectionOf(int y)
        {
            return y < AxisCenter ? ShiftDir.Fwd : ShiftDir.Back;
        }

        public bool IsEngaged(ShiftDir dir, int y)
        {
            return dir == ShiftDir.Fwd ? y <= EngageDepth : y >= AxisMax - EngageDepth;
        }

        public bool IsReleased(ShiftDir dir, int y)
        {
            return dir == ShiftDir.Fwd ? y > ReleaseDepth : y < AxisMax - ReleaseDepth;
        }

        /// <summary>
        /// How far into the slot the stick is: 0 at the channel centre, 1 at the engage
        /// threshold. Can exceed 1 at full deflection.
        /// </summary>
        public double EngageFraction(ShiftDir dir, int y)
        {
            double span = AxisCenter - EngageDepth;
            if (span <= 0) return 0;
            double travelled = dir == ShiftDir.Fwd ? AxisCenter - y : y - AxisCenter;
            return Clamp(travelled / span, 0.0, 1.2);
        }

        public bool InLockoutZone(int x)
        {
            return x >= LockoutStart;
        }

        /// <summary>
        /// Nearest of the three unprotected columns, with hysteresis so the soft neutral
        /// detent does not flip back and forth when the stick sits on a midpoint.
        /// </summary>
        public Column NearestMainColumn(int x, Column current)
        {
            Column best = Column.C1;
            int bestDist = int.MaxValue;
            for (int i = 0; i <= 2; i++)
            {
                int d = Math.Abs(x - _targets[i]);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = (Column)i;
                }
            }

            if (current == Column.None || current == Column.C4 || best == current) return best;

            // Only leave the current detent once clearly closer to the new one.
            int currentDist = Math.Abs(x - _targets[(int)current]);
            return currentDist - bestDist > DetentHysteresis ? best : current;
        }

        public static int GearOf(Column c, ShiftDir dir)
        {
            if (c == Column.None || dir == ShiftDir.None) return 0;
            return (int)c * 2 + (dir == ShiftDir.Fwd ? 1 : 2);
        }

        public static string GearLabel(int gear)
        {
            if (gear <= 0) return "N";
            if (gear >= 8) return "R";
            return gear.ToString();
        }
    }
}
