namespace AB9ActiveShifter.Output
{
    /// <summary>
    /// Where selected gears are published. Kept behind an interface so the gate logic can be
    /// tested without loading the vJoy wrapper, which is a 32-bit-only assembly.
    /// </summary>
    public interface IGearOutput
    {
        bool IsConnected { get; }

        /// <summary>Human-readable reason the output is unavailable, or null when healthy.</summary>
        string LastError { get; }

        bool Connect();

        /// <summary>Holds the button for <paramref name="gear"/> (1..8) and releases every other. 0 clears all.</summary>
        void SetGear(int gear);

        /// <summary>
        /// Raw single-button control, for the sequential pattern's pulsed up/down presses.
        /// Independent of the held-gear bookkeeping; <see cref="ReleaseAll"/> clears these too.
        /// </summary>
        void SetButton(int button, bool down);

        void ReleaseAll();

        void Disconnect();
    }
}
