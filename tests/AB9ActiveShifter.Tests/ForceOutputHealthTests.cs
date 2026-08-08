using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The classification behind "is the base actually producing force", and the sentences it
    /// produces. There is no device near a test runner, which is the whole reason this lives in
    /// Core in terms of plain booleans - the wording a user acts on mid-race is testable, and
    /// FfbDevice only has to map DirectInput's flags onto it.
    /// </summary>
    public class ForceOutputHealthTests
    {
        private static ForceOutputHealth Healthy()
        {
            return ForceFeedbackHealth.Classify(false, false, false, false, false, false, true);
        }

        [Fact]
        public void AHealthyDeviceIsProducing()
        {
            Assert.Equal(ForceOutputHealth.Producing, Healthy());
            Assert.False(ForceFeedbackHealth.WorthRecovering(ForceOutputHealth.Producing));
        }

        [Fact]
        public void AnEmptyEffectListIsTheRecoverableCase()
        {
            // The signature of the base resetting its force feedback engine with the handle still
            // valid: it holds no effects any more, but nothing else is wrong. Recreating them may
            // be enough, which is why this is separated from the states that need a power cycle.
            ForceOutputHealth h = ForceFeedbackHealth.Classify(
                deviceLost: false, powerOff: false, safetyCutout: false,
                actuatorsOff: false, stoppedOrPaused: false,
                deviceSaysEmpty: true, effectsStillHeld: false);

            Assert.Equal(ForceOutputHealth.EffectsGone, h);
            Assert.True(ForceFeedbackHealth.WorthRecovering(h));
        }

        [Fact]
        public void TheEmptyFlagAloneIsNotEnoughToConvict()
        {
            // Measured on the rig: this base set DIGFFS_EMPTY and held it for forty minutes, no
            // other fault flag, while the gate produced force perfectly - the same driver that
            // reports DIGFFS_STOPPED as its resting state, which is why Idle exists. Since the
            // repair for EffectsGone is now to throw the effects away and rebuild them, believing
            // the flag on its own would have destroyed working force once a second for as long as
            // the base felt like saying it. The effects have to agree before anything is done.
            ForceOutputHealth h = ForceFeedbackHealth.Classify(
                deviceLost: false, powerOff: false, safetyCutout: false,
                actuatorsOff: false, stoppedOrPaused: false,
                deviceSaysEmpty: true, effectsStillHeld: true);

            Assert.Equal(ForceOutputHealth.Producing, h);
            Assert.False(ForceFeedbackHealth.IsFault(h));
            Assert.False(ForceFeedbackHealth.WorthRecovering(h));
        }

        [Fact]
        public void EffectsMissingWithoutTheFlagIsAlsoNotEnough()
        {
            // The other half of the same rule, and the reason it is two arguments rather than one
            // corroborated bool computed at the edge: a probe that cannot get an answer must not
            // be able to invent a fault on its own either. Both sources, or nothing happens.
            ForceOutputHealth h = ForceFeedbackHealth.Classify(
                deviceLost: false, powerOff: false, safetyCutout: false,
                actuatorsOff: false, stoppedOrPaused: false,
                deviceSaysEmpty: false, effectsStillHeld: false);

            Assert.Equal(ForceOutputHealth.Producing, h);
            Assert.False(ForceFeedbackHealth.IsFault(h));
        }

        [Fact]
        public void ActuatorsOffOutranksAnEmptyList()
        {
            // A device with its actuators off produces nothing whatever its effect list says, so
            // rebuilding effects would be busywork reported as a fix. The more fundamental fault
            // has to win, or the message sends the user after the wrong thing.
            ForceOutputHealth h = ForceFeedbackHealth.Classify(
                deviceLost: false, powerOff: false, safetyCutout: false,
                actuatorsOff: true, stoppedOrPaused: false,
                deviceSaysEmpty: true, effectsStillHeld: false);

            Assert.Equal(ForceOutputHealth.ActuatorsOff, h);
        }

        [Fact]
        public void TheMostFundamentalFaultAlwaysWins()
        {
            // Everything wrong at once: the answer must be the one that explains the others.
            Assert.Equal(ForceOutputHealth.Lost,
                ForceFeedbackHealth.Classify(true, true, true, true, true, true, false));

            Assert.Equal(ForceOutputHealth.PowerOff,
                ForceFeedbackHealth.Classify(false, true, true, true, true, true, false));

            Assert.Equal(ForceOutputHealth.SafetyCutout,
                ForceFeedbackHealth.Classify(false, false, true, true, true, true, false));
        }

        [Fact]
        public void PlayingNoEffectsIsNotAFault()
        {
            // Measured on the rig: the base sets this whenever the gate is demanding nothing,
            // which in neutral is most of the time. Treating it as a fault put a warning in the
            // log 96 ms after every startup - a detector that cries wolf is worse than none.
            ForceOutputHealth h = ForceFeedbackHealth.Classify(
                deviceLost: false, powerOff: false, safetyCutout: false,
                actuatorsOff: false, stoppedOrPaused: true,
                deviceSaysEmpty: false, effectsStillHeld: true);

            Assert.Equal(ForceOutputHealth.Idle, h);
            Assert.False(ForceFeedbackHealth.IsFault(h));
            Assert.False(ForceFeedbackHealth.WorthRecovering(h));
        }

        [Fact]
        public void EveryUnhealthyStateTellsTheUserWhatToDo()
        {
            // These are read by someone who has just lost their gearbox. Each one has to say
            // whether to change something in the plugin or to reach for the power switch -
            // "force feedback unavailable" would be true and useless.
            foreach (ForceOutputHealth h in new[]
                     {
                         ForceOutputHealth.ActuatorsOff, ForceOutputHealth.PowerOff,
                         ForceOutputHealth.EffectsGone
                     })
            {
                Assert.True(ForceFeedbackHealth.IsFault(h), h + " should be a fault");

                string text = ForceFeedbackHealth.Describe(h);
                Assert.Contains("power", text.ToLowerInvariant());
                Assert.EndsWith(".", text.Trim());
            }
        }

        [Fact]
        public void RecoveryIsNeverAttemptedForSomethingOnlyAHumanCanFix()
        {
            // Sending SetActuatorsOn to a base whose power is off, or whose safety switch is
            // open, is noise that would also make the log look like the plugin was flailing.
            Assert.False(ForceFeedbackHealth.WorthRecovering(ForceOutputHealth.PowerOff));
            Assert.False(ForceFeedbackHealth.WorthRecovering(ForceOutputHealth.SafetyCutout));
            Assert.False(ForceFeedbackHealth.WorthRecovering(ForceOutputHealth.Lost));
            Assert.False(ForceFeedbackHealth.WorthRecovering(ForceOutputHealth.Unknown));
        }
    }
}
