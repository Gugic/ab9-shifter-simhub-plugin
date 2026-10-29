using AB9ActiveShifter.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class DeviceReconnectTests
    {
        [Fact]
        public void AutomaticRetakingDefaultsOnForExistingSettingsToo()
        {
            Assert.True(new EngineConfig().AutoRetakeBase);
            var settings = JsonConvert.DeserializeObject<ShifterSettings>("{\"TickHz\":1000}");
            Assert.True(settings.AutoRetakeBase);
            Assert.True(settings.ToEngineConfig().AutoRetakeBase);
            settings.AutoRetakeBase = false;
            var restored = JsonConvert.DeserializeObject<ShifterSettings>(JsonConvert.SerializeObject(settings));
            Assert.False(restored.ToEngineConfig().AutoRetakeBase);
        }

        [Fact]
        public void AProfileCannotChangeTheRigsRecoveryPreference()
        {
            var rig = new ShifterSettings { AutoRetakeBase = false };
            var preset = DefaultProfiles.BuildPreset(DefaultProfiles.Preset(DefaultProfiles.SevenRName));
            ProfileTransfer.CopyMachineFacts(rig, preset.Settings);
            Assert.False(preset.Settings.AutoRetakeBase);
            Assert.True(ProfileTransfer.IsMachineFact(nameof(ShifterSettings.AutoRetakeBase)));
            Assert.False(ProfileTransfer.IsTuning(nameof(ShifterSettings.AutoRetakeBase)));
            string file = ProfileTransfer.Export(preset);
            Assert.DoesNotContain("AutoRetakeBase", file);
            var doctored = JObject.Parse(file);
            ((JObject)doctored["Settings"])["AutoRetakeBase"] = true;
            Assert.False(ProfileTransfer.Import(doctored.ToString(), rig).Profile.Settings.AutoRetakeBase);
        }

        [Fact]
        public void RetakingIsAllowedDuringAGameButNeverOnTheLossTick()
        {
            var policy = new DeviceReconnectPolicy();
            policy.DeviceLost(DeviceFault.TakenByAnotherApp, 100);
            for (int now = 100; now < 1100; now++)
                Assert.False(policy.CanAttemptOpen(now, true, true, 0));
            Assert.True(policy.CanAttemptOpen(1100, true, true, 0));
            Assert.False(policy.WaitingForGame(true));
        }

        [Fact]
        public void FailedOpensBackOffEvenWhenTheGameKeepsPriority()
        {
            var policy = new DeviceReconnectPolicy();
            Assert.True(policy.CanAttemptOpen(0, true, true, 0));
            policy.OpenFailed(DeviceFault.TakenByAnotherApp, 0);
            Assert.False(policy.CanAttemptOpen(999, true, true, 0));
            Assert.True(policy.CanAttemptOpen(1000, true, true, 0));
            policy.OpenFailed(DeviceFault.TakenByAnotherApp, 1000);
            Assert.False(policy.CanAttemptOpen(2999, true, true, 0));
            Assert.True(policy.CanAttemptOpen(3000, true, true, 0));
            policy.OpenFailed(DeviceFault.TakenByAnotherApp, 3000);
            Assert.False(policy.CanAttemptOpen(7999, true, true, 0));
            Assert.True(policy.CanAttemptOpen(8000, true, true, 0));
            policy.OpenFailed(DeviceFault.Unknown, 8000);
            Assert.False(policy.CanAttemptOpen(12999, true, true, 0));
            Assert.True(policy.CanAttemptOpen(13000, true, true, 0));
        }

        [Fact]
        public void RepeatedSuccessfulTakeoversStillLeaveAFullDelayBeforeTheNextOpen()
        {
            var policy = new DeviceReconnectPolicy();
            for (int loss = 0; loss < 5000; loss += 1000)
            {
                policy.Reset(); // A successful open.
                policy.DeviceLost(DeviceFault.TakenByAnotherApp, loss);
                Assert.False(policy.CanAttemptOpen(loss + 999, true, true, 0));
                Assert.True(policy.CanAttemptOpen(loss + 1000, true, true, 0));
            }
        }

        [Fact]
        public void OptingOutWaitsForTheGameAndDoesNotMistakeStartupForExit()
        {
            var policy = new DeviceReconnectPolicy();
            policy.DeviceLost(DeviceFault.TakenByAnotherApp, 0);
            Assert.True(policy.WaitingForGame(false));
            Assert.False(policy.CanAttemptOpen(4999, false, false, 0));
            Assert.False(policy.CanAttemptOpen(5000, false, true, 0));
            Assert.False(policy.CanAttemptOpen(19999, false, false, 0));
            Assert.False(policy.CanAttemptOpen(20000, false, true, 0));
            Assert.False(policy.CanAttemptOpen(49999, false, false, 0));
            Assert.True(policy.CanAttemptOpen(50000, false, false, 0));
            Assert.False(policy.WaitingForGame(false));
        }

        [Theory]
        [InlineData(2000, false)]
        [InlineData(2001, true)]
        [InlineData(-1, true)]
        public void ConservativeRecoveryKeepsTheExistingTelemetryFreshnessRule(int age, bool allowed)
        {
            var policy = new DeviceReconnectPolicy();
            policy.DeviceLost(DeviceFault.TakenByAnotherApp, 0);
            Assert.Equal(allowed, policy.CanAttemptOpen(5000, false, true, age));
        }

        [Fact]
        public void ThePreferenceCanChangeWhileDisconnectedWithoutRestartingTheEngine()
        {
            var policy = new DeviceReconnectPolicy();
            policy.DeviceLost(DeviceFault.TakenByAnotherApp, 0);
            Assert.False(policy.CanAttemptOpen(1000, false, true, 0));
            Assert.True(policy.CanAttemptOpen(1000, true, true, 0));
            Assert.False(policy.CanAttemptOpen(1000, false, true, 0));
            Assert.True(policy.WaitingForGame(false));
        }

        [Theory]
        [InlineData(DeviceFault.Gone)]
        [InlineData(DeviceFault.Unknown)]
        public void OptingOutStillReconnectsAnUnpluggedOrOtherwiseLostBase(DeviceFault fault)
        {
            var policy = new DeviceReconnectPolicy();
            policy.DeviceLost(fault, 0);
            Assert.False(policy.CanAttemptOpen(999, false, true, 0));
            Assert.True(policy.CanAttemptOpen(1000, false, true, 0));
        }

        [Fact]
        public void AnInternalNativeWritePauseCannotOverrideTheOptOut()
        {
            var policy = new DeviceReconnectPolicy();
            policy.DeviceLost(DeviceFault.TakenByAnotherApp, 100000);
            policy.RestartClock(true);
            Assert.True(policy.WaitingForGame(false));
            Assert.False(policy.CanAttemptOpen(4999, false, false, 0));
            Assert.False(policy.CanAttemptOpen(5000, false, true, 0));
            policy.RestartClock(false); // Master off/on deliberately restarts acquisition.
            Assert.True(policy.CanAttemptOpen(0, false, true, 0));
        }

        [Fact]
        public void AnInitialPriorityRefusalAlsoHonorsTheOptOutAndAnExplicitRestartClearsIt()
        {
            var policy = new DeviceReconnectPolicy();
            policy.OpenFailed(DeviceFault.TakenByAnotherApp, 100000);
            Assert.False(policy.CanAttemptOpen(105000, false, true, 0));
            policy.Reset(); // New RunLoop uses a clock starting at zero.
            Assert.False(policy.TakeoverPending);
            Assert.True(policy.CanAttemptOpen(0, false, true, 0));
        }
    }
}
