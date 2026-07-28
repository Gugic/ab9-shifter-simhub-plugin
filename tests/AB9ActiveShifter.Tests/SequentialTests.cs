using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The sequential pattern: one shift per stroke, re-armed only by coming home, and a
    /// lever that is sprung back to centre on both axes rather than gated.
    /// </summary>
    public class SequentialTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int Max = GateGeometry.AxisMax;

        private static EngineConfig SeqConfig()
        {
            return new EngineConfig
            {
                Pattern = GatePattern.Sequential,
                OverallGainPct = 100,
                PolarityConfirmed = true,
                DampingPct = 0,
                DetentResistPct = 30,
                DetentHoldPct = 12
            };
        }

        private static SequentialStateMachine Machine(EngineConfig cfg)
        {
            return new SequentialStateMachine(cfg.BuildGeometry(), cfg.MinEngageTicks);
        }

        [Fact]
        public void OnePushFiresExactlyOneUpshift()
        {
            SequentialStateMachine sm = Machine(SeqConfig());

            int fired = 0;
            for (int i = 0; i < 50; i++)
            {
                fired += Math.Abs(sm.Update(2000).Shift); // held deep past the threshold
            }

            Assert.Equal(1, fired);
        }

        [Fact]
        public void ForwardIsUpAndBackIsDown()
        {
            EngineConfig cfg = SeqConfig();
            SequentialStateMachine sm = Machine(cfg);

            SeqTransition up = default(SeqTransition);
            for (int i = 0; i < 5; i++) up = sm.Update(2000);
            Assert.True(up.Shift == 0 || up.Shift == 1);

            for (int i = 0; i < 20; i++) sm.Update(Center); // come home, re-arm

            int down = 0;
            for (int i = 0; i < 5; i++) down += sm.Update(Max - 2000).Shift;
            Assert.Equal(-1, down);
        }

        [Fact]
        public void MirrorSlotsSwapsUpAndDown()
        {
            EngineConfig cfg = SeqConfig();
            cfg.MirrorSlots = true;
            SequentialStateMachine sm = Machine(cfg);

            int fired = 0;
            for (int i = 0; i < 5; i++) fired += sm.Update(2000).Shift;
            Assert.Equal(-1, fired);
        }

        [Fact]
        public void NothingRefiresUntilTheLeverComesHome()
        {
            EngineConfig cfg = SeqConfig();
            SequentialStateMachine sm = Machine(cfg);

            for (int i = 0; i < 5; i++) sm.Update(2000);

            // Bouncing between the engage and release thresholds must not machine-gun.
            int fired = 0;
            for (int i = 0; i < 30; i++)
            {
                fired += Math.Abs(sm.Update(i % 2 == 0 ? 2000 : cfg.EngageDepth + 500).Shift);
            }
            Assert.Equal(0, fired);

            // Past the release threshold, the next stroke fires again.
            for (int i = 0; i < 5; i++) sm.Update(cfg.ReleaseDepth + 2000);
            int refire = 0;
            for (int i = 0; i < 5; i++) refire += sm.Update(2000).Shift;
            Assert.Equal(1, refire);
        }

        [Fact]
        public void ResyncAdoptsADeepLeverWithoutFiring()
        {
            SequentialStateMachine sm = Machine(SeqConfig());
            sm.Resync(2000);

            int fired = 0;
            for (int i = 0; i < 20; i++) fired += Math.Abs(sm.Update(2000).Shift);
            Assert.Equal(0, fired);
        }

        [Fact]
        public void TheLeverIsSprungHomeOnBothAxes()
        {
            EngineConfig cfg = SeqConfig();
            ForceComposer c = new ForceComposer(cfg.BuildGeometry(), cfg);

            // At home: quiet ground, nothing to fight.
            ForceFrame home = c.ComposeSequential(Center, Center);
            Assert.Equal(0, home.ConstantX);
            Assert.Equal(0, home.ConstantY);

            // Displaced sideways: railed straight back toward the lateral centre.
            Assert.True(c.ComposeSequential(Center + 4000, Center).ConstantX < 0);
            Assert.True(c.ComposeSequential(Center - 4000, Center).ConstantX > 0);

            // Pushed forward: resisted back toward centre, harder the further it goes.
            int shallow = c.ComposeSequential(Center, Center - 6000).ConstantY;
            int deep = c.ComposeSequential(Center, Center - 18000).ConstantY;
            Assert.True(shallow > 0, "the spring must push back toward centre");
            Assert.True(deep > shallow, "the spring must build with travel");
        }

        [Fact]
        public void TheClickDropsTheResistancePastTheThreshold()
        {
            EngineConfig cfg = SeqConfig();
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = new ForceComposer(geo, cfg);

            int before = Math.Abs(c.ComposeSequential(Center, cfg.EngageDepth + 300).ConstantY);
            int after = Math.Abs(c.ComposeSequential(Center, cfg.EngageDepth - 300).ConstantY);

            Assert.True(after < before / 2,
                "crossing the threshold should feel like a click: " + before + " -> " + after);
            Assert.True(after > 0, "the hold must still push home so the lever returns");
        }

        [Fact]
        public void TheStrokeEndsAtAWallNotAtTheHardwareStop()
        {
            // Past the click there used to be twenty thousand counts of nothing before the
            // hardware stop. The stroke now has its own end: hold through the overtravel,
            // then a wall rising over the bite.
            EngineConfig cfg = SeqConfig();
            ForceComposer c = new ForceComposer(cfg.BuildGeometry(), cfg);

            int threshold = Center - cfg.EngageDepth;
            int landing = Math.Abs(c.ComposeSequential(
                Center, Center - (threshold + 1000)).ConstantY);
            int stop = Math.Abs(c.ComposeSequential(
                Center, Center - (threshold + cfg.SeqOvertravel + cfg.WallRamp + 100)).ConstantY);

            Assert.True(landing < 2000, "the landing should stay light: " + landing);
            Assert.True(stop >= 8000, "the end-stop should be a wall: " + stop);
        }

        [Fact]
        public void TheStopMovesWithTheThreshold()
        {
            // The stop is measured from the firing point, so shortening the throw shortens
            // the whole stroke rather than leaving the wall stranded near the hardware stop.
            EngineConfig cfg = SeqConfig();
            cfg.EngageDepth = 26000; // fires ~6800 counts from centre
            ForceComposer c = new ForceComposer(cfg.BuildGeometry(), cfg);

            int threshold = Center - cfg.EngageDepth;
            int depth = threshold + cfg.SeqOvertravel + cfg.WallRamp + 100;
            int shortThrow = Math.Abs(c.ComposeSequential(Center, Center - depth).ConstantY);

            EngineConfig longCfg = SeqConfig();
            ForceComposer c2 = new ForceComposer(longCfg.BuildGeometry(), longCfg);
            int longThrow = Math.Abs(c2.ComposeSequential(Center, Center - depth).ConstantY);

            Assert.True(shortThrow >= 8000, "short throw should hit its wall here: " + shortThrow);
            Assert.True(longThrow < 2000, "long throw should still be on its ramp here: " + longThrow);
        }

        [Fact]
        public void TheClickKickFiresWithTheShiftAndDecaysAway()
        {
            // The click that hits: a 25 ms burst in the stroke's own direction the moment the
            // shift registers, on top of the spatial drop. Two identical composers, one told
            // the shift fired: the outputs must differ by exactly the kick's envelope - full
            // on the firing tick, gone 25 ms later - even while the absorber is actively
            // cutting, because the kick joins beside the carrier, after the stabilisers.
            EngineConfig cfg = SeqConfig();
            ForceComposer plain = new ForceComposer(cfg.BuildGeometry(), cfg);
            ForceComposer clicked = new ForceComposer(cfg.BuildGeometry(), cfg);

            // Deep in a forward stroke, moving home fast enough that the return spring is
            // being yielded - the kick must ride through untouched.
            int y = 2000;
            int vy = 25000;

            int baseline = plain.ComposeSequential(Center, y, 0, vy, 1.0).ConstantY;
            int fired = clicked.ComposeSequential(Center, y, 0, vy, 1.0, 0, clickNow: true).ConstantY;

            // Forward stroke: the kick pushes toward -y, the full 60% of scale at gain 100.
            Assert.Equal(baseline - 6000, fired);

            int last = fired;
            for (int i = 0; i < 30; i++)
            {
                int p = plain.ComposeSequential(Center, y, 0, vy, 1.0).ConstantY;
                int c = clicked.ComposeSequential(Center, y, 0, vy, 1.0).ConstantY;
                int kick = c - p;

                Assert.True(kick <= 0, "the kick must never reverse: " + kick);
                Assert.True(Math.Abs(kick) <= Math.Abs(last - baseline) + 1,
                    "the kick must only ever decay: " + kick);
                last = c;
            }

            Assert.Equal(
                plain.ComposeSequential(Center, y, 0, vy, 1.0).ConstantY,
                clicked.ComposeSequential(Center, y, 0, vy, 1.0).ConstantY);
        }

        [Fact]
        public void TheClickKickIsGainCappedUntilPolarityIsConfirmed()
        {
            // The 10% cap is the safety story for an unmeasured base, and the kick is a
            // force like any other: it must shrink with the effective gain, cap included.
            EngineConfig cfg = SeqConfig();
            cfg.PolarityConfirmed = false;
            ForceComposer plain = new ForceComposer(cfg.BuildGeometry(), cfg);
            ForceComposer clicked = new ForceComposer(cfg.BuildGeometry(), cfg);

            int baseline = plain.ComposeSequential(Center, 2000, 0, 0, 1.0).ConstantY;
            int fired = clicked.ComposeSequential(Center, 2000, 0, 0, 1.0, 0, clickNow: true).ConstantY;

            Assert.Equal(baseline - 600, fired);
        }

        [Fact]
        public void SequentialForcesCarryTheMeasuredPolarity()
        {
            EngineConfig cfg = SeqConfig();
            cfg.InvertConstantX = true;
            ForceComposer inverted = new ForceComposer(cfg.BuildGeometry(), cfg);

            EngineConfig plain = SeqConfig();
            ForceComposer straight = new ForceComposer(plain.BuildGeometry(), plain);

            Assert.Equal(
                -straight.ComposeSequential(Center + 4000, Center).ConstantX,
                inverted.ComposeSequential(Center + 4000, Center).ConstantX);
        }
    }
}
