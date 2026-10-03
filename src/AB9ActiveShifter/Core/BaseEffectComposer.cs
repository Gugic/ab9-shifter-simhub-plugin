using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Adds the optional typed base conditions to an already composed virtual gate. Gate
    /// forces and their stabilisers are untouched. The engine calls this only for ordinary
    /// output: a calibration probe must reach the device without any decoration or scaling.
    /// </summary>
    public static class BaseEffectComposer
    {
        public static ForceFrame Apply(ForceFrame frame, EngineConfig config, double springScale = 1.0)
        {
            frame.SpringX = SpringPreset.Off;
            frame.SpringY = SpringPreset.Off;
            frame.DamperCoefficient = 0;
            frame.FrictionCoefficient = 0;
            frame.InertiaCoefficient = 0;

            if (!config.BaseEffectsViaDirectInput || config.FreeStick) return frame;

            double gain = config.EffectiveGain;
            frame.DamperCoefficient = (int)Math.Round(
                GateGeometry.Clamp(config.DamperCoeff, 0, GateGeometry.ForceMax) * gain);
            frame.FrictionCoefficient = Coefficient(config.BaseFrictionPct, gain);
            frame.InertiaCoefficient = Coefficient(config.BaseInertiaPct, gain);

            // Old installs confirmed constant polarity without retaining the spring signs.
            // They must measure again before a new global spring can exert any force.
            if (!config.BaseSpringPolarityConfirmed) return frame;

            double boundedScale = double.IsNaN(springScale) ? 0 : Math.Max(0, Math.Min(1, springScale));
            int spring = Coefficient(config.BaseSpringPct, gain * boundedScale);
            if (spring == 0) return frame;

            frame.SpringX = SpringPreset.Centering(0, config.InvertSpringX ? -spring : spring, 0);
            frame.SpringY = SpringPreset.Centering(0, config.InvertSpringY ? -spring : spring, 0);
            return frame;
        }

        private static int Coefficient(int percent, double gain)
        {
            return (int)Math.Round(GateGeometry.Clamp(percent, 0, 100) * 100.0 * gain);
        }
    }
}
