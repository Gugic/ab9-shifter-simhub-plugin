using System;

namespace AB9ActiveShifter.Core
{
    public enum CalibrationTarget
    {
        /// <summary>Constant force effects: the lockout push and the slot detent.</summary>
        Constant = 0,

        /// <summary>Condition (spring) effects: the gate walls and the neutral channel.</summary>
        Spring = 1
    }

    public enum CalibrationOutcome
    {
        Pending = 0,

        /// <summary>Commanded force moved the stick the way it was asked to.</summary>
        Correct = 1,

        /// <summary>Firmware applies this effect family backwards; the invert flag is needed.</summary>
        Inverted = 2,

        /// <summary>The stick barely moved, so the direction cannot be trusted either way.</summary>
        Inconclusive = 3
    }

    public sealed class CalibrationResult
    {
        public CalibrationTarget Target;
        public CalibrationOutcome Outcome;

        /// <summary>Signed separation between the two probes, in axis counts. Sign gives the answer.</summary>
        public int Score;

        public int BaselinePosition;
        public int PositiveDeflection;
        public int NegativeDeflection;
        public string Message;
    }

    /// <summary>
    /// Works out whether the base applies force in the direction it is asked to, by measuring the
    /// stick instead of asking the user.
    ///
    /// Asking is unreliable: the AB9 holds itself centred, so a correct centring spring and the
    /// base's own centring feel the same, and an inverted one just feels like a weaker hold. There
    /// is nothing for a hand to report. Measuring works regardless, because a known force is applied
    /// and the axis is sampled at the loop rate.
    ///
    /// Each target is probed twice, once in each direction, and the two deflections are subtracted.
    /// That cancels any resting bias - gravity, an off-centre trim, a base spring that is not
    /// actually at zero - so only the response to the commanded force survives. A probe is cut short
    /// the moment the stick has moved far enough to be sure of the sign, which keeps an inverted
    /// spring from running to its stop.
    ///
    /// Pure logic: it is handed positions and returns forces, so it can be tested against synthetic
    /// sticks with no hardware.
    /// </summary>
    public sealed class PolarityCalibrator
    {
        private const int BaselineMs = 350;
        private const int ProbeMs = 550;
        private const int SettleMs = 400;

        /// <summary>Deflection that ends a probe early; well short of the stops.</summary>
        private const int AbortDeflection = 12000;

        /// <summary>Minimum probe separation to trust the result, in axis counts (~3% of travel).</summary>
        private const int MinimumScore = 2000;

        /// <summary>How far off centre the probe spring is anchored, in DirectInput units.</summary>
        private const int SpringProbeOffset = 3000;

        private enum Phase { Baseline, ProbePositive, Settle, ProbeNegative, Complete }

        private readonly CalibrationTarget _target;
        private readonly int _probeMagnitude;
        private readonly int _probeCoefficient;

        private Phase _phase = Phase.Baseline;
        private long _phaseStartMs = -1;

        private long _baselineSum;
        private int _baselineCount;
        private int _baseline = GateGeometry.AxisCenter;

        /// <summary>
        /// Where the stick sat when the current probe began. Deflection is measured from here, not
        /// from the resting baseline: with the base's own spring at zero - which is how this plugin
        /// asks the user to configure it - the stick does not return to centre between probes, and
        /// measuring against a stale origin would read the leftover displacement as the response.
        /// </summary>
        private int _probeOrigin = GateGeometry.AxisCenter;

        private int _extreme;
        private int _positiveDeflection;
        private int _negativeDeflection;

        /// <summary>
        /// Which way each probe should move the stick if the firmware is behaving. For a constant
        /// force that is simply the sign of the magnitude; for a spring it is whichever way the
        /// anchor lies from where the stick actually started, which is why it is recomputed per
        /// probe rather than assumed.
        /// </summary>
        private int _expectedPositive = 1;
        private int _expectedNegative = -1;

        public PolarityCalibrator(CalibrationTarget target, int probeMagnitude)
        {
            _target = target;
            _probeMagnitude = GateGeometry.Clamp(probeMagnitude, 200, GateGeometry.ForceMax);

            // A spring's force is coefficient * displacement from its offset. Anchoring the probe
            // SpringProbeOffset away from centre means the stick starts that far from equilibrium,
            // so scale the coefficient to land near the requested probe force at that displacement.
            _probeCoefficient = GateGeometry.Clamp(
                (int)Math.Round(_probeMagnitude * (double)GateGeometry.ForceMax / SpringProbeOffset),
                200, GateGeometry.ForceMax);
        }

        public CalibrationTarget Target { get { return _target; } }

        public bool IsComplete { get { return _phase == Phase.Complete; } }

        public CalibrationResult Result { get; private set; }

        /// <summary>Rough progress for the UI, 0..1.</summary>
        public double Progress
        {
            get
            {
                switch (_phase)
                {
                    case Phase.Baseline: return 0.10;
                    case Phase.ProbePositive: return 0.35;
                    case Phase.Settle: return 0.60;
                    case Phase.ProbeNegative: return 0.85;
                    default: return 1.0;
                }
            }
        }

