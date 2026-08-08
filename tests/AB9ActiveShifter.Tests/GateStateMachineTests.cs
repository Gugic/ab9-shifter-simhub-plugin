using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Drives the gate with scripted stick positions. Coordinates are raw DirectInput
    /// counts: X grows right, Y grows toward the player, 32767 is centre.
    /// </summary>
    public class GateStateMachineTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int Max = GateGeometry.AxisMax;

        private const int C1 = 0;
        private const int C2 = 21845;
        private const int C3 = 43690;
        private const int C4 = 65535;

        private static readonly EngineConfig Config = new EngineConfig();

        private static GateStateMachine NewMachine()
        {
            return new GateStateMachine(Config.BuildGeometry(), Config.MinEngageTicks);
        }

        /// <summary>Feeds one position repeatedly so debounced transitions can settle.</summary>
        private static StateTransition Hold(GateStateMachine sm, int x, int y, int ticks = 4)
        {
            StateTransition t = default(StateTransition);
            for (int i = 0; i < ticks; i++) t = sm.Update(x, y);
            return t;
        }

        /// <summary>Moves the stick in a straight line, which is what a hand actually does.</summary>
        private static StateTransition Sweep(GateStateMachine sm, int fromX, int fromY, int toX, int toY, int steps = 40)
        {
            StateTransition t = default(StateTransition);
            for (int i = 1; i <= steps; i++)
            {
                int x = fromX + (toX - fromX) * i / steps;
                int y = fromY + (toY - fromY) * i / steps;
                t = sm.Update(x, y);
            }
            return t;
        }

        public static TheoryData<int, int, int> AllGears()
        {
            return new TheoryData<int, int, int>
            {
                { C1, 0,   1 },
                { C1, Max, 2 },
                { C2, 0,   3 },
                { C2, Max, 4 },
                { C3, 0,   5 },
                { C3, Max, 6 },
                { C4, 0,   7 },
                { C4, Max, 8 },
            };
        }

        [Theory]
        [MemberData(nameof(AllGears))]
        public void EveryGearEngagesAndReleases(int columnX, int slotY, int expectedGear)
        {
            GateStateMachine sm = NewMachine();

            // Slide across the neutral channel to the column, then into the slot.
            Sweep(sm, Center, Center, columnX, Center);
            StateTransition engaged = Sweep(sm, columnX, Center, columnX, slotY);
            Hold(sm, columnX, slotY);

            Assert.Equal(GateState.Engaged, sm.State);
            Assert.Equal(expectedGear, sm.CurrentGear);

            StateTransition released = Sweep(sm, columnX, slotY, columnX, Center);
            Assert.Equal(0, sm.CurrentGear);
            Assert.Equal(GateState.Neutral, sm.State);
            Assert.True(released.GearChanged || sm.CurrentGear == 0);
        }

        [Fact]
        public void ReverseIsBottomRight()
        {
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C4, Center);
            Sweep(sm, C4, Center, C4, Max);
            Hold(sm, C4, Max);

            Assert.Equal(8, sm.CurrentGear);
            Assert.Equal("R", new EngineConfig().BuildGeometry().LabelFor(sm.CurrentGear));
        }

        [Fact]
        public void SeventhIsTopRight()
        {
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C4, Center);
            Sweep(sm, C4, Center, C4, 0);
            Hold(sm, C4, 0);

            Assert.Equal(7, sm.CurrentGear);
            Assert.Equal("7", new EngineConfig().BuildGeometry().LabelFor(sm.CurrentGear));
        }

        [Fact]
        public void EngagementNeedsMoreThanOneTick()
        {
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C2, Center);
            Sweep(sm, C2, Center, C2, 5000);

            // First tick at engage depth must not yet select the gear.
            sm.Update(C2, 3000);
            Assert.Equal(0, sm.CurrentGear);

            sm.Update(C2, 3000);
            Assert.Equal(3, sm.CurrentGear);
        }

        [Fact]
        public void DitheringInsideTheHysteresisBandHoldsTheGear()
        {
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C2, Center);
            Sweep(sm, C2, Center, C2, 0);
            Hold(sm, C2, 0);
            Assert.Equal(3, sm.CurrentGear);

            // Engage threshold is 4000, release is 8000. Anything between must not toggle.
            int changes = 0;
            int[] wobble = { 4000, 6000, 7900, 4100, 7000, 4000, 7500 };
            foreach (int y in wobble)
            {
                if (sm.Update(C2, y).GearChanged) changes++;
            }

            Assert.Equal(0, changes);
            Assert.Equal(3, sm.CurrentGear);
        }

        [Fact]
        public void CrossingTheReleaseThresholdDropsTheGear()
        {
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C2, Center);
            Sweep(sm, C2, Center, C2, 0);
            Hold(sm, C2, 0);
            Assert.Equal(3, sm.CurrentGear);

            StateTransition t = sm.Update(C2, 9000);
            Assert.True(t.GearChanged);
            Assert.Equal(0, t.Gear);
            Assert.Equal(3, t.PreviousGear);
            Assert.Equal(GateState.Traveling, sm.State);
        }

        [Fact]
        public void PushingThroughTheLockoutReachesSeventh()
        {
            GateStateMachine sm = NewMachine();

            // Travel right along the neutral channel, through the lockout zone, to C4.
            Sweep(sm, C3, Center, C4, Center, 60);
            Assert.Equal(GateState.Neutral, sm.State);
            Assert.Equal(0, sm.CurrentGear);

            Sweep(sm, C4, Center, C4, 0);
            Hold(sm, C4, 0);
            Assert.Equal(7, sm.CurrentGear);
        }

        [Fact]
        public void StoppingShortOfTheGatesCrestCannotReachTheLockedColumn()
        {
            GateStateMachine sm = NewMachine();
            GateGeometry geo = Config.BuildGeometry();

            // Inside the gate's band and short of its crest: the toll is not paid, so this is
            // still 5/6's territory however hard the lever is pushed forward. It selects 5 - not
            // nothing, which is what it used to do and what made a full push land silently, and
            // emphatically not 7.
            int shortOfCrest = geo.LockoutCentre - geo.LockoutHalfWidth + 100;
            Sweep(sm, C3, Center, shortOfCrest, Center);
            Sweep(sm, shortOfCrest, Center, shortOfCrest, 0);
            Hold(sm, shortOfCrest, 0);

            Assert.Equal(5, sm.CurrentGear);

            // And nowhere short of the crest can reach 7/R at all - the property the gate exists
            // for. Ownership hands the locked column over at the gap's midpoint, and the geometry
            // keeps the crest on the main section's side of it.
            Assert.True(geo.LockoutCentre < (C3 + C4) / 2);

            for (int x = C3; x <= geo.LockoutCentre; x += 100)
            {
                Assert.NotEqual((Column)3, geo.ColumnAt(x));
            }
        }

        [Fact]
        public void NoLateralDistanceWhatsoeverReleasesTheGear()
        {
            // The lock is absolute, by design rather than by strength. Force cannot enforce it -
            // a hand beats 12 Nm - so any distance at which the latch gave way would be a
            // distance at which the rest of the pattern came back and could capture the lever
            // into a gear it was never driven into. Dragged the entire width of the gate while
            // deep in first, the gear must still be first.
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C1, Center);
            Sweep(sm, C1, Center, C1, 0);
            Hold(sm, C1, 0);
            Assert.Equal(1, sm.CurrentGear);

            for (int x = 0; x <= Max; x += 250)
            {
                StateTransition t = sm.Update(x, 0);
                Assert.Equal(1, t.Gear);
                Assert.False(t.GearChanged);
                Assert.Equal(GateState.Engaged, t.State);
            }

            // Only the tunnel gives it up.
            Sweep(sm, Max, 0, Max, Center);
            Assert.Equal(0, sm.CurrentGear);
            Assert.Equal(GateState.Neutral, sm.State);
        }

        [Fact]
        public void LeaningHardAgainstASlotWallKeepsTheGear()
        {
            // A gear is given up only by coming back through the neutral channel. A firm lean -
            // even one that pushes well past the band that used to release the gear - has to hold
            // it, or a gear falls out for no reason the hand can see. It also means the slot wall
            // no longer has to slam on inside that band, which is what made the slots oscillate.
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C2, Center);
            Sweep(sm, C2, Center, C2, 0);
            Hold(sm, C2, 0);
            Assert.Equal(3, sm.CurrentGear);

            foreach (int offset in new[] { 2500, 4000, 6000, -6000 })
            {
                StateTransition t = sm.Update(C2 + offset, 0);
                Assert.Equal(3, t.Gear);
                Assert.False(t.GearChanged);
            }

        }

        [Fact]
        public void ADiagonalDragCannotReachAnotherGear()
        {
            // The whole point of the lock: no route from one gear to another except through the
            // tunnel. Dragged sideways along the top of the pattern - all the way to where the
            // next column sits - the gear must neither change nor drop. It stays the gear the
            // lever is latched to, and the slot wall keeps pushing back toward it.
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C1, Center);
            Sweep(sm, C1, Center, C1, 0);
            Hold(sm, C1, 0);
            Assert.Equal(1, sm.CurrentGear);

            for (int x = C1; x <= C2; x += 500)
            {
                StateTransition t = sm.Update(x, 0);
                Assert.Equal(1, t.Gear);
            }

            Assert.Equal(1, sm.CurrentGear);

            // Coming back through the channel is the only way to hand the gear over.
            Sweep(sm, C2, 0, C2, Center);
            Assert.Equal(0, sm.CurrentGear);
            Sweep(sm, C2, Center, C2, 0);
            Hold(sm, C2, 0);
            Assert.Equal(3, sm.CurrentGear);
        }

        [Fact]
        public void SittingDeepInAnotherColumnStillReportsTheLatchedGear()
        {
            // Even a position that could only come from a sensor jump - straight into another
            // column's slot - must not hand over that column's gear. The latch is the truth
            // until the channel says otherwise; Resync is the only way to adopt a position.
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C1, Center);
            Sweep(sm, C1, Center, C1, 0);
            Hold(sm, C1, 0);

            sm.Update(C3, 0);
            Hold(sm, C3, 0);
            Assert.Equal(1, sm.CurrentGear);

            sm.Resync(C3, 0);
            Assert.Equal(5, sm.CurrentGear);
        }

        [Fact]
        public void LeavingTheChannelBetweenColumnsStillSelectsAGear()
        {
            // The fore/aft wall is what should stop this, and it is at full strength here - but
            // full strength is 12 Nm and a hand beats 12 Nm. Measured on the rig: two pushes to
            // full deflection, held for the best part of a second each, roughly 2400 counts off
            // the column, and the game was told nothing at all. Ownership means a push that gets
            // through always lands somewhere, and the somewhere is the column it is nearest.
            GateStateMachine sm = NewMachine();

            const int between = 32767;
            Sweep(sm, between, Center, between, 0);
            Hold(sm, between, 0);

            Assert.Equal(GateState.Engaged, sm.State);
            Assert.Equal(3, sm.CurrentGear);

            // A count either side of the boundary picks the column that side of it, and nothing
            // in between is ever left without an owner.
            GateGeometry geo = Config.BuildGeometry();
            for (int x = 0; x <= Max; x += 97)
            {
                Assert.NotEqual(Column.None, geo.ColumnAt(x));
            }
        }

        [Fact]
        public void APushAllTheWayHomeAlwaysSelectsAGear()
        {
            // A silent non-shift is the worst answer this gate can give, and it was giving it.
            // Trace-20260807-053318, two episodes: lever at FULL deflection, out of the channel,
            // held 896 ms and 616 ms, state Neutral, gear 0. The lever was about 2400 counts right
            // of 5/6 - past the old +-1200 selection band, resting on the lockout gate's entry
            // face, in a strip that belonged to nothing. Every position must select something.
            GateGeometry geo = Config.BuildGeometry();

            foreach (int slot in new[] { 0, Max })
            {
                for (int x = 0; x <= Max; x += 251)
                {
                    GateStateMachine sm = NewMachine();
                    Sweep(sm, x, Center, x, slot);
                    Hold(sm, x, slot);

                    int expected = geo.GearFor(geo.ColumnAt(x), geo.DirectionOf(slot));

                    Assert.Equal(GateState.Engaged, sm.State);
                    Assert.Equal(expected, sm.CurrentGear);
                }
            }
        }

        [Fact]
        public void TheMeasuredNonShiftNowLandsInSixth()
        {
            // The two positions the lever actually sat at in that trace, to the count: 2371 and
            // 3243 right of the 5/6 column, pushed fully back. Both used to give nothing.
            foreach (int offset in new[] { 2371, 3243 })
            {
                GateStateMachine sm = NewMachine();
                Sweep(sm, C3 + offset, Center, C3 + offset, Max);
                Hold(sm, C3 + offset, Max);

                Assert.Equal(6, sm.CurrentGear);
            }
        }

        [Fact]
        public void ResyncAdoptsAGearTheStickIsAlreadySittingIn()
        {
            GateStateMachine sm = NewMachine();
            sm.Resync(C3, Max);

            Assert.Equal(GateState.Engaged, sm.State);
            Assert.Equal(6, sm.CurrentGear);
        }

        [Fact]
        public void ResyncInTheChannelIsNeutral()
        {
            GateStateMachine sm = NewMachine();
            sm.Resync(C2, Center);

            Assert.Equal(GateState.Neutral, sm.State);
            Assert.Equal(0, sm.CurrentGear);
        }

        [Fact]
        public void ShiftingBetweenGearsPassesThroughNeutral()
        {
            GateStateMachine sm = NewMachine();

            Sweep(sm, Center, Center, C1, Center);
            Sweep(sm, C1, Center, C1, 0);
            Hold(sm, C1, 0);
            Assert.Equal(1, sm.CurrentGear);

            // 1 -> 2 is a straight pull through the channel within the same column.
            var gears = new System.Collections.Generic.List<int>();
            for (int i = 1; i <= 60; i++)
            {
                int y = 0 + (Max - 0) * i / 60;
                StateTransition t = sm.Update(C1, y);
                if (t.GearChanged) gears.Add(t.Gear);
            }
            Hold(sm, C1, Max);

            Assert.Equal(2, sm.CurrentGear);
            Assert.Contains(0, gears);          // released before re-engaging
        }

        [Fact]
        public void ABlockedEngagementNeverLatchesUntilAllowed()
        {
            // The grind's gear rejection: with allowEngage false the lever travels, presses,
            // sits at full depth - and no gear ever registers. The moment the block lifts
            // (the clutch goes down), engagement still takes the full debounce.
            GateStateMachine sm = NewMachine();
            Sweep(sm, Center, Center, C2, Center);

            for (int i = 0; i < 60; i++) sm.Update(C2, 1000, false);
            Assert.Equal(GateState.Traveling, sm.State);
            Assert.Equal(0, sm.CurrentGear);

            for (int i = 0; i < Config.MinEngageTicks; i++) sm.Update(C2, 1000, true);
            Assert.Equal(GateState.Engaged, sm.State);
            Assert.Equal(3, sm.CurrentGear);
        }
    }
}
