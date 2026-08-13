using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The slot placement: one gear's mouth given a toll of its own. Push-through shapes are
    /// bands added onto the ordinary detent - the entry fight spent before the crossover so
    /// the snick arrives whole, the exit toll a band between crossover and seat so a seated
    /// gear rests free - and the hard mode is the grind's own balk at the pinned strength.
    /// All in the gate's frame, before the polarity signs, like every force test.
    /// </summary>
    public class SlotLockoutTests
    {
        private const int Center = GateGeometry.AxisCenter;

        private static EngineConfig SlotConfig(int gear, LockoutSlotDirection direction,
            LockoutMode mode = LockoutMode.PushThrough)
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                LockoutPlacement = LockoutPlacement.Slot,
                LockoutSlotGear = gear,
                LockoutSlotDirection = direction,
                LockoutMode = mode
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        /// <summary>Axis reading at this engage fraction, forward side.</summary>
        private static int FwdAt(EngineConfig cfg, double d)
        {
            return Center - (int)Math.Round(d * (Center - cfg.EngageDepth));
        }

        [Fact]
        public void ASlotLockoutRaisesTheEntryFightAndLeavesTheSnickAlone()
        {
            // Extra resistance into one gear, spent entirely before the crossover begins: the
            // point where the slot starts to pull - where a gear begins to feel taken - must
            // not move, and the snick must arrive whole.
            EngineConfig guarded = SlotConfig(7, LockoutSlotDirection.Entry);
            EngineConfig plain = SlotConfig(7, LockoutSlotDirection.Entry);
            plain.LockoutPlacement = LockoutPlacement.Off;

            ForceComposer g = Composer(guarded);
            ForceComposer p = Composer(plain);

            for (int i = 1; i <= 10; i++)
            {
                double d = i * 0.05;
                int with = g.SlotForceAt(ShiftDir.Fwd, FwdAt(guarded, d), muted: false, column: Column.C4);
                int without = p.SlotForceAt(ShiftDir.Fwd, FwdAt(plain, d), muted: false, column: Column.C4);
                Assert.True(with > without,
                    "no extra fight at d=" + d + ": " + with + " vs " + without);
            }

            for (double d = 0.56; d <= 1.2; d += 0.05)
            {
                Assert.Equal(
                    p.SlotForceAt(ShiftDir.Fwd, FwdAt(plain, d), muted: false, column: Column.C4),
                    g.SlotForceAt(ShiftDir.Fwd, FwdAt(guarded, d), muted: false, column: Column.C4));
            }
        }

        [Fact]
        public void AnExitLockoutTollsTheWayOutAndLeavesTheSeatFree()
        {
            // The exit toll is a band between the crossover and the seat, not a deeper hold:
            // leaving the gear crosses it and pays, while a seated gear rests exactly as it
            // would unguarded - a free region, not a permanent load pressing it into the stop.
            // (Which is also what makes arming a hard exit over a seated gear step-free.)
            EngineConfig guarded = SlotConfig(7, LockoutSlotDirection.Exit);
            EngineConfig plain = SlotConfig(7, LockoutSlotDirection.Exit);
            plain.LockoutPlacement = LockoutPlacement.Off;

            ForceComposer g = Composer(guarded);
            ForceComposer p = Composer(plain);

            // 70% of full scale at full gain: the toll's flat core, inside the final clamp -
            // where the pull is already deep, base plus toll saturates at full scale.
            foreach (double d in new[] { 0.66, 0.75, 0.89 })
            {
                int expected = GateGeometry.Clamp(
                    p.SlotForceAt(ShiftDir.Fwd, FwdAt(plain, d), muted: false, column: Column.C4) - 7000,
                    -GateGeometry.ForceMax, GateGeometry.ForceMax);

                Assert.Equal(expected,
                    g.SlotForceAt(ShiftDir.Fwd, FwdAt(guarded, d), muted: false, column: Column.C4));
            }

            // The seat, the landing, and everything before the crossover: untouched.
            foreach (double d in new[] { 0.0, 0.25, 0.50, 0.54, 0.96, 1.0, 1.1, 1.2 })
            {
                Assert.Equal(
                    p.SlotForceAt(ShiftDir.Fwd, FwdAt(plain, d), muted: false, column: Column.C4),
                    g.SlotForceAt(ShiftDir.Fwd, FwdAt(guarded, d), muted: false, column: Column.C4));
            }
        }

        [Fact]
        public void TheSlotLockoutFollowsTheMirroredGearMap()
        {
            // The guarded slot is found by inverting the gear map, so mirroring moves the toll
            // with the gear - never a device corner. Reverse mirrored lives bottom-left.
            EngineConfig cfg = SlotConfig(8, LockoutSlotDirection.Entry);
            cfg.MirrorColumns = true;
            ForceComposer c = Composer(cfg);

            EngineConfig plain = SlotConfig(8, LockoutSlotDirection.Entry);
            plain.MirrorColumns = true;
            plain.LockoutPlacement = LockoutPlacement.Off;
            ForceComposer p = Composer(plain);

            int y = Center + (int)Math.Round(0.35 * (Center - cfg.EngageDepth));

            Assert.True(
                Math.Abs(c.SlotForceAt(ShiftDir.Back, y, muted: false, column: Column.C1))
                > Math.Abs(p.SlotForceAt(ShiftDir.Back, y, muted: false, column: Column.C1)),
                "the mirrored guard should sit on C1");

            Assert.Equal(
                p.SlotForceAt(ShiftDir.Back, y, muted: false, column: Column.C4),
                c.SlotForceAt(ShiftDir.Back, y, muted: false, column: Column.C4));
        }

        [Fact]
        public void NoSingleCountOfDepthEverStepsALockedSlotsStroke()
        {
            // The whole guarded stroke, both toll directions and the end-stop live at once,
            // every axis count: the detent, the entry band, the exit band and the stop must
            // meet nowhere in a step. The bound is the steepest face any of them declares.
            EngineConfig cfg = SlotConfig(7, LockoutSlotDirection.Both);
            cfg.EngageDepth = Center - 7000;
            cfg.ReleaseDepth = cfg.EngageDepth + 3000;
            cfg.SlotStopForcePct = 100;
            cfg.SlotOvertravel = 900;

            ForceComposer c = Composer(cfg);
            int span = Center - cfg.EngageDepth;

            int detentBound = (int)Math.Ceiling(
                Math.Max(cfg.SlotStopForcePct, cfg.DetentHoldPct) * 100 / (double)Math.Max(1, cfg.WallRamp));

            // The steepest lockout segment is the exit band's top face, 5% of the engage span.
            int boostBound = (int)Math.Ceiling(7000 / (0.05 * span));

            int bound = detentBound + boostBound + 2;

            int previous = c.SlotForceAt(ShiftDir.Fwd, Center, muted: false, column: Column.C4);
            for (int depth = 1; depth <= Center; depth++)
            {
                int force = c.SlotForceAt(ShiftDir.Fwd, Center - depth, muted: false, column: Column.C4);
                Assert.True(Math.Abs(force - previous) <= bound,
                    "stepped by " + Math.Abs(force - previous) + " at depth " + depth + ", bound " + bound);
                previous = force;
            }
        }

        [Fact]
        public void AHardSlotBalksLikeTheGrindAndTheTallerWallWins()
        {
            // A hard entry lockout is the grind's balk re-keyed: the detent becomes a border,
            // rendered by the identical curve. And when both are live at once the taller wall
            // wins - max, not sum - so one border, one attack, one yield floor.
            EngineConfig hard = SlotConfig(7, LockoutSlotDirection.Entry, LockoutMode.HotkeyToggle);
            hard.GrindWallPct = 30;

            EngineConfig grind = SlotConfig(7, LockoutSlotDirection.Entry);
            grind.LockoutPlacement = LockoutPlacement.Off;
            grind.GrindWallPct = 100;

            ForceComposer h = Composer(hard);
            ForceComposer g = Composer(grind);

            for (double d = 0.0; d <= 1.2; d += 0.1)
            {
                int viaLockout = h.SlotForceAt(ShiftDir.Fwd, FwdAt(hard, d), muted: false, column: Column.C4);
                int viaGrind = g.SlotForceAt(ShiftDir.Fwd, FwdAt(grind, d), muted: true, column: Column.C4);
                Assert.Equal(viaGrind, viaLockout);

                // Grind muted on top of the hard lockout: the 30% grind wall hides inside the
                // 100% gate instead of stacking a second border on it.
                int both = h.SlotForceAt(ShiftDir.Fwd, FwdAt(hard, d), muted: true, column: Column.C4);
                Assert.Equal(viaLockout, both);
            }
        }

        [Fact]
        public void TheGenericStrokeCurveShowsNoSlotsPrivateToll()
        {
            // Column.None is the Feel tab's generic stroke - the curve for "a slot", not the
            // guarded one - so the toll must not leak into it.
            EngineConfig cfg = SlotConfig(7, LockoutSlotDirection.Both);
            EngineConfig plain = SlotConfig(7, LockoutSlotDirection.Both);
            plain.LockoutPlacement = LockoutPlacement.Off;

            ForceComposer c = Composer(cfg);
            ForceComposer p = Composer(plain);

            for (double d = 0.0; d <= 1.2; d += 0.05)
            {
                Assert.Equal(
                    p.StrokeForceAt(ShiftDir.Fwd, FwdAt(plain, d), muted: false),
                    c.StrokeForceAt(ShiftDir.Fwd, FwdAt(cfg, d), muted: false));
            }
        }
    }
}
