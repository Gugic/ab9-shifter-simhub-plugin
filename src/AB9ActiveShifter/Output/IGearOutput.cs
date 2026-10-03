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

        /// <summary>
        /// Holds the H gear (1..8) or PRND position (11..14), releasing the previous one first.
        /// Zero clears the gear; a role output may additionally hold a configured H-neutral role.
        /// ReleaseAll is the unconditional clear, including that optional neutral role.
        /// </summary>
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
