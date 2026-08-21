using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Which HRESULTs mean "another program has the base", because acting on that answer is the
    /// difference between standing down and crashing the user's game.
    ///
    /// The numbers are SharpDX's own `ResultCode` constants, read off the shipped 4.2.0 assembly
    /// rather than remembered. Two of them are worth pinning for that reason alone:
    /// OtherApplicationHasPriority is E_ACCESSDENIED, which nobody would guess, and
    /// NotExclusiveAcquired is the one this rig actually logs when Forza or Wreckfest takes the
    /// device - the whole reason this classifier exists.
    /// </summary>
    public class DeviceFaultTests
    {
        [Theory]
        [InlineData(unchecked((int)0x80040205))] // DIERR_NOTEXCLUSIVEACQUIRED
        [InlineData(unchecked((int)0x80070005))] // DIERR_OTHERAPPHASPRIO, which is E_ACCESSDENIED
        public void ARevokedOrRefusedExclusiveGrabMeansSomebodyElseHasIt(int hresult)
        {
            Assert.Equal(DeviceFault.TakenByAnotherApp, DeviceFaults.Classify(hresult));
        }

        [Theory]
        [InlineData(unchecked((int)0x8007048F))] // ERROR_DEVICE_NOT_CONNECTED, what this base logs
        [InlineData(unchecked((int)0x80040209))] // DIERR_UNPLUGGED
        public void ADeviceOffTheBusIsGoneRatherThanTaken(int hresult)
        {
            Assert.Equal(DeviceFault.Gone, DeviceFaults.Classify(hresult));
        }

        [Theory]
        [InlineData(unchecked((int)0x8007001E))] // DIERR_INPUTLOST
        [InlineData(unchecked((int)0x8007000C))] // DIERR_NOTACQUIRED
        public void LosingInputSaysNothingAboutWhoTookIt(int hresult)
        {
            // Deliberately Unknown. A focus change and a pulled cable produce the same code, so
            // this keeps the old behaviour - re-acquire once - and it is the failure of THAT
            // attempt that gets classified. Reading these as "taken" would stand the gate down
            // every time the window lost focus.
            Assert.Equal(DeviceFault.Unknown, DeviceFaults.Classify(hresult));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(unchecked((int)0x80004005))] // E_FAIL
        [InlineData(unchecked((int)0x80040203))] // DIERR_NOTDOWNLOADED
        [InlineData(unchecked((int)0x80070057))] // E_INVALIDARG
        public void AnythingElseIsUnknownRatherThanGuessedAt(int hresult)
        {
            Assert.Equal(DeviceFault.Unknown, DeviceFaults.Classify(hresult));
        }

        [Fact]
        public void TheConstantsAreTheOnesTheClassifierActuallyUses()
        {
            // Cheap, and it catches the one edit that would silently disarm all of this: changing
            // a constant without changing the switch beside it.
            Assert.Equal(DeviceFault.TakenByAnotherApp,
                DeviceFaults.Classify(DeviceFaults.NotExclusiveAcquired));
            Assert.Equal(DeviceFault.TakenByAnotherApp,
                DeviceFaults.Classify(DeviceFaults.OtherApplicationHasPriority));
            Assert.Equal(DeviceFault.Gone, DeviceFaults.Classify(DeviceFaults.DeviceNotConnected));
            Assert.Equal(DeviceFault.Gone, DeviceFaults.Classify(DeviceFaults.Unplugged));
        }
    }
}
