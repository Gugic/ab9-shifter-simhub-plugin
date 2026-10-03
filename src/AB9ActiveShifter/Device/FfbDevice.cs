using System;
using System.Collections.Generic;
using System.Linq;
using AB9ActiveShifter.Core;
using SharpDX;
using SharpDX.DirectInput;

namespace AB9ActiveShifter.Device
{
    /// <summary>
    /// Owns the DirectInput handle to the AB9. Every method here must be called from the
    /// engine thread; the one exception is <see cref="StopForces"/>, which the watchdog may
    /// call to kill output if the loop stops ticking.
    ///
    /// The device is taken exclusive (required to create force feedback effects) and
    /// background (so forces stay live while the game, not SimHub, has focus).
    /// </summary>
    public sealed class FfbDevice : IDisposable
    {
        private DirectInput _directInput;
        private Joystick _joystick;
        private bool _acquired;

        public string ProductName { get; private set; }
        public bool IsOpen { get { return _joystick != null && _acquired; } }
        internal Joystick Joystick { get { return _joystick; } }

        /// <summary>
        /// Opens the device matching the configured VID/PID. Returns false with a message
        /// the UI can show verbatim - most failures here are the user's to fix (wrong mode,
        /// MOZA software holding the device, Steam Input grabbing it).
        /// </summary>
        public bool Open(EngineConfig cfg, IntPtr hwnd, out string error)
        {
            Close();

            try
            {
                _directInput = new DirectInput();

                IList<DeviceInstance> devices =
                    _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

                DeviceInstance match = null;
                var seen = new List<string>();

                foreach (DeviceInstance instance in devices)
                {
                    Joystick probe = null;
                    try
                    {
                        probe = new Joystick(_directInput, instance.InstanceGuid);
                        int vid = probe.Properties.VendorId;
                        int pid = probe.Properties.ProductId;
                        seen.Add(string.Format("{0} (VID {1:X4} PID {2:X4})", instance.ProductName, vid, pid));

                        if (vid == cfg.VendorId && pid == cfg.ProductId)
                        {
                            match = instance;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Skipping device " + instance.ProductName + ": " + ex.Message);
                    }
                    finally
                    {
                        if (probe != null) probe.Dispose();
                    }
                }

                if (match == null)
                {
                    error = string.Format(
                        "No device with VID {0:X4} / PID {1:X4} found. Detected: {2}",
                        cfg.VendorId, cfg.ProductId,
                        seen.Count == 0 ? "no game controllers" : string.Join("; ", seen));
                    Close();
                    return false;
                }

                ProductName = match.ProductName;
                _joystick = new Joystick(_directInput, match.InstanceGuid);

                if (hwnd == IntPtr.Zero)
                {
                    error = "No window handle available for DirectInput.";
                    Close();
                    return false;
                }

                _joystick.SetCooperativeLevel(hwnd, CooperativeLevel.Exclusive | CooperativeLevel.Background);

                // Both of these must be set before acquiring.
                try
                {
                    _joystick.Properties.Range = new InputRange(GateGeometry.AxisMin, GateGeometry.AxisMax);
                }
                catch (Exception ex)
                {
                    Log.Warn("Device rejected an explicit 0..65535 axis range (" + ex.Message +
                             "); using its native range and clamping.");
                }

                try
                {
                    // Measured: this base centres in firmware and ignores this property. The
                    // real control is MOZA Cockpit's Spring = 0 - Pit House has no such
                    // setting in flight mode. Asked anyway, in case another unit honours it.
                    _joystick.Properties.AutoCenter = false;
                }
                catch (Exception ex)
                {
                    Log.Debug("AutoCenter could not be disabled: " + ex.Message);
                }

                _joystick.Acquire();
                _acquired = true;

                _joystick.SendForceFeedbackCommand(ForceFeedbackCommand.Reset);

                int effectCount = 0;
                try { effectCount = _joystick.GetEffects().Count; }
                catch (Exception ex) { Log.Debug("GetEffects failed: " + ex.Message); }

                if (effectCount == 0)
                {
                    error = "Device '" + ProductName + "' reports no force feedback effects. " +
                            "Check that the base is in flight mode with FFB Mode set to DirectInput " +
                            "in MOZA Cockpit, and that Cockpit and Pit House are fully closed.";
                    Close();
                    return false;
                }

                Log.Info(string.Format("Opened '{0}' exclusive+background, {1} effect types available.",
                    ProductName, effectCount));
                error = null;
                return true;
            }
            catch (SharpDXException ex)
            {
                error = DescribeAcquireFailure(ex);
                Close();
                return false;
            }
            catch (Exception ex)
            {
                error = "Failed to open the device: " + ex.Message;
                Close();
                return false;
            }
        }

        /// <summary>
        /// What the last failure meant, as far as the driver would say. Read by the engine right
        /// after a false return, because the repair for a device someone else has taken is the
        /// opposite of the repair for one that has gone.
        /// </summary>
        public DeviceFault LastFault { get; private set; }

        private string DescribeAcquireFailure(SharpDXException ex)
        {
            LastFault = DeviceFaults.Classify(ex.HResult);

            // DIERR_OTHERAPPHASPRIO is what an exclusive grab by another program looks like.
            if (LastFault == DeviceFault.TakenByAnotherApp
                || ex.Message.IndexOf("other", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LastFault = DeviceFault.TakenByAnotherApp;
                return "The stick is held exclusively by another program. Close MOZA Cockpit and any " +
                       "Pit House tuning page, and disable Steam Input for this device. (" + ex.Message + ")";
            }

            return "DirectInput error: " + ex.Message;
        }

        /// <summary>Reads the stick. Attempts one re-acquire on device loss before giving up.</summary>
        public bool TryPoll(out int x, out int y, out string error)
        {
            x = GateGeometry.AxisCenter;
            y = GateGeometry.AxisCenter;
            error = null;

            if (_joystick == null)
            {
                error = "Device is not open.";
                return false;
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    _joystick.Poll();
                    JoystickState state = _joystick.GetCurrentState();
                    x = GateGeometry.Clamp(state.X, GateGeometry.AxisMin, GateGeometry.AxisMax);
                    y = GateGeometry.Clamp(state.Y, GateGeometry.AxisMin, GateGeometry.AxisMax);
                    return true;
                }
                catch (SharpDXException ex)
                {
                    if (attempt == 0)
                    {
                        try
                        {
                            _joystick.Acquire();
                            _acquired = true;
                            LastFault = DeviceFault.Unknown;
                            continue;
                        }
                        catch (SharpDXException reacquire)
                        {
                            // The poll's own code does not say who took the device - a focus
                            // change and an unplugged cable both read as INPUTLOST. The failure of
                            // the re-acquire does, so this is where the classification is made.
                            LastFault = DeviceFaults.Classify(reacquire.HResult);
                            error = "Lost the device and could not re-acquire it: " + reacquire.Message;
                            _acquired = false;
                            return false;
                        }
                        catch (Exception reacquire)
                        {
                            LastFault = DeviceFault.Unknown;
                            error = "Lost the device and could not re-acquire it: " + reacquire.Message;
                            _acquired = false;
                            return false;
                        }
                    }

                    LastFault = DeviceFaults.Classify(ex.HResult);
                    error = "Lost the device: " + ex.Message;
                    _acquired = false;
                    return false;
                }
            }

            return false;
        }

        public EffectSet CreateEffects(int damperCoefficient, out string error)
        {
            if (_joystick == null)
            {
                error = "Device is not open.";
                return null;
            }

            return EffectSet.Create(_joystick, damperCoefficient, out error);
        }

        /// <summary>
        /// Kills all output immediately. Safe to call from the watchdog: it swallows
        /// everything, because by the time it runs the device may already be gone.
        /// </summary>
        /// <summary>
        /// Asks the device what it is doing with the forces it is being sent - which is a
        /// different question from whether the writes succeeded, and the only one that can catch
        /// a base that has stopped producing torque while still accepting everything sent to it.
        /// Returns <see cref="ForceOutputHealth.Unknown"/> rather than throwing if the device
        /// will not answer; a driver that dislikes the query must not take the loop down.
        ///
        /// <paramref name="effectsStillHeld"/> is the caller's own answer to the one question the
        /// device is unreliable about - see <c>EffectSet.AnyStillDownloaded</c>. Both facts go to
        /// <see cref="ForceFeedbackHealth.Classify"/>, which owns the rule that it takes both to
        /// convict; this method only reads flags off a device.
        /// </summary>
        public ForceOutputHealth ReadForceOutputHealth(bool effectsStillHeld)
        {
            if (_joystick == null || !_acquired) return ForceOutputHealth.Unknown;

            try
            {
                ForceFeedbackState s = _joystick.GetForceFeedbackState();

                return ForceFeedbackHealth.Classify(
                    deviceLost: (s & ForceFeedbackState.DeviceLost) != 0,
                    powerOff: (s & ForceFeedbackState.PowerOff) != 0,
                    safetyCutout: (s & ForceFeedbackState.SafetySwitchOff) != 0
                                  || (s & ForceFeedbackState.UserSafetySwitchOff) != 0,
                    actuatorsOff: (s & ForceFeedbackState.ActuatorsOff) != 0,
                    stoppedOrPaused: (s & ForceFeedbackState.Stopped) != 0
                                     || (s & ForceFeedbackState.Paused) != 0,
                    deviceSaysEmpty: (s & ForceFeedbackState.Empty) != 0,
                    effectsStillHeld: effectsStillHeld);
            }
            catch (Exception)
            {
                return ForceOutputHealth.Unknown;
            }
        }

        /// <summary>
        /// Best-effort nudge for a device that has stopped playing: reset it and switch the
        /// actuators back on. Whether it takes is the caller's problem to observe on the next
        /// health read - this only asks.
        /// </summary>
        public void TryWakeForceFeedback()
        {
            if (_joystick == null || !_acquired) return;

            try { _joystick.SendForceFeedbackCommand(ForceFeedbackCommand.SetActuatorsOn); }
            catch (Exception) { }

            try { _joystick.SendForceFeedbackCommand(ForceFeedbackCommand.Continue); }
            catch (Exception) { }
        }

        public void StopForces()
        {
            try
            {
                if (_joystick != null && _acquired)
                {
                    _joystick.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll);
                }
            }
            catch { }
        }

        public void Close()
        {
            StopForces();

            try
            {
                if (_joystick != null)
                {
                    if (_acquired) _joystick.Unacquire();
                    _joystick.Dispose();
                }
            }
            catch { }
            finally
            {
                _joystick = null;
                _acquired = false;
            }

            try
            {
                if (_directInput != null) _directInput.Dispose();
            }
            catch { }
            finally
            {
                _directInput = null;
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
