namespace AB9ActiveShifter.Core
{
    /// <summary>Native profiles never fall back to a virtual gate or a different FFB stick.</summary>
    public static class NativeProfilePolicy
    {
        public static bool CanActivate(bool nativeProfile, int vendor, int product, Ab9NativeSnapshot hardware)
        {
            return !nativeProfile || (hardware != null && hardware.CanManage && hardware.IsNative
                && Ab9NativeProtocol.IsSupported(vendor, product, hardware.Firmware));
        }

        public static bool CanRunVirtual(bool nativeProfile, int vendor, int product, bool ab9InNativeMode)
        {
            return !nativeProfile && !(vendor == Ab9NativeProtocol.VendorId
                && product == Ab9NativeProtocol.ProductId && ab9InNativeMode);
        }
    }
}
