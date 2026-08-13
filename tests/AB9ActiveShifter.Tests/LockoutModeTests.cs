using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The lockout's direction and mode dials: the Both gate's edge-flip latch, and the hard
    /// modes' pinned strength, same-tick release and hold-fire arming. Everything here drives
    /// the composer through Compose rather than poking internals, because the latch IS internal
    /// state and the promise is about what the hand feels.
    /// </summary>
    public class LockoutModeTests
    {
        private const int Center = GateGeometry.AxisCenter;

        /// <summary>
        /// Full gain, every lateral force but the gate silenced so ConstantX is the band alone.
        /// The pin matters as much as the guide: the funnel steering rides it, and it reaches
        /// ConstantX everywhere the relief window does not cover - which is most of the band.
        /// </summary>
        private static EngineConfig Isolated(LockoutGapDirection direction,
            LockoutMode mode = LockoutMode.PushThrough)
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                LockoutGapDirection = direction,
                LockoutMode = mode,
                ChannelGuideForcePct = 0,
                ChannelWallForcePct = 0,
                ColumnPinForcePct = 0,
                ColumnDetentForcePct = 0,
                BarrierForcePct = 0
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        private static int GateX(ForceComposer c, int x, int y, bool released = false)
        {
            return c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y,
                lockoutReleased: released).ConstantX;
        }

        [Fact]
        public void ABothWayGateChargesComingAndGoing()
        {
            // A toll both ways cannot be a function of position alone - a position-only field
            // refunds one crossing whatever it charges the other - so the band's sign rides the
            // side latch. Crossing left to right the force opposes the whole way; once the band
            // has been fully exited, the way back opposes just the same.
            EngineConfig cfg = Isolated(LockoutGapDirection.Both);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int left = geo.LockoutCentre - geo.LockoutHalfWidth - 400;
            int right = geo.LockoutCentre + geo.LockoutHalfWidth + 400;

            for (int x = left; x <= right; x += 50)
            {
                int force = GateX(c, x, Center);
                Assert.True(force <= 0, "outbound crossing assisted at x=" + x + ": " + force);
                if (Math.Abs(x - geo.LockoutCentre) == 0)
                {
                    Assert.Equal(-7000, force);
                }
            }

            for (int x = right; x >= left; x -= 50)
            {
                int force = GateX(c, x, Center);
                Assert.True(force >= 0, "return crossing assisted at x=" + x + ": " + force);
                if (Math.Abs(x - geo.LockoutCentre) == 0)
                {
                    Assert.Equal(7000, force);
                }
            }
        }

        [Fact]
        public void TheBidirectionalGateNeverRefundsARetreat()
        {
            // The over-centre failure, pinned from the Both side: push most of the way across -
            // past the crest - then give up and retreat. The side only re-derives outside the
            // band, so the force must keep pushing back toward the entry the whole dance, and
            // never once propel the lever onward.
            EngineConfig cfg = Isolated(LockoutGapDirection.Both);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int left = geo.LockoutCentre - geo.LockoutHalfWidth - 400;
            int deepest = geo.LockoutCentre + geo.LockoutHalfWidth - 300;

            for (int x = left; x <= deepest; x += 25)
            {
                Assert.True(GateX(c, x, Center) <= 0, "pushed onward at x=" + x);
            }

            for (int x = deepest; x >= left; x -= 25)
            {
                Assert.True(GateX(c, x, Center) <= 0, "refunded onward during retreat at x=" + x);
            }
        }

        [Fact]
        public void TheBidirectionalGateFlipsOnlyWhereItsForceIsAlreadyZero()
        {
            // The latch's whole safety argument: it can only flip at the band's edges, and the
            // faces have already tapered the force to nothing there, so no single count of
            // movement ever steps the force - not even the counts on which the flip lands.
            EngineConfig cfg = Isolated(LockoutGapDirection.Both);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int face = Math.Min(cfg.WallRamp, Math.Max(1, geo.LockoutHalfWidth / 2));
            int bound = (int)Math.Ceiling(7000.0 / face) + 1;

            int left = geo.LockoutCentre - geo.LockoutHalfWidth - 100;
            int right = geo.LockoutCentre + geo.LockoutHalfWidth + 100;

            // A full crossing, a dance in and out at the far edge (where the flip lands), a
            // return crossing, and a dance at the near edge - one count at a time.
            var path = new System.Collections.Generic.List<int>();
            for (int x = left; x <= right; x++) path.Add(x);
            for (int i = 0; i < 60; i++) path.Add(right - (i % 3));
            for (int x = right; x >= left; x--) path.Add(x);
            for (int i = 0; i < 60; i++) path.Add(left + (i % 3));

            int previous = GateX(c, path[0], Center);
            for (int i = 1; i < path.Count; i++)
            {
                int force = GateX(c, path[i], Center);
                Assert.True(Math.Abs(force - previous) <= bound,
                    "step of " + (force - previous) + " at x=" + path[i] + ", bound " + bound);
                previous = force;
            }
        }

        [Fact]
        public void NoSingleCountOfDriftEverStepsTheBidirectionalField()
        {
            // The full-gate drift sweep the one-way field is held to, re-run with the latch in
            // play and everything else at its shipped defaults. Same bound: the pin over the
            // bite plus a unit for rounding plus the barriers' own smooth slope.
            EngineConfig cfg = new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                LockoutGapDirection = LockoutGapDirection.Both
            };

            int bound = (int)Math.Ceiling(
                GateGeometry.ForceMax * cfg.ColumnPinForcePct / 100.0 / (double)Math.Max(1, cfg.WallRamp)) + 3;

            foreach (int depth in new[] { 0, cfg.ChannelHalfEnter, 3000, cfg.ChannelHalfExit })
            {
                foreach (int direction in new[] { 1, -1 })
                {
                    ForceComposer c = Composer(cfg);
                    int from = direction > 0 ? 0 : GateGeometry.AxisMax;
                    int previous = GateX(c, from, Center + depth);

                    for (int step = 1; step <= GateGeometry.AxisMax; step++)
                    {
                        int x = direction > 0 ? step : GateGeometry.AxisMax - step;
                        int force = GateX(c, x, Center + depth);

                        Assert.True(Math.Abs(force - previous) <= bound,
                            "step of " + (force - previous) + " at x=" + x + " depth=" + depth
                            + " sweeping " + direction + ", bound " + bound);

                        previous = force;
                    }
                }
            }
        }

        [Fact]
        public void ACrossingAtDepthFlipsTheSideSilently()
        {
            // A diagonal dive under a Both gate must not be a free pass home. The latch updates
            // at every depth, where the depth fade already has the band at zero, so the flip
            // itself is silent - and the lever that surfaces on the far side owes the return
            // toll instead of being assisted back across.
            EngineConfig cfg = Isolated(LockoutGapDirection.Both);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int left = geo.LockoutCentre - geo.LockoutHalfWidth - 400;
            int right = geo.LockoutCentre + geo.LockoutHalfWidth + 400;
            int deep = Center + cfg.ChannelHalfExit + 800;

            Assert.True(GateX(c, left, Center) <= 0);

            // Under the band: the fade has the force at zero the whole way across.
            for (int x = left; x <= right; x += 50)
            {
                Assert.Equal(0, GateX(c, x, deep));
            }

            // Surfacing on the far side, outside the band: still nothing.
            Assert.Equal(0, GateX(c, right, Center));

            // But the way back is a charged crossing, not an assisted return.
            for (int x = right; x >= left; x -= 50)
            {
                Assert.True(GateX(c, x, Center) >= 0, "assisted back across at x=" + x);
            }
        }

        [Fact]
        public void TheHardGateIsPinnedToFullStrengthWhateverTheForceDialSays()
        {
            // Hard means hard: the force dial is idle in the hotkey modes, and the band runs at
            // 100% of the effective gain.
            EngineConfig cfg = Isolated(LockoutGapDirection.TowardHigh, LockoutMode.HotkeyToggle);
            cfg.LockoutForcePct = 5;
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            // Arm from outside the band first: a fresh composer's first tick over a lever
            // already inside a hard band deliberately holds fire.
            int outside = geo.LockoutCentre - geo.LockoutHalfWidth - 300;
            Assert.Equal(0, GateX(c, outside, Center));
            Assert.Equal(-10000, GateX(c, geo.LockoutCentre, Center));

            // The same dial honoured in push-through, so the pin is the mode's doing.
            EngineConfig soft = Isolated(LockoutGapDirection.TowardHigh);
            soft.LockoutForcePct = 5;
            Assert.Equal(-500, GateX(Composer(soft), soft.BuildGeometry().LockoutCentre, Center));
        }

        [Fact]
        public void TheHardGateStillWearsTheGainAndThePolarityCap()
        {
            // "100%" is 100% of the configured envelope, never a way around the cap: an
            // unmeasured base still gets at most the 10% safety gain, hard mode or not.
            EngineConfig cfg = Isolated(LockoutGapDirection.TowardHigh, LockoutMode.HotkeyToggle);
            cfg.PolarityConfirmed = false;
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            Assert.Equal(0, GateX(c, geo.LockoutCentre - geo.LockoutHalfWidth - 300, Center));
            Assert.Equal(-1000, GateX(c, geo.LockoutCentre, Center));
        }

        [Fact]
        public void ReleasingTheHardGateDropsItsForceTheSameTick()
        {
            // The hotkey is a release, and releases pass the time shaping instantly by design:
            // the tick the flag arrives, the band is gone.
            EngineConfig cfg = Isolated(LockoutGapDirection.TowardHigh, LockoutMode.HotkeyToggle);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            Assert.Equal(0, GateX(c, geo.LockoutCentre - geo.LockoutHalfWidth - 300, Center));
            Assert.Equal(-10000, GateX(c, geo.LockoutCentre, Center));
            Assert.Equal(0, GateX(c, geo.LockoutCentre, Center, released: true));
        }

        [Fact]
        public void ReEngagingOverTheLeverHoldsFireUntilTheBandIsLeft()
        {
            // Re-engaging with the lever inside the band must not materialise full force under
            // the hand - the attack is off by default, so nothing else would soften the step.
            // The gate arms but holds fire until the lever reaches the band's edge, where the
            // shape itself is zero, and only a fresh entry meets the wall - through its face.
            EngineConfig cfg = Isolated(LockoutGapDirection.TowardHigh, LockoutMode.HotkeyToggle);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int inside = geo.LockoutCentre;
            int outside = geo.LockoutCentre - geo.LockoutHalfWidth - 300;

            Assert.Equal(0, GateX(c, inside, Center, released: true));

            // The hotkey re-engages while the lever is parked mid-band: nothing, and nothing
            // however long it sits there.
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(0, GateX(c, inside, Center));
            }

            // Clear of the band the gate arms - still nothing at a zero-force position -
            // and the next entry pays like any wall.
            Assert.Equal(0, GateX(c, outside, Center));
            Assert.Equal(-10000, GateX(c, inside, Center));
        }

        [Fact]
        public void AFreshComposerOverTheLeverInsideAHardBandHoldsFire()
        {
            // Every config change rebuilds the composer, so the first tick of its life is an
            // arming edge too: a settings tweak made while the lever happens to rest mid-band
            // must not fire the band into the hand.
            EngineConfig cfg = Isolated(LockoutGapDirection.TowardHigh, LockoutMode.HotkeyToggle);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            Assert.Equal(0, GateX(c, geo.LockoutCentre, Center));

            int outside = geo.LockoutCentre - geo.LockoutHalfWidth - 300;
            Assert.Equal(0, GateX(c, outside, Center));
            Assert.Equal(-10000, GateX(c, geo.LockoutCentre, Center));
        }

        [Fact]
        public void ReEngagingWhileClearFiresImmediately()
        {
            // Arming with the lever already clear needs no grace: the force at the lever's
            // position is zero there by shape, so the wall simply exists again and is met
            // spatially, like every wall.
            EngineConfig cfg = Isolated(LockoutGapDirection.TowardHigh, LockoutMode.HotkeyToggle);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int outside = geo.LockoutCentre - geo.LockoutHalfWidth - 300;

            Assert.Equal(0, GateX(c, outside, Center, released: true));
            Assert.Equal(0, GateX(c, outside, Center));
            Assert.Equal(-10000, GateX(c, geo.LockoutCentre, Center));
        }

        [Fact]
        public void ThePushThroughGateIgnoresTheReleaseFlag()
        {
            // The hotkey belongs to the hard modes. A push-through gate is defeated by pushing,
            // and a stray release action must not open it.
            EngineConfig cfg = Isolated(LockoutGapDirection.TowardHigh);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            Assert.Equal(-7000, GateX(c, geo.LockoutCentre, Center, released: true));
        }
    }
}
