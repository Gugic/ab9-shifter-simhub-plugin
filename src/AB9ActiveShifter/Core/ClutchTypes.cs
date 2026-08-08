namespace AB9ActiveShifter.Core
{
    /// <summary>Where the clutch reading comes from.</summary>
    public enum ClutchSource
    {
        /// <summary>
        /// SimHub's own <c>GameData.Clutch</c>. The default, and the right answer whenever the
        /// game reports the pedal at all: it is already normalised, already per-car, and costs
        /// nothing to read.
        /// </summary>
        GameTelemetry,

        /// <summary>
        /// The pedal itself, read straight off its DirectInput axis. Worth the extra device
        /// handle for two reasons the telemetry path cannot fix: games that never report a clutch
        /// leave the grind with nothing to key on at all, and those that do report it at the
        /// telemetry rate, which is tens of milliseconds old by the time a 1 kHz loop sees it -
        /// against a shift that is over in a couple of hundred.
        /// </summary>
        Pedal
    }

    /// <summary>How far the clutch's position is allowed to matter to the grind.</summary>
    public enum GrindClutchMode
    {
        /// <summary>
        /// One line: above the threshold the clutch is down and nothing grinds, below it the
        /// clutch is up and a clutchless shift grinds at full strength. The original behaviour,
        /// kept as the default because it is predictable and because the threshold is the only
        /// number it needs.
        /// </summary>
        Threshold,

        /// <summary>
        /// The grind fades in across the pedal's travel from the bite point upward: a
        /// fully-released clutch grinds hardest, a half-lifted one grinds softly, and below the
        /// bite point it is silent. Needs the bite point to be honest, which is why that is a
        /// setting rather than a guess.
        /// </summary>
        Progressive
    }
}
