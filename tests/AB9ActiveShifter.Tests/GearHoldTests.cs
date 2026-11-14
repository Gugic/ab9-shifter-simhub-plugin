using System;
using AB9ActiveShifter.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The gear hold: with the clutch up, leaving an engaged H slot pays a toll in the slot
    /// lockout's exit band, scaled by the clutch and the load through the gearbox - drive less
    /// RPM-scaled engine braking, or a named property against its full scale. Force only -
    /// it never refuses a release, never drops a gear, and every uncertain reading resolves
    /// toward no hold. All in the gate's frame, before the polarity signs, like every force test.
    /// </summary>
    public class GearHoldTests
    {
        private const int Center = GateGeometry.AxisCenter;

        // The shape tests below sample the band at fixed depths, so they need a gate whose gear
        // stays latched through all of it: the release line at 0.51 of the engage depth, under
        // the band's lowest foot. Where the release line sits higher - every shipped H geometry,
        // and the bare default - the foot rides up to it; ThePullOutNeverStepsWhereTheLatchLetsGo
        // drives a real state machine through those.
        private const int LowReleaseDepth = 18000;

        private static EngineConfig HoldConfig(bool enabled = true)
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                GearHoldEnabled = enabled,
                GearHoldPct = 40,
                GearHoldEngineBrakePct = 30,
                ReleaseDepth = LowReleaseDepth
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        private static int FwdAt(EngineConfig cfg, double d)
        {
            return Center - (int)Math.Round(d * (Center - cfg.EngageDepth));
        }

        private static int BackAt(EngineConfig cfg, double d)
        {
            return Center + (int)Math.Round(d * (Center - cfg.EngageDepth));
        }

        private static TelemetryState Driving(double clutch = 0, double throttle = 0)
        {
            return new TelemetryState
            {
                GameRunning = true,
                Rpms = 3000,
                MaxRpm = 7000,
                SpeedKmh = 60,
                Clutch = clutch,
                Throttle = throttle,
                Gear = "3"
            };
        }

        // ---------------------------------------------------------------- the force shape

        [Fact]
        public void OffByDefaultTheStrokeIsExactlyWhatItWas()
        {
            // A fresh config must be the gate that shipped: even a full hold target changes
            // nothing while the setting is off.
            EngineConfig cfg = HoldConfig(enabled: false);
            ForceComposer c = Composer(cfg);

            for (double d = 0.0; d <= 1.2; d += 0.01)
            {
                Assert.Equal(
                    c.SlotForceAt(ShiftDir.Fwd, FwdAt(cfg, d), muted: false, column: Column.C2),
                    c.SlotForceAt(ShiftDir.Fwd, FwdAt(cfg, d), muted: false, column: Column.C2, gearHold: 1.0));
            }
        }

        [Fact]
        public void TheHoldIsAFlatWallFromItsFaceToTheEndOfTheSlot()
        {
            // Like the grind's balk turned round: past one wall face above its foot, the full
            // toll pushes deeper into the slot the whole way to the end of travel, seat included,
            // so a loaded gear is hard to move from the first count of the pull. The tunnel and
            // everything up to the crossover are untouched.
            EngineConfig cfg = HoldConfig();
            ForceComposer c = Composer(cfg);

            foreach (double d in new[] { 0.60, 0.75, 0.89, 0.96, 1.0, 1.1, 1.2 })
            {
                int plain = c.SlotForceAt(ShiftDir.Fwd, FwdAt(cfg, d), muted: false, column: Column.C2);
                int expected = GateGeometry.Clamp(plain - 4000, -GateGeometry.ForceMax, GateGeometry.ForceMax);
                Assert.Equal(expected,
                    c.SlotForceAt(ShiftDir.Fwd, FwdAt(cfg, d), muted: false, column: Column.C2, gearHold: 1.0));
            }

            foreach (double d in new[] { 0.0, 0.25, 0.50, 0.54 })
            {
                Assert.Equal(
                    c.SlotForceAt(ShiftDir.Fwd, FwdAt(cfg, d), muted: false, column: Column.C2),
                    c.SlotForceAt(ShiftDir.Fwd, FwdAt(cfg, d), muted: false, column: Column.C2, gearHold: 1.0));
            }
        }

        [Fact]
        public void ABackSlotIsTolledTheOtherWay()
        {
            // Deeper into a back slot is +y, so the toll's sign flips with the slot.
            EngineConfig cfg = HoldConfig();
            ForceComposer c = Composer(cfg);

            double d = 0.75;
            int plain = c.SlotForceAt(ShiftDir.Back, BackAt(cfg, d), muted: false, column: Column.C2);
            int held = c.SlotForceAt(ShiftDir.Back, BackAt(cfg, d), muted: false, column: Column.C2, gearHold: 1.0);
            Assert.Equal(GateGeometry.Clamp(plain + 4000, -GateGeometry.ForceMax, GateGeometry.ForceMax), held);
        }

        [Fact]
        public void TheTollScalesWithTheHoldLevel()
        {
            EngineConfig cfg = HoldConfig();
            ForceComposer c = Composer(cfg);
            int y = FwdAt(cfg, 0.75);

            int plain = c.SlotForceAt(ShiftDir.Fwd, y, muted: false, column: Column.C2);
            int half = c.SlotForceAt(ShiftDir.Fwd, y, muted: false, column: Column.C2, gearHold: 0.5);
            Assert.Equal(GateGeometry.Clamp(plain - 2000, -GateGeometry.ForceMax, GateGeometry.ForceMax), half);
        }

        [Fact]
        public void NoSingleCountOfDepthEverStepsAHeldStroke()
        {
            // The hold's one face is the walls' own stiffness, never steeper; with the hold at
            // full strength, no adjacent pair of axis counts may differ by more than the plain
            // stroke's own steepest step plus that stiffness.
            EngineConfig cfg = HoldConfig();
            ForceComposer c = Composer(cfg);

            int plainStep = 0;
            int previous = c.SlotForceAt(ShiftDir.Fwd, Center, muted: false, column: Column.C2);
            for (int depth = 1; depth <= Center; depth++)
            {
                int force = c.SlotForceAt(ShiftDir.Fwd, Center - depth, muted: false, column: Column.C2);
                plainStep = Math.Max(plainStep, Math.Abs(force - previous));
                previous = force;
            }

            // A full-scale wall rises across WallRamp counts; a weaker hold gets a shorter face
            // at the same slope.
            int bound = plainStep + (int)Math.Ceiling(GateGeometry.ForceMax / (double)cfg.WallRamp) + 2;

            previous = c.SlotForceAt(ShiftDir.Fwd, Center, muted: false, column: Column.C2, gearHold: 1.0);
            for (int depth = 1; depth <= Center; depth++)
            {
                int force = c.SlotForceAt(ShiftDir.Fwd, Center - depth, muted: false, column: Column.C2, gearHold: 1.0);
                Assert.True(Math.Abs(force - previous) <= bound,
                    "stepped by " + Math.Abs(force - previous) + " at depth " + depth + ", bound " + bound);
                previous = force;
            }
        }

        [Theory]
        [InlineData(4000, 8000)]     // bare EngineConfig: releases at 0.861
        [InlineData(20852, 23852)]   // the 5+R / 7+R presets: releases at 0.748
        [InlineData(20761, 26731)]   // narrow 5+R short throw: releases at 0.503, under the foot
        [InlineData(20761, 35317)]   // 7+R short throw: release line past the centre
        [InlineData(2767, 5767)]     // truck: releases at 0.900, past the old band's fall
        public void ThePullOutNeverStepsWhereTheLatchLetsGo(int engageDepth, int releaseDepth)
        {
            // The hold only exists while a gear is latched. With the slot lockout's foot at 0.55
            // it was at full strength where the 5+R presets unlatch (0.748) and vanished in one
            // tick: ~9800 DI at 100% in a sweep of that preset, and on the rig a toll too short to
            // feel at full throttle. Pulled out count by count through a real state machine, the
            // hold must be nothing as the latch lets go, nothing once it has, and still reach its
            // full strength somewhere on the way out of the gear - or it is not there to feel.
            EngineConfig cfg = HoldConfig();
            cfg.GearHoldPct = 100;
            cfg.EngageDepth = engageDepth;
            cfg.ReleaseDepth = releaseDepth;

            GateGeometry geo = cfg.BuildGeometry();
            GateStateMachine sm = new GateStateMachine(geo, cfg.MinEngageTicks);
            ForceComposer held = new ForceComposer(geo, cfg);
            ForceComposer plain = new ForceComposer(cfg.BuildGeometry(), cfg);
            int x = geo.ColumnTarget(Column.C2);
            int full = GateGeometry.ForceMax;

            for (int y = Center; y >= 0; y -= 64)
                for (int i = 0; i < 10; i++) sm.Update(x, y, true);
            Assert.Equal(GateState.Engaged, sm.State);

            // The peak is read off the band, not the composed force: where the detent already
            // pushes the same way, the sum saturates at the clamp and hides a full-strength hold.
            double peakBand = 0;
            int lastEngaged = int.MaxValue;
            bool released = false;
            for (int y = 0; y <= GateGeometry.AxisMax && !released; y++)
            {
                sm.Update(x, y, true);
                int toll = held.Compose(sm.State, sm.Column, sm.Direction, x, y, gearHoldScale: 1.0).ConstantY
                         - plain.Compose(sm.State, sm.Column, sm.Direction, x, y, gearHoldScale: 0.0).ConstantY;

                if (sm.State == GateState.Engaged)
                {
                    peakBand = Math.Max(peakBand, held.GearHoldBand(geo.EngageFraction(sm.Direction, y)));
                    lastEngaged = Math.Abs(toll);
                }
                else
                {
                    released = true;
                    Assert.Equal(0, toll);
                }
            }

            Assert.True(released, "the gear never let go");
            Assert.True(lastEngaged <= full / 100, "the hold was " + lastEngaged + " DI as the latch let go");
            Assert.True(peakBand >= 0.99, "the hold's band peaked at only " + peakBand + " on the way out");
        }

        [Fact]
        public void TheUnconfirmedPolarityCapCoversTheHold()
        {
            // The 10% cap is the safety story for an unmeasured base; the hold has no path
            // around it.
            EngineConfig capped = HoldConfig();
            capped.PolarityConfirmed = false;
            ForceComposer c = Composer(capped);
            int y = FwdAt(capped, 0.75);

            int toll = c.SlotForceAt(ShiftDir.Fwd, y, muted: false, column: Column.C2)
                     - c.SlotForceAt(ShiftDir.Fwd, y, muted: false, column: Column.C2, gearHold: 1.0);
            Assert.InRange(toll, 0, 400);
        }

        // ---------------------------------------------------------------- through Compose

        [Fact]
        public void OnlyAnEngagedGearIsHeld()
        {
            // Traveling into a slot, the hold is nothing at all: entering is the grind's
            // business, and a balked shift must not be helped in by a band meant for leaving.
            EngineConfig cfg = HoldConfig();
            int y = FwdAt(cfg, 0.75);

            ForceComposer travelling = Composer(cfg);
            ForceComposer travellingNone = Composer(cfg);
            Assert.Equal(
                travellingNone.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd, 0, y).ConstantY,
                travelling.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd, 0, y, gearHoldScale: 1.0).ConstantY);
            Assert.Equal(0.0, travelling.GearHoldLevel);

            ForceComposer engaged = Composer(cfg);
            ForceComposer engagedNone = Composer(cfg);
            Assert.NotEqual(
                engagedNone.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, y).ConstantY,
                engaged.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, y, gearHoldScale: 1.0).ConstantY);
            Assert.Equal(1.0, engaged.GearHoldLevel);
        }

        [Fact]
        public void TheHoldComesInSlowlyAndLetsGoQuickly()
        {
            // A threshold clutch is a step; the force must not be. Rising takes the rise time,
            // falling the shorter fall time, and neither arrives in one tick.
            EngineConfig cfg = HoldConfig();
            ForceComposer c = Composer(cfg);
            int y = FwdAt(cfg, 0.75);

            c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, y, dtMs: 1.0, gearHoldScale: 1.0);
            Assert.InRange(c.GearHoldLevel, 0.0, 1.0 / ForceComposer.GearHoldRiseMs + 1e-9);

            for (int i = 1; i < (int)ForceComposer.GearHoldRiseMs; i++)
            {
                c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, y, dtMs: 1.0, gearHoldScale: 1.0);
            }
            Assert.Equal(1.0, c.GearHoldLevel, 6);

            c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, y, dtMs: 1.0, gearHoldScale: 0.0);
            Assert.True(c.GearHoldLevel > 0.0, "the hold dropped in one tick");

            for (int i = 1; i < (int)ForceComposer.GearHoldFallMs; i++)
            {
                c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, y, dtMs: 1.0, gearHoldScale: 0.0);
            }
            Assert.Equal(0.0, c.GearHoldLevel, 6);
        }

        [Fact]
        public void LeavingTheEngagedStateReleasesTheHold()
        {
            EngineConfig cfg = HoldConfig();
            ForceComposer c = Composer(cfg);
            int y = FwdAt(cfg, 0.75);

            c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, y, gearHoldScale: 1.0);
            Assert.Equal(1.0, c.GearHoldLevel);

            c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 0, Center, gearHoldScale: 1.0);
            Assert.Equal(0.0, c.GearHoldLevel);
        }

        // ---------------------------------------------------------------- the decision

        [Fact]
        public void FullThrottleAtLowRevsHoldsNearlyFully()
        {
            // 3000 of 7000 rpm: overrun is 30% x 3/7, so full drive nets 1 - 0.1286.
            EngineConfig cfg = HoldConfig();
            Assert.Equal(1.0 - 0.3 * 3.0 / 7.0,
                EffectComposer.GearHoldLoad(cfg, Driving(clutch: 0, throttle: 100), 0, 3), 6);
        }

        [Fact]
        public void ALiftHighInTheRevsHoldsOnEngineBraking()
        {
            // Overrun loads the dogs from the other side; the magnitude is the load.
            EngineConfig cfg = HoldConfig();
            TelemetryState t = Driving(clutch: 0, throttle: 0);
            t.Rpms = 7000;
            Assert.Equal(0.30, EffectComposer.GearHoldLoad(cfg, t, 0, 3), 6);

            t.Rpms = 1400;
            Assert.Equal(0.06, EffectComposer.GearHoldLoad(cfg, t, 0, 3), 6);
        }

        [Fact]
        public void AtTheBalancePointTheGearSlipsOut()
        {
            // Part throttle that exactly meets engine braking carries nothing through the box -
            // the realistic way out of gear without the clutch - and either side of it the
            // hold grows back in proportion.
            EngineConfig cfg = HoldConfig();
            TelemetryState t = Driving(clutch: 0);
            t.Rpms = 7000;

            t.Throttle = 30;
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, t, 0, 3), 6);

            t.Throttle = 40;
            double over = EffectComposer.GearHoldLoad(cfg, t, 0, 3);
            t.Throttle = 20;
            double under = EffectComposer.GearHoldLoad(cfg, t, 0, 3);
            Assert.Equal(0.10, over, 6);
            Assert.Equal(0.10, under, 6);
        }

        [Fact]
        public void WithoutEngineBrakingTheHoldIsTheThrottle()
        {
            EngineConfig cfg = HoldConfig();
            cfg.GearHoldEngineBrakePct = 0;
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, Driving(clutch: 0, throttle: 0), 0, 3), 6);
            Assert.Equal(0.5, EffectComposer.GearHoldLoad(cfg, Driving(clutch: 0, throttle: 50), 0, 3), 6);
        }

        [Fact]
        public void AGameWithNoRedlineUsesTheFallback()
        {
            EngineConfig cfg = HoldConfig();
            TelemetryState t = Driving(clutch: 0, throttle: 0);
            t.MaxRpm = 0;
            t.Rpms = EffectComposer.FallbackRedlineRpm;
            Assert.Equal(0.30, EffectComposer.GearHoldLoad(cfg, t, 0, 3), 6);
        }

        [Fact]
        public void APropertyLoadIsProportionalAndSigned()
        {
            // A game's own torque: its magnitude against the full scale, so overrun (negative
            // torque) holds as firmly as drive.
            EngineConfig cfg = HoldConfig();
            cfg.GearHoldLoadSource = GearHoldLoadSource.Property;
            cfg.GearHoldPropertyFullScale = 400;

            TelemetryState t = Driving(clutch: 0, throttle: 0);
            t.GearLoadValue = 200;
            Assert.Equal(0.5, EffectComposer.GearHoldLoad(cfg, t, 0, 3), 6);
            t.GearLoadValue = -100;
            Assert.Equal(0.25, EffectComposer.GearHoldLoad(cfg, t, 0, 3), 6);
            t.GearLoadValue = 900;
            Assert.Equal(1.0, EffectComposer.GearHoldLoad(cfg, t, 0, 3), 6);

            // No reading is no hold - never a guess.
            t.GearLoadValue = double.NaN;
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, t, 0, 3));
        }

        [Fact]
        public void ThePressedClutchReleasesTheHold()
        {
            EngineConfig cfg = HoldConfig();
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, Driving(clutch: 100, throttle: 100), 0, 3));
        }

        [Fact]
        public void TheProgressiveClutchFeathersTheHold()
        {
            // The hold reads the clutch exactly as the grind does: in the fading mode, half way
            // from the bite point to fully released is half engaged.
            EngineConfig cfg = HoldConfig();
            cfg.GrindClutchMode = GrindClutchMode.Progressive;
            cfg.ClutchBitePointPct = 40;

            double fullyUp = EffectComposer.GearHoldLoad(cfg, Driving(clutch: 0, throttle: 100), 0, 3);
            Assert.Equal(0.5 * fullyUp, EffectComposer.GearHoldLoad(cfg, Driving(clutch: 20, throttle: 100), 0, 3), 6);
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, Driving(clutch: 45, throttle: 100), 0, 3), 6);
        }

        [Fact]
        public void EveryUncertainReadingResolvesToNoHold()
        {
            // The failure that matters is a gear the hand cannot leave, so anything doubtful
            // releases: the setting off, no gear held, no game, stale telemetry, a stale
            // clutch, a stopped engine, a car below the speed floor, a non-finite reading.
            EngineConfig cfg = HoldConfig();
            TelemetryState t = Driving(clutch: 0, throttle: 100);

            Assert.Equal(0.0, EffectComposer.GearHoldLoad(HoldConfig(enabled: false), t, 0, 3));
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, t, 0, 0));
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, null, 0, 3));
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, t, EffectComposer.StaleAfterMs + 1, 3));
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, t, -1, 3));

            TelemetryState noGame = Driving(clutch: 0, throttle: 100);
            noGame.GameRunning = false;
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, noGame, 0, 3));

            TelemetryState staleClutch = Driving(clutch: 0, throttle: 100);
            staleClutch.CapturedAtTick = 10000;
            staleClutch.ClutchCapturedAtTick = 10000 - EffectComposer.StaleAfterMs - 1;
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, staleClutch, 0, 3));

            TelemetryState stopped = Driving(clutch: 0, throttle: 100);
            stopped.Rpms = 0;
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, stopped, 0, 3));

            EngineConfig floored = HoldConfig();
            floored.GrindMinSpeedKmh = 10;
            TelemetryState crawling = Driving(clutch: 0, throttle: 100);
            crawling.SpeedKmh = 5;
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(floored, crawling, 0, 3));

            TelemetryState nanClutch = Driving(clutch: double.NaN, throttle: 100);
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, nanClutch, 0, 3));

            TelemetryState nanThrottle = Driving(clutch: 0, throttle: double.NaN);
            Assert.Equal(0.0, EffectComposer.GearHoldLoad(cfg, nanThrottle, 0, 3));
        }

        [Fact]
        public void TheEffectStepReportsTheHoldAndNeverBlocksAnything()
        {
            // The hold rides out of the effects step as a target and nothing else: it must not
            // block engagement or mute a detent, and stale telemetry silences it like a carrier.
            EngineConfig cfg = HoldConfig();
            var fx = new EffectComposer();
            TelemetryState t = Driving(clutch: 0, throttle: 100);

            EffectOutput held = fx.Step(cfg, t, 0, 1.0, approachingSlot: false, heldGear: 3);
            Assert.Equal(EffectComposer.GearHoldLoad(cfg, t, 0, 3), held.GearHoldScale, 6);
            Assert.True(held.GearHoldScale > 0.8);
            Assert.False(held.BlockEngage);
            Assert.False(held.MuteDetent);

            EffectOutput stale = fx.Step(cfg, t, EffectComposer.StaleAfterMs + 1, 1.0, approachingSlot: false, heldGear: 3);
            Assert.Equal(0.0, stale.GearHoldScale);
        }

        [Fact]
        public void TheSettingsReachTheEngineAndTheEffectsResetRestoresThem()
        {
            var s = new ShifterSettings
            {
                GearHoldEnabled = true,
                GearHoldPct = 70,
                GearHoldEngineBrakePct = 10,
                GearHoldLoadSource = GearHoldLoadSource.Property,
                GearHoldPropertyFullScale = 350
            };
            EngineConfig cfg = s.ToEngineConfig();
            Assert.True(cfg.GearHoldEnabled);
            Assert.Equal(70, cfg.GearHoldPct);
            Assert.Equal(10, cfg.GearHoldEngineBrakePct);
            Assert.Equal(GearHoldLoadSource.Property, cfg.GearHoldLoadSource);
            Assert.Equal(350.0, cfg.GearHoldPropertyFullScale);
            Assert.Equal(1, s.GearHoldLoadSourceIndex);

            s.GearHoldPct = 250;
            Assert.Equal(100, s.GearHoldPct);

            s.ResetToDefaults(ShifterSettings.ResetScope.Effects);
            var defaults = new ShifterSettings();
            Assert.Equal(defaults.GearHoldEnabled, s.GearHoldEnabled);
            Assert.Equal(defaults.GearHoldPct, s.GearHoldPct);
            Assert.Equal(defaults.GearHoldEngineBrakePct, s.GearHoldEngineBrakePct);
            Assert.Equal(defaults.GearHoldLoadSource, s.GearHoldLoadSource);
            Assert.Equal(defaults.GearHoldPropertyFullScale, s.GearHoldPropertyFullScale);
            Assert.False(defaults.GearHoldEnabled);
        }

        [Fact]
        public void ASharedProfileCarriesTheHoldButNeverItsComboAdapter()
        {
            // The hold is tuning, so it travels. GearHoldLoadSourceIndex is the combo box's view
            // of GearHoldLoadSource and must not: an int adapter escapes the enum's IsDefined
            // check and is applied after it, so a file could otherwise set a source that does
            // not exist.
            var profile = new ShifterProfile
            {
                Name = "Hold",
                Settings = new ShifterSettings
                {
                    GearHoldEnabled = true,
                    GearHoldPct = 65,
                    GearHoldLoadSource = GearHoldLoadSource.Property,
                    GearHoldEngineBrakePct = 20,
                    GearHoldLoadProperty = "DataCorePlugin.GameRawData.Torque",
                    GearHoldPropertyFullScale = 450
                }
            };

            string json = ProfileTransfer.Export(profile);
            Assert.Null(JObject.Parse(json)["Settings"]["GearHoldLoadSourceIndex"]);

            ShifterSettings back = ProfileTransfer.Import(json, new ShifterSettings()).Profile.Settings;
            Assert.True(back.GearHoldEnabled);
            Assert.Equal(65, back.GearHoldPct);
            Assert.Equal(GearHoldLoadSource.Property, back.GearHoldLoadSource);
            Assert.Equal(20, back.GearHoldEngineBrakePct);
            Assert.Equal("DataCorePlugin.GameRawData.Torque", back.GearHoldLoadProperty);
            Assert.Equal(450, back.GearHoldPropertyFullScale);

            JObject hostile = JObject.Parse(json);
            hostile["Settings"]["GearHoldLoadSourceIndex"] = 7;
            hostile["Settings"]["GearHoldPropertyFullScale"] = 999999;
            hostile["Settings"]["GearHoldPct"] = 900;
            ShifterSettings clamped = ProfileTransfer.Import(hostile.ToString(), new ShifterSettings()).Profile.Settings;
            Assert.Equal(GearHoldLoadSource.Property, clamped.GearHoldLoadSource);
            Assert.Equal(5000, clamped.GearHoldPropertyFullScale);
            Assert.Equal(100, clamped.GearHoldPct);
        }

        [Fact]
        public void TheEngineScratchCopyCarriesTheThrottle()
        {
            // The directly-read pedal path refills a scratch snapshot; dropping the throttle
            // there would silently turn every hold into the unloaded one.
            TelemetryState source = Driving(clutch: 0, throttle: 80);
            source.GearLoadValue = 123;
            var scratch = new TelemetryState();
            scratch.CopyFromWithClutch(source, 10.0);
            Assert.Equal(80.0, scratch.Throttle);
            Assert.Equal(123.0, scratch.GearLoadValue);
        }
    }
}
