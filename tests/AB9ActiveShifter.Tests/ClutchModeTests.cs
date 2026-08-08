using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The clutch's two jobs: deciding how hard a clutchless shift grinds, and marking where the
    /// bite point is. Threshold mode has to stay exactly what shipped before the mode existed -
    /// it is the default, so a drift here would change every existing profile's feel silently.
    /// </summary>
    public class ClutchModeTests
    {
        private static EngineConfig Config(GrindClutchMode mode, int threshold = 25, int bite = 25)
        {
            return new EngineConfig
            {
                GrindClutchMode = mode,
                GrindClutchThresholdPct = threshold,
                ClutchBitePointPct = bite
            };
        }

        [Fact]
        public void ThresholdModeIsExactlyTheOriginalStep()
        {
            // The original condition, verbatim: clutch < threshold grinds, anything else does
            // not. Full strength on the grinding side, so amplitudes are untouched by the new
            // multiplier. Swept rather than spot-checked because "the default still behaves as
            // it always did" is the whole promise of shipping this as a mode.
            EngineConfig cfg = Config(GrindClutchMode.Threshold, threshold: 25);

            for (int clutch = 0; clutch <= 100; clutch++)
            {
                double e = EffectComposer.ClutchEngagement(cfg, clutch);
                Assert.Equal(clutch < 25 ? 1.0 : 0.0, e);
            }
        }

        [Fact]
        public void ProgressiveModeFadesFromTheBitePointToFullyReleased()
        {
            EngineConfig cfg = Config(GrindClutchMode.Progressive, bite: 40);

            Assert.Equal(1.0, EffectComposer.ClutchEngagement(cfg, 0), 3);    // fully up
            Assert.Equal(0.5, EffectComposer.ClutchEngagement(cfg, 20), 3);   // half way
            Assert.Equal(0.0, EffectComposer.ClutchEngagement(cfg, 40), 3);   // at the bite
            Assert.Equal(0.0, EffectComposer.ClutchEngagement(cfg, 100), 3);  // floored
        }

        [Fact]
        public void ProgressiveModeIgnoresTheThresholdDial()
        {
            // The threshold belongs to Threshold mode. Letting it also clip Progressive would
            // give two dials authority over one decision and make the fade end somewhere the
            // bite point does not explain.
            EngineConfig loose = Config(GrindClutchMode.Progressive, threshold: 5, bite: 50);
            EngineConfig tight = Config(GrindClutchMode.Progressive, threshold: 90, bite: 50);

            for (int clutch = 0; clutch <= 100; clutch += 5)
            {
                Assert.Equal(EffectComposer.ClutchEngagement(loose, clutch),
                             EffectComposer.ClutchEngagement(tight, clutch), 6);
            }
        }

        [Fact]
        public void EngagementIsAlwaysBoundedAndNeverIncreasesWithTheClutch()
        {
            // Pressing the clutch further can only ever reduce the disagreement, in either mode.
            // A non-monotonic answer would mean a pedal that grinds harder the more it is pressed.
            foreach (GrindClutchMode mode in new[] { GrindClutchMode.Threshold, GrindClutchMode.Progressive })
            {
                foreach (int bite in new[] { 0, 1, 25, 60, 100 })
                {
                    EngineConfig cfg = Config(mode, bite: bite);
                    double previous = double.MaxValue;

                    for (int clutch = 0; clutch <= 100; clutch++)
                    {
                        double e = EffectComposer.ClutchEngagement(cfg, clutch);
                        Assert.InRange(e, 0.0, 1.0);
                        Assert.True(e <= previous,
                            mode + " with bite " + bite + " rose at clutch " + clutch);
                        previous = e;
                    }
                }
            }
        }

        [Fact]
        public void AClutchReadingOutsideZeroToOneHundredCannotEscapeTheBounds()
        {
            // A pedal axis read directly can hand over anything if a calibration is stale or a
            // device is swapped. It must not turn into a negative amplitude or an over-unity one.
            EngineConfig cfg = Config(GrindClutchMode.Progressive, bite: 30);

            Assert.Equal(1.0, EffectComposer.ClutchEngagement(cfg, -500), 3);
            Assert.Equal(0.0, EffectComposer.ClutchEngagement(cfg, 5000), 3);
        }

        [Fact]
        public void ABitePointOfZeroNeverDividesByZero()
        {
            EngineConfig cfg = Config(GrindClutchMode.Progressive, bite: 0);

            for (int clutch = 0; clutch <= 100; clutch += 10)
            {
                double e = EffectComposer.ClutchEngagement(cfg, clutch);
                Assert.InRange(e, 0.0, 1.0);
            }
        }

        // ---------- the bite point felt through the lever ----------

        private static EngineConfig BiteConfig()
        {
            return new EngineConfig
            {
                PolarityConfirmed = true,
                OverallGainPct = 100,
                ClutchBitePointPct = 40,
                FxBiteEnabled = true,
                FxBiteGainPct = 60,
                FxBiteDurationMs = 60
            };
        }

        private static TelemetryState AtClutch(double clutch)
        {
            return new TelemetryState
            {
                GameRunning = true,
                Rpms = 2000,
                SpeedKmh = 40,
                Clutch = clutch,
                Gear = "3"
            };
        }

        /// <summary>Runs some ticks at one clutch position and returns the loudest carrier seen.</summary>
        private static int Peak(EffectComposer fx, EngineConfig cfg, double clutch, int ticks = 40)
        {
            int peak = 0;
            for (int i = 0; i < ticks; i++)
            {
                EffectOutput o = fx.Step(cfg, AtClutch(clutch), 0, 1.0, false);
                if (System.Math.Abs(o.VibY) > peak) peak = System.Math.Abs(o.VibY);
            }
            return peak;
        }

        [Fact]
        public void AdoptingTheFirstClutchReadingIsSilent()
        {
            // The trap this guards: seeding "was the clutch engaged" from a default rather than
            // from the first reading fires a pulse the moment any game connects with the pedal
            // released. A cue that goes off on every session start is a cue nobody feels.
            var fx = new EffectComposer();
            EngineConfig cfg = BiteConfig();

            Assert.Equal(0, Peak(fx, cfg, clutch: 0));
        }

        [Fact]
        public void CrossingTheBitePointPulsesInEitherDirection()
        {
            EngineConfig cfg = BiteConfig();

            var pressing = new EffectComposer();
            Peak(pressing, cfg, clutch: 0);                        // adopt, released
            Assert.True(Peak(pressing, cfg, clutch: 80) > 0,
                "no pulse when the clutch was pressed through the bite point");

            var lifting = new EffectComposer();
            Peak(lifting, cfg, clutch: 80);                        // adopt, pressed
            Assert.True(Peak(lifting, cfg, clutch: 0) > 0,
                "no pulse when the clutch was lifted back through the bite point");
        }

        [Fact]
        public void MovingWithoutCrossingIsSilent()
        {
            // Riding the clutch below the bite point must not rattle continuously.
            var fx = new EffectComposer();
            EngineConfig cfg = BiteConfig();

            Peak(fx, cfg, clutch: 0);
            Assert.Equal(0, Peak(fx, cfg, clutch: 10));
            Assert.Equal(0, Peak(fx, cfg, clutch: 35));
            Assert.Equal(0, Peak(fx, cfg, clutch: 20));
        }

        [Fact]
        public void TheBitePulseObeysItsOwnSwitch()
        {
            var fx = new EffectComposer();
            EngineConfig cfg = BiteConfig();
            cfg.FxBiteEnabled = false;

            Peak(fx, cfg, clutch: 0);
            Assert.Equal(0, Peak(fx, cfg, clutch: 80));
        }

        [Fact]
        public void StaleTelemetrySilencesTheBitePulseAndReArmsItQuietly()
        {
            // Same safety rule every carrier obeys: a game that pauses or exits must not leave a
            // buzz running. And the re-adoption afterwards has to be silent, or every unpause
            // fires a phantom pulse.
            var fx = new EffectComposer();
            EngineConfig cfg = BiteConfig();

            Peak(fx, cfg, clutch: 0);

            EffectOutput stale = fx.Step(cfg, AtClutch(80), EffectComposer.StaleAfterMs + 1, 1.0, false);
            Assert.Equal(0, stale.VibY);

            Assert.Equal(0, Peak(fx, cfg, clutch: 80));
        }
    }
}
