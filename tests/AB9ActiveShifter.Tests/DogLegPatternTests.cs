using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The dog-leg four-speed: R above 1 in the first column, 2 above 3 in the middle, 4 alone at
    /// the top of the last. Everything about it is a fact of the gear map - the relocated reverse
    /// and the hole under fourth - so these tests ask the map, and then check that the walls, the
    /// state machine and the output mask all read the same answer from it.
    /// </summary>
    public class DogLegPatternTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int Max = GateGeometry.AxisMax;

        private static EngineConfig Config()
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                Pattern = GatePattern.H4RDogLeg
            };
        }

        [Fact]
        public void TheMapIsRAboveFirstThenTwoThreeThenFourAlone()
        {
            GateGeometry geo = Config().BuildGeometry();

            Assert.Equal(3, geo.ColumnCount);

            Assert.Equal(8, geo.GearFor(Column.C1, ShiftDir.Fwd));
            Assert.Equal(1, geo.GearFor(Column.C1, ShiftDir.Back));
            Assert.Equal(2, geo.GearFor(Column.C2, ShiftDir.Fwd));
            Assert.Equal(3, geo.GearFor(Column.C2, ShiftDir.Back));
            Assert.Equal(4, geo.GearFor(Column.C3, ShiftDir.Fwd));
            Assert.Equal(0, geo.GearFor(Column.C3, ShiftDir.Back));

            // Reverse is still gear 8 and still labelled R, wherever in the gate it sits, so a
            // game bound for any other H pattern reads this one's reverse as reverse.
            Assert.Equal("R", geo.LabelFor(geo.GearFor(Column.C1, ShiftDir.Fwd)));
            Assert.False(geo.SlotExists(Column.C3, ShiftDir.Back));
        }

        [Fact]
        public void TheLeverRestsBetweenSecondAndThird()
        {
            // The home column is the one a racing four-speed's lever springs to: the 2-3 plane.
            GateGeometry geo = Config().BuildGeometry();

            Assert.Equal(Column.C2, geo.HomeColumn);
            Assert.Equal(2, geo.GearFor(geo.HomeColumn, ShiftDir.Fwd));
            Assert.Equal(3, geo.GearFor(geo.HomeColumn, ShiftDir.Back));
        }

        [Fact]
        public void EveryGearCanBeFoundAndNothingElseCan()
        {
            GateGeometry geo = Config().BuildGeometry();
            Column column;
            ShiftDir dir;

            foreach (int gear in new[] { 1, 2, 3, 4, 8 })
            {
                Assert.True(geo.TryFindSlot(gear, out column, out dir), "gear " + gear);
                Assert.Equal(gear, geo.GearFor(column, dir));
            }

            foreach (int gear in new[] { 5, 6, 7 })
            {
                Assert.False(geo.TryFindSlot(gear, out column, out dir), "gear " + gear);
            }
        }

        [Fact]
        public void MirroringMovesReverseAndTheHoleWithTheGears()
        {
            EngineConfig columns = Config();
            columns.MirrorColumns = true;
            GateGeometry byColumn = columns.BuildGeometry();

            // Left-hand-drive mirror: R/1 on the right, 4 alone on the left.
            Assert.Equal(8, byColumn.GearFor(Column.C3, ShiftDir.Fwd));
            Assert.Equal(1, byColumn.GearFor(Column.C3, ShiftDir.Back));
            Assert.Equal(4, byColumn.GearFor(Column.C1, ShiftDir.Fwd));
            Assert.False(byColumn.SlotExists(Column.C1, ShiftDir.Back));

            EngineConfig slots = Config();
            slots.MirrorSlots = true;
            GateGeometry bySlot = slots.BuildGeometry();

            // Fore/aft mirror: first above reverse, and the hole moves to the top of the last column.
            Assert.Equal(1, bySlot.GearFor(Column.C1, ShiftDir.Fwd));
            Assert.Equal(8, bySlot.GearFor(Column.C1, ShiftDir.Back));
            Assert.Equal(4, bySlot.GearFor(Column.C3, ShiftDir.Back));
            Assert.False(bySlot.SlotExists(Column.C3, ShiftDir.Fwd));
        }

        [Fact]
        public void ThePatternDefaultIsNoLockoutAndReverseCanStillBeGuarded()
        {
            EngineConfig cfg = Config();
            Assert.False(cfg.BuildGeometry().HasLockout);

            // The real box guards reverse; here that is the ordinary slot lockout pointed at R.
            cfg.LockoutPlacement = LockoutPlacement.Slot;
            cfg.LockoutSlotGear = 8;
            GateGeometry guarded = cfg.BuildGeometry();

            Assert.True(guarded.LockoutIsSlot);
            Assert.False(guarded.LockoutPlacementRepaired);
            Assert.Equal(Column.C1, guarded.LockoutSlotColumn);
            Assert.Equal(ShiftDir.Fwd, guarded.LockoutSlotDir);
        }

        [Fact]
        public void TheNextGearAlwaysLiesWhereTheZigZagSaysItDoes()
        {
            // The angled mouth steers toward the next sequential gear. From first that is across
            // to second, from third across to fourth; reverse and the gears' own column mates
            // have no cross-column neighbour on the outer side.
            GateGeometry geo = Config().BuildGeometry();

            Assert.Equal(1, geo.SequentialBias(Column.C1, ShiftDir.Back));   // 1 -> 2
            Assert.Equal(-1, geo.SequentialBias(Column.C2, ShiftDir.Fwd));   // 2 -> 1
            Assert.Equal(1, geo.SequentialBias(Column.C2, ShiftDir.Back));   // 3 -> 4
            Assert.Equal(-1, geo.SequentialBias(Column.C3, ShiftDir.Fwd));   // 4 -> 3
            Assert.Equal(0, geo.SequentialBias(Column.C1, ShiftDir.Fwd));    // R: nothing beyond it
        }

        [Fact]
        public void TheWallUnderFourthNeverOpens()
        {
            // The missing slot is rendered exactly as 6+R's missing seventh is: the fore/aft wall
            // over it stays as strong as squarely between two columns, while the slot above opens.
            EngineConfig cfg = Config();
            GateGeometry geo = cfg.BuildGeometry();
            var composer = new ForceComposer(geo, cfg);

            int last = geo.ColumnTarget(Column.C3);
            int between = (geo.ColumnTarget(Column.C1) + geo.ColumnTarget(Column.C2)) / 2;

            int closed = Math.Abs(composer.Compose(GateState.Neutral, Column.None, ShiftDir.None, last, Center + 3200).ConstantY);
            int wall = Math.Abs(composer.Compose(GateState.Neutral, Column.None, ShiftDir.None, between, Center + 3200).ConstantY);
            int open = Math.Abs(composer.Compose(GateState.Neutral, Column.None, ShiftDir.None, last, Center - 3200).ConstantY);

            Assert.Equal(wall, closed);
            Assert.True(open < closed / 4, "fourth should still open: " + open + " vs " + closed);
        }

        [Fact]
        public void TheStateMachineLatchesFiveGearsAndRefusesTheHole()
        {
            EngineConfig cfg = Config();
            GateGeometry geo = cfg.BuildGeometry();

            var expected = new[]
            {
                new { X = geo.ColumnTarget(Column.C1), Y = 2000, Gear = 8 },
                new { X = geo.ColumnTarget(Column.C1), Y = Max - 2000, Gear = 1 },
                new { X = geo.ColumnTarget(Column.C2), Y = 2000, Gear = 2 },
                new { X = geo.ColumnTarget(Column.C2), Y = Max - 2000, Gear = 3 },
                new { X = geo.ColumnTarget(Column.C3), Y = 2000, Gear = 4 },
                new { X = geo.ColumnTarget(Column.C3), Y = Max - 2000, Gear = 0 },
            };

            foreach (var slot in expected)
            {
                var sm = new GateStateMachine(geo, cfg.MinEngageTicks);
                sm.Update(slot.X, Center);
                for (int i = 0; i < 4; i++) sm.Update(slot.X, slot.Y);

                Assert.Equal(slot.Gear, sm.CurrentGear);
                Assert.Equal(slot.Gear == 0 ? GateState.Neutral : GateState.Engaged, sm.State);
            }

            // The hole refuses across the whole of its column's territory, not only on its line.
            int boundary = (geo.ColumnTarget(Column.C2) + geo.ColumnTarget(Column.C3)) / 2;
            for (int x = boundary + 1; x <= geo.ColumnTarget(Column.C3); x += 251)
            {
                var edge = new GateStateMachine(geo, cfg.MinEngageTicks);
                edge.Update(x, Center);
                for (int i = 0; i < 4; i++) edge.Update(x, Max);

                Assert.Equal(0, edge.CurrentGear);
                Assert.Equal(GateState.Neutral, edge.State);
            }
        }

        [Fact]
        public void OnlyFourGearsReverseAndNeutralReachTheOutput()
        {
            for (int button = 0; button < GearOutputConfig.RoleCount; button++)
            {
                bool expected = button == 0 || (button >= 1 && button <= 4) || button == 8;
                Assert.Equal(expected, GearOutputConfig.UsesButton(GatePattern.H4RDogLeg, button));
            }
        }

        [Fact]
        public void TheSettingsTreatItAsAnHPattern()
        {
            var settings = new ShifterSettings { Pattern = GatePattern.H4RDogLeg };

            Assert.True(settings.IsHPattern);
            Assert.True(settings.HasSlotDetent);
            Assert.False(settings.HasLateralLane);
            Assert.Equal((int)GatePattern.H4RDogLeg, settings.PatternIndex);
            Assert.Equal(GatePattern.H4RDogLeg, settings.ToEngineConfig().Pattern);
        }
    }
}
