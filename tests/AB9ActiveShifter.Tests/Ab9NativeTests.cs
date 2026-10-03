using System;
using System.Collections.Generic;
using System.Linq;
using AB9ActiveShifter.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class Ab9NativeTests
    {
        [Fact]
        public void FirmwareUsesCockpitsOrderRatherThanTheRawFinalPair()
        {
            // Read from COM11, compared to Cockpit's displayed 1.1.5.2 on the same base.
            Assert.Equal(new Version(1, 1, 5, 2), Ab9NativeProtocol.FirmwareFromReply(new byte[] { 1, 1, 2, 5 }));
            Assert.Null(Ab9NativeProtocol.FirmwareFromReply(new byte[] { 1, 1, 5 }));
        }

        [Theory]
        [InlineData(0x346E, 0x1000, "1.1.5.2", true)]
        [InlineData(0x346E, 0x1000, "1.1.5.3", true)]
        [InlineData(0x346E, 0x1000, "1.1.6.0", true)]
        [InlineData(0x346E, 0x1000, "1.1.5.1", false)]
        [InlineData(0x346E, 0x1000, "1.1.4.99", false)]
        [InlineData(0x346E, 0x1002, "1.2.0.0", false)] // AB6 has no verified protocol parity.
        [InlineData(0x346E, 0x0006, "1.2.0.0", false)] // A wheelbase is not an AB9.
        [InlineData(0x1234, 0x1000, "1.2.0.0", false)]
        public void EligibilityRequiresExactHardwareAndAtLeastTheMeasuredFirmware(int vendor, int product, string version, bool expected)
        {
            Assert.Equal(expected, Ab9NativeProtocol.IsSupported(vendor, product, Version.Parse(version)));
            Assert.False(Ab9NativeProtocol.IsSupported(vendor, product, null));
        }

        [Fact]
        public void ModeAndSpringWritesMatchDocumentedWireCaptures()
        {
            Assert.Equal(new byte[] { 0x7E, 2, 0x1F, 0x12, 0x5D, 0, 0x1B }, Ab9NativeProtocol.Write(Ab9Parameter.InputMode, 0));
            Assert.Equal(new byte[] { 0x7E, 2, 0x1F, 0x12, 0x5D, 1, 0x1C }, Ab9NativeProtocol.Write(Ab9Parameter.InputMode, 1));
            Assert.Equal(new byte[] { 0x7E, 3, 0x1F, 0x12, 0xAF, 0, 0, 0x6E }, Ab9NativeProtocol.Write(Ab9Parameter.Spring, 0));
            Assert.Equal(new byte[] { 0x7E, 3, 0x1F, 0x12, 0x85, 0, 1, 0x45 }, Ab9NativeProtocol.Write(Ab9Parameter.FfbMode, 1));
        }

        [Fact]
        public void TheReadbackMustNameTheRightDeviceGroupCommandAndValueWidth()
        {
            int value;
            Assert.True(Ab9NativeProtocol.TryValue(new Ab9Frame(0x9E, 0x21, new byte[] { 0xA9, 0, 100 }), Ab9Parameter.Torque, out value));
            Assert.Equal(100, value);
            Assert.False(Ab9NativeProtocol.TryValue(new Ab9Frame(0x9F, 0x21, new byte[0]), Ab9Parameter.Torque, out value));
            Assert.False(Ab9NativeProtocol.TryValue(new Ab9Frame(0x9E, 0x31, new byte[] { 0xA9, 0, 100 }), Ab9Parameter.Torque, out value));
            Assert.False(Ab9NativeProtocol.TryValue(new Ab9Frame(0x9E, 0x21, new byte[] { 0xAE, 0, 100 }), Ab9Parameter.Torque, out value));
            Assert.False(Ab9NativeProtocol.TryValue(new Ab9Frame(0x9E, 0x21, new byte[] { 0xA9, 100 }), Ab9Parameter.Torque, out value));
            Assert.False(Ab9NativeProtocol.TryValue(new Ab9Frame(0x9E, 0x21, new byte[] { 0xA9, 1, 0 }), Ab9Parameter.Torque, out value));
        }

        [Fact]
        public void AnIncrementalReaderHandlesRealRepliesZeroLengthAcksAndEscapes()
        {
            var reader = new Ab9FrameReader();
            byte[] bytes = { 0, 0xFF, 0x7E, 0, 0x9F, 0x21, 0x4B, 0x7E, 4, 0x84, 0x21, 1, 1, 2, 5, 0x3D };
            var found = new List<Ab9Frame>();
            foreach (byte b in bytes)
            {
                reader.Add(b);
                Ab9Frame frame;
                while (reader.TryTake(out frame)) found.Add(frame);
            }
            Assert.Equal(2, found.Count);
            Assert.Empty(found[0].Payload);
            Assert.Equal(new byte[] { 1, 1, 2, 5 }, found[1].Payload);

            // Escape both a payload byte and a checksum; split every possible byte boundary.
            foreach (byte[] packet in new[] { Ab9NativeProtocol.Frame(0x1F, 0x7E), Ab9NativeProtocol.Frame(0x1F, 0xC1) })
            {
                for (int split = 0; split < packet.Length; split++)
                {
                    var partial = new Ab9FrameReader();
                    foreach (byte b in packet.Take(split)) partial.Add(b);
                    Ab9Frame parsed;
                    Assert.False(partial.TryTake(out parsed));
                    foreach (byte b in packet.Skip(split)) partial.Add(b);
                    Assert.True(partial.TryTake(out parsed));
                    Assert.False(partial.TryTake(out parsed));
                }
            }
        }

        [Fact]
        public void ACorruptChecksumCannotConvictADeviceOrHideTheNextReply()
        {
            var reader = new Ab9FrameReader();
            byte[] corrupt = { 0x7E, 4, 0x84, 0x21, 1, 1, 2, 5, 0 };
            byte[] good = { 0x7E, 2, 0x9E, 0x21, 0x5D, 0, 0xA9 };
            foreach (byte b in corrupt.Concat(good)) reader.Add(b);
            Ab9Frame frame;
            Assert.True(reader.TryTake(out frame));
            Assert.Equal(0x9E, frame.Group);
            Assert.Equal(new byte[] { 0x5D, 0 }, frame.Payload);
        }

        [Fact]
        public void VirtualSetupRemovesCentringBeforeRaisingHardwareGainAndNeverConfiguresANativeLayout()
        {
            var recipe = Ab9NativeSettings.VirtualSetup();
            var writes = recipe.Writes(false).ToList();
            Assert.Equal(Ab9Parameter.Spring, writes[0].Key);
            Assert.Equal(0, writes[0].Value);
            Assert.Equal(15, recipe.Damper);
            Assert.Equal(1, recipe.FfbMode);
            Assert.Equal(0, recipe.Inertia);
            Assert.Equal(0, recipe.Friction);
            Assert.Equal(100, recipe.GameGain);
            Assert.Equal(100, recipe.OverallIntensity);
            Assert.Equal(100, recipe.Torque);
            Assert.DoesNotContain(writes, w => w.Key == Ab9Parameter.Layout || w.Key == Ab9Parameter.MechanicalResistance);
            recipe.Torque = 101;
            Assert.Throws<ArgumentOutOfRangeException>(() => recipe.Writes(false));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void HardwareTorqueStaysMutedUntilEveryDialAndTheModeHaveBeenWritten(int mode)
        {
            var recipe = mode == 0 ? Ab9NativeSettings.VirtualSetup() : new ShifterSettings().ToNativeSettings();
            var writes = recipe.TransactionWrites(mode).ToList();
            Assert.Equal(Ab9Parameter.Torque, writes.First().Key);
            Assert.Equal(0, writes.First().Value);
            Assert.Equal(Ab9Parameter.InputMode, writes[writes.Count - 2].Key);
            Assert.Equal(mode, writes[writes.Count - 2].Value);
            Assert.Equal(Ab9Parameter.Torque, writes.Last().Key);
            Assert.Equal(recipe.Torque, writes.Last().Value);
            Assert.Single(writes.Skip(1), w => w.Key == Ab9Parameter.Torque);
            recipe.Spring = 101;
            Assert.Throws<ArgumentOutOfRangeException>(() => recipe.TransactionWrites(mode));
        }

        [Fact]
        public void NativeProfilesRequireSupportedAb9AndNativeModeWhileGenericProfilesWorkWithoutEither()
        {
            var native = new Ab9NativeSnapshot("native", "COM11", new Version(1, 1, 5, 2), 1, new Ab9NativeSettings(), true);
            var flight = new Ab9NativeSnapshot("flight", "COM11", new Version(1, 1, 5, 2), 0, new Ab9NativeSettings(), true);
            Assert.True(NativeProfilePolicy.CanActivate(true, 0x346E, 0x1000, native));
            Assert.False(NativeProfilePolicy.CanActivate(true, 0x346E, 0x1000, flight));
            Assert.False(NativeProfilePolicy.CanActivate(true, 0x1234, 0x5678, native));
            Assert.False(NativeProfilePolicy.CanActivate(true, 0x346E, 0x1000, null));
            Assert.True(NativeProfilePolicy.CanActivate(false, 0x1234, 0x5678, null));
            Assert.False(NativeProfilePolicy.CanRunVirtual(true, 0x346E, 0x1000, false));
            Assert.False(NativeProfilePolicy.CanRunVirtual(false, 0x346E, 0x1000, true));
            Assert.True(NativeProfilePolicy.CanRunVirtual(false, 0x1234, 0x5678, true));
        }

        [Fact]
        public void NativeDialsCloneIndependentlyAndKeepTheVirtualPolarityCap()
        {
            var native = new ShifterSettings { Ab9NativeProfile = true, NativeTorquePct = 100, OverallGainPct = 100 };
            var copy = SettingsCloner.Clone(native);
            copy.NativeTorquePct = 5;
            Assert.Equal(100, native.NativeTorquePct);
            Assert.Equal(0.10, native.ToEngineConfig().EffectiveGain);
            Assert.False(ProfileTransfer.IsMachineFact(nameof(ShifterSettings.NativeTorquePct)));
            Assert.True(ProfileTransfer.IsTuning(nameof(ShifterSettings.NativeTorquePct)));
        }

        [Fact]
        public void NativeProfileFilesCarryTheirTypeAndClampEveryNativeDialWithoutArmingAnything()
        {
            var profile = new ShifterProfile { Name = "Native", Settings = new ShifterSettings { Ab9NativeProfile = true, NativeLayout = 9 } };
            var file = JObject.Parse(ProfileTransfer.Export(profile));
            Assert.Equal(2, (int)file["FormatVersion"]);
            file["Settings"]["NativeTorquePct"] = 500;
            file["Settings"]["NativeFfbMode"] = 500;
            file["Settings"]["NativeLayout"] = 500;
            file["Settings"]["Enabled"] = true;
            var imported = ProfileTransfer.Import(file.ToString(), new ShifterSettings()).Profile.Settings;
            Assert.True(imported.Ab9NativeProfile);
            Assert.Equal(100, imported.NativeTorquePct);
            Assert.Equal(2, imported.NativeFfbMode);
            Assert.Equal(9, imported.NativeLayout);
            Assert.False(imported.Enabled);

            var legacy = JObject.Parse(ProfileTransfer.Export(new ShifterProfile { Name = "Virtual", Settings = new ShifterSettings() }));
            ((JObject)legacy["Settings"]).Remove("Ab9NativeProfile");
            Assert.False(ProfileTransfer.Import(legacy.ToString(), profile.Settings).Profile.Settings.Ab9NativeProfile);
        }

        [Fact]
        public void CyclingSkipsIneligibleNativeProfilesInBothDirections()
        {
            var store = new ProfileStore
            {
                Profiles = new List<ShifterProfile>
            {
                new ShifterProfile { Name = "A", Settings = new ShifterSettings() },
                new ShifterProfile { Name = "Native", Settings = new ShifterSettings { Ab9NativeProfile = true } },
                new ShifterProfile { Name = "B", Settings = new ShifterSettings() }
            }
            };
            Func<ShifterProfile, bool> eligible = p => NativeProfilePolicy.CanActivate(p.Settings.Ab9NativeProfile, 0x1234, 0x5678, null);
            Assert.Equal("B", store.NextInCycle("A", 1, eligible));
            Assert.Equal("A", store.NextInCycle("B", -1, eligible));
        }
    }
}
