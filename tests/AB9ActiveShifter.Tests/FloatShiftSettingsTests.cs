using AB9ActiveShifter.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class FloatShiftSettingsTests
    {
        [Fact]
        public void DefaultsStayOffAndEffectsResetClearsTheTune()
        {
            var settings = new ShifterSettings();
            Assert.False(settings.FloatShiftingEnabled);
            Assert.Equal(100, settings.FloatToleranceRpm);
            settings.FloatShiftingEnabled = true;
            settings.FloatToleranceRpm = 200;
            settings.FloatLearnRatios = false;
            settings.FloatRpmAt100Kmh = "12000,8000";
            settings.FloatScsHandlePositions = "3,2";
            var cfg = settings.ToEngineConfig();
            Assert.True(cfg.FloatShiftingEnabled);
            Assert.Equal(200, cfg.FloatToleranceRpm);
            Assert.Equal(8000, cfg.FloatRpmAt100Kmh[1]);
            settings.ResetToDefaults(ShifterSettings.ResetScope.Effects);
            Assert.False(settings.FloatShiftingEnabled);
            Assert.Equal("", settings.FloatRpmAt100Kmh);
            Assert.Equal("2,3,4,5,6,7,8,1", settings.FloatScsHandlePositions);
        }

        [Fact]
        public void SharedProfilesCarryTheTuneAndClampTheTolerance()
        {
            var profile = new ShifterProfile
            {
                Name = "Float",
                Settings = new ShifterSettings
                {
                    FloatShiftingEnabled = true,
                    FloatLearnRatios = false,
                    FloatToleranceRpm = 120,
                    FloatRpmAt100Kmh = "12000,8000",
                    FloatScsHandlePositions = "3,2"
                }
            };
            string json = ProfileTransfer.Export(profile);
            var back = ProfileTransfer.Import(json, new ShifterSettings()).Profile.Settings;
            Assert.True(back.FloatShiftingEnabled);
            Assert.False(back.FloatLearnRatios);
            Assert.Equal(profile.Settings.FloatRpmAt100Kmh, back.FloatRpmAt100Kmh);
            Assert.Equal(profile.Settings.FloatScsHandlePositions, back.FloatScsHandlePositions);
            JObject hostile = JObject.Parse(json);
            hostile["Settings"]["FloatToleranceRpm"] = 999999;
            var clamped = ProfileTransfer.Import(hostile.ToString(), new ShifterSettings());
            Assert.Equal(1000, clamped.Profile.Settings.FloatToleranceRpm);
            Assert.Equal(1, clamped.Clamped);
        }

        [Fact]
        public void BadRatioEntriesStayUnknownWithoutShiftingTheColumnOrder()
        {
            double[] values = RevMatchModel.ParseRatios("NaN,8000,Infinity,-4,0,100001,3200,999");
            Assert.Equal(new double[] { 0, 8000, 0, 0, 0, 0, 3200, 0 }, values);
            Assert.Equal(new uint[] { 0, 3, 0, 0, 0, 0, 0, 0 }, ScsShiftTelemetryReader.ParseHandlePositions("NaN,3,-1,33"));
        }
    }
}
