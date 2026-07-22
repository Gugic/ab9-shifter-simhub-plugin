using System;
using AB9ActiveShifter.Core;
using SharpDX;
using SharpDX.DirectInput;

namespace AB9ActiveShifter.Device
{
    /// <summary>
    /// The five DirectInput effects that make up the gate. They are created and downloaded
    /// once, started once, and from then on only their type-specific parameters are
    /// rewritten - restarting an effect every tick would audibly stutter and floods the
    /// device's USB pipe.
    ///
    /// "Off" is expressed as a zero coefficient or zero magnitude rather than stopping the
    /// effect, for the same reason.
    /// </summary>
    public sealed class EffectSet : IDisposable
    {
        /// <summary>Smallest constant-force change worth a device write.</summary>
        private const int ConstantDeadband = 50;

        /// <summary>Minimum gap between constant-force writes, in milliseconds.</summary>
        private const int ConstantMinIntervalMs = 4;

        private const int MaxStrikes = 3;

        private readonly Joystick _joystick;

        private Effect _springX;
        private Effect _springY;
        private Effect _constantX;
        private Effect _constantY;
        private Effect _damper;

        private EffectParameters _pSpringX;
        private EffectParameters _pSpringY;
        private EffectParameters _pConstantX;
        private EffectParameters _pConstantY;
        private EffectParameters _pDamper;

        // Held so the arrays the ConditionSets reference are the ones being mutated.
        private readonly Condition[] _condX = new Condition[1];
        private readonly Condition[] _condY = new Condition[1];
        private readonly Condition[] _condDamper = new Condition[2];
        private readonly ConstantForce _forceX = new ConstantForce();
        private readonly ConstantForce _forceY = new ConstantForce();

        private SpringPreset _lastSpringX;
        private SpringPreset _lastSpringY;
        private bool _springsPrimed;
        private int _lastConstantX;
        private int _lastConstantY;
        private long _lastConstantXMs;
        private long _lastConstantYMs;

        // Until the first write lands there is no "last" anything to rate limit against, and a
        // sentinel timestamp cannot express that: subtracting long.MinValue from the clock
        // overflows and the interval test silently fails forever, so the effect never receives a
        // magnitude at all.
        private bool _constantXPrimed;
        private bool _constantYPrimed;

        private int _lastDamper = -1;

        private int _strikes;

        public bool HasDamper { get; private set; }

        /// <summary>Consecutive parameter-update failures. The engine reopens the device at 3.</summary>
        public int Strikes { get { return _strikes; } }

        public bool IsFaulted { get { return _strikes >= MaxStrikes; } }

        private EffectSet(Joystick joystick)
        {
            _joystick = joystick;
        }

        public static EffectSet Create(Joystick joystick, int damperCoefficient, out string error)
        {
            var set = new EffectSet(joystick);
            try
            {
                set.Build(damperCoefficient);
                error = null;
                return set;
            }
            catch (Exception ex)
            {
                error = "Could not create force feedback effects: " + ex.Message;
                set.Dispose();
                return null;
            }
        }

        private void Build(int damperCoefficient)
        {
            _pSpringX = BuildConditionParameters(JoystickOffset.X, _condX);
            _springX = CreateAndStart(EffectGuid.Spring, _pSpringX);

            _pSpringY = BuildConditionParameters(JoystickOffset.Y, _condY);
            _springY = CreateAndStart(EffectGuid.Spring, _pSpringY);

            _pConstantX = BuildConstantParameters(JoystickOffset.X, _forceX);
            _constantX = CreateAndStart(EffectGuid.ConstantForce, _pConstantX);

            _pConstantY = BuildConstantParameters(JoystickOffset.Y, _forceY);
            _constantY = CreateAndStart(EffectGuid.ConstantForce, _pConstantY);

            // The damper only suppresses oscillation. If the device will not take it on both
            // axes, fall back to Y alone; if it will not take it at all, the gate still works.
            if (!TryBuildDamper(damperCoefficient, true) && !TryBuildDamper(damperCoefficient, false))
            {
                HasDamper = false;
            }
        }

