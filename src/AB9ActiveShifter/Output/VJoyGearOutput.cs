using System;
using System.Diagnostics;
using vJoyInterfaceWrap;

namespace AB9ActiveShifter.Output
{
    /// <summary>
    /// Publishes the selected gear as a held vJoy button, so any game can bind gears the
    /// same way it would bind a real H-shifter.
    ///
    /// Gear i maps to button i, with 8 as reverse. Exactly one button is ever held; the
    /// previous one is released before the new one is set so a game never sees two gears.
    /// All calls come from the engine thread except <see cref="ReleaseAll"/>, which the
    /// watchdog may invoke to clear a stuck gear.
    ///
    /// PRND rides the same path with its position buttons 11-14, because "exactly one held, the
    /// old one released first" is precisely what a selector needs too - and doing it here means
    /// the shutdown ordering, the watchdog's clear and the profile switch all cover it without
    /// knowing the pattern. That is the reason <see cref="GearCount"/> reaches past the gears:
    /// it bounds what this may press, not what a gear is.
    /// </summary>
    public sealed class VJoyGearOutput : IGearOutput, IHandbrakeOutput
    {
        /// <summary>Highest button <see cref="SetGear"/> will hold: 8 gears, then PRND's 11-14.</summary>
        public const int GearCount = 14;

        private readonly vJoy _vjoy = new vJoy();
        private readonly uint _deviceId;
        private readonly object _sync = new object();

        private bool _acquired;
        private bool _hasOwnedDevice;
        private int _heldGear;
        private readonly bool _useHandbrake;
        private bool _handbrakeAvailable;
        private long _axisMinimum;
        private long _axisMaximum;
        private int _lastBrake = -1;
        private bool _axisFaulted;

        public bool HandbrakeAvailable { get { return IsConnected && _handbrakeAvailable; } }

        private void ProbeHandbrakeAxis()
        {
            _handbrakeAvailable = _vjoy.GetVJDAxisExist(_deviceId, HID_USAGES.HID_USAGE_Z)
                && _vjoy.GetVJDAxisMin(_deviceId, HID_USAGES.HID_USAGE_Z, ref _axisMinimum)
                && _vjoy.GetVJDAxisMax(_deviceId, HID_USAGES.HID_USAGE_Z, ref _axisMaximum)
                && _axisMinimum >= 0 && _axisMaximum > _axisMinimum && _axisMaximum <= int.MaxValue;
            if (!_handbrakeAvailable)
            {
                LastError = "Handbrake output needs the Z axis enabled in vJoyConf. Re-enable the plugin after changing it.";
                Log.Warn(LastError);
            }
            else SetHandbrake(0);
        }

        public VJoyGearOutput(uint deviceId, bool handbrake = false)
        {
            _deviceId = deviceId == 0 ? 1u : deviceId;
            _useHandbrake = handbrake;
        }

        public bool IsConnected { get { return _acquired && !_axisFaulted; } }

        public string LastError { get; private set; }

        public int HeldGear { get { return _heldGear; } }

        public bool CheckConnection()
        {
            lock (_sync)
            {
                if (!_acquired) return false;
                try
                {
                    if (_vjoy.GetVJDStatus(_deviceId) != VjdStat.VJD_STAT_OWN)
                    {
                        _hasOwnedDevice = false;
                        LostOwnership("vJoy device " + _deviceId + " is no longer owned by this plugin; reconnecting automatically.");
                    }
                }
                catch (Exception ex) { LostOwnership("Could not check vJoy ownership: " + ex.Message); }
                return IsConnected;
            }
        }

        private void LostOwnership(string reason)
        {
            _acquired = false;
            _heldGear = 0;
            _handbrakeAvailable = false;
            _lastBrake = -1;
            LastError = reason;
        }

        public bool Connect()
        {
            lock (_sync)
            {
                if (IsConnected) return true;

                try
                {
                    if (_acquired) Disconnect();
                    if (!_vjoy.vJoyEnabled())
                    {
                        LastError = "vJoy driver is not enabled. Install vJoy and enable at least one device.";
                        return false;
                    }

                    VjdStat status = _vjoy.GetVJDStatus(_deviceId);
                    switch (status)
                    {
                        case VjdStat.VJD_STAT_MISS:
                            LastError = "vJoy device " + _deviceId +
                                        " does not exist. Create it in vJoyConf (needs at least " + GearCount + " buttons).";
                            return false;

                        case VjdStat.VJD_STAT_BUSY:
                            LastError = "vJoy device " + _deviceId + " is owned by another program (PID " +
                                        _vjoy.GetOwnerPid(_deviceId) + "). Close it or pick a different vJoy device.";
                            return false;

                        case VjdStat.VJD_STAT_UNKN:
                            LastError = "vJoy device " + _deviceId + " is in an unknown state.";
                            return false;
                    }

                    if (status != VjdStat.VJD_STAT_OWN && !_vjoy.AcquireVJD(_deviceId))
                    {
                        LastError = "Could not acquire vJoy device " + _deviceId + ".";
                        return false;
                    }

                    _hasOwnedDevice = true;
                    int buttons = _vjoy.GetVJDButtonNumber(_deviceId);
                    if (!_useHandbrake && buttons < Core.VJoyDeviceInfo.ButtonsNeeded)
                    {
                        // Not fatal: whatever buttons exist still work, so run and tell the
                        // user what is missing. Gears use 1..8; the sequential up/down pulses
                        // use 9/10 and PRND's positions 11-14, each kept above the last so no
                        // binding means two things.
                        LastError = "vJoy device " + _deviceId + " exposes only " + buttons +
                                    " buttons; gears use 1-8, sequential up/down use 9-10 and " +
                                    "PRND uses 11-14. Raise the button count in vJoyConf.";
                        Log.Warn(LastError);
                    }
                    else
                    {
                        LastError = null;
                    }

                    if (!_vjoy.ResetButtons(_deviceId))
                    {
                        LostOwnership("Could not clear vJoy device " + _deviceId + " while connecting.");
                        return false;
                    }
                    _heldGear = 0;
                    _axisFaulted = false;
                    _lastBrake = -1;
                    _handbrakeAvailable = false;
                    _acquired = true;
                    if (_useHandbrake) ProbeHandbrakeAxis();
                    Log.Info("vJoy device " + _deviceId + " acquired (" + buttons + " buttons).");
                    return IsConnected;
                }
                catch (Exception ex)
                {
                    LastError = "vJoy initialisation failed: " + ex.Message;
                    _axisFaulted = true;
                    Log.Error("vJoy initialisation failed", ex);
                    return false;
                }
            }
        }

