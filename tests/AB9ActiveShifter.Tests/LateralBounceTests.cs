using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// An outer column's lever bouncing off its inner slot wall, driven by the base's centring
    /// spring. Measured in trace-20261113-192727 at AB9 onboard Spring 50: 12000 counts at
    /// ~13 Hz in both outer columns, the middle column quiet. The model below is a lever of
    /// fixed mass between that spring and the composed wall, with the measured round trip as a
    /// pure delay; its constants were fitted to the trace's free flight (spring alone) and wall
    /// contact (wall against spring), and it reproduces the bounce at the traced settings.
    /// In the gate's frame, before the polarity signs, like every force test.
    /// </summary>
    public class LateralBounceTests
    {
        private const int Deep = 61400;

        /// <summary>The 5+R profile the trace was taken on, as far as the lateral field goes.</summary>
        private static EngineConfig Traced()
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                Pattern = GatePattern.H5R,
                PatternWidthPct = 60,
                SlotHalfWidth = 2400,
                ColumnPinForcePct = 80,
                ChannelWallForcePct = 100,
                ChannelGuideForcePct = 8.96,
                ColumnDetentForcePct = 0,
                PatternEdgeForcePct = 100,
                MouthShape = SlotMouthShape.Angled,
                MouthDepth = 12000,
                ChannelHalfEnter = 3268,
                ChannelHalfExit = 4051,
                ChannelFreeDepth = 2165,
                EngageDepth = 20852,
                ReleaseDepth = 23852,
                DetentHoldPct = 40,
                DetentPullPct = 0,
                DetentResistPct = 0,
                WallRamp = 3816,
                WallAttackMs = 15,
                WallYieldPct = 10,
                YieldRecoveryMs = 20,
                YieldVelocityDeadband = 10000,
                YieldVelocityBlend = 12000,
                WallFrictionPct = 5,
                DampingPct = 0,
                BaseSpringPct = 50
            };
        }

        /// <summary>The same gate at the shipped stabiliser defaults.</summary>
        private static EngineConfig Defaults()
        {
            EngineConfig cfg = Traced();
            cfg.WallAttackMs = 0;
            cfg.WallYieldPct = 45;
            return cfg;
        }

        /// <summary>
        /// Releases the lever at the outer column's centre, lets the spring carry it into the
        /// inner wall, and returns the peak-to-peak lateral swing over the third second.
        /// </summary>
        private static int Swing(EngineConfig cfg, int delayTicks, double physicalDamping)
        {
            // Per DI unit and per count from centre, in counts/s^2 - fitted to the trace.
            const double PerDi = 5600;
            const double Spring = 1600;

            GateGeometry geo = cfg.BuildGeometry();
            var composer = new ForceComposer(geo, cfg);
            var estimator = new VelocityEstimator();
            var inFlight = new int[delayTicks + 1];

            double x = geo.ColumnTarget(Column.C1), v = 0;
            double min = double.MaxValue, max = double.MinValue;

            for (int tick = 0; tick < 3000; tick++)
            {
                int reported = (int)Math.Round(x);
                estimator.Update(reported, Deep, tick);
                ForceFrame f = composer.Compose(GateState.Engaged, Column.C1, ShiftDir.Back,
                    reported, Deep, estimator.X, estimator.Y, 1.0);

                Array.Copy(inFlight, 1, inFlight, 0, delayTicks);
                inFlight[delayTicks] = f.ConstantX;
                int acting = inFlight[0];

                for (int step = 0; step < 10; step++)
                {
                    double a = PerDi * acting + Spring * (GateGeometry.AxisCenter - x) - physicalDamping * v;
                    v += a * 0.0001;
                    x += v * 0.0001;
                    if (x < 0) { x = 0; v = 0; }
                    if (x > 65535) { x = 65535; v = 0; }
                }

                if (tick >= 2000)
                {
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }

            return (int)(max - min);
        }

        [Fact]
        public void TheBounceNeedsTheDelay()
        {
            // The premise: at the traced settings the bounce is a property of the round trip,
            // not of the wall's shape. With no delay the lever settles.
            Assert.Equal(0, Swing(Traced(), 0, 15));
        }

        [Fact]
        public void TheTracedOuterColumnBounceDies()
        {
            // 11105 counts of swing before the cap, from a model fitted to a trace that swung
            // 12069. The cap takes it under a few hundred at the measured round trip.
            int swing = Swing(Traced(), 3, 15);
            Assert.True(swing < 500, "the outer column still swings " + swing + " counts");
        }

        [Theory]
        [InlineData(4, 4000)]
        [InlineData(5, 6000)]
        public void ALongerRoundTripStillLosesMostOfTheBounce(int delayTicks, int bound)
        {
            // 14915 and 26236 counts before the cap; 2722 and 4563 with it. The measured delay
            // is about 4 ms, and the residue there is a limit cycle at the turnaround that no
            // software force reaches in time - the base's own damper is what removes it.
            int swing = Swing(Traced(), delayTicks, 15);
            Assert.True(swing < bound, "at " + delayTicks + " ms the column still swings " + swing + " counts");
        }

        [Fact]
        public void WithNoPhysicalDampingTheBounceIsStillContained()
        {
            // Without the base's own damper and friction the uncapped lever crosses the whole
            // axis (49970 counts). The wall alone has to be the loss: 2431 with the cap.
            int swing = Swing(Traced(), 3, 0);
            Assert.True(swing < 4000, "the undamped column still swings " + swing + " counts");
        }

        [Fact]
        public void ThePhysicalDamperIsWhatEndsTheResidue()
        {
            // At the measured 4 ms the cap leaves a turnaround cycle it cannot reach. Twice the
            // physical damping the model was fitted with takes it from 2722 to 149: a zero-latency term acts
            // inside the delay, which no rendered force can (docs/hardware.md).
            Assert.True(Swing(Traced(), 4, 15) > 1000, "the premise: the cap alone leaves a residue at 4 ms");
            int swing = Swing(Traced(), 4, 30);
            Assert.True(swing < 500, "with more physical damping the column still swings " + swing + " counts");
        }

        [Fact]
        public void TheDefaultTuneDoesNotGetWorse()
        {
            // 769 counts before the cap, 724 with it: a cap that reached down to a lever
            // resting on its wall turned into a relay there (2762), which the launch-speed
            // fade exists to prevent.
            int swing = Swing(Defaults(), 3, 15);
            Assert.True(swing < 1000, "the default tune now hunts " + swing + " counts");
        }

        /// <summary>Just outside the inner wall's corridor of the first column, on its face.</summary>
        private static int OnTheInnerFace(EngineConfig cfg)
        {
            return cfg.BuildGeometry().ColumnTarget(Column.C1) + cfg.SlotHalfWidth + (cfg.WallRamp / 2);
        }

        private static EngineConfig Bare()
        {
            EngineConfig cfg = Defaults();
            cfg.WallFrictionPct = 0;
            cfg.WallYieldPct = 0;
            return cfg;
        }

        [Fact]
        public void ALaunchWithNothingStoredIsCutToNothing()
        {
            // A wall that never took any energy has none to give: a lever leaving it fast,
            // on the first tick of contact, gets no push.
            EngineConfig cfg = Bare();
            var c = new ForceComposer(cfg.BuildGeometry(), cfg);
            int x = OnTheInnerFace(cfg);

            int wall = c.LateralGuide(x, Deep, Column.C1);
            Assert.True(wall < -1000, "expected the inner wall to push back toward the column, got " + wall);

            int pushed = c.Compose(GateState.Engaged, Column.C1, ShiftDir.Back, x, Deep, -300000, 0, 1.0).ConstantX;
            Assert.Equal(0, pushed);
        }

        [Theory]
        [InlineData(-5000)]
        [InlineData(-40000)]
        [InlineData(-60000)]
        public void ASlowRetreatIsNeverCut(int velocity)
        {
            // Below the launch speed a lever leaving the wall is a hand adjusting, not a
            // rebound: the wall stays whole, whatever the ledger holds.
            EngineConfig cfg = Bare();
            var c = new ForceComposer(cfg.BuildGeometry(), cfg);
            int x = OnTheInnerFace(cfg);

            int wall = c.LateralGuide(x, Deep, Column.C1);
            int pushed = c.Compose(GateState.Engaged, Column.C1, ShiftDir.Back, x, Deep, velocity, 0, 1.0).ConstantX;
            Assert.Equal(wall, pushed);
        }

        [Fact]
        public void ALeanIsNeverCut()
        {
            // Moving into the wall, at any speed, the wall resists whole.
            EngineConfig cfg = Bare();
            var c = new ForceComposer(cfg.BuildGeometry(), cfg);
            int x = OnTheInnerFace(cfg);

            int wall = c.LateralGuide(x, Deep, Column.C1);
            int pushed = c.Compose(GateState.Engaged, Column.C1, ShiftDir.Back, x, Deep, 300000, 0, 1.0).ConstantX;
            Assert.Equal(wall, pushed);
        }

        [Fact]
        public void TheTunnelIsUntouched()
        {
            // Neutral keeps its lateral field: there the guide can change hands, and a push
            // with nothing stored behind it is the field doing its job.
            EngineConfig cfg = Bare();
            int x = OnTheInnerFace(cfg);
            int y = GateGeometry.AxisCenter;

            var reference = new ForceComposer(cfg.BuildGeometry(), cfg);
            reference.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, 0, 0, 0);
            int still = reference.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, 0, 0, 0).ConstantX;

            var c = new ForceComposer(cfg.BuildGeometry(), cfg);
            c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, 0, 0, 0);
            int launched = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, -300000, 0, 0).ConstantX;
            Assert.Equal(still, launched);
        }
    }
}
