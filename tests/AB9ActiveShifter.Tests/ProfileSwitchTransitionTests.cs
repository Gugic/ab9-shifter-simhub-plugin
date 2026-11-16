using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The gap between one gate and the next. Worth testing away from hardware because the whole
    /// point is what happens in the awkward cases - a hand that never lets go, a switch made from
    /// a corner of the gate, a base that is not moving at all - and because getting it wrong
    /// reintroduces exactly the oscillation it exists to remove.
    /// </summary>
    public class ProfileSwitchTransitionTests
    {
        private const int Centre = GateGeometry.AxisCenter;

        /// <summary>Runs the transition forward, holding the stick at one place.</summary>
        private static long Run(ProfileSwitchTransition t, long fromMs, long toMs, int x, int y)
        {
            for (long ms = fromMs; ms <= toMs; ms++) t.Step(ms, x, y);
            return toMs;
        }

        [Fact]
        public void NoForceIsAppliedUntilTheLeverHasComeHome()
        {
            // The reported bug, as a property: switching while sitting in first must not put the
            // new gate's full force onto a lever that is hard over. Zero, not "less", because any
            // force at all against a lever the firmware is already dragging home is a fight.
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 1);

            for (long ms = 0; ms < ProfileSwitchTransition.SettleTimeoutMs; ms++)
            {
                t.Step(ms, 1000, 1000);   // hard forward, hard left - deep in a gear
                Assert.Equal(0.0, t.ForceScale);
            }
        }

        [Fact]
        public void OnceHomeTheGateWindsInRatherThanSwitchingOn()
        {
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 0);

            t.Step(1, Centre, Centre);          // already home, so settling ends at once
            Assert.Equal(0.0, t.ForceScale);

            // Somewhere in the middle of the ramp it must be partway, not all or nothing.
            t.Step(1 + ProfileSwitchTransition.RampMs / 2, Centre, Centre);
            Assert.InRange(t.ForceScale, 0.3, 0.7);

            t.Step(1 + ProfileSwitchTransition.RampMs, Centre, Centre);
            Assert.Equal(1.0, t.ForceScale);
        }

        [Fact]
        public void TheRampNeverGoesBackwards()
        {
            // A force that wound in and then dipped would be a step in the other direction, which
            // is the same fault wearing a different hat.
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 3);

            double previous = -1;
            bool rampDone = false;
            for (long ms = 0; ms < 3000; ms++)
            {
                t.Step(ms, Centre, Centre);
                if (t.ForceScale >= 1.0) rampDone = true;
                if (!rampDone)
                {
                    Assert.True(t.ForceScale >= previous,
                        "force scale fell at " + ms + " ms: " + previous + " -> " + t.ForceScale);
                    previous = t.ForceScale;
                }
                else
                {
                    Assert.Equal(1.0, t.ForceScale);
                }
            }
        }

        [Fact]
        public void AHandThatNeverLetsGoStillGetsItsProfile()
        {
            // Someone can hold the lever anywhere indefinitely. Waiting for it to come home would
            // mean a switch that silently never took effect - much worse than a firm arrival.
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 0);

            long ms = Run(t, 0, ProfileSwitchTransition.SettleTimeoutMs - 1, 500, 500);
            Assert.Equal(0.0, t.ForceScale);

            ms = Run(t, ms + 1, ms + 1 + ProfileSwitchTransition.RampMs, 500, 500);
            Assert.Equal(1.0, t.ForceScale);
            Assert.False(t.Active);
        }

        [Fact]
        public void TheProfileNumberIsPulsedOutOnce()
        {
            // The confirmation the rig asked for: one pulse for the first profile, two for the
            // second. Counted by rising edges, because that is what a hand counts.
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 3);

            int edges = 0;
            bool wasOn = false;

            for (long ms = 0; ms < 5000; ms++)
            {
                t.Step(ms, Centre, Centre);
                bool on = t.PulseEnvelope > 0;
                if (on && !wasOn) edges++;
                wasOn = on;
            }

            Assert.Equal(3, edges);
            Assert.False(t.Active);
        }

        [Fact]
        public void PulsesOnlyPlayOnceTheGateIsFullyIn()
        {
            // A confirmation buzz competing with a force that is still winding up would be felt
            // as part of the wind-up rather than as a count.
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 2);

            for (long ms = 0; ms < 5000; ms++)
            {
                t.Step(ms, Centre, Centre);
                if (t.PulseEnvelope > 0) Assert.Equal(1.0, t.ForceScale);
            }
        }

        [Fact]
        public void ZeroPulsesMeansNoConfirmationAtAll()
        {
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 0);

            for (long ms = 0; ms < 3000; ms++)
            {
                t.Step(ms, Centre, Centre);
                Assert.Equal(0.0, t.PulseEnvelope);
            }

            Assert.False(t.Active);
        }

        [Fact]
        public void APreposterousProfileNumberIsCapped()
        {
            // Nobody wants to sit through forty pulses because they made forty profiles.
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 40);

            int edges = 0;
            bool wasOn = false;
            for (long ms = 0; ms < 20000; ms++)
            {
                t.Step(ms, Centre, Centre);
                bool on = t.PulseEnvelope > 0;
                if (on && !wasOn) edges++;
                wasOn = on;
            }

            Assert.Equal(ProfileSwitchTransition.MaxPulses, edges);
        }

        [Fact]
        public void AnEngineRestartMidPulseCannotLeaveThePulseOn()
        {
            // The traced fault: the engine had been running for ten minutes, a pattern change
            // began a transition, and the onboard-settings write restarted the engine - and with
            // it the clock, from zero - while a confirmation pulse was playing. The pulse then
            // stayed on until the new clock caught up with the old one.
            const long oldClock = 600000;
            var t = new ProfileSwitchTransition();
            t.Begin(oldClock, pulses: 3);

            long ms = oldClock;
            while (t.PulseEnvelope <= 0) t.Step(++ms, Centre, Centre);   // into the first pulse

            // The clock restarts. Everything must be over well inside the time three pulses take.
            long budget = 3 * (ProfileSwitchTransition.PulseMs + ProfileSwitchTransition.PulseGapMs) + 50;
            int pulseTicks = 0;
            for (long now = 0; now <= budget; now++)
            {
                t.Step(now, Centre, Centre);
                if (t.PulseEnvelope > 0) pulseTicks++;
            }

            Assert.False(t.Active, "the transition never finished after the clock restarted");
            Assert.Equal(0.0, t.PulseEnvelope);
            Assert.Equal(1.0, t.ForceScale);
            Assert.True(pulseTicks <= 3 * ProfileSwitchTransition.PulseMs + 3, "pulse ticks after restart: " + pulseTicks);
        }

        [Fact]
        public void AnEngineRestartMidRampCannotHoldTheGateOff()
        {
            // The same fault one stage earlier: a negative elapsed time clamps the ramp to zero,
            // so the new gate would simply not exist until the clock caught up.
            const long oldClock = 600000;
            var t = new ProfileSwitchTransition();
            t.Begin(oldClock, pulses: 0);
            t.Step(oldClock + 1, Centre, Centre);                                  // home: ramp begins
            t.Step(oldClock + 1 + ProfileSwitchTransition.RampMs / 2, Centre, Centre);
            Assert.InRange(t.ForceScale, 0.3, 0.7);

            for (long now = 0; now <= ProfileSwitchTransition.RampMs + 1; now++) t.Step(now, Centre, Centre);

            Assert.Equal(1.0, t.ForceScale);
            Assert.False(t.Active);
        }

        [Fact]
        public void AnEngineRestartWhileSettlingStillTimesOut()
        {
            // A hand holding the lever out of the band must not be able to hang the switch just
            // because the clock restarted underneath it.
            var t = new ProfileSwitchTransition();
            t.Begin(600000, pulses: 0);

            for (long now = 0; now <= ProfileSwitchTransition.SettleTimeoutMs + ProfileSwitchTransition.RampMs + 2; now++)
                t.Step(now, 1000, 1000);

            Assert.Equal(1.0, t.ForceScale);
            Assert.False(t.Active);
        }

        [Fact]
        public void AnIdleTransitionLeavesTheGateAlone()
        {
            // The overwhelmingly common tick: nothing is switching, so nothing is scaled.
            var t = new ProfileSwitchTransition();
            t.Step(0, 1000, 60000);

            Assert.False(t.Active);
            Assert.Equal(1.0, t.ForceScale);
            Assert.Equal(0.0, t.PulseEnvelope);
        }

        [Fact]
        public void CancellingHandsTheGateBackAtFullStrength()
        {
            var t = new ProfileSwitchTransition();
            t.Begin(0, pulses: 5);
            t.Step(10, 1000, 1000);
            Assert.Equal(0.0, t.ForceScale);

            t.Cancel();

            Assert.False(t.Active);
            Assert.Equal(1.0, t.ForceScale);
        }
    }
}
