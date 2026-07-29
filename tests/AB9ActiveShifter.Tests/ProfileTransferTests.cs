using System.Reflection;
using AB9ActiveShifter;
using AB9ActiveShifter.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The shareable profile file. Text in, text out - no file system here - so what is pinned is
    /// the part that matters when the text came from a stranger: that a tune survives the round
    /// trip intact, that nothing about the receiving machine is overwritten by it, and that no
    /// file however hostile can arrive with force switched on or a dial past its envelope.
    /// </summary>
    public class ProfileTransferTests
    {
        private static ShifterProfile Sample()
        {
            ShifterSettings s = new ShifterSettings
            {
                Pattern = GatePattern.H5R,
                MouthShape = SlotMouthShape.Angled,
                OverallGainPct = 73,
                LockoutForcePct = 81,
                LockoutHalfWidth = 5123,
                SlotHalfWidth = 2400,
                WallRamp = 6000,
                EngageDepth = 20852,
                ReleaseDepth = 21500,
                GrindEnabled = true,
                FxCurbsFullAtG = 1.5,
                FxCustomProperty = "ShakeItBass.Export1"
            };
            return new ShifterProfile { Name = "Truck gate", Settings = s };
        }

        [Fact]
        public void ATuneSurvivesTheRoundTrip()
        {
            ShifterProfile original = Sample();
            ProfileImportResult back = ProfileTransfer.Import(
                ProfileTransfer.Export(original), new ShifterSettings());

            Assert.Equal("Truck gate", back.Profile.Name);
            Assert.Equal(0, back.Clamped);
            Assert.Equal(0, back.Unknown);

            ShifterSettings a = original.Settings;
            ShifterSettings b = back.Profile.Settings;

            Assert.Equal(a.Pattern, b.Pattern);
            Assert.Equal(a.MouthShape, b.MouthShape);
            Assert.Equal(a.OverallGainPct, b.OverallGainPct);
            Assert.Equal(a.LockoutForcePct, b.LockoutForcePct);
            Assert.Equal(a.LockoutHalfWidth, b.LockoutHalfWidth);
            Assert.Equal(a.EngageDepth, b.EngageDepth);
            Assert.Equal(a.ReleaseDepth, b.ReleaseDepth);
            Assert.Equal(a.GrindEnabled, b.GrindEnabled);
            Assert.Equal(a.FxCurbsFullAtG, b.FxCurbsFullAtG);
            Assert.Equal(a.FxCustomProperty, b.FxCustomProperty);
        }

        [Fact]
        public void AnImportedProfileNeverArrivesArmed()
        {
            // The one thing a downloaded file must not be able to do. Even a file that explicitly
            // asks for it - and even when the machine it lands on was running with forces on.
            ShifterProfile original = Sample();
            original.Settings.Enabled = true;
            original.Settings.FreeStick = true;

            string json = ProfileTransfer.Export(original);

            // Belt and braces: put the keys in by hand too, in case the exporter ever writes them.
            JObject doctored = JObject.Parse(json);
            ((JObject)doctored["Settings"]).Add("Enabled", true);
            ((JObject)doctored["Settings"]).Add("FreeStick", true);

            ShifterSettings live = new ShifterSettings { Enabled = true, FreeStick = true };
            ProfileImportResult result = ProfileTransfer.Import(doctored.ToString(), live);

            Assert.False(result.Profile.Settings.Enabled);
            Assert.False(result.Profile.Settings.FreeStick);
        }

        [Fact]
        public void TheReceivingMachinesOwnFactsAreKept()
        {
            // Polarity is measured per unit: carrying someone else's would drive the gate
            // backwards, and carrying their confirmation flag would lift the 10% cap on a base
            // nobody here has probed. Device and vJoy numbers are equally local.
            ShifterProfile original = Sample();
            original.Settings.InvertConstantX = true;
            original.Settings.InvertConstantY = true;
            original.Settings.PolarityConfirmed = true;
            original.Settings.VJoyDeviceId = 7;
            original.Settings.VendorId = 0x1234;
            original.Settings.TickHz = 250;

            ShifterSettings mine = new ShifterSettings
            {
                InvertConstantX = false,
                InvertConstantY = false,
                PolarityConfirmed = false,
                VJoyDeviceId = 1,
                VendorId = 0x346E,
                TickHz = 1000
            };

            ShifterSettings landed = ProfileTransfer.Import(
                ProfileTransfer.Export(original), mine).Profile.Settings;

            Assert.False(landed.InvertConstantX);
            Assert.False(landed.InvertConstantY);
            Assert.False(landed.PolarityConfirmed);
            Assert.Equal(1u, landed.VJoyDeviceId);
            Assert.Equal(0x346E, landed.VendorId);
            Assert.Equal(1000, landed.TickHz);

            // ...and none of it is even in the file.
            JObject dials = (JObject)JObject.Parse(ProfileTransfer.Export(original))["Settings"];
            Assert.Null(dials["PolarityConfirmed"]);
            Assert.Null(dials["InvertConstantX"]);
            Assert.Null(dials["VJoyDeviceId"]);
            Assert.Null(dials["VendorId"]);
            Assert.Null(dials["TickHz"]);
        }

        [Fact]
        public void AForcePercentageCannotArriveAboveFull()
        {
            // The clamp that exists because this drives a 12 Nm base: a corrupt or hand-edited
            // file asking for 5000% gain gets 100%, and is reported as having been held back.
            JObject doctored = JObject.Parse(ProfileTransfer.Export(Sample()));
            JObject dials = (JObject)doctored["Settings"];
            dials["OverallGainPct"] = 5000;
            dials["LockoutForcePct"] = -40;
            dials["DetentHoldPct"] = 100000;

            ProfileImportResult result = ProfileTransfer.Import(doctored.ToString(), new ShifterSettings());

            Assert.Equal(100, result.Profile.Settings.OverallGainPct);
            Assert.Equal(0, result.Profile.Settings.LockoutForcePct);
            Assert.Equal(100, result.Profile.Settings.DetentHoldPct);
            Assert.Equal(3, result.Clamped);
        }

        [Fact]
        public void EveryPercentageDialIsHeldToTheEnvelope()
        {
            // Not a spot check: every dial whose name says it scales something is swept, because
            // a new one added later would otherwise slip in unclamped.
            JObject doctored = JObject.Parse(ProfileTransfer.Export(Sample()));
            JObject dials = (JObject)doctored["Settings"];

            int swept = 0;
            foreach (PropertyInfo p in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite || p.PropertyType != typeof(int)) continue;
                if (!p.Name.EndsWith("Pct")) continue;
                if (dials[p.Name] == null) continue;

                dials[p.Name] = 999999;
                swept++;
            }
            Assert.True(swept > 5, "expected a handful of percentage dials, swept " + swept);

            ShifterSettings landed = ProfileTransfer.Import(
                doctored.ToString(), new ShifterSettings()).Profile.Settings;

            foreach (PropertyInfo p in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite || p.PropertyType != typeof(int)) continue;
                if (!p.Name.EndsWith("Pct")) continue;
                if (dials[p.Name] == null) continue;

                Assert.True((int)p.GetValue(landed, null) <= 100,
                    p.Name + " came in at " + p.GetValue(landed, null));
            }
        }

        [Fact]
        public void APositionCannotArriveOffTheAxis()
        {
            JObject doctored = JObject.Parse(ProfileTransfer.Export(Sample()));
            JObject dials = (JObject)doctored["Settings"];
            dials["LockoutHalfWidth"] = 999999;
            dials["EngageDepth"] = -5;

            ShifterSettings landed = ProfileTransfer.Import(
                doctored.ToString(), new ShifterSettings()).Profile.Settings;

            Assert.Equal(GateGeometry.AxisMax, landed.LockoutHalfWidth);
            Assert.Equal(0, landed.EngageDepth);
        }

        [Fact]
        public void OneUnreadableDialDoesNotCostTheRest()
        {
            JObject doctored = JObject.Parse(ProfileTransfer.Export(Sample()));
            JObject dials = (JObject)doctored["Settings"];
            dials["LockoutForcePct"] = "not a number";
            dials["Pattern"] = "SomePatternFromTheFuture";

            ProfileImportResult result = ProfileTransfer.Import(doctored.ToString(), new ShifterSettings());

            // The bad ones fall back to the local values; everything else still arrives.
            Assert.Equal(new ShifterSettings().LockoutForcePct, result.Profile.Settings.LockoutForcePct);
            Assert.Equal(new ShifterSettings().Pattern, result.Profile.Settings.Pattern);
            Assert.Equal(5123, result.Profile.Settings.LockoutHalfWidth);
        }

        [Fact]
        public void KeysThisVersionDoesNotKnowAreCountedNotFatal()
        {
            JObject doctored = JObject.Parse(ProfileTransfer.Export(Sample()));
            ((JObject)doctored["Settings"]).Add("SomeDialFromVersion9", 42);

            ProfileImportResult result = ProfileTransfer.Import(doctored.ToString(), new ShifterSettings());

            Assert.Equal(1, result.Unknown);
            Assert.True(result.Applied > 0);
        }

        [Fact]
        public void SomethingThatIsNotAProfileIsRefusedWithAnExplanation()
        {
            Assert.Throws<ProfileTransferException>(() =>
                ProfileTransfer.Import("", new ShifterSettings()));

            Assert.Throws<ProfileTransferException>(() =>
                ProfileTransfer.Import("{ not json", new ShifterSettings()));

            // Valid JSON, wrong thing entirely - notably SimHub's own settings file, which is
            // the mistake a user is most likely to make.
            Assert.Throws<ProfileTransferException>(() =>
                ProfileTransfer.Import("{\"Profiles\":[],\"ActiveProfile\":\"x\"}", new ShifterSettings()));

            // Ours, but with nothing in it.
            Assert.Throws<ProfileTransferException>(() => ProfileTransfer.Import(
                "{\"Format\":\"" + ProfileTransfer.FormatId + "\"}", new ShifterSettings()));
        }

        [Fact]
        public void AFileFromANewerFormatIsRefusedRatherThanGuessedAt()
        {
            JObject doctored = JObject.Parse(ProfileTransfer.Export(Sample()));
            doctored["FormatVersion"] = ProfileTransfer.FormatVersion + 1;

            ProfileTransferException ex = Assert.Throws<ProfileTransferException>(
                () => ProfileTransfer.Import(doctored.ToString(), new ShifterSettings()));
            Assert.Contains("newer version", ex.Message);
        }

        [Fact]
        public void ANameFromAFileIsMadeFitToUse()
        {
            Assert.Equal("Imported profile", ProfileTransfer.CleanName(null));
            Assert.Equal("Imported profile", ProfileTransfer.CleanName("   "));
            Assert.Equal("Truck gate", ProfileTransfer.CleanName("  Truck gate  "));
            Assert.Equal("one two", ProfileTransfer.CleanName("one\r\ntwo"));
            Assert.True(ProfileTransfer.CleanName(new string('x', 500)).Length <= ProfileTransfer.MaxNameLength);
        }

        [Fact]
        public void TheFileSaysWhatItIs()
        {
            // A shared file lands in a downloads folder among a hundred others; it has to be
            // identifiable on its own, and readable enough to diff against someone else's.
            JObject root = JObject.Parse(ProfileTransfer.Export(Sample()));

            Assert.Equal(ProfileTransfer.FormatId, (string)root["Format"]);
            Assert.Equal(ProfileTransfer.FormatVersion, (int)root["FormatVersion"]);
            Assert.Equal("Truck gate", (string)root["Name"]);
            Assert.Equal("H5R", (string)root["Settings"]["Pattern"]);
            Assert.Equal("Angled", (string)root["Settings"]["MouthShape"]);
        }
    }
}
