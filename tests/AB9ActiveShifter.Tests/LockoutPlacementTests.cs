using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The lockout's placement dial: where the gate actually lands for each pattern, direction
    /// and mirror flag, and what happens to a request the pattern cannot honour. The load-bearing
    /// property is the first test - the dial's default must rebuild today's geometry exactly,
    /// because that is the entire migration story for every saved configuration that predates it.
    /// </summary>
    public class LockoutPlacementTests
    {
        private static GateGeometry Geo(GatePattern pattern, LockoutPlacement placement,
            LockoutGapDirection direction = LockoutGapDirection.TowardHigh,
            bool mirror = false, int slotGear = 8)
        {
            return new EngineConfig
            {
                Pattern = pattern,
                LockoutPlacement = placement,
                LockoutGapDirection = direction,
                LockoutSlotGear = slotGear,
                MirrorColumns = mirror
            }.BuildGeometry();
        }

        [Fact]
        public void TheDefaultPlacementIsExactlyTheConfiguredLastGap()
        {
            // PatternDefault must be indistinguishable from asking for the last gap explicitly,
            // and must resolve to nothing on the patterns that never had a gate. This is what
            // lets a saved configuration with no placement key behave exactly as it always did.
            foreach (bool mirror in new[] { false, true })
            {
                foreach (GatePattern p in new[] { GatePattern.H7R, GatePattern.H6R })
                {
                    GateGeometry byDefault = Geo(p, LockoutPlacement.PatternDefault, mirror: mirror);
                    GateGeometry explicitly = Geo(p, LockoutPlacement.Gap3, mirror: mirror);

                    Assert.True(byDefault.HasLockout, p + " should keep its gate");
                    Assert.Equal(explicitly.LockoutGapIndex, byDefault.LockoutGapIndex);
                    Assert.Equal(explicitly.LockoutCentre, byDefault.LockoutCentre);
                    Assert.Equal(explicitly.LockoutHalfWidth, byDefault.LockoutHalfWidth);
                    Assert.Equal(LockoutPlacement.Gap3, byDefault.EffectiveLockoutPlacement);
                    Assert.False(byDefault.LockoutPlacementRepaired);
                }

                foreach (GatePattern p in new[] { GatePattern.H5R, GatePattern.H6 })
                {
                    GateGeometry geo = Geo(p, LockoutPlacement.PatternDefault, mirror: mirror);

                    Assert.False(geo.HasLockout, p + " has never shipped a gate");
                    Assert.Equal(-1, geo.LockoutGapIndex);
                    Assert.Equal(LockoutPlacement.Off, geo.EffectiveLockoutPlacement);
                    Assert.False(geo.LockoutPlacementRepaired);
                }
            }
        }

        [Fact]
        public void ALockoutCanGuardAnyAdjacentGapAndFollowsTheMirroredMap()
        {
            // Placement is stated in map gaps, so mirroring relocates the gate with the gears -
            // the same rule that moves 6+R's missing slot. Gap1 guards 1/2-3/4: device gap 0
            // plain, device gap 2 mirrored, anchored against whichever device column holds the
            // approach side.
            GateGeometry plain = Geo(GatePattern.H7R, LockoutPlacement.Gap1);
            Assert.True(plain.HasLockout);
            Assert.Equal(0, plain.LockoutGapIndex);
            Assert.Equal(plain.ColumnTarget(Column.C1) + plain.ColumnEdgeEnter,
                plain.LockoutCentre - plain.LockoutHalfWidth);

            GateGeometry mirrored = Geo(GatePattern.H7R, LockoutPlacement.Gap1, mirror: true);
            Assert.Equal(2, mirrored.LockoutGapIndex);
            Assert.Equal(mirrored.ColumnTarget(Column.C4) - mirrored.ColumnEdgeEnter,
                mirrored.LockoutCentre + mirrored.LockoutHalfWidth);

            // A middle gap has interior columns on both sides, so the anchor is the ordinary
            // exit clearance, and the device gap is the same one either way round.
            GateGeometry middle = Geo(GatePattern.H7R, LockoutPlacement.Gap2);
            Assert.Equal(1, middle.LockoutGapIndex);
            Assert.Equal(middle.ColumnTarget(Column.C2) + middle.ColumnExitHalfWidth(Column.C2),
                middle.LockoutCentre - middle.LockoutHalfWidth);
            Assert.Equal(1, Geo(GatePattern.H7R, LockoutPlacement.Gap2, mirror: true).LockoutGapIndex);
        }

        [Fact]
        public void AGapThePatternDoesNotHaveIsRepairedToTheLastGapAndSaysSo()
        {
            // Three-column patterns have no fourth-column gap. The user asked for a lockout, so
            // silently having none would be the bigger surprise: the request clamps to the last
            // gap that exists, and the repair is reported for the UI to print.
            foreach (GatePattern p in new[] { GatePattern.H5R, GatePattern.H6 })
            {
                GateGeometry geo = Geo(p, LockoutPlacement.Gap3);

                Assert.True(geo.HasLockout, p + " should still get its gate");
                Assert.Equal(1, geo.LockoutGapIndex);
                Assert.Equal(LockoutPlacement.Gap2, geo.EffectiveLockoutPlacement);
                Assert.True(geo.LockoutPlacementRepaired);
            }

            // A gap the pattern does have is not a repair.
            Assert.False(Geo(GatePattern.H5R, LockoutPlacement.Gap2).LockoutPlacementRepaired);
        }

        [Fact]
        public void TheGateAnchorsAgainstTheApproachSideColumnForEachDirection()
        {
            // The approach column is the one the paying crossing comes from, and the band
            // starts exactly where its band ends so the toll is met with no dead travel.
            // TowardHigh on the last gap is today's gate; TowardLow mirrors it onto the far
            // column, whose edge band is wider than the exit dial.
            GateGeometry high = Geo(GatePattern.H7R, LockoutPlacement.Gap3);
            int mid = (high.ColumnTarget(Column.C3) + high.ColumnTarget(Column.C4)) / 2;

            Assert.Equal(1, high.LockoutBlockSign);
            Assert.Equal(high.ColumnTarget(Column.C3) + high.ColumnExitHalfWidth(Column.C3),
                high.LockoutCentre - high.LockoutHalfWidth);
            Assert.True(high.LockoutCentre < mid, "a TowardHigh crest stays on the low side");

            GateGeometry low = Geo(GatePattern.H7R, LockoutPlacement.Gap3, LockoutGapDirection.TowardLow);

            Assert.Equal(-1, low.LockoutBlockSign);
            Assert.Equal(low.ColumnTarget(Column.C4) - low.ColumnEdgeEnter,
                low.LockoutCentre + low.LockoutHalfWidth);
            Assert.True(low.LockoutCentre > mid, "a TowardLow crest stays on the high side");
        }

        [Fact]
        public void TheGateClearsAnEdgeColumnsWiderBand()
        {
            // Gap1's approach column is the first column, whose free band (ColumnEdgeEnter) is
            // wider than the exit clearance dial. Anchoring by the exit dial alone would start
            // the toll inside the column's own lined-up band - the hard bump exactly where the
            // hand expects to be resting on a column, the same failure the inside-the-band
            // faces fixed once already.
            GateGeometry geo = Geo(GatePattern.H7R, LockoutPlacement.Gap1);

            Assert.True(geo.ColumnEdgeEnter > geo.ColumnExitHalfWidth(Column.C1),
                "this test only means something while the edge band is the wider one");
            Assert.Equal(geo.ColumnTarget(Column.C1) + geo.ColumnEdgeEnter,
                geo.LockoutCentre - geo.LockoutHalfWidth);
        }

        [Fact]
        public void ABothWayGateSitsOnTheMidpointAndClearsBothColumns()
        {
            // A Both gate has no single approach side. Ownership hands over at the midpoint
            // whatever the gate does, so the only symmetric placement is on the midpoint
            // itself; anchoring to either column would leave the other direction's return a
            // free strip ending in a selectable column.
            GateGeometry geo = Geo(GatePattern.H7R, LockoutPlacement.Gap3, LockoutGapDirection.Both);
            int mid = (geo.ColumnTarget(Column.C3) + geo.ColumnTarget(Column.C4)) / 2;

            Assert.Equal(mid, geo.LockoutCentre);
            Assert.Equal(0, geo.LockoutBlockSign);
            Assert.True(geo.LockoutCentre - geo.LockoutHalfWidth
                >= geo.ColumnTarget(Column.C3) + geo.ColumnExitHalfWidth(Column.C3));
            Assert.True(geo.LockoutCentre + geo.LockoutHalfWidth
                <= geo.ColumnTarget(Column.C4) - geo.ColumnEdgeEnter);

            // And an oversized width request is clamped to the room, not obeyed into a band
            // that swallows a column.
            GateGeometry wide = new EngineConfig
            {
                Pattern = GatePattern.H7R,
                LockoutPlacement = LockoutPlacement.Gap3,
                LockoutGapDirection = LockoutGapDirection.Both,
                LockoutHalfWidth = 60000
            }.BuildGeometry();
            Assert.True(wide.LockoutCentre + wide.LockoutHalfWidth
                <= wide.ColumnTarget(Column.C4) - wide.ColumnEdgeEnter);
        }

        [Fact]
        public void TurningThePlacementOffRemovesTheGateEverywhere()
        {
            // Off means off, even on the pattern that has always had a gate: no displaced
            // crest, every barrier at its gap's midpoint, no position inside a band.
            GateGeometry geo = Geo(GatePattern.H7R, LockoutPlacement.Off);

            Assert.False(geo.HasLockout);
            Assert.Equal(LockoutPlacement.Off, geo.EffectiveLockoutPlacement);
            for (int gap = 0; gap < geo.ColumnCount - 1; gap++)
            {
                int mid = (geo.ColumnTarget((Column)gap) + geo.ColumnTarget((Column)(gap + 1))) / 2;
                Assert.Equal(mid, geo.BarrierCentre(gap));
            }
            Assert.False(geo.InLockoutGate(geo.LockoutCentre));
        }

        [Fact]
        public void AFiveGearGateCanStillBeGivenALockout()
        {
            // The 5+R pattern never shipped one, but the dial can put one anywhere it has a
            // gap - presence is configuration now, not a fact of the pattern.
            GateGeometry geo = Geo(GatePattern.H5R, LockoutPlacement.Gap2);

            Assert.True(geo.HasLockout);
            Assert.Equal(1, geo.LockoutGapIndex);
            Assert.Equal(geo.ColumnTarget(Column.C2) + geo.ColumnExitHalfWidth(Column.C2),
                geo.LockoutCentre - geo.LockoutHalfWidth);
        }

        [Fact]
        public void ASlotPlacementResolvesThroughTheGearMapAndClosesTheGapGate()
        {
            // One lockout per profile: choosing a slot spends it, so the traditional gap gate
            // goes away and every crest returns to its midpoint. The slot itself is found by
            // inverting the gear map, so mirroring moves it with the gear.
            GateGeometry geo = Geo(GatePattern.H7R, LockoutPlacement.Slot, slotGear: 8);

            Assert.True(geo.LockoutIsSlot);
            Assert.Equal(Column.C4, geo.LockoutSlotColumn);
            Assert.Equal(ShiftDir.Back, geo.LockoutSlotDir);
            Assert.False(geo.HasLockout);
            for (int gap = 0; gap < geo.ColumnCount - 1; gap++)
            {
                int mid = (geo.ColumnTarget((Column)gap) + geo.ColumnTarget((Column)(gap + 1))) / 2;
                Assert.Equal(mid, geo.BarrierCentre(gap));
            }

            GateGeometry mirrored = Geo(GatePattern.H7R, LockoutPlacement.Slot, mirror: true, slotGear: 8);
            Assert.Equal(Column.C1, mirrored.LockoutSlotColumn);
            Assert.Equal(ShiftDir.Back, mirrored.LockoutSlotDir);
        }

        [Fact]
        public void ASlotLockoutOnAGearThatDoesNotExistRepairsToOffAndSaysSo()
        {
            // There is no nearest sensible gear to substitute, so the lockout turns off and
            // the repair is reported: 7 on the six-gear-plus-R map, R on the truck map.
            GateGeometry missing7 = Geo(GatePattern.H6R, LockoutPlacement.Slot, slotGear: 7);
            Assert.False(missing7.LockoutIsSlot);
            Assert.False(missing7.HasLockout);
            Assert.Equal(LockoutPlacement.Off, missing7.EffectiveLockoutPlacement);
            Assert.True(missing7.LockoutPlacementRepaired);

            GateGeometry noReverse = Geo(GatePattern.H6, LockoutPlacement.Slot, slotGear: 8);
            Assert.Equal(LockoutPlacement.Off, noReverse.EffectiveLockoutPlacement);
            Assert.True(noReverse.LockoutPlacementRepaired);

            // The same request against a pattern that holds the gear is honoured, not repaired.
            Assert.False(Geo(GatePattern.H7R, LockoutPlacement.Slot, slotGear: 7).LockoutPlacementRepaired);
        }

        [Fact]
        public void TheSixGearGateIsThreeColumnsWithNoReverseAnywhere()
        {
            GateGeometry geo = Geo(GatePattern.H6, LockoutPlacement.PatternDefault);

            Assert.Equal(3, geo.ColumnCount);
            Assert.False(geo.HasLockout);

            // Buttons 1..6 laid out as the classic H, and gear 8 - reverse - nowhere in the
            // map. The sixth slot is just button 6; the game decides what it means.
            Assert.Equal(1, geo.GearFor(Column.C1, ShiftDir.Fwd));
            Assert.Equal(2, geo.GearFor(Column.C1, ShiftDir.Back));
            Assert.Equal(3, geo.GearFor(Column.C2, ShiftDir.Fwd));
            Assert.Equal(4, geo.GearFor(Column.C2, ShiftDir.Back));
            Assert.Equal(5, geo.GearFor(Column.C3, ShiftDir.Fwd));
            Assert.Equal(6, geo.GearFor(Column.C3, ShiftDir.Back));
            Assert.Equal("6", geo.LabelFor(6));

            for (int c = 0; c < geo.ColumnCount; c++)
            {
                Assert.True(geo.SlotExists((Column)c, ShiftDir.Fwd));
                Assert.True(geo.SlotExists((Column)c, ShiftDir.Back));
                Assert.NotEqual(8, geo.GearFor((Column)c, ShiftDir.Fwd));
                Assert.NotEqual(8, geo.GearFor((Column)c, ShiftDir.Back));
            }
        }

        [Fact]
        public void MirroringTheSixGearGateRelabelsWithoutMovingAnything()
        {
            GateGeometry plain = Geo(GatePattern.H6, LockoutPlacement.PatternDefault);
            GateGeometry mirrored = Geo(GatePattern.H6, LockoutPlacement.PatternDefault, mirror: true);

            for (int c = 0; c < plain.ColumnCount; c++)
            {
                Assert.Equal(plain.ColumnTarget((Column)c), mirrored.ColumnTarget((Column)c));
            }

            Assert.Equal(5, mirrored.GearFor(Column.C1, ShiftDir.Fwd));
            Assert.Equal(6, mirrored.GearFor(Column.C1, ShiftDir.Back));
            Assert.Equal(1, mirrored.GearFor(Column.C3, ShiftDir.Fwd));
            Assert.Equal(2, mirrored.GearFor(Column.C3, ShiftDir.Back));
        }

        [Fact]
        public void TheSixGearGateTakesAConfiguredLockoutLikeAnyH()
        {
            // The truck recipe: a gate between the first two columns, paying on the way DOWN
            // into the low range. The approach side is then the higher-gear column - the
            // interior one - and the crest stays in its half of the gap.
            GateGeometry geo = Geo(GatePattern.H6, LockoutPlacement.Gap1, LockoutGapDirection.TowardLow);
            int mid = (geo.ColumnTarget(Column.C1) + geo.ColumnTarget(Column.C2)) / 2;

            Assert.True(geo.HasLockout);
            Assert.Equal(0, geo.LockoutGapIndex);
            Assert.Equal(-1, geo.LockoutBlockSign);
            Assert.Equal(geo.ColumnTarget(Column.C2) - geo.ColumnExitHalfWidth(Column.C2),
                geo.LockoutCentre + geo.LockoutHalfWidth);
            Assert.True(geo.LockoutCentre > mid, "the crest belongs to the approach side");
        }
    }
}
