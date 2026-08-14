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

        /// <summary>
        /// The narrowest the neutral tunnel's enter/exit band is allowed to be after repair. Not
        /// a user dial and not a feel constant: it is the width below which a band that also
        /// serves as a force ramp stops being a ramp and becomes a step the hand meets as a bang.
        /// At full scale this bounds the gradient at 10 DI per axis count; the shipped gate runs
        /// at 3.8. Bands that are only ever state hysteresis do not need it - see the constructor.
        /// </summary>
        public const int MinBandSpan = 1000;

        /// <summary>Full-scale force in DirectInput units.</summary>
        public const int ForceMax = 10000;

        public GatePattern Pattern { get; private set; }

        /// <summary>How many columns this pattern has. Three for 5+R and the truck 6, four otherwise.</summary>
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

        /// <summary>
        /// Whether a lockout gate guards one of this gate's gaps. No longer a fact of the
        /// pattern alone: the placement dial decides, and its default hands each pattern what
        /// it has always shipped with. False for a Slot placement - a slot lockout is a
        /// fore/aft force at one mouth, and every barrier crest stays at its gap's midpoint.
        /// </summary>
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
        /// outside the band of the approach-side column - the one the paying crossing comes
        /// from - so sliding across the gate finds the gate immediately rather than after a
        /// long stretch of dead travel. That dead travel was a usability trap - the hand stops
        /// where the gate stops it, assumes it has arrived at a column, and finds that pushing
        /// fore or aft neither engages a gear nor explains why. A Both-direction gate has no
        /// single approach side and sits on the midpoint instead: anchoring it to either column
        /// would leave the other direction's return crossing a free path through the dead-travel
        /// strip, because ownership hands over at the midpoint whatever the gate does.
        /// </summary>
        public int LockoutCentre { get; private set; }

        /// <summary>Which device gap the lockout guards. Map-relative placement, so mirroring moves it with the gears.</summary>
        public int LockoutGapIndex { get; private set; }

        /// <summary>The placement as configured, before any repair.</summary>
        public LockoutPlacement RequestedLockoutPlacement { get; private set; }

        /// <summary>
        /// The placement actually built: PatternDefault resolved to a real gap or Off, an
        /// impossible gap clamped to the last one, a slot the pattern does not hold turned Off.
        /// </summary>
        public LockoutPlacement EffectiveLockoutPlacement { get; private set; }

        /// <summary>True when the effective placement is not the requested one, so the UI can say so.</summary>
        public bool LockoutPlacementRepaired { get; private set; }

        /// <summary>Which crossing of the guarded gap pays. Meaningful only when <see cref="HasLockout"/>.</summary>
        public LockoutGapDirection LockoutDirection { get; private set; }

        /// <summary>
        /// Device-x sign of the blocked crossing: +1 when the toll is paid moving toward +X,
        /// -1 toward -X, 0 for a Both gate (the composer's latch decides per crossing).
        /// </summary>
        public int LockoutBlockSign { get; private set; }

        /// <summary>Whether the lockout guards a single slot's mouth instead of a gap.</summary>
        public bool LockoutIsSlot { get; private set; }

        /// <summary>The guarded slot's column, or None. Resolved through the gear map, so mirroring moves it.</summary>
        public Column LockoutSlotColumn { get; private set; }

        /// <summary>The guarded slot's direction, or None.</summary>
        public ShiftDir LockoutSlotDir { get; private set; }

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
            GatePattern pattern = GatePattern.H7R,
            LockoutPlacement lockoutPlacement = LockoutPlacement.PatternDefault,
            LockoutGapDirection lockoutDirection = LockoutGapDirection.TowardHigh,
            int lockoutSlotGear = 8)
        {
            Pattern = pattern;
            ColumnCount = pattern == GatePattern.H5R || pattern == GatePattern.H6 ? 3 : 4;
            ReverseGear = 8;

            MirrorColumns = mirrorColumns;
            MirrorSlots = mirrorSlots;

            // Exit bands must be looser than enter bands or the hysteresis inverts and
            // the state machine oscillates. Clamp rather than throw: these come from
            // user-editable settings and a bad value must not kill the FFB loop.
            //
            // The tunnel pair gets a wider floor than the rest, because it is the only one that
            // does double duty: GuidePlateau and SlotConfinementFactor ramp the force ACROSS it.
            // Ordering alone is enough for a state band, and enter + 1 delivers that - but as a
            // ramp span, one axis count in 65535 is not a narrow ramp, it is a cliff. Measured on
            // this rig from a trace: with the pair typed the wrong way round (enter 7200, exit
            // 5200) the repair produced a span of 1, the plateau read 0 DI at depth 7200 and full
            // scale at 7201, and one count of sensor dither commanded a 12 Nm reversal at the
            // report rate for as long as the lever sat there.
            //
            // The floor is narrower than the shipped tunnel gap (2600), so it repairs a broken
            // configuration and touches nothing anyone has tuned. The other two pairs keep the
            // ordering-only clamp: they are hysteresis, no force ramps across them, and the
            // shipped Sequential profile deliberately runs a 500-count release gap.
            ChannelHalfEnter = channelHalfEnter;
            ChannelHalfExit = Math.Max(channelHalfExit, channelHalfEnter + MinBandSpan);
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

            PlaceLockout(lockoutHalfWidth, lockoutPlacement, lockoutDirection, lockoutSlotGear);
        }

        /// <summary>
        /// Resolves the placement dial into an actual gate, and positions it. PatternDefault
        /// hands each pattern what it always shipped with - 7+R and 6+R guard their last gap,
        /// the rest have none - which is what keeps a configuration that predates the dial
        /// behaving exactly as it always did. An impossible request is repaired, never obeyed
        /// blindly and never thrown on: a gap the pattern does not have clamps to its last gap
        /// (the user asked for a lockout; silently having none is the bigger surprise), and a
        /// slot gear the pattern does not hold turns the lockout off (there is no nearest
        /// sensible gear). Repairs are reported through <see cref="LockoutPlacementRepaired"/>
        /// so the UI can say so instead of the geometry lying quietly.
        ///
        /// A gap gate is positioned against the approach-side column - the one the paying
        /// crossing comes from - and its width is clamped to the room actually available so an
        /// extreme setting cannot swallow either column's band. A pattern without a lockout
        /// gets no gap at all: every barrier crest is its gap's midpoint, so the watershed and
        /// handover windows sit where the geometry says they should instead of being displaced
        /// by a gate that exerts nothing.
        /// </summary>
        private void PlaceLockout(int requestedHalfWidth, LockoutPlacement placement,
            LockoutGapDirection direction, int slotGear)
        {
            RequestedLockoutPlacement = placement;
            LockoutDirection = direction;
            LockoutSlotColumn = Column.None;
            LockoutSlotDir = ShiftDir.None;

            LockoutPlacement resolved = placement;
            if (placement == LockoutPlacement.PatternDefault)
            {
                // The position each pattern has always had; the direction dial still applies,
                // and its own default is the one-way gate 7+R has always been.
                resolved = Pattern == GatePattern.H7R || Pattern == GatePattern.H6R
                    ? LockoutPlacement.Gap1 + (ColumnCount - 2)
                    : LockoutPlacement.Off;
            }

            if (resolved == LockoutPlacement.Slot)
            {
                Column slotColumn;
                ShiftDir slotDir;
                if (TryFindSlot(slotGear, out slotColumn, out slotDir))
                {
                    LockoutIsSlot = true;
                    LockoutSlotColumn = slotColumn;
                    LockoutSlotDir = slotDir;
                }
                else
                {
                    // No such gear in this pattern - gear 7 on 6+R, or R on the truck gate.
                    resolved = LockoutPlacement.Off;
                    LockoutPlacementRepaired = true;
                }
            }

            int mapGap = -1;
            if (resolved >= LockoutPlacement.Gap1 && resolved <= LockoutPlacement.Gap3)
            {
                mapGap = resolved - LockoutPlacement.Gap1;
                if (mapGap > ColumnCount - 2)
                {
                    mapGap = ColumnCount - 2;
                    LockoutPlacementRepaired = true;
                }
                resolved = LockoutPlacement.Gap1 + mapGap;
            }

            EffectiveLockoutPlacement = resolved;
            HasLockout = mapGap >= 0;

            if (!HasLockout)
            {
                LockoutGapIndex = -1;
                LockoutHalfWidth = 0;
                LockoutBlockSign = 0;
                LockoutCentre = (_targets[ColumnCount - 2] + _targets[ColumnCount - 1]) / 2;
                return;
            }

            // Placement is stated in map gaps so mirroring relocates the gate with the gears -
            // the same rule that moves 6+R's missing slot. The traditional last-gap gate falls
            // out of this as the mapGap = ColumnCount-2 case.
            LockoutGapIndex = MirrorColumns ? ColumnCount - 2 - mapGap : mapGap;

            // The approach column is the one the paying crossing comes from: the lower-gear
            // column of the gap for TowardHigh (today's "main"), the higher-gear column for
            // TowardLow. Both has no single approach side and is handled below.
            int lowMapColumn = mapGap;
            int highMapColumn = mapGap + 1;
            Column approach = (Column)DeviceColumn(direction == LockoutGapDirection.TowardLow
                ? highMapColumn : lowMapColumn);
            Column other = (Column)DeviceColumn(direction == LockoutGapDirection.TowardLow
                ? lowMapColumn : highMapColumn);
            int sign = _targets[(int)other] > _targets[(int)approach] ? 1 : -1;
            LockoutBlockSign = direction == LockoutGapDirection.Both ? 0 : sign;

            int midpoint = (_targets[(int)approach] + _targets[(int)other]) / 2;

            if (direction == LockoutGapDirection.Both)
            {
                // Centred on the midpoint, the one place both crossings pay symmetrically:
                // ownership (ColumnAt) hands over there whatever the gate does, so a band
                // anchored to either column would leave the other direction's approach a free
                // strip that ends in a selectable column. Width clamped to clear both columns'
                // bands, each with the wider of its exit and free half-widths.
                int roomLow = midpoint - _targets[Math.Min((int)approach, (int)other)]
                              - AnchorClearance((Column)Math.Min((int)approach, (int)other));
                int roomHigh = _targets[Math.Max((int)approach, (int)other)]
                               - AnchorClearance((Column)Math.Max((int)approach, (int)other)) - midpoint;

                LockoutHalfWidth = Clamp(requestedHalfWidth, 200,
                    Math.Max(200, Math.Min(roomLow, roomHigh)));
                LockoutCentre = midpoint;
                return;
            }

            // One-way: the band starts exactly where the approach column's band ends, so the
            // toll is met immediately with no dead travel. The clearance takes the wider of the
            // column's exit and free half-widths: for an interior column those agree (the exit
            // band is repaired to be the looser), but Gap1's approach is an edge column whose
            // free band is wider than the exit dial, and starting the toll inside a column's
            // own lined-up band is the "hard bump where the hand rests" failure the
            // inside-the-band faces already fixed once.
            int clearance = AnchorClearance(approach);
            int room = Math.Abs(_targets[(int)other] - _targets[(int)approach])
                       - clearance - ColumnFreeHalfWidth(other);

            LockoutHalfWidth = Clamp(requestedHalfWidth, 200, Math.Max(200, room / 2));
            LockoutCentre = _targets[(int)approach] + (sign * (clearance + LockoutHalfWidth));

            // The crest must stay in the approach side's half of the gap. Ownership hands the
            // guarded column over at the gap's midpoint (see ColumnAt), so a crest sitting past
            // that midpoint would leave positions that belong to the guarded column but are
            // short of the gate - and a push out of the tunnel there would select it without
            // the toll ever being paid. It cannot happen at the shipped bands, only if the
            // clearance dial is driven wider than the far column's own free width, which is
            // exactly the kind of setting a repair exists for.
            LockoutCentre = sign > 0
                ? Math.Min(LockoutCentre, midpoint - 1)
                : Math.Max(LockoutCentre, midpoint + 1);
        }

        /// <summary>Map column index to device column index, honouring the mirror flag.</summary>
        private int DeviceColumn(int mapColumn)
        {
            return MirrorColumns ? ColumnCount - 1 - mapColumn : mapColumn;
        }

        /// <summary>The room the lockout leaves beside a column: the wider of its exit and free bands.</summary>
        private int AnchorClearance(Column c)
        {
            return Math.Max(ColumnExitHalfWidth(c), ColumnFreeHalfWidth(c));
        }

        /// <summary>
        /// Finds the slot holding a gear by inverting the gear map, so a slot lockout named by
        /// gear number follows the mirror flags exactly as the gear does. False when no slot in
        /// this pattern holds that gear. Public because the UI's slot picker asks the same
        /// question when labelling what a choice would actually guard.
        /// </summary>
        public bool TryFindSlot(int gear, out Column column, out ShiftDir dir)
        {
            for (int c = 0; c < ColumnCount; c++)
            {
                for (int d = 1; d <= 2; d++)
                {
                    if (GearFor((Column)c, (ShiftDir)d) == gear)
                    {
                        column = (Column)c;
                        dir = (ShiftDir)d;
                        return true;
                    }
                }
            }

            column = Column.None;
            dir = ShiftDir.None;
            return false;
        }

        public int ColumnTarget(Column c)
        {
            return c == Column.None ? AxisCenter : _targets[(int)c];
        }

        /// <summary>Converts a raw axis reading to the DirectInput Â±10000 force/position scale.</summary>
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

        /// <summary>
        /// Which column owns x: the one it is physically nearest, boundaries at the midpoints
        /// between columns. Never None - every position in the gate belongs to some column.
        ///
        /// This is what a push out of the tunnel selects, and it is deliberately the same
        /// ownership <see cref="ChannelBlockFactor"/> already uses to decide where the fore/aft
        /// wall opens, and the same one <see cref="GuideColumn"/> falls back to. One rule, so the
        /// force and the state machine cannot disagree about whose slot the lever is heading for.
        ///
        /// It used to be a tight band - <see cref="ColumnInnerHalfEnter"/> either side of the
        /// target - and outside it a push selected nothing at all. That is a narrower promise than
        /// the force can keep. The fore/aft wall opens over the free width and then blends shut
        /// across WallBlend counts, so between the two there is an annulus where the wall is
        /// passable and no gear existed to be selected; and past that the wall is only 12 Nm,
        /// which a hand simply beats. Measured on the rig: two pushes to FULL deflection, 896 ms
        /// and 616 ms, roughly 2400 counts off the column, state still Neutral, no gear - the
        /// lever shoved home and the game told nothing. A silent non-shift is the worst answer
        /// available here; selecting the column the hand was plainly reaching for is the least bad.
        ///
        /// Which slot, if any, that column holds is still a fact of the gear map - see
        /// <see cref="SlotExists"/>. Ownership says whose territory this is, not that there is
        /// something in it.
        /// </summary>
        public Column ColumnAt(int x)
        {
            return ColumnPastCrests(x, 0, true);
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
        /// The window has to cover every position the guide can hand over at, and that is now one
        /// rule rather than two: <see cref="GuideColumn"/> only ever changes hands in the tunnel,
        /// where the boundaries are the barrier crests. <see cref="Pick"/> biases each crest by
        /// <see cref="DetentHysteresis"/> toward whichever column is held, so the flip can land
        /// anywhere in a band that wide, and that band is the whole window.
        ///
        /// It used to span the hull of the crest AND the plain midpoint, because the pick was live
        /// below the tunnel too and used midpoints down there. At the lockout gap those are
        /// thousands of counts apart, so the window swallowed the gate's whole doorway - and since
        /// the fade applied at every depth, a gear held at the bottom of the pattern lost its
        /// lateral wall entirely across that span. Freezing the pick outside the tunnel retires
        /// the second rule, and the window shrinks to what one rule actually needs.
        ///
        /// Still a function of x alone, so no amount of wander can find a step in it; the fade
        /// that consumes it is what carries the depth term, and only in the direction of giving
        /// the wall BACK. See ForceComposer.Relief.
        /// </summary>
        public int HandoverClearance(int x)
        {
            int nearest = int.MaxValue;

            for (int gap = 0; gap < ColumnCount - 1; gap++)
            {
                int crest = BarrierCentre(gap);

                int outside = Math.Max(0, Math.Max(crest - DetentHysteresis - x,
                                                   x - (crest + DetentHysteresis)));
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
        /// Which column the lateral guide belongs to. The boundaries are the barrier crests, so
        /// fighting through the lockout gate hands the lever to 7/R rather than letting it be
        /// dragged back into the gate it has just paid for.
        ///
        /// It changes hands ONLY in the tunnel. Out of the tunnel the answer is simply the one
        /// already held, which is what makes the whole lateral field safe: a handover is the only
        /// discontinuity this field has, and confining it to the tunnel confines it to the depths
        /// where <see cref="ForceComposer"/>'s plateau is the light detent rather than the full
        /// slot wall. That is what lets the relief window - the fade that pays for the handover -
        /// be faded out with depth, and that fade is what stops a latched gear's wall being holed
        /// at every gap (measured: a gear held at full deflection had 0 DI of lateral wall at
        /// three separate places, and a hand parks in every one of them).
        ///
        /// It also closes a lockout bypass that a live pick at depth used to leave open. The gate
        /// sits well off its gap's midpoint, so a lever at gear depth just past the gate reads as
        /// "in 7/R's territory": the wall that was holding it in 5/6 reversed into a conveyor
        /// toward 7 and the toll was never paid. Pull out of 5, drag right at depth, drop into 7.
        /// A frozen pick cannot do that - the lever keeps whichever column it left the tunnel
        /// with, and it can only have left the tunnel where the tunnel's own crests put it.
        ///
        /// The fallback for a pick that does not exist yet - a cold start, or the first tick after
        /// the forces are released - is the plain nearest column, which is the same ownership rule
        /// <see cref="ColumnAt"/> captures by, so a lever that starts life below the tunnel is
        /// pushed toward the very column that is about to claim it.
        /// </summary>
        public Column GuideColumn(int x, Column current, bool inTunnel)
        {
            if (!inTunnel) return current == Column.None ? ColumnAt(x) : current;
            return Pick(x, current, false);
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
        /// <see cref="ReverseGear"/> for why the buttons are deliberately not contiguous. The
        /// truck 6 is the exception with no reverse at all, so it never produces gear 8.
        /// </summary>
        public int GearFor(Column c, ShiftDir dir)
        {
            if (c == Column.None || dir == ShiftDir.None || (int)c >= ColumnCount) return 0;

            int column = MirrorColumns ? (ColumnCount - 1 - (int)c) : (int)c;
            bool forward = MirrorSlots ? dir == ShiftDir.Back : dir == ShiftDir.Fwd;

            // The truck gate has no reverse anywhere: its last back slot is simply gear 6, so
            // the raw formula runs through and the buttons come out contiguous at 1..6.
            if (Pattern != GatePattern.H6 && column == ColumnCount - 1 && !forward) return ReverseGear;

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

