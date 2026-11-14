using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// A bottomed slot under a centring spring. The free landing is only a resting place when
    /// nothing pushes the lever out of it; a centring spring does, so the lever comes to rest on
    /// the seated hold's fade instead - and that face must then take the walls' stabilisers, not
    /// the snick's exemptions, or a hard entry rings on it. In the gate's frame, before the
    /// polarity signs, like every force test.
    /// </summary>
    public class SlotSpringTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int Seat = 12006;

        private static EngineConfig Bottomed(double springPct)
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                EngageDepth = Center - Seat,
                ReleaseDepth = 35317,
                SlotOvertravel = 5022,
                SlotStopForcePct = 100,
                WallRamp = 3816,
                DetentHoldPct = 50,
                WallAttackMs = 15,
                BaseSpringPct = springPct,
                BaseSpringPolarityConfirmed = true
            };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        /// <summary>Axis reading this far past the seat of a forward slot.</summary>
        private static int PastSeat(int counts)
        {
            return Center - (Seat + counts);
        }

        private static int Column2X(EngineConfig cfg)
        {
            return cfg.BuildGeometry().ColumnTarget(Column.C2);
        }

        [Fact]
        public void ACentringSpringMovesTheRestOntoTheHoldsFade()
        {
            // The premise the fix rests on, pinned on the real force curve. With no spring the
            // lever rests in the free landing; a centring spring of 0.15-0.3 DI per count - well
            // inside what a generic spring or the AB9's onboard one produces - moves the rest
            // onto the hold's fade, between the seat and one wall bite past it, on a face
            // steeper than one DI per count.
            EngineConfig cfg = Bottomed(50);
            ForceComposer c = Composer(cfg);

            foreach (double k in new[] { 0.15, 0.3 })
            {
                int rest = -1;
                double previous = double.NaN;
                for (int past = -2000; past <= cfg.SlotOvertravel; past += 10)
                {
                    int depth = Seat + past;
                    double outward = c.SlotForceAt(ShiftDir.Fwd, PastSeat(past), false, Column.C2) + k * depth;
                    if (!double.IsNaN(previous) && previous < 0 && outward >= 0) { rest = past; break; }
                    previous = outward;
                }

                Assert.InRange(rest, 0, cfg.WallRamp);

                int below = c.SlotForceAt(ShiftDir.Fwd, PastSeat(rest - 200), false, Column.C2);
                int above = c.SlotForceAt(ShiftDir.Fwd, PastSeat(rest + 200), false, Column.C2);
                double gradient = (above - below) / 400.0 + k;
                Assert.True(gradient > 1.0, "the resting face is only " + gradient + " DI per count at k=" + k);
            }
        }

        [Fact]
        public void WithASpringTheFadeTakesTheAttack()
        {
            // Arriving on the fade from nothing, the force winds up instead of landing whole.
            EngineConfig cfg = Bottomed(50);
            ForceComposer c = Composer(cfg);
            int y = PastSeat(1500);

            int target = Math.Abs(c.SlotForceAt(ShiftDir.Fwd, y, false, Column.C2));
            int first = Math.Abs(Composer(cfg).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                Column2X(cfg), y, 0, 0, 1.0).ConstantY);

            Assert.True(target > 1000, "expected a real hold at this depth, got " + target);
            Assert.True(first < target, "the fade arrived whole: " + first + " of " + target);
        }

        [Fact]
        public void WithASpringALeverRestingOnTheFadeIsHeldStill()
        {
            // The static hold: pressed against the face and effectively still, small position
            // wobble freezes the force rather than tracking it - which is what quiets a lever
            // resting on a gradient too steep for the loop's delay.
            EngineConfig cfg = Bottomed(50);
            ForceComposer c = Composer(cfg);
            int x = Column2X(cfg);

            int settled = 0;
            for (int i = 0; i < 200; i++)
            {
                settled = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, PastSeat(1500), 0, 0, 1.0).ConstantY;
            }

            int wobble = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, PastSeat(1560), 0, 0, 1.0).ConstantY;
            Assert.Equal(settled, wobble);
        }

        [Fact]
        public void WithoutASpringTheSlotIsExactlyWhatItWas()
        {
            // No centring, no change: the lever rests in the free landing and the fade keeps
            // the snick's treatment, tick for tick.
            EngineConfig cfg = Bottomed(0);
            ForceComposer c = Composer(cfg);
            int y = PastSeat(1500);

            int target = c.SlotForceAt(ShiftDir.Fwd, y, false, Column.C2);
            int first = Composer(cfg).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                Column2X(cfg), y, 0, 0, 1.0).ConstantY;
            Assert.Equal(target, first);
        }

        [Fact]
        public void ASpringTheBaseNeverReceivesChangesNothing()
        {
            // The dial alone is not a spring. DirectInput renders it only once the spring signs
            // are measured, so an unmeasured spring pushes nothing out of the landing and the
            // fade keeps the snick's treatment exactly as with the dial at zero.
            EngineConfig cfg = Bottomed(50);
            cfg.BaseSpringPolarityConfirmed = false;
            int y = PastSeat(1500);

            int target = Composer(cfg).SlotForceAt(ShiftDir.Fwd, y, false, Column.C2);
            int first = Composer(cfg).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                Column2X(cfg), y, 0, 0, 1.0).ConstantY;
            Assert.Equal(target, first);
        }

        [Fact]
        public void TheAb9sOnboardSpringCountsOnceItIsWritten()
        {
            // In Moza AB9 mode the spring is written onboard, gated on confirmed polarity rather
            // than on the DirectInput spring signs; that spring centres the lever just the same.
            EngineConfig onboard = Bottomed(50);
            onboard.BaseEffectsViaDirectInput = false;
            onboard.BaseSpringPolarityConfirmed = false;
            int y = PastSeat(1500);

            int target = Math.Abs(Composer(onboard).SlotForceAt(ShiftDir.Fwd, y, false, Column.C2));
            int first = Math.Abs(Composer(onboard).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                Column2X(onboard), y, 0, 0, 1.0).ConstantY);
            Assert.True(first < target, "the onboard spring did not move the fade to the walls' treatment");

            onboard.PolarityConfirmed = false;
            onboard.OverallGainPct = 100;
            int capped = Composer(onboard).SlotForceAt(ShiftDir.Fwd, y, false, Column.C2);
            Assert.Equal(capped, Composer(onboard).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd,
                Column2X(onboard), y, 0, 0, 1.0).ConstantY);
        }

        [Fact]
        public void TheSnickStillArrivesWholeWithASpring()
        {
            // Before the seat is the crossover, and the snick must arrive whole whatever the
            // base is set to - only the region past the seat changes.
            EngineConfig sprung = Bottomed(50);
            EngineConfig plain = Bottomed(0);
            int x = Column2X(sprung);

            foreach (int before in new[] { -3000, -1500, -200 })
            {
                int y = PastSeat(before);
                Assert.Equal(
                    Composer(plain).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, y, 0, 0, 1.0).ConstantY,
                    Composer(sprung).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, y, 0, 0, 1.0).ConstantY);
            }
        }

        [Fact]
        public void AnUnbottomedSlotIsUntouched()
        {
            // With no slot stop the hold pulls on to the mechanical stop and there is no fade
            // to rest on; the spring changes nothing there.
            EngineConfig sprung = Bottomed(50);
            sprung.SlotStopForcePct = 0;
            EngineConfig plain = Bottomed(0);
            plain.SlotStopForcePct = 0;
            int x = Column2X(sprung);
            int y = PastSeat(1500);

            Assert.Equal(
                Composer(plain).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, y, 0, 0, 1.0).ConstantY,
                Composer(sprung).Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, y, 0, 0, 1.0).ConstantY);
        }

        [Fact]
        public void CrossingTheSeatAtSpeedDoesNotStepTheForce()
        {
            // The handover from the snick's passthrough to the wall's shaping keeps the shaping
            // state continuous, so a hard entry crossing the seat is not met by a step.
            EngineConfig cfg = Bottomed(50);
            ForceComposer c = Composer(cfg);
            int x = Column2X(cfg);

            int previous = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, PastSeat(-600), 0, -60000, 1.0).ConstantY;
            for (int past = -540; past <= 600; past += 60)
            {
                int force = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, x, PastSeat(past), 0, -60000, 1.0).ConstantY;
                Assert.True(Math.Abs(force - previous) <= 1500,
                    "stepped by " + Math.Abs(force - previous) + " at " + past + " past the seat");
                previous = force;
            }
        }
    }
}
