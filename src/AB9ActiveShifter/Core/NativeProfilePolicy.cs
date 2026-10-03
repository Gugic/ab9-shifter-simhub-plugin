namespace AB9ActiveShifter.Core
{
    /// <summary>The master provider switch gates hardware, never the portability of a profile.</summary>
    public static class NativeProfilePolicy
    {
        public static bool CanActivate(OperatingMode mode)
        {
            return mode == OperatingMode.GenericFfbStick || mode == OperatingMode.Ab9Native;
        }

        public static bool CanRunVirtual(OperatingMode mode,
            int vendor, int product, Ab9NativeSnapshot hardware, bool ab9ModeUnsafe)
        {
            if (!CanActivate(mode)) return false;
            if (mode == OperatingMode.Ab9Native && (hardware == null || !hardware.CanManage
                || hardware.InputMode != 0 || !Ab9NativeProtocol.IsSupported(vendor, product, hardware.Firmware))) return false;
            return !(vendor == Ab9NativeProtocol.VendorId
                && product == Ab9NativeProtocol.ProductId && ab9ModeUnsafe);
        }

        public static bool CanOwnGearOutput(OperatingMode mode, int vendor, int product, bool ab9InHPattern)
        {
            return CanActivate(mode) && !(vendor == Ab9NativeProtocol.VendorId
                && product == Ab9NativeProtocol.ProductId && ab9InHPattern);
        }
    }
}
