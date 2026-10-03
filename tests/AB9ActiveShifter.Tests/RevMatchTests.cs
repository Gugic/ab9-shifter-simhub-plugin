using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class RevMatchTests
    {
        private static EngineConfig Config()
        {
            return new EngineConfig
            {
                FloatShiftingEnabled = true,
                GrindEnabled = true,
                OverallGainPct = 100,
                PolarityConfirmed = true,
                FloatProfileKey = "car",
                FloatRpmAt100KmhText = "10000,6000,4000,3000",
                FloatRpmAt100Kmh = RevMatchModel.ParseRatios("10000,6000,4000,3000")
            };
        }

        private static TelemetryState Driving(double rpm = 2400)
        {
            return new TelemetryState
            {
                GameRunning = true,
                VehicleKey = "game/car",
                Gear = "3",
                SpeedKmh = 60,
                Rpms = rpm,
                Clutch = 0
            };
        }

        [Theory]
        [InlineData(2, 3600)]
        [InlineData(3, 2400)]
        [InlineData(4, 1800)]
        public void EachTargetUsesItsOwnRatioIncludingSkippedShifts(int gear, double rpm)
        {
            RevMatchResult result = new RevMatchModel().Step(Config(), Driving(rpm), 0, gear, 0);
            Assert.True(result.Matched);
            Assert.Equal(rpm, result.TargetRpm);
            Assert.Equal(RevMatchSource.Configured, result.Source);
        }

        [Fact]
        public void PermissionHasHysteresisButCannotFollowAnotherTargetOrNeutral()
        {
            var model = new RevMatchModel();
            var cfg = Config();
            var t = Driving(2500);
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            t.Rpms = 2520;
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            Assert.False(model.Step(cfg, t, 0, 4, 0).Matched);
            Assert.False(model.Step(cfg, t, 0, 3, 0).Matched);
            t.Rpms = 2400;
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            model.Step(cfg, t, 0, 0, 0);
            t.Rpms = 2520;
            Assert.False(model.Step(cfg, t, 0, 3, 0).Matched);
        }

        [Theory]
        [InlineData(151)]
        [InlineData(-1)]
        [InlineData(501)]
        public void StaleOrFutureFramesCannotGrantOrRetainAMatch(int age)
        {
            var model = new RevMatchModel();
            var cfg = Config();
            var t = Driving();
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            Assert.False(model.Step(cfg, t, age, 3, 0).Matched);
            t.Rpms = 2520;
            Assert.False(model.Step(cfg, t, 0, 3, 0).Matched);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(0)]
        public void InvalidEngineSpeedsCannotMatch(double rpm)
        {
            Assert.False(new RevMatchModel().Step(Config(), Driving(rpm), 0, 3, 0).Matched);
        }

        [Fact]
        public void UnknownGearsAndUnsignedReverseKeepTheClutchRequirement()
        {
            var fx = new EffectComposer();
            var cfg = Config();
            foreach (int gear in new[] { 0, 5, 8 })
            {
                EffectOutput o = fx.Step(cfg, Driving(), 0, 1, true, 1, gear);
                Assert.False(o.RevMatch.Available);
                Assert.True(o.BlockEngage);
                Assert.Equal(1, o.GrindWallScale);
            }
        }

        [Fact]
        public void MatchingReleasesTheBalkAndMismatchSoftensOnlyTheExtraWall()
        {
            var fx = new EffectComposer();
            var cfg = Config();
            var t = Driving(3300);
            EffectOutput far = fx.Step(cfg, t, 0, 1, true, 1, 3);
            t.Rpms = 2600;
            EffectOutput near = fx.Step(cfg, t, 0, 1, true, 1, 3);
            Assert.True(far.BlockEngage && near.BlockEngage);
            Assert.InRange(near.GrindLevel, 0.01, far.GrindLevel - 0.01);
            Assert.InRange(near.GrindWallScale, 0.01, far.GrindWallScale - 0.01);
            t.Rpms = 2400;
            EffectOutput matched = fx.Step(cfg, t, 0, 1, true, 1, 3);
            Assert.True(matched.RevMatch.Matched);
            Assert.False(matched.GrindActive || matched.BlockEngage || matched.MuteDetent);
            Assert.Equal(0, matched.GrindLevel);
        }

        [Theory]
        [InlineData(GrindClutchMode.Threshold)]
        [InlineData(GrindClutchMode.Progressive)]
        public void PressingTheClutchStillBypassesRPMAndDisabledFloatPreservesOldArithmetic(GrindClutchMode mode)
        {
            var cfg = Config();
            cfg.GrindClutchMode = mode;
            var fx = new EffectComposer();
            var t = Driving(9000);
            t.Clutch = 100;
            Assert.False(fx.Step(cfg, t, 0, 1, true, 1, 3).BlockEngage);
            t.Clutch = 10;
            cfg.FloatShiftingEnabled = false;
            EffectOutput o = fx.Step(cfg, t, 0, 1, true, 1, 3);
            Assert.True(o.BlockEngage);
            Assert.Equal(EffectComposer.ClutchEngagement(cfg, t.Clutch), o.GrindStrength);
            Assert.Equal(1, o.GrindWallScale);
        }

        [Fact]
        public void StaleFloatTelemetrySilencesCarriersAndRefusesANewLatch()
        {
            var cfg = Config();
            var fx = new EffectComposer();
            var t = Driving();
            var o = fx.Step(cfg, t, 501, 1, true, 1, 3);
            Assert.Equal(0, o.VibY);
            Assert.Equal(0, o.GrindLevel);
            Assert.True(o.BlockEngage && o.MuteDetent);
            Assert.False(fx.Step(cfg, t, 501, 1, false, 1, 3, 3).BlockEngage);
            cfg.ClutchSource = ClutchSource.Pedal;
            t.Clutch = 100;
            Assert.False(fx.Step(cfg, t, 501, 1, true, 1, 3).BlockEngage);
        }

        [Fact]
        public void LearningNeedsConfirmedReleasedClutchAndFreshDistinctFrames()
        {
            var cfg = Config();
            cfg.FloatRpmAt100Kmh = new double[8];
            var model = new RevMatchModel();
            var t = Driving();
            for (int i = 0; i < 1000; i++) model.Step(cfg, t, 0, 0, 3);
            Assert.False(model.Step(cfg, t, 0, 3, 0).Available);
            for (int tick = 50; tick <= 850; tick += 50)
            {
                t.CapturedAtTick = tick;
                model.Step(cfg, t, 0, 0, 3);
            }
            RevMatchResult learned = model.Step(cfg, t, 0, 3, 0);
            Assert.True(learned.Matched);
            Assert.Equal(RevMatchSource.Learned, learned.Source);
            Assert.Equal(1, learned.LearnedGears);
            t.SpeedKmh = 80;
            t.Rpms = 3200;
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
        }

        [Theory]
        [InlineData("clutch")]
        [InlineData("gear")]
        [InlineData("tc")]
        [InlineData("speed")]
        [InlineData("identity")]
        [InlineData("gap")]
        [InlineData("unstable")]
        public void UnreliableSamplesDoNotTeachARatio(string fault)
        {
            var cfg = Config();
            cfg.FloatRpmAt100Kmh = new double[8];
            var model = new RevMatchModel();
            var t = Driving();
            if (fault == "clutch") t.Clutch = 10;
            if (fault == "gear") t.Gear = "2";
            if (fault == "tc") t.TcActive = true;
            if (fault == "speed") t.SpeedKmh = 2;
            if (fault == "identity") t.VehicleKey = null;
            for (int i = 0; i < 30; i++)
            {
                t.CapturedAtTick = i * (fault == "gap" ? 200 : 50);
                if (fault == "unstable") t.Rpms = i % 2 == 0 ? 2400 : 2700;
                model.Step(cfg, t, 0, 0, 3);
            }
            Assert.False(model.Step(cfg, t, 0, 3, 0).Available);
        }

        [Theory]
        [InlineData("profile")]
        [InlineData("vehicle")]
        [InlineData("inactive")]
        [InlineData("disabled")]
        public void LearnedRatiosNeverLeakToAnotherSession(string change)
        {
            var cfg = Config();
            cfg.FloatRpmAt100Kmh = new double[8];
            var model = new RevMatchModel();
            var t = Driving();
            for (int i = 0; i <= 20; i++) { t.CapturedAtTick = i * 50; model.Step(cfg, t, 0, 0, 3); }
            Assert.True(model.Step(cfg, t, 0, 3, 0).Available);
            if (change == "profile") cfg.FloatProfileKey = "other";
            if (change == "vehicle") t.VehicleKey = "other";
            if (change == "inactive") { t.GameRunning = false; model.Step(cfg, t, 0, 0, 0); t.GameRunning = true; }
            if (change == "disabled") { cfg.FloatShiftingEnabled = false; model.Step(cfg, t, 0, 0, 0); cfg.FloatShiftingEnabled = true; }
            Assert.False(model.Step(cfg, t, 0, 3, 0).Available);
        }

        [Fact]
        public void TruckTelemetryWinsAndCannotFallBackWhenItsMappingIsMissing()
        {
            var model = new RevMatchModel();
            var cfg = Config();
            var t = Driving(1800);
            t.Shift = new ShiftTelemetry(new double[] { 0, 0, 1800 }, "truck/low", 0);
            Assert.True(model.Step(cfg, t, 0, 3, 0).Matched);
            t.Shift = new ShiftTelemetry(new double[8], "truck/high", 0);
            Assert.False(model.Step(cfg, t, 0, 3, 0).Available);
        }

        [Fact]
        public void RepeatedSCSFramesCannotBecomeFreshThroughNormalizedTelemetry()
        {
            var t = Driving();
            t.Shift = new ShiftTelemetry(new double[] { 0, 0, 2400 }, "truck", 0);
            t.CapturedAtTick = 151;
            Assert.False(new RevMatchModel().Step(Config(), t, 0, 3, 0).Matched);
        }

        [Theory]
        [InlineData(GatePattern.H7R, false, false)]
        [InlineData(GatePattern.H6R, true, false)]
        [InlineData(GatePattern.H5R, false, true)]
        [InlineData(GatePattern.H6, true, true)]
        public void EngagementUsesMappedGearAndNeverDropsItOnceHeld(GatePattern pattern, bool columns, bool slots)
        {
            var cfg = Config();
            cfg.Pattern = pattern;
            cfg.MirrorColumns = columns;
            cfg.MirrorSlots = slots;
            var geo = cfg.BuildGeometry();
            Column col;
            ShiftDir dir;
            Assert.True(geo.TryFindSlot(3, out col, out dir));
            int x = geo.ColumnTarget(col);
            int y = dir == ShiftDir.Fwd ? cfg.EngageDepth - 100 : GateGeometry.AxisMax - cfg.EngageDepth + 100;
            var state = new GateStateMachine(geo, 1);
            var fx = new EffectComposer();
            state.Update(x, y);
            Assert.Equal(0, state.Update(x, y, false).Gear);
            var o = fx.Step(cfg, Driving(), 0, 1, true, 1, geo.GearFor(state.Column, state.Direction));
            Assert.Equal(3, state.Update(x, y, !o.BlockEngage).Gear);
            Assert.Equal(3, state.Update(x, y, false).Gear);
        }

        [Theory]
        [InlineData(GatePattern.Sequential)]
        [InlineData(GatePattern.Prnd)]
        public void OtherPatternsNeverAcquireFloatPermission(GatePattern pattern)
        {
            var cfg = Config(); cfg.Pattern = pattern;
            Assert.False(new RevMatchModel().Step(cfg, Driving(), 0, 3, 0).Matched);
        }

        [Fact]
        public void BalkScalingOnlyRemovesForceAndCannotWeakenAHardSlotLockout()
        {
            var cfg = Config();
            cfg.LockoutPlacement = LockoutPlacement.Off;
            var force = new ForceComposer(cfg.BuildGeometry(), cfg);
            for (int y = cfg.EngageDepth; y < GateGeometry.AxisCenter; y += 200)
            {
                int full = force.SlotForceAt(ShiftDir.Fwd, y, true, Column.C1);
                int soft = force.SlotForceAt(ShiftDir.Fwd, y, true, Column.C1, 0.1);
                Assert.InRange(soft, 0, full);
            }
            cfg.LockoutPlacement = LockoutPlacement.Slot;
            cfg.LockoutSlotGear = 1;
            cfg.LockoutMode = LockoutMode.HotkeyToggle;
            cfg.LockoutSlotDirection = LockoutSlotDirection.Entry;
            force = new ForceComposer(cfg.BuildGeometry(), cfg);
            force.Compose(GateState.Neutral, Column.None, ShiftDir.None, GateGeometry.AxisCenter, GateGeometry.AxisCenter);
            Assert.True(force.LockoutRefusesEngage(Column.C1, ShiftDir.Fwd));
            Assert.Equal(force.SlotForceAt(ShiftDir.Fwd, cfg.EngageDepth, true, Column.C1, 1),
                force.SlotForceAt(ShiftDir.Fwd, cfg.EngageDepth, true, Column.C1, 0));
        }

        [Fact]
        public void NativeGrindStopsImmediatelyAtAMatchWithoutMutingOtherTonesOrTests()
        {
            var frame = new NativeEffectFrame(0, 0, new[] {
                new NativeEffectTone { Id = 1, Frequency = 25, Level = 1, Grind = true, Priority = true },
                new NativeEffectTone { Id = 2, Frequency = 25, Level = 0.2 }
            });
            Assert.Equal(600, new NativeEffectMixer().Step(frame, 0, 0, 10, 1, true, false));
            frame.Tones[0].Test = true;
            Assert.Equal(4500, new NativeEffectMixer().Step(frame, 0, 0, 10, 1, true, false));
        }
    }
}