        public string StatusText
        {
            get
            {
                string what = _target == CalibrationTarget.Constant ? "push" : "spring";
                switch (_phase)
                {
                    case Phase.Baseline: return "Measuring resting position - hands off the stick.";
                    case Phase.ProbePositive: return "Testing " + what + " one way.";
                    case Phase.Settle: return "Letting the stick settle.";
                    case Phase.ProbeNegative: return "Testing " + what + " the other way.";
                    default: return Result != null ? Result.Message : "Done.";
                }
            }
        }

        /// <summary>
        /// Advances one tick. Returns the force to apply right now; the caller must send it to the
        /// device unmodified, without applying any invert flags - this is measuring the raw
        /// behaviour those flags exist to correct.
        /// </summary>
        public ForceFrame Step(int y, long nowMs)
        {
            if (_phaseStartMs < 0) _phaseStartMs = nowMs;
            long elapsed = nowMs - _phaseStartMs;

            switch (_phase)
            {
                case Phase.Baseline:
                    // Average only the tail of the window, after any leftover motion has died down.
                    if (elapsed > BaselineMs / 2)
                    {
                        _baselineSum += y;
                        _baselineCount++;
                    }

                    if (elapsed >= BaselineMs)
                    {
                        if (_baselineCount > 0) _baseline = (int)(_baselineSum / _baselineCount);
                        BeginPhase(Phase.ProbePositive, nowMs, y);
                    }
                    return Idle();

                case Phase.ProbePositive:
                    TrackExtreme(y);
                    if (elapsed >= ProbeMs || Math.Abs(_extreme) >= AbortDeflection)
                    {
                        _positiveDeflection = _extreme;
                        BeginPhase(Phase.Settle, nowMs, y);
                        return Idle();
                    }
                    return Probe(1);

                case Phase.Settle:
                    if (elapsed >= SettleMs) BeginPhase(Phase.ProbeNegative, nowMs, y);
                    return Idle();

                case Phase.ProbeNegative:
                    TrackExtreme(y);
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

        private void BeginPhase(Phase next, long nowMs, int y)
        {
            _phase = next;
            _phaseStartMs = nowMs;
            _probeOrigin = y;
            _extreme = 0;

            if (next == Phase.ProbePositive) _expectedPositive = ExpectedDirection(1, y);
            else if (next == Phase.ProbeNegative) _expectedNegative = ExpectedDirection(-1, y);
        }

        /// <summary>Which way a healthy device would move the stick for this probe.</summary>
        private int ExpectedDirection(int sign, int origin)
        {
            if (_target == CalibrationTarget.Constant) return sign;

            // A spring pulls toward its anchor, so the expected direction depends on which side of
            // the anchor the stick is sitting on when the probe starts.
            int anchor = GateGeometry.DiToAxis(sign * SpringProbeOffset);
            int toward = anchor - origin;
            return toward == 0 ? sign : Math.Sign(toward);
        }

        private void TrackExtreme(int y)
        {
            int deflection = y - _probeOrigin;
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
            var frame = Idle();

            if (_target == CalibrationTarget.Constant)
            {
                frame.ConstantY = sign * _probeMagnitude;
            }
            else
            {
                frame.SpringY = SpringPreset.Centering(sign * SpringProbeOffset, _probeCoefficient, 0);
            }

            return frame;
        }

        private void Finish()
        {
            _phase = Phase.Complete;

            // Score each probe by whether it moved the way it was told to, then add them. Summing
            // agreements rather than subtracting raw deflections still cancels a steady bias - the
            // two probes expect opposite directions, so a common drift contributes equally and
            // oppositely - but it also survives an inverted spring, which accelerates away from its
            // anchor and therefore drives BOTH probes the same way. Subtraction reads that as zero.
            int score = (_expectedPositive * _positiveDeflection) + (_expectedNegative * _negativeDeflection);

            var result = new CalibrationResult
            {
                Target = _target,
                Score = score,
                BaselinePosition = _baseline,
                PositiveDeflection = _positiveDeflection,
                NegativeDeflection = _negativeDeflection
            };

            string what = _target == CalibrationTarget.Constant ? "Constant forces" : "Springs";

            if (Math.Abs(score) < MinimumScore)
            {
                result.Outcome = CalibrationOutcome.Inconclusive;
                result.Message = what + ": the stick barely moved (" + Math.Abs(score) +
                                 " counts, need " + MinimumScore + "). Set the base's own Spring to 0 in " +
                                 "MOZA Pit House, make sure nothing is holding the stick, and raise the " +
                                 "calibration force.";
            }
            else if (score > 0)
            {
                result.Outcome = CalibrationOutcome.Correct;
                result.Message = what + ": correct - the stick moved the way it was pushed (" + score + " counts).";
            }
            else
            {
                result.Outcome = CalibrationOutcome.Inverted;
                result.Message = what + ": INVERTED - the stick moved opposite to the commanded force (" +
                                 score + " counts). Compensation enabled.";
            }

            Result = result;
        }
    }
}
