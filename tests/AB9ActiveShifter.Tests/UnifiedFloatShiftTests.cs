using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class UnifiedFloatShiftTests
    {
        private static EngineConfig Config(GatePattern pattern)
        {
            return new EngineConfig
            {
                Pattern = pattern,
                FloatShiftingEnabled = true,
                GrindEnabled = true,
                OverallGainPct = 100,
                PolarityConfirmed = true,
                FloatProfileKey = "profile"
            };
        }

        private static TelemetryState Driving()
        {
            return new TelemetryState
            {
                GameRunning = true,
                VehicleKey = "game/car",
                Gear = "3",
                SpeedKmh = 60,
                Rpms = 2400,
                Clutch = 0
            };
        }

        [Theory]
        [InlineData(GatePattern.H7R)]
        [InlineData(GatePattern.H6R)]
        [InlineData(GatePattern.H5R)]
        [InlineData(GatePattern.H6)]
        public void EveryTargetSourceProducesTheSameMatchRefusalBalkAndGrind(GatePattern pattern)
        {
            EngineConfig configured = Config(pattern);
            configured.FloatRpmAt100KmhText = "0,0,4000";
            configured.FloatRpmAt100Kmh = RevMatchModel.ParseRatios(configured.FloatRpmAt100KmhText);
            EngineConfig automatic = Config(pattern);
            EngineConfig learned = Config(pattern);
            var configuredFx = new EffectComposer();
            var automaticFx = new EffectComposer();
            var learnedFx = new EffectComposer();
            var configuredData = Driving();
            var automaticData = Driving();
            var learnedData = Driving();
            for (int tick = 0; tick <= 1000; tick += 50)
            {
                learnedData.CapturedAtTick = tick;
                learnedFx.Step(learned, learnedData, 0, 1, false, 1, 0, 3);
            }

            int sample = 2000;
            foreach (double error in new double[] { 500, 200, 100, 120, 130, 80, -120, -130 })
            {
                configuredData.Rpms = automaticData.Rpms = learnedData.Rpms = 2400 + error;
                configuredData.CapturedAtTick = automaticData.CapturedAtTick = learnedData.CapturedAtTick = sample;
                automaticData.Shift = new ShiftTelemetry(new double[] { 0, 0, 2400 }, "car gearbox", sample);
                sample += 20;
                EffectOutput expected = configuredFx.Step(configured, configuredData, 0, 1, true, 0.8, 3);
                EffectOutput reported = automaticFx.Step(automatic, automaticData, 0, 1, true, 0.8, 3);
                EffectOutput learnt = learnedFx.Step(learned, learnedData, 0, 1, true, 0.8, 3);
                Assert.Equal(RevMatchSource.Configured, expected.RevMatch.Source);
                Assert.Equal(RevMatchSource.GameTelemetry, reported.RevMatch.Source);
                Assert.Equal(RevMatchSource.Learned, learnt.RevMatch.Source);
                AssertSameMechanics(expected, reported);
                AssertSameMechanics(expected, learnt);
            }
        }

        private static void AssertSameMechanics(EffectOutput expected, EffectOutput actual)
        {
            Assert.Equal(expected.RevMatch.TargetRpm, actual.RevMatch.TargetRpm);
            Assert.Equal(expected.RevMatch.ErrorRpm, actual.RevMatch.ErrorRpm);
            Assert.Equal(expected.RevMatch.Matched, actual.RevMatch.Matched);
            Assert.Equal(expected.GrindActive, actual.GrindActive);
            Assert.Equal(expected.BlockEngage, actual.BlockEngage);
            Assert.Equal(expected.MuteDetent, actual.MuteDetent);
            Assert.Equal(expected.GrindLevel, actual.GrindLevel);
            Assert.Equal(expected.GrindWallScale, actual.GrindWallScale);
            Assert.Equal(expected.VibY, actual.VibY);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(8)]
        public void AnyGameCanReportForwardOrReverseTargetsWithoutRoadSpeed(int gear)
        {
            var t = Driving();
            t.SpeedKmh = double.NaN;
            var rpms = new double[8]; rpms[gear - 1] = 2400;
            t.Shift = new ShiftTelemetry(rpms, "car gearbox", 0);
            EffectOutput output = new EffectComposer().Step(Config(GatePattern.H7R), t, 0, 1, true, 1, gear);
            Assert.Equal(RevMatchSource.GameTelemetry, output.RevMatch.Source);
            Assert.True(output.RevMatch.Matched);
            Assert.False(output.BlockEngage || output.MuteDetent || output.GrindActive);
        }

        [Fact]
        public void KnowingATargetDoesNotGrantPermissionWithoutEngineRPM()
        {
            var t = Driving();
            t.Shift = new ShiftTelemetry(new double[] { 0, 0, 2400 }, "car gearbox", 0);
            t.Rpms = double.NaN;
            EngineConfig cfg = Config(GatePattern.H7R);
            Assert.True(new ShiftTargetResolver().Resolve(cfg, t, 0, 3, 0).Available);
            EffectOutput output = new EffectComposer().Step(cfg, t, 0, 1, true, 1, 3);
            Assert.False(output.RevMatch.Matched);
            Assert.True(output.BlockEngage);
        }

        [Theory]
        [InlineData("vehicle")]
        [InlineData("profile")]
        [InlineData("transmission")]
        public void TheComparatorRevokesItsHeldMatchWhenTheTargetContextChanges(string change)
        {
            var model = new RevMatchModel();
            var cfg = Config(GatePattern.H7R);
            var t = Driving();
            t.Shift = new ShiftTelemetry(new double[] { 0, 0, 2400 }, "car gearbox", 0);
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            t.Rpms = 2520;
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            if (change == "vehicle") t.VehicleKey = "other car";
            if (change == "profile") cfg.FloatProfileKey = "other profile";
            if (change == "transmission") t.Shift = new ShiftTelemetry(new double[] { 0, 0, 2400 }, "other gearbox", 0);
            Assert.False(model.Step(cfg, t, 0, 3, 0).Matched);
        }
    }
}
