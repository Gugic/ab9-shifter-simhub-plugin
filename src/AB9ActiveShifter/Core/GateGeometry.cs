using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Pure geometry of the H gate: where the columns are, how wide the bands are, and
    /// the hysteresis pairs that keep the state machine from chattering on a boundary.
    ///
    /// The pattern decides how many columns there are and which slots hold a gear. A missing
    /// slot is expressed through the gear map - <see cref="GearFor"/> returns 0 for it - so
    /// <see cref="SlotExists"/> follows the map, and the mirror flags move the hole with the
    /// gears rather than leaving it pinned to a device corner.
    ///
    /// Axis units are raw DirectInput 0..65535 with 32767 at centre. X grows to the right,
    /// Y grows toward the player (so a gear at low Y is "forward").
    /// </summary>
    public sealed class GateGeometry
    {
        public const int AxisMin = 0;
        public const int AxisMax = 65535;
        public const int AxisCenter = 32767;

        /// <summary>Full-scale force in DirectInput units.</summary>
        public const int ForceMax = 10000;

        public GatePattern Pattern { get; private set; }

        /// <summary>How many columns this pattern has. Three for 5+R, four otherwise.</summary>
        public int ColumnCount { get; private set; }

        /// <summary>
        /// The gear number labelled R: always 8, whatever the pattern. Reverse used to be the
        /// pattern's highest gear - 8, 7 or 6 - which kept the vJoy buttons contiguous and was
        /// wrong in the way that matters: a game bound for one pattern read another pattern's
        /// reverse as a forward gear (5+R's R landed on button 6, "sixth gear", at speed).
        /// Pinning R to button 8 means one set of game bindings survives switching patterns;
        /// the unused buttons in between cost nothing.
        /// </summary>
        public int ReverseGear { get; private set; }

        /// <summary>Whether this pattern has a push-through gate before its last column.</summary>
        public bool HasLockout { get; private set; }

        private readonly int[] _targets;

        public int ChannelHalfEnter { get; private set; }
        public int ChannelHalfExit { get; private set; }
        public int ColumnEdgeEnter { get; private set; }
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

        /// <summary>
        /// The column the neutral spring pulls toward: the one holding gears 3 and 4, where a
        /// real H lever rests. Gear-column 1 in map space, so mirroring relocates it with the
        /// gears - and the middle column of a three-column gate either way.
        /// </summary>
        public Column HomeColumn
        {
            get { return (Column)(MirrorColumns ? ColumnCount - 2 : 1); }
        }

        /// <summary>Gear layout preference; see <see cref="GearFor(Column, ShiftDir)"/>.</summary>
        public bool MirrorColumns { get; private set; }

        public bool MirrorSlots { get; private set; }

        public GateGeometry(
            int channelHalfEnter,
            int channelHalfExit,
            int columnEdgeEnter,
            int columnInnerHalfEnter,
            int columnInnerHalfExit,
            int engageDepth,
            int releaseDepth,
            int lockoutHalfWidth,
            int detentHysteresis,
            bool mirrorColumns = false,
            bool mirrorSlots = false,
            GatePattern pattern = GatePattern.H7R)
        {
            Pattern = pattern;
            ColumnCount = pattern == GatePattern.H5R ? 3 : 4;
            ReverseGear = 8;

            // 5+R has no lockout by design, and Sequential has no gate at all; the geometry
            // stays well-formed either way so nothing downstream needs a null check.
            HasLockout = pattern == GatePattern.H7R || pattern == GatePattern.H6R;

            MirrorColumns = mirrorColumns;
            MirrorSlots = mirrorSlots;

            // Exit bands must be looser than enter bands or the hysteresis inverts and
            // the state machine oscillates. Clamp rather than throw: these come from
            // user-editable settings and a bad value must not kill the FFB loop.
            ChannelHalfEnter = channelHalfEnter;
            ChannelHalfExit = Math.Max(channelHalfExit, channelHalfEnter + 1);
            ColumnEdgeEnter = columnEdgeEnter;
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
        /// to the room actually available between that column's band and the reverse column's, so
        /// an extreme setting cannot swallow either. A pattern without a lockout gets no gap at
        /// all: every barrier crest is its gap's midpoint, so the watershed and handover windows
        /// sit where the geometry says they should instead of being displaced by a gate that
        /// exerts nothing.
        /// </summary>
        private void PlaceLockout(int requestedHalfWidth)
        {
            if (!HasLockout)
            {
                LockoutGapIndex = -1;
                LockoutHalfWidth = 0;
                LockoutCentre = (_targets[ColumnCount - 2] + _targets[ColumnCount - 1]) / 2;
                return;
            }

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
            if (x >= AxisMax - ColumnEdgeEnter) return (Column)(ColumnCount - 1);

            for (int i = 1; i < ColumnCount - 1; i++)
            {
                if (Math.Abs(x - _targets[i]) <= ColumnInnerHalfEnter) return (Column)i;
            }

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
            return HasLockout && Math.Abs(x - LockoutCentre) <= LockoutHalfWidth;
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
        /// How far x is from the nearest place the lateral guide can change hands, and 0 while it is
        /// inside one. The lateral field is faded out over this, so a handover always happens where
        /// the force is already zero.
        ///
        /// This is the whole fix for the gate's worst discontinuity. The guide's force saturates at
        /// its plateau and used to hold it flat right up to the boundary between two columns, so the
        /// moment the pick flipped, the force reversed: measured 2 x plateau in a single tick - up to
        /// the full +-12 Nm from a hundred counts of drift, and felt as the notches kicking while
        /// sliding along the tunnel.
        ///
        /// The window has to cover every position ANY rule can hand over at, which is why it spans
        /// the hull of both. <see cref="Pick"/> biases each boundary by <see cref="DetentHysteresis"/>
        /// toward whichever column is held, so the flip can land anywhere in a band that wide; and
        /// <see cref="GuideColumn"/> uses the barrier crest in the tunnel but the plain midpoint below
        /// it, which at the lockout gap are thousands of counts apart. Covering only the rule in force
        /// at the current depth was measured to move the same reversal onto the DEPTH axis instead -
        /// 2403 DI from one single axis count of fore/aft movement, right where the fore/aft wall's
        /// own deadband leaves the lever freest. Taking the hull makes this a function of x alone, so
        /// no amount of wander can find a step and a cold start resolves identically.
        /// </summary>
        public int HandoverClearance(int x)
        {
            int nearest = int.MaxValue;

            for (int gap = 0; gap < ColumnCount - 1; gap++)
            {
                int crest = BarrierCentre(gap);
                int mid = (_targets[gap] + _targets[gap + 1]) / 2;

                int lo = Math.Min(crest, mid) - DetentHysteresis;
                int hi = Math.Max(crest, mid) + DetentHysteresis;

                int outside = Math.Max(0, Math.Max(lo - x, x - hi));
                if (outside < nearest) nearest = outside;
            }

            return nearest == int.MaxValue ? AxisMax : nearest;
        }

        /// <summary>
        /// How far either side of a column's centre counts as "lined up with it". Matches the
        /// bands <see cref="ColumnAt"/> uses, so the forces and the state machine agree about
        /// where a column begins.
        /// </summary>
        public int ColumnFreeHalfWidth(Column c)
        {
            return IsEdgeColumn(c) ? ColumnEdgeEnter : ColumnInnerHalfEnter;
        }

        /// <summary>First and last columns sit at the ends of travel and get the edge bands.</summary>
        private bool IsEdgeColumn(Column c)
        {
            return (int)c == 0 || (int)c == ColumnCount - 1;
        }

        /// <summary>
        /// The loose band around a column - how far off centre still counts as its territory.
        /// No longer releases anything: a latched column is held until the stick comes back
        /// through the channel, so this is purely a clearance figure, and its one job is to say
        /// how much room the lockout gate has to leave beside the last main column.
        ///
        /// The column argument is kept because that is what this measures, but every value is
        /// the same one now. There used to be a wider band for the two outer columns; it could
        /// never be reached, because the only caller asks about the last main column, which is
        /// an interior one in every pattern.
        /// </summary>
        public int ColumnExitHalfWidth(Column c)
        {
            return ColumnInnerHalfExit;
        }

        /// <summary>
        /// How strongly the gate should resist fore/aft movement at this lateral position and
        /// push direction: 0 when lined up with a column whose slot holds a gear that way,
        /// rising to 1 squarely between columns. Blended over blendWidth counts so the wall
        /// arrives smoothly rather than snapping on at a band edge.
        ///
        /// A column with no gear in the push direction never opens: its factor is 1 however
        /// well lined up the lever is, which is the entire rendering of a missing slot - the
        /// divider simply continues across where the mouth would have been. Keying on direction
        /// is safe because the fore/aft force crosses zero at the channel centre, so the switch
        /// between the two directions' factors happens where there is no force to step.
        /// </summary>
        public double ChannelBlockFactor(int x, int blendWidth, ShiftDir dir)
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

            if (!SlotExists(nearest, dir)) return 1.0;

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
            return Pick(x, current, false);
        }

        private Column Pick(int x, Column current, bool byMidpoint)
        {
            Column plain = ColumnPastCrests(x, 0, byMidpoint);
            if (current == Column.None || plain == current) return plain;

            // Bias every boundary toward whichever column we are already parked on, so leaving it
            // costs the hysteresis distance in whichever direction we are travelling.
            int bias = (int)plain > (int)current ? DetentHysteresis : -DetentHysteresis;
            return ColumnPastCrests(x, bias, byMidpoint);
        }

        /// <summary>
        /// Which column the lateral guide belongs to. In the tunnel the boundaries are the barrier
        /// crests, so fighting through the lockout gate hands the lever to 7/R rather than letting
        /// it be dragged back. Below the tunnel they are the plain midpoints instead: down there
        /// the lever simply belongs to the column it is physically nearest.
        ///
        /// That distinction closes a lockout bypass, and it has to be positional rather than
        /// historical to close it properly. The gate sits well off its gap's midpoint, so with
        /// crest boundaries a lever at gear depth just past the gate is "in 7/R's territory" and
        /// the guide pushes it that way at full pin force for thousands of counts - the wall that
        /// was holding it in 5/6 reverses into a conveyor toward 7, and the toll is never paid.
        /// Pull out of 5, drag right at depth, drop into 7: no lockout at all. Using midpoints
        /// below the tunnel means that lever keeps 5/6's inward wall the whole way, exactly as it
        /// did before the lateral field was unified, and a cold start at that position resolves
        /// the same way as a lever that was dragged there.
        /// </summary>
        public Column GuideColumn(int x, Column current, bool inTunnel)
        {
            return Pick(x, current, !inTunnel);
        }

        private Column ColumnPastCrests(int x, int bias, bool byMidpoint)
        {
            Column c = Column.C1;
            for (int i = 0; i < ColumnCount - 1; i++)
            {
                int boundary = byMidpoint ? (_targets[i] + _targets[i + 1]) / 2 : BarrierCentre(i);
                if (x > boundary + bias) c = (Column)(i + 1);
            }
            return c;
        }


        /// <summary>
        /// Which way the next sequential gear lies from this slot, as -1, 0 or +1 in device x.
        ///
        /// Derived from the gear map rather than assumed. Gear-column m holds the odd gear 2m+1 on
        /// its forward side and the even gear 2m+2 on its back side, so from an even gear the next
        /// gear is in gear-column m+1, and from an odd gear the previous gear is in gear-column
        /// m-1; every other transition stays inside one column. Each slot therefore has at most one
        /// cross-column sequential neighbour, which is why one signed value describes it completely.
        /// In plain terms it is the classic H zig-zag: leaving a back slot goes one way, leaving a
        /// forward slot goes the other.
        ///
        /// Both mirror flags are handled where they act. MirrorSlots changes which device direction
        /// is the even gear, so it inverts the test. MirrorColumns maps gear-column m to device
        /// column ColumnCount-1-m, so the next gear-column becomes the previous device column.
        ///
        /// Returns 0 where there is no neighbour, and deliberately 0 across the lockout gap: the
        /// toll is paid in the tunnel and a real range gate does not help you across itself either.
        /// The gap is asked of <see cref="LockoutGapIndex"/> rather than assumed, because mirroring
        /// moves it to the other end of the gate.
        /// </summary>
        public int SequentialBias(Column c, ShiftDir dir)
        {
            if (c == Column.None || dir == ShiftDir.None) return 0;

            bool gearBack = MirrorSlots ? dir == ShiftDir.Fwd : dir == ShiftDir.Back;
            int step = gearBack ? 1 : -1;
            int deviceStep = MirrorColumns ? -step : step;

            int target = (int)c + deviceStep;
            if (target < 0 || target >= ColumnCount) return 0;
            if (Math.Min((int)c, target) == LockoutGapIndex) return 0;

            return deviceStep;
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
        /// How far the tunnel has been left behind: 0 anywhere inside the channel's enter band, 1 by
        /// its exit band, and the transition confined to the hysteresis band between them.
        ///
        /// Both ends of that matter and both were learned by getting them wrong. Ending later than
        /// the exit band gives the slot walls a depth term, and the guides leading to each gear ring.
        /// Starting earlier than the enter band gives the TUNNEL one, and since this factor also
        /// fades the barriers, the lockout's own force then swings by thousands of units as the lever
        /// wanders fore and aft while sliding past - felt, accurately, as being pushed and pulled in
        /// random directions.
        ///
        /// A lever at gear depth is inside a slot whether or not the state machine has a column
        /// latched, so lateral confinement has to be a fact about depth rather than about the
        /// latch. When it depended on the latch, overpowering one slot wall dropped the latch,
        /// which swapped in the neutral force field, which had no lateral wall at depth at all -
        /// so the gate gave way completely and the lever could be dragged along the top or bottom
        /// of the pattern from gear to gear, helped on its way by the guide adopting each column
        /// as it passed the halfway line.
        /// </summary>
        public double SlotConfinementFactor(int y)
        {
            int depth = Math.Abs(y - AxisCenter);
            int span = Math.Max(1, ChannelHalfExit - ChannelHalfEnter);
            return Clamp((depth - ChannelHalfEnter) / (double)span, 0.0, 1.0);
        }

        /// <summary>
        /// Gear number for a gate position, honouring the layout preference. Mirroring is applied
        /// here, to the labels, rather than to the axis readings - the readings have to stay in the
        /// device's own coordinates because spring anchors are sent back to it in those same
        /// coordinates, and mirroring those would turn the gate springs into repellers.
        ///
        /// Returns 0 for a slot that holds no gear in this pattern. That single fact is what a
        /// "missing slot" IS: 6+R is the four-column map with the slot that would hold 7 mapped
        /// to nothing. Because the hole lives in the map, the mirror flags relocate it along
        /// with every other gear.
        ///
        /// Reverse is the last gear-column's back slot and is always gear 8 - see
        /// <see cref="ReverseGear"/> for why the buttons are deliberately not contiguous.
        /// </summary>
        public int GearFor(Column c, ShiftDir dir)
        {
            if (c == Column.None || dir == ShiftDir.None || (int)c >= ColumnCount) return 0;

            int column = MirrorColumns ? (ColumnCount - 1 - (int)c) : (int)c;
            bool forward = MirrorSlots ? dir == ShiftDir.Back : dir == ShiftDir.Fwd;

            if (column == ColumnCount - 1 && !forward) return ReverseGear;

            int raw = column * 2 + (forward ? 1 : 2);
            if (Pattern == GatePattern.H6R && raw == 7) return 0;

            return raw;
        }

        /// <summary>Whether this slot holds a gear. A missing slot's mouth never opens.</summary>
        public bool SlotExists(Column c, ShiftDir dir)
        {
            return GearFor(c, dir) > 0;
        }

        public string LabelFor(int gear)
        {
            if (gear <= 0) return "N";
            return gear == ReverseGear ? "R" : gear.ToString();
        }
    }
}
