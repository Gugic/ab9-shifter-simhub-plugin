using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class BaseEffectComposerTests
    {
        [Theory]
        [InlineData(GatePattern.H7R)]
        [InlineData(GatePattern.Sequential)]
        [InlineData(GatePattern.Prnd)]
        public void DefaultsLeaveTheExistingGateExactlyAsComposed(GatePattern pattern)
        {
            var config = new EngineConfig { Pattern = pattern };
            ForceFrame gate = GateFrame(config);
            Assert.Equal(gate, BaseEffectComposer.Apply(gate, config));
        }

        [Fact]
        public void EveryConditionUsesTheUnconfirmedGainCap()
        {
            EngineConfig config = AllConditions();
            config.PolarityConfirmed = false;
            ForceFrame frame = BaseEffectComposer.Apply(ForceComposer.FreeFrame(), config);

            Assert.Equal(1000, frame.SpringX.PositiveCoefficient);
            Assert.Equal(1000, frame.SpringY.NegativeCoefficient);
            Assert.Equal(1000, frame.DamperCoefficient);
            Assert.Equal(1000, frame.FrictionCoefficient);
            Assert.Equal(1000, frame.InertiaCoefficient);
        }

        [Fact]
        public void SpringRequiresItsOwnMeasuredPolarity()
        {
            EngineConfig config = AllConditions();
            config.BaseSpringPolarityConfirmed = false;
            ForceFrame frame = BaseEffectComposer.Apply(ForceComposer.FreeFrame(), config);

            Assert.Equal(SpringPreset.Off, frame.SpringX);
            Assert.Equal(SpringPreset.Off, frame.SpringY);
            Assert.Equal(10000, frame.DamperCoefficient);
        }

        [Theory]
        [InlineData(false, false, 1, 1)]
        [InlineData(true, false, -1, 1)]
        [InlineData(false, true, 1, -1)]
        [InlineData(true, true, -1, -1)]
        public void SpringUsesItsOwnPerAxisSignsAndNeverTheConstantSigns(
            bool invertX, bool invertY, int signX, int signY)
        {
            EngineConfig config = AllConditions();
            config.BaseSpringPct = 40;
            config.OverallGainPct = 50;
            config.InvertSpringX = invertX;
            config.InvertSpringY = invertY;
            config.InvertConstantX = !invertX;
            config.InvertConstantY = !invertY;
            ForceFrame frame = BaseEffectComposer.Apply(ForceComposer.FreeFrame(), config);

            Assert.Equal(0, frame.SpringX.Offset);
            Assert.Equal(0, frame.SpringY.Offset);
            Assert.Equal(signX * 2000, frame.SpringX.PositiveCoefficient);
            Assert.Equal(signX * 2000, frame.SpringX.NegativeCoefficient);
            Assert.Equal(signY * 2000, frame.SpringY.PositiveCoefficient);
            Assert.Equal(signY * 2000, frame.SpringY.NegativeCoefficient);
        }

        [Theory]
        [InlineData(GatePattern.H7R)]
        [InlineData(GatePattern.Sequential)]
        [InlineData(GatePattern.Prnd)]
        public void OnboardModeOnlyRemovesDuplicatedBaseConditions(GatePattern pattern)
        {
            EngineConfig config = AllConditions();
            config.Pattern = pattern;
            ForceFrame gate = GateFrame(config);
            ForceFrame generic = BaseEffectComposer.Apply(gate, config);
            Assert.NotEqual(0, generic.DamperCoefficient);

            config.BaseEffectsViaDirectInput = false;
            ForceFrame native = BaseEffectComposer.Apply(generic, config);
            Assert.Equal(gate.ConstantX, native.ConstantX);
            Assert.Equal(gate.ConstantY, native.ConstantY);
            AssertConditionsOff(native);
        }

        [Fact]
        public void FreeStickClearsEveryPreviouslyRunningBaseCondition()
        {
            EngineConfig config = AllConditions();
            ForceFrame running = BaseEffectComposer.Apply(ForceComposer.FreeFrame(), config);
            config.FreeStick = true;
            AssertConditionsOff(BaseEffectComposer.Apply(running, config));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0.25, 2500)]
        [InlineData(1, 10000)]
        [InlineData(2, 10000)]
        [InlineData(-1, 0)]
        public void ProfileTransitionRampsTheSpringWithoutRampingPassiveResistance(double scale, int spring)
        {
            EngineConfig config = AllConditions();
            ForceFrame frame = BaseEffectComposer.Apply(ForceComposer.FreeFrame(), config, scale);
            Assert.Equal(spring, frame.SpringX.PositiveCoefficient);
            Assert.Equal(spring, frame.SpringY.PositiveCoefficient);
            Assert.Equal(10000, frame.DamperCoefficient);
            Assert.Equal(10000, frame.FrictionCoefficient);
            Assert.Equal(10000, frame.InertiaCoefficient);
        }

        [Theory]
        [InlineData(-50, 0)]
        [InlineData(500, 10000)]
        public void InvalidConditionValuesCannotExceedTheForceRange(int percent, int expected)
        {
            EngineConfig config = AllConditions();
            config.BaseSpringPct = config.BaseFrictionPct = config.BaseInertiaPct = percent;
            config.DamperCoeff = percent * 100;
            ForceFrame frame = BaseEffectComposer.Apply(ForceComposer.FreeFrame(), config);
            Assert.Equal(expected, frame.SpringX.PositiveCoefficient);
            Assert.Equal(expected, frame.SpringY.PositiveCoefficient);
            Assert.Equal(expected, frame.FrictionCoefficient);
            Assert.Equal(expected, frame.InertiaCoefficient);
            Assert.Equal(expected, frame.DamperCoefficient);
        }

        private static EngineConfig AllConditions()
        {
            return new EngineConfig
            {
                OverallGainPct = 100,
                PolarityConfirmed = true,
                BaseSpringPolarityConfirmed = true,
                BaseSpringPct = 100,
                BaseFrictionPct = 100,
                BaseInertiaPct = 100,
                DamperCoeff = 10000
            };
        }

        private static ForceFrame GateFrame(EngineConfig config)
        {
            var composer = new ForceComposer(config.BuildGeometry(), config);
            switch (config.Pattern)
            {
                case GatePattern.Sequential:
                    return composer.ComposeSequential(12000, 8000, 20000, -30000);
                case GatePattern.Prnd:
                    return composer.ComposePrnd(12000, 8000, 20000, -30000);
                default:
                    return composer.Compose(GateState.Traveling, Column.C1, ShiftDir.Fwd,
                        12000, 8000, 20000, -30000);
            }
        }

        private static void AssertConditionsOff(ForceFrame frame)
        {
            Assert.Equal(SpringPreset.Off, frame.SpringX);
            Assert.Equal(SpringPreset.Off, frame.SpringY);
            Assert.Equal(0, frame.DamperCoefficient);
            Assert.Equal(0, frame.FrictionCoefficient);
            Assert.Equal(0, frame.InertiaCoefficient);
        }
    }
}
