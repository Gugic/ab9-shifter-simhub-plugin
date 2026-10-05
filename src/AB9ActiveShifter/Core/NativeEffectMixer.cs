using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>One native ShakeIt tone, copied on the data thread without SimHub objects.</summary>
    public struct NativeEffectTone
    {
        public int Id;
        public double Frequency;
        public double Level;
        public int DelayMs;
        public bool Priority;
        public bool Grind;
        public bool Test;
    }

    /// <summary>Whole handoff from ShakeIt to the force loop. The array is never changed after publication.</summary>
    public sealed class NativeEffectFrame
    {
        public static readonly NativeEffectFrame Silent = new NativeEffectFrame(0, 0, new NativeEffectTone[0]);

        public readonly int ProfileEpoch;
        public readonly int CapturedAtTick;
        public readonly NativeEffectTone[] Tones;

        public NativeEffectFrame(int profileEpoch, int capturedAtTick, NativeEffectTone[] tones)
        {
            ProfileEpoch = profileEpoch;
            CapturedAtTick = capturedAtTick;
            Tones = tones ?? new NativeEffectTone[0];
        }
    }

    /// <summary>
    /// Renders native strength/frequency envelopes at the force loop's rate. No native effect,
    /// renderer, property lookup or allocation enters the tick. Each tone keeps its own phase;
    /// combining frequencies into one average would erase overlapping effects.
    /// </summary>
    public sealed class NativeEffectMixer
    {
        public const int MaxTones = 64;
        public const int MinimumFrequency = 4;
        public const int MaximumFrequency = 130;

        private struct Voice
        {
            public int Id;
            public double Phase;
            public double AgeMs;
            public bool Seen;
        }

        private readonly Voice[] _voices = new Voice[MaxTones];
        private int _epoch;

        public static bool IsTestActive(bool requested, DateTime? start, double durationMs, DateTime now)
        {
            if (requested) return true;
            if (!start.HasValue || !Finite(durationMs)) return false;
            double ageMs = (now - start.Value).TotalMilliseconds;
            return ageMs >= 0 && ageMs < durationMs;
        }

        public int Step(NativeEffectFrame frame, int epoch, int ageMs, double dtMs,
                        double effectiveGain, bool telemetryFresh, bool grindAllowed = true)
        {
            if (frame == null || frame.ProfileEpoch != epoch || ageMs < 0
                || ageMs > EffectComposer.StaleAfterMs || epoch != _epoch)
            {
                Array.Clear(_voices, 0, _voices.Length);
                _epoch = epoch;
                if (frame == null || frame.ProfileEpoch != epoch || ageMs < 0
                    || ageMs > EffectComposer.StaleAfterMs) return 0;
            }

            dtMs = Finite(dtMs) ? GateGeometry.Clamp(dtMs, 0, 20) : 0;
            effectiveGain = Finite(effectiveGain) ? GateGeometry.Clamp(effectiveGain, 0, 1) : 0;
            bool priority = false;
            int count = Math.Min(frame.Tones.Length, MaxTones);
            for (int i = 0; i < count; i++)
            {
                NativeEffectTone tone = frame.Tones[i];
                if ((telemetryFresh || tone.Test) && (grindAllowed || !tone.Grind || tone.Test)
                    && tone.Priority && Valid(tone)) priority = true;
            }

            for (int i = 0; i < _voices.Length; i++) _voices[i].Seen = false;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                NativeEffectTone tone = frame.Tones[i];
                if ((!telemetryFresh && !tone.Test) || (priority && !tone.Priority) || !Valid(tone)) continue;
                if (tone.Grind && !grindAllowed && !tone.Test) continue;

                int index = FindVoice(tone.Id);
                if (index < 0) continue;
                _voices[index].Seen = true;
                _voices[index].AgeMs += dtMs;
                double frequency = GateGeometry.Clamp(tone.Frequency, MinimumFrequency, MaximumFrequency);
                _voices[index].Phase = (_voices[index].Phase + 2 * Math.PI * frequency * dtMs / 1000) % (2 * Math.PI);
                if (_voices[index].AgeMs < GateGeometry.Clamp(tone.DelayMs, 0, 2000)) continue;

                int budget = tone.Grind ? EffectComposer.GrindFullScale : EffectComposer.VibFullScale;
                sum += Math.Sin(_voices[index].Phase) * budget
                    * GateGeometry.Clamp(tone.Level, 0, 1);
            }
            for (int i = 0; i < _voices.Length; i++)
            {
                if (!_voices[i].Seen) _voices[i] = default(Voice);
            }
            return (int)Math.Round(GateGeometry.Clamp(sum, -EffectComposer.VibTotalMax, EffectComposer.VibTotalMax) * effectiveGain);
        }

        private int FindVoice(int id)
        {
            int empty = -1;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Id == id) return i;
                if (_voices[i].Id == 0 && empty < 0) empty = i;
            }
            if (empty >= 0) _voices[empty].Id = id;
            return empty;
        }

        private static bool Valid(NativeEffectTone tone)
        {
            return tone.Id > 0 && Finite(tone.Level) && tone.Level > 0
                && Finite(tone.Frequency) && tone.Frequency > 0;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
