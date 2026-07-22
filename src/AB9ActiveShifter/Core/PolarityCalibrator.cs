using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// One effect family on one axis. Measured separately because the AB9 does not treat them
    /// alike: on the base this was developed against, constant force is inverted on X but not on
    /// Y, while the spring is inverted on Y but not on X. A single global polarity flag cannot
    /// express that, and guessing it wrong sends the gate the wrong way.
    /// </summary>
    public enum CalibrationTarget
    {
        ConstantX = 0,
        ConstantY = 1,
        SpringX = 2,
        SpringY = 3
    }

    public enum CalibrationOutcome
    {
        Pending = 0,

        /// <summary>Commanded force moved the stick the way it was asked to.</summary>
        Correct = 1,

        /// <summary>This effect runs backwards here; the sign needs flipping.</summary>
        Inverted = 2,

        /// <summary>The stick barely moved, so the direction cannot be trusted either way.</summary>
        Inconclusive = 3
    }

    public sealed class CalibrationResult
    {
        public CalibrationTarget Target;
        public CalibrationOutcome Outcome;

        /// <summary>Signed agreement between commanded and observed motion, in axis counts.</summary>
        public int Score;

        public int PositiveDeflection;
        public int NegativeDeflection;
        public string Message;
    }

    /// <summary>
    /// Works out whether the base applies an effect in the direction it is asked to, by measuring
    /// the stick instead of asking the user.
    ///
    /// Asking is unreliable: the AB9 holds itself centred, so a correct centring spring and the
    /// base's own centring feel the same, and an inverted one just feels like a weaker hold. There
    /// is nothing for a hand to report. Measuring works regardless, because a known force is
    /// applied and the axis is sampled at the loop rate.
    ///
    /// Each target is probed twice, once each way, and each probe is scored on whether the stick
    /// moved the direction it was commanded. Summing those agreements cancels any resting bias -
    /// gravity, an off-centre trim - while still giving the right answer for an inverted spring,
    /// which accelerates away from its anchor and so drives both probes the same way. A probe stops
    /// as soon as the sign is certain, so an inverted spring never reaches its stop.
    ///
    /// Pure logic: it is handed positions and returns forces, so it can be tested against synthetic
    /// sticks with no hardware.
    /// </summary>
    public sealed class PolarityCalibrator
    {
        private const int BaselineMs = 300;
        private const int ProbeMs = 550;
        private const int SettleMs = 400;

        /// <summary>Deflection that ends a probe early; well short of the stops.</summary>
        private const int AbortDeflection = 12000;

        /// <summary>Minimum agreement to trust the result, in axis counts (~3% of travel).</summary>
        private const int MinimumScore = 2000;

        /// <summary>How far off centre the probe spring is anchored, in DirectInput units.</summary>
        private const int SpringProbeOffset = 3000;

        private enum Phase { Baseline, ProbePositive, Settle, ProbeNegative, Complete }

        private readonly CalibrationTarget _target;
        private readonly bool _isX;
        private readonly bool _isSpring;
        private readonly int _probeMagnitude;
        private readonly int _probeCoefficient;

        private Phase _phase = Phase.Baseline;
        private long _phaseStartMs = -1;

        /// <summary>
        /// Where the stick sat when the current probe began. Deflection is measured from here
        /// rather than from a resting baseline: with the base's own centring off, the stick does
        /// not return between probes, and a stale origin would read leftover displacement as the
        /// response.
        /// </summary>
        private int _probeOrigin = GateGeometry.AxisCenter;

        private int _extreme;
        private int _positiveDeflection;
        private int _negativeDeflection;

        /// <summary>Which way each probe should move the stick if the device is behaving.</summary>
        private int _expectedPositive = 1;
        private int _expectedNegative = -1;

        public PolarityCalibrator(CalibrationTarget target, int probeMagnitude)
        {
            _target = target;
            _isX = target == CalibrationTarget.ConstantX || target == CalibrationTarget.SpringX;
            _isSpring = target == CalibrationTarget.SpringX || target == CalibrationTarget.SpringY;
            _probeMagnitude = GateGeometry.Clamp(probeMagnitude, 200, GateGeometry.ForceMax);

            // A spring's force is coefficient times displacement from its anchor. Anchoring it
            // SpringProbeOffset away means the stick starts that far out, so scale the coefficient
            // to land near the requested probe force at that displacement.
            _probeCoefficient = GateGeometry.Clamp(
                (int)Math.Round(_probeMagnitude * (double)GateGeometry.ForceMax / SpringProbeOffset),
                200, GateGeometry.ForceMax);
        }

        public CalibrationTarget Target { get { return _target; } }

        public bool IsComplete { get { return _phase == Phase.Complete; } }

        public CalibrationResult Result { get; private set; }

        public string StatusText
        {
            get
            {
                if (_phase == Phase.Complete) return Result != null ? Result.Message : "Done.";
                return "Measuring " + Describe() + " - hands off the stick.";
            }
        }

        private string Describe()
        {
            return (_isSpring ? "spring" : "push") + " on " + (_isX ? "left/right" : "forward/back");
        }

        /// <summary>
        /// Advances one tick. Returns the force to apply now; the caller must send it to the device
        /// unmodified, with no sign flags and no gain scaling applied - this is measuring the raw
        /// behaviour those settings exist to correct.
        /// </summary>
        public ForceFrame Step(int x, int y, long nowMs)
        {
            int position = _isX ? x : y;

            if (_phaseStartMs < 0) _phaseStartMs = nowMs;
            long elapsed = nowMs - _phaseStartMs;

            switch (_phase)
            {
                case Phase.Baseline:
                    if (elapsed >= BaselineMs) BeginPhase(Phase.ProbePositive, nowMs, position);
                    return Idle();

                case Phase.ProbePositive:
                    TrackExtreme(position);
                    if (elapsed >= ProbeMs || Math.Abs(_extreme) >= AbortDeflection)
                    {
                        _positiveDeflection = _extreme;
                        BeginPhase(Phase.Settle, nowMs, position);
                        return Idle();
                    }
                    return Probe(1);

                case Phase.Settle:
                    if (elapsed >= SettleMs) BeginPhase(Phase.ProbeNegative, nowMs, position);
                    return Idle();

                case Phase.ProbeNegative:
                    TrackExtreme(position);
                    if (elapsed >= ProbeMs || Math.Abs(_extreme) >= AbortDeflection)
                    {
                        _negativeDeflection = _extreme;
                        Finish();
                        return Idle();
                    }
                    return Probe(-1);

                default:
                    return Idle();
            }
        }

        private void BeginPhase(Phase next, long nowMs, int position)
        {
            _phase = next;
            _phaseStartMs = nowMs;
            _probeOrigin = position;
            _extreme = 0;

            if (next == Phase.ProbePositive) _expectedPositive = ExpectedDirection(1, position);
            else if (next == Phase.ProbeNegative) _expectedNegative = ExpectedDirection(-1, position);
        }

        /// <summary>Which way a healthy device would move the stick for this probe.</summary>
        private int ExpectedDirection(int sign, int origin)
        {
            if (!_isSpring) return sign;

            // A spring pulls toward its anchor, so the expected direction depends on which side of
            // the anchor the stick happens to be sitting on when the probe starts.
            int anchor = GateGeometry.DiToAxis(sign * SpringProbeOffset);
            int toward = anchor - origin;
            return toward == 0 ? sign : Math.Sign(toward);
        }

        private void TrackExtreme(int position)
        {
            int deflection = position - _probeOrigin;
            if (Math.Abs(deflection) > Math.Abs(_extreme)) _extreme = deflection;
        }

        private static ForceFrame Idle()
        {
            return new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off,
                ConstantX = 0,
                ConstantY = 0,
                DamperCoefficient = 0
            };
        }

        private ForceFrame Probe(int sign)
        {
            ForceFrame frame = Idle();

            if (_isSpring)
            {
                SpringPreset preset = SpringPreset.Centering(sign * SpringProbeOffset, _probeCoefficient, 0);
                if (_isX) frame.SpringX = preset;
                else frame.SpringY = preset;
            }
            else
            {
                if (_isX) frame.ConstantX = sign * _probeMagnitude;
                else frame.ConstantY = sign * _probeMagnitude;
            }

            return frame;
        }

        private void Finish()
        {
            _phase = Phase.Complete;

            // Score each probe by whether it moved the way it was told to, then add. Summing
            // agreements rather than subtracting raw deflections still cancels a steady bias - the
            // probes expect opposite directions - but also survives an inverted spring, which
            // drives both probes the same way and which subtraction would read as zero.
            int score = (_expectedPositive * _positiveDeflection) + (_expectedNegative * _negativeDeflection);

            var result = new CalibrationResult
            {
                Target = _target,
                Score = score,
                PositiveDeflection = _positiveDeflection,
                NegativeDeflection = _negativeDeflection
            };

            string what = char.ToUpperInvariant(Describe()[0]) + Describe().Substring(1);

            if (Math.Abs(score) < MinimumScore)
            {
                result.Outcome = CalibrationOutcome.Inconclusive;
                result.Message = what + ": inconclusive, the stick barely moved (" + Math.Abs(score) +
                                 " counts, need " + MinimumScore + "). Check nothing is holding the " +
                                 "stick and raise the calibration force.";
            }
            else if (score > 0)
            {
                result.Outcome = CalibrationOutcome.Correct;
                result.Message = what + ": correct (" + score + " counts).";
            }
            else
            {
                result.Outcome = CalibrationOutcome.Inverted;
                result.Message = what + ": inverted, compensating (" + score + " counts).";
            }

            Result = result;
        }
    }
}
