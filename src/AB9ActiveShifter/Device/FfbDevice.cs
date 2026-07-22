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
                    // The base's own centring would fight the gate. Pit House "Spring = 0" is
                    // the real control, but ask the driver too in case it honours this.
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
                            "Check that the base is in flight mode and that MOZA Pit House is not holding it.";
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

        private static string DescribeAcquireFailure(SharpDXException ex)
        {
            // DIERR_OTHERAPPHASPRIO is what an exclusive grab by another program looks like.
            if (unchecked((uint)ex.HResult) == 0x80070005 || ex.Message.IndexOf("other", StringComparison.OrdinalIgnoreCase) >= 0)
            {
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
                            continue;
                        }
                        catch (Exception reacquire)
                        {
                            error = "Lost the device and could not re-acquire it: " + reacquire.Message;
                            _acquired = false;
                            return false;
                        }
                    }

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
