namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// One controller as the pedal picker shows it. Restated here, in Core, for the same reason
    /// <see cref="VJoyDeviceInfo"/> restates vJoy's status: the sentence a user reads while
    /// choosing a device is worth testing, and Core holds no driver types.
    /// </summary>
    public sealed class PedalDeviceInfo
    {
        /// <summary>Stable per-device identity - the DirectInput instance GUID as a string.</summary>
        public string Id;

        /// <summary>What the driver calls it.</summary>
        public string Name;

        /// <summary>How many axes it reports, after flattening to a fixed order.</summary>
        public int AxisCount;

        /// <summary>True when this is the AB9 itself, which must never be picked as a pedal.</summary>
        public bool IsTheShifterBase;

        /// <summary>
        /// What the picker shows. A device with no axes cannot hold a pedal, and the base itself
        /// is excluded by name rather than silently missing, because a user looking for it and
        /// not finding it will otherwise assume the list is broken.
        /// </summary>
        public string Describe()
        {
            if (IsTheShifterBase) return Name + " - this is the shifter base, not a pedal set";
            if (AxisCount <= 0) return Name + " - reports no axes";
            return Name + " (" + AxisCount + " axes)";
        }

        /// <summary>Whether this device could hold a clutch pedal at all.</summary>
        public bool Usable { get { return AxisCount > 0 && !IsTheShifterBase; } }
    }
}
