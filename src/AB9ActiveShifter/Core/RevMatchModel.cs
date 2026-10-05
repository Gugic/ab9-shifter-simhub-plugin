using System;

namespace AB9ActiveShifter.Core
{
    public enum RevMatchSource { None, GameTelemetry, Configured, Learned }

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
    /// One RPM comparison for every H-pattern gearbox. Target acquisition and learning belong
    /// to ShiftTargetResolver; source, game and vehicle type cannot change the match window,
    /// hysteresis or mismatch envelope. This never operates the throttle or clutch.
    /// </summary>
    public sealed class RevMatchModel
    {
        public const int StaleAfterMs = 150;
        private readonly ShiftTargetResolver _targets = new ShiftTargetResolver();
        private int _contextVersion, _matchedGear;
        private bool _matched;

        public RevMatchResult Step(EngineConfig cfg, TelemetryState t, int ageMs, int targetGear, int heldGear)
        {
            ShiftTarget target = _targets.Resolve(cfg, t, ageMs, targetGear, heldGear);
            var result = new RevMatchResult
            {
                Mismatch = 1,
                Source = target.Source,
                TargetGear = target.Gear,
                TargetRpm = target.Rpm,
                LearnedGears = target.LearnedGears
            };
            if (_contextVersion != _targets.ContextVersion) ForgetMatch();
            _contextVersion = _targets.ContextVersion;

            if (!target.Available || t == null || !FinitePositive(t.Rpms)) { ForgetMatch(); return result; }
            result.Available = true;
            result.ErrorRpm = t.Rpms - result.TargetRpm;
            double tolerance = GateGeometry.Clamp(cfg.FloatToleranceRpm, 25, 1000);
            if (_matchedGear != targetGear) ForgetMatch();
            _matchedGear = targetGear;
            // Hysteresis is permission for this target only. No match can survive a stale
            // frame, a transmission context change, a different target, or neutral.
            _matched = Math.Abs(result.ErrorRpm) <= tolerance * (_matched ? 1.25 : 1);
            result.Matched = _matched;
            result.Mismatch = result.Matched ? 0 : GateGeometry.Clamp(
                (Math.Abs(result.ErrorRpm) - tolerance) / (tolerance * 4), 0, 1);
            return result;
        }

        private void ForgetMatch() { _matched = false; _matchedGear = 0; }

        public static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public static bool FinitePositive(double value) { return Finite(value) && value > 0; }

        /// <summary>RPM at 100 km/h for gears 1..7. Empty/invalid entries remain unknown.</summary>
        public static double[] ParseRatios(string text)
        {
            return ShiftTargetResolver.ParseRatios(text);
        }
    }
}
