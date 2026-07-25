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

        /// <summary>
        /// Where the lockout gate sits. Asked of the geometry rather than assumed: the gate places
        /// itself against the last main-section column, not at the midpoint of the gap it guards.
        /// </summary>
        private static int GateCentre(EngineConfig cfg)
        {
            return cfg.BuildGeometry().LockoutCentre;
        }

        /// <summary>A point clear of the gate's band. The faces live inside it, so this is close.</summary>
        private static int OutsideGate(EngineConfig cfg, int sign)
        {
            GateGeometry geo = cfg.BuildGeometry();
            return geo.LockoutCentre + (sign * (geo.LockoutHalfWidth + 200));
        }

        /// <summary>Largest offset from the gate's centre that still sees its full force.</summary>
        private static int GateFlatCore(EngineConfig cfg)
        {
            GateGeometry geo = cfg.BuildGeometry();
            int face = Math.Min(cfg.WallRamp, Math.Max(1, geo.LockoutHalfWidth / 2));
            return geo.LockoutHalfWidth - face;
        }

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
        public void AWallIsFlatPastItsBite()
        {
            // The stability property for every wall, not just the lockout: past the short bite
            // the force is a plateau, identical at any depth. A hand leaning anywhere on it
            // rests on zero gradient, and a flat force has nothing for the loop's delay to
            // pump - the shape that cannot ring.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int shallow = Neutral(c, between, Center + cfg.ChannelHalfEnter + cfg.WallRamp + 100).ConstantY;
            int deep = Neutral(c, between, Center + 6000).ConstantY;

            Assert.Equal(shallow, deep);
            Assert.Equal(-9000, shallow);
        }

        [Fact]
        public void ASlotWallIsFlatPastItsBite()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.SlotHalfWidth = 1100;
            ForceComposer c = Composer(cfg);

            int shallow = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                                    C2 + 1100 + cfg.WallRamp + 50, 2000).ConstantX;
            int deep = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                                 C2 + 2300, 2000).ConstantX;

            Assert.Equal(shallow, deep);
            Assert.Equal(-9000, shallow);
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
        public void LockoutDwarfsAnOrdinaryBarrier()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 15;
            cfg.LockoutForcePct = 70;
            cfg.ColumnDetentForcePct = 0;   // isolate the two shapes
            ForceComposer c = Composer(cfg);

            int inner = Math.Abs(Neutral(c, ((C1 + C2) / 2) + cfg.BarrierWidth, Center).ConstantX);
            int lockout = Math.Abs(Neutral(c, GateCentre(cfg) + 1500, Center).ConstantX);

            Assert.True(lockout > inner * 3,
                "lockout " + lockout + " should dwarf an ordinary barrier " + inner);
        }

        [Fact]
        public void LockoutIsFlatAndOneWayAcrossItsWholeBand()
        {
            // Two properties in one shape. Flat: no gradient anywhere in the fight, so there
            // is nothing for the loop's delay to pump. One-way: the push is toward the main
            // gears on both sides of the centre, because an over-centre gate refunds past its
            // crest the energy it charged before it - which let a fast flick through for free.
            EngineConfig cfg = FullGainConfig();
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            ForceComposer c = Composer(cfg);

            int core = GateFlatCore(cfg);
            foreach (int offset in new[] { -core, -core / 2, 0, core / 2, core })
            {
                Assert.Equal(-7000, Neutral(c, GateCentre(cfg) + offset, Center).ConstantX);
            }
        }

        [Fact]
        public void TheAttackCostsTheLockoutAlmostNoneOfItsToll()
        {
            // The lockout was exempted from time shaping at first, on the theory that slewing a
            // crossing hands a fast flick a discount. The arithmetic says otherwise, and this
            // test is that arithmetic: the band is thousands of counts wide, so even a fast flick
            // spends far longer inside it than the attack lasts. Cross it at speed with the
            // attack on and off, and compare the toll actually paid.
            //
            // The exemption was not free - it left the lockout as the one force in the gate still
            // arriving raw, so it rejected the lever hard where every wall had learned not to.
            EngineConfig shaped = FullGainConfig();
            shaped.ColumnDetentForcePct = 0;
            shaped.BarrierForcePct = 0;
            shaped.DampingPct = 0;
            shaped.WallAttackMs = 20;

            EngineConfig raw = FullGainConfig();
            raw.ColumnDetentForcePct = 0;
            raw.BarrierForcePct = 0;
            raw.DampingPct = 0;
            raw.WallAttackMs = 0;

            long withAttack = TollAcross(shaped);
            long without = TollAcross(raw);

            Assert.True(withAttack >= without * 0.9,
                "the attack should cost the toll almost nothing: " + withAttack + " of " + without);
        }

        /// <summary>Sums the lockout force felt crossing the whole band at a brisk 60000 counts/s.</summary>
        private static long TollAcross(EngineConfig cfg)
        {
            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();

            const int speed = 60000;          // counts per second
            const int step = speed / 1000;    // per millisecond tick

            long toll = 0;
            for (int x = geo.LockoutCentre - geo.LockoutHalfWidth;
                 x <= geo.LockoutCentre + geo.LockoutHalfWidth;
                 x += step)
            {
                toll += Math.Abs(c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                           x, Center, speed, 0, dtMs: 1).ConstantX);
            }
            return toll;
        }

        [Fact]
        public void LockoutFollowsTheMirroredGearMap()
        {
            // Mirroring puts 7/R at the other physical end of the gate, so the gate guarding
            // it must move to that gap and push the other way - toward where the main gears
            // now live.
            EngineConfig cfg = FullGainConfig();
            cfg.MirrorColumns = true;
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            ForceComposer c = Composer(cfg);

            Assert.Equal(7000, Neutral(c, GateCentre(cfg), Center).ConstantX);
            Assert.Equal(0, Neutral(c, GateCentre(FullGainConfig()), Center).ConstantX);
        }

        [Fact]
        public void LockoutIsADotOnTheChannelNotAZone()
        {
            // The walls own the box, so the gate only guards the crossing itself. Approach on
            // either side is free - no long zone dragging the stick around the channel.
            EngineConfig cfg = FullGainConfig();
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            ForceComposer c = Composer(cfg);

            Assert.Equal(0, Neutral(c, OutsideGate(cfg, -1), Center).ConstantX);
            Assert.Equal(0, Neutral(c, OutsideGate(cfg, +1), Center).ConstantX);
        }

        [Fact]
        public void TheGateNeverReachesPastTheWidthItDeclares()
        {
            // Both faces live inside the band. They used to overhang it by a whole bite distance,
            // which ate the clearance the gate is positioned with and put the onset of the toll on
            // top of the 5/6 column - a hard bump exactly where the hand expects to be resting on
            // a column. A huge bite must still not stretch the gate outward, and must still leave
            // a flat core in the middle rather than collapsing to a spike.
            EngineConfig cfg = FullGainConfig();
            cfg.WallRamp = 6000;
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();

            Assert.Equal(0, Neutral(c, geo.LockoutCentre - geo.LockoutHalfWidth, Center).ConstantX);
            Assert.Equal(0, Neutral(c, geo.LockoutCentre + geo.LockoutHalfWidth, Center).ConstantX);
            Assert.Equal(-7000, Neutral(c, GateCentre(cfg), Center).ConstantX);
            Assert.True(GateFlatCore(cfg) > 0, "a flat core must survive any bite setting");
        }

        [Fact]
        public void TheGateClearsTheColumnItGuards()
        {
            // Nothing of the gate, face included, may reach into the 5/6 column's band. That is
            // the whole point of positioning it with a clearance.
            EngineConfig cfg = FullGainConfig();
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();

            int columnBand = geo.ColumnTarget(Column.C3) + geo.ColumnExitHalfWidth(Column.C3);
            for (int x = geo.ColumnTarget(Column.C3); x <= columnBand; x += 100)
            {
                Assert.Equal(0, Neutral(c, x, Center).ConstantX);
            }
        }

        [Fact]
        public void TheGateSitsAgainstTheMainSectionNotInTheMiddleOfTheGap()
        {
            // The gate used to sit at the gap's midpoint, thousands of counts right of 5/6, and
            // the dead travel in between was a usability trap: the hand stops where the gate
            // stops it, assumes it has reached a column, and finds fore/aft neither engages a
            // gear nor explains itself. Its inner face now begins where the 5/6 column's band
            // ends, so meeting the gate means the column is directly behind you.
            GateGeometry geo = new EngineConfig().BuildGeometry();

            int innerFace = geo.LockoutCentre - geo.LockoutHalfWidth;
            int columnBand = geo.ColumnTarget(Column.C3) + geo.ColumnExitHalfWidth(Column.C3);

            Assert.Equal(columnBand, innerFace);
            Assert.True(geo.LockoutCentre < (C3 + C4) / 2,
                "the gate must sit nearer the main section than the midpoint it used to occupy");
        }

        [Fact]
        public void CrossingTheGateHandsTheStickToTheSeventhColumn()
        {
            // The lateral guide switches columns at the barrier crests, not at the geometric
            // midpoints. With the gate off-centre a midpoint boundary would keep pulling the
            // stick back toward 5/6 for thousands of counts after it had fought its way through
            // - dragging it straight back into the gate it just paid for.
            EngineConfig cfg = FullGainConfig();
            GateGeometry geo = cfg.BuildGeometry();

            Assert.Equal(Column.C3, geo.NearestColumn(geo.LockoutCentre - 500, Column.C3));
            Assert.Equal(Column.C4, geo.NearestColumn(geo.LockoutCentre + 3000, Column.C3));

            // And the guide past the gate must pull on toward 7/R, not back.
            EngineConfig noGate = FullGainConfig();
            noGate.LockoutForcePct = 0;
            Assert.True(Neutral(Composer(noGate), OutsideGate(noGate, +1), Center).ConstantX > 0,
                "past the gate the guide should carry on toward 7/R");
        }

        [Fact]
        public void AnOrdinaryBarrierPeaksAtItsWidthAndReleasesBeyond()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 15;
            cfg.BarrierWidth = 2500;
            cfg.ColumnDetentForcePct = 0;   // isolate the hump
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            int crest = (C1 + C2) / 2;
            int atCrest = Math.Abs(Neutral(c, crest, Center).ConstantX);
            int atPeak = Math.Abs(Neutral(c, crest + 2500, Center).ConstantX);
            int farBeyond = Math.Abs(Neutral(c, crest + 9000, Center).ConstantX);

            Assert.True(atCrest < 200, "the crest is an unstable point, not a shove: " + atCrest);
            Assert.Equal(1500, atPeak);
            Assert.True(farBeyond < atPeak / 4,
                "past the hump the barrier must let go: " + farBeyond + " vs " + atPeak);
        }

        [Fact]
        public void BarrierRepelsFromItsCrestOnBothSides()
        {
            // An ordinary hump resists on the way up and helps on the way down - unlike the
            // lockout, these are meant to be cheap to cross.
            EngineConfig cfg = FullGainConfig();
            cfg.ColumnDetentForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            int crest = (C1 + C2) / 2;
            Assert.True(Neutral(c, crest + 2000, Center).ConstantX > 0, "should push on toward the next column");
            Assert.True(Neutral(c, crest - 2000, Center).ConstantX < 0, "should push back where it came from");
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

        // ---------------------------------------------------------------- the funnel

        [Fact]
        public void TheGuideStrengthensIntoAFunnelAsAGearIsTakenP()
        {
            // Off-column entries used to be a dead end - the gate wall held, no gear arrived, and
            // nothing steered the hand onto a slot. The lateral guide now grows with depth out of
            // the channel, like the tapered mouth of a real gate.
            EngineConfig cfg = FullGainConfig();
            cfg.ColumnDetentForcePct = 12;
            cfg.ColumnFunnelForcePct = 40;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            int offColumn = C2 + 2200;
            int sliding = Math.Abs(Neutral(c, offColumn, Center).ConstantX);
            int entering = Math.Abs(Neutral(c, offColumn, Center - cfg.ChannelHalfExit - 500).ConstantX);

            Assert.True(entering > sliding * 2,
                "the funnel should take over on entry: " + entering + " vs " + sliding);
        }

        [Fact]
        public void AtGearDepthTheSlotWallsApplyWithoutALatch()
        {
            // The hole that let the lever be walked sideways from gear to gear. Overpowering one
            // slot wall dropped the latch, the neutral field took over, and down at gear depth
            // that field had no lateral wall at all - so the gate gave way completely and the
            // guide then adopted each column the lever passed, helping it along. Confinement is a
            // fact about depth now, so the wall is here whether or not a column is latched.
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            // Deep, and pushed well off the column: full slot-wall strength, pushing back.
            int deep = cfg.EngageDepth + 500;
            int wall = Neutral(c, C2 + 6000, deep).ConstantX;

            Assert.Equal(-9000, wall);

            // In the channel at the same lateral offset there is no wall - that is the one place
            // the lever is meant to travel sideways freely.
            Assert.True(Math.Abs(Neutral(c, C2 + 6000, Center).ConstantX) < 5000,
                "the neutral channel must stay open across the gate");
        }

        [Fact]
        public void ThereIsNowhereLaterallyFreeAtGearDepth()
        {
            // Walk the whole width at gear depth. Between the columns the lever must always be
            // under a substantial wall - that is what stops it being dragged from gear to gear
            // along the top of the pattern. It used to be free here, held only by a guide of at
            // most 40%, which then adopted each column it passed and helped it onward.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();
            int deep = cfg.EngageDepth + 500;

            for (int x = 0; x <= Max; x += 250)
            {
                Column nearest = geo.NearestColumn(x, Column.None);

                // Skip a column's own width and the wall's face, which are meant to be soft.
                int free = geo.ColumnFreeHalfWidth(nearest);
                int face = Math.Min(cfg.WallRamp, Math.Max(200, (geo.ColumnSpacing / 2) - free));
                if (Math.Abs(x - geo.ColumnTarget(nearest)) <= free + face) continue;

                int force = Math.Abs(Neutral(c, x, deep).ConstantX);
                Assert.True(force >= 5000, "the gate is free at x=" + x + ", force " + force);
            }
        }

        [Fact]
        public void TheWallAtGearDepthPushesBackTowardItsColumn()
        {
            // Direction, checked clear of the boundaries where the guide's hysteresis legitimately
            // holds on to the column it came from.
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            GateGeometry geo = cfg.BuildGeometry();
            int deep = cfg.EngageDepth + 500;

            foreach (Column column in new[] { Column.C2, Column.C3 })
            {
                int target = geo.ColumnTarget(column);
                foreach (int offset in new[] { -6000, -3000, 3000, 6000 })
                {
                    // A fresh composer each time, so no earlier position biases the choice.
                    int force = Neutral(Composer(cfg), target + offset, deep).ConstantX;
                    Assert.True(Math.Sign(force) == -Math.Sign(offset),
                        column + " at " + offset + " should be pushed back, got " + force);
                }
            }
        }

        [Fact]
        public void TheFunnelPushesTowardTheColumnAndIsFlatBottomed()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);
            int deep = Center - cfg.ChannelHalfExit - 500;

            // Either side of a column it steers inward.
            Assert.True(Neutral(c, C2 + 2200, deep).ConstantX < 0);
            Assert.True(Neutral(c, C2 - 2200, deep).ConstantX > 0);

            // And across the column's own width it does nothing at all, so there is no centre
            // line for the stick to hunt around exactly where the hand is trying to hold still.
            GateGeometry geo = cfg.BuildGeometry();
            int free = geo.ColumnFreeHalfWidth(Column.C2);
            foreach (int offset in new[] { -free, -free / 2, 0, free / 2, free })
            {
                Assert.Equal(0, Neutral(c, C2 + offset, deep).ConstantX);
            }
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

        // ---------------------------------------------------------------- rebound absorption

        [Fact]
        public void LeaningOnAWallGetsFullForce()
        {
            // The yield must never soften a wall someone is leaning on: at rest the wall is
            // solid regardless of the absorption setting.
            EngineConfig cfg = FullGainConfig();
            cfg.WallYieldPct = 45;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            ForceFrame still = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                         between, Center + 3500, 0, 0);
            ForceFrame implied = Neutral(c, between, Center + 3500);

            Assert.Equal(implied.ConstantY, still.ConstantY);
            Assert.NotEqual(0, still.ConstantY);
        }

        [Fact]
        public void PushingIntoTheWallIsNeverReduced()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;   // isolate the yield from the damping term
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            // Moving deeper into the wall: force opposes motion, so it passes through whole.
            int leaning = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                    between, Center + 3500, 0, 0).ConstantY;
            int pushing = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                    between, Center + 3500, 0, 20000).ConstantY;

            Assert.Equal(leaning, pushing);
        }

        [Fact]
        public void ReboundOffAWallIsAbsorbed()
        {
            // The instability mechanism: the stick overshoots, and the delayed wall force then
            // accelerates it back out with interest. On the way out the force is scaled down,
            // so each bounce returns less energy than the last and the ring dies.
            EngineConfig cfg = FullGainConfig();
            cfg.WallYieldPct = 45;
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int full = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, Center + 3500, 0, 0).ConstantY;
            int rebounding = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                       between, Center + 3500, 0,
                                       -(cfg.YieldVelocityDeadband + cfg.YieldVelocityBlend)).ConstantY;

            Assert.Equal((int)Math.Round(full * 0.55), rebounding);
        }

        [Fact]
        public void CreepBelowTheVelocityDeadbandStaysSolid()
        {
            // Sensor jitter reads as a small velocity; it must not soften a wall being leant on.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int full = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, Center + 3500, 0, 0).ConstantY;
            int creeping = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                     between, Center + 3500, 0, -1000).ConstantY;

            Assert.Equal(full, creeping);
        }

        [Fact]
        public void SnickKeepsMostOfItsPullWhileAssisting()
        {
            // The pull into the slot is supposed to do positive work; it gets a much milder
            // absorption than the walls so the shift still feels like it seats itself.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);

            int full = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd, C2, 5000, 0, 0).ConstantY;
            int assisted = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd, C2, 5000, 0, -20000).ConstantY;

            Assert.True(full < 0, "the pull should point into the slot");
            Assert.True(Math.Abs(assisted) >= (int)(Math.Abs(full) * 0.8),
                "the snick lost too much: " + assisted + " of " + full);
            Assert.True(Math.Abs(assisted) < Math.Abs(full), "some absorption should still apply");
        }

        [Fact]
        public void ASlotWallGetsTheSameBiteAsAGateWall()
        {
            // The slot wall's face used to be squeezed into the state machine's exit band, about
            // a fifth of the configured bite, so it was several times steeper than any wall a
            // hand found stable - the slots oscillated while the channel did not. A latched gear
            // is now held until the stick returns through the channel, so nothing needs the
            // squeeze and both kinds of wall share one bite distance.
            EngineConfig cfg = FullGainConfig();
            cfg.WallRamp = 4000;
            cfg.SlotHalfWidth = 1100;
            ForceComposer c = Composer(cfg);

            // Half way up the face, the force is half the plateau - a gentle gradient, not the
            // near-step the exit band used to force.
            int halfway = C2 + 1100 + 2000;
            Assert.Equal(-4500, c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, halfway, 2000).ConstantX);
        }

        [Fact]
        public void ASlotWallStillCannotBleedIntoTheNextColumn()
        {
            // The one bound left on the face: whatever the bite is set to, the wall must be at
            // full strength before the neighbouring column's territory begins.
            EngineConfig cfg = FullGainConfig();
            cfg.WallRamp = 40000;   // absurd on purpose
            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();

            int atMidpoint = C2 + (geo.ColumnSpacing / 2);
            Assert.Equal(-9000, c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, atMidpoint, 2000).ConstantX);
        }

        // ---------------------------------------------------------------- time shaping

        [Fact]
        public void AWallAttacksOverTimeInsteadOfArrivingAsAStep()
        {
            // With the attack on, contact winds up at a bounded rate instead of landing as a
            // delay-late hammer blow. Full scale over 20 ms means 500 units per millisecond.
            EngineConfig cfg = FullGainConfig();
            cfg.WallAttackMs = 20;
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;
            int deep = Center + 4000;

            int first = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                  between, deep, 0, 20000, dtMs: 1).ConstantY;
            Assert.Equal(-500, first);

            int last = first;
            for (int i = 0; i < 40; i++)
            {
                last = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, deep, 0, 20000, dtMs: 1).ConstantY;
            }
            Assert.Equal(-9000, last);
        }

        [Fact]
        public void ASlotWallAttacksLikeAChannelWall()
        {
            // The shaping covers both kinds of wall: fore/aft in the channel, sideways in a
            // slot. The slot wall is the one whose face is clamped short by the gear-exit
            // band, so it needs the attack most.
            EngineConfig cfg = FullGainConfig();
            cfg.WallAttackMs = 20;
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);

            int first = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                                  C2 + 2300, 2000, 20000, 0, dtMs: 1).ConstantX;
            Assert.Equal(-500, first);
        }

        [Fact]
        public void ReleaseIsInstantEvenMidAttack()
        {
            // A retreating stick must never be chased by stale force: any drop passes through
            // immediately, only growth is rate-limited.
            EngineConfig cfg = FullGainConfig();
            cfg.WallAttackMs = 20;
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            for (int i = 0; i < 3; i++)
            {
                c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                          between, Center + 4000, 0, 20000, dtMs: 1);
            }

            int released = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                     between, Center, 0, -20000, dtMs: 1).ConstantY;
            Assert.Equal(0, released);
        }

        [Fact]
        public void AStillHandGetsAFrozenForce()
        {
            // Static friction: pressed against the wall and effectively still, small force
            // deviations are held instead of tracked. A delayed gradient can only pump through
            // force changes, so freezing them is what quiets a light sustained press.
            EngineConfig cfg = FullGainConfig();
            cfg.WallAttackMs = 20;
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            for (int i = 0; i < 40; i++)
            {
                c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                          between, Center + 4000, 0, 20000, dtMs: 1);
            }

            // Drift back onto the face: the position asks for less force, but the hand is
            // still, so the force stays frozen at the plateau.
            int onFace = Center + cfg.ChannelHalfEnter + 500;
            int frozen = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                   between, onFace, 0, 0, dtMs: 1).ConstantY;
            Assert.Equal(-9000, frozen);

            // The same position while genuinely moving tracks the face value immediately.
            int moving = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                   between, onFace, 0, -20000, dtMs: 1).ConstantY;
            Assert.True(Math.Abs(moving) < 9000, "a moving stick must be tracked: " + moving);
        }

        [Fact]
        public void TimeShapingIsBypassedWhenOffOrWithoutTime()
        {
            // Attack zero is the escape hatch, and a zero dt (as every other test here uses)
            // must behave exactly like the shaping never existed.
            EngineConfig off = FullGainConfig();
            off.DampingPct = 0;
            int between = (C2 + C3) / 2;

            int instant = Composer(off).Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                                between, Center + 4000, 0, 20000, dtMs: 1).ConstantY;
            Assert.Equal(-9000, instant);

            EngineConfig on = FullGainConfig();
            on.WallAttackMs = 20;
            on.DampingPct = 0;
            int bypassed = Composer(on).Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                                between, Center + 4000, 0, 20000).ConstantY;
            Assert.Equal(-9000, bypassed);
        }

        [Fact]
        public void DampingKeepsFullBandwidthThroughTheAttack()
        {
            // Damping is the stabiliser; slewing it would defeat its purpose. It joins after
            // the shaping and must appear at full strength from the first millisecond.
            EngineConfig cfg = FullGainConfig();
            cfg.WallAttackMs = 20;
            cfg.DampingPct = 25;
            cfg.ChannelWallForcePct = 0;
            cfg.ChannelGuideForcePct = 0;
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            int force = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                  C2, Center, 0, 120000, dtMs: 1).ConstantY;
            Assert.Equal(-2500, force);
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
                                         GateCentre(cfg) + 1800, Center - 5000, v, v);
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

            int a = Neutral(Composer(plain), GateCentre(plain) + 1800, Center).ConstantX;
            int b = Neutral(Composer(inverted), GateCentre(inverted) + 1800, Center).ConstantX;

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

            int xPlain = Neutral(Composer(plain), GateCentre(plain) + 1800, Center).ConstantX;
            int xInverted = Neutral(Composer(inverted), GateCentre(inverted) + 1800, Center).ConstantX;
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

            int lockFull = Math.Abs(Neutral(Composer(full), GateCentre(full) + 1800, Center).ConstantX);
            int lockHalf = Math.Abs(Neutral(Composer(half), GateCentre(half) + 1800, Center).ConstantX);
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
