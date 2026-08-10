using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// The geometry of an automatic's selector: one lane along the fore/aft axis with four fixed
    /// positions in it, evenly spaced, and a button held at whichever one the lever is in.
    ///
    /// It is deliberately not an H gate with a single column. An H gate has a neutral to come back
    /// through, a gear to engage and a state that can be "nowhere"; a PRND lever is always in
    /// exactly one position and moves between them without passing through anything. Sharing
    /// <see cref="GateGeometry"/> would have meant a column count of one, a channel that means
    /// nothing, a lockout that cannot exist and a gear map with no reverse in it - four
    /// special cases in the middle of the gate, to save writing forty lines here.
    ///
    /// Positions are indexed 0..3 by axis reading, low y first - so index 0 is the end of the lane
    /// furthest from the player, where a real console selector puts P.
    /// <see cref="GateGeometry.MirrorSlots"/> flips the labels along the lane for a rig mounted the
    /// other way round, and the buttons follow the LABEL rather than the slot: R is button 12
    /// whichever end of the lane it is at, exactly as reverse is button 8 in every H pattern, so a
    /// layout preference never costs a rebind.
    /// </summary>
    public sealed class PrndLane
    {
        public const int PositionCount = 4;

        /// <summary>
        /// P, R, N and D take buttons 11, 12, 13 and 14. Above the gear range (1-8) and above the
        /// sequential up/down pulses (9-10), so no binding a game already carries can mean two
        /// things - the same rule that moved the sequential pulses off buttons 1 and 2, where an
        /// upshift read as "engage 1st" to anything still bound for an H pattern.
        /// </summary>
        public const int FirstButton = 11;

        private static readonly string[] Labels = { "P", "R", "N", "D" };

        private readonly int[] _positions = new int[PositionCount];
        private readonly int _hysteresis;
        private readonly bool _mirrored;

        /// <summary>
        /// <paramref name="halfLength"/> is the distance from centre to each end of the lane, so
        /// the outermost positions sit there and the other two divide the rest evenly. It is the
        /// throw, the same stored fact every other pattern measures its stroke with.
        /// </summary>
        public PrndLane(int halfLength, int hysteresis, bool mirrored)
        {
            int half = GateGeometry.Clamp(halfLength, PositionCount, GateGeometry.AxisCenter);

            Spacing = Math.Max(1, (2 * half) / (PositionCount - 1));
            for (int i = 0; i < PositionCount; i++)
            {
                _positions[i] = GateGeometry.AxisCenter - half + (i * Spacing);
            }

            HalfLength = half;
            _hysteresis = Math.Max(0, hysteresis);
            _mirrored = mirrored;
        }

        /// <summary>Distance from centre to each end of the lane.</summary>
        public int HalfLength { get; private set; }

        /// <summary>Axis counts between adjacent positions.</summary>
        public int Spacing { get; private set; }

        /// <summary>Axis reading of one position, low y first.</summary>
        public int PositionY(int index)
        {
            return _positions[GateGeometry.Clamp(index, 0, PositionCount - 1)];
        }

        /// <summary>
        /// Where one position gives way to the next: the midpoint between them. Nothing is decided
        /// at a crest except which position is held - the force is already zero there, by
        /// construction, so a handover costs nothing however the hysteresis biases it.
        /// </summary>
        public int CrestY(int gap)
        {
            int i = GateGeometry.Clamp(gap, 0, PositionCount - 2);
            return (_positions[i] + _positions[i + 1]) / 2;
        }

        /// <summary>P, R, N or D at this index, honouring the mirror flag.</summary>
        public string LabelFor(int index)
        {
            return Labels[LabelIndex(index)];
        }

        /// <summary>The vJoy button held while the lever is at this index.</summary>
        public int ButtonFor(int index)
        {
            return FirstButton + LabelIndex(index);
        }

        private int LabelIndex(int index)
        {
            int i = GateGeometry.Clamp(index, 0, PositionCount - 1);
            return _mirrored ? PositionCount - 1 - i : i;
        }

        /// <summary>
        /// Which position this reading belongs to, with the boundaries biased toward whichever one
        /// is already held so resting on a crest cannot flutter the button. Pass -1 for
        /// <paramref name="current"/> when nothing is held yet - a cold start - and the unbiased
        /// answer comes back.
        /// </summary>
        public int PositionAt(int y, int current)
        {
            int plain = IndexPastCrests(y, 0);
            if (current < 0 || plain == current) return plain;

            int bias = plain > current ? _hysteresis : -_hysteresis;
            return IndexPastCrests(y, bias);
        }

        private int IndexPastCrests(int y, int bias)
        {
            int index = 0;
            for (int i = 0; i < PositionCount - 1; i++)
            {
                if (y > CrestY(i) + bias) index = i + 1;
            }
            return index;
        }
    }
}
