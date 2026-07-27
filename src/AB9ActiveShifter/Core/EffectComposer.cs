using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>What the telemetry effects contribute to one tick.</summary>
    public struct EffectOutput
    {
        /// <summary>Vibration to sum onto the fore/aft force, gate frame, DirectInput units.</summary>
        public int VibY;

        /// <summary>The grind conditions are met this tick.</summary>
        public bool GrindActive;

        /// <summary>The state machine must refuse to latch a gear this tick.</summary>
        public bool BlockEngage;

        /// <summary>The slot detent renders entry resistance only - no snick, no hold.</summary>
        public bool MuteDetent;
    }

    /// <summary>
    /// Turns game telemetry into lever vibration, and decides when a clutchless shift grinds.
    ///
    /// Every effect is a zero-mean carrier: it can make the lever tremble but never push it
    /// anywhere, which is what makes the whole family safe to sum onto the gate. A carrier is
    /// keyed on time rather than position - moving the stick does not change what it will do
    /// next - so it cannot form the position-to-force loop the yield and the attack exist to
    /// stabilise, and it deliberately bypasses both (see ForceComposer.Bound).
    ///
    /// Freshness is a safety property, not a nicety: a game that pauses, hangs or exits must
    /// not leave a buzz running against the hand, so telemetry older than
    /// <see cref="StaleAfterMs"/> silences everything at once. Amplitudes are shares of a
    /// fixed vibration budget scaled by the same effective gain as the gate, so the
    /// unconfirmed-polarity cap covers the effects too (symmetric carriers have no polarity,
    /// but 12 Nm of anything needs the cap).
    ///
    /// The grind: pushing into a gear with the clutch up while the engine turns. It only ever
    /// fires while an H-pattern lever is actually travelling into a slot - an engaged gear
    /// cannot grind (the dogs are already meshed), and sequential boxes are exempt because
    /// clutchless shifting is exactly what their dog engagement is for. With rejection on, the
    /// gear also refuses to register and the slot detent renders resist-only, so the lever is
    /// pushed back out the way a blocking synchro ring balks it; press the clutch mid-push and
    /// the gear thunks straight in.
    /// </summary>
    public sealed class EffectComposer
    {
        /// <summary>Peak of one ordinary effect at 100% volume and 100% gain, in DI units.</summary>
        public const int VibFullScale = 3000;

        /// <summary>The grind's own, harsher budget.</summary>
        public const int GrindFullScale = 4500;

        /// <summary>Ceiling on the summed carriers, so stacked effects stay a texture.</summary>
        public const int VibTotalMax = 5000;

        /// <summary>Telemetry older than this is treated as absent.</summary>
        public const int StaleAfterMs = 500;

        /// <summary>Below this the engine is off or cranking, and nothing engine-driven plays.</summary>
        public const double MinEngineRpm = 300.0;

        private double _enginePhase;
        private double _limiterPhase;
        private double _absPhase;
        private double _tcPhase;
        private double _shiftPhase;
        private double _customPhase;
        private double _grindPhase;

        private double _shiftPulseLeftMs;
        private string _lastGear;

        // The grind's tooth-depth jitter. A plain square wave reads as a buzzer; giving each
        // half-cycle a fresh pseudo-random depth reads as teeth skipping. Deterministic - a
        // seeded LCG, no clock, no Random - so a recorded complaint replays exactly.
        private uint _rng = 0x6C078965u;
        private double _grindJitter = 1.0;
        private int _grindSign = 1;

        /// <summary>
        /// One tick of effects. <paramref name="ageMs"/> is how old the telemetry snapshot is;
        /// <paramref name="approachingSlot"/> is whether an H-pattern lever is currently
        /// travelling into a slot (the engine passes last tick's state, which at 1 kHz is the
        /// same fact one millisecond late).
        /// </summary>
        public EffectOutput Step(EngineConfig cfg, TelemetryState t, int ageMs, double dtMs, bool approachingSlot)
        {
            EffectOutput output = default(EffectOutput);

            bool fresh = t != null && t.GameRunning && ageMs >= 0 && ageMs <= StaleAfterMs;
            if (!fresh)
            {
                // Everything transient dies with the telemetry, and the gear edge detector
                // re-adopts on the next fresh frame so a game start never fires a phantom pulse.
                _shiftPulseLeftMs = 0;
                _lastGear = null;
                return output;
            }

            double gain = cfg.EffectiveGain;
            int vib = 0;

            // Engine vibration: the carrier tracks engine speed times the order dial, like the
            // firing pulses of a real engine coming up the linkage. Order 1 is one pulse per
            // revolution; a four-cylinder four-stroke fires at order 2.
            if (cfg.FxEngineEnabled && t.Rpms > MinEngineRpm)
            {
                double freq = GateGeometry.Clamp(t.Rpms / 60.0 * cfg.FxEngineOrder, 4.0, 130.0);
                vib += Sine(ref _enginePhase, freq, dtMs, Amp(cfg.FxEngineGainPct, gain, VibFullScale));
            }

            // Rev limiter: a fixed-pitch buzz from just under the redline. Skipped entirely
            // when the game does not report a plausible limit.
            if (cfg.FxLimiterEnabled && t.MaxRpm >= 1000
                && t.Rpms >= t.MaxRpm * cfg.FxLimiterFromPct / 100.0)
            {
                vib += Sine(ref _limiterPhase, cfg.FxLimiterFreqHz, dtMs,
                            Amp(cfg.FxLimiterGainPct, gain, VibFullScale));
            }

            if (cfg.FxAbsEnabled && t.AbsActive)
            {
                vib += Sine(ref _absPhase, cfg.FxAbsFreqHz, dtMs, Amp(cfg.FxAbsGainPct, gain, VibFullScale));
            }

            if (cfg.FxTcEnabled && t.TcActive)
            {
                vib += Sine(ref _tcPhase, cfg.FxTcFreqHz, dtMs, Amp(cfg.FxTcGainPct, gain, VibFullScale));
            }

            // Shift confirmation: one pulse when the game's own gear changes. Edge-detected on
            // the game's gear string rather than our vJoy output, so it confirms what the game
            // actually accepted. The first gear ever seen is adopted silently.
            string gear = t.Gear;
            if (!string.IsNullOrEmpty(gear))
            {
                if (_lastGear != null && gear != _lastGear && cfg.FxShiftEnabled)
                {
                    _shiftPulseLeftMs = Math.Max(20, cfg.FxShiftDurationMs);
                }
                _lastGear = gear;
            }

            if (_shiftPulseLeftMs > 0)
            {
                if (dtMs > 0) _shiftPulseLeftMs -= dtMs;
                vib += Sine(ref _shiftPhase, cfg.FxShiftFreqHz, dtMs,
                            Amp(cfg.FxShiftGainPct, gain, VibFullScale));
            }

            // Custom property: any SimHub property scaled 0..100 drives the volume, which puts
            // ShakeIt's whole effects engine - road rumble, wheel lock, impacts - at the
            // lever's disposal through an exported effect-group property.
            if (cfg.FxCustomEnabled)
            {
                double level = GateGeometry.Clamp(t.CustomValue, 0.0, 100.0) / 100.0;
                if (level > 0)
                {
                    int amp = (int)Math.Round(Amp(cfg.FxCustomGainPct, gain, VibFullScale) * level);
                    vib += Sine(ref _customPhase, cfg.FxCustomFreqHz, dtMs, amp);
                }
            }

            // The grind. Every condition at once: enabled, an H lever pushing into a slot,
            // the clutch up, the car moving at least the floor speed, the engine turning.
            if (cfg.GrindEnabled && approachingSlot
                && t.Clutch < cfg.GrindClutchThresholdPct
                && t.SpeedKmh >= cfg.GrindMinSpeedKmh
                && t.Rpms > MinEngineRpm)
            {
                output.GrindActive = true;
                output.BlockEngage = cfg.GrindRejectsGear;
                output.MuteDetent = cfg.GrindRejectsGear;

                vib += Square(ref _grindPhase, cfg.GrindFreqHz, dtMs,
                              Amp(cfg.GrindGainPct, gain, GrindFullScale));
            }

            output.VibY = GateGeometry.Clamp(vib, -VibTotalMax, VibTotalMax);
            return output;
        }

        private static int Amp(int pct, double gain, int fullScale)
        {
            return (int)Math.Round(fullScale * GateGeometry.Clamp(pct, 0, 100) / 100.0 * gain);
        }

        private static int Sine(ref double phase, double freqHz, double dtMs, int amplitude)
        {
            if (amplitude <= 0) return 0;
            Advance(ref phase, freqHz, dtMs);
            return (int)Math.Round(Math.Sin(phase) * amplitude);
        }

        /// <summary>The grind's carrier: a square wave whose tooth depth changes every half-cycle.</summary>
        private int Square(ref double phase, double freqHz, double dtMs, int amplitude)
        {
            if (amplitude <= 0) return 0;
            Advance(ref phase, freqHz, dtMs);

            int sign = Math.Sin(phase) >= 0 ? 1 : -1;
            if (sign != _grindSign)
            {
                _grindSign = sign;
                _rng = _rng * 1664525u + 1013904223u;
                _grindJitter = 0.55 + 0.45 * (((_rng >> 8) & 0xFFFF) / 65535.0);
            }

            return (int)Math.Round(sign * amplitude * _grindJitter);
        }

        private static void Advance(ref double phase, double freqHz, double dtMs)
        {
            if (dtMs <= 0) return;
            phase += 2.0 * Math.PI * freqHz * dtMs / 1000.0;
            if (phase > 64.0 * Math.PI) phase %= 2.0 * Math.PI;
        }
    }
}
