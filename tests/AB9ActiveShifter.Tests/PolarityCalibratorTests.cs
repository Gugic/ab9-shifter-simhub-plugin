using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Runs the calibrator against a simulated stick. The important case is a stick that holds
    /// itself centred: that is what makes asking the user useless, so the measurement has to work
    /// through it.
    /// </summary>
    public class PolarityCalibratorTests
    {
        private const int Center = GateGeometry.AxisCenter;
        private const int TickMs = 3;
        private const int MaxTicks = 4000;

        /// <summary>
        /// A stick with mass and damping, so forces accelerate it rather than teleporting it. The
        /// whole force response can be inverted to model the firmware bug, and the base's own
        /// centring spring can be dialled in independently.
        /// </summary>
        private sealed class StickModel
        {
            private const double AccelerationPerForce = 0.002;
            private const double Damping = 0.90;

            private readonly bool _invertConstant;
            private readonly bool _invertSpring;
            private readonly double _centeringStrength;
            private readonly double _mobility;
            private readonly int _restPosition;

            private double _position;
            private double _velocity;

            public StickModel(bool invertConstant, bool invertSpring, double centeringStrength,
                              double mobility = 1.0, int restPosition = Center)
            {
                _invertConstant = invertConstant;
                _invertSpring = invertSpring;
                _centeringStrength = centeringStrength;
                _mobility = mobility;
                _restPosition = restPosition;
                _position = restPosition;
            }

            public int Position { get { return GateGeometry.Clamp((int)Math.Round(_position), 0, GateGeometry.AxisMax); } }

            public void Apply(ForceFrame frame)
            {
                double force = (_invertConstant ? -1 : 1) * frame.ConstantY;

                SpringPreset s = frame.SpringY;
                if (s.PositiveCoefficient != 0 || s.NegativeCoefficient != 0)
                {
                    double positionDi = GateGeometry.AxisToDi(Position);
                    double delta = positionDi - s.Offset;
                    int coeff = delta >= 0 ? s.PositiveCoefficient : s.NegativeCoefficient;

                    // Restoring: force opposes displacement from the offset.
                    double springForce = -delta * coeff / (double)GateGeometry.ForceMax;
                    double saturation = delta >= 0 ? s.PositiveSaturation : s.NegativeSaturation;
                    springForce = Math.Max(-saturation, Math.Min(saturation, springForce));

                    force += (_invertSpring ? -1 : 1) * springForce;
                }

                // The base's own centring: what makes a hands-off human test useless.
                force += (_restPosition - _position) * _centeringStrength / AccelerationPerForce * 0.001;

                _velocity += force * AccelerationPerForce * _mobility;
                _velocity *= Damping;
                _position += _velocity;

                if (_position <= 0 || _position >= GateGeometry.AxisMax) _velocity = 0;
                _position = GateGeometry.Clamp(_position, 0, GateGeometry.AxisMax);
            }
        }

        private static CalibrationResult Run(CalibrationTarget target, StickModel stick, int probeForce = 2500)
        {
            var calibrator = new PolarityCalibrator(target, probeForce);
            long now = 0;

            for (int i = 0; i < MaxTicks && !calibrator.IsComplete; i++)
            {
                ForceFrame frame = calibrator.Step(stick.Position, now);
                stick.Apply(frame);
                now += TickMs;
            }

            Assert.True(calibrator.IsComplete, "calibration did not finish within " + MaxTicks + " ticks");
            Assert.NotNull(calibrator.Result);
            return calibrator.Result;
        }

        [Theory]
        [InlineData(CalibrationTarget.Constant)]
        [InlineData(CalibrationTarget.Spring)]
        public void DetectsCorrectPolarityOnAFreeStick(CalibrationTarget target)
        {
            var stick = new StickModel(invertConstant: false, invertSpring: false, centeringStrength: 0.0);
            Assert.Equal(CalibrationOutcome.Correct, Run(target, stick).Outcome);
        }

        [Theory]
        [InlineData(CalibrationTarget.Constant)]
        [InlineData(CalibrationTarget.Spring)]
        public void DetectsInvertedPolarityOnAFreeStick(CalibrationTarget target)
        {
            var stick = new StickModel(invertConstant: true, invertSpring: true, centeringStrength: 0.0);
            Assert.Equal(CalibrationOutcome.Inverted, Run(target, stick).Outcome);
        }

        [Theory]
        [InlineData(CalibrationTarget.Constant, false, CalibrationOutcome.Correct)]
        [InlineData(CalibrationTarget.Constant, true, CalibrationOutcome.Inverted)]
        [InlineData(CalibrationTarget.Spring, false, CalibrationOutcome.Correct)]
        [InlineData(CalibrationTarget.Spring, true, CalibrationOutcome.Inverted)]
        public void WorksWhileTheStickHoldsItselfCentred(CalibrationTarget target, bool inverted, CalibrationOutcome expected)
        {
            // The case that defeats a hands-on test: the base keeps pulling the stick back to centre,
            // so "does it centre?" tells the user nothing. Measuring the deflection still does.
            var stick = new StickModel(inverted, inverted, centeringStrength: 0.08);
            Assert.Equal(expected, Run(target, stick).Outcome);
        }

        [Theory]
        [InlineData(CalibrationTarget.Constant)]
        [InlineData(CalibrationTarget.Spring)]
        public void ResistsAnOffCentreRestingBias(CalibrationTarget target)
        {
            // Probing both directions and subtracting cancels a stick that does not rest at centre.
            var stick = new StickModel(invertConstant: false, invertSpring: false,
                                       centeringStrength: 0.05, restPosition: Center + 9000);
            Assert.Equal(CalibrationOutcome.Correct, Run(target, stick).Outcome);
        }

        [Theory]
        [InlineData(CalibrationTarget.Constant)]
        [InlineData(CalibrationTarget.Spring)]
        public void ReportsInconclusiveWhenTheStickCannotMove(CalibrationTarget target)
        {
            // Clamped, or the base's own spring is far stronger than the probe. Guessing here would
            // be worse than admitting it, because the answer gates the force cap.
            var stick = new StickModel(invertConstant: false, invertSpring: false,
                                       centeringStrength: 0.0, mobility: 0.0002);

            CalibrationResult result = Run(target, stick);
            Assert.Equal(CalibrationOutcome.Inconclusive, result.Outcome);
            Assert.Contains("barely moved", result.Message);
        }

        [Fact]
        public void InvertedSpringIsCutShortInsteadOfRunningToTheStop()
        {
            // An inverted spring accelerates away from its anchor, so the probe has to stop as soon
            // as the sign is known rather than let the stick slam into its travel limit.
            var stick = new StickModel(invertConstant: false, invertSpring: true, centeringStrength: 0.0);
            var calibrator = new PolarityCalibrator(CalibrationTarget.Spring, 2500);

            long now = 0;
            int worstExcursion = 0;

            for (int i = 0; i < MaxTicks && !calibrator.IsComplete; i++)
            {
                ForceFrame frame = calibrator.Step(stick.Position, now);
                stick.Apply(frame);
                worstExcursion = Math.Max(worstExcursion, Math.Abs(stick.Position - Center));
                now += TickMs;
            }

            Assert.Equal(CalibrationOutcome.Inverted, calibrator.Result.Outcome);
            Assert.True(stick.Position > 0 && stick.Position < GateGeometry.AxisMax,
                "the stick was driven into a stop; the early abort did not fire");
            Assert.True(worstExcursion < Center,
                "excursion " + worstExcursion + " reached the end of travel");
        }

        [Fact]
        public void AppliesNoForceOnceComplete()
        {
            var stick = new StickModel(invertConstant: false, invertSpring: false, centeringStrength: 0.05);
            var calibrator = new PolarityCalibrator(CalibrationTarget.Constant, 2500);

            long now = 0;
            while (!calibrator.IsComplete && now < MaxTicks * TickMs)
            {
                stick.Apply(calibrator.Step(stick.Position, now));
                now += TickMs;
            }

            ForceFrame after = calibrator.Step(stick.Position, now);
            Assert.Equal(0, after.ConstantX);
            Assert.Equal(0, after.ConstantY);
            Assert.Equal(0, after.SpringX.PositiveCoefficient);
            Assert.Equal(0, after.SpringY.PositiveCoefficient);
        }

        [Fact]
        public void ProbeForceNeverExceedsWhatWasAsked()
        {
            var stick = new StickModel(invertConstant: false, invertSpring: false, centeringStrength: 0.05);
            var calibrator = new PolarityCalibrator(CalibrationTarget.Constant, 3000);

            long now = 0;
            while (!calibrator.IsComplete && now < MaxTicks * TickMs)
            {
                ForceFrame frame = calibrator.Step(stick.Position, now);
                Assert.InRange(frame.ConstantY, -3000, 3000);
                stick.Apply(frame);
                now += TickMs;
            }
        }
    }
}
