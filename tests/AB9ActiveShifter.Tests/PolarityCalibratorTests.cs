using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Runs the calibrator against a simulated two-axis stick.
    ///
    /// Two cases matter most. A stick that holds itself centred is what makes asking the user
    /// useless, so the measurement has to see through it. And the real AB9 inverts different effect
    /// families on different axes, so each of the four targets has to be measured on its own.
    /// </summary>
    public class PolarityCalibratorTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int TickMs = 3;
        private const int MaxTicks = 6000;

        /// <summary>One axis with mass, damping, optional self-centring, and per-family inversion.</summary>
        private sealed class Axis
        {
            private const double AccelerationPerForce = 0.002;
            private const double Damping = 0.90;

            private readonly bool _invertConstant;
            private readonly bool _invertSpring;
            private readonly double _centering;
            private readonly double _mobility;
            private readonly int _rest;

            private double _position;
            private double _velocity;

            public Axis(bool invertConstant, bool invertSpring, double centering, double mobility, int rest)
            {
                _invertConstant = invertConstant;
                _invertSpring = invertSpring;
                _centering = centering;
                _mobility = mobility;
                _rest = rest;
                _position = rest;
            }

            public int Position
            {
                get { return GateGeometry.Clamp((int)Math.Round(_position), 0, GateGeometry.AxisMax); }
            }

            public void Step(int constant, SpringPreset spring)
            {
                double force = (_invertConstant ? -1 : 1) * constant;

                if (spring.PositiveCoefficient != 0 || spring.NegativeCoefficient != 0)
                {
                    double positionDi = GateGeometry.AxisToDi(Position);
                    double delta = positionDi - spring.Offset;
                    int coeff = delta >= 0 ? spring.PositiveCoefficient : spring.NegativeCoefficient;

                    double springForce = -delta * coeff / (double)GateGeometry.ForceMax;
                    double saturation = delta >= 0 ? spring.PositiveSaturation : spring.NegativeSaturation;
                    springForce = Math.Max(-saturation, Math.Min(saturation, springForce));

                    force += (_invertSpring ? -1 : 1) * springForce;
                }

                force += (_rest - _position) * _centering / AccelerationPerForce * 0.001;

                _velocity += force * AccelerationPerForce * _mobility;
                _velocity *= Damping;
                _position += _velocity;

                if (_position <= 0 || _position >= GateGeometry.AxisMax) _velocity = 0;
                _position = GateGeometry.Clamp(_position, 0, GateGeometry.AxisMax);
            }
        }

        private sealed class StickModel
        {
            public readonly Axis X;
            public readonly Axis Y;

            public StickModel(bool invertConstantX = false, bool invertConstantY = false,
                              bool invertSpringX = false, bool invertSpringY = false,
                              double centering = 0.0, double mobility = 1.0,
                              int restX = Center, int restY = Center)
            {
                X = new Axis(invertConstantX, invertSpringX, centering, mobility, restX);
                Y = new Axis(invertConstantY, invertSpringY, centering, mobility, restY);
            }

            public void Apply(ForceFrame f)
            {
                X.Step(f.ConstantX, f.SpringX);
                Y.Step(f.ConstantY, f.SpringY);
            }
        }

        private static CalibrationResult Run(CalibrationTarget target, StickModel stick, int probeForce = 2500)
        {
            var calibrator = new PolarityCalibrator(target, probeForce);
            long now = 0;

            for (int i = 0; i < MaxTicks && !calibrator.IsComplete; i++)
            {
                ForceFrame frame = calibrator.Step(stick.X.Position, stick.Y.Position, now);
                stick.Apply(frame);
                now += TickMs;
            }

            Assert.True(calibrator.IsComplete, "calibration did not finish");
            Assert.NotNull(calibrator.Result);
            return calibrator.Result;
        }

        public static TheoryData<CalibrationTarget> AllTargets()
        {
            return new TheoryData<CalibrationTarget>
            {
                CalibrationTarget.ConstantX,
                CalibrationTarget.ConstantY,
                CalibrationTarget.SpringX,
                CalibrationTarget.SpringY,
            };
        }

        [Theory]
        [MemberData(nameof(AllTargets))]
        public void DetectsCorrectPolarity(CalibrationTarget target)
        {
            Assert.Equal(CalibrationOutcome.Correct, Run(target, new StickModel()).Outcome);
        }

        [Theory]
        [MemberData(nameof(AllTargets))]
        public void DetectsInvertedPolarity(CalibrationTarget target)
        {
            var stick = new StickModel(invertConstantX: true, invertConstantY: true,
                                       invertSpringX: true, invertSpringY: true);
            Assert.Equal(CalibrationOutcome.Inverted, Run(target, stick).Outcome);
        }

        [Fact]
        public void MeasuresEachAxisAndFamilyIndependently()
        {
            // The pattern measured on the real AB9: constant force inverted on X but not on Y, and
            // the spring inverted on Y but not on X. A single global polarity flag cannot express
            // this, which is why there are four.
            Func<StickModel> stick = () => new StickModel(
                invertConstantX: true, invertConstantY: false,
                invertSpringX: false, invertSpringY: true);

            Assert.Equal(CalibrationOutcome.Inverted, Run(CalibrationTarget.ConstantX, stick()).Outcome);
            Assert.Equal(CalibrationOutcome.Correct, Run(CalibrationTarget.ConstantY, stick()).Outcome);
            Assert.Equal(CalibrationOutcome.Correct, Run(CalibrationTarget.SpringX, stick()).Outcome);
            Assert.Equal(CalibrationOutcome.Inverted, Run(CalibrationTarget.SpringY, stick()).Outcome);
        }

        [Fact]
        public void ProbingOneAxisDoesNotDisturbTheOther()
        {
            var stick = new StickModel();
            Run(CalibrationTarget.ConstantY, stick);

            Assert.InRange(stick.X.Position, Center - 500, Center + 500);
        }

        [Theory]
        [MemberData(nameof(AllTargets))]
        public void WorksWhileTheStickHoldsItselfCentred(CalibrationTarget target)
        {
            // The case that defeats a hands-on test: the base keeps pulling the stick back, so
            // "did it centre?" tells the user nothing. Measuring the deflection still does.
            var stick = new StickModel(centering: 0.08);
            Assert.Equal(CalibrationOutcome.Correct, Run(target, stick).Outcome);
        }

        [Theory]
        [MemberData(nameof(AllTargets))]
        public void ResistsAnOffCentreRestingBias(CalibrationTarget target)
        {
            // Probing both ways and scoring agreement cancels a stick that does not rest centred.
            var stick = new StickModel(centering: 0.05, restX: Center + 9000, restY: Center + 9000);
            Assert.Equal(CalibrationOutcome.Correct, Run(target, stick).Outcome);
        }

        [Theory]
        [MemberData(nameof(AllTargets))]
        public void ReportsInconclusiveWhenTheStickCannotMove(CalibrationTarget target)
        {
            // Clamped, or the base overwhelms the probe. Guessing would be worse than admitting it,
            // because this answer gates the force cap.
            var stick = new StickModel(mobility: 0.0002);

            CalibrationResult result = Run(target, stick);
            Assert.Equal(CalibrationOutcome.Inconclusive, result.Outcome);
            Assert.Contains("barely moved", result.Message);
        }

        [Theory]
        [InlineData(CalibrationTarget.SpringX)]
        [InlineData(CalibrationTarget.SpringY)]
        public void InvertedSpringIsCutShortInsteadOfRunningToTheStop(CalibrationTarget target)
        {
            // An inverted spring accelerates away from its anchor, so the probe has to stop as soon
            // as the sign is known rather than let the stick slam into its travel limit.
            var stick = new StickModel(invertSpringX: true, invertSpringY: true);
            bool isX = target == CalibrationTarget.SpringX;

            var calibrator = new PolarityCalibrator(target, 2500);
            long now = 0;
            int worst = 0;

            for (int i = 0; i < MaxTicks && !calibrator.IsComplete; i++)
            {
                ForceFrame frame = calibrator.Step(stick.X.Position, stick.Y.Position, now);
                stick.Apply(frame);

                int position = isX ? stick.X.Position : stick.Y.Position;
                worst = Math.Max(worst, Math.Abs(position - Center));
                now += TickMs;
            }

            Assert.Equal(CalibrationOutcome.Inverted, calibrator.Result.Outcome);
            Assert.True(worst < Center, "excursion " + worst + " reached the end of travel");
        }

        [Fact]
        public void AppliesNoForceOnceComplete()
        {
            var stick = new StickModel(centering: 0.05);
            var calibrator = new PolarityCalibrator(CalibrationTarget.ConstantY, 2500);

            long now = 0;
            while (!calibrator.IsComplete && now < MaxTicks * TickMs)
            {
                stick.Apply(calibrator.Step(stick.X.Position, stick.Y.Position, now));
                now += TickMs;
            }

            ForceFrame after = calibrator.Step(stick.X.Position, stick.Y.Position, now);
            Assert.Equal(0, after.ConstantX);
            Assert.Equal(0, after.ConstantY);
            Assert.Equal(0, after.SpringX.PositiveCoefficient);
            Assert.Equal(0, after.SpringY.PositiveCoefficient);
            Assert.Equal(0, after.DamperCoefficient);
        }

        [Theory]
        [MemberData(nameof(AllTargets))]
        public void ProbeForceNeverExceedsWhatWasAsked(CalibrationTarget target)
        {
            var stick = new StickModel(centering: 0.05);
            var calibrator = new PolarityCalibrator(target, 3000);

            long now = 0;
            while (!calibrator.IsComplete && now < MaxTicks * TickMs)
            {
                ForceFrame frame = calibrator.Step(stick.X.Position, stick.Y.Position, now);

                Assert.InRange(frame.ConstantX, -3000, 3000);
                Assert.InRange(frame.ConstantY, -3000, 3000);

                stick.Apply(frame);
                now += TickMs;
            }
        }
    }
}
