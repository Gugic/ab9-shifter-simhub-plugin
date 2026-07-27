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
        public void StoppingHalfwayThroughTheLockoutSelectsNothing()
        {
            GateStateMachine sm = NewMachine();

            // Past the lockout boundary (48000) but short of the C4 band (62935).
            const int halfway = 55000;
            Sweep(sm, C3, Center, halfway, Center);
            Sweep(sm, halfway, Center, halfway, 0);
            Hold(sm, halfway, 0);

            Assert.Equal(0, sm.CurrentGear);
            Assert.Equal(GateState.Neutral, sm.State);

            // Springing back left must leave nothing latched.
            Sweep(sm, halfway, 0, C3, Center);
            Assert.Equal(0, sm.CurrentGear);
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
        public void LeavingTheChannelBetweenColumnsSelectsNothing()
        {
            GateStateMachine sm = NewMachine();

            // Midway between C2 and C3 the Y wall should be resisting; nothing to select.
            const int between = 32767;
            Sweep(sm, between, Center, between, 0);
            Hold(sm, between, 0);

            Assert.Equal(0, sm.CurrentGear);
            Assert.Equal(GateState.Neutral, sm.State);
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
