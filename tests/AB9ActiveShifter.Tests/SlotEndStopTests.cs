using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The bottom of an H slot - the piece that makes a short throw a real thing rather than an
    /// early registration. Everything here is in the gate's own frame, before the measured
    /// polarity signs, which is where ForceComposer composes.
    ///
    /// The property that matters most is the last one: the whole stroke has to be continuous in
    /// depth. Two forces meet here - the seated hold fading out and the wall rising - and the
    /// gate has been bitten before by a band that was only ordered rather than given a width
    /// (see GateGeometry.MinBandSpan and AnInvertedTunnelPairCannotBecomeAForceCliff).
    /// </summary>
    public class SlotEndStopTests
    {
        private const int Center = GateGeometry.AxisCenter;

        private static EngineConfig FullGainConfig()
        {
            return new EngineConfig { OverallGainPct = 100, PolarityConfirmed = true };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        /// <summary>Axis reading this far into the slot, on the forward side.</summary>
        private static int AtDepth(int depth)
        {
            return Center - depth;
        }

        [Fact]
        public void ASlotHasNoBottomUntilOneIsAskedFor()
        {
            // The default has to leave the gate byte-identical: this shipped as an addition, and
            // three tuned profiles plus every settings file in the wild are built on the old
            // shape. Zero force is the switch, so the whole feature is one dial away from absent.
            EngineConfig cfg = FullGainConfig();
            Assert.Equal(0, cfg.SlotStopForcePct);

            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();

            for (int depth = 0; depth <= Center; depth += 37)
            {
                int expected = c.DetentMagnitude(
                    ShiftDir.Fwd, geo.EngageFraction(ShiftDir.Fwd, AtDepth(depth)), muted: false);

                Assert.Equal(expected, c.SlotForceAt(ShiftDir.Fwd, AtDepth(depth), muted: false));
            }
        }

        [Fact]
        public void AShortThrowWithoutAStopStillRunsToTheMechanicalStop()
        {
            // The trap this feature exists to close, pinned so it cannot be quietly re-opened by
            // someone "simplifying" the stop away. Shortening the throw alone moves where a gear
            // REGISTERS and nothing else: past that line the seated hold keeps pulling deeper, so
            // the lever ends up at full deflection exactly as it did before and the travel is
            // unchanged. A user reading the throw dial would reasonably expect otherwise.
            EngineConfig cfg = FullGainConfig();
            cfg.EngageDepth = Center - 12000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;

            ForceComposer c = Composer(cfg);

            int atSeat = c.SlotForceAt(ShiftDir.Fwd, AtDepth(12000), muted: false);
            int atStops = c.SlotForceAt(ShiftDir.Fwd, AtDepth(Center), muted: false);

            // Negative is deeper on the forward side, and it never lets up.
            Assert.True(atSeat < 0);
            Assert.Equal(atSeat, atStops);
        }

        [Fact]
        public void TheEndStopOnlyEverPushesBackTowardNeutral()
        {
            // A wall, never a pocket - the same rule the sequential end-stop and the lockout gate
            // follow. Anything that pulled the lever deeper past the seat would be an over-centre
            // trap: a lever released in it would be dragged further in rather than staying put.
            EngineConfig cfg = FullGainConfig();
            cfg.EngageDepth = Center - 10000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;
            cfg.SlotStopForcePct = 90;
            cfg.SlotOvertravel = 2000;

            ForceComposer c = Composer(cfg);
            int stop = c.StrokeStopDepth;

            for (int depth = stop + cfg.WallRamp; depth <= Center; depth += 53)
            {
                Assert.True(c.SlotForceAt(ShiftDir.Fwd, AtDepth(depth), muted: false) > 0);
                Assert.True(c.SlotForceAt(ShiftDir.Back, Center + depth, muted: false) < 0);
            }
        }

        [Fact]
        public void TheLandingIsFreeSoASeatedGearRestsInARegionNotOnAPoint()
        {
            // The whole stability argument. A hold pulling in against a wall pushing out is a
            // restoring force about an interior equilibrium, which this project has learned twice
            // over is an oscillator - it is why the slot corridor and the neutral tunnel are free
            // spaces rather than centre lines. Making the landing carry no force at all means the
            // lever's resting place is a stretch of travel, and there is no gradient at rest for
            // the loop's delay to pump. It only works because the base does not self-centre.
            EngineConfig cfg = FullGainConfig();
            cfg.EngageDepth = Center - 9000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;
            cfg.SlotStopForcePct = 90;
            cfg.SlotOvertravel = 4000;

            ForceComposer c = Composer(cfg);

            // Between the end of the hold's fade and the start of the wall there is nothing.
            for (int depth = 9000 + cfg.WallRamp; depth <= 9000 + 4000; depth += 29)
            {
                Assert.Equal(0, c.SlotForceAt(ShiftDir.Fwd, AtDepth(depth), muted: false));
                Assert.Equal(0, c.SlotForceAt(ShiftDir.Back, Center + depth, muted: false));
            }
        }

        [Fact]
        public void TheLandingCanNeverBeShorterThanTheWallBite()
        {
            // A landing of zero would ask the seated hold to reach nothing within a count or two
            // of the seat, and a full-strength force removed over one axis count is a bang rather
            // than a face - the fore/aft twin of the tunnel band cliff MinBandSpan exists to
            // prevent. The floor is visible rather than silent: StrokeStopDepth reports where the
            // wall really begins, and the Feel tab prints it.
            EngineConfig cfg = FullGainConfig();
            cfg.EngageDepth = Center - 10000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;
            cfg.SlotStopForcePct = 90;
            cfg.SlotOvertravel = 0;

            ForceComposer c = Composer(cfg);

            Assert.Equal(10000 + cfg.WallRamp, c.StrokeStopDepth);
            AssertNoStep(c, cfg);
        }

        [Fact]
        public void AWallAskedForPastTheEndOfTravelIsSimplyNeverMet()
        {
            // At the shipped throw the landing already reaches the end of the base's travel, so
            // turning the stop on cannot conjure a wall the lever could hit. It must report that
            // honestly rather than quoting a depth beyond the axis, because the Feel tab prints
            // this number and the detent curve draws a guide at it.
            EngineConfig cfg = FullGainConfig();
            cfg.SlotStopForcePct = 90;

            Assert.Equal(Center, Composer(cfg).StrokeStopDepth);
        }

        [Fact]
        public void ShorteningTheThrowMovesTheSeatAndTheStopTogether()
        {
            // Measured from the seat, exactly like the sequential stroke, so the shape of a slot
            // is preserved as the throw changes and only its length moves.
            EngineConfig cfg = FullGainConfig();
            cfg.SlotStopForcePct = 90;
            cfg.SlotOvertravel = 2500;

            cfg.EngageDepth = Center - 20000;
            ForceComposer longThrow = Composer(cfg);

            cfg.EngageDepth = Center - 8000;
            ForceComposer shortThrow = Composer(cfg);

            Assert.Equal(20000, longThrow.StrokeSeatDepth);
            Assert.Equal(22500, longThrow.StrokeStopDepth);

            Assert.Equal(8000, shortThrow.StrokeSeatDepth);
            Assert.Equal(10500, shortThrow.StrokeStopDepth);
        }

        [Fact]
        public void ABalkedShiftKeepsTheGrindWallAsItsOnlyStop()
        {
            // A refused gear is stopped a third of the way in by the balk wall; it never reaches
            // a seat, so a second wall behind that one would say nothing and could only confuse
            // the shape the grind deliberately makes.
            EngineConfig cfg = FullGainConfig();
            cfg.EngageDepth = Center - 10000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;
            cfg.SlotStopForcePct = 90;
            cfg.SlotOvertravel = 2000;

            ForceComposer c = Composer(cfg);
            GateGeometry geo = cfg.BuildGeometry();

            for (int depth = 0; depth <= Center; depth += 61)
            {
                int expected = c.DetentMagnitude(
                    ShiftDir.Fwd, geo.EngageFraction(ShiftDir.Fwd, AtDepth(depth)), muted: true);

                Assert.Equal(expected, c.SlotForceAt(ShiftDir.Fwd, AtDepth(depth), muted: true));
            }
        }

        [Fact]
        public void NoSingleCountOfDepthEverStepsTheSlotForce()
        {
            // The one that would catch a regression nothing else here would. Two forces meet at
            // the bottom of a short slot and they are both full-strength; if the fade and the
            // wall ever overlapped, or either were given a span of one count, the result would be
            // a square wave at the report rate for as long as the lever sat there - which is
            // exactly what an inverted tunnel pair once produced on the other axis.
            EngineConfig cfg = FullGainConfig();
            cfg.EngageDepth = Center - 7000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;
            cfg.SlotStopForcePct = 100;
            cfg.SlotOvertravel = 900;

            AssertNoStep(Composer(cfg), cfg);
        }

        /// <summary>
        /// Every single axis count of the stroke, both directions, bounded by the steepest face
        /// the configuration allows: a plateau over the wall bite. Plus one for rounding.
        /// </summary>
        private static void AssertNoStep(ForceComposer c, EngineConfig cfg)
        {
            int steepest = Math.Max(cfg.SlotStopForcePct, cfg.DetentHoldPct) * 100;
            int bound = (int)Math.Ceiling(steepest / (double)Math.Max(1, cfg.WallRamp)) + 1;

            int previousFwd = c.SlotForceAt(ShiftDir.Fwd, Center, muted: false);
            int previousBack = c.SlotForceAt(ShiftDir.Back, Center, muted: false);

            for (int depth = 1; depth <= Center; depth++)
            {
                int fwd = c.SlotForceAt(ShiftDir.Fwd, AtDepth(depth), muted: false);
                int back = c.SlotForceAt(ShiftDir.Back, Center + depth, muted: false);

                Assert.True(Math.Abs(fwd - previousFwd) <= bound,
                            "Forward force stepped by " + Math.Abs(fwd - previousFwd) +
                            " at depth " + depth + ", bound " + bound);
                Assert.True(Math.Abs(back - previousBack) <= bound,
                            "Back force stepped by " + Math.Abs(back - previousBack) +
                            " at depth " + depth + ", bound " + bound);

                previousFwd = fwd;
                previousBack = back;
            }
        }

        [Fact]
        public void TheComposedFrameCarriesTheStopIntoTheGate()
        {
            // The unit above tests the shape; this tests that Compose actually reaches it, so a
            // slot end-stop cannot be correct in isolation and unwired in the gate. Polarity is
            // left un-inverted so the frame is read in the gate's own frame.
            EngineConfig cfg = FullGainConfig();
            cfg.EngageDepth = Center - 9000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;
            cfg.SlotStopForcePct = 90;
            cfg.SlotOvertravel = 2000;

            ForceComposer c = Composer(cfg);

            ForceFrame frame = c.Compose(
                GateState.Engaged, Column.C2, ShiftDir.Fwd, 21845, AtDepth(Center));

            Assert.True(frame.ConstantY > 0, "The lever at full deflection is pushed back toward neutral.");
        }
    }
}
