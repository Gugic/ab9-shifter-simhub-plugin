using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// What happens between one gate and the next.
    ///
    /// <para>
    /// Switching profile used to apply the new gate's forces on the very next tick, wherever the
    /// lever happened to be. Change pattern while sitting in first - lever hard forward and hard
    /// left - and the sequential lever's spring wants it at centre, so full force arrives as a
    /// step through the 3-4 ms round trip. Reported from the rig as a vile oscillation, and it is
    /// the textbook version of this project's one recurring problem.
    /// </para>
    /// <para>
    /// The fix is deliberately NOT a centring force. A restoring force about an interior
    /// equilibrium is an oscillator - that rule is why the gate is built from corridors and
    /// plateaus rather than springs - and adding one here to cure an oscillation would be
    /// self-defeating. Instead:
    /// </para>
    /// <list type="number">
    /// <item>apply nothing at all, and let the base's own firmware centring carry the lever home;
    /// it pulls with roughly 90% of available force at full deflection, so it needs no help;</item>
    /// <item>once the lever is near centre - or the wait times out, because a hand can hold it
    /// anywhere and must not be able to hang the switch - wind the new gate in over a few hundred
    /// milliseconds instead of switching it on;</item>
    /// <item>then confirm which profile arrived, by pulsing the lever once per profile number.</item>
    /// </list>
    /// <para>
    /// Pure and clock-driven, so every awkward case is testable: a hand holding the lever through
    /// the whole settle, a switch made while already at centre, a switch made while the base is
    /// not moving at all.
    /// </para>
    /// </summary>
    public sealed class ProfileSwitchTransition
    {
        /// <summary>How close to centre counts as home, per axis, in axis counts.</summary>
        public const int SettleBand = 3000;

        /// <summary>
        /// Longest the settle may wait. A hand resting on the lever can hold it out of the band
        /// indefinitely, and a switch that never finished would leave the gate off for good.
        /// </summary>
        public const int SettleTimeoutMs = 800;

        /// <summary>How long the new gate takes to reach full strength.</summary>
        public const int RampMs = 350;

        /// <summary>One confirmation pulse, and the silence after it.</summary>
        public const int PulseMs = 70;
        public const int PulseGapMs = 130;

        /// <summary>Ceiling on how many pulses will ever be played, so profile 12 is not a minute.</summary>
        public const int MaxPulses = 8;

        private enum Stage { Idle, Settling, Ramping, Confirming }

        private Stage _stage = Stage.Idle;
        private long _stageStartedMs;
        private int _pulsesLeft;

        /// <summary>True while the gate should not simply be applied at full strength.</summary>
        public bool Active { get { return _stage != Stage.Idle; } }

        /// <summary>
        /// What to multiply the gate's force by this tick: zero while settling, winding up through
        /// the ramp, one once the gate is fully in.
        /// </summary>
        public double ForceScale { get; private set; }

        /// <summary>
        /// Amplitude of the confirmation buzz this tick, 0..1. Time-keyed, like every other
        /// carrier, so it cannot join the position-to-force loop the stabilisers exist to damp.
        /// </summary>
        public double PulseEnvelope { get; private set; }

        /// <summary>
        /// Begins a transition. <paramref name="pulses"/> is the profile's position in the store,
        /// one-based, so the hand can count which one arrived without looking at the screen.
        /// </summary>
        public void Begin(long nowMs, int pulses)
        {
            _stage = Stage.Settling;
            _stageStartedMs = nowMs;
            _pulsesLeft = GateGeometry.Clamp(pulses, 0, MaxPulses);
            ForceScale = 0;
            PulseEnvelope = 0;
        }

        /// <summary>Abandons the transition and hands the gate straight back at full strength.</summary>
        public void Cancel()
        {
            _stage = Stage.Idle;
            ForceScale = 1;
            PulseEnvelope = 0;
        }

        /// <summary>Called once per tick with the current stick position.</summary>
        public void Step(long nowMs, int x, int y)
        {
            PulseEnvelope = 0;

            switch (_stage)
            {
                case Stage.Idle:
                    ForceScale = 1;
                    return;

                case Stage.Settling:
                    {
                        ForceScale = 0;

                        bool home = Math.Abs(x - GateGeometry.AxisCenter) <= SettleBand
                                    && Math.Abs(y - GateGeometry.AxisCenter) <= SettleBand;

                        // The timeout is not a fallback, it is the common case: a hand that never let
                        // go holds the lever out of the band forever, and waiting for it would mean a
                        // profile switch that silently never took effect.
                        if (home || nowMs - _stageStartedMs >= SettleTimeoutMs)
                        {
                            _stage = Stage.Ramping;
                            _stageStartedMs = nowMs;
                        }
                        return;
                    }

                case Stage.Ramping:
                    {
                        long elapsed = nowMs - _stageStartedMs;
                        ForceScale = GateGeometry.Clamp(elapsed / (double)RampMs, 0.0, 1.0);

                        if (elapsed >= RampMs)
                        {
                            ForceScale = 1;
                            _stage = _pulsesLeft > 0 ? Stage.Confirming : Stage.Idle;
                            _stageStartedMs = nowMs;
                        }
                        return;
                    }

                case Stage.Confirming:
                    {
                        // The gate is fully in by now, so the pulses ride on top of a settled lever
                        // rather than competing with a force that is still winding up.
                        ForceScale = 1;

                        long elapsed = nowMs - _stageStartedMs;
                        if (elapsed < PulseMs)
                        {
                            PulseEnvelope = 1.0;
                            return;
                        }

                        if (elapsed < PulseMs + PulseGapMs) return;

                        _pulsesLeft--;
                        _stageStartedMs = nowMs;
                        if (_pulsesLeft <= 0) _stage = Stage.Idle;
                        return;
                    }
            }
        }
    }
}
