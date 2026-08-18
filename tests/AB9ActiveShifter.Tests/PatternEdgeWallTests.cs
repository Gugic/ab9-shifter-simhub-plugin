using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The wall outside the outermost columns, which is what a narrowed pattern needs and a
    /// full-width one has for free.
    /// <para>
    /// Reported from the rig against the first build of the width dial: the pattern narrowed
    /// correctly and then had no edge. In the neutral tunnel the guide's plateau is
    /// <c>ColumnDetentForcePct</c>, which every shipped tune sets to zero on purpose - sliding
    /// across the gate is meant to be free - and that was harmless only while the outer columns
    /// sat at the ends of travel, where the base's own stop WAS the edge. Narrowed, the tunnel is
    /// free from one stop to the other with the gate floating in the middle: measured at exactly
    /// 0 DI across the whole axis, and a lever parked ten thousand counts outside the first column
    /// with nothing to say the gate had ended.
    /// </para>
    /// <para>
    /// Everything here samples <c>Compose</c> rather than calling <c>LateralGuide</c> with a
    /// hand-picked column, and that is not a style choice. Below the tunnel the guide column is
    /// deliberately frozen to the one already held; picking it by position instead reintroduces
    /// the midpoint reversal the whole handover design exists to remove, and reads as a 9810 DI
    /// step that the gate does not have. Measured while writing these tests, at full width, where
    /// the edge wall cannot be involved at all.
    /// </para>
    /// </summary>
    public class PatternEdgeWallTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int Max = GateGeometry.AxisMax;

        /// <summary>The shipped loose tune, whose tunnel is free - the case that had no edge.</summary>
        private static EngineConfig Tune(int widthPct, int edgePct = 100)
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                Pattern = GatePattern.H5R,
                PatternWidthPct = widthPct,
                PatternEdgeForcePct = edgePct,
                SlotHalfWidth = 2400,
                ChannelFreeDepth = 2165,
                ColumnPinForcePct = 80,
                ChannelWallForcePct = 100,
                ChannelGuideForcePct = 20,
                ColumnDetentForcePct = 0,
                BarrierForcePct = 0,
                WallRamp = 3816,
                WallBlend = 1559,
                ChannelHalfEnter = 3268,
                ChannelHalfExit = 4051,
                MouthShape = SlotMouthShape.Angled,
                MouthDepth = 12000
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        private static int Neutral(ForceComposer c, int x, int y)
        {
            return c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y).ConstantX;
        }

        [Fact]
        public void ANarrowedPatternIsWalledAtBothEdges()
        {
            // The reported symptom, at the two places it was felt: hard against either stop, in
            // the neutral tunnel, where before this there was nothing at all.
            EngineConfig cfg = Tune(67);

            foreach (int depth in new[] { 0, 2000, cfg.ChannelHalfEnter, 6000, 12000 })
            {
                Assert.Equal(GateGeometry.ForceMax, Neutral(Composer(cfg), 0, Center + depth));
                Assert.Equal(-GateGeometry.ForceMax, Neutral(Composer(cfg), Max, Center + depth));
            }
        }

        [Fact]
        public void TheEdgeWallOnlyEverPushesBackIn()
        {
            // One-sided, which is the whole of why it is safe: a wall that only pushes inward is
            // not a restoring force about an interior equilibrium and cannot hunt. If it ever
            // pushed outward it would be a centring force with the outer column as its middle,
            // which is the shape corridors exist to avoid.
            EngineConfig cfg = Tune(50);
            GateGeometry geo = cfg.BuildGeometry();

            int left = geo.ColumnTarget(Column.C1);
            int right = geo.ColumnTarget((Column)(geo.ColumnCount - 1));

            ForceComposer c = Composer(cfg);
            for (int x = 0; x < left; x += 13)
            {
                Assert.True(Neutral(c, x, Center) >= 0, "the left edge pushed outward at x=" + x);
            }

            c = Composer(cfg);
            for (int x = Max; x > right; x -= 13)
            {
                Assert.True(Neutral(c, x, Center) <= 0, "the right edge pushed outward at x=" + x);
            }
        }

        [Fact]
        public void InsideThePatternTheTunnelIsStillCompletelyFree()
        {
            // The thing the fix must not cost. A free tunnel is the shipped tunes' whole
            // character - no hump between columns, no detent holding the lever at one - so the
            // edge wall has to be exactly zero everywhere a hand actually slides.
            EngineConfig cfg = Tune(67);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int left = geo.ColumnTarget(Column.C1);
            int right = geo.ColumnTarget((Column)(geo.ColumnCount - 1));

            for (int x = left; x <= right; x += 7)
            {
                Assert.Equal(0, Neutral(c, x, Center));
            }
        }

        [Fact]
        public void AtFullWidthTheEdgeWallRendersNothingWhateverItIsSetTo()
        {
            // Inert by construction rather than by a guard: at 100% the outer columns are at the
            // stops, so the region this renders in has no width. That is what makes it safe to
            // default to full strength and to hand it to every existing profile at once.
            foreach (GatePattern pattern in new[] { GatePattern.H7R, GatePattern.H6R,
                                                    GatePattern.H5R, GatePattern.H6 })
            {
                EngineConfig off = Tune(100, 0);
                EngineConfig on = Tune(100, 100);
                off.Pattern = pattern;
                on.Pattern = pattern;

                foreach (int depth in new[] { 0, 3000, 8000, 14000 })
                {
                    ForceComposer a = Composer(off);
                    ForceComposer b = Composer(on);

                    for (int x = 0; x <= Max; x += 251)
                    {
                        Assert.Equal(Neutral(a, x, Center + depth), Neutral(b, x, Center + depth));
                    }
                }
            }
        }

        [Fact]
        public void NoSingleCountOfDriftStepsTheFieldAtANarrowedWidth()
        {
            // The continuity invariant, re-run where the new code path lives. The sweep in
            // ForceComposerTests runs at the default width, where the edge wall is inert, so it
            // cannot see this at all.
            //
            // It shares that sweep's bound - the wall's own stiffness - and that is the point.
            // The edge is stronger than the lateral pin, so GuideFace gives it a proportionally
            // LONGER face rather than a steeper one, which is the rule that method exists to
            // enforce. If the ceiling at one wall bite were applied out there the gradient would
            // rise with the force and this would fail.
            //
            // Square mouths, deliberately, to put the edge wall on its own: the angled mouth has
            // a step of its own at narrow widths that has nothing to do with this. See
            // AnAngledMouthStepsAtNarrowWidthsAndThatIsNotTheEdgeWall below.
            EngineConfig cfg = Tune(45);
            cfg.MouthShape = SlotMouthShape.Square;

            int bound = (int)Math.Ceiling(
                GateGeometry.ForceMax * cfg.ColumnPinForcePct / 100.0 / Math.Max(1, cfg.WallRamp)) + 3;

            foreach (int depth in new[] { 0, 1000, cfg.ChannelHalfEnter, cfg.ChannelHalfExit, 9000, 14000 })
            {
                foreach (int direction in new[] { 1, -1 })
                {
                    ForceComposer c = Composer(cfg);
                    int from = direction > 0 ? 0 : Max;
                    int previous = Neutral(c, from, Center + depth);

                    for (int step = 1; step <= Max; step++)
                    {
                        int x = direction > 0 ? step : Max - step;
                        int force = Neutral(c, x, Center + depth);

                        Assert.True(Math.Abs(force - previous) <= bound,
                            "step of " + (force - previous) + " at x=" + x + " depth=" + depth
                            + " sweeping " + direction + ", bound " + bound);
                        previous = force;
                    }
                }
            }
        }

        [Fact]
        public void AnAngledMouthStepsAtNarrowWidthsAndThatIsNotTheEdgeWall()
        {
            // A defect found while testing the edge wall, recorded here rather than left to be
            // rediscovered. It belongs to the width dial, not to this branch, and it is present
            // with the edge wall switched entirely off.
            //
            // MouthOpeningFor bounds a mouth at ColumnSpacing/2 - corridor - 200. Narrow the
            // pattern and that half-spacing shrinks until the mouth reaches the column boundary,
            // where the guide changes hands - and the two columns' mouths do not match there,
            // because an angled mouth opens ONE flank and the outer and inner columns have
            // different corridors. Measured on the shipped loose tune, worst step across the axis
            // at depth = ChannelHalfExit:
            //
            //     H5R  100%    3 DI        H7R  100%    7 DI
            //     H5R   67%    7 DI        H7R   67%   37 DI
            //     H5R   55%   27 DI        H7R   45%   32 DI
            //     H5R   45%   38 DI
            //
            // 38 DI is 0.046 Nm - three orders of magnitude under the failures this bound was
            // written for (20000, 9810, 4924) - but it is a step, and it grows as the pattern
            // narrows. The fix is to bound the mouth by the handover window instead of by a flat
            // 200 counts, which changes mouths at every width and so wants its own change and its
            // own session on the rig. This test holds the line where it is measured today, so a
            // fix shows up as a failure here and anything that makes it worse does too.
            EngineConfig cfg = Tune(45, 0);
            Assert.Equal(SlotMouthShape.Angled, cfg.MouthShape);

            int worst = 0;
            ForceComposer c = Composer(cfg);
            int previous = Neutral(c, 0, Center + cfg.ChannelHalfExit);

            for (int x = 1; x <= Max; x++)
            {
                int force = Neutral(c, x, Center + cfg.ChannelHalfExit);
                worst = Math.Max(worst, Math.Abs(force - previous));
                previous = force;
            }

            Assert.InRange(worst, 20, 45);
        }

        [Fact]
        public void TheEdgeWallIsAsStrongAsAskedFor()
        {
            // A dial, not a constant, so a soft edge is available - and turning it off gives back
            // exactly the behaviour the first width build shipped with.
            foreach (int pct in new[] { 0, 40, 80, 100 })
            {
                EngineConfig cfg = Tune(60, pct);
                int expected = (int)Math.Round(GateGeometry.ForceMax * pct / 100.0);

                Assert.Equal(expected, Neutral(Composer(cfg), 0, Center));
                Assert.Equal(-expected, Neutral(Composer(cfg), Max, Center));
            }
        }

        [Fact]
        public void ALatchedGearIsStillWalledPastTheEdge()
        {
            // The latched branch gets it too, because it is one function of position and the guide
            // column called by both. A gear held in an outer column and dragged off the side of
            // the pattern must still meet the edge - the latch is absolute and the wall is the
            // entire enforcement of it.
            EngineConfig cfg = Tune(55);
            GateGeometry geo = cfg.BuildGeometry();
            int outermost = geo.ColumnCount - 1;

            ForceComposer c = Composer(cfg);
            Assert.Equal(GateGeometry.ForceMax,
                c.Compose(GateState.Engaged, Column.C1, ShiftDir.Fwd, 0, 0).ConstantX);

            c = Composer(cfg);
            Assert.Equal(-GateGeometry.ForceMax,
                c.Compose(GateState.Engaged, (Column)outermost, ShiftDir.Fwd, Max, 0).ConstantX);

            // An inner column keeps its own pin out there instead, which is right: what holds a
            // latched gear is the wall of the column holding it, and that wall already covers
            // everything beyond the pattern.
            c = Composer(cfg);
            int inner = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 0, 0).ConstantX;
            Assert.Equal((int)Math.Round(GateGeometry.ForceMax * cfg.ColumnPinForcePct / 100.0), inner);
        }
    }
}
