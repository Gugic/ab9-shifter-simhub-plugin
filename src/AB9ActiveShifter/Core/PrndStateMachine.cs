using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Which PRND position the lever is in. Pure logic, no I/O.
    ///
    /// Much simpler than the H gate's machine, and simpler on purpose: there is no neutral, no
    /// travelling and no engage debounce, because a selector lever is always in exactly one
    /// position and moving between them is not a state. All the chatter protection a debounce
    /// would buy is already in <see cref="PrndLane.PositionAt"/>'s hysteresis, which biases each
    /// crest toward whichever position is held - and unlike a tick count that works when the hand
    /// rests on a boundary indefinitely, which is the case that actually happens.
    ///
    /// The transition is reported as an ordinary <see cref="StateTransition"/> with the vJoy button
    /// number in <see cref="StateTransition.Gear"/>, so the engine's existing "buttons before
    /// forces" path carries it unchanged - including the release-before-press that stops a game
    /// ever seeing two positions at once.
    /// </summary>
    public sealed class PrndStateMachine
    {
        private readonly PrndLane _lane;
        private int _index = -1;

        public PrndStateMachine(PrndLane lane)
        {
            if (lane == null) throw new ArgumentNullException("lane");
            _lane = lane;
        }

        public PrndLane Lane { get { return _lane; } }

        /// <summary>Position index, low y first; -1 before any reading has been seen.</summary>
        public int Index { get { return _index; } }

        /// <summary>The button currently held, or 0 before the first reading.</summary>
        public int CurrentButton { get { return _index < 0 ? 0 : _lane.ButtonFor(_index); } }

        public string CurrentLabel { get { return _index < 0 ? "-" : _lane.LabelFor(_index); } }

        public StateTransition Update(int y)
        {
            int previous = CurrentButton;
            _index = _lane.PositionAt(y, _index);
            int button = CurrentButton;

            return new StateTransition
            {
                // Always somewhere, which is the whole difference from the H gate.
                State = GateState.Engaged,
                Column = Column.None,
                Direction = ShiftDir.None,
                Gear = button,
                GearChanged = button != previous,
                PreviousGear = previous
            };
        }

        /// <summary>
        /// Adopts whatever position the lever is actually in, without reporting a change. Used at
        /// startup and after a geometry change, exactly like the other two machines; the engine
        /// pushes the adopted button to vJoy itself rather than inferring it from a transition.
        /// </summary>
        public void Resync(int y)
        {
            _index = _lane.PositionAt(y, -1);
        }
    }
}
