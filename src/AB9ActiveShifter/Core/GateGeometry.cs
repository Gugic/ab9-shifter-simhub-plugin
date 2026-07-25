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
        public int DetentHysteresis { get; private set; }

        /// <summary>Half-width of the lockout gate's band, clamped to fit the gap it guards.</summary>
        public int LockoutHalfWidth { get; private set; }

        /// <summary>
        /// Where the lockout gate sits. Not the midpoint of the gap: the gate is placed just
        /// outside the band of the last main-section column, so sliding across the gate finds
        /// the gate immediately rather than after a long stretch of dead travel. That dead
        /// travel was a usability trap - the hand stops where the gate stops it, assumes it has
        /// arrived at a column, and finds that pushing fore or aft neither engages a gear nor
        /// explains why.
        /// </summary>
        public int LockoutCentre { get; private set; }

        /// <summary>Which gap the lockout guards. Mirroring moves 7/R to the other end.</summary>
        public int LockoutGapIndex { get; private set; }

        /// <summary>Distance between adjacent columns.</summary>
        public int ColumnSpacing { get { return AxisMax / (ColumnCount - 1); } }

        /// <summary>Gear layout preference; see <see cref="GearOf(Column, ShiftDir)"/>.</summary>
        public bool MirrorColumns { get; private set; }

        public bool MirrorSlots { get; private set; }

        public GateGeometry(
            int channelHalfEnter,
            int channelHalfExit,
            int columnEdgeEnter,
            int columnEdgeExit,
            int columnInnerHalfEnter,
            int columnInnerHalfExit,
            int engageDepth,
            int releaseDepth,
            int lockoutHalfWidth,
            int detentHysteresis,
            bool mirrorColumns = false,
            bool mirrorSlots = false)
        {
            MirrorColumns = mirrorColumns;
            MirrorSlots = mirrorSlots;

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
            DetentHysteresis = detentHysteresis;

            _targets = new int[ColumnCount];
            for (int i = 0; i < ColumnCount; i++)
            {
                _targets[i] = (int)Math.Round(i * (double)AxisMax / (ColumnCount - 1));
            }

            PlaceLockout(lockoutHalfWidth);
        }

        /// <summary>
        /// Positions the lockout gate against the last main-section column, and clamps its width
        /// to the room actually available between that column's band and the 7/R column's, so an
        /// extreme setting cannot swallow either.
        /// </summary>
        private void PlaceLockout(int requestedHalfWidth)
        {
            LockoutGapIndex = MirrorColumns ? 0 : ColumnCount - 2;

            Column main = (Column)(MirrorColumns ? 1 : ColumnCount - 2);
            Column locked = (Column)(MirrorColumns ? 0 : ColumnCount - 1);
            int sign = MirrorColumns ? -1 : 1;

            int clearance = ColumnExitHalfWidth(main);
            int room = Math.Abs(_targets[(int)locked] - _targets[(int)main])
                       - clearance - ColumnFreeHalfWidth(locked);

            LockoutHalfWidth = Clamp(requestedHalfWidth, 200, Math.Max(200, room / 2));
            LockoutCentre = _targets[(int)main] + (sign * (clearance + LockoutHalfWidth));
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

        /// <summary>Whether x is inside the lockout gate's band, where its force acts.</summary>
        public bool InLockoutGate(int x)
        {
            return Math.Abs(x - LockoutCentre) <= LockoutHalfWidth;
        }

        /// <summary>
        /// Where the barrier between two adjacent columns sits. Ordinary barriers are the
        /// midpoint between their columns; the one guarding 7/R is the lockout gate, which is
        /// placed against the main section instead - see <see cref="LockoutCentre"/>.
        /// </summary>
        public int BarrierCentre(int index)
        {
            int i = Clamp(index, 0, ColumnCount - 2);
            if (i == LockoutGapIndex) return LockoutCentre;
            return (_targets[i] + _targets[i + 1]) / 2;
        }

        /// <summary>
        /// How far either side of a column's centre counts as "lined up with it". Matches the
        /// bands <see cref="ColumnAt"/> uses, so the forces and the state machine agree about
        /// where a column begins.
        /// </summary>
        public int ColumnFreeHalfWidth(Column c)
        {
            return (c == Column.C1 || c == Column.C4) ? ColumnEdgeEnter : ColumnInnerHalfEnter;
        }

        /// <summary>
        /// The loose band around a column - how far off centre still counts as its territory.
        /// No longer releases anything: a latched column is held until the stick comes back
        /// through the channel, so this is a clearance figure, and what the lockout gate is
        /// positioned against.
        /// </summary>
        public int ColumnExitHalfWidth(Column c)
        {
            return (c == Column.C1 || c == Column.C4) ? ColumnEdgeExit : ColumnInnerHalfExit;
        }

        /// <summary>
        /// How strongly the gate should resist fore/aft movement at this lateral position:
        /// 0 when lined up with a column, where a gear can be taken, rising to 1 squarely
        /// between columns, where the stick must stay in the neutral channel. Blended over
        /// blendWidth counts so the wall arrives smoothly rather than snapping on at a band
        /// edge.
        /// </summary>
        public double ChannelBlockFactor(int x, int blendWidth)
        {
            Column nearest = Column.C1;
            int bestDist = int.MaxValue;

            for (int i = 0; i < ColumnCount; i++)
            {
                int d = Math.Abs(x - _targets[i]);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = (Column)i;
                }
            }

            int free = ColumnFreeHalfWidth(nearest);
            if (bestDist <= free) return 0.0;

            return Clamp((bestDist - free) / (double)Math.Max(1, blendWidth), 0.0, 1.0);
        }

        /// <summary>
        /// Which column the lateral guide should pull toward, with hysteresis so it does not flip
        /// back and forth when the stick sits on a boundary.
        ///
        /// The boundaries are the barrier crests, not the geometric midpoints between columns.
        /// For ordinary gaps those are the same thing, but the lockout gate sits well off its
        /// gap's midpoint, and a midpoint boundary would leave the stick pulled back toward the
        /// main section for thousands of counts after it had already fought its way through the
        /// gate - dragging it straight back in. Crossing a crest is what hands the stick over.
        /// </summary>
        public Column NearestColumn(int x, Column current)
        {
            Column plain = ColumnPastCrests(x, 0);
            if (current == Column.None || plain == current) return plain;

            // Bias every crest toward whichever column we are already parked on, so leaving it
            // costs the hysteresis distance in whichever direction we are travelling.
            int bias = (int)plain > (int)current ? DetentHysteresis : -DetentHysteresis;
            return ColumnPastCrests(x, bias);
        }

        private Column ColumnPastCrests(int x, int bias)
        {
            Column c = Column.C1;
            for (int i = 0; i < ColumnCount - 1; i++)
            {
                if (x > BarrierCentre(i) + bias) c = (Column)(i + 1);
            }
            return c;
        }


        /// <summary>
        /// How much of the way out of the neutral channel the stick is, 0 inside the channel and
        /// 1 once clear of it. Scales the lateral guide, so entering a gear steers toward the
        /// column rather than merely being blocked by the gate wall.
        /// </summary>
        public double FunnelDepthFactor(int y)
        {
            int depth = Math.Abs(y - AxisCenter);
            int span = Math.Max(1, ChannelHalfExit - ChannelHalfEnter);
            return Clamp((depth - ChannelHalfEnter) / (double)span, 0.0, 1.0);
        }

        /// <summary>
        /// How much a slot's own walls apply at this depth: 0 inside the neutral channel, rising
        /// to 1 over the wall's bite once clear of it.
        ///
        /// A lever at gear depth is inside a slot whether or not the state machine has a column
        /// latched, so lateral confinement has to be a fact about depth rather than about the
        /// latch. When it depended on the latch, overpowering one slot wall dropped the latch,
        /// which swapped in the neutral force field, which had no lateral wall at depth at all -
        /// so the gate gave way completely and the lever could be dragged along the top or bottom
        /// of the pattern from gear to gear, helped on its way by the guide adopting each column
        /// as it passed the halfway line.
        /// </summary>
        public double SlotConfinementFactor(int y, int biteDistance)
        {
            int depth = Math.Abs(y - AxisCenter);
            int span = Math.Max(1, biteDistance);
            return Clamp((depth - ChannelHalfExit) / (double)span, 0.0, 1.0);
        }

        /// <summary>
        /// Gear number for a gate position, honouring the layout preference. Mirroring is applied
        /// here, to the labels, rather than to the axis readings - the readings have to stay in the
        /// device's own coordinates because spring anchors are sent back to it in those same
        /// coordinates, and mirroring those would turn the gate springs into repellers.
        /// </summary>
        public static int GearOf(Column c, ShiftDir dir, bool mirrorColumns = false, bool mirrorSlots = false)
        {
            if (c == Column.None || dir == ShiftDir.None) return 0;

            int column = mirrorColumns ? (ColumnCount - 1 - (int)c) : (int)c;
            bool forward = mirrorSlots ? dir == ShiftDir.Back : dir == ShiftDir.Fwd;

            return column * 2 + (forward ? 1 : 2);
        }

        public int GearFor(Column c, ShiftDir dir)
        {
            return GearOf(c, dir, MirrorColumns, MirrorSlots);
        }

        public static string GearLabel(int gear)
        {
            if (gear <= 0) return "N";
            if (gear >= 8) return "R";
            return gear.ToString();
        }
    }
}
