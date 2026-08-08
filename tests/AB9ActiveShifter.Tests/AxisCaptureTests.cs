using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The "press the pedal you want" capture, and the arithmetic that turns what it saw into a
    /// clutch reading. There is no pedal set near a test runner, which is exactly why this lives
    /// in Core: the cases that are painful to reproduce by hand - a pedal wired backwards, one
    /// that rests at full scale, a device that says nothing at all - are the ones most likely to
    /// be wrong, and a wrong sign here makes the grind fire precisely when it should not.
    /// </summary>
    public class AxisCaptureTests
    {
        private const string Pedals = "pedals";

        /// <summary>A resting pedal set with a little jitter, then a press and a release.</summary>
        private static AxisCapture Run(int rest, int pressed, int jitter = 6, int axisCount = 3,
                                       int axis = 2, bool release = true)
        {
            var c = new AxisCapture(0);
            long t = 0;

            int[] frame = new int[axisCount];
            for (int i = 0; i < axisCount; i++) frame[i] = 20000;
            frame[axis] = rest;

            // Baseline: hold still, wobbling by the jitter so a noise floor exists to measure.
            for (; t <= AxisCapture.BaselineMs; t += 10)
            {
                frame[axis] = rest + (t / 10 % 2 == 0 ? jitter : -jitter);
                c.Observe(Pedals, frame, t);
            }

            // Press, in steps, so peak tracking has something to accumulate.
            for (int step = 1; step <= 20; step++, t += 10)
            {
                frame[axis] = rest + (pressed - rest) * step / 20;
                c.Observe(Pedals, frame, t);
            }

            if (!release) return c;

            for (int step = 20; step >= 0; step--, t += 10)
            {
                frame[axis] = rest + (pressed - rest) * step / 20;
                c.Observe(Pedals, frame, t);
            }

            return c;
        }

        [Fact]
        public void AnOrdinaryPedalIsCommittedOnRelease()
        {
            AxisCapture c = Run(rest: 400, pressed: 62000);

            Assert.Equal(CapturePhase.Committed, c.Phase);
            Assert.Equal(Pedals, c.DeviceId);
            Assert.Equal(2, c.AxisIndex);
            Assert.False(c.Result.Invert);

            // Released reads 0 and pressed reads 100, which is the only property that matters.
            Assert.Equal(0, c.Result.ToPercent(400), 1);
            Assert.Equal(100, c.Result.ToPercent(62000), 1);
        }

        [Fact]
        public void APedalWiredBackwardsStillReadsZeroAtRest()
        {
            // Half of all pedal sets fall as they are pressed. A user cannot be expected to know
            // which they own, so the direction is measured - and if it were ever taken the wrong
            // way round the clutch would read fully pressed at rest and the grind would never
            // fire at all, which is a silent failure rather than a loud one.
            AxisCapture c = Run(rest: 65000, pressed: 900);

            Assert.Equal(CapturePhase.Committed, c.Phase);
            Assert.True(c.Result.Invert);
            Assert.Equal(0, c.Result.ToPercent(65000), 1);
            Assert.Equal(100, c.Result.ToPercent(900), 1);
        }

        [Fact]
        public void TheAxisThatMovedIsTheAxisThatIsBound()
        {
            // A pedal set presents three axes at once and two of them are not the clutch. The
            // capture has to pick by movement, not by index.
            AxisCapture c = Run(rest: 500, pressed: 60000, axisCount: 6, axis: 4);

            Assert.Equal(CapturePhase.Committed, c.Phase);
            Assert.Equal(4, c.AxisIndex);
        }

        [Fact]
        public void RestingJitterNeverCommitsAnything()
        {
            var c = new AxisCapture(0);
            int[] frame = new int[3];

            for (long t = 0; t < AxisCapture.DeadlineMs - 100; t += 10)
            {
                frame[1] = 30000 + (t / 10 % 2 == 0 ? 40 : -40);
                c.Observe(Pedals, frame, t);
            }

            // Still waiting: jitter of 80 counts never beats the trigger, so nothing is latched
            // and nothing is committed on it.
            Assert.Equal(CapturePhase.Waiting, c.Phase);
            Assert.Null(c.Result);
        }

        [Fact]
        public void ASilentDeviceTimesOutInsteadOfHangingTheUi()
        {
            // Phase has to advance on the clock, not on frames: a device that reports nothing at
            // all must still leave the baseline window and still reach the deadline, or the
            // capture dialog waits forever with no way out.
            var c = new AxisCapture(0);
            c.Tick(AxisCapture.BaselineMs + 1);
            Assert.Equal(CapturePhase.Waiting, c.Phase);

            c.Tick(AxisCapture.DeadlineMs + 1);
            Assert.Equal(CapturePhase.TimedOut, c.Phase);
            Assert.Null(c.Result);
        }

        [Fact]
        public void APedalHeldPastTheDeadlineStillCommitsItsTravel()
        {
            // Someone who presses and holds has told us everything about that axis. Throwing it
            // away and reporting failure would be the least helpful possible answer.
            AxisCapture c = Run(rest: 300, pressed: 61000, release: false);
            Assert.Equal(CapturePhase.Tracking, c.Phase);

            c.Tick(AxisCapture.DeadlineMs + 1);

            Assert.Equal(CapturePhase.Committed, c.Phase);
            Assert.Equal(100, c.Result.ToPercent(61000), 1);
        }

        [Fact]
        public void TheRestingDeadzoneSwallowsTheJitterItMeasured()
        {
            // The point of measuring a noise floor: a pedal that rests noisily must still read
            // exactly zero, or a grind keyed on "clutch up" flickers while the foot is off.
            AxisCapture c = Run(rest: 500, pressed: 60000, jitter: 60);

            Assert.True(c.Result.DeadzoneLow > 0, "no deadzone was derived from the jitter");
            for (int raw = 440; raw <= 560; raw += 10)
            {
                Assert.Equal(0, c.Result.ToPercent(raw), 3);
            }
        }

        [Fact]
        public void CalibrationIsMonotonicAndBounded()
        {
            AxisCapture c = Run(rest: 400, pressed: 62000);
            AxisCalibration cal = c.Result;

            double previous = -1;
            for (int raw = 0; raw <= 65535; raw += 137)
            {
                double pct = cal.ToPercent(raw);
                Assert.InRange(pct, 0.0, 100.0);
                Assert.True(pct >= previous, "clutch went backwards at raw " + raw);
                previous = pct;
            }
        }

        [Fact]
        public void AnUncalibratedAxisPassesThroughSoTheUiShowsMovement()
        {
            // A freshly picked axis with nothing measured yet has to look alive, or "wrong axis"
            // is indistinguishable from "feature broken".
            var cal = new AxisCalibration();
            Assert.False(cal.IsCalibrated);
            Assert.Equal(0, cal.ToPercent(0), 1);
            Assert.Equal(100, cal.ToPercent(65535), 1);
        }

        [Fact]
        public void CancellingStopsItAcceptingAnythingFurther()
        {
            var c = new AxisCapture(0);
            c.Cancel();

            int[] frame = { 0, 65535, 0 };
            c.Observe(Pedals, frame, AxisCapture.BaselineMs + 500);

            Assert.Equal(CapturePhase.Cancelled, c.Phase);
            Assert.Null(c.Result);
        }
    }
}
