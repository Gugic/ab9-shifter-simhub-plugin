using System;
using System.Collections.Generic;
using AB9ActiveShifter.Core;
using SharpDX.DirectInput;

namespace AB9ActiveShifter.Device
{
    /// <summary>
    /// A second DirectInput handle, on whatever controller holds the clutch pedal.
    ///
    /// <para>
    /// Opened <b>non-exclusive</b> and background, and that is the whole safety story of this
    /// class. The base is taken exclusive because creating force feedback effects requires it;
    /// pedals must not be, because the game needs to read them too, and an exclusive grab here
    /// would silently take the clutch away from whatever is being driven. Nothing in this class
    /// creates an effect or writes anything - it only ever reads.
    /// </para>
    /// <para>
    /// Like <see cref="FfbDevice"/>, every method belongs to the engine thread. The one exception
    /// is <see cref="Enumerate"/>, which is static, query-only, and opens nothing that outlives
    /// the call, so the settings UI can populate a picker without going near the running loop.
    /// </para>
    /// </summary>
    public sealed class PedalDevice : IDisposable
    {
        /// <summary>
        /// The axis order every reading is flattened into. Fixed, and fixed forever: a saved
        /// calibration stores an index into this list, so reordering it would quietly rebind a
        /// user's clutch to their brake.
        /// </summary>
        public const int AxisCount = 8;

        private DirectInput _directInput;
        private Joystick _joystick;
        private bool _acquired;

        public string DeviceId { get; private set; }
        public string ProductName { get; private set; }
        public bool IsOpen { get { return _joystick != null && _acquired; } }

        /// <summary>
        /// Every attached game controller, for the picker. Opens each one briefly and disposes
        /// it, so nothing is held and no cooperative level is set.
        /// </summary>
        public static List<PedalDeviceInfo> Enumerate(int shifterVendorId, int shifterProductId)
        {
            var found = new List<PedalDeviceInfo>();

            DirectInput di = null;
            try
            {
                di = new DirectInput();

                foreach (DeviceInstance instance in
                         di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
                {
                    Joystick probe = null;
                    try
                    {
                        probe = new Joystick(di, instance.InstanceGuid);

                        found.Add(new PedalDeviceInfo
                        {
                            Id = instance.InstanceGuid.ToString(),
                            Name = instance.ProductName,
                            AxisCount = AxisCount,
                            IsTheShifterBase = probe.Properties.VendorId == shifterVendorId
                                               && probe.Properties.ProductId == shifterProductId
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Skipping controller " + instance.ProductName + ": " + ex.Message);
                    }
                    finally
                    {
                        if (probe != null) probe.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not enumerate controllers for the pedal picker: " + ex.Message);
            }
            finally
            {
                if (di != null) di.Dispose();
            }

            return found;
        }

        /// <summary>Opens the device by its instance id. Returns false with a reason for the UI.</summary>
        public bool Open(string deviceId, IntPtr hwnd, out string error)
        {
            Close();

            if (string.IsNullOrEmpty(deviceId))
            {
                error = "No pedal device chosen.";
                return false;
            }

            Guid guid;
            try
            {
                guid = new Guid(deviceId);
            }
            catch (Exception)
            {
                error = "The saved pedal device id is not readable.";
                return false;
            }

            try
            {
                _directInput = new DirectInput();
                _joystick = new Joystick(_directInput, guid);
                DeviceId = deviceId;
                ProductName = _joystick.Information.ProductName;

                // NonExclusive is the point: the game reads these pedals too. Background so the
                // reading survives the game having focus, exactly like the base.
                _joystick.SetCooperativeLevel(hwnd,
                    CooperativeLevel.NonExclusive | CooperativeLevel.Background);

                _joystick.Acquire();
                _acquired = true;

                Log.Info("Reading the clutch from '" + ProductName + "' (non-exclusive).");
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not open the pedal device: " + ex.Message;
                Close();
                return false;
            }
        }

        /// <summary>
        /// Reads every axis into <paramref name="axes"/>, in the fixed order. Returns false on
        /// device loss after one re-acquire attempt - and losing the pedals is never fatal, so
        /// the caller falls back to telemetry rather than stopping the gate.
        /// </summary>
        public bool TryPoll(int[] axes)
        {
            if (_joystick == null || axes == null || axes.Length < AxisCount) return false;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    _joystick.Poll();
                    JoystickState s = _joystick.GetCurrentState();

                    axes[0] = s.X;
                    axes[1] = s.Y;
                    axes[2] = s.Z;
                    axes[3] = s.RotationX;
                    axes[4] = s.RotationY;
                    axes[5] = s.RotationZ;

                    int[] sliders = s.Sliders;
                    axes[6] = sliders != null && sliders.Length > 0 ? sliders[0] : 0;
                    axes[7] = sliders != null && sliders.Length > 1 ? sliders[1] : 0;

                    return true;
                }
                catch (Exception)
                {
                    if (attempt == 0)
                    {
                        try
                        {
                            _joystick.Acquire();
                            _acquired = true;
                            continue;
                        }
                        catch (Exception)
                        {
                            _acquired = false;
                            return false;
                        }
                    }

                    _acquired = false;
                    return false;
                }
            }

            return false;
        }

        public void Close()
        {
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
