using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Turns a stream of stick positions into gear selections. Pure logic, no I/O, so the
    /// whole gate can be exercised from tests with scripted coordinate traces.
    ///
    /// The column is latched on leaving the neutral channel and then held - whatever the stick
    /// does sideways, however far - until it comes back through the channel. A real gate works
    /// the same way: once the lever is in a slot, the only route to another gear is back out to
    /// neutral and along the tunnel.
    ///
    /// There is deliberately no lateral escape at all, not even a generous one. Force cannot
    /// enforce this: a hand beats 12 Nm, so any distance at which the latch gave way would be a
    /// distance at which the rest of the pattern came back and could capture the lever into a
    /// gear it was never driven into. Making the lock absolute means pushing sideways can achieve
    /// nothing except being pushed back, which is the guarantee a gate is supposed to give. It is
    /// also what frees the slot walls from ever having to be strong enough to win.
    ///
    /// Engage and release use separate depth thresholds so resting on the boundary cannot
    /// chatter the button. <see cref="Resync"/> remains the way to adopt whatever position the
    /// stick is actually in, for startup and for a geometry change under the running loop.
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

        public GateStateMachine(GateGeometry geometry, int minEngageTicks)
        {
            _geo = geometry;
            _minEngageTicks = Math.Max(1, minEngageTicks);
        }

        public GateState State { get { return _state; } }
        public Column Column { get { return _column; } }
        public ShiftDir Direction { get { return _direction; } }
        public int CurrentGear { get { return _gear; } }

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
        /// Derives state purely from the current position. Used at startup, after a geometry
        /// change, and to recover from an anomaly, so the engine never carries a stale latch.
        /// </summary>
        public void Resync(int x, int y)
        {
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
