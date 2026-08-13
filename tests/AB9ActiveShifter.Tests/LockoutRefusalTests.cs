using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The hard lockout's refusal: a locked gear never latches and no button fires until the
    /// key turns. It rides the grind's own allowEngage path, which is what bounds it - refusal
    /// can block a new latch and nothing else. A gear already held is never dropped, an exit
    /// toll never refuses a release, and the moment the hotkey releases the gate the pending
    /// gear latches like any blocked engagement always has.
    /// </summary>
    public class LockoutRefusalTests
    {
        private const int Center = GateGeometry.AxisCenter;

        private static EngineConfig HardGap(LockoutGapDirection direction)
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                LockoutMode = LockoutMode.HotkeyToggle,
                LockoutGapDirection = direction
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        /// <summary>One composed tick, so the lockout state steps; the frame is discarded.</summary>
        private static void Tick(ForceComposer c, int x, int y, bool released = false)
        {
            c.Compose(GateState.Neutral, Column.None, ShiftDir.None, x, y, lockoutReleased: released);
        }

        [Fact]
        public void AHardLockoutRefusesTheGearUntilReleased()
        {
            // One-way: the refused side is fixed by the direction dial, whatever the lever
            // does. TowardHigh guards everything past the gate - both of the last column's
            // slots - and nothing on the near side.
            EngineConfig cfg = HardGap(LockoutGapDirection.TowardHigh);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            Tick(c, geo.ColumnTarget(Column.C2), Center);

            Assert.True(c.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));
            Assert.True(c.LockoutRefusesEngage(Column.C4, ShiftDir.Back));
            Assert.False(c.LockoutRefusesEngage(Column.C3, ShiftDir.Fwd));
            Assert.False(c.LockoutRefusesEngage(Column.C1, ShiftDir.Back));

            // The key in the gate: nothing is refused while released.
            Tick(c, geo.ColumnTarget(Column.C2), Center, released: true);
            Assert.False(c.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));

            // And a push-through gate never refuses anything - force is its whole answer.
            EngineConfig soft = HardGap(LockoutGapDirection.TowardHigh);
            soft.LockoutMode = LockoutMode.PushThrough;
            ForceComposer s = Composer(soft);
            Tick(s, geo.ColumnTarget(Column.C2), Center);
            Assert.False(s.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));
        }

        [Fact]
        public void AnOverpoweredBothGateStaysRefusedUntilTheKeyTurns()
        {
            // The refusal's home side is captured when the key turns, not read off the live
            // latch: an overpowered crossing flips the latch the moment the band is exited,
            // and refusal keyed on it would lift exactly when the fight was won. Locked means
            // locked - the crossed-to side becomes home only when the gate re-engages.
            EngineConfig cfg = HardGap(LockoutGapDirection.Both);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int left = geo.LockoutCentre - geo.LockoutHalfWidth - 400;
            int right = geo.LockoutCentre + geo.LockoutHalfWidth + 400;

            Tick(c, left, Center);
            Assert.Equal(-1, c.LockoutSideLatch);
            Assert.True(c.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));
            Assert.False(c.LockoutRefusesEngage(Column.C3, ShiftDir.Fwd));

            // Fight all the way across: the latch flips, the refusal does not.
            for (int x = left; x <= right; x += 50) Tick(c, x, Center);
            Assert.Equal(1, c.LockoutSideLatch);
            Assert.Equal(-1, c.LockoutPermittedSide);
            Assert.True(c.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));

            // Release, re-engage where the lever now lives: the far side becomes home and the
            // old side is the locked one.
            Tick(c, right, Center, released: true);
            Tick(c, right, Center);
            Assert.False(c.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));
            Assert.True(c.LockoutRefusesEngage(Column.C1, ShiftDir.Fwd));
        }

        [Fact]
        public void ARefusedGearLatchesTheTickTheHotkeyReleasesIt()
        {
            // The grind's own contract, re-used: a blocked engagement is pending, not dead.
            // Held against the balk with the gate locked, nothing latches however long the
            // shove lasts; the release makes the very same position latch.
            EngineConfig cfg = HardGap(LockoutGapDirection.TowardHigh);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);
            var sm = new GateStateMachine(geo, cfg.MinEngageTicks);

            int x = geo.ColumnTarget(Column.C4);
            int deep = 2000;

            sm.Update(x, Center, true);
            Tick(c, x, Center);

            for (int i = 0; i < 25; i++)
            {
                Tick(c, x, deep);
                sm.Update(x, deep, !c.LockoutRefusesEngage(sm.Column, sm.Direction));
            }

            Assert.Equal(0, sm.CurrentGear);

            for (int i = 0; i < 25; i++)
            {
                Tick(c, x, deep, released: true);
                sm.Update(x, deep, !c.LockoutRefusesEngage(sm.Column, sm.Direction));
            }

            Assert.Equal(7, sm.CurrentGear);
        }

        [Fact]
        public void AHardLockoutNeverDropsAGearAlreadyHeld()
        {
            // Refusal gates new latches only. Re-engaging the gate over a gear taken while it
            // was released must not turn that gear into a phantom: the lever has not moved,
            // and a release goes through the neutral channel absolutely.
            EngineConfig cfg = HardGap(LockoutGapDirection.TowardHigh);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);
            var sm = new GateStateMachine(geo, cfg.MinEngageTicks);

            int x = geo.ColumnTarget(Column.C4);
            int deep = 2000;

            sm.Update(x, Center, true);
            for (int i = 0; i < 25; i++)
            {
                Tick(c, x, deep, released: true);
                sm.Update(x, deep, !c.LockoutRefusesEngage(sm.Column, sm.Direction));
            }
            Assert.Equal(7, sm.CurrentGear);

            for (int i = 0; i < 25; i++)
            {
                Tick(c, x, deep);
                sm.Update(x, deep, !c.LockoutRefusesEngage(sm.Column, sm.Direction));
            }
            Assert.Equal(7, sm.CurrentGear);
        }

        [Fact]
        public void AHardSlotRefusesItsOwnGearAndNothingElse()
        {
            // Slot placement: the guard is one slot, found through the gear map. Its neighbour
            // in the same column stays free - locking 7 does not lock R - and an exit toll
            // refuses nothing at all, because refusing a release would hold a button down
            // while the lever physically leaves.
            EngineConfig cfg = new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                LockoutPlacement = LockoutPlacement.Slot,
                LockoutSlotGear = 7,
                LockoutSlotDirection = LockoutSlotDirection.Entry,
                LockoutMode = LockoutMode.HotkeyToggle
            };
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            Tick(c, geo.ColumnTarget(Column.C2), Center);

            Assert.True(c.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));
            Assert.False(c.LockoutRefusesEngage(Column.C4, ShiftDir.Back));
            Assert.False(c.LockoutRefusesEngage(Column.C3, ShiftDir.Fwd));

            EngineConfig exit = new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                LockoutPlacement = LockoutPlacement.Slot,
                LockoutSlotGear = 7,
                LockoutSlotDirection = LockoutSlotDirection.Exit,
                LockoutMode = LockoutMode.HotkeyToggle
            };
            ForceComposer e = Composer(exit);
            Tick(e, geo.ColumnTarget(Column.C2), Center);
            Assert.False(e.LockoutRefusesEngage(Column.C4, ShiftDir.Fwd));
        }

        [Fact]
        public void TheSideLatchLandsOppositeOnlyAfterAFullCrossing()
        {
            // The auto re-arm's primitive: the engine re-engages when the latch lands opposite
            // the side the release was granted on, and the latch can only get there by fully
            // exiting the band on the far side - a poke into the band and a retreat leaves it
            // exactly where it was.
            EngineConfig cfg = HardGap(LockoutGapDirection.Both);
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer c = Composer(cfg);

            int left = geo.LockoutCentre - geo.LockoutHalfWidth - 400;
            int nearFarEdge = geo.LockoutCentre + geo.LockoutHalfWidth - 200;

            Tick(c, left, Center, released: true);
            Assert.Equal(-1, c.LockoutSideLatch);

            for (int x = left; x <= nearFarEdge; x += 50) Tick(c, x, Center, released: true);
            Assert.Equal(-1, c.LockoutSideLatch);

            for (int x = nearFarEdge; x >= left; x -= 50) Tick(c, x, Center, released: true);
            Assert.Equal(-1, c.LockoutSideLatch);

            for (int x = left; x <= geo.LockoutCentre + geo.LockoutHalfWidth + 100; x += 50)
            {
                Tick(c, x, Center, released: true);
            }
            Assert.Equal(1, c.LockoutSideLatch);
        }
    }
}
