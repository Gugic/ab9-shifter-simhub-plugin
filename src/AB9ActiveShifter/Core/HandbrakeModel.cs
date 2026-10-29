using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>Bounded monotone force curves. Zero preserves the original saved tune.</summary>
    public enum HandbrakeCurveShape
    {
        BuildLater = 0,
        BuildEarly = 1,
        Linear = 2,
        SmoothS = 3
    }

    /// <summary>
    /// One-sided lever travel, independent force and braking curves, and release-to-arm output.
    /// Raw positions stay in device coordinates; calibration alone determines pull direction.
    /// No telemetry, hardware, clock or allocation belongs in this per-tick model.
    /// </summary>
    public sealed class HandbrakeModel
    {
        public const int MinimumTravel = 8000;
        public const int OutputMax = 10000;
        private readonly EngineConfig _cfg;
        private bool _armed;

        public HandbrakeModel(EngineConfig cfg, bool armed = false) { _cfg = cfg; _armed = armed; }

        public bool IsValid
        {
            get
            {
                return _cfg.HandbrakeRestY >= 0 && _cfg.HandbrakeRestY <= GateGeometry.AxisMax
                    && _cfg.HandbrakeFullPullY >= 0 && _cfg.HandbrakeFullPullY <= GateGeometry.AxisMax
                    && Math.Abs(_cfg.HandbrakeFullPullY - _cfg.HandbrakeRestY) >= MinimumTravel;
            }
        }

        public bool Armed { get { return _armed; } }
        public int PullSign { get { return _cfg.HandbrakeFullPullY >= _cfg.HandbrakeRestY ? 1 : -1; } }
        public int RestY { get { return _cfg.HandbrakeRestY; } }
        public int Travel
        {
            get
            {
                if (!IsValid) return MinimumTravel;
                return Math.Max(MinimumTravel, (int)Math.Round(
                    Math.Abs(_cfg.HandbrakeFullPullY - RestY)
                    * GateGeometry.Clamp(_cfg.HandbrakeTravelPct, 25, 100) / 100.0));
            }
        }
        public int FullPullY { get { return RestY + PullSign * Travel; } }

        /// <summary>Physical travel before the output dead zone, clamped to 0..1.</summary>
        public double PullFraction(int y)
        {
            return IsValid ? GateGeometry.Clamp((y - RestY) * PullSign / (double)Travel, 0, 1) : 0;
        }

        public int BrakeValue(int y)
        {
            if (!IsValid) return 0;
            double dead = GateGeometry.Clamp(_cfg.HandbrakeDeadzonePct, 0, 20) / 100.0;
            double p = GateGeometry.Clamp((PullFraction(y) - dead) / (1 - dead), 0, 1);
            return (int)Math.Round(OutputMax * Curve(p, _cfg.HandbrakeOutputProgressionPct));
        }

        public void Reset() { _armed = false; }

        /// <summary>A restart or missing output cannot publish a pull until release is observed.</summary>
        public int Update(int y, bool outputAllowed)
        {
            if (!outputAllowed || !IsValid) { Reset(); return 0; }
            int value = BrakeValue(y);
            if (!_armed && value == 0) _armed = true;
            return _armed ? value : 0;
        }

        /// <summary>
        /// Unstabilised, gain-capped constant force. Graphs sample this same method.
        /// The return blend is continuous through zero velocity; no direction-switch relay.
        /// Both end stops restore toward rest, with broad faces rather than stiff steps.
        /// </summary>
        public int Resistance(int y, int velocity = 0)
        {
            if (!IsValid) return 0;
            double depth = (y - RestY) * PullSign;
            double pull = GateGeometry.Clamp(_cfg.HandbrakePullForcePct, 0, 100);
            double returning = Math.Min(pull, GateGeometry.Clamp(_cfg.HandbrakeReturnForcePct, 0, 100));
            double blend = GateGeometry.Clamp(-velocity * (double)PullSign / 20000.0, 0, 1);
            blend = blend * blend * (3 - 2 * blend);
            double strength = pull + (returning - pull) * blend;
            double force = strength * Curve(PullFraction(y), _cfg.HandbrakeForceProgressionPct, _cfg.HandbrakeForceCurve);
            double stop = GateGeometry.Clamp(_cfg.HandbrakeStopForcePct, 0, 100);
            if (depth < 0) force = -stop * GateGeometry.Clamp(-depth / MinimumTravel, 0, 1);
            else if (depth > Travel) force += stop * GateGeometry.Clamp((depth - Travel) / MinimumTravel, 0, 1);
            return -PullSign * (int)Math.Round(GateGeometry.ForceMax * _cfg.EffectiveGain
                * GateGeometry.Clamp(force, -100, 100) / 100.0);
        }

        private static double Curve(double p, int progressionPct, HandbrakeCurveShape shape = HandbrakeCurveShape.BuildLater)
        {
            double blend = GateGeometry.Clamp(progressionPct, 0, 100) / 100.0;
            // Never use a fractional power: its unbounded slope at rest would turn the
            // first few counts into a force step. These families all have slope <= 2.
            switch (shape)
            {
                case HandbrakeCurveShape.Linear: return p;
                case HandbrakeCurveShape.BuildEarly: return p * (1 - blend) + p * (2 - p) * blend;
                case HandbrakeCurveShape.SmoothS: return p * (1 - blend) + p * p * (3 - 2 * p) * blend;
                default: return p * (1 - blend) + p * p * blend;
            }
        }

        /// <summary>vJoy reports its logical range; do not assume 0..32768.</summary>
        public static int AxisValue(int brake, long minimum, long maximum)
        {
            return (int)(minimum + Math.Round((maximum - minimum)
                * (GateGeometry.Clamp(brake, 0, OutputMax) / (double)OutputMax)));
        }
    }
}
