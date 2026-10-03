using System;
using System.Globalization;

namespace AB9ActiveShifter.Core
{
    public enum RevMatchSource { None, TruckTelemetry, Configured, Learned }

    /// <summary>Copied on the data thread. RPMs are indexed by vJoy gear button minus one,
    /// not by transmission gear: range and splitter can make one button select many gears.</summary>
    public sealed class ShiftTelemetry
    {
        public readonly double[] TargetRpms;
        public readonly string TransmissionKey;
        public readonly int CapturedAtTick;

        public ShiftTelemetry(double[] targetRpms, string transmissionKey, int capturedAtTick)
        {
            TargetRpms = (double[])targetRpms.Clone();
            TransmissionKey = transmissionKey;
            CapturedAtTick = capturedAtTick;
        }
    }

    public struct RevMatchResult
    {
        public RevMatchSource Source;
        public int TargetGear;
        public int LearnedGears;
        public bool Available;
        public bool Matched;
        public double TargetRpm;
        public double ErrorRpm;
        public double Mismatch;
    }

    /// <summary>
    /// Independent speed comparison, never a throttle or clutch controller. Unknown ratios
    /// grant no permission. Learning needs separate, stable telemetry frames with the clutch
    /// fully released and the game confirming the physical gear. Runtime learning belongs to
    /// one vehicle/transmission/profile session and never edits persisted tuning.
    /// </summary>
    public sealed class RevMatchModel
    {
        public const int StaleAfterMs = 150;
        private readonly double[] _learned = new double[8];
        private string _vehicle, _profile, _transmission, _manual;
        private GatePattern _pattern;
        private int _sampleTick, _candidateStart, _candidateGear, _matchedGear;
        private bool _sampleSeeded, _matched;
        private double _candidateRatio;

        public RevMatchResult Step(EngineConfig cfg, TelemetryState t, int ageMs, int targetGear, int heldGear)
        {
            var result = new RevMatchResult { Mismatch = 1 };
            if (!cfg.FloatShiftingEnabled || cfg.Pattern == GatePattern.Sequential || cfg.Pattern == GatePattern.Prnd
                || t == null || !t.GameRunning)
            {
                Reset();
                return result;
            }

            string transmission = t.Shift == null ? null : t.Shift.TransmissionKey;
            if (_vehicle != t.VehicleKey || _profile != cfg.FloatProfileKey || _pattern != cfg.Pattern
                || _transmission != transmission || _manual != cfg.FloatRpmAt100KmhText)
            {
                Reset();
                _vehicle = t.VehicleKey;
                _profile = cfg.FloatProfileKey;
                _pattern = cfg.Pattern;
                _transmission = transmission;
                _manual = cfg.FloatRpmAt100KmhText;
            }

            if (ageMs < 0 || ageMs > StaleAfterMs || !FinitePositive(t.Rpms) || !Finite(t.SpeedKmh))
            {
                ForgetMatch();
                _candidateGear = 0;
                return result;
            }

            if (t.Shift == null && cfg.FloatLearnRatios && !string.IsNullOrEmpty(t.VehicleKey)) Learn(t, heldGear);
            for (int i = 0; i < 7; i++) if (_learned[i] > 0) result.LearnedGears++;
            if (targetGear < 1 || targetGear > 8) { ForgetMatch(); return result; }
            result.TargetGear = targetGear;

            if (t.Shift != null)
            {
                // An SCS frame with an unknown mapping stays unknown: neither a learned
                // passenger-car ratio nor a manual fallback knows what its selectors mean.
                int rawAge = unchecked(t.CapturedAtTick - t.Shift.CapturedAtTick) + ageMs;
                if (rawAge < 0 || rawAge > StaleAfterMs) { ForgetMatch(); return result; }
                result.Source = RevMatchSource.TruckTelemetry;
                if (targetGear <= t.Shift.TargetRpms.Length) result.TargetRpm = t.Shift.TargetRpms[targetGear - 1];
            }
            else
            {
                // Generic speed is usually unsigned. Reverse requires the clutch: an unsigned
                // road speed cannot prove the reverse shaft is turning in the right direction.
                if (targetGear == 8 || t.SpeedKmh < 5) { ForgetMatch(); return result; }
                double ratio = cfg.FloatRpmAt100Kmh != null && targetGear <= cfg.FloatRpmAt100Kmh.Length
                    ? cfg.FloatRpmAt100Kmh[targetGear - 1] : 0;
                result.Source = RevMatchSource.Configured;
                if (!FinitePositive(ratio)) { ratio = _learned[targetGear - 1]; result.Source = RevMatchSource.Learned; }
                result.TargetRpm = ratio * t.SpeedKmh / 100;
            }

            if (!FinitePositive(result.TargetRpm)) { ForgetMatch(); return result; }
            result.Available = true;
            result.ErrorRpm = t.Rpms - result.TargetRpm;
            double tolerance = GateGeometry.Clamp(cfg.FloatToleranceRpm, 25, 1000);
            if (_matchedGear != targetGear) ForgetMatch();
            _matchedGear = targetGear;
            // Hysteresis is permission for this target only. No match can survive a stale
            // frame, a selector change, a different target, or a return through neutral.
            _matched = Math.Abs(result.ErrorRpm) <= tolerance * (_matched ? 1.25 : 1);
            result.Matched = _matched;
            result.Mismatch = result.Matched ? 0 : GateGeometry.Clamp(
                (Math.Abs(result.ErrorRpm) - tolerance) / (tolerance * 4), 0, 1);
            return result;
        }

