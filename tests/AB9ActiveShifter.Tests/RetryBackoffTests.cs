using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The gate on retrying I/O that failed. Small, but it is the piece whose absence took the
    /// force loop from 990 Hz to 81 on the rig: the clutch pedal's open was attempted every tick
    /// while the device was missing, and opening a DirectInput device that is not there costs
    /// milliseconds. The last test is the regression - it counts attempts rather than trusting
    /// the arithmetic.
    /// </summary>
    public class RetryBackoffTests
    {
        [Fact]
        public void TheFirstAttemptIsAllowedImmediately()
        {
            // Nothing has failed yet, so there is nothing to hold off on. A backoff that made the
            // first open wait would delay every healthy start by its own schedule.
            var retry = new RetryBackoff(1000, 2000, 5000);

            Assert.True(retry.Due(0));
        }

        [Fact]
        public void AFailureHoldsOffUntilTheWaitHasElapsed()
        {
            var retry = new RetryBackoff(1000, 2000, 5000);

            retry.Failed(0);

            Assert.False(retry.Due(1));
            Assert.False(retry.Due(999));
            Assert.True(retry.Due(1000));
        }

        [Fact]
        public void TheWaitsEscalateAndThenHoldAtTheLast()
        {
            // Escalating means a device that is simply absent stops being asked about, without a
            // device that is briefly busy having to wait the long interval on its first failure.
            var retry = new RetryBackoff(1000, 2000, 5000);

            retry.Failed(0);
            Assert.True(retry.Due(1000));

            retry.Failed(1000);
            Assert.False(retry.Due(2999));
            Assert.True(retry.Due(3000));

            retry.Failed(3000);
            Assert.False(retry.Due(7999));
            Assert.True(retry.Due(8000));

            // And held there, rather than growing without bound - a pedal plugged back in after
            // an hour should be picked up in seconds, not in another hour.
            retry.Failed(8000);
            Assert.True(retry.Due(13000));
        }

        [Fact]
        public void SuccessStartsTheScheduleOver()
        {
            var retry = new RetryBackoff(1000, 2000, 5000);

            retry.Failed(0);
            retry.Failed(1000);
            retry.Succeeded();

            Assert.True(retry.Due(1000));

            // Back to the shortest wait, not the one it had climbed to.
            retry.Failed(1000);
            Assert.True(retry.Due(2000));
        }

        [Fact]
        public void ResetMakesTheNextCheckDueAtOnce()
        {
            // What a changed situation looks like: a different device picked, or a source
            // switched on. The previous failure says nothing about this attempt, and a user who
            // picks a device in the UI must not watch nothing happen for five seconds.
            var retry = new RetryBackoff(1000, 2000, 5000);

            retry.Failed(0);
            Assert.False(retry.Due(500));

            retry.Reset();
            Assert.True(retry.Due(500));
        }

        [Fact]
        public void AFailingDeviceCannotBeAskedAboutOncePerTick()
        {
            // The regression, counted rather than reasoned about. Ten seconds of a 1 kHz loop
            // with the device missing throughout: unguarded that is 10000 attempts, and at the
            // measured ~12 ms each it is why the loop read 81 Hz. The schedule here allows at
            // most one per second once it has escalated.
            var retry = new RetryBackoff(1000, 2000, 5000);

            int attempts = 0;
            for (long ms = 0; ms < 10000; ms++)
            {
                if (!retry.Due(ms)) continue;

                attempts++;
                retry.Failed(ms);
            }

            Assert.True(attempts <= 6, "attempted " + attempts + " opens in ten seconds");
            Assert.True(attempts >= 3, "backed off so far it would never notice a replugged pedal");
        }
    }
}
