using System.Collections.Generic;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Output;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class GearOutputConnectionTests
    {
        private sealed class Output : IGearOutput
        {
            public readonly List<string> Calls;
            private readonly string _name;
            public Output(List<string> calls = null, string name = "output") { Calls = calls ?? new List<string>(); _name = name; }
            public bool IsConnected { get; private set; }
            public string LastError { get { return "unavailable"; } }
            public bool CanConnect = true;
            public bool OwnsDevice = true;
            public int Connects;
            public int Checks;
            public int Held;
            public bool Connect()
            {
                Connects++;
                Calls.Add(_name + " connect");
                IsConnected = CanConnect;
                if (IsConnected) OwnsDevice = true;
                return IsConnected;
            }
            public bool CheckConnection() { Checks++; IsConnected = OwnsDevice; return IsConnected; }
            public void SetGear(int gear) { Held = gear; Calls.Add(_name + " gear " + gear); }
            public void SetButton(int button, bool down) { }
            public void ReleaseAll() { Held = 0; Calls.Add(_name + " clear"); }
            public void Disconnect() { ReleaseAll(); IsConnected = false; Calls.Add(_name + " disconnect"); }
        }

        [Fact]
        public void AnAbsentBaseDoesNotDelayAcquisitionOrLoseTheSelectedDevice()
        {
            var output = new Output();
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            Assert.True(connection.Poll(0, false, 7));
            Assert.True(output.IsConnected);
            Assert.Equal(0, output.Held);
            output.SetGear(7);
            output.ReleaseAll(); // base loss clears buttons without relinquishing ownership
            for (int ms = 25; ms <= 5000; ms += 25) connection.Poll(ms, false, 7);
            Assert.Same(output, connection.Output);
            Assert.True(output.IsConnected);
            Assert.Equal(1, output.Connects);
            Assert.Equal(0, output.Held);
            Assert.DoesNotContain("output disconnect", output.Calls);
        }

        [Fact]
        public void LostOwnershipIsReacquiredAndAFreshGearIsRepublishedWithoutASettingsEdit()
        {
            var output = new Output();
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            connection.Poll(0, true, 3);
            output.OwnsDevice = false;
            Assert.True(connection.Poll(1000, true, 3));
            Assert.Equal(2, output.Connects);
            Assert.True(output.IsConnected);
            Assert.Equal(3, output.Held);
            Assert.Equal(new[] { "output connect", "output gear 3", "output connect", "output gear 3" }, output.Calls);
        }

        [Theory]
        [InlineData(0)] // optional H neutral may be a real keyboard role
        [InlineData(11)] // PRND always has a position, even without a sample
        public void RecoveryWithoutAFreshBasePositionNeverPressesAnyRole(int held)
        {
            var output = new Output();
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            connection.Poll(0, false, held);
            output.OwnsDevice = false;
            connection.Poll(1000, false, held);
            Assert.Equal(new[] { "output connect", "output connect" }, output.Calls);
        }

        [Fact]
        public void OwnershipChecksAreBoundedRatherThanQueriesOnEveryForceTick()
        {
            var output = new Output();
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            connection.Poll(0, false, 0);
            for (int ms = 1; ms < 1000; ms++) connection.Poll(ms, false, 0);
            Assert.Equal(0, output.Checks);
            connection.Poll(1000, false, 0);
            Assert.Equal(1, output.Checks);
            connection.Poll(1999, false, 0);
            Assert.Equal(1, output.Checks);
            connection.Poll(2000, false, 0);
            Assert.Equal(2, output.Checks);
            Assert.Equal(1, output.Connects);
        }

        [Fact]
        public void FailedAcquisitionUsesTheSameBackoffWhileTheBaseIsAbsent()
        {
            var output = new Output { CanConnect = false };
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            int[] attempts = { 0, 1000, 3000, 8000, 23000, 38000 };
            for (int ms = 0; ms <= 38000; ms++) connection.Poll(ms, false, 0);
            Assert.Equal(attempts.Length, output.Connects);
            output.CanConnect = true;
            Assert.False(connection.Poll(52999, false, 0));
            Assert.True(connection.Poll(53000, false, 0));
        }

        [Fact]
        public void RestartingTheEngineClockCannotLeaveAFormerDeadlineInTheFuture()
        {
            var output = new Output();
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            connection.Poll(100000, false, 0);
            output.OwnsDevice = false;
            connection.RestartClock();
            Assert.True(connection.Poll(0, false, 0));
            Assert.Equal(2, output.Connects);
            output.OwnsDevice = false;
            output.CanConnect = false;
            connection.Poll(1000, false, 0);
            connection.RestartClock();
            output.CanConnect = true;
            Assert.True(connection.Poll(0, false, 0));
        }

        [Fact]
        public void NativeConfigurationPausesDoNotReplaceOrRelinquishTheOutput()
        {
            var output = new Output();
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            connection.Poll(0, false, 0);
            Assert.False(connection.Configure(new EngineConfig { VirtualDeviceEnabled = false }));
            connection.Poll(1000, false, 0);
            Assert.False(connection.Configure(new EngineConfig { VirtualDeviceEnabled = true }));
            Assert.Equal(1, output.Connects);
            Assert.Same(output, connection.Output);
            Assert.True(output.IsConnected);
            Assert.DoesNotContain("output disconnect", output.Calls);
        }

        [Fact]
        public void ChangingTheSelectedDeviceReleasesTheOldOneBeforeAcquiringItsReplacement()
        {
            var calls = new List<string>();
            var old = new Output(calls, "old");
            var next = new Output(calls, "new");
            var connection = new GearOutputConnection(cfg => cfg.VJoyDeviceId == 1 ? old : next);
            connection.Configure(new EngineConfig());
            connection.Poll(0, true, 2);
            connection.Configure(new EngineConfig { VJoyDeviceId = 2 });
            connection.Poll(1, true, 2);
            Assert.Equal(new[] { "old connect", "old gear 2", "old clear", "old disconnect", "new connect", "new gear 2" }, calls);
            Assert.False(old.IsConnected);
            Assert.True(next.IsConnected);
        }

        [Fact]
        public void ExplicitDisconnectClearsHeldButtonsAndReleasesOwnership()
        {
            var output = new Output();
            var connection = new GearOutputConnection(cfg => output);
            connection.Configure(new EngineConfig());
            connection.Poll(0, true, 4);
            connection.Disconnect();
            Assert.Null(connection.Output);
            Assert.Equal(0, output.Held);
            Assert.False(output.IsConnected);
            Assert.Equal(new[] { "output connect", "output gear 4", "output clear", "output disconnect" }, output.Calls);
        }

        [Theory]
        [InlineData(OperatingMode.GenericFfbStick, false, true)]
        [InlineData(OperatingMode.Ab9Native, false, true)]
        [InlineData(OperatingMode.Ab9HPattern, false, false)]
        [InlineData(OperatingMode.GenericFfbStick, true, false)]
        [InlineData(OperatingMode.Ab9Native, true, false)]
        public void FirmwareHPatternOwnsItsButtonsAndReleasesPluginOutput(OperatingMode mode, bool hardwareHPattern, bool expected)
        {
            Assert.Equal(expected, NativeProfilePolicy.CanOwnGearOutput(mode,
                Ab9NativeProtocol.VendorId, Ab9NativeProtocol.ProductId, hardwareHPattern));
        }

        [Fact]
        public void AnAb9FirmwareObservationCannotBlockAnUnrelatedGenericStick()
        {
            Assert.True(NativeProfilePolicy.CanOwnGearOutput(OperatingMode.GenericFfbStick, 123, 456, true));
        }
    }
}
