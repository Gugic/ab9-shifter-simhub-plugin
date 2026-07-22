using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Checks the shape of the forces, especially the lockout, which is the whole point of
    /// the plugin and the part a user cannot easily verify except by feel.
    /// </summary>
    public class ForceComposerTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int Max = GateGeometry.AxisMax;
        private const int C3 = 43690;
        private const int C4 = 65535;

        private static EngineConfig FullGainConfig()
        {
            return new EngineConfig { OverallGainPct = 100, PolarityConfirmed = true };
        }

        private static ForceComposer Composer(EngineConfig cfg)
        {
            return new ForceComposer(cfg.BuildGeometry(), cfg);
        }

        [Fact]
        public void NoLockoutForceBeforeTheBoundary()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            Assert.Equal(0, c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C3, Center).ConstantX);
            Assert.Equal(0, c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 47999, Center).ConstantX);
        }

        [Fact]
        public void LockoutRampsToThePlateauAndHoldsIt()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            // 70% of full scale, pushing left (negative X).
            const int expectedPlateau = -7000;

            Assert.Equal(0, c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 48000, Center).ConstantX);

            int midRamp = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 49250, Center).ConstantX;
            Assert.InRange(midRamp, -3600, -3400);

            Assert.Equal(expectedPlateau,
                c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 50500, Center).ConstantX);
            Assert.Equal(expectedPlateau,
                c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center).ConstantX);
        }

        [Fact]
        public void LockoutStopsFightingOnceTheGearIsSelected()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            // Holding against the lockout in the C4 channel: still pushing back.
            Assert.True(c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center).ConstantX < 0);

            // Slotted in 7 and in R: the column wall holds the stick, no lockout push.
            Assert.Equal(0, c.Compose(GateState.Traveling, Column.C4, ShiftDir.Fwd, C4, 20000).ConstantX);
            Assert.Equal(0, c.Compose(GateState.Engaged, Column.C4, ShiftDir.Fwd, C4, 0).ConstantX);
            Assert.Equal(0, c.Compose(GateState.Engaged, Column.C4, ShiftDir.Back, C4, Max).ConstantX);
        }

        [Fact]
        public void LockoutScalesWithOverallGain()
        {
            EngineConfig half = new EngineConfig { OverallGainPct = 50, PolarityConfirmed = true };
            Assert.Equal(-3500,
                Composer(half).Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center).ConstantX);
        }

        [Fact]
        public void GainIsCappedUntilPolarityIsConfirmed()
        {
            EngineConfig unconfirmed = new EngineConfig { OverallGainPct = 100, PolarityConfirmed = false };
            Assert.Equal(0.10, unconfirmed.EffectiveGain, 3);

            // 70% of full scale at the 10% safety cap.
            Assert.Equal(-700,
                Composer(unconfirmed).Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center).ConstantX);
        }

        [Fact]
        public void InvertingConstantXFlipsTheLockout()
        {
            // The lockout is a constant force on X, so it follows the X constant flag alone.
            EngineConfig cfg = FullGainConfig();
            cfg.InvertConstantX = true;

            Assert.Equal(7000,
                Composer(cfg).Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center).ConstantX);
        }

        [Fact]
        public void InvertingConstantYFlipsTheDetentButNotTheLockout()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.InvertConstantY = true;
            ForceComposer c = Composer(cfg);

            Assert.Equal(1600, c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 21845, 0).ConstantY);
            Assert.Equal(-7000, c.Compose(GateState.Neutral, Column.None, ShiftDir.None, C4, Center).ConstantX);
        }

        [Fact]
        public void SpringInversionIsPerAxis()
        {
            // This base inverts the spring on one axis and not the other, so the flags have to act
            // independently: the X flag must not touch the channel spring, and vice versa.
            EngineConfig cfg = FullGainConfig();
            cfg.InvertSpringX = true;
            ForceComposer c = Composer(cfg);

            ForceFrame engaged = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 21845, 0);
            Assert.Equal(-8000, engaged.SpringX.PositiveCoefficient);

            ForceFrame neutral = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 32767, Center);
            Assert.Equal(9500, neutral.SpringY.PositiveCoefficient);
        }

        [Fact]
        public void FreeStickReleasesEverything()
        {
            EngineConfig cfg = FullGainConfig();
            cfg.FreeStick = true;
            ForceComposer c = Composer(cfg);

            // Whatever the gate would otherwise be doing, nothing goes to the device.
            foreach (GateState state in new[] { GateState.Neutral, GateState.Traveling, GateState.Engaged })
            {
                ForceFrame f = c.Compose(state, Column.C4, ShiftDir.Back, C4, Max);
                Assert.Equal(0, f.ConstantX);
                Assert.Equal(0, f.ConstantY);
                Assert.Equal(0, f.SpringX.PositiveCoefficient);
                Assert.Equal(0, f.SpringX.NegativeCoefficient);
                Assert.Equal(0, f.SpringY.PositiveCoefficient);
                Assert.Equal(0, f.SpringY.NegativeCoefficient);
                Assert.Equal(0, f.DamperCoefficient);
            }
        }

        [Fact]
        public void MirroringRelabelsGearsWithoutMovingTheGate()
        {
            // Layout preference must not touch geometry: the columns stay where the device puts
            // them, only the names change. Anything else would mirror the spring anchors too.
            var plain = new EngineConfig();
            var mirrored = new EngineConfig { MirrorColumns = true };

            Assert.Equal(plain.BuildGeometry().ColumnTarget(Column.C1),
                         mirrored.BuildGeometry().ColumnTarget(Column.C1));

            Assert.Equal(1, GateGeometry.GearOf(Column.C1, ShiftDir.Fwd));
            Assert.Equal(7, GateGeometry.GearOf(Column.C1, ShiftDir.Fwd, mirrorColumns: true));
            Assert.Equal(8, GateGeometry.GearOf(Column.C1, ShiftDir.Back, mirrorColumns: true));
            Assert.Equal(2, GateGeometry.GearOf(Column.C1, ShiftDir.Fwd, mirrorSlots: true));
        }

        [Fact]
        public void DetentResistsOnTheWayInThenPullsIntoTheSlot()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            // Just out of the channel, heading forward: pushes back toward neutral (+Y).
            int early = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Fwd, 21845, 28000).ConstantY;
            Assert.True(early > 0, "expected resistance early in the travel, got " + early);

            // Deep in the slot: pulls further forward (-Y) and holds.
            int seated = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Fwd, 21845, 0).ConstantY;
            Assert.Equal(-1600, seated);
        }

        [Fact]
        public void DetentMirrorsForBackwardSlots()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            int early = c.Compose(GateState.Traveling, Column.C2, ShiftDir.Back, 21845, 37000).ConstantY;
            Assert.True(early < 0, "expected resistance toward neutral (-Y), got " + early);

            int seated = c.Compose(GateState.Engaged, Column.C2, ShiftDir.Back, 21845, Max).ConstantY;
            Assert.Equal(1600, seated);
        }

        [Fact]
        public void ChannelIsWalledBetweenColumnsAndOpenOnThem()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            int onColumn = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 21845, Center)
                            .SpringY.PositiveCoefficient;
            int betweenColumns = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 32767, Center)
                                  .SpringY.PositiveCoefficient;

            Assert.Equal(600, onColumn);
            Assert.Equal(9500, betweenColumns);
            Assert.True(betweenColumns > onColumn * 5, "the wall must be decisively stiffer than the guide");
        }

        [Fact]
        public void ChannelSpringIsOutOfTheWayWhileInAColumn()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            SpringPreset y = c.Compose(GateState.Traveling, Column.C1, ShiftDir.Fwd, 0, 20000).SpringY;
            Assert.Equal(0, y.PositiveCoefficient);
            Assert.Equal(0, y.NegativeCoefficient);
        }

        [Fact]
        public void ColumnWallPinsToTheCorrectSide()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            int left = c.Compose(GateState.Engaged, Column.C1, ShiftDir.Fwd, 0, 0).SpringX.Offset;
            int right = c.Compose(GateState.Engaged, Column.C4, ShiftDir.Fwd, C4, 0).SpringX.Offset;

            Assert.Equal(-10000, left);
            Assert.Equal(10000, right);
        }

        [Fact]
        public void NeutralDetentHasHysteresisBetweenColumns()
        {
            EngineConfig cfg = FullGainConfig();
            ForceComposer c = Composer(cfg);

            // Settle on C2, then creep just past the C2/C3 midpoint: the detent should
            // stay on C2 until the stick is clearly closer to C3.
            c.Compose(GateState.Neutral, Column.None, ShiftDir.None, 21845, Center);

            int justPastMidpoint = (21845 + 43690) / 2 + 200;
            int offset = c.Compose(GateState.Neutral, Column.None, ShiftDir.None, justPastMidpoint, Center)
                          .SpringX.Offset;

            Assert.Equal(GateGeometry.AxisToDi(21845), offset);
        }
    }
}
