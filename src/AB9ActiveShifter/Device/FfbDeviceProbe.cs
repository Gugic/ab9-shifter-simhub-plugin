using SharpDX.DirectInput;

namespace AB9ActiveShifter.Device
{
    /// <summary>
    /// Query-only attachment check for setup, including while the master switch is off.
    /// It never acquires a controller, sets its properties, or creates/writes an effect.
    /// Run on the settings refresh worker, not on each UI or force tick. Null means the
    /// query failed and must not be reported as a missing base.
    /// </summary>
    public static class FfbDeviceProbe
    {
        public static bool? IsPresent(int vendor, int product)
        {
            try
            {
                using (var input = new DirectInput())
                {
                    bool incomplete = false;
                    foreach (DeviceInstance device in input.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
                    {
                        try
                        {
                            using (var controller = new Joystick(input, device.InstanceGuid))
                                if (controller.Properties.VendorId == vendor && controller.Properties.ProductId == product)
                                    return true;
                        }
                        catch { incomplete = true; }
                    }
                    return incomplete ? (bool?)null : false;
                }
            }
            catch { return null; }
        }
    }
}
