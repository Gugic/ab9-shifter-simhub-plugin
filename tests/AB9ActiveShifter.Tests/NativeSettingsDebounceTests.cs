using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class NativeSettingsDebounceTests
    {
        [Fact]
        public void DraggingWaitsForQuietAndWritesOnlyTheLatestCopiedTune()
        {
            var queue = new NativeSettingsDebounce();
            var tune = new Ab9NativeSettings { Spring = 10, Torque = 100 };
            queue.Schedule(tune, 0);
            tune.Spring = 20;
            queue.Schedule(tune, 400);
            tune.Spring = 30; // Unsubmitted mutations cannot alter the queued snapshot.
            Ab9NativeSettings write;
            Assert.False(queue.TryTake(500, true, out write));
            Assert.False(queue.TryTake(899, true, out write));
            Assert.True(queue.TryTake(900, true, out write));
            Assert.Equal(20, write.Spring);
            Assert.Equal(100, write.Torque);
            Assert.False(queue.TryTake(1500, true, out write));
        }

        [Fact]
        public void AReadHoldingThePortDoesNotLoseThePendingEdit()
        {
            var queue = new NativeSettingsDebounce();
            queue.Schedule(new Ab9NativeSettings { Damper = 16 }, 0);
            Ab9NativeSettings write;
            Assert.False(queue.TryTake(700, false, out write));
            Assert.True(queue.Pending);
            Assert.True(queue.TryTake(800, true, out write));
            Assert.Equal(16, write.Damper);
        }

        [Fact]
        public void EditsDuringAWriteCoalesceIntoOneFollowupWithoutRearming()
        {
            var queue = new NativeSettingsDebounce();
            var pause = new NativeWritePause();
            pause.TryBegin(true, true);
            queue.Schedule(new Ab9NativeSettings { Friction = 5 }, 0);
            Ab9NativeSettings first;
            Assert.True(queue.TryTake(500, true, out first));
            queue.Schedule(new Ab9NativeSettings { Friction = 15 }, 600);
            queue.Schedule(new Ab9NativeSettings { Friction = 25 }, 700);
            pause.CancelResume(); // Panic while the first transaction is awaiting readback.
            Ab9NativeSettings followup;
            Assert.False(queue.TryTake(1000, true, out followup));
            Assert.True(queue.TryTake(1200, true, out followup));
            Assert.Equal(5, first.Friction);
            Assert.Equal(25, followup.Friction);
            Assert.False(queue.Pending);
            Assert.True(pause.Active);
            Assert.False(pause.CanResume(true, true));
        }

        [Fact]
        public void CancellingAnOutgoingProfileOrFailedBatchDiscardsQueuedWrites()
        {
            var queue = new NativeSettingsDebounce();
            queue.Schedule(new Ab9NativeSettings { Spring = 80 }, 0);
            queue.Cancel();
            Ab9NativeSettings write;
            Assert.False(queue.TryTake(5000, true, out write));
            queue.Schedule(new Ab9NativeSettings { Spring = 10 }, 5100);
            Assert.True(queue.TryTake(5600, true, out write));
            Assert.Equal(10, write.Spring);
        }

        [Fact]
        public void TickCountWrapDoesNotLoseTheQuietPeriod()
        {
            var queue = new NativeSettingsDebounce();
            int started = int.MaxValue - 200;
            queue.Schedule(new Ab9NativeSettings(), started);
            Ab9NativeSettings write;
            Assert.False(queue.TryTake(unchecked(started + 499), true, out write));
            Assert.True(queue.TryTake(unchecked(started + 500), true, out write));
        }
    }
}
