using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Turns a stream of stick positions into gear selections. Pure logic, no I/O, so the
    /// whole gate can be exercised from tests with scripted coordinate traces.
    ///
    /// The column is latched on leaving the neutral channel and then held, whatever the stick
    /// does sideways, until it comes back through the channel. A real gate works the same way:
    /// once the lever is in a slot the only route to another gear is back out to neutral and
    /// along the tunnel. That makes a gear impossible to change diagonally, so a wall that is
    /// leant on hard - or briefly overpowered - cannot hand over a gear it was guarding, and the
    /// slot walls no longer have to reach full strength before an exit band they no longer own.
    /// Only a gross lateral escape counts, as a fault to resynchronise from.
    ///
    /// Engage and release use separate depth thresholds so resting on the boundary cannot
    /// chatter the button.
    /// </summary>
    public sealed class GateStateMachine
    {
        private readonly GateGeometry _geo;
        private readonly int _minEngageTicks;

        private GateState _state = GateState.Neutral;
        private Column _column = Column.None;
        private ShiftDir _direction = ShiftDir.None;
        private int _gear;
        private int _engageTicks;

        /// <summary>
        /// Set after a fault: nothing may be latched until the stick has been seen in the neutral
        /// channel. Without it a stick dragged clean out of one column and into the next would be
        /// handed the new gear on the following tick, which is the diagonal shift the gate exists
        /// to forbid - just reached through the fault path instead of the front door.
        /// </summary>
        private bool _awaitChannel;

        public GateStateMachine(GateGeometry geometry, int minEngageTicks)
        {
            _geo = geometry;
            _minEngageTicks = Math.Max(1, minEngageTicks);
        }

        public GateState State { get { return _state; } }
        public Column Column { get { return _column; } }
        public ShiftDir Direction { get { return _direction; } }
        public int CurrentGear { get { return _gear; } }

        /// <summary>Times the stick was forced out of a latched column, e.g. by overpowering a wall.</summary>
        public long AnomalyCount { get; private set; }

        public StateTransition Update(int x, int y)
        {
            int previousGear = _gear;

            switch (_state)
            {
                case GateState.Neutral:
                    StepNeutral(x, y);
                    break;

                case GateState.Traveling:
                    StepTraveling(x, y);
                    break;

                case GateState.Engaged:
                    StepEngaged(x, y);
                    break;
            }

            return new StateTransition
            {
                State = _state,
                Column = _column,
                Direction = _direction,
                Gear = _gear,
                GearChanged = _gear != previousGear,
                PreviousGear = previousGear
            };
        }

        private void StepNeutral(int x, int y)
        {
            if (_awaitChannel)
            {
                if (!_geo.InChannel(y)) return;
                _awaitChannel = false;
            }

            if (!_geo.OutOfChannel(y)) return;

            Column c = _geo.ColumnAt(x);
            if (c == Column.None)
            {
                // Pressing against the channel wall between columns. Not an error: the Y wall
                // is what should be resisting here, so there is nothing to select.
                return;
            }

            _column = c;
            _direction = _geo.DirectionOf(y);
            _state = GateState.Traveling;
            _engageTicks = 0;
        }

        private void StepTraveling(int x, int y)
        {
            if (_geo.EscapedColumn(_column, x))
            {
                Fault();
                return;
            }

            if (_geo.InChannel(y))
            {
                EnterNeutral();
                return;
            }

            if (_geo.IsEngaged(_direction, y))
            {
                _engageTicks++;
                if (_engageTicks >= _minEngageTicks)
                {
                    _state = GateState.Engaged;
                    _gear = _geo.GearFor(_column, _direction);
                }
            }
            else
            {
                _engageTicks = 0;
            }
        }

        private void StepEngaged(int x, int y)
        {
            if (_geo.EscapedColumn(_column, x))
            {
                Fault();
                return;
            }

            if (_geo.IsReleased(_direction, y))
            {
                _state = GateState.Traveling;
                _gear = 0;
                _engageTicks = 0;
            }
        }

        private void EnterNeutral()
        {
            _state = GateState.Neutral;
            _column = Column.None;
            _direction = ShiftDir.None;
            _gear = 0;
            _engageTicks = 0;
        }

        /// <summary>
        /// The stick left a latched column by a distance no wall should have allowed. Drop the
        /// gear and refuse to latch anything until the neutral channel has been seen, so a fault
        /// cannot be a shortcut into the gear the stick happens to have landed on.
        /// </summary>
        private void Fault()
        {
            AnomalyCount++;
            EnterNeutral();
            _awaitChannel = true;
        }

        /// <summary>
        /// Derives state purely from the current position. Used at startup, after a geometry
        /// change, and to recover from an anomaly, so the engine never carries a stale latch.
        /// </summary>
        public void Resync(int x, int y)
        {
            // An explicit resynchronisation is a statement that the position is to be trusted -
            // at startup, or after the geometry moved - so it clears any pending fault.
            _awaitChannel = false;

            if (_geo.InChannel(y))
            {
                EnterNeutral();
                return;
            }

            Column c = _geo.ColumnAt(x);
            if (c == Column.None)
            {
                EnterNeutral();
                return;
            }

            _column = c;
            _direction = _geo.DirectionOf(y);

            if (_geo.IsEngaged(_direction, y))
            {
                _state = GateState.Engaged;
                _gear = _geo.GearFor(c, _direction);
                _engageTicks = _minEngageTicks;
            }
            else
            {
                _state = GateState.Traveling;
                _gear = 0;
                _engageTicks = 0;
            }
        }
    }
}
