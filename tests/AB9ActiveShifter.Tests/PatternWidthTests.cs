using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// How wide the pattern stands. The dial exists for the three-column patterns: 5+R and the
    /// truck 6 spread half as many columns across the same stick as 7+R does, which puts 32767
    /// counts between columns against 21845 and reads as sprawling next to a real five-speed.
    /// <para>
    /// The first test is the load-bearing one. The default must rebuild today's geometry to the
    /// exact count, for every pattern, because that is the whole migration story for every saved
    /// profile that predates the dial - a column target that moved by one would move every wall,
    /// every barrier crest and the lockout with it.
    /// </para>
    /// </summary>
    public class PatternWidthTests
    {
        private static readonly GatePattern[] AllPatterns =
        {
            GatePattern.H7R, GatePattern.H6R, GatePattern.H5R, GatePattern.H6
        };

        private static GateGeometry Geo(GatePattern pattern, int widthPct = 100)
        {
            return new EngineConfig { Pattern = pattern, PatternWidthPct = widthPct }.BuildGeometry();
        }

        [Fact]
        public void TheDefaultWidthLeavesEveryColumnExactlyWhereItAlwaysWas()
        {
            // The formula this replaced, kept literally: targets spread over the whole axis.
            foreach (GatePattern pattern in AllPatterns)
            {
                GateGeometry geo = Geo(pattern);

                Assert.Equal(100, geo.PatternWidthPct);
                Assert.Equal(GateGeometry.AxisMax, geo.PatternSpan);
                Assert.Equal(GateGeometry.AxisMax / (geo.ColumnCount - 1), geo.ColumnSpacing);

                for (int i = 0; i < geo.ColumnCount; i++)
                {
                    int expected = (int)Math.Round(i * (double)GateGeometry.AxisMax / (geo.ColumnCount - 1));
                    Assert.Equal(expected, geo.ColumnTarget((Column)i));
                }

                // And the outer columns are still at the ends of travel, which is what made them
                // one-sided against the stop rather than an interior equilibrium.
                Assert.Equal(0, geo.ColumnTarget(Column.C1));
                Assert.Equal(GateGeometry.AxisMax, geo.ColumnTarget((Column)(geo.ColumnCount - 1)));
            }
        }

        [Fact]
        public void NarrowingSqueezesThePatternInFromBothSidesRatherThanAnchoringIt()
        {
            // Centred, not anchored: the same room comes off each end, so the gate stays where
            // the hand expects it and an odd pattern's middle column does not move at all.
            // Anchoring at zero would slide the whole gate left as it narrowed.
            foreach (GatePattern pattern in AllPatterns)
            {
                GateGeometry full = Geo(pattern);
                GateGeometry half = Geo(pattern, 50);

                int last = half.ColumnCount - 1;
                int cutLeft = half.ColumnTarget(Column.C1) - full.ColumnTarget(Column.C1);
                int cutRight = full.ColumnTarget((Column)last) - half.ColumnTarget((Column)last);

                Assert.True(cutLeft > 0, "narrowing did not move the first column in");
                Assert.True(Math.Abs(cutLeft - cutRight) <= 1,
                    pattern + " narrowed unevenly: " + cutLeft + " off the left, " + cutRight + " off the right");
            }

            // Three columns, so there is a middle one, and it does not move at ANY width - swept
            // rather than spot-checked, because the two roundings this needs both land on exact
            // half-counts at some widths and not others, and .NET rounds those to even by
            // default. That column is 3/4 and the home spring's anchor; narrowing must not
            // shift the place a hand comes back to.
            int middle = Geo(GatePattern.H5R).ColumnTarget(Column.C2);
            for (int w = GateGeometry.MinPatternWidthPct; w <= 100; w++)
            {
                Assert.Equal(middle, Geo(GatePattern.H5R, w).ColumnTarget(Column.C2));
                Assert.Equal(middle, Geo(GatePattern.H6, w).ColumnTarget(Column.C2));
            }
        }

        [Fact]
        public void AThreeColumnPatternCanBeGivenTheSpacingOfAFourColumnOne()
        {
            // The reported complaint, and the number that answers it: 5+R at full width crosses
            // 32767 counts between columns where 7+R crosses 21845. Two thirds of the axis is
            // what makes the reach the same.
            int fourColumn = Geo(GatePattern.H7R).ColumnSpacing;
            int narrowed = Geo(GatePattern.H5R, 67).ColumnSpacing;

            Assert.True(Math.Abs(narrowed - fourColumn) < 300,
                "5+R at 67% spaces its columns " + narrowed + " apart against the four-column " + fourColumn);
        }

        [Fact]
        public void EveryPositionInTheGateStillBelongsToAColumn()
        {
            // The invariant a narrowed pattern is most likely to break: past the outermost column
            // there is now bare axis on both sides, and if that read as belonging to nothing the
            // gate would be passable there with no gear to select - a silent non-shift, which is
            // the worst answer this gate can give. ColumnAt is nearest-column, so it does not.
            GateGeometry geo = Geo(GatePattern.H5R, 40);

            for (int x = 0; x <= GateGeometry.AxisMax; x += 137)
            {
                Column c = geo.ColumnAt(x);
                Assert.NotEqual(Column.None, c);
                Assert.InRange((int)c, 0, geo.ColumnCount - 1);
            }
        }

        [Fact]
        public void AWidthBelowTheFloorIsClampedRatherThanObeyed()
        {
            // It arrives from a settings file, so it is data. Below the floor a four-column gate
            // has less room between columns than a shipped slot corridor and its wall need, and
            // zero would collapse every column onto the same point - one target for every gear.
            foreach (int asked in new[] { int.MinValue, -50, 0, 1, GateGeometry.MinPatternWidthPct - 1 })
            {
                GateGeometry geo = Geo(GatePattern.H7R, asked);
                Assert.Equal(GateGeometry.MinPatternWidthPct, geo.PatternWidthPct);
                Assert.True(geo.ColumnSpacing > 0);
            }

            foreach (int asked in new[] { 101, 1000, int.MaxValue })
            {
                Assert.Equal(100, Geo(GatePattern.H7R, asked).PatternWidthPct);
            }

            // Distinct targets at the floor, in ascending order, for every pattern.
            foreach (GatePattern pattern in AllPatterns)
            {
                GateGeometry geo = Geo(pattern, GateGeometry.MinPatternWidthPct);
                for (int i = 1; i < geo.ColumnCount; i++)
                {
                    Assert.True(geo.ColumnTarget((Column)i) > geo.ColumnTarget((Column)(i - 1)),
                        pattern + " column " + i + " is not right of the one before it");
                }
            }
        }

        [Fact]
        public void NarrowingMovesTheSpacingAndLeavesEveryLateralDialAsTheCountItWas()
        {
            // The dial is a multiplier on the geometry, not a rescale of the tune. Every lateral
            // width stays the raw count it was set to, which is exactly why it becomes a larger
            // share of a narrower gate - and why the percent-of-spacing views move when this does
            // while nothing stored changes.
            EngineConfig cfg = new EngineConfig
            {
                Pattern = GatePattern.H5R,
                PatternWidthPct = 60,
                ChannelHalfEnter = 3268,
                ChannelHalfExit = 4051,
                ColumnEdgeEnter = 2600,
                ColumnInnerHalfEnter = 2286,
                DetentHysteresis = 905
            };
            GateGeometry geo = cfg.BuildGeometry();

            Assert.Equal(3268, geo.ChannelHalfEnter);
            // ...except where a floor already applied before this dial existed: the tunnel pair is
            // the one band a force ramps across, so MinBandSpan widens 4051 to 3268 + 1000.
            Assert.Equal(4268, geo.ChannelHalfExit);
            Assert.Equal(2600, geo.ColumnEdgeEnter);
            Assert.Equal(2286, geo.ColumnInnerHalfEnter);
            Assert.Equal(905, geo.DetentHysteresis);

            Assert.Equal(geo.PatternSpan / (geo.ColumnCount - 1), geo.ColumnSpacing);
            Assert.True(geo.ColumnSpacing < Geo(GatePattern.H5R).ColumnSpacing);
        }

        [Fact]
        public void TheLockoutNarrowsWithTheGateItGuards()
        {
            // The gate places itself against a column rather than at a fixed position, so it has
            // to follow them in. A second copy of that position would not - which is the bug the
            // Monitor tab's shading once was.
            GateGeometry full = Geo(GatePattern.H7R);
            GateGeometry narrow = Geo(GatePattern.H7R, 60);

            Assert.True(full.HasLockout && narrow.HasLockout);
            Assert.True(narrow.LockoutCentre < full.LockoutCentre,
                "the gate did not move in with its columns");

            // Still between the two columns it guards, at both widths.
            foreach (GateGeometry geo in new[] { full, narrow })
            {
                int gap = geo.LockoutGapIndex;
                Assert.InRange(geo.LockoutCentre,
                    geo.ColumnTarget((Column)gap), geo.ColumnTarget((Column)(gap + 1)));
            }
        }

        [Fact]
        public void ANarrowedGateIsStillAGateAtEveryColumn()
        {
            // The whole point is a shorter reach, not a softer gate: each column must still open
            // its own doorway and still be walled off between them. Sampled through the tunnel,
            // where the fore/aft wall is what a hand meets sliding across.
            EngineConfig cfg = new EngineConfig { Pattern = GatePattern.H5R, PatternWidthPct = 60 };
            GateGeometry geo = cfg.BuildGeometry();

            for (int i = 0; i < geo.ColumnCount; i++)
            {
                Assert.Equal(0.0, geo.ChannelBlockFactor(geo.ColumnTarget((Column)i), cfg.WallBlend, ShiftDir.Fwd));
            }

            for (int gap = 0; gap < geo.ColumnCount - 1; gap++)
            {
                int crest = geo.BarrierCentre(gap);
                Assert.Equal(1.0, geo.ChannelBlockFactor(crest, cfg.WallBlend, ShiftDir.Fwd));
            }
        }
    }
}
