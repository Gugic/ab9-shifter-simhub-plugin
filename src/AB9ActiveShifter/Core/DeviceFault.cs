namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Why the base stopped answering, to the extent the driver will say.
    ///
    /// The distinction that matters is <see cref="TakenByAnotherApp"/> against everything else,
    /// because the repair is opposite. A device that has gone wants reopening as fast as the
    /// backoff allows. A device another application has taken wants leaving alone: reopening it is
    /// not a repair, it is a theft, and it is what crashes the game that took it.
    /// </summary>
    public enum DeviceFault
    {
        /// <summary>The driver did not say, or said something that means neither of the below.</summary>
        Unknown,

        /// <summary>The device has left the bus. Reopen when it comes back.</summary>
        Gone,

        /// <summary>Another process holds it. Do not reach for it.</summary>
        TakenByAnotherApp
    }

    /// <summary>
    /// Reads a DirectInput HRESULT as one of <see cref="DeviceFault"/>.
    ///
    /// Pure and in Core so it can be tested without a device, and so the numbers live in one place
    /// rather than being re-recognised at each catch site. The values are SharpDX's own
    /// `ResultCode` constants, read off the shipped assembly rather than remembered:
    /// `OtherApplicationHasPriority` really is `E_ACCESSDENIED`, which is not guessable.
    /// </summary>
    public static class DeviceFaults
    {
        /// <summary>DIERR_NOTEXCLUSIVEACQUIRED. Our exclusive acquisition was revoked.</summary>
        public const int NotExclusiveAcquired = unchecked((int)0x80040205);

        /// <summary>DIERR_OTHERAPPHASPRIO, which is E_ACCESSDENIED. A foreground app owns it.</summary>
        public const int OtherApplicationHasPriority = unchecked((int)0x80070005);

        /// <summary>ERROR_DEVICE_NOT_CONNECTED. The one this base logs when it leaves the bus.</summary>
        public const int DeviceNotConnected = unchecked((int)0x8007048F);

        /// <summary>DIERR_UNPLUGGED.</summary>
        public const int Unplugged = unchecked((int)0x80040209);

        /// <summary>
        /// DIERR_INPUTLOST and DIERR_NOTACQUIRED are deliberately <see cref="DeviceFault.Unknown"/>.
        /// Both mean "you no longer have this device" without saying who took it or whether anyone
        /// did - a focus change and an unplugged cable produce the same code - so they keep the old
        /// behaviour of trying to re-acquire once, and it is the failure of THAT attempt which says
        /// what is really going on.
        /// </summary>
        public static DeviceFault Classify(int hresult)
        {
            switch (hresult)
            {
                case NotExclusiveAcquired:
                case OtherApplicationHasPriority:
                    return DeviceFault.TakenByAnotherApp;

                case DeviceNotConnected:
                case Unplugged:
                    return DeviceFault.Gone;

                default:
                    return DeviceFault.Unknown;
            }
        }
    }
}
