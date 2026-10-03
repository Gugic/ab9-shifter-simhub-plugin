using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class NativeWritePauseTests
    {
        [Fact]
        public void ApplyingAndSwitchingProfilesPreservesTheRigsEnabledSwitch()
        {
            var session = new ProfileStore { SessionEnabled = true, SessionFreeStick = true };
            var outgoing = new ShifterSettings { Enabled = true };
            var incoming = new ShifterSettings { Enabled = false };
            var pause = new NativeWritePause();

            Assert.True(pause.TryBegin(true, outgoing.Enabled));
            Assert.True(pause.Active);
            Assert.True(outgoing.Enabled);
            incoming.ApplyLiveSwitches(session.SessionEnabled, session.SessionFreeStick);
            Assert.True(incoming.Enabled);
            Assert.True(incoming.FreeStick);
            Assert.True(pause.CanResume(true, incoming.Enabled));
            pause.End();
            Assert.False(pause.Active);
            Assert.True(session.SessionEnabled);
        }

        [Fact]
        public void AnOffRequestDuringTheWriteWinsOverTheEarlierEnabledState()
        {
            var settings = new ShifterSettings { Enabled = true };
            var pause = new NativeWritePause();
            pause.TryBegin(true, settings.Enabled);
            settings.Enabled = false;
            Assert.False(pause.CanResume(true, settings.Enabled));
            pause.End();

            // A later Apply while still disabled must not enable a session merely because
            // the same tune had been running before the first write.
            pause.TryBegin(true, settings.Enabled);
            Assert.False(pause.CanResume(true, settings.Enabled));
        }

        [Fact]
        public void PanicCancelsResumeBeforeItsUiUpdateArrives()
        {
            var pause = new NativeWritePause();
            pause.TryBegin(true, true);
            pause.CancelResume();
            Assert.True(pause.Active);
            Assert.False(pause.CanResume(true, true));
            pause.End();

            // Cancellation belongs to that operation. A later deliberate enable/apply works.
            Assert.True(pause.TryBegin(true, true));
            Assert.True(pause.CanResume(true, true));
        }

        [Fact]
        public void FailedReadbackCannotResumeAndSetupAlwaysStaysOff()
        {
            var pause = new NativeWritePause();
            pause.TryBegin(true, true);
            Assert.False(pause.CanResume(false, true));
            pause.End();
            pause.TryBegin(false, true);
            Assert.False(pause.CanResume(true, true));
        }

        [Fact]
        public void AnotherApplyCannotReplaceAnActiveOrCancelledPause()
        {
            var pause = new NativeWritePause();
            pause.TryBegin(true, true);
            Assert.False(pause.TryBegin(false, false));
            Assert.True(pause.CanResume(true, true));
            pause.CancelResume();
            Assert.False(pause.TryBegin(true, true));
            Assert.False(pause.CanResume(true, true));
            pause.End();
            pause.CancelResume();
            Assert.False(pause.Active);
        }
    }
}
