using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Checks the shape of the gate forces. This is the part of the plugin a user cannot
    /// verify except by feel, and the part where a sign error would drive a 12 Nm base the
    /// wrong way, so the arithmetic is pinned down here instead.
    ///
    /// Every wall is a shaped constant force. Springs are deliberately absent: on this
    /// hardware a condition effect at maximum coefficient yields about 1.5% force 500 counts
    /// past a wall, which is why the gate used to feel like nothing at all.
    /// </summary>
    public class ForceComposerTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int Max = GateGeometry.AxisMax;
        private const int C1 = 0;
        private const int C2 = 21845;
        private const int C3 = 43690;
        private const int C4 = 65535;

        /// <summary>Midpoint between C3 and C4: the crest of the lockout hump.</summary>
        private const int LockoutCrest = (C3 + C4) / 2;

        private static EngineConfig FullGainConfig()
        {
            return new EngineConfig { OverallGainPct = 100, PolarityConfirmed = true };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        private static ForceFrame Neutral(ForceComposer c, int x, int y)
        {
            return c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y);
        }

        // ---------------------------------------------------------------- gate walls

        [Fact]
        public void GateWallIsAsStrongAsRequestedWithinTheBiteDistance()
        {
            // The whole point of using constant force: a wall must reach its plateau in a few
            // hundred counts. A spring at maximum coefficient would manage about 1.5% here.
            EngineConfig cfg = FullGainConfig();
            cfg.ChannelWallForcePct = 90;
            cfg.WallRamp = 600;

            // Squarely between two columns, pushed clear of the channel corridor and past the ramp.
            int between = (C2 + C3) / 2;
            int past = cfg.ChannelHalfEnter + cfg.WallRamp + 200;
            int force = Math.Abs(Neutral(Composer(cfg), between, Center - past).ConstantY);

            Assert.Equal(9000, force);
        }

        [Fact]
        public void GateWallPushesBackTowardTheChannel()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            // Y below centre is "forward"; the wall must push back toward the player, and vice
            // versa. A sign error here would fling the stick to a stop.
            Assert.True(Neutral(c, between, Center - 3000).ConstantY > 0);
            Assert.True(Neutral(c, between, Center + 3000).ConstantY < 0);
        }

        [Fact]
        public void ChannelIsOpenOnAColumnAndWalledBetweenThem()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            int onColumn = Math.Abs(Neutral(c, C2, Center - 3000).ConstantY);
            int between = Math.Abs(Neutral(c, (C2 + C3) / 2, Center - 3000).ConstantY);

            Assert.True(onColumn < between / 4,
                "lined up with a column the gate must open: " + onColumn + " vs " + between);
        }

        [Fact]
        public void GateWallArrivesGraduallyRatherThanSnappingOn()
        {
            // A step change in wall force at a band edge is felt as a jolt, so the wall blends
            // in with lateral distance from the column.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            int atColumn = Math.Abs(Neutral(c, C2, Center - 3000).ConstantY);
            int nearEdge = Math.Abs(Neutral(c, C2 + 1800, Center - 3000).ConstantY);
            int wellOut = Math.Abs(Neutral(c, C2 + 3200, Center - 3000).ConstantY);

            Assert.True(atColumn < nearEdge, "wall should grow leaving the column");
            Assert.True(nearEdge < wellOut, "wall should keep growing toward the midpoint");
        }

        [Fact]
        public void RestingInTheChannelCostsNoForce()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            Assert.Equal(0, Neutral(c, between, Center).ConstantY);
            Assert.True(Math.Abs(Neutral(c, between, Center + cfg.ChannelHalfEnter + 800).ConstantY) > 0);
        }

        // ---------------------------------------------------------------- column hold

        [Fact]
        public void ASlotIsAFreeCorridorNotAPullToItsCentreLine()
        {
            // The stick must be able to rest anywhere inside the slot with no lateral force.
            // A restoring force here would put an equilibrium mid-slot for the stick to hunt
            // around, which is what made the middle gears shake while seated.
            EngineConfig cfg = FullGainConfig();
            cfg.SlotHalfWidth = 1100;
            ForceComposer c = Composer(cfg);

            foreach (int offset in new[] { -1000, -500, 0, 500, 1000 })
            {
                int force = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, C2 + offset, 2000).ConstantX;
                Assert.Equal(0, force);
            }
        }

        [Fact]
        public void SlotWallsPushBackOnceOutsideTheCorridor()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.SlotHalfWidth = 1100;
            ForceComposer c = Composer(cfg);

            Assert.True(c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, C2 + 1800, 2000).ConstantX < 0);
            Assert.True(c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, C2 - 1800, 2000).ConstantX > 0);
        }

        [Fact]
        public void TheSlotCorridorStaysInsideTheBandThatHoldsTheGear()
        {
            // If the corridor were wider than the state machine's exit band, the stick could
            // wander out of its own gear without a wall ever pushing back.
            EngineConfig cfg = FullGainConfig();
            cfg.SlotHalfWidth = 9000;   // absurd on purpose
            ForceComposer c = Composer(cfg);

            int justInsideExit = cfg.ColumnInnerHalfExit - 100;
            int force = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, C2 + justInsideExit, 2000).ConstantX;

            Assert.True(force < 0,
                "a wall must arrive before the gear is lost, even with a silly corridor width");
        }

        [Fact]
        public void TheNeutralChannelIsAlsoACorridor()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            // Free along the channel's width, walled beyond it.
            Assert.Equal(0, Neutral(c, between, Center + 900).ConstantY);
            Assert.True(Math.Abs(Neutral(c, between, Center + 3000).ConstantY) > 0);
        }

        [Fact]
        public void OuterSlotsAreOneSidedAgainstTheEndOfTravel()
        {
            // The outer columns sit at the ends of travel, so their wall can only ever push
            // inward. That is why they were stable when the middle ones were not.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            for (int x = 0; x <= 4000; x += 250)
            {
                Assert.True(c.Compose(GateState.Engaged, Column.C1, ShiftDir.Fwd, x, 2000).ConstantX <= 0);
            }

            for (int x = Max; x >= Max - 4000; x -= 250)
            {
                Assert.True(c.Compose(GateState.Engaged, Column.C4, ShiftDir.Back, x, Max - 2000).ConstantX >= 0);
            }
        }

        [Fact]
        public void BarriersDoNotFightTheStickOnceItIsInAGear()
        {
            // Committed to a gear there is nothing left to push through, so the humps must not
            // keep shoving sideways while the slot detent is doing its job.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            int pinned = c.Compose(GateState.Engaged, Column.C4, ShiftDir.Back, C4, Max - 1000).ConstantX;

            Assert.True(pinned >= 0, "the 7/R column must not be pushed back out once engaged");
        }

        // ---------------------------------------------------------------- barriers and lockout

        [Fact]
        public void LockoutIsJustTheStrongestBarrier()
        {
            // The lockout is not a separate mechanism; it is the hump guarding 7/R. Confirm it
            // behaves like the others, only harder.
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 15;
            cfg.LockoutForcePct = 70;
            ForceComposer c = Composer(cfg);

            int width = cfg.BarrierWidth;
            int inner = Math.Abs(Neutral(c, ((C1 + C2) / 2) + width, Center).ConstantX);
            int lockout = Math.Abs(Neutral(c, LockoutCrest + width, Center).ConstantX);

            Assert.True(lockout > inner * 3,
                "lockout " + lockout + " should dwarf an ordinary barrier " + inner);
        }

        [Fact]
        public void BarrierPeaksAtItsWidthAndReleasesBeyond()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.LockoutForcePct = 70;
            cfg.BarrierWidth = 2500;
            cfg.ColumnDetentForcePct = 0;   // isolate the hump
            ForceComposer c = Composer(cfg);

            int atCrest = Math.Abs(Neutral(c, LockoutCrest, Center).ConstantX);
            int atPeak = Math.Abs(Neutral(c, LockoutCrest + 2500, Center).ConstantX);
            int farBeyond = Math.Abs(Neutral(c, LockoutCrest + 9000, Center).ConstantX);

            Assert.True(atCrest < 200, "the crest is an unstable point, not a shove: " + atCrest);
            Assert.Equal(7000, atPeak);
            Assert.True(farBeyond < atPeak / 4,
                "past the hump the lockout must let go: " + farBeyond + " vs " + atPeak);
        }

        [Fact]
        public void BarrierRepelsFromItsCrestOnBothSides()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.ColumnDetentForcePct = 0;
            ForceComposer c = Composer(cfg);

            Assert.True(Neutral(c, LockoutCrest + 2000, Center).ConstantX > 0, "should push on toward 7/R");
            Assert.True(Neutral(c, LockoutCrest - 2000, Center).ConstantX < 0, "should push back toward 5/6");
        }

        [Fact]
        public void SlidingAlongTheChannelIsFreeAtAColumn()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            // Sitting on a column, lateral force should be negligible - the detent holds it
            // there and no barrier is nearby.
            Assert.True(Math.Abs(Neutral(c, C2, Center).ConstantX) < 500);
        }

        [Fact]
        public void ColumnDetentPullsTowardTheNearestColumn()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 0;    // isolate the detent
            cfg.LockoutForcePct = 0;
            cfg.ColumnDetentForcePct = 12;
            ForceComposer c = Composer(cfg);

            Assert.True(Neutral(c, C2 + 1500, Center).ConstantX < 0);
            Assert.True(Neutral(c, C2 - 1500, Center).ConstantX > 0);
        }

        [Fact]
        public void ColumnDetentHasHysteresisBetweenColumns()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            // Settle on C2, then drift just past the midpoint: the detent should not flip until
            // the stick is clearly closer to C3, or it chatters between the two.
            Neutral(c, C2, Center);
            int justPast = Neutral(c, ((C2 + C3) / 2) + 200, Center).ConstantX;

            Assert.True(justPast < 0, "should still be pulling back toward C2");
        }

        [Fact]
        public void TheGateReachesTheOuterColumns()
        {
            // The outer columns sit at the ends of travel, so nothing may push away from them
            // once the stick is there, or those gears become unreachable.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            Assert.True(Neutral(c, C4, Center).ConstantX >= 0, "must not be pushed off 7/R");
            Assert.True(Neutral(c, C1, Center).ConstantX <= 0, "must not be pushed off 1/2");
        }

        // ---------------------------------------------------------------- slot detent

        [Fact]
        public void SlotDetentResistsThenPullsIn()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            // Early in the travel it resists; past the crossover it pulls into the slot.
            int early = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd, C2, Center - 4000).ConstantY;
            int late = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd, C2, 5000).ConstantY;

            Assert.True(early > 0, "should resist on the way in");
            Assert.True(late < 0, "should snick into the slot");
        }

        [Fact]
        public void SeatedHoldKeepsTheGearEngaged()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.DetentHoldPct = 55;
            ForceComposer c = Composer(cfg);

            Assert.Equal(-5500, c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, C2, 0).ConstantY);
            Assert.Equal(5500, c.Compose(GateState.Engaged, Column.C2, ShiftDir.Back, C2, Max).ConstantY);
        }

        [Fact]
        public void SeatedHoldCanOutpullABaseThatStillSelfCentres()
        {
            // The AB9 measured here drags the stick home with roughly 90% of full force at full
            // deflection. A hold weaker than that loses, and the gear falls straight back out -
            // which is what a 16% hold used to do.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            int hold = Math.Abs(c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, C2, 0).ConstantY);

            Assert.True(hold >= 5000,
                "seated hold " + hold + " is too weak to keep a gear against a self-centring base");
        }

        // ---------------------------------------------------------------- damping

        [Fact]
        public void DampingOpposesMotion()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 25;
            cfg.ChannelWallForcePct = 0;   // isolate damping from the walls
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            ForceFrame moving = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C2, Center,
                                          vx: 60000, vy: 60000);

            Assert.True(moving.ConstantX < 0, "damping must push against rightward motion");
            Assert.True(moving.ConstantY < 0, "damping must push against motion toward the player");
        }

        [Fact]
        public void DampingIsProportionalToSpeedAndCappedAtTheReference()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 25;
            cfg.DampingReferenceSpeed = 120000;
            cfg.ChannelWallForcePct = 0;
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            int half = Math.Abs(c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C2, Center,
                                          0, 60000).ConstantY);
            int atReference = Math.Abs(c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C2, Center,
                                                 0, 120000).ConstantY);
            int wayOver = Math.Abs(c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C2, Center,
                                             0, 600000).ConstantY);

            Assert.Equal(1250, half);
            Assert.Equal(2500, atReference);
            Assert.Equal(2500, wayOver);
        }

        [Fact]
        public void AStationaryStickIsNotDamped()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            ForceFrame still = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C2, Center, 0, 0);
            ForceFrame implied = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C2, Center);

            Assert.Equal(implied.ConstantX, still.ConstantX);
            Assert.Equal(implied.ConstantY, still.ConstantY);
        }

        [Fact]
        public void DampingFollowsTheMeasuredPolarityOfItsAxis()
        {
            // Damping is a constant force like everything else, so it has to be flipped by the
            // same measured sign. Getting this backwards would turn the damper into an
            // accelerator and make the shake worse rather than better.
            EngineConfig plain = FullGainConfig();
            EngineConfig inverted = FullGainConfig();
            inverted.InvertConstantY = true;

            foreach (EngineConfig cfg in new[] { plain, inverted })
            {
                cfg.ChannelWallForcePct = 0;
                cfg.ColumnDetentForcePct = 0;
                cfg.BarrierForcePct = 0;
                cfg.LockoutForcePct = 0;
            }

            int a = Composer(plain).Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                            C2, Center, 0, 60000).ConstantY;
            int b = Composer(inverted).Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                               C2, Center, 0, 60000).ConstantY;

            Assert.Equal(-a, b);
            Assert.NotEqual(0, a);
        }

        [Fact]
        public void DampingNeverPushesTotalForceOutOfRange()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 80;
            cfg.ChannelWallForcePct = 100;
            cfg.ColumnPinForcePct = 100;
            cfg.LockoutForcePct = 100;
            ForceComposer c = Composer(cfg);

            foreach (int v in new[] { -900000, -120000, 0, 120000, 900000 })
            {
                ForceFrame f = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                         LockoutCrest + 2500, Center - 5000, v, v);
                Assert.InRange(f.ConstantX, -GateGeometry.ForceMax, GateGeometry.ForceMax);
                Assert.InRange(f.ConstantY, -GateGeometry.ForceMax, GateGeometry.ForceMax);
            }
        }

        // ---------------------------------------------------------------- polarity and gain

        [Fact]
        public void InvertingConstantXFlipsEveryLateralForce()
        {
            EngineConfig plain = FullGainConfig();
            EngineConfig inverted = FullGainConfig();
            inverted.InvertConstantX = true;

            int a = Neutral(Composer(plain), LockoutCrest + 2500, Center).ConstantX;
            int b = Neutral(Composer(inverted), LockoutCrest + 2500, Center).ConstantX;

            Assert.Equal(-a, b);
            Assert.NotEqual(0, a);
        }

        [Fact]
        public void InvertingConstantYFlipsForeAftButNotLateral()
        {
            // The two axes are measured independently because this base disagrees with itself
            // about them, so one flag must not disturb the other.
            EngineConfig plain = FullGainConfig();
            EngineConfig inverted = FullGainConfig();
            inverted.InvertConstantY = true;

            int between = (C2 + C3) / 2;

            int yPlain = Neutral(Composer(plain), between, Center - 3000).ConstantY;
            int yInverted = Neutral(Composer(inverted), between, Center - 3000).ConstantY;
            Assert.Equal(-yPlain, yInverted);

            int xPlain = Neutral(Composer(plain), LockoutCrest + 2500, Center).ConstantX;
            int xInverted = Neutral(Composer(inverted), LockoutCrest + 2500, Center).ConstantX;
            Assert.Equal(xPlain, xInverted);
        }

        [Fact]
        public void EveryForceScalesWithOverallGain()
        {
            EngineConfig full = FullGainConfig();
            EngineConfig half = FullGainConfig();
            half.OverallGainPct = 50;

            int between = (C2 + C3) / 2;

            int wallFull = Math.Abs(Neutral(Composer(full), between, Center - 3000).ConstantY);
            int wallHalf = Math.Abs(Neutral(Composer(half), between, Center - 3000).ConstantY);
            Assert.Equal(wallFull / 2, wallHalf);

            int lockFull = Math.Abs(Neutral(Composer(full), LockoutCrest + 2500, Center).ConstantX);
            int lockHalf = Math.Abs(Neutral(Composer(half), LockoutCrest + 2500, Center).ConstantX);
            Assert.Equal(lockFull / 2, lockHalf);
        }

        [Fact]
        public void GainIsCappedUntilPolarityIsConfirmed()
        {
            // An unmeasured base might apply these backwards, so nothing may exceed the cap.
            EngineConfig cfg = new EngineConfig { OverallGainPct = 100, PolarityConfirmed = false };
            ForceComposer c = Composer(cfg);

            int wall = Math.Abs(Neutral(c, (C2 + C3) / 2, Center - 3000).ConstantY);
            int expected = (int)Math.Round(
                GateGeometry.ForceMax * (cfg.ChannelWallForcePct / 100.0) *
                (EngineConfig.UnconfirmedGainCapPct / 100.0));

            Assert.Equal(expected, wall);
        }

        [Fact]
        public void NoForceEscapesFullScale()
        {
            // Several forces sum on X, so confirm the total is still bounded on a hostile config.
            EngineConfig cfg = FullGainConfig();
            cfg.LockoutForcePct = 100;
            cfg.BarrierForcePct = 100;
            cfg.ColumnDetentForcePct = 100;
            cfg.ColumnPinForcePct = 100;
            cfg.ChannelWallForcePct = 100;
            ForceComposer c = Composer(cfg);

            for (int x = 0; x <= Max; x += 137)
            {
                for (int y = 0; y <= Max; y += 4093)
                {
                    ForceFrame f = Neutral(c, x, y);
                    Assert.InRange(f.ConstantX, -GateGeometry.ForceMax, GateGeometry.ForceMax);
                    Assert.InRange(f.ConstantY, -GateGeometry.ForceMax, GateGeometry.ForceMax);

                    ForceFrame g = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, y);
                    Assert.InRange(g.ConstantX, -GateGeometry.ForceMax, GateGeometry.ForceMax);
                    Assert.InRange(g.ConstantY, -GateGeometry.ForceMax, GateGeometry.ForceMax);
                }
            }
        }

        [Fact]
        public void FreeStickReleasesEverything()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FreeStick = true;
            ForceComposer c = Composer(cfg);

            foreach (GateState state in new[] { GateState.Neutral, GateState.Traveling, GateState.Engaged })
            {
                ForceFrame f = c.Compose(state, Column.C4, ShiftDir.Back, C4, Max);
                Assert.Equal(0, f.ConstantX);
                Assert.Equal(0, f.ConstantY);
                Assert.Equal(0, f.SpringX.PositiveCoefficient);
                Assert.Equal(0, f.SpringY.PositiveCoefficient);
                Assert.Equal(0, f.DamperCoefficient);
            }
        }

        [Fact]
        public void SpringsAreNeverUsedForTheGate()
        {
            // Springs cannot make a wall on this hardware. If one reappears in the gate, the
            // walls have silently gone soft again.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            for (int x = 0; x <= Max; x += 1021)
            {
                foreach (GateState state in new[] { GateState.Neutral, GateState.Traveling, GateState.Engaged })
                {
                    ForceFrame f = c.Compose(state, Column.C2, ShiftDir.Fwd, x, Center - 2000);
                    Assert.Equal(0, f.SpringX.PositiveCoefficient);
                    Assert.Equal(0, f.SpringX.NegativeCoefficient);
                    Assert.Equal(0, f.SpringY.PositiveCoefficient);
                    Assert.Equal(0, f.SpringY.NegativeCoefficient);
                }
            }
        }

        [Fact]
        public void MirroringRelabelsGearsWithoutMovingTheGate()
        {
            // Layout preference must not touch geometry: the columns stay where the device puts
            // them, only the names change. Anything else would mirror the forces too.
            var plain = new EngineConfig();
            var mirrored = new EngineConfig { MirrorColumns = true };

            Assert.Equal(plain.BuildGeometry().ColumnTarget(Column.C1),
                         mirrored.BuildGeometry().ColumnTarget(Column.C1));

            Assert.Equal(1, GateGeometry.GearOf(Column.C1, ShiftDir.Fwd));
            Assert.Equal(7, GateGeometry.GearOf(Column.C1, ShiftDir.Fwd, mirrorColumns: true));
            Assert.Equal(8, GateGeometry.GearOf(Column.C1, ShiftDir.Back, mirrorColumns: true));
            Assert.Equal(2, GateGeometry.GearOf(Column.C1, ShiftDir.Fwd, mirrorSlots: true));
        }
    }
}
