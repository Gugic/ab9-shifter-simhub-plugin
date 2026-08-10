using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The lateral rail - what holds the lever on the centre line in the patterns with no columns.
    ///
    /// It had a deadband of zero, which made it the only lateral force in the gate without a free
    /// region and the one place the rule that a restoring force about an interior equilibrium is an
    /// oscillator was still being broken. Past its short face the force is a flat plateau whose
    /// sign inverts at the centre line: a relay, and it behaved like one. Measured on the rig from
    /// a PRND trace at 80% pin force and full gain - a sustained 9.8 Hz limit cycle, +-16484 counts
    /// of swing, 972000 counts/s peak, saturated at +-12 Nm for 79% of the cycle.
    ///
    /// These go through Compose rather than at the shape directly, so a rail cannot be the right
    /// shape and wired to nothing. With zero velocity and no dt, damping, friction, the yield and
    /// the time shaping all pass through untouched, so ConstantX is the rail itself.
    /// </summary>
    public class LateralRailTests
    {
        private const int Center = GateGeometry.AxisCenter;

        private static EngineConfig RailConfig(int corridor)
        {
            return new EngineConfig
            {
                Pattern = GatePattern.Sequential,
                OverallGainPct = 100,
                PolarityConfirmed = true,
                ColumnPinForcePct = 90,
                SlotHalfWidth = corridor
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        private static int RailAt(ForceComposer c, int x)
        {
            return c.ComposeSequential(x, Center).ConstantX;
        }

        [Fact]
        public void TheRailIsFreeInsideItsCorridor()
        {
            // The whole fix. A selected place has to be a region the lever rests in, exactly as a
            // slot is - it is the same dial, and now the same rule.
            EngineConfig cfg = RailConfig(1100);
            ForceComposer c = Composer(cfg);

            for (int offset = -1100; offset <= 1100; offset += 17)
            {
                Assert.Equal(0, RailAt(c, Center + offset));
            }
        }

        [Fact]
        public void TheRailOnlyEverPushesBackTowardTheCentreLine()
        {
            EngineConfig cfg = RailConfig(1100);
            ForceComposer c = Composer(cfg);

            for (int offset = 1200; offset < Center; offset += 79)
            {
                Assert.True(RailAt(c, Center + offset) < 0);
                Assert.True(RailAt(c, Center - offset) > 0);
            }
        }

        [Fact]
        public void TheRailCannotInvertItsForceAcrossTheCentreLine()
        {
            // The regression this exists to prevent, stated as the property that actually failed:
            // with no corridor the plateau is held right up to the centre line, so one axis count
            // of drift swaps a saturated push left for a saturated push right. With a corridor
            // there is a band around centre where both sides read zero, so no crossing can invert
            // anything - the lever coasts across instead of being caught and thrown back.
            ForceComposer railed = Composer(RailConfig(1100));

            Assert.Equal(0, RailAt(railed, Center - 1));
            Assert.Equal(0, RailAt(railed, Center));
            Assert.Equal(0, RailAt(railed, Center + 1));

            // And the old shape, kept as a supported setting, still does exactly that - which is
            // why zero is documented as the rail gate and as stable only at moderate pin force.
            ForceComposer line = Composer(RailConfig(0));

            Assert.True(RailAt(line, Center - 1) > 0);
            Assert.True(RailAt(line, Center + 1) < 0);
        }

        [Fact]
        public void AZeroCorridorIsStillSupportedAndStillReachesFullForce()
        {
            // Zero has always meant the rail gate - the native shifter-mode topology - and must
            // keep meaning it. What changed is that it is no longer the only option.
            EngineConfig cfg = RailConfig(0);
            ForceComposer c = Composer(cfg);

            Assert.Equal(-9000, RailAt(c, Center + 20000));
            Assert.Equal(9000, RailAt(c, Center - 20000));
        }

        [Fact]
        public void NoSingleCountEverStepsTheRail()
        {
            // Every axis count across the whole of travel, bounded by the steepest face the
            // configuration allows: the plateau over the wall bite, which is the one stiffness
            // every lateral force in this gate is held to.
            foreach (int corridor in new[] { 0, 1100, 2400 })
            {
                EngineConfig cfg = RailConfig(corridor);
                ForceComposer c = Composer(cfg);

                int bound = (int)Math.Ceiling(cfg.ColumnPinForcePct * 100 / (double)cfg.WallRamp) + 1;
                int previous = RailAt(c, 0);

                for (int x = 1; x <= GateGeometry.AxisMax; x++)
                {
                    int force = RailAt(c, x);

                    Assert.True(Math.Abs(force - previous) <= bound,
                                "Rail stepped by " + Math.Abs(force - previous) + " at x " + x +
                                " with corridor " + corridor + ", bound " + bound);

                    previous = force;
                }
            }
        }

        [Fact]
        public void TheCorridorNarrowsTheFaceRatherThanSteepeningIt()
        {
            // One stiffness: widening the corridor moves where the face starts, never how steep it
            // is. A rail that got steeper as it got narrower would trade one instability for
            // another.
            foreach (int corridor in new[] { 0, 1100, 2400 })
            {
                ForceComposer c = Composer(RailConfig(corridor));

                int atFaceEnd = Math.Abs(RailAt(c, Center + corridor + 600));
                Assert.Equal(9000, atFaceEnd);

                int halfway = Math.Abs(RailAt(c, Center + corridor + 300));
                Assert.InRange(halfway, 4400, 4600);
            }
        }
    }
}
