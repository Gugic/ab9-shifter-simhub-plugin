using System;
using System.Globalization;

namespace AB9ActiveShifter.Core
{
    /// <summary>A target accepted by the common RPM comparator, independent of its source.</summary>
    public struct ShiftTarget
    {
        public RevMatchSource Source;
        public int Gear;
        public double Rpm;
        public bool Available;
        public int LearnedGears;
    }

    /// <summary>
    /// Chooses targets by available data, never vehicle category. A game adapter can report
    /// shaft RPM directly for any gearbox. Otherwise configured or session-learned RPM/speed
    /// ratios supply the same target. Only acquisition differs; RevMatchModel owns permission.
    /// Runs on the engine thread, with no I/O or per-tick allocation.
    /// </summary>
    public sealed class ShiftTargetResolver
    {
        private readonly double[] _learned = new double[8];
        private string _vehicle, _profile, _transmission, _manual;
        private GatePattern _pattern;
        private int _configEpoch;
        private int _sampleTick, _candidateStart, _candidateGear;
        private bool _sampleSeeded, _hasContext;
        private double _candidateRatio;

        /// <summary>Changes whenever a match or learned ratio would belong to another context.</summary>
        public int ContextVersion { get; private set; }

        public ShiftTarget Resolve(EngineConfig cfg, TelemetryState t, int ageMs, int targetGear, int heldGear)
        {
            var result = new ShiftTarget();
            if (!cfg.FloatShiftingEnabled || cfg.Pattern == GatePattern.Sequential || cfg.Pattern == GatePattern.Prnd
                || t == null || !t.GameRunning)
            {
                if (_hasContext) Reset();
                return result;
            }

            string transmission = t.Shift == null ? null : t.Shift.TransmissionKey;
            if (!_hasContext || _configEpoch != cfg.FloatConfigEpoch
                || _vehicle != t.VehicleKey || _profile != cfg.FloatProfileKey || _pattern != cfg.Pattern
                || _transmission != transmission || _manual != cfg.FloatRpmAt100KmhText)
            {
                Reset();
                _vehicle = t.VehicleKey;
                _profile = cfg.FloatProfileKey;
                _pattern = cfg.Pattern;
                _configEpoch = cfg.FloatConfigEpoch;
                _transmission = transmission;
                _manual = cfg.FloatRpmAt100KmhText;
                _hasContext = true;
            }

            if (ageMs < 0 || ageMs > RevMatchModel.StaleAfterMs)
            { _candidateGear = 0; return result; }

            if (t.Shift == null && cfg.FloatLearnRatios && !string.IsNullOrEmpty(t.VehicleKey)) Learn(t, ageMs, heldGear);
            for (int i = 0; i < 7; i++) if (_learned[i] > 0) result.LearnedGears++;
            if (targetGear < 1 || targetGear > 8) return result;
            result.Gear = targetGear;

            if (t.Shift != null)
            {
                // An adapter's mapping is authoritative. Falling back on a missing target
                // could confuse a physical button with a gear after selectors or mappings
                // change. This contract applies to every game, including ordinary H boxes.
                int rawAge = unchecked(t.CapturedAtTick - t.Shift.CapturedAtTick) + ageMs;
                result.Source = RevMatchSource.GameTelemetry;
                if (rawAge < 0 || rawAge > RevMatchModel.StaleAfterMs) return result;
                if (targetGear <= t.Shift.TargetRpms.Length) result.Rpm = t.Shift.TargetRpms[targetGear - 1];
            }
            else
            {
                // Road-speed ratios cannot prove reverse shaft direction when the speed
                // has no direction. A reported shaft-RPM target has no such limitation.
                if (targetGear == 8 || !RevMatchModel.Finite(t.SpeedKmh) || t.SpeedKmh < 5) return result;
                double ratio = cfg.FloatRpmAt100Kmh != null && targetGear <= cfg.FloatRpmAt100Kmh.Length
                    ? cfg.FloatRpmAt100Kmh[targetGear - 1] : 0;
                result.Source = RevMatchSource.Configured;
                if (!RevMatchModel.FinitePositive(ratio)) { ratio = _learned[targetGear - 1]; result.Source = RevMatchSource.Learned; }
                result.Rpm = ratio * t.SpeedKmh / 100;
            }

            result.Available = RevMatchModel.FinitePositive(result.Rpm);
            return result;
        }

        private void Learn(TelemetryState t, int ageMs, int heldGear)
        {
            if (_sampleSeeded && _sampleTick == t.CapturedAtTick) return;
            if (_sampleSeeded && (unchecked(t.CapturedAtTick - _sampleTick) < 0
                || unchecked(t.CapturedAtTick - _sampleTick) > RevMatchModel.StaleAfterMs)) _candidateGear = 0;
            _sampleTick = t.CapturedAtTick;
            _sampleSeeded = true;
            int gameGear;
            if (heldGear < 1 || heldGear > 7 || !int.TryParse(t.Gear, out gameGear) || gameGear != heldGear
                || t.Clutch > 1 || !RevMatchModel.Finite(t.Clutch) || t.Clutch < 0 || !t.IsClutchFresh(ageMs)
                || !RevMatchModel.Finite(t.SpeedKmh) || t.SpeedKmh < 10
                || t.AbsActive || t.TcActive || t.Rpms <= EffectComposer.MinEngineRpm)
            { _candidateGear = 0; return; }
            double ratio = t.Rpms * 100 / t.SpeedKmh;
            if (!RevMatchModel.FinitePositive(ratio) || ratio > 100000) { _candidateGear = 0; return; }
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

        private void Reset()
        {
            Array.Clear(_learned, 0, _learned.Length);
            _candidateGear = 0;
            _sampleSeeded = false;
            _hasContext = false;
            _vehicle = _profile = _transmission = _manual = null;
            _pattern = default(GatePattern);
            ContextVersion = unchecked(ContextVersion + 1);
        }

        public static double[] ParseRatios(string text)
        {
            var ratios = new double[8];
            string[] values = (text ?? "").Split(',');
            for (int i = 0; i < Math.Min(7, values.Length); i++)
            {
                double value;
                if (double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && RevMatchModel.FinitePositive(value) && value <= 100000) ratios[i] = value;
            }
            return ratios;
        }
    }
}
