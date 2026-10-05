using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class FloatShiftLifecycleTests
    {
        private static EngineConfig Config()
        {
            return new EngineConfig
            {
                FloatShiftingEnabled = true,
                FloatProfileKey = "profile",
                GrindEnabled = true,
                ClutchSource = ClutchSource.Pedal
            };
        }

        private static TelemetryState Driving()
        {
            return new TelemetryState
            {
                GameRunning = true,
                VehicleKey = "game/car",
                Gear = "3",
                CapturedAtTick = 1000,
                Rpms = 2400,
                SpeedKmh = 60
            };
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(501, true)]
        public void AnUnavailablePedalFallsBackToTheGameClutchsOwnFreshness(int ageMs, bool blocked)
        {
            var game = Driving();
            game.Clutch = 100;
            EffectOutput output = new EffectComposer().Step(Config(), game, ageMs, 1, true, 1, 3);
            Assert.False(output.RevMatch.Matched);
            Assert.Equal(blocked, output.BlockEngage);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(500, false)]
        [InlineData(501, true)]
        [InlineData(-1, true)]
        public void OnlyAFreshSuccessfulPedalSampleCanReleaseAStaleGameFrame(int pedalAgeMs, bool blocked)
        {
            var game = Driving();
            var effective = new TelemetryState();
            effective.CopyFromWithClutch(game, 100, game.CapturedAtTick + 501 - pedalAgeMs);
            EffectOutput output = new EffectComposer().Step(Config(), effective, 501, 1, true, 1, 3);
            Assert.False(output.RevMatch.Matched);
            Assert.Equal(blocked, output.BlockEngage);
            Assert.Equal(blocked, output.MuteDetent);
            Assert.Equal(0, output.VibY);
        }

        [Fact]
        public void IndependentClutchFreshnessSurvivesTickCountWrap()
        {
            var game = Driving();
            game.CapturedAtTick = int.MaxValue - 30;
            int now = unchecked(game.CapturedAtTick + 501);
            var effective = new TelemetryState();
            effective.CopyFromWithClutch(game, 100, unchecked(now - 10));
            Assert.True(effective.IsClutchFresh(501));
            Assert.False(new EffectComposer().Step(Config(), effective, 501, 1, true, 1, 3).BlockEngage);
            effective.CopyFromWithClutch(game, 100, unchecked(now + 1));
            Assert.False(effective.IsClutchFresh(501));
        }

        [Fact]
        public void ReusingTheScratchSnapshotCannotCarryFreshnessIntoAFallback()
        {
            var game = Driving();
            var effective = new TelemetryState();
            effective.CopyFromWithClutch(game, 100, 1501);
            Assert.True(effective.IsClutchFresh(501));
            effective.CopyFromWithClutch(game, 100);
            Assert.Null(effective.ClutchCapturedAtTick);
            Assert.False(effective.IsClutchFresh(501));
            Assert.Null(game.ClutchCapturedAtTick);
        }

        [Fact]
        public void FreshRPMDataCannotMakeAnOldIndependentClutchSampleFresh()
        {
            var effective = Driving();
            effective.Clutch = 100;
            effective.ClutchCapturedAtTick = effective.CapturedAtTick - 501;
            EffectOutput output = new EffectComposer().Step(Config(), effective, 0, 1, true, 1, 3);
            Assert.True(output.BlockEngage && output.MuteDetent);
        }

        [Fact]
        public void AMatchedFloatShiftDoesNotNeedAClutchSample()
        {
            var cfg = Config();
            cfg.FloatRpmAt100KmhText = "0,0,4000";
            cfg.FloatRpmAt100Kmh = RevMatchModel.ParseRatios(cfg.FloatRpmAt100KmhText);
            var t = Driving();
            t.ClutchCapturedAtTick = t.CapturedAtTick - 501;
            EffectOutput output = new EffectComposer().Step(cfg, t, 0, 1, true, 1, 3);
            Assert.True(output.RevMatch.Matched);
            Assert.False(output.BlockEngage);
        }

        private static void LearnThirdGear(RevMatchModel model, EngineConfig cfg, TelemetryState t)
        {
            for (int tick = 1000; tick <= 2000; tick += 50)
            {
                t.CapturedAtTick = tick;
                model.Step(cfg, t, 0, 0, 3);
            }
        }

        [Theory]
        [InlineData("enabled")]
        [InlineData("profile")]
        [InlineData("manual")]
        [InlineData("pattern")]
        public void ContextRoundTripsBetweenEngineTicksDiscardLearnedTargets(string change)
        {
            var tracker = new FloatShiftConfigTracker();
            var cfg = Config();
            tracker.Stamp(cfg);
            var model = new RevMatchModel();
            var t = Driving();
            LearnThirdGear(model, cfg, t);
            Assert.True(model.Step(cfg, t, 0, 3, 0).Available);

            var intermediate = Config();
            if (change == "enabled") intermediate.FloatShiftingEnabled = false;
            if (change == "profile") intermediate.FloatProfileKey = "other profile";
            if (change == "manual") intermediate.FloatRpmAt100KmhText = "1";
            if (change == "pattern") intermediate.Pattern = GatePattern.Sequential;
            tracker.Stamp(intermediate);
            var resumed = Config();
            tracker.Stamp(resumed);
            // No model ticks saw the intermediate configuration: the engine was stopped.
            t.CapturedAtTick = 3000;
            RevMatchResult result = model.Step(resumed, t, 0, 3, 0);
            Assert.Equal(0, result.LearnedGears);
            Assert.False(result.Available || result.Matched);

            for (int tick = 3050; tick <= 3900; tick += 50)
            {
                t.CapturedAtTick = tick;
                model.Step(resumed, t, 0, 0, 3);
            }
            Assert.True(model.Step(resumed, t, 0, 3, 0).Matched);
        }

        [Fact]
        public void AnOffOnRoundTripAlsoRevokesConfiguredTargetHysteresis()
        {
            var tracker = new FloatShiftConfigTracker();
            var cfg = Config();
            cfg.FloatRpmAt100KmhText = "0,0,4000";
            cfg.FloatRpmAt100Kmh = RevMatchModel.ParseRatios(cfg.FloatRpmAt100KmhText);
            tracker.Stamp(cfg);
            var t = Driving();
            var model = new RevMatchModel();
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            t.Rpms = 2520;
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            cfg.FloatShiftingEnabled = false;
            tracker.Stamp(cfg);
            cfg.FloatShiftingEnabled = true;
            tracker.Stamp(cfg);
            Assert.False(model.Step(cfg, t, 0, 3, 0).Matched);
        }

        [Fact]
        public void ForceOnlyConfigurationEditsKeepLearnedRatios()
        {
            var tracker = new FloatShiftConfigTracker();
            var cfg = Config();
            tracker.Stamp(cfg);
            var model = new RevMatchModel();
            var t = Driving();
            LearnThirdGear(model, cfg, t);
            var tuned = Config();
            tuned.OverallGainPct = 20;
            tuned.GrindWallPct = 30;
            tracker.Stamp(tuned);
            Assert.Equal(cfg.FloatConfigEpoch, tuned.FloatConfigEpoch);
            Assert.True(model.Step(tuned, t, 0, 3, 0).Matched);
        }

        [Fact]
        public void AStaleIndependentClutchSampleCannotTeachARatio()
        {
            var model = new RevMatchModel();
            var cfg = Config();
            var t = Driving();
            for (int tick = 1000; tick <= 2000; tick += 50)
            {
                t.CapturedAtTick = tick;
                t.ClutchCapturedAtTick = tick - 501;
                model.Step(cfg, t, 0, 0, 3);
            }
            Assert.False(model.Step(cfg, t, 0, 3, 0).Available);
        }
    }
}
