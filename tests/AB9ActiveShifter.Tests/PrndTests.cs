using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The automatic's selector: four fixed positions in one lane, a button held at each.
    ///
    /// Two properties carry most of the weight. The lever must always be in exactly one position -
    /// there is no neutral here for it to fall into and no "nowhere" the game could read as a
    /// missing selection - and the lane's force must be continuous everywhere, because a detent
    /// field picks the nearest position and any nearest-anything field flips at the midpoint. On
    /// the lateral axis that flip was a step of twice the plateau and cost a whole relief window;
    /// here it is free only because the force is already nothing where the flip happens.
    /// </summary>
    public class PrndTests
    {
        private const int Center = GateGeometry.AxisCenter;

        private static EngineConfig FullGainConfig()
        {
            return new EngineConfig
            {
                Pattern = GatePattern.Prnd,
                OverallGainPct = 100,
                PolarityConfirmed = true
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        // ---------------------------------------------------------------- the lane

        [Fact]
        public void TheLaneHasFourEvenlySpacedPositionsEndingAtItsHalfLength()
        {
            var lane = new PrndLane(12000, 400, false);

            Assert.Equal(4, PrndLane.PositionCount);
            Assert.Equal(8000, lane.Spacing);

            Assert.Equal(Center - 12000, lane.PositionY(0));
            Assert.Equal(Center - 4000, lane.PositionY(1));
            Assert.Equal(Center + 4000, lane.PositionY(2));
            Assert.Equal(Center + 12000, lane.PositionY(3));
        }

        [Fact]
        public void ParkIsAtTheFarEndAndDriveNearest()
        {
            // The console order, and the one a hand reaches for: P furthest away, D pulled back
            // toward the player. Fwd is low y throughout this project, so index 0 is P.
            var lane = new PrndLane(12000, 400, false);

            Assert.Equal("P", lane.LabelFor(0));
            Assert.Equal("R", lane.LabelFor(1));
            Assert.Equal("N", lane.LabelFor(2));
            Assert.Equal("D", lane.LabelFor(3));
        }

        [Fact]
        public void MirroringTurnsTheLaneRoundAndTheButtonsFollowTheLabels()
        {
            // A layout preference must never cost a rebind - the same rule that pins reverse to
            // button 8 in every H pattern however many gears it has.
            var plain = new PrndLane(12000, 400, false);
            var mirrored = new PrndLane(12000, 400, true);

            Assert.Equal("D", mirrored.LabelFor(0));
            Assert.Equal("P", mirrored.LabelFor(3));

            for (int i = 0; i < PrndLane.PositionCount; i++)
            {
                Assert.Equal(plain.ButtonFor(i), mirrored.ButtonFor(PrndLane.PositionCount - 1 - i));
            }

            // R is button 12 whichever end of the lane it is at.
            Assert.Equal(12, plain.ButtonFor(1));
            Assert.Equal(12, mirrored.ButtonFor(2));
        }

        [Fact]
        public void EveryPositionSitsAboveTheGearsAndTheSequentialPulses()
        {
            // Gears take 1-8, the sequential up/down take 9-10. A binding a game already carries
            // must never be able to mean two things - the trap that moved the sequential pulses
            // off buttons 1 and 2, where an upshift read as "engage 1st" at speed.
            var lane = new PrndLane(12000, 400, false);

            for (int i = 0; i < PrndLane.PositionCount; i++)
            {
                Assert.True(lane.ButtonFor(i) > 10, "Position " + i + " collides with a gear or a pulse.");
                Assert.True(lane.ButtonFor(i) <= 14);
            }
        }

        // ---------------------------------------------------------------- the state machine

        [Fact]
        public void TheLeverIsAlwaysInExactlyOnePosition()
        {
            // Including past both ends of the lane, where there is no position at all - the
            // outermost one keeps it. A selector that could report "nowhere" would show a game a
            // moment with no gear selected every time the lever was pushed against its stop.
            var machine = new PrndStateMachine(new PrndLane(12000, 400, false));

            for (int y = 0; y <= GateGeometry.AxisMax; y += 97)
            {
                StateTransition t = machine.Update(y);

                Assert.InRange(t.Gear, PrndLane.FirstButton, PrndLane.FirstButton + PrndLane.PositionCount - 1);
                Assert.Equal(GateState.Engaged, t.State);
            }
        }

        [Fact]
        public void RestingOnACrestCannotFlutterTheButton()
        {
            // The hysteresis is the whole chatter story here - there is deliberately no engage
            // debounce, because a tick count does nothing for a hand that rests on a boundary
            // indefinitely, which is the case that actually happens.
            var lane = new PrndLane(12000, 400, false);
            var machine = new PrndStateMachine(lane);

            machine.Update(lane.PositionY(1));
            int held = machine.CurrentButton;

            int crest = lane.CrestY(1);
            for (int wobble = -300; wobble <= 300; wobble += 7)
            {
                machine.Update(crest + wobble);
                Assert.Equal(held, machine.CurrentButton);
            }

            // Past the bias, it does hand over - the hysteresis is a delay, not a lock.
            machine.Update(crest + 500);
            Assert.NotEqual(held, machine.CurrentButton);
        }

        [Fact]
        public void AColdStartAdoptsWhereverTheLeverAlreadyIsWithoutReportingAChange()
        {
            // Startup, and a geometry change under the running loop. The engine pushes the
            // adopted button to vJoy itself rather than inferring it from a transition, so this
            // must not manufacture one.
            var lane = new PrndLane(12000, 400, false);
            var machine = new PrndStateMachine(lane);

            machine.Resync(lane.PositionY(3));
            Assert.Equal(lane.ButtonFor(3), machine.CurrentButton);

            StateTransition first = machine.Update(lane.PositionY(3));
            Assert.False(first.GearChanged);
        }

        // ---------------------------------------------------------------- the force

        [Fact]
        public void TheDetentIsNothingAtEveryPositionAndNothingAtEveryCrest()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();

            for (int i = 0; i < PrndLane.PositionCount; i++)
            {
                Assert.Equal(0, c.PrndLaneForce(lane.PositionY(i)));
            }

            for (int gap = 0; gap < PrndLane.PositionCount - 1; gap++)
            {
                Assert.Equal(0, c.PrndLaneForce(lane.CrestY(gap)));
            }
        }

        [Fact]
        public void TheNotchIsFreeSoASelectedPositionIsARegion()
        {
            // The fore/aft twin of the slot corridor. A force pulling toward a single point is an
            // oscillator; a free region has no gradient at rest for the loop's delay to pump.
            EngineConfig cfg = FullGainConfig();
            cfg.PrndNotchHalfWidth = 800;

            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();

            for (int i = 0; i < PrndLane.PositionCount; i++)
            {
                // P and D sit at the ends of the lane, so their notch only reaches inward -
                // outside them the lever has left the lane and the end-stop owns it.
                int from = i == 0 ? 0 : -800;
                int to = i == PrndLane.PositionCount - 1 ? 0 : 800;

                for (int offset = from; offset <= to; offset += 23)
                {
                    Assert.Equal(0, c.PrndLaneForce(lane.PositionY(i) + offset));
                }
            }

            // And the wall does start immediately outside, rather than after a second notch.
            Assert.NotEqual(0, c.PrndLaneForce(lane.PositionY(0) - cfg.WallRamp));
            Assert.NotEqual(0, c.PrndLaneForce(lane.PositionY(PrndLane.PositionCount - 1) + cfg.WallRamp));
        }

        [Fact]
        public void TheEndStopOnlyEverPushesBackDownTheLane()
        {
            // A wall, never a pocket: there is nothing past P or D to be held in, and a force
            // that pulled outward there would trap a released lever off the end of the lane.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();

            for (int past = cfg.WallRamp; past < 8000; past += 41)
            {
                Assert.True(c.PrndLaneForce(Center - lane.HalfLength - past) > 0);
                Assert.True(c.PrndLaneForce(Center + lane.HalfLength + past) < 0);
            }
        }

        [Fact]
        public void TheDetentAlwaysPushesBackTowardItsOwnPosition()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();

            int notch = cfg.PrndNotchHalfWidth;
            int reach = (lane.Spacing / 2) - 1;

            for (int i = 0; i < PrndLane.PositionCount; i++)
            {
                for (int offset = notch + 50; offset < reach; offset += 29)
                {
                    Assert.True(c.PrndLaneForce(lane.PositionY(i) + offset) <= 0);
                    Assert.True(c.PrndLaneForce(lane.PositionY(i) - offset) >= 0);
                }
            }
        }

        [Fact]
        public void TheNotchCannotBeWidenedIntoAStep()
        {
            // Widen it until the hump beside it has a couple of hundred counts to rise in and the
            // detent stops being a shape and becomes a wall with a curve drawn on it. The clamp is
            // reported rather than applied silently, because the Feel tab prints it.
            EngineConfig cfg = FullGainConfig();
            cfg.PrndNotchHalfWidth = 100000;

            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();

            int expected = (lane.Spacing / 2) - (int)Math.Round(Math.PI * cfg.WallRamp);
            Assert.Equal(expected, c.PrndNotchHalfWidthCeiling);

            AssertNoStep(c, cfg);
        }

        [Fact]
        public void NoSingleCountOfTheLaneEverStepsTheForce()
        {
            // The one that matters. The detent picks the NEAREST position, so it changes which
            // position it is measuring from at every crest - and the whole safety of that is that
            // the force is already zero there. Sweeping every axis count is the only way to prove
            // it, and the same sweep catches the notch edge and both ends of the lane.
            EngineConfig cfg = FullGainConfig();
            cfg.PrndLaneHalfLength = 9000;
            cfg.PrndDetentForcePct = 100;
            cfg.PrndStopForcePct = 100;
            cfg.PrndNotchHalfWidth = 400;

            AssertNoStep(Composer(cfg), cfg);
        }

        [Fact]
        public void AShortLaneStaysContinuousEvenWhereTheNotchIsClampedAway()
        {
            // Short enough that the ceiling is zero and the detent has less room than one wall
            // bite: steeper than a wall face, which the Feel tab says out loud, but still without
            // a step anywhere.
            EngineConfig cfg = FullGainConfig();
            cfg.PrndLaneHalfLength = 2000;
            cfg.PrndNotchHalfWidth = 600;

            ForceComposer c = Composer(cfg);
            Assert.Equal(0, c.PrndNotchHalfWidthCeiling);

            AssertNoStep(c, cfg, boundOverride: (int)Math.Ceiling(
                cfg.PrndDetentForcePct * 100 * Math.PI / Math.Max(1, cfg.BuildPrndLane().Spacing / 2)) + 2);
        }

        /// <summary>
        /// Every axis count of the travel, bounded by the steepest flank the configuration allows.
        /// A raised cosine peaks at pi/2 times its average, and the notch ceiling sizes the span so
        /// that works out at exactly the wall's own stiffness - a plateau over one wall bite.
        /// </summary>
        private static void AssertNoStep(ForceComposer c, EngineConfig cfg, int boundOverride = 0)
        {
            int steepest = Math.Max(cfg.PrndDetentForcePct, cfg.PrndStopForcePct) * 100;
            int bound = boundOverride > 0
                ? boundOverride
                : (int)Math.Ceiling(steepest / (double)Math.Max(1, cfg.WallRamp)) + 1;

            int previous = c.PrndLaneForce(0);

            for (int y = 1; y <= GateGeometry.AxisMax; y++)
            {
                int force = c.PrndLaneForce(y);

                Assert.True(Math.Abs(force - previous) <= bound,
                            "Lane force stepped by " + Math.Abs(force - previous) +
                            " at y " + y + ", bound " + bound);

                previous = force;
            }
        }

        [Fact]
        public void TheComposedFrameRailsTheLeverToTheMiddleAndDetentsItAlongTheLane()
        {
            // The unit tests above cover the shape; this covers the wiring, so a lane cannot be
            // correct in isolation and unreached by the engine. Polarity is left un-inverted so
            // the frame reads in the gate's own frame.
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);
            PrndLane lane = cfg.BuildPrndLane();

            // Pushed off the rail to the right: held back toward the middle.
            Assert.True(c.ComposePrnd(Center + 4000, lane.PositionY(1)).ConstantX < 0);
            Assert.True(c.ComposePrnd(Center - 4000, lane.PositionY(1)).ConstantX > 0);

            // Parked on a position: nothing fore or aft.
            Assert.Equal(0, c.ComposePrnd(Center, lane.PositionY(2)).ConstantY);
        }

        [Fact]
        public void FreeStickReleasesTheLaneLikeEveryOtherPattern()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FreeStick = true;

            ForceFrame frame = Composer(cfg).ComposePrnd(Center + 9000, 4000);

            Assert.Equal(0, frame.ConstantX);
            Assert.Equal(0, frame.ConstantY);
        }
    }
}
