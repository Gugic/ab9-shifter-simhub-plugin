using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The telemetry effects: vibration carriers keyed on game state, and the clutch grind.
    /// Everything runs against synthetic telemetry stepped at 1 ms, the engine's own tick.
    ///
    /// Two properties here are safety, not feel: stale telemetry silences everything at once
    /// (a hung game must not leave a buzz running against the hand), and the amplitudes obey
    /// the same effective gain as the gate, unconfirmed-polarity cap included.
    /// </summary>
    public class EffectComposerTests
    {
        private static EngineConfig FullGainConfig()
        {
            return new EngineConfig { OverallGainPct = 100, PolarityConfirmed = true };
        }

        private static TelemetryState Driving()
        {
            return new TelemetryState
            {
                GameRunning = true,
                Rpms = 3000,
                MaxRpm = 7000,
                SpeedKmh = 60,
                Clutch = 0,
                Gear = "3"
            };
        }

        private static int PeakVib(EffectComposer fx, EngineConfig cfg, TelemetryState t,
                                   bool approaching = false, int ticks = 120)
        {
            int peak = 0;
            for (int i = 0; i < ticks; i++)
            {
                EffectOutput o = fx.Step(cfg, t, 0, 1.0, approaching);
                peak = Math.Max(peak, Math.Abs(o.VibY));
            }
            return peak;
        }

        // ---------------------------------------------------------------- the quiet default

        [Fact]
        public void NothingPlaysWithEveryEffectDisabled()
        {
            // The effects are additions to the gate. A fresh config must be exactly the gate
            // that existed before they did - no vibration, no grind, no blocked engagement.
            EngineConfig cfg = FullGainConfig();
            var fx = new EffectComposer();
            TelemetryState t = Driving();

            for (int i = 0; i < 200; i++)
            {
                EffectOutput o = fx.Step(cfg, t, 0, 1.0, true);
                Assert.Equal(0, o.VibY);
                Assert.False(o.GrindActive);
                Assert.False(o.BlockEngage);
                Assert.False(o.MuteDetent);
            }
        }

        // ---------------------------------------------------------------- amplitude and gain

        [Fact]
        public void AbsBuzzesOnlyWhileAbsIsActive()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FxAbsEnabled = true;
            cfg.FxAbsGainPct = 100;

            var fx = new EffectComposer();
            TelemetryState quiet = Driving();
            Assert.Equal(0, PeakVib(fx, cfg, quiet));

            TelemetryState braking = Driving();
            braking.AbsActive = true;
            int peak = PeakVib(fx, cfg, braking);

            // Full volume at full gain is the full-scale budget; a 1 kHz sampling of a 44 Hz
            // sine lands within a percent of the true peak.
            Assert.InRange(peak, 2900, EffectComposer.VibFullScale);

            Assert.Equal(0, PeakVib(fx, cfg, quiet));
        }

        [Fact]
        public void TheGainCapAppliesToEffectsToo()
        {
            // Until polarity is measured the gate is capped at 10%, and 12 Nm of vibration
            // needs the same cap - a carrier has no polarity, but it has an amplitude.
            EngineConfig cfg = FullGainConfig();
            cfg.PolarityConfirmed = false;
            cfg.FxAbsEnabled = true;
            cfg.FxAbsGainPct = 100;

            TelemetryState t = Driving();
            t.AbsActive = true;

            int peak = PeakVib(new EffectComposer(), cfg, t);
            Assert.InRange(peak, 250, EffectComposer.VibFullScale / 10);
        }

        // ---------------------------------------------------------------- freshness

        [Fact]
        public void EffectsDieTheMomentTelemetryGoesStale()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FxAbsEnabled = true;
            cfg.FxAbsGainPct = 100;
            cfg.GrindEnabled = true;

            TelemetryState t = Driving();
            t.AbsActive = true;

            var fx = new EffectComposer();
            Assert.True(PeakVib(fx, cfg, t, approaching: true) > 0);

            // One stale tick is enough: no decay, no last cycle. Stale means silent.
            EffectOutput stale = fx.Step(cfg, t, EffectComposer.StaleAfterMs + 1, 1.0, true);
            Assert.Equal(0, stale.VibY);
            Assert.False(stale.GrindActive);
            Assert.False(stale.BlockEngage);

            // A negative age is the tick-counter wrap's pathological edge; treat it as stale
            // rather than fresh, because fresh is the dangerous direction to be wrong in.
            Assert.Equal(0, fx.Step(cfg, t, -5, 1.0, true).VibY);

            TelemetryState gone = Driving();
            gone.GameRunning = false;
            gone.AbsActive = true;
            Assert.Equal(0, fx.Step(cfg, gone, 0, 1.0, true).VibY);
        }

        // ---------------------------------------------------------------- the carriers

        [Fact]
        public void TheLimiterWaitsForTheRedline()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FxLimiterEnabled = true;
            cfg.FxLimiterGainPct = 100;

            TelemetryState cruising = Driving();
            cruising.Rpms = 6000;
            Assert.Equal(0, PeakVib(new EffectComposer(), cfg, cruising));

            TelemetryState banging = Driving();
            banging.Rpms = 6800;
            Assert.True(PeakVib(new EffectComposer(), cfg, banging) > 2000);

            // A game that reports no redline gets no limiter effect at any revs, rather than
            // one that fires against a zero.
            TelemetryState unreported = Driving();
            unreported.Rpms = 9000;
            unreported.MaxRpm = 0;
            Assert.Equal(0, PeakVib(new EffectComposer(), cfg, unreported));
        }

        [Fact]
        public void EngineVibrationTracksTheRevsThroughItsOrder()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FxEngineEnabled = true;
            cfg.FxEngineGainPct = 100;

            // 3000 rpm at order 1 is 50 Hz: a thousand 1 ms ticks hold a hundred sign flips.
            Assert.InRange(CountSignFlips(cfg, 3000, 1.0), 90, 110);

            // Order 2 doubles the pitch without touching anything else.
            Assert.InRange(CountSignFlips(cfg, 3000, 2.0), 180, 220);

            // An engine that is not turning is silent - no idle hum in the menus.
            TelemetryState off = Driving();
            off.Rpms = 0;
            Assert.Equal(0, PeakVib(new EffectComposer(), cfg, off));
        }

        private static int CountSignFlips(EngineConfig cfg, double rpm, double order)
        {
            cfg.FxEngineOrder = order;
            TelemetryState t = Driving();
            t.Rpms = rpm;

            var fx = new EffectComposer();
            int flips = 0;
            int lastSign = 0;
            for (int i = 0; i < 1000; i++)
            {
                int v = fx.Step(cfg, t, 0, 1.0, false).VibY;
                if (v == 0) continue;
                int sign = Math.Sign(v);
                if (lastSign != 0 && sign != lastSign) flips++;
                lastSign = sign;
            }
            return flips;
        }

        [Fact]
        public void AShiftPulseFiresOncePerGearChangeAndForItsDurationOnly()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FxShiftEnabled = true;
            cfg.FxShiftGainPct = 100;
            cfg.FxShiftDurationMs = 80;

            var fx = new EffectComposer();

            // The first gear ever seen is adopted silently - a game starting up must not
            // greet the hand with a phantom shift.
            TelemetryState third = Driving();
            Assert.Equal(0, PeakVib(fx, cfg, third, ticks: 50));

            // The change fires a pulse of the configured length...
            TelemetryState fourth = Driving();
            fourth.Gear = "4";
            Assert.InRange(PeakVib(fx, cfg, fourth, ticks: 80), 2900, EffectComposer.VibFullScale);

            // ...and then stops, even though the new gear is still the new gear.
            Assert.Equal(0, PeakVib(fx, cfg, fourth, ticks: 100));
        }

        [Fact]
        public void TheCustomPropertyScalesItsVolume()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FxCustomEnabled = true;
            cfg.FxCustomGainPct = 100;

            TelemetryState half = Driving();
            half.CustomValue = 50;
            Assert.InRange(PeakVib(new EffectComposer(), cfg, half), 1450, 1500);

            TelemetryState silent = Driving();
            silent.CustomValue = 0;
            Assert.Equal(0, PeakVib(new EffectComposer(), cfg, silent));
        }

        // ---------------------------------------------------------------- the grind

        [Fact]
        public void GrindNeedsEveryConditionAtOnce()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.GrindEnabled = true;

            var fx = new EffectComposer();

            // All conditions met: pushing into a slot, clutch up, moving, engine turning.
            EffectOutput grinding = fx.Step(cfg, Driving(), 0, 1.0, true);
            Assert.True(grinding.GrindActive);
            Assert.True(Math.Abs(grinding.VibY) > 2000);

            // Not pushing into a slot - sliding the tunnel, seated in a gear, or sequential.
            Assert.False(fx.Step(cfg, Driving(), 0, 1.0, false).GrindActive);

            // Clutch down: the whole point.
            TelemetryState clutched = Driving();
            clutched.Clutch = 80;
            Assert.False(fx.Step(cfg, clutched, 0, 1.0, true).GrindActive);

            // Engine off: nothing is spinning, nothing grinds.
            TelemetryState engineOff = Driving();
            engineOff.Rpms = 100;
            Assert.False(fx.Step(cfg, engineOff, 0, 1.0, true).GrindActive);

            // Below the speed floor, when one is set - the garage stays quiet.
            cfg.GrindMinSpeedKmh = 10;
            TelemetryState crawling = Driving();
            crawling.SpeedKmh = 5;
            Assert.False(fx.Step(cfg, crawling, 0, 1.0, true).GrindActive);
        }

        [Fact]
        public void GrindBlocksAndMutesOnlyWhenRejectionIsOn()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.GrindEnabled = true;

            cfg.GrindRejectsGear = true;
            EffectOutput balked = new EffectComposer().Step(cfg, Driving(), 0, 1.0, true);
            Assert.True(balked.GrindActive);
            Assert.True(balked.BlockEngage);
            Assert.True(balked.MuteDetent);

            // Rejection off: the box complains but takes the gear, detent intact.
            cfg.GrindRejectsGear = false;
            EffectOutput tolerant = new EffectComposer().Step(cfg, Driving(), 0, 1.0, true);
            Assert.True(tolerant.GrindActive);
            Assert.False(tolerant.BlockEngage);
            Assert.False(tolerant.MuteDetent);
        }

        [Fact]
        public void TheSummedCarriersStayInsideTheVibrationBudget()
        {
            // Every effect at full volume at once. The sum is clamped to the vibration
            // budget, so stacked effects stay a texture and the gate keeps its authority.
            EngineConfig cfg = FullGainConfig();
            cfg.FxEngineEnabled = true;
            cfg.FxEngineGainPct = 100;
            cfg.FxLimiterEnabled = true;
            cfg.FxLimiterGainPct = 100;
            cfg.FxAbsEnabled = true;
            cfg.FxAbsGainPct = 100;
            cfg.FxTcEnabled = true;
            cfg.FxTcGainPct = 100;
            cfg.GrindEnabled = true;
            cfg.GrindGainPct = 100;

            TelemetryState t = Driving();
            t.Rpms = 6900;
            t.AbsActive = true;
            t.TcActive = true;

            var fx = new EffectComposer();
            int peak = 0;
            for (int i = 0; i < 300; i++)
            {
                int v = Math.Abs(fx.Step(cfg, t, 0, 1.0, true).VibY);
                Assert.True(v <= EffectComposer.VibTotalMax, "vibration escaped the budget: " + v);
                peak = Math.Max(peak, v);
            }

            Assert.True(peak > 3000, "the stack should actually reach real amplitude: " + peak);
        }

        [Fact]
        public void TheGrindReplaysExactly()
        {
            // The jitter is a seeded LCG, no clock and no Random, so a recorded complaint
            // replays tick for tick - the property every debugging session here has leaned on.
            EngineConfig cfg = FullGainConfig();
            cfg.GrindEnabled = true;

            var a = new EffectComposer();
            var b = new EffectComposer();
            TelemetryState t = Driving();

            for (int i = 0; i < 300; i++)
            {
                Assert.Equal(a.Step(cfg, t, 0, 1.0, true).VibY, b.Step(cfg, t, 0, 1.0, true).VibY);
            }
        }
    }
}
