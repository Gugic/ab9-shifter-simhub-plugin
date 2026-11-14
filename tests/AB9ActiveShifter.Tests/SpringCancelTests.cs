using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The AB9 onboard spring, cancelled in a slot only while a throw is being caught. Measured:
    /// the spring pulls ~0.18 DI per count toward the axis centre at Spring 50, and an outer
    /// column's lever thrown into gear bounced off its inner wall instead of settling. A gentle
    /// entry rested calmly against that wall, which is the feel the spring is kept for.
    /// In the gate's frame, before the polarity signs, like every force test.
    /// </summary>
    public class SpringCancelTests
    {
        private const int Deep = 61400;

        /// <summary>The 5+R profile the traces were taken on, in Moza AB9 mode.</summary>
        private static EngineConfig Rig(double springPct = 50)
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                BaseEffectsViaDirectInput = false,
                BaseSpringPct = springPct,
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
                DampingPct = 0
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        private static int OnTheInnerFace(EngineConfig cfg)
        {
            return cfg.BuildGeometry().ColumnTarget(Column.C1) + cfg.SlotHalfWidth + 1100;
        }

        private static int Fx(ForceComposer c, int x, int y, int vx)
        {
            return c.Compose(GateState.Engaged, Column.C1, ShiftDir.Back, x, y, vx, 0, 1.0).ConstantX;
        }

        /// <summary>
        /// The lever model from LateralBounceTests with the measured spring: thrown into the
        /// outer column's inner wall at 300000 counts/s, returns the peak-to-peak lateral swing
        /// over the second, third and fourth seconds.
        /// </summary>
        private static int Swing(EngineConfig cfg, int delayTicks)
        {
            const double PerDi = 5600;
            const double Spring = 0.18 * PerDi;

            GateGeometry geo = cfg.BuildGeometry();
            var composer = new ForceComposer(geo, cfg);
            var estimator = new VelocityEstimator();
            var inFlight = new int[delayTicks + 1];

            double x = geo.ColumnTarget(Column.C1), v = 300000;
            double min = double.MaxValue, max = double.MinValue;

            for (int tick = 0; tick < 4000; tick++)
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
                    double a = (PerDi * acting) + (Spring * (GateGeometry.AxisCenter - x)) - (15 * v);
                    v += a * 0.0001;
                    x += v * 0.0001;
                    if (x < 0) { x = 0; v = 0; }
                    if (x > 65535) { x = 65535; v = 0; }
                }

                if (tick >= 1000)
                {
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }

            return (int)(max - min);
        }

        [Theory]
        [InlineData(3, 1500)]
        [InlineData(4, 4000)]
        [InlineData(5, 6000)]
        public void AThrownGearStopsBouncing(int delayTicks, int premise)
        {
            // Without the cancel - the slot-wall energy cap alone - the measured spring keeps a
            // 2084 / 5858 / 8132-count bounce going at 3 / 4 / 5 ms (4 is the base's measured
            // delay); with it, ~150 / ~400 / ~725. The premise is pinned alongside.
            int without = Swing(Rig(0), delayTicks);
            int with = Swing(Rig(), delayTicks);

            Assert.True(without > premise, "the premise: without the cancel the lever bounces, got " + without);
            // The bound has room for the settle damper at 0.005, halved twice from 0.02 because a
            // stronger one fed a leaning hand's buzz on the rig (1224 counts at 5 ms here).
            Assert.True(with < 1500, "at " + delayTicks + " ms the lever still swings " + with + " counts");
        }

        private static int Traveling(ForceComposer c, int x, int y, int vx)
        {
            return c.Compose(GateState.Traveling, Column.C1, ShiftDir.Back, x, y, vx, 0, 1.0).ConstantX;
        }

        [Fact]
        public void ItHoldsForAsLongAsTheGearIsIn()
        {
            // Giving the spring back in gear, even to a lever settled at the edge, crept it onto
            // the wall's face and the hunt restarted a second later with no throw at all
            // (trace-20261113-205008). Seconds of stillness change nothing now.
            EngineConfig cfg = Rig();
            ForceComposer c = Composer(cfg);
            ForceComposer reference = Composer(Rig(0));
            int edge = cfg.BuildGeometry().ColumnTarget(Column.C1) + cfg.SlotHalfWidth - 100;

            for (int i = 0; i < 5000; i++) Fx(c, edge, Deep, 0);
            for (int i = 0; i < 5; i++) Fx(reference, edge, Deep, 0);

            int expectedCancel = (int)Math.Round(c.SpringCancelX(edge, Deep) * 0.85);
            Assert.InRange(Fx(c, edge, Deep, 0) - Fx(reference, edge, Deep, 0), expectedCancel - 2, expectedCancel + 2);
        }

        [Fact]
        public void ALatchArmsItWithoutAStep()
        {
            // The cancel ramps in as the gear latches rather than landing whole: no single tick
            // moves the force by more than a share of it.
            EngineConfig cfg = Rig();
            ForceComposer c = Composer(cfg);
            int x = OnTheInnerFace(cfg);
            int full = Math.Abs(c.SpringCancelX(x, Deep));
            Assert.True(full > 2500, "expected the spring's pull here to be ~3000 DI, got " + full);

            // What the cancel adds is the difference from the same gate with no spring at the
            // base; the wall's own shaping reacts to the throw identically in both.
            ForceComposer reference = Composer(Rig(0));
            int previous = Fx(c, x, Deep, 0) - Fx(reference, x, Deep, 0);
            int largest = 0;
            for (int i = 0; i < 40; i++)
            {
                int now = Fx(c, x, Deep, 300000) - Fx(reference, x, Deep, 300000);
                largest = Math.Max(largest, Math.Abs(now - previous));
                previous = now;
            }

            Assert.True(largest < full / 4, "the cancel stepped by " + largest + " DI in one tick");
        }

        [Fact]
        public void ItLetsGoWithinMillisecondsOfLeavingTheGear()
        {
            // A 500 ms fade after the gear was left kept up to ~800 DI pushing away from centre
            // on the way out (trace-20261113-205008). Unlatched, still at depth, it is gone in
            // tens of milliseconds.
            EngineConfig cfg = Rig();
            ForceComposer c = Composer(cfg);
            ForceComposer reference = Composer(Rig(0));
            int x = OnTheInnerFace(cfg);

            for (int i = 0; i < 200; i++) Fx(c, x, Deep, 0);
            for (int i = 0; i < 40; i++) Traveling(c, x, Deep, 0);
            for (int i = 0; i < 40; i++) Traveling(reference, x, Deep, 0);
            Assert.Equal(Traveling(reference, x, Deep, 0), Traveling(c, x, Deep, 0));
        }

        [Fact]
        public void ASweepBeforeTheGearLatchesKeepsTheSpring()
        {
            // Entering a slot the lever is not yet latched, so the spring is untouched, tick for
            // tick. Arming on fast sweeps took the spring away on ordinary gate entries
            // (trace-20261113-203341).
            EngineConfig cfg = Rig();
            ForceComposer c = Composer(cfg);
            ForceComposer reference = Composer(Rig(0));
            int x = OnTheInnerFace(cfg);

            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(Traveling(reference, x, Deep, -150000), Traveling(c, x, Deep, -150000));
            }
        }

        [Fact]
        public void NothingIsLeftInTheTunnelAfterLeavingAGear()
        {
            // Armed in a slot, then out through the tunnel sliding fast sideways: the settle
            // damper and the cancel are gone the moment the lever is out of the slot. A damper
            // riding the release fade braked that slide with up to 3000 DI - the shift going
            // haywire on the way out.
            EngineConfig cfg = Rig();
            ForceComposer c = Composer(cfg);
            ForceComposer reference = Composer(Rig(0));
            int x = OnTheInnerFace(cfg);
            int y = GateGeometry.AxisCenter;

            for (int i = 0; i < 20; i++) Fx(c, x, Deep, 300000);

            reference.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, 150000, 0, 1.0);
            c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, 150000, 0, 1.0);
            for (int i = 0; i < 50; i++)
            {
                int expected = reference.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, 150000, 0, 1.0).ConstantX;
                int actual = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, 150000, 0, 1.0).ConstantX;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TheStiffnessIsLinearFromTheMeasuredPoint()
        {
            // Measured at 50. A square law below it cancelled ~400 DI at Spring 20 and the lever
            // bounced as if there were no cancel (trace-20261113-204252); linear is what worked.
            Assert.Equal(0.18, ForceComposer.OnboardSpringStiffness(50), 6);
            Assert.Equal(0.072, ForceComposer.OnboardSpringStiffness(20), 6);
            Assert.Equal(0.36, ForceComposer.OnboardSpringStiffness(100), 6);
            Assert.Equal(0.0, ForceComposer.OnboardSpringStiffness(0), 6);
        }

        [Fact]
        public void TheSpringCannotPullALatchedGearBackOut()
        {
            // At Spring 50 the onboard spring pulls ~0.18 DI per count of depth toward neutral -
            // ~5000 DI at the bottom of a slot, more than the hold alone. Onboard friction kept
            // gears in at Hold 40% / Friction 25; at Hold 22.5% / Friction 15 the rig reported
            // the lever pulled out. In gear, 85% of that pull is pushed back; the remainder is
            // well under the hold either way.
            EngineConfig cfg = Rig();
            ForceComposer c = Composer(cfg);
            ForceComposer reference = Composer(Rig(0));
            int x = cfg.BuildGeometry().ColumnTarget(Column.C1);
            int depth = Deep - GateGeometry.AxisCenter;

            int Fy(ForceComposer composer) =>
                composer.Compose(GateState.Engaged, Column.C1, ShiftDir.Back, x, Deep, 0, 0, 1.0).ConstantY;

            for (int i = 0; i < 100; i++) { Fy(c); Fy(reference); }

            double pull = 0.18 * depth;
            int pushedIn = Fy(c) - Fy(reference);
            Assert.InRange(pushedIn, (int)(0.85 * pull) - 2, (int)(0.85 * pull) + 2);

            double hold = GateGeometry.ForceMax * cfg.DetentHoldPct / 100.0;
            Assert.True(pull > hold, "the premise: the spring out-pulls the hold, " + pull + " vs " + hold);
            Assert.True(pull * 0.15 < hold / 2, "what is left of the spring is not well under the hold");
        }

        [Fact]
        public void NeutralKeepsItsForeAftCentring()
        {
            ForceComposer c = Composer(Rig());
            Assert.Equal(0, c.SpringCancelY(GateGeometry.AxisCenter - 15000, GateGeometry.AxisCenter));
            Assert.Equal(0, c.SpringCancelY(GateGeometry.AxisCenter, GateGeometry.AxisCenter + 3000));
            Assert.True(c.SpringCancelY(GateGeometry.AxisCenter, Deep) > 0, "deep in a Back slot it pushes further in");
        }

        [Fact]
        public void TheTunnelKeepsItsSpring()
        {
            // In neutral the spring still centres the lever: the cancel's shape is zero there.
            EngineConfig cfg = Rig();
            ForceComposer c = Composer(cfg);
            int x = OnTheInnerFace(cfg);

            Assert.Equal(0, c.SpringCancelX(x, GateGeometry.AxisCenter));
            Assert.Equal(0, c.SpringCancelX(x, GateGeometry.AxisCenter + cfg.ChannelHalfEnter));
            Assert.NotEqual(0, c.SpringCancelX(x, Deep));
        }

        [Fact]
        public void ItPullsAwayFromCentreAtTheMeasuredStiffness()
        {
            // Opposite to the spring on both sides, ~0.18 DI per count at Spring 50, and nothing
            // at the middle column, which the spring does not pull sideways.
            ForceComposer c = Composer(Rig());
            int left = c.SpringCancelX(GateGeometry.AxisCenter - 10000, Deep);
            int right = c.SpringCancelX(GateGeometry.AxisCenter + 10000, Deep);

            Assert.InRange(left, -1850, -1750);
            Assert.InRange(right, 1750, 1850);
            Assert.Equal(0, c.SpringCancelX(GateGeometry.AxisCenter, Deep));
        }

        [Fact]
        public void NoSpringAtTheBaseMeansNoCancel()
        {
            // Spring 0, unconfirmed polarity (the AB9 is not written), or the DirectInput
            // provider (whose spring is unmeasured in these units): no cancel at all.
            EngineConfig unconfirmed = Rig();
            unconfirmed.PolarityConfirmed = false;
            EngineConfig generic = Rig();
            generic.BaseEffectsViaDirectInput = true;

            foreach (EngineConfig cfg in new[] { Rig(0), unconfirmed, generic })
            {
                Assert.Equal(0, Composer(cfg).SpringCancelX(GateGeometry.AxisCenter - 15000, Deep));
            }
        }
    }
}