        private bool TryBuildDamper(int coefficient, bool bothAxes)
        {
            try
            {
                int axisCount = bothAxes ? 2 : 1;
                for (int i = 0; i < axisCount; i++)
                {
                    _condDamper[i] = new Condition
                    {
                        Offset = 0,
                        PositiveCoefficient = coefficient,
                        NegativeCoefficient = coefficient,
                        PositiveSaturation = GateGeometry.ForceMax,
                        NegativeSaturation = GateGeometry.ForceMax,
                        DeadBand = 0
                    };
                }

                var conditions = new Condition[axisCount];
                Array.Copy(_condDamper, conditions, axisCount);

                _pDamper = new EffectParameters
                {
                    Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                    Duration = -1,
                    SamplePeriod = 0,
                    Gain = GateGeometry.ForceMax,
                    TriggerButton = -1,
                    TriggerRepeatInterval = 0,
                    StartDelay = 0,
                    Envelope = null,
                    Parameters = new ConditionSet { Conditions = conditions }
                };

                _pDamper.Axes = bothAxes
                    ? new[] { (int)JoystickOffset.X, (int)JoystickOffset.Y }
                    : new[] { (int)JoystickOffset.Y };
                _pDamper.Directions = bothAxes ? new[] { 1, 1 } : new[] { 1 };

                _damper = CreateAndStart(EffectGuid.Damper, _pDamper);
                _lastDamper = coefficient;
                HasDamper = true;

                if (!bothAxes) Log.Warn("Two-axis damper unavailable; damping the Y axis only.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Damper creation failed (bothAxes=" + bothAxes + "): " + ex.Message);
                _damper = null;
                _pDamper = null;
                return false;
            }
        }

        private static EffectParameters BuildConditionParameters(JoystickOffset axis, Condition[] conditions)
        {
            conditions[0] = new Condition
            {
                Offset = 0,
                PositiveCoefficient = 0,
                NegativeCoefficient = 0,
                PositiveSaturation = GateGeometry.ForceMax,
                NegativeSaturation = GateGeometry.ForceMax,
                DeadBand = 0
            };

            var p = new EffectParameters
            {
                Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                Duration = -1,
                SamplePeriod = 0,
                Gain = GateGeometry.ForceMax,
                TriggerButton = -1,
                TriggerRepeatInterval = 0,
                StartDelay = 0,
                Envelope = null,
                Parameters = new ConditionSet { Conditions = conditions }
            };
            p.Axes = new[] { (int)axis };
            p.Directions = new[] { 1 };
            return p;
        }

        private static EffectParameters BuildConstantParameters(JoystickOffset axis, ConstantForce force)
        {
            force.Magnitude = 0;

            var p = new EffectParameters
            {
                Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                Duration = -1,
                SamplePeriod = 0,
                Gain = GateGeometry.ForceMax,
                TriggerButton = -1,
                TriggerRepeatInterval = 0,
                StartDelay = 0,
                Envelope = null,
                Parameters = force
            };
            p.Axes = new[] { (int)axis };
            p.Directions = new[] { 1 };
            return p;
        }

        private Effect CreateAndStart(Guid guid, EffectParameters parameters)
        {
            Effect effect;
            try
            {
                effect = new Effect(_joystick, guid, parameters);
            }
            catch (SharpDXException)
            {
                // Some drivers reject -1 for an infinite duration; retry with a long finite one.
                parameters.Duration = int.MaxValue;
                effect = new Effect(_joystick, guid, parameters);
            }

            effect.Start(1, EffectPlayFlags.None);
            return effect;
        }