        private void Learn(TelemetryState t, int heldGear)
        {
            if (_sampleSeeded && _sampleTick == t.CapturedAtTick) return;
            if (_sampleSeeded && (unchecked(t.CapturedAtTick - _sampleTick) < 0
                || unchecked(t.CapturedAtTick - _sampleTick) > StaleAfterMs)) _candidateGear = 0;
            _sampleTick = t.CapturedAtTick;
            _sampleSeeded = true;
            int gameGear;
            if (heldGear < 1 || heldGear > 7 || !int.TryParse(t.Gear, out gameGear) || gameGear != heldGear
                || t.Clutch > 1 || !Finite(t.Clutch) || t.Clutch < 0 || t.SpeedKmh < 10
                || t.AbsActive || t.TcActive || t.Rpms <= EffectComposer.MinEngineRpm)
            { _candidateGear = 0; return; }
            double ratio = t.Rpms * 100 / t.SpeedKmh;
            if (!FinitePositive(ratio) || ratio > 100000) { _candidateGear = 0; return; }
            if (_candidateGear != heldGear || Math.Abs(ratio - _candidateRatio) > _candidateRatio * 0.015
                || unchecked(t.CapturedAtTick - _candidateStart) < 0)
            {
                _candidateGear = heldGear;
                _candidateRatio = ratio;
                _candidateStart = t.CapturedAtTick;
                return;
            }
            if (unchecked(t.CapturedAtTick - _candidateStart) >= 750) _learned[heldGear - 1] = _candidateRatio;
        }

        private void ForgetMatch() { _matched = false; _matchedGear = 0; }
        private void Reset()
        {
            Array.Clear(_learned, 0, _learned.Length);
            _candidateGear = 0;
            _sampleSeeded = false;
            ForgetMatch();
            _vehicle = _profile = _transmission = _manual = null;
            _pattern = default(GatePattern);
        }

        public static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public static bool FinitePositive(double value) { return Finite(value) && value > 0; }

        /// <summary>RPM at 100 km/h for gears 1..7. Empty/invalid entries remain unknown.</summary>
        public static double[] ParseRatios(string text)
        {
            var ratios = new double[8];
            string[] values = (text ?? "").Split(',');
            for (int i = 0; i < Math.Min(7, values.Length); i++)
            {
                double value;
                if (double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && FinitePositive(value) && value <= 100000) ratios[i] = value;
            }
            return ratios;
        }
    }
}
