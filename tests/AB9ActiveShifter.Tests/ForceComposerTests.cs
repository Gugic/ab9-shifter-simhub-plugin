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

            // And the guide past the gate must never pull BACK toward the main section - that is the
            // property that matters, because being dragged back into a gate you have just paid for
            // is what a midpoint boundary used to do. Across the gate's doorway the answer is now
            // zero rather than a pull onward, because the two boundary rules disagree over that span
            // and the field is faded out wherever a handover can happen; zero satisfies "not back".
            EngineConfig noGate = FullGainConfig();
            noGate.LockoutForcePct = 0;
            GateGeometry g2 = noGate.BuildGeometry();

            for (int x = OutsideGate(noGate, +1); x <= C4; x += 100)
            {
                Assert.True(Neutral(Composer(noGate), x, Center).ConstantX >= 0,
                    "past the gate the guide must never pull back toward the main section, at x=" + x);
            }

            // Once clear of the handover window it carries on toward 7/R under its own steam.
            int clear = g2.LockoutCentre + g2.LockoutHalfWidth;
            while (g2.HandoverClearance(clear) == 0) clear += 100;
            Assert.True(Neutral(Composer(noGate), clear + noGate.WallRamp, Center).ConstantX > 0,
                "clear of the window the guide should carry on toward 7/R");
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

            // Settle on C2, then drift past the midpoint. The pick still holds on to C2 for the
            // hysteresis distance - but the force there is zero either way, which is the point:
            // the hysteresis no longer has to hide a cliff, so it can be small.
            GateGeometry geo = cfg.BuildGeometry();
            int midpoint = (C2 + C3) / 2;

            Neutral(c, C2, Center);
            Assert.Equal(Column.C2, geo.NearestColumn(midpoint + 200, Column.C2));
            Assert.Equal(0, Neutral(c, midpoint + 200, Center).ConstantX);

            // Clear of the window the guide has plainly changed hands, and pulls the other way.
            int clear = midpoint + cfg.DetentHysteresis + cfg.WallRamp + 100;
            Assert.True(Neutral(c, clear, Center).ConstantX > 0, "past the window the pull is toward C3");
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

        // ---------------------------------------------------------------- one lateral field

        [Fact]
        public void EnteringYourOwnColumnNeverChangesTheLateralForce()
        {
            // THE REGRESSION TEST. Its absence is what let a 4924 DI step - nearly six newton-metres
            // - exist at the same physical position, because the two branches used different formulas
            // for the same column. The channel bands are hysteretic, so that made the force depend on
            // how the lever had arrived, and going around a divider end is the manoeuvre that crosses
            // the boundary. It rang there and nowhere else.
            EngineConfig cfg = FullGainConfig();
            GateGeometry geo = cfg.BuildGeometry();

            foreach (ShiftDir dir in new[] { ShiftDir.Fwd, ShiftDir.Back })
            {
                for (int x = 0; x <= Max; x += 617)
                {
                    // The case that matters, and the only one a normal shift ever visits: the gear
                    // the lever is in is the column the lever is over. A latched gear does own the
                    // lateral field - that is what pushes a lever dragged sideways back to its own
                    // slot - but entering or leaving your own column must not change the force by a
                    // single unit, because that boundary is hysteretic and the mouth is where a hand
                    // crosses it again and again.
                    foreach (int depth in new[] { 0, 900, 1399, 1400, 1401, 2399, 2400, 2401, 4000, 9000, 20000 })
                    {
                        int y = dir == ShiftDir.Fwd ? Center - depth : Center + depth;

                        // The column the tunnel itself would pick at this position - which is the one
                        // a lever arriving here would have latched.
                        Column latched = geo.GuideColumn(x, Column.None, geo.InChannel(y));

                        int neutral = Neutral(Composer(cfg), x, y).ConstantX;
                        int inColumn = Composer(cfg)
                            .Compose(GateState.Engaged, latched, dir, x, y).ConstantX;

                        Assert.Equal(neutral, inColumn);
                    }
                }
            }
        }

        [Fact]
        public void TheLateralFieldHasNoStepAcrossTheChannelBands()
        {
            // The other half of the same property: no depth at which the lever is handed a jump.
            // The plateau profile is piecewise linear and continuous, so a single count of depth
            // may only change the force by about one stiffness unit.
            EngineConfig cfg = FullGainConfig();
            int limit = (int)Math.Ceiling(cfg.ColumnPinForcePct * 100.0 / cfg.WallRamp) + 2;

            foreach (int offset in new[] { 0, 800, 1500, 2400, 5000, 9000 })
            {
                int previous = Neutral(Composer(cfg), C2 + offset, Center).ConstantX;
                for (int depth = 1; depth <= 6000; depth++)
                {
                    int now = Neutral(Composer(cfg), C2 + offset, Center - depth).ConstantX;
                    Assert.True(Math.Abs(now - previous) <= limit,
                        "step of " + (now - previous) + " at depth " + depth + ", offset " + offset);
                    previous = now;
                }
            }
        }

        [Fact]
        public void TheLockoutCannotBeConveyedPastAtDepth()
        {
            // Unifying the field opened a complete lockout bypass, found by review before it
            // shipped: with the tunnel's crest boundaries applied at depth, a lever dragged out of
            // 5/6 crosses the gate's crest and the guide adopts 7/R, so the wall that was holding
            // it in reverses into a conveyor pushing it toward 7 at full pin force - no toll paid.
            // Below the tunnel the boundary is the plain midpoint, so the lever keeps 5/6's inward
            // wall the whole way across the gate's band.
            EngineConfig cfg = FullGainConfig();
            GateGeometry geo = cfg.BuildGeometry();
            int deep = Center - cfg.EngageDepth - 2000;

            int from = geo.LockoutCentre - geo.LockoutHalfWidth;
            int to = (geo.ColumnTarget(Column.C3) + geo.ColumnTarget(Column.C4)) / 2;

            for (int x = from; x < to; x += 100)
            {
                int force = Neutral(Composer(cfg), x, deep).ConstantX;
                Assert.True(force <= 0,
                    "at x=" + x + " the field must not carry the lever toward 7/R, got " + force);
            }
        }

        [Fact]
        public void EveryLateralForceRisesAtTheWallStiffness()
        {
            // One stiffness everywhere: a gentler force gets a shorter face, never a steeper one.
            // The funnel used to have its own ramp, and at the bottom of its range that made it
            // 13.3 DI per count against a wall face of 3.8 - the steepest gradient in the gate,
            // living only in the mouth, which is the one region the lever crosses on every shift.
            EngineConfig cfg = FullGainConfig();
            cfg.WallRamp = 2364;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            GateGeometry geo = cfg.BuildGeometry();

            int corridor = Math.Min(cfg.SlotHalfWidth, geo.ColumnFreeHalfWidth(Column.C2) - 100);
            double expected = (cfg.ColumnPinForcePct * 100.0)
                / Math.Min(cfg.WallRamp, (geo.ColumnSpacing / 2) - corridor);

            foreach (int depth in new[] { 1200, 2400, 3600, 4800, 12000 })
            {
                int a = Math.Abs(Neutral(Composer(cfg), C2 + corridor + 100, Center - depth).ConstantX);
                int b = Math.Abs(Neutral(Composer(cfg), C2 + corridor + 200, Center - depth).ConstantX);
                if (a == 0 || b <= a) continue;   // past the plateau, nothing left to measure

                double slope = (b - a) / 100.0;
                Assert.True(Math.Abs(slope - expected) < 0.6,
                    "stiffness at depth " + depth + " was " + slope + ", expected " + expected);
            }
        }

        [Fact]
        public void TheGuideColumnIsForgottenWhenTheForcesAreReleased()
        {
            // The guide column is remembered between ticks for its hysteresis, so it has to be
            // dropped when the forces are. Otherwise the lever can be moved anywhere with free
            // stick on and come back to a saturated wall aimed at where it used to be.
            EngineConfig held = FullGainConfig();
            ForceComposer c = Composer(held);

            c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center);

            EngineConfig free = FullGainConfig();
            free.FreeStick = true;
            ForceComposer released = new ForceComposer(free.BuildGeometry(), free);
            Assert.Equal(0, released.Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center).ConstantX);

            // Coming back at the far end of travel must resolve from position alone.
            int fresh = Neutral(Composer(held), C1, Center - 6000).ConstantX;
            Assert.True(fresh >= 0, "a cold guide must not push away from the column it is on: " + fresh);
        }

        // ---------------------------------------------------------------- slot mouths

        private static EngineConfig MouthConfig(SlotMouthShape shape)
        {
            EngineConfig cfg = FullGainConfig();
            cfg.MouthShape = shape;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            return cfg;
        }

        [Fact]
        public void SquareIsTheGateExactlyAsItWasWithoutTheFeature()
        {
            // The default must be inert. Sweep the whole gate at every depth and demand that Square
            // with the mouth dials wide open is bit-for-bit what the gate produces with them shut.
            EngineConfig square = MouthConfig(SlotMouthShape.Square);
            square.MouthDepth = 12000;
            square.MouthOpenPct = 100;

            EngineConfig off = MouthConfig(SlotMouthShape.Square);
            off.MouthDepth = 1000;
            off.MouthOpenPct = 0;

            for (int x = 0; x <= Max; x += 811)
            {
                foreach (int depth in new[] { 0, 1400, 2400, 4000, 6000, 12000, 20000 })
                {
                    int a = Neutral(Composer(square), x, Center - depth).ConstantX;
                    int b = Neutral(Composer(off), x, Center - depth).ConstantX;
                    Assert.Equal(b, a);
                }
            }
        }

        [Fact]
        public void AShapedMouthOpensAtTheTunnelAndClosesDownTheSlot()
        {
            // The point of the shape: the slot is wider where it meets the tunnel and narrows to its
            // own width further in, so the lever is not cornered on the way past a divider end.
            EngineConfig cfg = MouthConfig(SlotMouthShape.Rounded);
            GateGeometry geo = cfg.BuildGeometry();
            int corridor = Math.Min(cfg.SlotHalfWidth, geo.ColumnFreeHalfWidth(Column.C2) - 100);

            // Just outside the plain corridor, the shaped mouth is still free near the tunnel...
            int shallow = Neutral(Composer(cfg), C2 + corridor + 300, Center - geo.ChannelHalfEnter - 200).ConstantX;
            Assert.Equal(0, shallow);

            // ...and walled once past the reach.
            int deep = Neutral(Composer(cfg), C2 + corridor + 300,
                               Center - geo.ChannelHalfEnter - cfg.MouthDepth - 200).ConstantX;
            Assert.True(deep < 0, "past the mouth's reach the slot wall must be back: " + deep);
        }

        [Fact]
        public void TheMouthOnlyEverRemovesForce()
        {
            // The safety property, and the reason no shape can run away: a mouth never pushes
            // outward. Everywhere, in every mode, the shaped force must point the same way as the
            // square one and never be larger.
            foreach (SlotMouthShape shape in new[] { SlotMouthShape.Rounded, SlotMouthShape.Angled })
            {
                EngineConfig shaped = MouthConfig(shape);
                EngineConfig square = MouthConfig(SlotMouthShape.Square);

                foreach (ShiftDir dir in new[] { ShiftDir.Fwd, ShiftDir.Back })
                {
                    for (int x = 0; x <= Max; x += 733)
                    {
                        foreach (int depth in new[] { 1500, 2400, 3000, 5000, 8000 })
                        {
                            int y = dir == ShiftDir.Fwd ? Center - depth : Center + depth;
                            int a = Neutral(Composer(shaped), x, y).ConstantX;
                            int b = Neutral(Composer(square), x, y).ConstantX;

                            Assert.True(Math.Abs(a) <= Math.Abs(b) + 1,
                                "shape added force at x=" + x + " depth " + depth + ": " + a + " vs " + b);
                            if (a != 0 && b != 0) Assert.Equal(Math.Sign(b), Math.Sign(a));
                        }
                    }
                }
            }
        }

        [Fact]
        public void TheAngledMouthOpensOnlyTowardTheNextGear()
        {
            // Coming out of 2 - C1's back slot - the next gear is 3, one column to the right, so
            // that is the flank that opens. The other flank must be exactly square.
            EngineConfig angled = MouthConfig(SlotMouthShape.Angled);
            EngineConfig square = MouthConfig(SlotMouthShape.Square);
            GateGeometry geo = angled.BuildGeometry();

            int corridor = Math.Min(angled.SlotHalfWidth, geo.ColumnFreeHalfWidth(Column.C1) - 100);
            int depth = geo.ChannelHalfEnter + 400;
            int y = Center + depth;               // back slot, so gear 2

            int probe = corridor + 300;
            Assert.True(Math.Abs(Neutral(Composer(angled), C1 + probe, y).ConstantX)
                        < Math.Abs(Neutral(Composer(square), C1 + probe, y).ConstantX),
                        "the flank toward gear 3 should be open");

            // The forward slot of the same column is gear 1, which has no next gear at all.
            int forward = Center - depth;
            Assert.Equal(Neutral(Composer(square), C1 + probe, forward).ConstantX,
                         Neutral(Composer(angled), C1 + probe, forward).ConstantX);
        }

        [Fact]
        public void TheAngledMouthFollowsTheGearMapUnderEveryMirror()
        {
            // The direction rule, checked against the gear map itself rather than against a
            // hand-written table: from an even gear the next gear is one gear-column up, from an odd
            // gear the previous is one down, and the bias must point at whichever DEVICE column that
            // gear-column actually is once mirroring has had its say.
            foreach (bool mirrorColumns in new[] { false, true })
            {
                foreach (bool mirrorSlots in new[] { false, true })
                {
                    var cfg = new EngineConfig { MirrorColumns = mirrorColumns, MirrorSlots = mirrorSlots };
                    GateGeometry geo = cfg.BuildGeometry();

                    foreach (Column c in new[] { Column.C1, Column.C2, Column.C3, Column.C4 })
                    {
                        foreach (ShiftDir dir in new[] { ShiftDir.Fwd, ShiftDir.Back })
                        {
                            int bias = geo.SequentialBias(c, dir);
                            if (bias == 0) continue;

                            int gear = geo.GearFor(c, dir);
                            int wanted = (gear % 2 == 0) ? gear + 1 : gear - 1;
                            Column target = (Column)((int)c + bias);

                            bool found = geo.GearFor(target, ShiftDir.Fwd) == wanted
                                      || geo.GearFor(target, ShiftDir.Back) == wanted;

                            Assert.True(found,
                                "gear " + gear + " at " + c + "/" + dir + " biased to " + target +
                                " but gear " + wanted + " is not there (mirrors " +
                                mirrorColumns + "/" + mirrorSlots + ")");
                        }
                    }
                }
            }
        }

        [Fact]
        public void NoMouthEverReachesTheLockoutBand()
        {
            // A column feature inside the gate's band would make the toll's size depend on the mouth
            // setting. Angled already refuses to open across the lockout gap; Rounded opens both
            // flanks, so it is the one that has to be clamped.
            foreach (SlotMouthShape shape in new[] { SlotMouthShape.Rounded, SlotMouthShape.Angled })
            {
                foreach (bool mirrorColumns in new[] { false, true })
                {
                    EngineConfig cfg = MouthConfig(shape);
                    cfg.MirrorColumns = mirrorColumns;
                    cfg.MouthDepth = 12000;
                    cfg.MouthOpenPct = 100;
                    cfg.LockoutForcePct = 100;

                    GateGeometry geo = cfg.BuildGeometry();
                    int from = geo.LockoutCentre - geo.LockoutHalfWidth;
                    int to = geo.LockoutCentre + geo.LockoutHalfWidth;

                    for (int x = from; x <= to; x += 150)
                    {
                        foreach (int depth in new[] { 1500, 2400, 4000, 8000 })
                        {
                            int shapedForce = Neutral(Composer(cfg), x, Center - depth).ConstantX;
                            EngineConfig square = MouthConfig(SlotMouthShape.Square);
                            square.MirrorColumns = mirrorColumns;
                            square.LockoutForcePct = 100;
                            int plainForce = Neutral(Composer(square), x, Center - depth).ConstantX;

                            Assert.Equal(plainForce, shapedForce);
                        }
                    }
                }
            }
        }

        [Fact]
        public void AMouthFlankIsNeverSteeperThanHalfTheWallFace()
        {
            // The whole stability argument for the feature. The flank is a cross-gradient - lateral
            // force changing with depth - and it must stay well inside the budget at any setting.
            foreach (SlotMouthShape shape in new[] { SlotMouthShape.Rounded, SlotMouthShape.Angled })
            {
                EngineConfig cfg = MouthConfig(shape);
                cfg.MouthDepth = 6000;      // long enough to reach past the transition band
                cfg.WallRamp = 2364;
                GateGeometry geo = cfg.BuildGeometry();

                EngineConfig square = MouthConfig(SlotMouthShape.Square);
                square.MouthDepth = cfg.MouthDepth;
                square.WallRamp = cfg.WallRamp;

                double wallFace = (cfg.ColumnPinForcePct * 100.0) / cfg.WallRamp;
                double budget = (EngineConfig.MouthSlopeMax * wallFace) + 0.1;   // +0.1 for rounding
                int corridor = Math.Min(cfg.SlotHalfWidth, geo.ColumnFreeHalfWidth(Column.C2) - 100);

                // Difference the shape against the plain gate, so what is measured is the flank alone
                // and not the guide's own plateau ramp riding along underneath it. Measured over a
                // window rather than count by count: the corridor edge is a whole number of counts,
                // and one count of it is worth a whole stiffness unit, so single-count differences
                // report the quantisation rather than the slope.
                const int window = 100;
                for (int offset = corridor; offset < corridor + 900; offset += 50)
                {
                    for (int depth = geo.ChannelHalfExit; depth <= geo.ChannelHalfEnter + 5000; depth += window)
                    {
                        int a = Neutral(Composer(cfg), C2 + offset, Center - depth).ConstantX
                              - Neutral(Composer(square), C2 + offset, Center - depth).ConstantX;
                        int b = Neutral(Composer(cfg), C2 + offset, Center - depth - window).ConstantX
                              - Neutral(Composer(square), C2 + offset, Center - depth - window).ConstantX;

                        double slope = Math.Abs(b - a) / (double)window;
                        Assert.True(slope <= budget,
                            shape + " flank slope " + slope + " at depth " + depth + ", offset " + offset);
                    }
                }
            }
        }

        [Fact]
        public void TheMouthReachesFarEnoughToBeFelt()
        {
            // Encodes the finding that killed the first design of this feature. The base answers in
            // 3-4 ms, in which a lever being shifted covers 1500 counts or more, so shaping confined
            // to a shorter stretch is over before one corrected force arrives. The default reach must
            // span several of those.
            Assert.True(new EngineConfig().MouthDepth >= 3 * 1500,
                "the default mouth reach is too short to survive the loop's latency");
        }

        // ---------------------------------------------------------------- the funnel

        [Fact]
        public void TheGuideStrengthensAsAGearIsTaken()
        {
            // Off-column entries used to be a dead end - the gate wall held, no gear arrived, and
            // nothing steered the hand onto a slot. The lateral guide rises with depth out of the
            // channel, like the tapered mouth of a real gate.
            EngineConfig cfg = FullGainConfig();
            cfg.ColumnDetentForcePct = 12;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            ForceComposer c = Composer(cfg);

            int offColumn = C2 + 2200;
            int sliding = Math.Abs(Neutral(c, offColumn, Center).ConstantX);
            int entering = Math.Abs(Neutral(c, offColumn, Center - cfg.ChannelHalfExit - 500).ConstantX);

            Assert.True(entering > sliding * 2,
                "the guide should take over on entry: " + entering + " vs " + sliding);
        }

        [Fact]
        public void BelowTheChannelTheLateralFieldHasNoDepthTermAtAll()
        {
            // The regression that put the guides leading to each gear back into oscillation. Below
            // the channel's exit a column can be latched, and there the lateral field must be a
            // function of x alone - a wall that grows under the hand as the lever is pushed in is a
            // cross-gradient where there had been exactly none, and it rang while the deep walls,
            // which had not changed, stayed calm.
            EngineConfig cfg = FullGainConfig();
            cfg.MouthShape = SlotMouthShape.Square;
            GateGeometry geo = cfg.BuildGeometry();

            for (int x = 0; x <= Max; x += 449)
            {
                int reference = Neutral(Composer(cfg), x, Center - geo.ChannelHalfExit).ConstantX;

                foreach (int extra in new[] { 100, 600, 1600, 2400, 8000, 24000 })
                {
                    Assert.Equal(reference,
                        Neutral(Composer(cfg), x, Center - geo.ChannelHalfExit - extra).ConstantX);
                }
            }
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

                // Skip the handover windows and their flanks. The field is zero there by design, so
                // that a change of guide column can never hand the lever a step - the fault this
                // whole shape exists to remove. OnlyTheLockoutGapLosesItsWallToTheHandoverWindow
                // is what stops those windows growing.
                if (geo.HandoverClearance(x) <= face) continue;

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
                    // Inside a handover window there is deliberately no force at all, so there is no
                    // direction to check. Everywhere else the wall must point home.
                    if (geo.HandoverClearance(target + offset) == 0) continue;

                    // A fresh composer each time, so no earlier position biases the choice.
                    int force = Neutral(Composer(cfg), target + offset, deep).ConstantX;
                    Assert.True(Math.Sign(force) == -Math.Sign(offset),
                        column + " at " + offset + " should be pushed back, got " + force);
                }
            }
        }

        [Fact]
        public void NoSingleCountOfDriftEverStepsTheLateralField()
        {
            // The fault this whole shape exists to remove, pinned on the axis it lived on. The guide
            // used to hold its plateau flat up to the boundary between two columns and then reverse,
            // so one count of drift could hand the lever twice the plateau - measured at 20000 DI,
            // the full scale, from a hundred counts. Every lateral gradient must now be inside the
            // wall's own stiffness, which is what the gate is built to tolerate.
            EngineConfig cfg = FullGainConfig();
            GateGeometry geo = cfg.BuildGeometry();

            // pin over the bite, plus a unit for rounding, plus the barriers' own smooth slope.
            int bound = (int)Math.Ceiling(
                GateGeometry.ForceMax * cfg.ColumnPinForcePct / 100.0 / (double)Math.Max(1, cfg.WallRamp)) + 3;

            foreach (int depth in new[] { 0, 1000, cfg.ChannelHalfEnter, 3000, cfg.ChannelHalfExit,
                                          6000, cfg.EngageDepth + 500, 12000 })
            {
                foreach (int direction in new[] { 1, -1 })
                {
                    ForceComposer c = Composer(cfg);
                    int from = direction > 0 ? 0 : Max;
                    int previous = Neutral(c, from, Center + depth).ConstantX;

                    for (int step = 1; step <= Max; step++)
                    {
                        int x = direction > 0 ? step : Max - step;
                        int force = Neutral(c, x, Center + depth).ConstantX;

                        Assert.True(Math.Abs(force - previous) <= bound,
                            "step of " + (force - previous) + " at x=" + x + " depth=" + depth
                            + " sweeping " + direction + ", bound " + bound);

                        previous = force;
                    }
                }
            }
        }

        [Fact]
        public void NoSingleCountOfDepthEverStepsTheLateralField()
        {
            // The same property on the other axis, because the natural way to write the fix - fade
            // the guide out at whichever boundary applies AT THIS DEPTH - moves the reversal here
            // instead. The two boundary rules are far apart at the lockout gap, so a window keyed on
            // depth leaves the other rule's flip window on a live part of the field, and one count of
            // fore/aft movement then reverses the guide. Measured at 2403 DI before the window was
            // widened to span both rules. Fore/aft wander is constant and involuntary, so this axis
            // matters at least as much as the other one.
            EngineConfig cfg = FullGainConfig();

            // Two terms, and only two. The plateau's own depth ramp, which is the one transition the
            // field is allowed to have; and one count of the wall's stiffness, because GuideFace is
            // an integer, so a face that grows with the plateau shifts where the lever sits on it by
            // up to a count. The second term is quantisation, not shape - it is worth a handful of DI
            // in practice, against the 2403 a depth-keyed relief window was measured to produce.
            int rampPerCount = (int)Math.Ceiling(
                GateGeometry.ForceMax * (cfg.ColumnPinForcePct - cfg.ColumnDetentForcePct) / 100.0
                / (double)Math.Max(1, cfg.ChannelHalfExit - cfg.ChannelHalfEnter));

            int quantisation = (int)Math.Ceiling(
                GateGeometry.ForceMax * cfg.ColumnPinForcePct / 100.0 / (double)Math.Max(1, cfg.WallRamp));

            int bound = rampPerCount + quantisation + 2;

            for (int x = 0; x <= Max; x += 37)   // prime-ish stride, so no landmark is skipped twice
            {
                ForceComposer c = Composer(cfg);
                int previous = Neutral(c, x, Center).ConstantX;

                for (int depth = 1; depth <= 12000; depth++)
                {
                    int force = Neutral(c, x, Center + depth).ConstantX;

                    Assert.True(Math.Abs(force - previous) <= bound,
                        "depth step of " + (force - previous) + " at x=" + x
                        + " depth=" + depth + ", bound " + bound);

                    previous = force;
                }
            }
        }

        [Fact]
        public void TheReliefWindowCannotInventHistoryDependence()
        {
            // Why the window is a MULTIPLIER on the finished force rather than a limit on the guide's
            // reach. A reach measured from the guide column is a property of WHICH column owns the
            // field, and the latched column and the position-picked one can differ - so the two
            // branches disagreed by the full pin force wherever both columns lay on the same side of
            // the lever, where a flat plateau had made them identical. Measured: 10000 DI at one
            // physical position, selected by whether the lever had once dipped into the tunnel.
            //
            // A shared scalar cannot do that. The field is F_unrelieved(column) x Relief(x), and
            // Relief reads only x, so any two histories that agreed before still agree.
            EngineConfig cfg = FullGainConfig();

            foreach (int depth in new[] { 0, 2000, cfg.ChannelHalfExit, 6000, cfg.EngageDepth + 500 })
            {
                for (int x = 0; x <= Max; x += 53)
                {
                    // Latched in a far column, versus the same position reached with that column
                    // picked by position. Both must see the identical lateral force.
                    foreach (Column latched in new[] { Column.C1, Column.C2, Column.C3, Column.C4 })
                    {
                        int viaLatch = Composer(cfg)
                            .Compose(GateState.Traveling, latched, ShiftDir.Fwd, x, Center + depth).ConstantX;

                        ForceComposer c = Composer(cfg);
                        c.Compose(GateState.Traveling, latched, ShiftDir.Fwd,
                                  cfg.BuildGeometry().ColumnTarget(latched), Center + depth);
                        int afterTravel = c.Compose(GateState.Traveling, latched, ShiftDir.Fwd, x, Center + depth).ConstantX;

                        Assert.Equal(viaLatch, afterTravel);
                    }
                }
            }
        }

        [Fact]
        public void OnlyTheLockoutGapLosesItsWallToTheHandoverWindow()
        {
            // The price of the relief window, pinned so it cannot quietly grow. At an ordinary gap
            // the two boundary rules agree, so the window is only the hysteresis either side and a
            // slot keeps its wall almost to the boundary. At the lockout gap the crest and the
            // midpoint are thousands of counts apart and the window spans both, which is why
            // dragging out of 5/6 toward 7/R at gear depth goes slack across the gate's doorway.
            EngineConfig cfg = FullGainConfig();
            GateGeometry geo = cfg.BuildGeometry();

            for (int gap = 0; gap < geo.ColumnCount - 1; gap++)
            {
                int crest = geo.BarrierCentre(gap);
                int mid = (geo.ColumnTarget((Column)gap) + geo.ColumnTarget((Column)(gap + 1))) / 2;

                // Both rules' flip positions are covered, whichever rule is in force at this depth.
                Assert.Equal(0, geo.HandoverClearance(crest));
                Assert.Equal(0, geo.HandoverClearance(mid));

                int expected = Math.Abs(crest - mid) + (2 * cfg.DetentHysteresis) + 1;
                int width = 0;
                for (int x = Math.Min(crest, mid) - (2 * cfg.DetentHysteresis);
                         x <= Math.Max(crest, mid) + (2 * cfg.DetentHysteresis); x++)
                {
                    if (geo.HandoverClearance(x) == 0) width++;
                }

                Assert.Equal(expected, width);

                if (gap != geo.LockoutGapIndex)
                {
                    // An ordinary gap's two rules agree, so its dead strip is only the hysteresis.
                    Assert.Equal(crest, mid);
                    Assert.Equal((2 * cfg.DetentHysteresis) + 1, width);
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

            // And across the slot's own corridor it does nothing at all, so there is no centre
            // line for the stick to hunt around exactly where the hand is trying to hold still.
            // The flat bottom is the slot's corridor now, in every state - it used to be the
            // column's selection band here and the corridor once a gear was latched, and those
            // differing by a hundred counts was one seam among several.
            GateGeometry geo = cfg.BuildGeometry();
            int free = Math.Min(cfg.SlotHalfWidth, geo.ColumnFreeHalfWidth(Column.C2) - 100);
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
        public void AHandsTremorNeverTripsTheAbsorber()
        {
            // The relay failure, pinned. A hand leaning on a wall reverses direction at tremor
            // scale several times a second - measured under 3700 counts/s on a real lean - and
            // with the deadband at sensor-noise level every reversal fired a fresh cut, each
            // cut stepped the force by the yield fraction, and each step kicked the lever into
            // a bigger reversal: 26 Hz chatter held in a slot, a 12 Hz rebound off the lockout,
            // both on real traces. Inside the deadband the sign of the velocity is tremor, not
            // intent, so it must never select between two different forces.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int atRest = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                   between, Center + 3500, 0, 0, 1.0).ConstantY;
            Assert.NotEqual(0, atRest);

            for (int i = 0; i < 200; i++)
            {
                int vy = (i % 2 == 0) ? 3700 : -3700;
                int force = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                      between, Center + 3500, 0, vy, 1.0).ConstantY;
                Assert.Equal(atRest, force);
            }
        }

        [Fact]
        public void TheLockoutHoldsWholeAgainstALeaningHand()
        {
            // Where the rebound was felt on hardware: leaning into the toll, every tremor
            // reversal used to flip the force between whole and floor - 8000 down to 3200 and
            // back, at 12 Hz on the trace - and the flip itself pumped the lever back out of
            // the band in 20000-count swings. Held against a leaning hand, the toll must be
            // one number.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int inGate = GateCentre(cfg);

            int atRest = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                   inGate, Center, 0, 0, 1.0).ConstantX;
            Assert.NotEqual(0, atRest);

            for (int i = 0; i < 200; i++)
            {
                int vx = (i % 2 == 0) ? 3700 : -3700;
                int force = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                      inGate, Center, vx, 0, 1.0).ConstantX;
                Assert.Equal(atRest, force);
            }
        }

        [Fact]
        public void AnEstimateDipBelowTheDeadbandKeepsTheHeldCut()
        {
            // The other half of the deadband's contract, and what keeps the raised deadband
            // from reopening the strobe: the speed estimate ripples under the device's ~500 Hz
            // report quantisation, so a launch's estimate can dip below the deadband for a
            // tick. Restoring the wall whole on that tick would flip the force across the yield
            // span at report rate - the gear-teeth texture. Inside the deadband the HELD scale
            // applies, climbing only at the recovery slew; sustained stillness is a lean, so it
            // does recover completely - slowly.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int full = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, Center + 3500, 0, 0, 1.0).ConstantY;

            // Launch: cut to the floor.
            c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                      between, Center + 3500, 0, -25000, 1.0);

            // One sub-deadband tick: the cut holds, give or take one recovery step.
            int dipped = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                   between, Center + 3500, 0, -6000, 1.0).ConstantY;
            Assert.True(Math.Abs(dipped) <= Math.Abs(full) * 0.62,
                "a sub-deadband tick restored the wall: " + dipped + " of " + full);

            // Sustained stillness firms the wall back up at the slew rate, no faster.
            int previous = dipped;
            int step = (int)(Math.Abs(full) / (double)cfg.YieldRecoveryMs) + 50;
            for (int i = 0; i < cfg.YieldRecoveryMs + 5; i++)
            {
                int force = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                      between, Center + 3500, 0, 0, 1.0).ConstantY;
                Assert.True(Math.Abs(force - previous) <= step,
                    "recovery outran the slew: " + previous + " -> " + force);
                previous = force;
            }
            Assert.Equal(full, previous);
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

        // ---------------------------------------------------------------- the rail gate

        /// <summary>Corridors closed: the native shifter-mode topology as a config, not a mode.</summary>
        private static EngineConfig RailConfig()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.SlotHalfWidth = 0;
            cfg.ChannelFreeDepth = 0;
            cfg.ColumnPinForcePct = 55;
            cfg.DampingPct = 0;
            return cfg;
        }

        [Fact]
        public void ClosedCorridorsLeaveNoFreeSpaceAnywhere()
        {
            // The rail gate's defining property: at every point off a guide line, at least one
            // axis is being pushed back toward it. No 2D float, no face to accelerate across.
            ForceComposer c = Composer(RailConfig());

            // In the tunnel between columns, even one count of fore/aft wander meets centring.
            Assert.NotEqual(0, Neutral(c, (C2 + C3) / 2, Center - 400).ConstantY);
            Assert.NotEqual(0, Neutral(c, (C2 + C3) / 2, Center + 400).ConstantY);

            // In a slot at gear depth, even a small lateral displacement meets the rail.
            ForceComposer c2 = Composer(RailConfig());
            int railed = c2.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                    C2 + 400, Center - 9000, 0, 0).ConstantX;
            Assert.True(railed < 0, "the rail must push back toward the column line: " + railed);
        }

        [Fact]
        public void TheRailItselfIsQuietGround()
        {
            // On the guide line the rail exerts nothing - the lever rests at equilibrium with
            // zero force, which is what makes a rail calmer than a wall being leant on.
            ForceComposer c = Composer(RailConfig());

            Assert.Equal(0, Neutral(c, C2, Center).ConstantY);
            Assert.Equal(0, Neutral(c, C2, Center).ConstantX);
        }

        [Fact]
        public void ARailedTunnelStillOpensOverAColumn()
        {
            // Closing the free depth must not close the slot mouths: the centring stays light
            // over a column so a gear can be taken, and only hardens between them.
            ForceComposer c = Composer(RailConfig());

            int onColumn = Math.Abs(Neutral(c, C2, Center - 2000).ConstantY);
            int between = Math.Abs(Neutral(c, (C2 + C3) / 2, Center - 2000).ConstantY);

            Assert.True(onColumn < between / 4,
                "the mouth closed with the corridor: " + onColumn + " vs " + between);
        }

        [Fact]
        public void TheDefaultFreeDepthKeepsTheClassicTunnel()
        {
            // The dial ships equal to the channel enter band, so an existing gate is unchanged:
            // inside the tunnel there is still no fore/aft force at all.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            Assert.Equal(0, Neutral(c, (C2 + C3) / 2, Center - 2000).ConstantY);
        }

        [Fact]
        public void FreeDepthCannotExceedTheStateBand()
        {
            // A force deadband wider than the enter band would mean walls the state machine
            // believes exist and the hand never meets; the composer clamps it.
            EngineConfig cfg = FullGainConfig();
            cfg.ChannelFreeDepth = 99999;
            ForceComposer c = Composer(cfg);

            int past = cfg.ChannelHalfEnter + cfg.WallRamp + 200;
            Assert.NotEqual(0, Neutral(c, (C2 + C3) / 2, Center - past).ConstantY);
        }

        [Fact]
        public void AnAliasedSpeedEstimateCannotGrindTheWall()
        {
            // The failure this pins: distinct positions arrive at ~500 Hz under write
            // contention, so the speed estimate used to swing 2:1 on alternate ticks. An
            // absorber that follows the estimate both ways sweeps its scale across the whole
            // blend range at that rate - measured as a 25-50% force ripple, felt as the lever
            // grinding against a running gear. With slewed recovery the ripple is bounded by
            // the recovery rate instead of the blend span.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int full = Math.Abs(c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                          between, Center + 3500, 0, 0, 1.0).ConstantY);

            // Assisting speeds alternating 3:1, one tick apart, straddling the deadband the
            // way a real launch's rippling estimate does. The dip lands INSIDE the deadband -
            // the hardest case, because a deadband that restored the wall whole there would
            // flip the force across the entire yield span at the report rate.
            int previous = 0;
            int worstStep = 0;
            for (int i = 0; i < 40; i++)
            {
                int vy = (i % 2 == 0) ? -24000 : -8000;
                int force = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                      between, Center + 3500, 0, vy, 1.0).ConstantY;

                if (i > 0 && Math.Abs(force - previous) > worstStep)
                    worstStep = Math.Abs(force - previous);
                previous = force;

                // The cut also has to hold: the wall is assisting throughout, so the force
                // must stay near the floor rather than climbing back between dips.
                if (i > 0) Assert.True(Math.Abs(force) <= (int)(full * 0.62),
                    "absorption let go between estimate dips: " + force);
            }

            // Recovery rate bound: full-scale/YieldRecoveryMs per tick, plus rounding. The
            // unslewed absorber stepped 3206 here.
            int bound = full / cfg.YieldRecoveryMs + 50;
            Assert.True(worstStep <= bound,
                "force ripple " + worstStep + " exceeds the recovery slew " + bound);
        }

        [Fact]
        public void TheAbsorberCutsInstantlyAndRecoversSlowly()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int full = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, Center + 3500, 0, 0, 1.0).ConstantY;

            // The launch is caught whole on its first tick - absorption is never slewed in.
            int cut = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                between, Center + 3500, 0, -25000, 1.0).ConstantY;
            Assert.Equal((int)Math.Round(full * 0.55), cut);

            // The estimate dips below the deadband. Point-wise that reads as "leaning";
            // the slew keeps the cut and climbs back over YieldRecoveryMs instead.
            int afterDip = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                     between, Center + 3500, 0, -6000, 1.0).ConstantY;
            Assert.True(Math.Abs(afterDip) < Math.Abs(full) * 0.70,
                "one mild tick restored the wall: " + afterDip + " of " + full);

            int settled = afterDip;
            for (int i = 0; i < 15; i++)
            {
                settled = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                    between, Center + 3500, 0, -15000, 1.0).ConstantY;
            }

            // Sustained mild assist converges on that speed's own scale - recovery is slewed,
            // not denied.
            double t = (15000.0 - cfg.YieldVelocityDeadband) / cfg.YieldVelocityBlend;
            int expected = (int)Math.Round(full * (1.0 - 0.45 * t));
            Assert.Equal(expected, settled);

            // And the moment the wall is resisting again it is whole, no matter how deep the
            // cut was a tick ago.
            c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                      between, Center + 3500, 0, -25000, 1.0);
            int resisting = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                      between, Center + 3500, 0, 25000, 1.0).ConstantY;
            Assert.Equal(full, resisting);
        }

        [Fact]
        public void PointwiseCompositionKeepsTheUnslewedSemantics()
        {
            // A dt of zero bypasses the absorber's memory, the same convention as the time
            // shaping. Every stateless property test in this file leans on that.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);
            int between = (C2 + C3) / 2;

            int full = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, Center + 3500, 0, 0).ConstantY;
            int deep = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, Center + 3500, 0, -25000).ConstantY;
            int mild = c.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                 between, Center + 3500, 0, -15000).ConstantY;

            double t = (15000.0 - cfg.YieldVelocityDeadband) / cfg.YieldVelocityBlend;
            Assert.Equal((int)Math.Round(full * 0.55), deep);
            Assert.Equal((int)Math.Round(full * (1.0 - 0.45 * t)), mild);
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
            // full strength before the neighbouring column's territory begins. "Begins" is now the
            // edge of the handover window rather than the midpoint itself, because the field is
            // faded to zero across the window on purpose - see GateGeometry.HandoverClearance.
            EngineConfig cfg = FullGainConfig();
            cfg.WallRamp = 40000;   // absurd on purpose
            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();

            int atMidpoint = C2 + (geo.ColumnSpacing / 2);

            int peak = 0;
            for (int x = C2; x <= atMidpoint; x++)
            {
                int force = Math.Abs(c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, 2000).ConstantX);
                if (force > peak) peak = force;
            }

            Assert.Equal(9000, peak);

            // And zero at the boundary itself, which is the whole point of the window.
            Assert.Equal(0, c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, atMidpoint, 2000).ConstantX);
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

            // Sampled clear of the fore/aft wall's own face, which starts at ChannelHalfEnter.
            int deep = Center - (cfg.ChannelHalfEnter + cfg.WallRamp + 100);
            int wall = Math.Abs(Neutral(c, (C2 + C3) / 2, deep).ConstantY);
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

            Assert.Equal(1, plain.BuildGeometry().GearFor(Column.C1, ShiftDir.Fwd));
            Assert.Equal(7, mirrored.BuildGeometry().GearFor(Column.C1, ShiftDir.Fwd));
            Assert.Equal(8, mirrored.BuildGeometry().GearFor(Column.C1, ShiftDir.Back));
            Assert.Equal(2, new EngineConfig { MirrorSlots = true }.BuildGeometry()
                                .GearFor(Column.C1, ShiftDir.Fwd));
        }

        // ---------------------------------------------------------------- patterns

        [Fact]
        public void EachPatternMapsItsGearsAndNothingElse()
        {
            GateGeometry h7r = new EngineConfig { Pattern = GatePattern.H7R }.BuildGeometry();
            GateGeometry h6r = new EngineConfig { Pattern = GatePattern.H6R }.BuildGeometry();
            GateGeometry h5r = new EngineConfig { Pattern = GatePattern.H5R }.BuildGeometry();

            // 7+R: four columns, every slot filled, reverse is 8.
            Assert.Equal(4, h7r.ColumnCount);
            Assert.Equal(8, h7r.GearFor(Column.C4, ShiftDir.Back));
            Assert.Equal("R", h7r.LabelFor(8));

            // 6+R: the slot that would hold 7 holds nothing. Reverse stays on button 8 - NOT
            // compacted down to fill the hole - so a game bound for any other pattern still
            // reads this pattern's R as reverse.
            Assert.Equal(4, h6r.ColumnCount);
            Assert.Equal(0, h6r.GearFor(Column.C4, ShiftDir.Fwd));
            Assert.False(h6r.SlotExists(Column.C4, ShiftDir.Fwd));
            Assert.Equal(8, h6r.GearFor(Column.C4, ShiftDir.Back));
            Assert.Equal("R", h6r.LabelFor(8));
            Assert.Equal("6", h6r.LabelFor(6));

            // 5+R: three columns spread over the full axis, no lockout, and reverse is still
            // button 8. It used to be 6, which a game carrying 7+R bindings read as sixth
            // gear - reverse engaged at speed, reported from the driver's seat. The middle
            // column rounds to 32768 because full travel is an odd count.
            Assert.Equal(3, h5r.ColumnCount);
            Assert.InRange(h5r.ColumnTarget(Column.C2), GateGeometry.AxisCenter, GateGeometry.AxisCenter + 1);
            Assert.Equal(GateGeometry.AxisMax, h5r.ColumnTarget(Column.C3));
            Assert.Equal(5, h5r.GearFor(Column.C3, ShiftDir.Fwd));
            Assert.Equal(8, h5r.GearFor(Column.C3, ShiftDir.Back));
            Assert.Equal("R", h5r.LabelFor(8));
            Assert.False(h5r.HasLockout);
        }

        [Fact]
        public void MirroringMovesTheMissingSlotWithTheGears()
        {
            // The hole lives in the gear map, so mirroring the columns puts the missing 7
            // where the map says it should be, not wherever the device's corner happens to be.
            GateGeometry mirrored = new EngineConfig
            {
                Pattern = GatePattern.H6R,
                MirrorColumns = true
            }.BuildGeometry();

            Assert.False(mirrored.SlotExists(Column.C1, ShiftDir.Fwd));
            Assert.Equal(8, mirrored.GearFor(Column.C1, ShiftDir.Back));
            Assert.True(mirrored.SlotExists(Column.C4, ShiftDir.Fwd));
        }

        [Fact]
        public void AMissingSlotsWallNeverOpens()
        {
            // The whole rendering of 6+R's missing 7: the fore/aft wall over the last column
            // stays closed pushing forward, exactly as strong as squarely between columns,
            // while pulling back into R opens as usual.
            EngineConfig cfg = FullGainConfig();
            cfg.Pattern = GatePattern.H6R;
            ForceComposer c = Composer(cfg);

            int closed = Math.Abs(Neutral(c, C4, Center - 3200).ConstantY);
            int between = Math.Abs(Neutral(c, (C2 + C3) / 2, Center - 3200).ConstantY);
            int open = Math.Abs(Neutral(c, C4, Center + 3200).ConstantY);

            Assert.Equal(between, closed);
            Assert.True(open < closed / 4,
                "the R slot should still open: " + open + " vs " + closed);
        }

        [Fact]
        public void TheFiveGearGateHasNoLockoutAnywhere()
        {
            // No lockout means no displaced crest: every barrier sits at its gap's midpoint,
            // and no position on the axis feels the one-way toll.
            EngineConfig cfg = FullGainConfig();
            cfg.Pattern = GatePattern.H5R;
            GateGeometry geo = cfg.BuildGeometry();

            for (int gap = 0; gap < geo.ColumnCount - 1; gap++)
            {
                int mid = (geo.ColumnTarget((Column)gap) + geo.ColumnTarget((Column)(gap + 1))) / 2;
                Assert.Equal(mid, geo.BarrierCentre(gap));
            }

            Assert.False(geo.InLockoutGate(geo.LockoutCentre));
        }

        [Fact]
        public void ThePatternsShareTheStateMachineHonestly()
        {
            // Pushing into 6+R's missing 7 selects nothing however deep the lever goes, and
            // the R slot still engages: the map, the walls and the state machine agree.
            EngineConfig cfg = FullGainConfig();
            cfg.Pattern = GatePattern.H6R;
            GateGeometry geo = cfg.BuildGeometry();
            var sm = new GateStateMachine(geo, cfg.MinEngageTicks);

            sm.Update(C4, Center);
            sm.Update(C4, 2000);
            sm.Update(C4, 2000);
            sm.Update(C4, 2000);
            Assert.Equal(0, sm.CurrentGear);
            Assert.Equal(GateState.Neutral, sm.State);

            sm.Update(C4, Center);
            sm.Update(C4, GateGeometry.AxisMax - 2000);
            sm.Update(C4, GateGeometry.AxisMax - 2000);
            sm.Update(C4, GateGeometry.AxisMax - 2000);
            Assert.Equal(8, sm.CurrentGear);
            Assert.Equal("R", geo.LabelFor(sm.CurrentGear));
        }

        // ---------------------------------------------------------------- telemetry vibration

        [Fact]
        public void VibrationRidesThroughUntouchedByYieldAndAttack()
        {
            // A carrier is keyed on time, not position, so it cannot form the loop the yield
            // and the attack stabilise - and shaping it would just filter the texture away.
            // Two identical composers, one fed vibration: the outputs must differ by exactly
            // the carrier, mid-attack and mid-yield alike.
            EngineConfig cfg = FullGainConfig();
            cfg.WallAttackMs = 20;
            cfg.DampingPct = 0;

            ForceComposer plain = Composer(cfg);
            ForceComposer buzzing = Composer(cfg);

            int between = (C2 + C3) / 2;
            int y = Center - 4000;

            // First against the wall (resisting velocity, attack still rising), then bouncing
            // off it (assisting velocity, yield actively cutting).
            foreach (int vy in new[] { -20000, 20000 })
            {
                for (int i = 0; i < 12; i++)
                {
                    ForceFrame a = plain.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                                 between, y, 0, vy, 1.0);
                    ForceFrame b = buzzing.Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                                   between, y, 0, vy, 1.0, 1500);
                    Assert.Equal(a.ConstantY + 1500, b.ConstantY);
                }
            }
        }

        [Fact]
        public void FreeStickSilencesVibrationToo()
        {
            // Free stick is the escape hatch and the diagnostic baseline: with it on, nothing
            // the plugin renders may reach the hand - the game effects included.
            EngineConfig cfg = FullGainConfig();
            cfg.FreeStick = true;

            ForceFrame f = Composer(cfg).Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                                 Center, Center, 0, 0, 1.0, 3000);
            Assert.Equal(0, f.ConstantX);
            Assert.Equal(0, f.ConstantY);
        }

        [Fact]
        public void TheFinalClampStillRulesWithVibration()
        {
            // A full-strength wall plus a hostile carrier must still never exceed full scale.
            EngineConfig cfg = FullGainConfig();
            cfg.ChannelWallForcePct = 100;
            cfg.DampingPct = 0;

            int between = (C2 + C3) / 2;
            ForceFrame f = Composer(cfg).Compose(GateState.Neutral, Column.None, ShiftDir.None,
                                                 between, Center - 4000, 0, 0, 0, 9000);

            Assert.Equal(GateGeometry.ForceMax, Math.Abs(f.ConstantY));
        }

        [Fact]
        public void ABalkedSlotOnlyEverPushesTheLeverOut()
        {
            // The grind's rejection, rendered: with the detent muted there is no crossover, no
            // snick and no hold - entry resistance rises and then simply stays, however deep
            // the lever is held, the way a blocking synchro ring balks it. For a forward slot
            // that means the fore/aft force never goes negative.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            ForceComposer c = Composer(cfg);

            for (int y = Center - cfg.ChannelHalfExit; y >= 0; y -= 400)
            {
                ForceFrame f = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                         C2, y, 0, 0, 0, 0, true);
                Assert.True(f.ConstantY >= 0,
                    "balked detent pulled inward at y=" + y + ": " + f.ConstantY);
            }

            // The same depth unmuted seats the gear: the pull points into the slot.
            ForceFrame seated = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                          C2, 500, 0, 0, 0, 0, false);
            Assert.True(seated.ConstantY < 0);
        }

        [Fact]
        public void TheBalkWallStandsBehindTheResistance()
        {
            // A rejected shift meets a border, not a lean: the balk wall stacks on the entry
            // resistance and stays. At zero the old resistance-only feel remains.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;

            ForceFrame balked = Composer(cfg).Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                                      C2, 500, 0, 0, 0, 0, true);
            Assert.Equal(9200, balked.ConstantY);   // 22% resistance + 70% wall

            cfg.GrindWallPct = 0;
            ForceFrame bare = Composer(cfg).Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                                    C2, 500, 0, 0, 0, 0, true);
            Assert.Equal(2200, bare.ConstantY);
        }

        [Fact]
        public void TheBalkWallTakesTheAttackAndTheSnickStillArrivesWhole()
        {
            // While balked there is no snick to protect - the detent has become a wall being
            // leaned on, so it winds up over the attack like every wall. The moment the clutch
            // unmutes it, the exemption returns and the pull lands in one piece.
            EngineConfig cfg = FullGainConfig();
            cfg.DampingPct = 0;
            cfg.WallAttackMs = 20;
            ForceComposer c = Composer(cfg);

            ForceFrame first = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                         C2, 500, 0, 0, 1.0, 0, true);
            ForceFrame second = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                          C2, 500, 0, 0, 1.0, 0, true);
            Assert.Equal(500, first.ConstantY);     // one attack step of the wall, not the wall
            Assert.Equal(1000, second.ConstantY);

            ForceFrame seated = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd,
                                          C2, 500, 0, 0, 1.0, 0, false);
            Assert.Equal(-5500, seated.ConstantY);  // the hold, whole, the same millisecond
        }

        // ---------------------------------------------------------------- the home spring

        /// <summary>Home spring alone: every other lateral force silenced.</summary>
        private static EngineConfig HomeSpringOnly()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.HomeSpringPct = 30;
            cfg.ColumnDetentForcePct = 0;
            cfg.BarrierForcePct = 0;
            cfg.LockoutForcePct = 0;
            cfg.DampingPct = 0;
            return cfg;
        }

        [Fact]
        public void TheHomeSpringPullsTowardTheThreeFourColumn()
        {
            // A real H lever rests at the 3/4 gate. In the channel the spring is a flat pull
            // toward that column from either side, and dead across the column's own width -
            // the equilibrium is a region, not a point, so there is nothing to hunt around.
            ForceComposer c = Composer(HomeSpringOnly());

            Assert.Equal(3000, Neutral(c, C1, Center).ConstantX);
            Assert.Equal(-3000, Neutral(c, C3, Center).ConstantX);
            Assert.Equal(0, Neutral(c, C2, Center).ConstantX);

            // Flat beyond the face: the far end of the gate feels the same pull as halfway,
            // because a gradient that kept growing would be a spring this base cannot render
            // and an oscillator this project will not.
            Assert.Equal(Neutral(c, C3, Center).ConstantX, Neutral(c, Max, Center).ConstantX);
        }

        [Fact]
        public void TheHomeSpringFadesOutWithDepthLikeTheHumps()
        {
            // A held gear must feel no sideways pull toward home - below the channel the slot
            // walls own the lateral axis alone. Pin force zeroed too, so anything left at
            // depth could only be the spring failing to fade.
            EngineConfig cfg = HomeSpringOnly();
            cfg.ColumnPinForcePct = 0;
            ForceComposer c = Composer(cfg);

            Assert.Equal(3000, Neutral(c, C1, Center).ConstantX);

            ForceFrame deep = c.Compose(GateState.Traveling, Column.C1, ShiftDir.Fwd,
                                        C1, 2000, 0, 0, 0);
            Assert.Equal(0, deep.ConstantX);
        }

        [Fact]
        public void MirroringMovesTheHomeColumnWithTheGears()
        {
            // Home is gear-column 1 - the one holding 3 and 4 - not a device position, so the
            // mirror flags relocate it exactly as they relocate the gears themselves.
            Assert.Equal(Column.C2, new EngineConfig().BuildGeometry().HomeColumn);
            Assert.Equal(Column.C3, new EngineConfig { MirrorColumns = true }.BuildGeometry().HomeColumn);

            // A three-column gate is symmetric: home is the middle either way.
            Assert.Equal(Column.C2, new EngineConfig { Pattern = GatePattern.H5R }.BuildGeometry().HomeColumn);
            Assert.Equal(Column.C2, new EngineConfig { Pattern = GatePattern.H5R, MirrorColumns = true }
                                        .BuildGeometry().HomeColumn);

            EngineConfig mirrored = HomeSpringOnly();
            mirrored.MirrorColumns = true;
            ForceComposer c = Composer(mirrored);

            Assert.Equal(3000, Neutral(c, C2, Center).ConstantX);
            Assert.Equal(0, Neutral(c, C3, Center).ConstantX);
        }

        [Fact]
        public void TheHomeSpringNeverStepsTheField()
        {
            // The whole lateral field, every force on, spring at full slider, one count at a
            // time across the entire channel: no adjacent pair may differ by more than the
            // steepest sanctioned face. A step here is the mouth-oscillation bug reborn.
            EngineConfig cfg = FullGainConfig();
            cfg.HomeSpringPct = 60;
            ForceComposer c = Composer(cfg);

            int previous = Neutral(c, 0, Center).ConstantX;
            for (int x = 1; x <= Max; x++)
            {
                int fx = Neutral(c, x, Center).ConstantX;
                Assert.True(Math.Abs(fx - previous) <= 120,
                    "lateral field stepped " + Math.Abs(fx - previous) + " DI at x=" + x);
                previous = fx;
            }
        }
    }
}
