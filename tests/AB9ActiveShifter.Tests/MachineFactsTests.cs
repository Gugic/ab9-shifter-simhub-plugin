using AB9ActiveShifter;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The facts that belong to the rig rather than to a tune: what polarity measured, which
    /// devices are bound, how fast the loop runs.
    ///
    /// They used to be stored per profile, which meant switching profiles changed the answer -
    /// the identical mistake ProfileStore.SessionEnabled was created to fix for the live switches.
    /// Presets are what made it unmissable: they ship with polarity unmeasured on purpose, so
    /// selecting one re-armed the 10% force cap and unbound the clutch pedal as a side effect of
    /// choosing a different gate.
    /// </summary>
    public class MachineFactsTests
    {
        /// <summary>A rig that has been through calibration and has its pedals bound.</summary>
        private static ShifterSettings Calibrated()
        {
            return new ShifterSettings
            {
                PolarityConfirmed = true,
                InvertConstantX = true,
                InvertConstantY = false,
                OverallGainPct = 100,
                VJoyDeviceId = 3,
                ClutchSource = ClutchSource.Pedal,
                PedalDeviceId = "66ee1e50-4e69-11f1-8001-444553540000",
                PedalAxisIndex = 2,
                PedalRawMin = 120,
                PedalRawMax = 64000,
                PedalInvert = true
            };
        }

        [Fact]
        public void ActivatingAnUncalibratedProfileKeepsTheMeasuredPolarity()
        {
            // The reported symptom, at the layer that caused it. A preset carries
            // PolarityConfirmed = false because DefaultProfilesTests demands it, so without the
            // machine facts being stamped on, selecting one silently caps a 12 Nm base at 10% and
            // throws away the invert flags that make the gate push the right way.
            ShifterSettings preset =
                DefaultProfiles.BuildPreset(DefaultProfiles.Preset(DefaultProfiles.SevenRName)).Settings;

            Assert.False(preset.PolarityConfirmed);
            Assert.True(preset.ToEngineConfig().EffectiveGain <= 0.10 + 1e-9);

            ProfileTransfer.CopyMachineFacts(Calibrated(), preset);

            Assert.True(preset.PolarityConfirmed);
            Assert.True(preset.InvertConstantX);
            Assert.True(preset.ToEngineConfig().EffectiveGain > 0.10);
        }

        [Fact]
        public void ActivatingAProfileKeepsTheClutchBindingAndTheDeviceIds()
        {
            // The twin of the above, and the one that would have been reported next: a preset
            // knows nothing about this rig's pedals, so switching to one used to stop the clutch
            // being read at all - which silently disables the grind and the bite point with it.
            ShifterSettings preset =
                DefaultProfiles.BuildPreset(DefaultProfiles.Preset(DefaultProfiles.SevenRName)).Settings;

            ProfileTransfer.CopyMachineFacts(Calibrated(), preset);

            Assert.Equal(ClutchSource.Pedal, preset.ClutchSource);
            Assert.Equal("66ee1e50-4e69-11f1-8001-444553540000", preset.PedalDeviceId);
            Assert.Equal(2, preset.PedalAxisIndex);
            Assert.True(preset.PedalCalibrated);
            Assert.Equal(3u, preset.VJoyDeviceId);
        }

        [Fact]
        public void CopyingMachineFactsLeavesEveryTunedDialAlone()
        {
            // The property that makes this safe to do on every activation: it must move the rig's
            // answers and nothing else, or activating a profile would quietly overwrite the gate
            // you just switched to with the one you switched from.
            ShifterSettings tuned = new ShifterSettings
            {
                OverallGainPct = 73,
                WallRamp = 4321,
                EngageDepth = 19000,
                DetentHoldPct = 51,
                Pattern = GatePattern.Prnd
            };

            ProfileTransfer.CopyMachineFacts(Calibrated(), tuned);

            Assert.Equal(73, tuned.OverallGainPct);
            Assert.Equal(4321, tuned.WallRamp);
            Assert.Equal(19000, tuned.EngageDepth);
            Assert.Equal(51, tuned.DetentHoldPct);
            Assert.Equal(GatePattern.Prnd, tuned.Pattern);

            // ...while the rig's answers did move.
            Assert.True(tuned.PolarityConfirmed);
            Assert.Equal(3u, tuned.VJoyDeviceId);
        }

        [Fact]
        public void AMachineFactIsNeverAlsoATuningChange()
        {
            // The two questions have to partition, or the preset fork and the machine-fact
            // write-back would both fire for the same property: an edit would be recorded as the
            // rig's answer AND spawn a copy of the preset.
            string[] machine =
            {
                "PolarityConfirmed", "InvertConstantX", "InvertConstantY", "CalibrationForcePct",
                "VendorId", "ProductId", "VJoyDeviceId", "TickHz",
                "PedalDeviceId", "PedalAxisIndex", "PedalRawMin", "PedalRawMax",
                "PedalDeadzoneLow", "PedalDeadzoneHigh", "PedalInvert", "ClutchSource"
            };

            foreach (string name in machine)
            {
                Assert.True(ProfileTransfer.IsMachineFact(name), name + " is not read as a machine fact");
                Assert.False(ProfileTransfer.IsTuning(name), name + " reads as tuning as well");
            }

            foreach (string name in new[] { "OverallGainPct", "WallRamp", "Pattern", "CarModels" })
            {
                Assert.False(ProfileTransfer.IsMachineFact(name), name + " reads as a machine fact");
                Assert.True(ProfileTransfer.IsTuning(name), name + " is not read as tuning");
            }
        }

        [Fact]
        public void TheHexIdViewsAreNotCopiedSeparatelyFromTheNumericOnes()
        {
            // They share a backing field with the numeric ids. Copying both would write the same
            // fact twice under two names and let reflection order pick the winner - the failure
            // the *Percent views already caused once on the import path.
            Assert.False(ProfileTransfer.IsMachineFact("VendorIdHex"));
            Assert.False(ProfileTransfer.IsMachineFact("ProductIdHex"));

            ShifterSettings to = new ShifterSettings();
            ProfileTransfer.CopyMachineFacts(new ShifterSettings { VendorId = 0x346E, ProductId = 0x1000 }, to);

            Assert.Equal(0x346E, to.VendorId);
            Assert.Equal(0x1000, to.ProductId);
        }

        [Fact]
        public void TheLiveSwitchesAreNotMachineFacts()
        {
            // They describe the session, are carried by SessionEnabled/SessionFreeStick, and are
            // applied by their own call. Copying them here as well would give an activation two
            // sources of truth for whether the base is armed.
            Assert.False(ProfileTransfer.IsMachineFact("Enabled"));
            Assert.False(ProfileTransfer.IsMachineFact("FreeStick"));

            ShifterSettings to = new ShifterSettings { Enabled = false, FreeStick = false };
            ProfileTransfer.CopyMachineFacts(new ShifterSettings { Enabled = true, FreeStick = true }, to);

            Assert.False(to.Enabled);
            Assert.False(to.FreeStick);
        }

        [Fact]
        public void ARoundTripThroughTheStoreChangesNothing()
        {
            // What the plugin does on every start: adopt the rig's answers off the active profile,
            // then stamp them back on. It has to be a fixed point, or a restart would drift.
            ShifterSettings rig = Calibrated();

            ShifterSettings carrier = SettingsCloner.Clone(rig);
            ShifterSettings back = new ShifterSettings();
            ProfileTransfer.CopyMachineFacts(carrier, back);
            ProfileTransfer.CopyMachineFacts(back, carrier);

            Assert.True(carrier.PolarityConfirmed);
            Assert.True(carrier.InvertConstantX);
            Assert.False(carrier.InvertConstantY);
            Assert.Equal(rig.PedalRawMax, carrier.PedalRawMax);
            Assert.Equal(rig.VJoyDeviceId, carrier.VJoyDeviceId);
        }

        [Fact]
        public void CopyingFromNothingIsHarmless()
        {
            // The store has no machine block until the first start that writes one, and an
            // activation must not throw or blank a profile in the meantime.
            ShifterSettings tuned = Calibrated();
            ProfileTransfer.CopyMachineFacts(null, tuned);
            ProfileTransfer.CopyMachineFacts(tuned, null);

            Assert.True(tuned.PolarityConfirmed);
            Assert.Equal(3u, tuned.VJoyDeviceId);
        }
    }
}