        /// <summary>
        /// Pushes a frame to the device, skipping writes that would not change the feel.
        /// Springs are written only when the preset actually changes (a few times a second
        /// at most); the constant forces are rate limited.
        /// </summary>
        public void Apply(ForceFrame frame, long nowMs)
        {
            if (IsFaulted) return;

            bool ok = true;

            if (!_springsPrimed || !frame.SpringX.Equals(_lastSpringX))
            {
                ok &= WriteCondition(_springX, _pSpringX, _condX, frame.SpringX, "springX");
                _lastSpringX = frame.SpringX;
            }

            if (!_springsPrimed || !frame.SpringY.Equals(_lastSpringY))
            {
                ok &= WriteCondition(_springY, _pSpringY, _condY, frame.SpringY, "springY");
                _lastSpringY = frame.SpringY;
            }

            _springsPrimed = true;

            if (!_constantXPrimed || ShouldWriteConstant(frame.ConstantX, _lastConstantX, _lastConstantXMs, nowMs))
            {
                if (WriteConstant(_constantX, _pConstantX, _forceX, frame.ConstantX, "constantX"))
                {
                    _lastConstantX = frame.ConstantX;
                    _lastConstantXMs = nowMs;
                    _constantXPrimed = true;
                }
                else ok = false;
            }

            if (!_constantYPrimed || ShouldWriteConstant(frame.ConstantY, _lastConstantY, _lastConstantYMs, nowMs))
            {
                if (WriteConstant(_constantY, _pConstantY, _forceY, frame.ConstantY, "constantY"))
                {
                    _lastConstantY = frame.ConstantY;
                    _lastConstantYMs = nowMs;
                    _constantYPrimed = true;
                }
                else ok = false;
            }

            if (_damper != null && frame.DamperCoefficient != _lastDamper)
            {
                if (WriteDamper(frame.DamperCoefficient)) _lastDamper = frame.DamperCoefficient;
                else ok = false;
            }

            if (ok) _strikes = 0;
        }

        private static bool ShouldWriteConstant(int value, int last, long lastMs, long nowMs)
        {
            if (value == last) return false;

            // Always land exactly on zero: leaving a residual force running is the one
            // failure mode that is felt in the hand.
            if (value == 0) return true;

            if (Math.Abs(value - last) < ConstantDeadband) return false;
            return nowMs - lastMs >= ConstantMinIntervalMs;
        }

        private bool WriteCondition(Effect effect, EffectParameters parameters, Condition[] conditions,
                                    SpringPreset preset, string name)
        {
            conditions[0].Offset = preset.Offset;
            conditions[0].PositiveCoefficient = preset.PositiveCoefficient;
            conditions[0].NegativeCoefficient = preset.NegativeCoefficient;
            conditions[0].PositiveSaturation = preset.PositiveSaturation;
            conditions[0].NegativeSaturation = preset.NegativeSaturation;
            conditions[0].DeadBand = preset.DeadBand;

            return SetParameters(effect, parameters, name);
        }

        private bool WriteDamper(int coefficient)
        {
            var set = _pDamper.Parameters as ConditionSet;
            if (set == null || set.Conditions == null) return true;

            for (int i = 0; i < set.Conditions.Length; i++)
            {
                set.Conditions[i].PositiveCoefficient = coefficient;
                set.Conditions[i].NegativeCoefficient = coefficient;
            }

            return SetParameters(_damper, _pDamper, "damper");
        }

        private bool WriteConstant(Effect effect, EffectParameters parameters, ConstantForce force,
                                   int magnitude, string name)
        {
            force.Magnitude = magnitude;
            return SetParameters(effect, parameters, name);
        }

        private bool SetParameters(Effect effect, EffectParameters parameters, string name)
        {
            if (effect == null) return true;

            try
            {
                effect.SetParameters(parameters,
                    EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.NoRestart);
                return true;
            }
            catch (SharpDXException)
            {
                try
                {
                    effect.SetParameters(parameters, EffectParameterFlags.TypeSpecificParameters);
                    return true;
                }
                catch (SharpDXException ex)
                {
                    _strikes++;
                    Log.ErrorThrottled("effect-" + name,
                        "Failed to update " + name + " (strike " + _strikes + " of " + MaxStrikes + ")", ex);
                    return false;
                }
            }
        }

        public void StopAll()
        {
            StopEffect(_springX);
            StopEffect(_springY);
            StopEffect(_constantX);
            StopEffect(_constantY);
            StopEffect(_damper);
        }

        private static void StopEffect(Effect effect)
        {
            if (effect == null) return;
            try { effect.Stop(); } catch { }
        }

        public void Dispose()
        {
            StopAll();
            DisposeEffect(ref _springX);
            DisposeEffect(ref _springY);
            DisposeEffect(ref _constantX);
            DisposeEffect(ref _constantY);
            DisposeEffect(ref _damper);
        }

        private static void DisposeEffect(ref Effect effect)
        {
            if (effect == null) return;
            try { effect.Dispose(); } catch { }
            effect = null;
        }
    }
}