        public void SetGear(int gear)
        {
            lock (_sync)
            {
                if (!_acquired || gear == _heldGear) return;

                try
                {
                    // Release first: a game must never observe two gears held at once.
                    if (_heldGear >= 1 && _heldGear <= GearCount)
                    {
                        if (!_vjoy.SetBtn(false, _deviceId, (uint)_heldGear))
                        {
                            LostOwnership("vJoy did not release the previous gear; reconnecting automatically.");
                            return;
                        }
                    }

                    if (gear >= 1 && gear <= GearCount)
                    {
                        if (!_vjoy.SetBtn(true, _deviceId, (uint)gear))
                        {
                            LostOwnership("vJoy did not accept the gear; reconnecting automatically.");
                            return;
                        }
                    }

                    _heldGear = gear;
                }
                catch (Exception ex)
                {
                    Log.ErrorThrottled("vjoy-setbtn", "vJoy button update failed", ex);
                    LostOwnership("vJoy button update failed: " + ex.Message);
                }
            }
        }

        public void SetButton(int button, bool down)
        {
            lock (_sync)
            {
                if (!_acquired || button < 1) return;

                try
                {
                    if (!_vjoy.SetBtn(down, _deviceId, (uint)button))
                        LostOwnership("vJoy did not accept the button update; reconnecting automatically.");
                }
                catch (Exception ex)
                {
                    Log.ErrorThrottled("vjoy-setbtn", "vJoy button update failed", ex);
                    LostOwnership("vJoy button update failed: " + ex.Message);
                }
            }
        }

        public void SetHandbrake(int value)
        {
            lock (_sync)
            {
                if (!HandbrakeAvailable) return;
                int axis = Core.HandbrakeModel.AxisValue(value, _axisMinimum, _axisMaximum);
                if (axis == _lastBrake) return;
                try
                {
                    if (!_vjoy.SetAxis(axis, _deviceId, HID_USAGES.HID_USAGE_Z))
                        throw new InvalidOperationException("vJoy rejected the handbrake axis update.");
                    _lastBrake = axis;
                }
                catch (Exception ex)
                {
                    AxisFailed(ex);
                }
            }
        }

        private void AxisFailed(Exception ex)
        {
            LastError = "Handbrake output failed: " + ex.Message;
            Log.ErrorThrottled("vjoy-axis", LastError, ex);
            _axisFaulted = true;
            _lastBrake = -1;
        }

        // Teardown tries zero even after an axis write fault. Ordinary ticks respect the
        // fault flag and leave retries to the engine's backoff, never one retry per tick.
        private void ReleaseHandbrake()
        {
            if (!_acquired || !_handbrakeAvailable) return;
            try
            {
                if (!_vjoy.SetAxis((int)_axisMinimum, _deviceId, HID_USAGES.HID_USAGE_Z))
                    throw new InvalidOperationException("vJoy rejected the handbrake release.");
                _lastBrake = (int)_axisMinimum;
            }
            catch (Exception ex) { AxisFailed(ex); }
        }

        public void ReleaseAll()
        {
            lock (_sync)
            {
                if (!_acquired) return;
                ReleaseHandbrake();
                try
                {
                    if (!_vjoy.ResetButtons(_deviceId))
                    {
                        LostOwnership("vJoy did not clear its buttons; reconnecting automatically.");
                        return;
                    }
                    _heldGear = 0;
                }
                catch (Exception ex)
                {
                    Log.ErrorThrottled("vjoy-reset", "vJoy button reset failed", ex);
                    LostOwnership("vJoy button reset failed: " + ex.Message);
                }
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (!_acquired && !_hasOwnedDevice) return;
                try
                {
                    // A failed write can clear our cache while the driver still owns the
                    // device. Only relinquish our ownership; never reset someone else's.
                    if (_vjoy.GetVJDStatus(_deviceId) != VjdStat.VJD_STAT_OWN) return;
                    ReleaseHandbrake();
                    _vjoy.ResetButtons(_deviceId);
                    _vjoy.RelinquishVJD(_deviceId);
                    Log.Info("vJoy device " + _deviceId + " released.");
                }
                catch (Exception ex)
                {
                    Log.Error("vJoy release failed", ex);
                }
                finally
                {
                    _heldGear = 0;
                    _acquired = false;
                    _hasOwnedDevice = false;
                    _handbrakeAvailable = false;
                    _axisFaulted = false;
                    _lastBrake = -1;
                }
            }
        }

        /// <summary>Owning process id of the configured device, for UI diagnostics. 0 when free.</summary>
        public int OwnerPid
        {
            get
            {
                try { return _vjoy.GetOwnerPid(_deviceId); }
                catch { return 0; }
            }
        }

        public static string DescribeProcess(int pid)
        {
            if (pid <= 0) return "";
            try { return Process.GetProcessById(pid).ProcessName; }
            catch { return "pid " + pid; }
        }
    }
}
