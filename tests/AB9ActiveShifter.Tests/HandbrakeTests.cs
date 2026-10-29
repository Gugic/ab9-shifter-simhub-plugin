using System;
using System.Reflection;
using AB9ActiveShifter.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class HandbrakeTests
    {
        private static EngineConfig Config(bool reversed = false)
        {
            return new EngineConfig
            {
                Pattern = GatePattern.Handbrake,
                HandbrakeRestY = reversed ? 50000 : 15000,
                HandbrakeFullPullY = reversed ? 15000 : 50000,
                OverallGainPct = 100,
                PolarityConfirmed = true,
                InvertConstantX = false,
                InvertConstantY = false,
                DampingPct = 0,
                WallFrictionPct = 0,
                WallYieldPct = 0,
                DamperCoeff = 0
            };
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TravelAndOutputAreMonotonicInEitherPullDirection(bool reverse)
        {
            var model = new HandbrakeModel(Config(reverse));
            int last = 0;
            for (int depth = -1000; depth <= 36000; depth++)
            {
                int value = model.BrakeValue(model.RestY + model.PullSign * depth);
                Assert.InRange(value, last, Math.Min(10000, last + 2));
                last = value;
            }
            Assert.Equal(0, model.BrakeValue(model.RestY));
            Assert.Equal(10000, model.BrakeValue(model.FullPullY));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(30000, 32000)]
        [InlineData(-1, 60000)]
        [InlineData(32767, int.MaxValue)]
        public void InvalidCalibrationCannotApplyBrakeOrRestoringForce(int rest, int full)
        {
            EngineConfig cfg = Config(); cfg.HandbrakeRestY = rest; cfg.HandbrakeFullPullY = full;
            var model = new HandbrakeModel(cfg);
            Assert.False(model.IsValid);
            Assert.Equal(0, model.Update(60000, true));
            Assert.Equal(0, model.Resistance(60000, 10000));
            Assert.False(model.Armed);
        }

        [Fact]
        public void AnExistingPullCannotArmAfterStartOrOutputLoss()
        {
            var model = new HandbrakeModel(Config());
            Assert.Equal(0, model.Update(40000, true));
            Assert.False(model.Armed);
            Assert.Equal(0, model.Update(model.RestY, true));
            Assert.True(model.Armed);
            Assert.True(model.Update(40000, true) > 0);
            Assert.Equal(0, model.Update(40000, false));
            Assert.Equal(0, model.Update(40000, true));
            model.Update(model.RestY, true);
            Assert.True(model.Update(40000, true) > 0);
            model.Reset();
            Assert.Equal(0, model.Update(40000, true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EveryRestoringForcePointsHomeAndNoPositionCountMakesAStep(bool reverse)
        {
            EngineConfig cfg = Config(reverse);
            cfg.HandbrakePullForcePct = 100; cfg.HandbrakeStopForcePct = 100;
            cfg.HandbrakeTravelPct = 25;
            var model = new HandbrakeModel(cfg);
            int last = model.Resistance(0);
            for (int y = 0; y <= GateGeometry.AxisMax; y++)
            {
                int f = model.Resistance(y);
                Assert.True((long)f * (y - model.RestY) <= 0);
                Assert.InRange(f, -10000, 10000);
                Assert.InRange(Math.Abs(f - last), 0, 4);
                last = f;
            }
            Assert.Equal(0, model.Resistance(model.RestY));
        }

        [Fact]
        public void ReturnStrengthBlendsContinuouslyWithoutASignRelay()
        {
            EngineConfig cfg = Config(); var model = new HandbrakeModel(cfg);
            int last = model.Resistance(40000, -21000);
            for (int v = -21000; v <= 21000; v++)
            {
                int force = model.Resistance(40000, v);
                Assert.InRange(Math.Abs(force - last), 0, 1);
                Assert.True(force < 0);
                last = force;
            }
            Assert.True(Math.Abs(model.Resistance(40000, -20000)) < Math.Abs(model.Resistance(40000, 20000)));
            cfg.HandbrakeReturnForcePct = 100;
            Assert.Equal(model.Resistance(40000, 20000), model.Resistance(40000, -20000));
        }

        [Fact]
        public void BrakingAndResistanceCurvesAreIndependent()
        {
            EngineConfig cfg = Config(); var model = new HandbrakeModel(cfg);
            int brake = model.BrakeValue(32500), force = model.Resistance(32500);
            cfg.HandbrakeForceProgressionPct = 0;
            Assert.Equal(brake, model.BrakeValue(32500));
            Assert.NotEqual(force, model.Resistance(32500));
            force = model.Resistance(32500);
            cfg.HandbrakeOutputProgressionPct = 100;
            Assert.True(model.BrakeValue(32500) < brake);
            Assert.Equal(force, model.Resistance(32500));
        }

        [Fact]
        public void ShortenedTravelKeepsItsGradientSpanAndStaysInsideCalibration()
        {
            EngineConfig cfg = Config(); cfg.HandbrakeTravelPct = 0;
            var model = new HandbrakeModel(cfg);
            Assert.Equal(8750, model.Travel);
            Assert.InRange(model.Travel, 8000, cfg.HandbrakeFullPullY - cfg.HandbrakeRestY);
            Assert.Equal(10000, model.BrakeValue(model.FullPullY));
            cfg.HandbrakeDeadzonePct = 20;
            Assert.Equal(0, model.BrakeValue(model.RestY + model.Travel / 5));
            cfg.HandbrakeFullPullY = cfg.HandbrakeRestY + 9000;
            Assert.Equal(8000, model.Travel);
        }

        [Theory]
        [InlineData(0, 0, 32768, 0)]
        [InlineData(10000, 0, 32768, 32768)]
        [InlineData(5000, 100, 20100, 10100)]
        [InlineData(-1, 100, 20100, 100)]
        [InlineData(20000, 100, 20100, 20100)]
        public void VirtualAxisUsesTheDriversActualRange(int brake, long min, long max, int expected)
        { Assert.Equal(expected, HandbrakeModel.AxisValue(brake, min, max)); }

        [Fact]
        public void CompositionRetainsPolarityCapAndFreeStick()
        {
            EngineConfig cfg = Config(); cfg.PolarityConfirmed = false;
            cfg.HandbrakePullForcePct = 100; cfg.HandbrakeStopForcePct = 100;
            var composer = new ForceComposer(cfg.BuildGeometry(), cfg);
            ForceFrame frame = composer.ComposeHandbrake(32767, 60000);
            Assert.InRange(frame.ConstantY, -1000, 0);
            cfg.InvertConstantY = true;
            var inverted = new ForceComposer(cfg.BuildGeometry(), cfg).ComposeHandbrake(32767, 60000);
            Assert.Equal(-frame.ConstantY, inverted.ConstantY);
            cfg.FreeStick = true;
            frame = new ForceComposer(cfg.BuildGeometry(), cfg).ComposeHandbrake(0, 60000, 30000, 30000, 1, 500);
            Assert.Equal(0, frame.ConstantX); Assert.Equal(0, frame.ConstantY);
            Assert.Equal(SpringPreset.Off, frame.SpringX); Assert.Equal(SpringPreset.Off, frame.SpringY);
        }

        [Fact]
        public void DampingAndTelemetryJoinTheHandbrakeComposition()
        {
            EngineConfig cfg = Config(); cfg.HandbrakePullForcePct = 0; cfg.HandbrakeStopForcePct = 0;
            var composer = new ForceComposer(cfg.BuildGeometry(), cfg);
            Assert.Equal(350, composer.ComposeHandbrake(32767, 15000, vibY: 350).ConstantY);
            cfg.DampingPct = 25;
            ForceFrame frame = new ForceComposer(cfg.BuildGeometry(), cfg).ComposeHandbrake(32767, 15000, vy: 30000);
            Assert.True(frame.ConstantY < 0);
        }

        [Fact]
        public void HandbrakeSettingsMapAndResetWithoutChangingCalibration()
        {
            var s = new ShifterSettings
            {
                Pattern = GatePattern.Handbrake,
                HandbrakeRestY = 10000,
                HandbrakeFullPullY = 55000,
                HandbrakePullForcePct = 66,
                HandbrakeTravelPct = 80
            };
            Assert.True(s.IsHandbrake); Assert.False(s.IsHPattern); Assert.False(s.HasSlotDetent);
            EngineConfig cfg = s.ToEngineConfig();
            foreach (FieldInfo field in typeof(EngineConfig).GetFields())
                if (field.Name.StartsWith("Handbrake", StringComparison.Ordinal))
                    Assert.Equal(typeof(ShifterSettings).GetProperty(field.Name).GetValue(s), field.GetValue(cfg));
            s.ResetToDefaults(ShifterSettings.ResetScope.Forces);
            Assert.Equal(40, s.HandbrakePullForcePct); Assert.Equal(80, s.HandbrakeTravelPct);
            s.ResetToDefaults(ShifterSettings.ResetScope.Geometry);
            Assert.Equal(100, s.HandbrakeTravelPct);
            Assert.Equal(10000, s.HandbrakeRestY); Assert.Equal(55000, s.HandbrakeFullPullY);
        }

        [Fact]
        public void SharedTunesKeepLocalEndpointsAndCannotArmTheSession()
        {
            var s = new ShifterSettings
            {
                Pattern = GatePattern.Handbrake,
                HandbrakePullForcePct = 63,
                HandbrakeRestY = 10000,
                HandbrakeFullPullY = 55000,
                Enabled = true
            };
            string json = ProfileTransfer.Export(new ShifterProfile { Name = "Handbrake", Settings = s });
            Assert.DoesNotContain("HandbrakeRestY", json);
            JObject root = JObject.Parse(json);
            root["Settings"]["HandbrakePullForcePct"] = 999;
            root["Settings"]["HandbrakeRestY"] = 0;
            var local = new ShifterSettings { HandbrakeRestY = 50000, HandbrakeFullPullY = 10000 };
            ShifterSettings imported = ProfileTransfer.Import(root.ToString(), local).Profile.Settings;
            Assert.Equal(100, imported.HandbrakePullForcePct); Assert.False(imported.Enabled);
            Assert.Equal(50000, imported.HandbrakeRestY); Assert.Equal(10000, imported.HandbrakeFullPullY);
            Assert.True(ProfileTransfer.IsMachineFact("HandbrakeRestY"));
            Assert.False(ProfileTransfer.IsTuning("HandbrakeRestY"));
        }

        [Fact]
        public void TheCapturedHandbrakePresetKeepsRigAndSessionFactsAtDefaults()
        {
            ShifterSettings s = DefaultProfiles.BuildPreset(DefaultProfiles.Preset(DefaultProfiles.HandbrakeName)).Settings;
            Assert.Equal(GatePattern.Handbrake, s.Pattern);
            Assert.False(s.Enabled); Assert.False(s.PolarityConfirmed);
            Assert.Equal(100, s.OverallGainPct);
            Assert.Equal(0.1, s.ToEngineConfig().EffectiveGain);
            Assert.True(new HandbrakeModel(s.ToEngineConfig()).IsValid);
            var defaults = new ShifterSettings();
            foreach (PropertyInfo property in typeof(ShifterSettings).GetProperties())
                if (property.CanRead && property.CanWrite && ProfileTransfer.IsMachineFact(property.Name))
                    Assert.Equal(property.GetValue(defaults), property.GetValue(s));
        }

        [Fact]
        public void GearMirroringCannotMoveTheHandbrakeCalibrationOrForce()
        {
            EngineConfig cfg = Config();
            var model = new HandbrakeModel(cfg);
            int brake = model.BrakeValue(40000), force = model.Resistance(40000);
            cfg.MirrorColumns = true; cfg.MirrorSlots = true;
            Assert.Equal(brake, model.BrakeValue(40000));
            Assert.Equal(force, model.Resistance(40000));
        }

        [Fact]
        public void ProfileTransitionSettlesAtTheHandbrakesCalibratedRest()
        {
            var transition = new ProfileSwitchTransition();
            transition.Begin(0, 0);
            transition.Step(1, GateGeometry.AxisCenter, 50000, 50000);
            transition.Step(1 + ProfileSwitchTransition.RampMs / 2, GateGeometry.AxisCenter, 50000, 50000);
            Assert.InRange(transition.ForceScale, 0.49, 0.51);
        }

        [Theory]
        [InlineData(0, 65535)]
        [InlineData(65535, 0)]
        [InlineData(8000, 60000)]
        [InlineData(56000, 3000)]
        public void FullRangeReturnAndBrakingUseTheChosenStartInsteadOfBaseCentre(int start, int end)
        {
            EngineConfig cfg = Config();
            cfg.HandbrakeRestY = start; cfg.HandbrakeFullPullY = end;
            var model = new HandbrakeModel(cfg);
            Assert.Equal(0, model.Resistance(start));
            Assert.Equal(0, model.BrakeValue(start));
            Assert.Equal(10000, model.BrakeValue(end));
            Assert.NotEqual(0, model.Resistance(GateGeometry.AxisCenter));
            for (int y = 0; y <= GateGeometry.AxisMax; y += 43)
            {
                foreach (int velocity in new[] { -20000, 0, 20000 })
                    Assert.True((long)model.Resistance(y, velocity) * (y - start) <= 0);
            }
        }

        [Fact]
        public void BaseSpringsCannotCompeteWithTheChosenHandbrakeStartInEitherProvider()
        {
            EngineConfig cfg = Config(); cfg.BaseSpringPct = 100; cfg.BaseSpringPolarityConfirmed = true;
            cfg.BaseFrictionPct = 20;
            var composer = new ForceComposer(cfg.BuildGeometry(), cfg);
            ForceFrame original = composer.ComposeHandbrake(32767, 32767);
            ForceFrame result = BaseEffectComposer.Apply(original, cfg);
            Assert.Equal(SpringPreset.Off, result.SpringX);
            Assert.Equal(SpringPreset.Off, result.SpringY);
            Assert.Equal(original.ConstantY, result.ConstantY);
            Assert.True(result.FrictionCoefficient > 0);
            var settings = new ShifterSettings { Pattern = GatePattern.Handbrake, BaseSpringPct = 100, PolarityConfirmed = true };
            Assert.Equal(0, settings.ToNativeSettings().Spring);
            Assert.Equal(100, settings.BaseSpringPct);
            settings.Pattern = GatePattern.Sequential;
            Assert.Equal(100, settings.ToNativeSettings().Spring);
        }

        [Fact]
        public void EarlyBuildIsStrongestAtTheBeginningAndFlattensTowardTheEnd()
        {
            EngineConfig cfg = Config();
            cfg.HandbrakeForceCurve = HandbrakeCurveShape.BuildEarly;
            cfg.HandbrakeForceProgressionPct = 100;
            var model = new HandbrakeModel(cfg);
            int tenthY = model.RestY + model.Travel / 10;
            int ninthY = model.RestY + 9 * model.Travel / 10;
            int firstTenth = Math.Abs(model.Resistance(tenthY));
            int lastTenth = Math.Abs(model.Resistance(model.FullPullY)) - Math.Abs(model.Resistance(ninthY));
            Assert.True(firstTenth > 5 * lastTenth);
            Assert.Equal(4000, Math.Abs(model.Resistance(model.FullPullY)));
            cfg.HandbrakeForceCurve = HandbrakeCurveShape.BuildLater;
            Assert.True(Math.Abs(model.Resistance(tenthY)) < firstTenth);
            Assert.Equal(4000, Math.Abs(model.Resistance(model.FullPullY)));
        }

        [Theory]
        [InlineData(HandbrakeCurveShape.BuildLater)]
        [InlineData(HandbrakeCurveShape.BuildEarly)]
        [InlineData(HandbrakeCurveShape.Linear)]
        [InlineData(HandbrakeCurveShape.SmoothS)]
        public void EveryCurveIsMonotoneContinuousAndBoundedInEitherDirection(HandbrakeCurveShape shape)
        {
            foreach (bool reversed in new[] { false, true })
            {
                EngineConfig cfg = Config(reversed);
                cfg.HandbrakeForceCurve = shape;
                cfg.HandbrakeFullPullY = cfg.HandbrakeRestY + (reversed ? -8000 : 8000);
                cfg.HandbrakePullForcePct = 100;
                var model = new HandbrakeModel(cfg);
                foreach (int amount in new[] { 0, 25, 70, 100 })
                {
                    cfg.HandbrakeForceProgressionPct = amount;
                    int last = 0;
                    for (int depth = 0; depth <= model.Travel; depth++)
                    {
                        int y = model.RestY + model.PullSign * depth;
                        int force = model.Resistance(y);
                        int magnitude = Math.Abs(force);
                        Assert.InRange(magnitude, last, Math.Min(10000, last + 3));
                        Assert.True((long)force * (y - model.RestY) <= 0);
                        last = magnitude;
                    }
                    Assert.Equal(10000, last);
                    Assert.Equal(0, model.Resistance(model.RestY));
                }
            }
        }

        [Fact]
        public void DefaultCurvePreservesTheOriginalSavedForceTuneExactly()
        {
            var settings = new ShifterSettings();
            EngineConfig cfg = settings.ToEngineConfig();
            Assert.Equal(HandbrakeCurveShape.BuildLater, cfg.HandbrakeForceCurve);
            Assert.Equal(70, cfg.HandbrakeForceProgressionPct);
            var model = new HandbrakeModel(cfg);
            for (int depth = 0; depth <= model.Travel; depth++)
            {
                double p = depth / (double)model.Travel;
                double blend = cfg.HandbrakeForceProgressionPct / 100.0;
                double oldCurve = p * (1 - blend) + p * p * blend;
                int expected = -(int)Math.Round(GateGeometry.ForceMax * cfg.EffectiveGain
                    * (cfg.HandbrakePullForcePct * oldCurve) / 100.0);
                Assert.Equal(expected, model.Resistance(model.RestY + depth));
            }
        }

        [Fact]
        public void CurveSelectionDoesNotChangeBrakingAndZeroAmountIsLinear()
        {
            EngineConfig cfg = Config();
            var model = new HandbrakeModel(cfg);
            int middle = model.RestY + model.Travel / 4;
            int brake = model.BrakeValue(middle);
            foreach (HandbrakeCurveShape shape in Enum.GetValues(typeof(HandbrakeCurveShape)))
            {
                cfg.HandbrakeForceCurve = shape;
                Assert.Equal(brake, model.BrakeValue(middle));
                cfg.HandbrakeForceProgressionPct = 0;
                Assert.Equal(-1000, model.Resistance(middle));
                cfg.HandbrakeForceProgressionPct = 100;
            }
            cfg.HandbrakeForceCurve = (HandbrakeCurveShape)999;
            int fallback = model.Resistance(middle);
            cfg.HandbrakeForceCurve = HandbrakeCurveShape.BuildLater;
            Assert.Equal(fallback, model.Resistance(middle));
        }

        [Fact]
        public void CurveChoiceTravelsAsOneTuningDialAndResetRestoresTheOriginalFamily()
        {
            var settings = new ShifterSettings { Pattern = GatePattern.Handbrake };
            settings.HandbrakeForceCurveIndex = 1;
            Assert.Equal(HandbrakeCurveShape.BuildEarly, settings.ToEngineConfig().HandbrakeForceCurve);
            Assert.True(settings.HandbrakeCurveUsesAmount);
            string json = ProfileTransfer.Export(new ShifterProfile { Name = "Early build", Settings = settings });
            Assert.Contains("BuildEarly", json);
            Assert.DoesNotContain("HandbrakeForceCurveIndex", json);
            Assert.Equal(HandbrakeCurveShape.BuildEarly,
                ProfileTransfer.Import(json, new ShifterSettings()).Profile.Settings.HandbrakeForceCurve);
            Assert.True(ProfileTransfer.IsTuning(nameof(ShifterSettings.HandbrakeForceCurve)));
            Assert.False(ProfileTransfer.IsTuning(nameof(ShifterSettings.HandbrakeForceCurveIndex)));
            settings.HandbrakeForceCurve = HandbrakeCurveShape.Linear;
            Assert.False(settings.HandbrakeCurveUsesAmount);
            settings.ResetToDefaults(ShifterSettings.ResetScope.Forces);
            Assert.Equal(HandbrakeCurveShape.BuildLater, settings.HandbrakeForceCurve);
            Assert.Equal(70, settings.HandbrakeForceProgressionPct);
        }
    }
}
