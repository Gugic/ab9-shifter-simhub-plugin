using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Turns fore/aft lever motion into single up/down shift events. Pure logic, no I/O.
    ///
    /// One shift fires per stroke: pushing past the engage threshold fires once, and nothing
    /// more can fire until the lever has come back inside the release threshold - the same
    /// engage/release hysteresis pair the H gate uses, doing the same job of keeping a lever
    /// resting on the boundary from machine-gunning the button. Forward is upshift unless
    /// <see cref="GateGeometry.MirrorSlots"/> says otherwise, mirroring the H gate's use of
    /// that flag for which direction is "first".
    /// </summary>
    public sealed class SequentialStateMachine
    {
        private readonly GateGeometry _geo;
        private readonly int _minEngageTicks;

        private bool _armed = true;
        private ShiftDir _pushed = ShiftDir.None;
        private int _engageTicks;

        public SequentialStateMachine(GateGeometry geometry, int minEngageTicks)
        {
            _geo = geometry;
            _minEngageTicks = Math.Max(1, minEngageTicks);
        }

        public bool Armed { get { return _armed; } }
        public ShiftDir Pushed { get { return _pushed; } }

        /// <summary>The shared meaning of a physical stroke, for output, status and the monitor.</summary>
        public static int ShiftForDirection(ShiftDir direction, bool pullToUpshift)
        {
            int shift = direction == ShiftDir.Fwd ? 1 : direction == ShiftDir.Back ? -1 : 0;
            return pullToUpshift ? -shift : shift;
        }

        public static string LabelForDirection(ShiftDir direction, bool pullToUpshift)
        {
            int shift = ShiftForDirection(direction, pullToUpshift);
            return shift > 0 ? "+" : shift < 0 ? "-" : "N";
        }

        public SeqTransition Update(int y)
        {
            int fired = 0;
            ShiftDir dir = _geo.DirectionOf(y);

            if (_armed)
            {
                if (_geo.IsEngaged(dir, y))
                {
                    _engageTicks++;
                    if (_engageTicks >= _minEngageTicks)
                    {
                        fired = ShiftForDirection(dir, _geo.MirrorSlots);
                        _armed = false;
                        _pushed = dir;
                    }
                }
                else
                {
                    _engageTicks = 0;
                }
            }
            else if (_geo.IsReleased(_pushed, y))
            {
                _armed = true;
                _pushed = ShiftDir.None;
                _engageTicks = 0;
            }

            return new SeqTransition
            {
                Shift = fired,
                Armed = _armed,
                Pushed = _pushed
            };
        }

        /// <summary>
        /// Adopts the current position without firing anything. A lever already past a
        /// threshold at startup must not shift a gear the user never asked for.
        /// </summary>
        public void Resync(int y)
        {
            ShiftDir dir = _geo.DirectionOf(y);
            if (_geo.IsEngaged(dir, y))
            {
                _armed = false;
                _pushed = dir;
            }
            else
            {
                _armed = true;
                _pushed = ShiftDir.None;
            }
            _engageTicks = 0;
        }
    }
}
