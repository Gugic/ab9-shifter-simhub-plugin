using System.Globalization;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// What vJoy says about one device slot, in this project's own terms rather than vJoy's.
    /// The wrapper's own enum lives in a 32-bit native assembly that a test runner cannot load,
    /// so the state is restated here and mapped at the edge - which is the same reason
    /// <c>Core/</c> is free of I/O generally.
    /// </summary>
    public enum VJoyDeviceState
    {
        /// <summary>No such device: it has not been created in vJoyConf.</summary>
        Missing,

        /// <summary>Exists and nothing holds it.</summary>
        Free,

        /// <summary>Exists, held by some other program.</summary>
        Busy,

        /// <summary>Exists and this plugin holds it.</summary>
        Owned,

        /// <summary>Exists, and vJoy will not say more than that.</summary>
        Unknown
    }

    /// <summary>
    /// One vJoy device as the picker shows it. Pure data plus the sentence describing it, so the
    /// wording is testable without a vJoy driver anywhere near the test runner.
    /// </summary>
    public sealed class VJoyDeviceInfo
    {
        /// <summary>
        /// Gears take buttons 1-8, the sequential up/down take 9 and 10, and PRND's four positions
        /// take 11-14 - each range kept above the last so no game binding can ever mean two
        /// things. A device with fewer still works for whatever fits, which is why this is a
        /// warning everywhere and never a refusal, and why it deliberately does not gate the
        /// tuning tabs.
        /// </summary>
        public const int ButtonsNeeded = 14;

        public uint Id { get; set; }
        public VJoyDeviceState State { get; set; }
        public int Buttons { get; set; }

        /// <summary>Process holding it when <see cref="State"/> is Busy; otherwise 0.</summary>
        public int OwnerPid { get; set; }

        /// <summary>Readable name for <see cref="OwnerPid"/>, when one could be resolved.</summary>
        public string OwnerName { get; set; }

        public bool Exists { get { return State != VJoyDeviceState.Missing; } }

        /// <summary>Free or already ours, and wide enough for every button this plugin sends.</summary>
        public bool Usable
        {
            get
            {
                return (State == VJoyDeviceState.Free || State == VJoyDeviceState.Owned)
                       && Buttons >= ButtonsNeeded;
            }
        }

        /// <summary>The line shown in the picker. Says the number, the size and the catch.</summary>
        public string Describe()
        {
            string id = "Device " + Id.ToString(CultureInfo.InvariantCulture);

            switch (State)
            {
                case VJoyDeviceState.Missing:
                    return id + " - not created";

                case VJoyDeviceState.Unknown:
                    return id + " - unavailable";

                case VJoyDeviceState.Busy:
                    return id + " - " + Buttons + " buttons, in use by " + Owner();

                case VJoyDeviceState.Owned:
                    return id + " - " + Buttons + " buttons, in use by this plugin" + Shortfall();

                default:
                    return id + " - " + Buttons + " buttons, free" + Shortfall();
            }
        }

        private string Shortfall()
        {
            return Buttons >= ButtonsNeeded
                ? ""
                : " (needs " + ButtonsNeeded + ")";
        }

        private string Owner()
        {
            if (!string.IsNullOrEmpty(OwnerName)) return OwnerName;
            return OwnerPid > 0 ? "pid " + OwnerPid.ToString(CultureInfo.InvariantCulture) : "another program";
        }
    }
}
