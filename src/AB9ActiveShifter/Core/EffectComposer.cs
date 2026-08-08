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

        /// <summary>
        /// How hard the teeth are disagreeing, 0..1. Always 1 while grinding in
        /// <see cref="GrindClutchMode.Threshold"/>, which is what makes that mode exactly the
        /// behaviour that shipped before the mode existed. Reported for the Monitor tab and for
        /// tests; the balk wall deliberately does NOT scale by it, because a border that softens
        /// as the clutch lifts would let a determined shove through the moment it mattered most.
        /// </summary>
        public double GrindStrength;
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

        /// <summary>Vertical shake below this is road noise, not a curb, in G.</summary>
        public const double HeaveDeadzoneG = 0.10;

        /// <summary>How fast the heave baseline follows sustained load, in milliseconds.</summary>
        private const double HeaveBaseTauMs = 150.0;

        /// <summary>How long a curb strike rings down in the envelope, in milliseconds.</summary>
        private const double CurbReleaseMs = 150.0;

        private double _enginePhase;
        private double _limiterPhase;
        private double _absPhase;
        private double _tcPhase;
        private double _curbsPhase;
        private double _shiftPhase;
        private double _customPhase;
        private double _grindPhase;
        private double _bitePhase;

        // Curb detection state: the slow-heave baseline and the strike envelope.
        private bool _heaveSeeded;
        private double _heaveBase;
        private double _curbEnv;

        private double _shiftPulseLeftMs;
        private string _lastGear;

        // The bite point crossing. Nullable-by-flag rather than a bare bool, because "we have
        // not seen the clutch yet" must not read as "the clutch was below the bite point" - that
        // would fire a pulse the first time a released pedal is reported, on every game start.
        private bool _biteSeeded;
        private bool _biteWasEngaged;
        private double _bitePulseLeftMs;

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
        /// same fact one millisecond late), and <paramref name="slotDepth"/> how far into it,
        /// 0..1 - the grind gets louder the harder the lever is forced against the balk.
        /// </summary>
        public EffectOutput Step(EngineConfig cfg, TelemetryState t, int ageMs, double dtMs,
                                 bool approachingSlot, double slotDepth = 1.0)
        {
            EffectOutput output = default(EffectOutput);

            bool fresh = t != null && t.GameRunning && ageMs >= 0 && ageMs <= StaleAfterMs;
            if (!fresh)
            {
                // Everything transient dies with the telemetry, and the gear edge detector
                // re-adopts on the next fresh frame so a game start never fires a phantom pulse.
                // The heave baseline re-seeds too, so a session starting mid-corner is not a
                // strike.
                _shiftPulseLeftMs = 0;
                _lastGear = null;
                _curbEnv = 0;
                _heaveSeeded = false;
                _bitePulseLeftMs = 0;
                _biteSeeded = false;
                return output;
            }

            double gain = cfg.EffectiveGain;
            int vib = 0;

            // Engine vibration: the carrier's pitch scales with engine speed, anchored by a
            // directly settable frequency at 1000 rpm - so the idle buzz is a number a hand
            // can dial, and revving still raises it proportionally, like firing pulses coming
            // up a real linkage. 17 Hz per 1000 rpm is once per revolution; engine firing
            // orders are multiples of that. Capped where the write rate stops rendering pitch.
            if (cfg.FxEngineEnabled && t.Rpms > MinEngineRpm)
            {
                double freq = GateGeometry.Clamp(
                    t.Rpms / 1000.0 * cfg.FxEngineFreqAt1000Rpm, 4.0, 130.0);
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

            // Curbs and bumps, read out of the vertical acceleration - the one signal a curb
            // cannot hide from and sustained load cannot fake. The baseline follows the slow
            // part (cornering, braking, crests) so only the shake remains; the envelope rises
            // the tick a strike lands and rings down over ~150 ms, which is what keeps a
            // rumble strip's da-da-da rhythm intact through a fixed-pitch carrier.
            if (cfg.FxCurbsEnabled)
            {
                if (!_heaveSeeded)
                {
                    _heaveBase = t.HeaveG;
                    _heaveSeeded = true;
                }

                if (dtMs > 0)
                {
                    _heaveBase += (t.HeaveG - _heaveBase) * Math.Min(1.0, dtMs / HeaveBaseTauMs);
                    _curbEnv *= Math.Max(0.0, 1.0 - dtMs / CurbReleaseMs);
                }

                _curbEnv = Math.Max(_curbEnv, Math.Abs(t.HeaveG - _heaveBase));

                double level = GateGeometry.Clamp(
                    (_curbEnv - HeaveDeadzoneG) / Math.Max(0.1, cfg.FxCurbsFullAtG), 0.0, 1.0);
                if (level > 0)
                {
                    int amp = (int)Math.Round(Amp(cfg.FxCurbsGainPct, gain, VibFullScale) * level);
                    vib += Sine(ref _curbsPhase, cfg.FxCurbsFreqHz, dtMs, amp);
                }
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

            // The bite point, felt through the lever. Fires on the crossing in either direction,
            // because the useful moment is the same one going down as coming up - it is where
            // the drivetrain connects. Edge-triggered off a seeded state so adopting the first
            // reading is silent; a pulse on every game start would train the hand to ignore it.
            bool engaged = t.Clutch < GateGeometry.Clamp(cfg.ClutchBitePointPct, 0, 100);
            if (_biteSeeded && engaged != _biteWasEngaged && cfg.FxBiteEnabled)
            {
                _bitePulseLeftMs = Math.Max(20, cfg.FxBiteDurationMs);
            }
            _biteWasEngaged = engaged;
            _biteSeeded = true;

            if (_bitePulseLeftMs > 0)
            {
                if (dtMs > 0) _bitePulseLeftMs -= dtMs;
                vib += Sine(ref _bitePhase, cfg.FxBiteFreqHz, dtMs,
                            Amp(cfg.FxBiteGainPct, gain, VibFullScale));
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
            // the clutch engaged enough to fight, the car moving at least the floor speed, the
            // engine turning. Louder the deeper the lever is forced - the teeth are being
            // pressed together harder - never fully silent while active, so the mouth still warns.
            double engagement = ClutchEngagement(cfg, t.Clutch);
            if (cfg.GrindEnabled && approachingSlot
                && engagement > 0
                && t.SpeedKmh >= cfg.GrindMinSpeedKmh
                && t.Rpms > MinEngineRpm)
            {
                output.GrindActive = true;
                output.BlockEngage = cfg.GrindRejectsGear;
                output.MuteDetent = cfg.GrindRejectsGear;
                output.GrindStrength = engagement;

                double press = 0.4 + 0.6 * GateGeometry.Clamp(slotDepth, 0.0, 1.0);
                int amp = (int)Math.Round(
                    Amp(cfg.GrindGainPct, gain, GrindFullScale) * press * engagement);
                vib += Square(ref _grindPhase, cfg.GrindFreqHz, dtMs, amp);
            }

            output.VibY = GateGeometry.Clamp(vib, -VibTotalMax, VibTotalMax);
            return output;
        }

        /// <summary>
        /// How hard the dog teeth are being asked to meet, 0 (clutch fully down, nothing to
        /// grind) to 1 (clutch fully up, full disagreement).
        /// <para>
        /// In <see cref="GrindClutchMode.Threshold"/> this is deliberately a step and nothing
        /// else, so the mode is the original behaviour rather than an approximation of it: one
        /// line, full strength on the up side, silence on the down side.
        /// </para>
        /// <para>
        /// In <see cref="GrindClutchMode.Progressive"/> it ramps from the bite point - where the
        /// drivetrain starts to connect and there is first something to disagree with - to fully
        /// released. A clutch held below its bite point is disengaged, so it is silent whatever
        /// the threshold says; the threshold is a Threshold-mode dial and is not consulted here.
        /// </para>
        /// </summary>
        public static double ClutchEngagement(EngineConfig cfg, double clutchPct)
        {
            double clutch = GateGeometry.Clamp(clutchPct, 0.0, 100.0);

            if (cfg.GrindClutchMode == GrindClutchMode.Threshold)
            {
                return clutch < cfg.GrindClutchThresholdPct ? 1.0 : 0.0;
            }

            double bite = GateGeometry.Clamp(cfg.ClutchBitePointPct, 0, 100);
            if (clutch >= bite) return 0.0;
            if (bite <= 0) return 1.0;

            // Linear from the bite point up to fully released. Nothing subtler is warranted:
            // the real curve is a property of a car's clutch that no game reports, and a shape
            // invented here would be a guess dressed as a model.
            return GateGeometry.Clamp((bite - clutch) / bite, 0.0, 1.0);
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
