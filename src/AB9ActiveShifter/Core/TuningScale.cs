using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>A display scale only: settings and engine arithmetic keep their original units.</summary>
    public sealed class TuningScale
    {
        public const double DisplayStep = 0.1;
        public double First { get; }
        public double Last { get; }

        public TuningScale(double first, double last)
        {
            if (double.IsNaN(first) || double.IsInfinity(first)
                || double.IsNaN(last) || double.IsInfinity(last) || first == last)
                throw new ArgumentOutOfRangeException(nameof(last));
            First = first;
            Last = last;
        }

        public double ToDisplay(double raw)
        {
            return Clamp((raw - First) * 100.0 / (Last - First), 0, 100);
        }

        public double ToRaw(double display, double step = 1)
        {
            double raw = First + Clamp(display, 0, 100) * (Last - First) / 100.0;
            if (step > 0) raw = Math.Round(raw / step) * step;
            return Clamp(raw, Math.Min(First, Last), Math.Max(First, Last));
        }

        public static double SnapDisplay(double display)
        {
            return Math.Round(Clamp(display, 0, 100), 1, MidpointRounding.AwayFromZero);
        }

        public double DisplayIncrement(double rawStep)
        {
            double quantum = 100 * rawStep / Math.Abs(Last - First);
            // A coarser saved setting still needs a useful +/- step, but never an arbitrary
            // repeating fraction such as 1.428571... for a 30-100 saved range.
            return Math.Round(Math.Max(DisplayStep, Math.Ceiling(quantum * 10 - 1e-9) / 10), 1);
        }

        public bool Contains(double raw)
        {
            return raw >= Math.Min(First, Last) && raw <= Math.Max(First, Last);
        }

        private static double Clamp(double value, double lo, double hi)
        {
            return Math.Max(lo, Math.Min(hi, value));
        }
    }
}
