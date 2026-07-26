using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The estimator exists as its own class because of one hardware fact: while force writes
    /// are in flight, distinct positions arrive at ~500 Hz, so a 1 kHz poll sees the same
    /// snapshot on alternate ticks. These tests feed exactly that stale-then-jump stream and
    /// demand a steady answer - the failure mode was a 2:1 velocity sawtooth that the rebound
    /// absorber turned into a 250-500 Hz grinding texture on every wall.
    /// </summary>
    public class VelocityEstimatorTests
    {
        /// <summary>The measured contended report pattern: the position moves every other tick.</summary>
        private static void FeedStaleThenJump(VelocityEstimator v, ref int x, ref double ms, int step, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                if (i % 2 == 0) x += step;
                ms += 1.0;
                v.Update(x, 30000, ms);
            }
        }

        [Fact]
        public void AStaleThenJumpStreamReadsAsItsTrueMeanSpeed()
        {
            // 35 counts every 2 ms = 17500 counts/s, the sweep speed measured in the trace
            // that exposed this. Adjacent-tick differencing reported it as 10000-25000
            // alternating; the windowed difference must not.
            VelocityEstimator v = new VelocityEstimator();
            int x = 5000;
            double ms = 0;

            FeedStaleThenJump(v, ref x, ref ms, 35, 20); // settle

            int min = int.MaxValue, max = int.MinValue;
            for (int i = 0; i < 40; i++)
            {
                if (i % 2 == 0) x += 35;
                ms += 1.0;
                v.Update(x, 30000, ms);
                if (v.X < min) min = v.X;
                if (v.X > max) max = v.X;
            }

            Assert.InRange(min, 16000, 19000);
            Assert.InRange(max, 16000, 19000);
            Assert.True(max - min <= 1500,
                "the report quantisation must not reach the estimate: ripple " + (max - min));
        }

        [Fact]
        public void AReversalChangesSignWithinAFewMilliseconds()
        {
            // The yield keys its same-direction test on this sign. Lag here is force given
            // back after a real launch, so the window buys ripple rejection with only a
            // couple of milliseconds of it.
            VelocityEstimator v = new VelocityEstimator();
            int x = 30000;
            double ms = 0;

            FeedStaleThenJump(v, ref x, ref ms, 35, 30);
            Assert.True(v.X > 10000);

            double reversedAt = ms;
            for (int i = 0; i < 20; i++)
            {
                if (i % 2 == 0) x -= 35;
                ms += 1.0;
                v.Update(x, 30000, ms);
                if (v.X < 0) break;
            }

            Assert.True(v.X < 0, "estimate never flipped sign after a reversal");
            Assert.True(ms - reversedAt <= 8.0, "sign flip took " + (ms - reversedAt) + " ms");
        }

        [Fact]
        public void RestingJitterStaysInsideTheYieldDeadband()
        {
            // A hand resting on the stick wobbles the axis by a count or two. That must read
            // as leaning, not motion, or walls soften against a still hand.
            VelocityEstimator v = new VelocityEstimator();
            double ms = 0;

            for (int i = 0; i < 60; i++)
            {
                ms += 1.0;
                v.Update(30000 + (i % 2), 30000, ms);
                if (i > 10) Assert.InRange(v.X, -1000, 1000);
            }
        }

        [Fact]
        public void AStallResynchronisesInsteadOfSpiking()
        {
            // Positions from either side of a stall are not adjacent in time; differencing
            // across one would read the gap as enormous speed and fire the yield on nothing.
            VelocityEstimator v = new VelocityEstimator();
            int x = 10000;
            double ms = 0;

            FeedStaleThenJump(v, ref x, ref ms, 35, 20);
            int before = v.X;

            ms += 80.0; // the thread stalls, the stick keeps moving
            x += 1400;
            v.Update(x, 30000, ms);

            Assert.Equal(before, v.X); // the stall tick keeps the last estimate

            FeedStaleThenJump(v, ref x, ref ms, 35, 20);
            Assert.InRange(v.X, 12000, 22000); // and recovery converges on the true speed
        }

        [Fact]
        public void ResetForgetsMotion()
        {
            VelocityEstimator v = new VelocityEstimator();
            int x = 10000;
            double ms = 0;

            FeedStaleThenJump(v, ref x, ref ms, 35, 20);
            Assert.NotEqual(0, v.X);

            v.Reset();
            Assert.Equal(0, v.X);
            Assert.Equal(0, v.Y);

            // The first samples after a reset must not read the position gap as speed.
            v.Update(50000, 30000, ms + 1.0);
            Assert.Equal(0, v.X);
        }

        [Fact]
        public void BothAxesAreEstimatedIndependently()
        {
            VelocityEstimator v = new VelocityEstimator();
            int x = 10000, y = 40000;
            double ms = 0;

            for (int i = 0; i < 30; i++)
            {
                if (i % 2 == 0) { x += 35; y -= 20; }
                ms += 1.0;
                v.Update(x, y, ms);
            }

            Assert.InRange(v.X, 15000, 20000);
            Assert.InRange(v.Y, -12000, -8000);
        }
    }
}
