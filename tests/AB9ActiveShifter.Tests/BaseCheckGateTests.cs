using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The rule that an AB9 is read before it is opened. The plugin used to check the base's mode
    /// once per SimHub start, so a base switched off and on mid-session was reopened unread - and a
    /// power-up read on 2026-10-25 found it in native H-pattern mode, where the gate's forces would
    /// land on the firmware's own gate. These pin the two halves: nothing opens on a stale or
    /// missing read, and a waiting engine keeps asking without asking every tick.
    /// </summary>
    public class BaseCheckGateTests
    {
        [Fact]
        public void NothingOpensBeforeTheFirstRead()
        {
            // The very first open of a session waits like any other. A gate that started open
            // would let the one unread open through - the case the startup check used to cover
            // alone, and missed whenever SimHub was already running.
            var gate = new BaseCheckGate();

            Assert.False(gate.MayOpen(0));
            Assert.False(gate.MayOpen(100000));
        }

        [Fact]
        public void AReadVouchesForTheBaseOnlyBriefly()
        {
            var gate = new BaseCheckGate();
            gate.Checked(10000);

            Assert.True(gate.MayOpen(10000));
            Assert.True(gate.MayOpen(10000 + BaseCheckGate.FreshForMs));

            // An old read says nothing about the base now: it may have been switched off and on
            // since, which is exactly what the startup check could not see.
            Assert.False(gate.MayOpen(10000 + BaseCheckGate.FreshForMs + 1));
        }

        [Fact]
        public void TheWindowIsShorterThanAPowerCycle()
        {
            // The window is only sound if no power cycle fits inside it. Off, on and back on USB
            // took 13 s on the rig; a window that grew past that could vouch for a base that had
            // rebooted into native mode after the read.
            Assert.True(BaseCheckGate.FreshForMs < 13000);
        }

        [Fact]
        public void ANewerReadRenewsTheWindow()
        {
            var gate = new BaseCheckGate();
            gate.Checked(0);
            Assert.False(gate.MayOpen(10000));

            gate.Checked(9000);

            Assert.True(gate.MayOpen(10000));
        }

        [Fact]
        public void AReadStampedAfterNowWaitsForTheNextLook()
        {
            // The plugin stamps its read on another thread, so it can land a tick after the
            // engine took its own time. Waiting one more 25 ms look is the safe answer; treating
            // a negative age as fresh would also accept any stamp at all after a clock wrap.
            var gate = new BaseCheckGate();
            gate.Checked(5001);

            Assert.False(gate.MayOpen(5000));
            Assert.True(gate.MayOpen(5001));
        }

        [Fact]
        public void TheTickCountWrapIsHarmless()
        {
            // Environment.TickCount wraps every 25 days, and SimHub runs for weeks.
            var gate = new BaseCheckGate();
            gate.Checked(int.MaxValue - 500);

            Assert.True(gate.MayOpen(unchecked(int.MaxValue + 1000)));
            Assert.False(gate.MayOpen(unchecked(int.MaxValue + BaseCheckGate.FreshForMs + 1000)));
        }

        [Fact]
        public void AWaitingEngineAsksAtOnceThenEveryFewSeconds()
        {
            // The engine looks every 25 ms while it waits. Asking on every look would put a
            // configuration read on the port forty times a second; asking once would strand it
            // when that read found no base because the base was still off.
            var gate = new BaseCheckGate();

            Assert.True(gate.ShouldAsk(0));
            Assert.False(gate.ShouldAsk(25));
            Assert.False(gate.ShouldAsk(BaseCheckGate.AskAgainMs - 1));
            Assert.True(gate.ShouldAsk(BaseCheckGate.AskAgainMs));
            Assert.False(gate.ShouldAsk(BaseCheckGate.AskAgainMs + 25));
        }

        [Fact]
        public void AnOpenLetsTheNextWaitAskAtOnce()
        {
            // A base lost a second after it was opened should be read straight away, not after
            // the remainder of a wait that belonged to the previous connection.
            var gate = new BaseCheckGate();
            Assert.True(gate.ShouldAsk(0));

            gate.Opened();

            Assert.True(gate.ShouldAsk(500));
        }

        [Fact]
        public void AskingNeverOpensAndOpeningNeverReads()
        {
            // The two halves stay separate: only a read that found flight mode opens the gate.
            var gate = new BaseCheckGate();

            gate.ShouldAsk(0);
            gate.Opened();

            Assert.False(gate.MayOpen(0));
        }
    }
}
