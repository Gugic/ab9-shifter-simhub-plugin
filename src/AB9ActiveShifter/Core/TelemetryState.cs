using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// One snapshot of the game telemetry the effects run on. Built on SimHub's data thread,
    /// published to the engine as a whole object and never mutated afterwards, so the FFB
    /// tick reads a consistent frame without locking.
    ///
    /// <see cref="CapturedAtTick"/> is Environment.TickCount at capture; the engine judges
    /// freshness by unchecked subtraction, which survives the counter's 25-day wrap. A
    /// snapshot older than <see cref="EffectComposer.StaleAfterMs"/> counts as no telemetry
    /// at all, because a paused or hung game must not leave a buzz running against the hand.
    /// </summary>
    public sealed class TelemetryState
    {
        /// <summary>The inert state: no game, nothing plays.</summary>
        public static readonly TelemetryState Inactive = new TelemetryState();

        public bool GameRunning;

        /// <summary>Engine speed in revolutions per minute.</summary>
        public double Rpms;

        /// <summary>The rev limiter as the game reports it. Zero when the game does not say.</summary>
        public double MaxRpm;

        public double SpeedKmh;

        /// <summary>Clutch pedal position, 0..100 with 100 fully pressed - SimHub's convention.</summary>
        public double Clutch;

        /// <summary>The game's own gear string ("N", "R", "1"...), for the shift pulse edge.</summary>
        public string Gear;

        public bool AbsActive;
        public bool TcActive;

        /// <summary>Vertical acceleration in G, zero when the game does not report it.</summary>
        public double HeaveG;

        /// <summary>Value of the user-chosen SimHub property, expected 0..100.</summary>
        public double CustomValue;

        /// <summary>Environment.TickCount when this snapshot was built.</summary>
        public int CapturedAtTick;

        /// <summary>
        /// Overwrites this instance with another's fields, substituting the clutch. Exists so the
        /// engine can swap in a directly-read pedal without allocating: the engine owns one
        /// scratch instance and refills it, rather than building a fresh snapshot every
        /// millisecond. Only ever called on the engine thread, and only on an instance the data
        /// thread has never seen - copying INTO a published snapshot would tear it under the
        /// reader that is meant to see whole frames.
        /// </summary>
        public void CopyFromWithClutch(TelemetryState source, double clutchPct)
        {
            if (source == null) return;

            GameRunning = source.GameRunning;
            Rpms = source.Rpms;
            MaxRpm = source.MaxRpm;
            SpeedKmh = source.SpeedKmh;
            Gear = source.Gear;
            AbsActive = source.AbsActive;
            TcActive = source.TcActive;
            HeaveG = source.HeaveG;
            CustomValue = source.CustomValue;
            CapturedAtTick = source.CapturedAtTick;

            Clutch = clutchPct;
        }
    }
}
