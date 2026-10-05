using System;
using System.Collections.Generic;
using AB9ActiveShifter.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class OperatingModeTests
    {
        [Theory]
        [InlineData(OperatingMode.GenericFfbStick, "Generic FFB stick")]
        [InlineData(OperatingMode.Ab9Native, "Moza AB9")]
        [InlineData(OperatingMode.Ab9HPattern, "Moza AB9 native H-Pattern")]
        public void EveryModeCanBeSavedWithoutConnectedHardwareOrAnEnabledSession(OperatingMode mode, string label)
        {
            var settings = new ShifterSettings { OverallGainPct = 71, SlotOvertravel = 8123 };
            var store = new ProfileStore
            {
                ActiveProfile = "Tune",
                SessionEnabled = false,
                Profiles = new List<ShifterProfile> { new ShifterProfile { Name = "Tune", Settings = settings } }
            };
            Assert.True(store.SelectOperatingMode(mode));
            Assert.Equal(mode, store.SelectedOperatingMode);
            Assert.Equal(mode == OperatingMode.Ab9Native, store.Ab9PreparationRequired);
            Assert.Equal(label, NativeProfilePolicy.ModeLabel(mode));
            Assert.Same(settings, store.FindActive().Settings);
            Assert.Equal(71, settings.OverallGainPct);
            Assert.Equal(8123, settings.SlotOvertravel);
            Assert.False(store.SetupCompleted);
            Assert.False(store.SessionEnabled);
            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<ProfileStore>(
                Newtonsoft.Json.JsonConvert.SerializeObject(store));
            Assert.Equal(mode, restored.SelectedOperatingMode);
            Assert.Equal(store.Ab9PreparationRequired, restored.Ab9PreparationRequired);
            store.SessionEnabled = true;
            Assert.True(store.SelectOperatingMode(mode));
            Assert.True(store.SessionEnabled);
            if (mode != OperatingMode.GenericFfbStick)
                Assert.False(NativeProfilePolicy.CanRunVirtual(mode, 0x346E, 0x1000, null, false));
        }

        [Fact]
        public void AnUnknownModeCannotReplaceTheSavedChoice()
        {
            var store = new ProfileStore { SelectedOperatingMode = OperatingMode.Ab9Native, Ab9PreparationRequired = true };
            Assert.False(store.SelectOperatingMode((OperatingMode)99));
            Assert.Equal(OperatingMode.Ab9Native, store.SelectedOperatingMode);
            Assert.True(store.Ab9PreparationRequired);
        }

        [Fact]
        public void SelectingTheAb9ProviderWaitsForVerifiedPreparationBeforeRunning()
        {
            var flight = new Ab9NativeSnapshot("flight", "COM11", new Version(1, 1, 5, 2), 0, new Ab9NativeSettings(), true);
            Assert.False(NativeProfilePolicy.CanRunVirtual(OperatingMode.Ab9Native, 0x346E, 0x1000, flight, false, true));
            Assert.True(NativeProfilePolicy.CanRunVirtual(OperatingMode.Ab9Native, 0x346E, 0x1000, flight, false, false));
            Assert.True(NativeProfilePolicy.CanRunVirtual(OperatingMode.GenericFfbStick, 0x1234, 0x5678, null, false, true));
        }

        [Fact]
        public void ExperimentalFirmwareProfilesAreDiscardedWithoutChangingGenericTunes()
        {
            var tune = new ShifterSettings { OverallGainPct = 71, SlotOvertravel = 8123, PolarityConfirmed = true };
            var store = new ProfileStore
            {
                ActiveProfile = "Generic",
                Profiles = new List<ShifterProfile>
                {
                    new ShifterProfile { Name = "Generic", Settings = tune },
                    new ShifterProfile { Name = "Experimental", Settings = new ShifterSettings { Ab9NativeProfile = true } }
                }
            };
            store.MigrateOperatingMode();
            Assert.Equal(OperatingMode.GenericFfbStick, store.SelectedOperatingMode);
            Assert.True(store.SetupCompleted);
            Assert.Single(store.Profiles);
            Assert.Same(tune, store.FindActive().Settings);
            Assert.Equal(71, tune.OverallGainPct);
            Assert.Equal(8123, tune.SlotOvertravel);
            store.SelectedOperatingMode = OperatingMode.Ab9Native;
            store.MigrateOperatingMode();
            Assert.Equal(OperatingMode.Ab9Native, store.SelectedOperatingMode);
            Assert.Single(store.Profiles);
        }

        [Fact]
        public void OneProfileUsesTheSameBasePercentagesWithBothProviders()
        {
            var settings = new ShifterSettings
            {
                BaseSpringPct = 22,
                BaseDamperPct = 15,
                BaseFrictionPct = 33,
                BaseInertiaPct = 44,
                NativeTorquePct = 78,
                NativeOverallIntensityPct = 87,
                NativeGameGainPct = 96,
                PolarityConfirmed = true,
                DampingPct = 47,
                WallFrictionPct = 13,
                OverallGainPct = 72
            };
            var directInput = settings.ToEngineConfig();
            var onboard = settings.ToNativeSettings();
            Assert.Equal(directInput.BaseSpringPct, onboard.Spring);
            Assert.Equal(directInput.DamperCoeff / 100, onboard.Damper);
            Assert.Equal(directInput.BaseFrictionPct, onboard.Friction);
            Assert.Equal(directInput.BaseInertiaPct, onboard.Inertia);
            Assert.Equal(78, onboard.Torque);
            Assert.Equal(87, onboard.OverallIntensity);
            Assert.Equal(96, onboard.GameGain);
            Assert.Equal(1, onboard.FfbMode);
            Assert.Equal(47, directInput.DampingPct);
            Assert.Equal(13, directInput.WallFrictionPct);
            Assert.Equal(72, directInput.OverallGainPct);
            Assert.Equal(8, new ShifterSettings().ToNativeSettings().Damper);
            var exactClone = SettingsCloner.Clone(new ShifterSettings { DamperCoeff = 1234 });
            Assert.Equal(1234, exactClone.DamperCoeff);
        }

        [Fact]
        public void UnconfirmedPolarityCannotApplyAnOnboardSpringOrLiftHardwareTorquePastTenPercent()
        {
            var settings = new ShifterSettings { NativeTorquePct = 100, BaseSpringPct = 88, OverallGainPct = 100 };
            var onboard = settings.ToNativeSettings();
            Assert.Equal(0, onboard.Spring);
            Assert.Equal(10, onboard.Torque);
            Assert.Equal(0.1, settings.ToEngineConfig().EffectiveGain);
            Assert.Equal(88, settings.BaseSpringPct);
            Assert.Equal(100, settings.NativeTorquePct);
            settings.PolarityConfirmed = true;
            Assert.Equal(88, settings.ToNativeSettings().Spring);
            Assert.Equal(100, settings.ToNativeSettings().Torque);
        }

        [Fact]
        public void FirmwareHPatternHasNeitherVirtualTuningNorActivatableProfiles()
        {
            var flight = new Ab9NativeSnapshot("flight", "COM11", new Version(1, 1, 5, 2), 0, new Ab9NativeSettings(), true);
            Assert.False(NativeProfilePolicy.CanActivate(OperatingMode.Ab9HPattern));
            Assert.False(NativeProfilePolicy.CanRunVirtual(OperatingMode.Ab9HPattern, 0x346E, 0x1000, flight, false));
            Assert.False(NativeProfilePolicy.CanActivate((OperatingMode)99));
        }

        [Fact]
        public void FreshSpringPolarityIsMachineOwnedAndNeverArrivesInAnImportedProfile()
        {
            var source = new ShifterSettings { PolarityConfirmed = true, BaseSpringPolarityConfirmed = true, InvertSpringY = true };
            var json = ProfileTransfer.Export(new ShifterProfile { Name = "Tune", Settings = source });
            Assert.DoesNotContain("BaseSpringPolarityConfirmed", json);
            Assert.DoesNotContain("InvertSpringY", json);
            Assert.False(ProfileTransfer.Import(json, new ShifterSettings { PolarityConfirmed = true }).Profile.Settings.BaseSpringPolarityConfirmed);
            var copy = new ShifterSettings();
            ProfileTransfer.CopyMachineFacts(source, copy);
            Assert.True(copy.BaseSpringPolarityConfirmed);
            Assert.True(copy.InvertSpringY);
            Assert.True(ProfileTransfer.IsMachineFact(nameof(ShifterSettings.BaseSpringPolarityConfirmed)));
            Assert.False(ProfileTransfer.IsTuning(nameof(ShifterSettings.InvertSpringY)));
        }

        [Fact]
        public void OldExperimentalNativeExportsCannotBeReinterpretedAsFlightProfiles()
        {
            var file = JObject.Parse(ProfileTransfer.Export(new ShifterProfile { Name = "Tune", Settings = new ShifterSettings() }));
            file["FormatVersion"] = 2;
            file["Settings"]["Ab9NativeProfile"] = true;
            Assert.Throws<ProfileTransferException>(() => ProfileTransfer.Import(file.ToString(), new ShifterSettings()));
        }

        [Fact]
        public void GenericAndCalibrationSetupNeutraliseEveryOnboardCondition()
        {
            var tune = Ab9NativeSettings.GenericSetup();
            Assert.Equal(0, tune.Spring);
            Assert.Equal(0, tune.Damper);
            Assert.Equal(0, tune.Friction);
            Assert.Equal(0, tune.Inertia);
            Assert.Equal(100, tune.Torque);
            Assert.Equal(1, tune.FfbMode);
        }

        [Fact]
        public void GeometryResetLeavesStrengthsAndMachineFactsAlone()
        {
            var settings = new ShifterSettings
            {
                TickHz = 700,
                DampingPct = 44,
                BaseSpringPct = 21,
                OverallGainPct = 78,
                WallRamp = 6789,
                SlotOvertravel = 9000,
                SeqOvertravel = 5678,
                PrndLaneHalfLength = 22222,
                MirrorColumns = true,
                MouthDepth = 9999,
                LockoutHalfWidth = 3333
            };
            var defaults = new ShifterSettings();
            settings.ResetToDefaults(ShifterSettings.ResetScope.Geometry);
            Assert.Equal(700, settings.TickHz);
            Assert.Equal(44, settings.DampingPct);
            Assert.Equal(21, settings.BaseSpringPct);
            Assert.Equal(78, settings.OverallGainPct);
            Assert.Equal(defaults.WallRamp, settings.WallRamp);
            Assert.Equal(defaults.SlotOvertravel, settings.SlotOvertravel);
            Assert.Equal(defaults.SeqOvertravel, settings.SeqOvertravel);
            Assert.Equal(defaults.PrndLaneHalfLength, settings.PrndLaneHalfLength);
            Assert.Equal(defaults.MouthDepth, settings.MouthDepth);
            Assert.Equal(defaults.LockoutHalfWidth, settings.LockoutHalfWidth);
            Assert.False(settings.MirrorColumns);
        }

        [Fact]
        public void FeelResetLeavesTheDrawnGeometryAndCalibrationAlone()
        {
            var settings = new ShifterSettings
            {
                WallRamp = 7890,
                SlotOvertravel = 9876,
                PrndNotchHalfWidth = 1234,
                MouthDepth = 6789,
                BaseSpringPct = 55,
                PolarityConfirmed = true,
                BaseSpringPolarityConfirmed = true
            };
            settings.ResetToDefaults(ShifterSettings.ResetScope.Forces);
            Assert.Equal(7890, settings.WallRamp);
            Assert.Equal(9876, settings.SlotOvertravel);
            Assert.Equal(1234, settings.PrndNotchHalfWidth);
            Assert.Equal(6789, settings.MouthDepth);
            Assert.Equal(0, settings.BaseSpringPct);
            Assert.True(settings.PolarityConfirmed);
            Assert.True(settings.BaseSpringPolarityConfirmed);
        }
    }
}
