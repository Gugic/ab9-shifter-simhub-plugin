using System;
using System.Collections.Generic;
using AB9ActiveShifter.Core;
using vJoyInterfaceWrap;

namespace AB9ActiveShifter.Output
{
    /// <summary>What a probe found: whether vJoy is there at all, and which devices exist.</summary>
    public sealed class VJoyProbeResult
    {
        /// <summary>False when vJoy is absent, disabled, or the wrong architecture to load.</summary>
        public bool DriverPresent { get; set; }

        /// <summary>Why there is nothing to choose from. Null when the driver answered.</summary>
        public string Problem { get; set; }

        public List<VJoyDeviceInfo> Devices { get; set; }

        public VJoyProbeResult()
        {
            Devices = new List<VJoyDeviceInfo>();
        }
    }

    /// <summary>
    /// Asks vJoy which devices exist, so the settings UI can offer a choice instead of a number
    /// between 1 and 16 that the user finds out about afterwards.
    /// <para>
    /// This is the one place vJoy is touched from outside the engine thread, and deliberately so:
    /// every call here is a query - <c>vJoyEnabled</c>, <c>GetVJDStatus</c>,
    /// <c>GetVJDButtonNumber</c>, <c>GetOwnerPid</c> - and none acquires, releases or writes a
    /// button. It has to work from the UI thread because the moment a user most needs to pick a
    /// device is before the plugin is enabled, when there is no engine thread running at all.
    /// Nothing else about vJoy may follow it off the engine thread.
    /// </para>
    /// </summary>
    public static class VJoyDeviceProbe
    {
        /// <summary>vJoy's own ceiling.</summary>
        public const uint MaxDeviceId = 16;

        // One wrapper instance, reused. The settings page re-checks the chosen device every couple
        // of seconds so the tab gate notices a device being taken while it is open, and building a
        // wrapper each time to ask one question would be waste. UI thread only, like the rest of
        // this class. A failure is remembered so a machine without vJoy does not throw on a timer;
        // Forget() clears it, which is what the Refresh button is for after installing vJoy.
        private static vJoy _driver;
        private static string _driverProblem;

        /// <summary>Drops the cached driver, so the next probe tries again from scratch.</summary>
        public static void Forget()
        {
            _driver = null;
            _driverProblem = null;
        }

        private static vJoy Driver(out string problem)
        {
            if (_driver != null) { problem = null; return _driver; }
            if (_driverProblem != null) { problem = _driverProblem; return null; }

            try
            {
                vJoy driver = new vJoy();
                if (!driver.vJoyEnabled())
                {
                    _driverProblem = "vJoy is installed but not enabled, or no device has been created yet.";
                    problem = _driverProblem;
                    return null;
                }

                _driver = driver;
                problem = null;
                return _driver;
            }
            catch (Exception ex)
            {
                // Missing native DLL, wrong bitness, driver not installed: all the same story to
                // a user, and none of them a reason to take the settings page down.
                _driverProblem = "vJoy is not installed. The gate still works without it - you " +
                                 "just get no gear output until vJoy is running. (" + ex.Message + ")";
                problem = _driverProblem;
                return null;
            }
        }

        /// <summary>
        /// One device, for the cheap repeated check behind the tab gate. Reports Missing when
        /// vJoy itself is unavailable, which is the answer that matters to a caller asking
        /// "can this send gears".
        /// </summary>
        public static VJoyDeviceInfo ProbeOne(uint id)
        {
            string problem;
            vJoy driver = Driver(out problem);
            if (driver == null) return new VJoyDeviceInfo { Id = id, State = VJoyDeviceState.Missing };

            return Describe(driver, id);
        }

        /// <summary>
        /// Enumerates the devices vJoy reports. <paramref name="alwaysInclude"/> - normally the
        /// id currently saved - is listed even when it does not exist, so a picker can show the
        /// user's own setting back to them rather than silently selecting something else.
        /// </summary>
        public static VJoyProbeResult Probe(uint alwaysInclude)
        {
            VJoyProbeResult result = new VJoyProbeResult();

            vJoy driver;
            try
            {
                driver = new vJoy();
                if (!driver.vJoyEnabled())
                {
                    result.Problem = "vJoy is installed but not enabled, or no device has been created yet.";
                    return result;
                }
            }
            catch (Exception ex)
            {
                // Missing native DLL, wrong bitness, driver not installed: all the same story to
                // a user, and none of them a reason to take the settings page down.
                result.Problem = "vJoy is not installed. The gate still works without it - you " +
                                 "just get no gear output until vJoy is running. (" + ex.Message + ")";
                return result;
            }

            result.DriverPresent = true;

            for (uint id = 1; id <= MaxDeviceId; id++)
            {
                VJoyDeviceInfo info = Describe(driver, id);
                if (info.Exists || id == alwaysInclude) result.Devices.Add(info);
            }

            if (result.Devices.Count == 0)
            {
                result.Problem = "vJoy is running but has no devices. Create one in vJoyConf with " +
                                 "at least " + VJoyDeviceInfo.ButtonsNeeded + " buttons.";
            }

            return result;
        }

        private static VJoyDeviceInfo Describe(vJoy driver, uint id)
        {
            VJoyDeviceInfo info = new VJoyDeviceInfo { Id = id };

            try
            {
                switch (driver.GetVJDStatus(id))
                {
                    case VjdStat.VJD_STAT_FREE: info.State = VJoyDeviceState.Free; break;
                    case VjdStat.VJD_STAT_OWN: info.State = VJoyDeviceState.Owned; break;
                    case VjdStat.VJD_STAT_BUSY: info.State = VJoyDeviceState.Busy; break;
                    case VjdStat.VJD_STAT_MISS: info.State = VJoyDeviceState.Missing; return info;
                    default: info.State = VJoyDeviceState.Unknown; return info;
                }

                info.Buttons = driver.GetVJDButtonNumber(id);

                if (info.State == VJoyDeviceState.Busy)
                {
                    info.OwnerPid = driver.GetOwnerPid(id);
                    info.OwnerName = VJoyGearOutput.DescribeProcess(info.OwnerPid);
                }
            }
            catch (Exception)
            {
                // One uncooperative slot must not cost the user the other fifteen.
                info.State = VJoyDeviceState.Unknown;
            }

            return info;
        }
    }
}
