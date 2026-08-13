using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The selector lane's lockout: a gate band in one chosen gap, replacing that gap's cosine
    /// hump. The lane's two load-bearing invariants survive it by construction - every position
    /// stays a free region (the width clamp ends the band before both notch edges), and the
    /// selector is always in exactly one position (the band is force only, and the state
    /// machine never learns it exists).
    /// </summary>
    public class PrndLockoutTests
    {
        private const int Center = GateGeometry.AxisCenter;

        private static EngineConfig Config(PrndLockoutGap gap,
            PrndLockoutDirection direction = PrndLockoutDirection.TowardD,
            LockoutMode mode = LockoutMode.PushThrough)
        {
            return new EngineConfig
            {
                Pattern = GatePattern.Prnd,
                OverallGainPct = 100,
                PolarityConfirmed = true,
                PrndLockoutGap = gap,
                PrndLockoutDirection = direction,
                PrndLockoutMode = mode
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        private static int LaneY(ForceComposer c, int x, int y, bool released = false)
        {
            return c.ComposePrnd(GateGeometry.AxisCenter, y, lockoutReleased: released).ConstantY;
        }

        [Fact]
        public void ThePrndLockoutReplacesItsGapsHumpAndOnlyItsGaps()
        {
            // The H gate's own dispatch, on the lane: the locked gap renders the band, every
            // other gap keeps its cosine hump untouched.
            EngineConfig cfg = Config(PrndLockoutGap.RN);
            EngineConfig plain = Config(PrndLockoutGap.Off);

            ForceComposer c = Composer(cfg);
            ForceComposer p = Composer(plain);
            PrndLane lane = cfg.BuildPrndLane();

            // The locked gap's crest carries the band's flat core - that IS the toll. R-N is
            // label gap 1, device gap 1 unmirrored; TowardD blocks +y, so the band pushes -y.
            Assert.Equal(-7000, c.PrndLaneForce(lane.CrestY(1)));

            // The other gaps: identical to the un-locked lane, sample by sample.
            for (int gap = 0; gap < 3; gap += 2)
            {
                for (int y = lane.PositionY(gap); y <= lane.PositionY(gap + 1); y += 40)
                {
                    Assert.Equal(p.PrndLaneForce(y), c.PrndLaneForce(y));
                }
            }
        }

        [Fact]
        public void ThePrndBandIsZeroAtBothNotchEdgesWhateverTheWidthAsks()
        {
            // A position must stay a free region: the width clamp ends the band before both
            // neighbouring notches however much is asked for, and the effective width is
            // reported, never applied silently.
            EngineConfig cfg = Config(PrndLockoutGap.RN);
            cfg.PrndLockoutHalfWidth = 60000;

            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();
            int notch = GateGeometry.Clamp(cfg.PrndNotchHalfWidth, 0, c.PrndNotchHalfWidthCeiling);

            Assert.True(c.PrndLockoutEffectiveHalfWidth <= c.PrndLockoutHalfWidthCeiling);

            foreach (int position in new[] { 1, 2 })
            {
                Assert.Equal(0, c.PrndLaneForce(lane.PositionY(position)));

                for (int off = 0; off <= notch; off += 50)
                {
                    Assert.Equal(0, c.PrndLaneForce(lane.PositionY(1) + off));
                    Assert.Equal(0, c.PrndLaneForce(lane.PositionY(2) - off));
                }
            }
        }

        [Fact]
        public void ThePrndLockoutFollowsTheLabelsUnderMirroring()
        {
            // Label-relative, like the buttons: turning the lane round moves the band with
            // P, R, N and D, and the paying direction stays the labelled one.
            EngineConfig plain = Config(PrndLockoutGap.PR);
            EngineConfig mirrored = Config(PrndLockoutGap.PR);
            mirrored.MirrorSlots = true;

            ForceComposer c = Composer(mirrored);
            PrndLane lane = mirrored.BuildPrndLane();

            // Mirrored, P-R is the lane's far gap (device gap 2), and TowardD - the paying
            // crossing moving toward D's end - now moves toward LOW y, so the band pushes +y.
            Assert.Equal(7000, c.PrndLaneForce(lane.CrestY(2)));

            // And the gap where P-R used to live is an ordinary hump again.
            ForceComposer p = Composer(Config(PrndLockoutGap.Off));
            PrndLane plainLane = plain.BuildPrndLane();
            for (int y = plainLane.PositionY(0); y <= plainLane.PositionY(1); y += 40)
            {
                Assert.Equal(p.PrndLaneForce(y), c.PrndLaneForce(y));
            }
        }

        [Fact]
        public void NoSingleCountOfTheLaneEverStepsTheForceWithTheLockoutOn()
        {
            // The lane's own per-count sweep, re-run with the band in the field. The bound
            // gains the band's face - the steepest thing the configuration now allows.
            EngineConfig cfg = Config(PrndLockoutGap.RN);
            cfg.PrndLaneHalfLength = 9000;
            cfg.PrndDetentForcePct = 100;
            cfg.PrndStopForcePct = 100;
            cfg.PrndLockoutForcePct = 100;
            cfg.PrndNotchHalfWidth = 400;

            ForceComposer c = Composer(cfg);

            int strongest = Math.Max(cfg.PrndLockoutForcePct,
                Math.Max(cfg.PrndDetentForcePct, cfg.PrndStopForcePct));
            int bound = (int)Math.Ceiling(strongest * 100 / (double)Math.Max(1, cfg.WallRamp)) + 1;

            int previous = c.PrndLaneForce(0);
            for (int y = 1; y <= GateGeometry.AxisMax; y++)
            {
                int force = c.PrndLaneForce(y);
                Assert.True(Math.Abs(force - previous) <= bound,
                    "step of " + (force - previous) + " at y=" + y + ", bound " + bound);
                previous = force;
            }
        }

        [Fact]
        public void ABothWayPrndBandNeverRefundsARetreat()
        {
            // The lane's edge-flip latch: crossing pays whichever way, a retreat is pushed
            // back the way it came, and the flip only ever lands where the band is zero.
            EngineConfig cfg = Config(PrndLockoutGap.RN, PrndLockoutDirection.Both);
            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();

            int crest = lane.CrestY(1);
            int hw = c.PrndLockoutEffectiveHalfWidth;
            int low = crest - hw - 300;
            int high = crest + hw + 300;

            for (int y = low; y <= high - 500; y += 25)
            {
                Assert.True(LaneY(c, Center, y) <= 0, "assisted onward at y=" + y);
            }
            for (int y = high - 500; y >= low; y -= 25)
            {
                Assert.True(LaneY(c, Center, y) <= 0, "refunded during retreat at y=" + y);
            }

            for (int y = low; y <= high; y += 25) LaneY(c, Center, y);
            for (int y = high; y >= low; y -= 25)
            {
                Assert.True(LaneY(c, Center, y) >= 0, "return crossing assisted at y=" + y);
            }
        }

        [Fact]
        public void ThePrndHardModeArmsOutsideItsBand()
        {
            // The gap band's hold-fire rule on the lane: a hard band never materialises under
            // the lever, releases the same tick, and is pinned to full strength through the
            // same gain the polarity cap rides.
            EngineConfig cfg = Config(PrndLockoutGap.RN, PrndLockoutDirection.TowardD,
                LockoutMode.HotkeyToggle);
            cfg.PrndLockoutForcePct = 5;

            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();
            int crest = lane.CrestY(1);
            int outside = crest - c.PrndLockoutEffectiveHalfWidth - 300;

            // Fresh composer over a lever mid-band: holds fire until clear.
            Assert.Equal(0, LaneY(c, Center, crest));
            Assert.Equal(0, LaneY(c, Center, outside));
            Assert.Equal(-10000, LaneY(c, Center, crest));

            // Released: gone the same tick, and the gap is simply free.
            Assert.Equal(0, LaneY(c, Center, crest, released: true));
        }

        [Fact]
        public void ThePrndLockoutNeverBlocksTheSelector()
        {
            // A selector is always in exactly one position and its buttons follow the lever -
            // the lockout is force only, hard mode included. The state machine is built from
            // the same config and never learns the band exists.
            EngineConfig cfg = Config(PrndLockoutGap.PR, PrndLockoutDirection.Both,
                LockoutMode.HotkeyToggle);
            var machine = new PrndStateMachine(cfg.BuildPrndLane());
            ForceComposer c = Composer(cfg);

            machine.Resync(Center);

            for (int y = 0; y <= GateGeometry.AxisMax; y += 97)
            {
                LaneY(c, Center, y);
                StateTransition t = machine.Update(y);

                Assert.Equal(GateState.Engaged, t.State);
                Assert.InRange(t.Gear, 11, 14);
            }
        }
    }
}
