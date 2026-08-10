using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The lines the vJoy device picker shows. There is no driver anywhere near a test runner -
    /// the wrapper is a 32-bit native assembly - which is exactly why the state is restated in
    /// Core and mapped at the edge: the wording, which is the part a user acts on, stays testable.
    /// </summary>
    public class VJoyDeviceInfoTests
    {
        private static VJoyDeviceInfo Device(uint id, VJoyDeviceState state, int buttons)
        {
            return new VJoyDeviceInfo { Id = id, State = state, Buttons = buttons };
        }

        [Fact]
        public void AUsableDeviceSaysItsSizeAndThatItIsFree()
        {
            Assert.Equal("Device 1 - 32 buttons, free",
                Device(1, VJoyDeviceState.Free, 32).Describe());
        }

        [Fact]
        public void ADeviceTooSmallSaysSoRatherThanLookingFine()
        {
            // The trap this picker exists to remove: 8 buttons covers every gear and silently
            // drops sequential, which a user discovers as "up/downshift does nothing in game".
            VJoyDeviceInfo small = Device(2, VJoyDeviceState.Free, 8);

            Assert.Equal("Device 2 - 8 buttons, free (needs 14)", small.Describe());
            Assert.False(small.Usable);
        }

        [Fact]
        public void ExactlyEnoughButtonsIsEnough()
        {
            VJoyDeviceInfo exact = Device(3, VJoyDeviceState.Free, VJoyDeviceInfo.ButtonsNeeded);

            Assert.True(exact.Usable);
            Assert.DoesNotContain("needs", exact.Describe());
        }

        [Fact]
        public void ADeviceThatDoesNotExistSaysThatAndNothingElse()
        {
            VJoyDeviceInfo missing = Device(4, VJoyDeviceState.Missing, 0);

            Assert.Equal("Device 4 - not created", missing.Describe());
            Assert.False(missing.Exists);
            Assert.False(missing.Usable);
        }

        [Fact]
        public void ADeviceHeldElsewhereNamesTheProgramWhenItCan()
        {
            VJoyDeviceInfo busy = Device(1, VJoyDeviceState.Busy, 32);
            busy.OwnerPid = 4321;
            busy.OwnerName = "SomeFeeder";
            Assert.Equal("Device 1 - 32 buttons, in use by SomeFeeder", busy.Describe());

            // ...and falls back to the pid, then to nothing, rather than printing an empty name.
            busy.OwnerName = null;
            Assert.Equal("Device 1 - 32 buttons, in use by pid 4321", busy.Describe());

            busy.OwnerPid = 0;
            Assert.Equal("Device 1 - 32 buttons, in use by another program", busy.Describe());

            Assert.False(busy.Usable);
        }

        [Fact]
        public void ADeviceThisPluginAlreadyHoldsIsUsable()
        {
            // Re-entering the settings page while the engine runs must not report our own device
            // as taken, or the picker would tell a working setup it is broken.
            VJoyDeviceInfo ours = Device(1, VJoyDeviceState.Owned, 32);

            Assert.True(ours.Usable);
            Assert.Contains("in use by this plugin", ours.Describe());
        }

        [Fact]
        public void AnUnknownStateIsNotOfferedAsWorking()
        {
            VJoyDeviceInfo unknown = Device(9, VJoyDeviceState.Unknown, 0);

            Assert.Equal("Device 9 - unavailable", unknown.Describe());
            Assert.False(unknown.Usable);
        }
    }
}
