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
    public sealed class VJoyGearOutput : IGearOutput
    {
        /// <summary>Highest button <see cref="SetGear"/> will hold: 8 gears, then PRND's 11-14.</summary>
        public const int GearCount = 14;

        private readonly vJoy _vjoy = new vJoy();
        private readonly uint _deviceId;
        private readonly object _sync = new object();

        private bool _acquired;
        private int _heldGear;

        public VJoyGearOutput(uint deviceId)
        {
            _deviceId = deviceId == 0 ? 1u : deviceId;
        }

        public bool IsConnected { get { return _acquired; } }

        public string LastError { get; private set; }

        public int HeldGear { get { return _heldGear; } }

        public bool Connect()
        {
            lock (_sync)
            {
                if (_acquired) return true;

                try
                {
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

                    int buttons = _vjoy.GetVJDButtonNumber(_deviceId);
                    if (buttons < Core.VJoyDeviceInfo.ButtonsNeeded)
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

                    _vjoy.ResetButtons(_deviceId);
                    _heldGear = 0;
                    _acquired = true;
                    Log.Info("vJoy device " + _deviceId + " acquired (" + buttons + " buttons).");
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = "vJoy initialisation failed: " + ex.Message;
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
                        _vjoy.SetBtn(false, _deviceId, (uint)_heldGear);
                    }

                    if (gear >= 1 && gear <= GearCount)
                    {
                        _vjoy.SetBtn(true, _deviceId, (uint)gear);
                    }

                    _heldGear = gear;
                }
                catch (Exception ex)
                {
                    Log.ErrorThrottled("vjoy-setbtn", "vJoy button update failed", ex);
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
                    _vjoy.SetBtn(down, _deviceId, (uint)button);
                }
                catch (Exception ex)
                {
                    Log.ErrorThrottled("vjoy-setbtn", "vJoy button update failed", ex);
                }
            }
        }

        public void ReleaseAll()
        {
            lock (_sync)
            {
                if (!_acquired) return;
                try
                {
                    _vjoy.ResetButtons(_deviceId);
                    _heldGear = 0;
                }
                catch (Exception ex)
                {
                    Log.ErrorThrottled("vjoy-reset", "vJoy button reset failed", ex);
                }
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (!_acquired) return;
                try
                {
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
