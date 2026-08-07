namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// What the base is doing with the forces it is being sent, as distinct from whether the
    /// writes are succeeding. Those are different questions, and the gap between them is a real
    /// failure mode on this hardware: the AB9 has been observed to stop producing torque while
    /// staying enumerated, readable, and accepting every effect write without error. Nothing in
    /// the write path can see that - see docs/hardware.md.
    /// </summary>
    public enum ForceOutputHealth
    {
        /// <summary>The device has not been asked yet, or would not say.</summary>
        Unknown,

        /// <summary>Actuators on, powered, not stopped, effects present. Force is being produced.</summary>
        Producing,

        /// <summary>
        /// The device is holding no effects. Ours were discarded behind our back - the signature
        /// of the base resetting its force feedback engine while the handle stayed valid. This is
        /// the one worth trying to recover from, because recreating the effects may be enough.
        /// </summary>
        EffectsGone,

        /// <summary>The device's actuators are switched off. Torque cannot be produced.</summary>
        ActuatorsOff,

        /// <summary>
        /// The device is playing no effects. Measured on this base: this is the ordinary resting
        /// state, set whenever the gate happens to be demanding nothing - which in neutral, with
        /// the lever against no wall, is most of the time. It is therefore NOT a fault, and
        /// treating it as one produced a warning within 96 ms of every startup. Kept as a
        /// distinct value rather than folded into Producing so the distinction stays visible.
        /// </summary>
        Idle,

        /// <summary>The device reports its force feedback power is off.</summary>
        PowerOff,

        /// <summary>A safety switch - the device's own or the user's - is open.</summary>
        SafetyCutout,

        /// <summary>The handle is gone; the reconnect path owns this one.</summary>
        Lost
    }

    /// <summary>
    /// Turns the device's force feedback status flags into one answer and one sentence.
    /// <para>
    /// Kept here, in terms of plain booleans rather than DirectInput's own enum, for the same
    /// reason <see cref="VJoyDeviceInfo"/> restates vJoy's status: <c>Core</c> holds no I/O and
    /// no driver types, so the classification and the wording a user acts on stay testable with
    /// no device anywhere near the test runner. <c>FfbDevice</c> maps the real flags at the edge.
    /// </para>
    /// </summary>
    public static class ForceFeedbackHealth
    {
        /// <summary>
        /// Order matters: the most specific and most actionable answer wins. A device that is
        /// both lost and empty is lost, and a device that is empty and otherwise healthy is the
        /// case worth recovering rather than merely reporting.
        /// </summary>
        public static ForceOutputHealth Classify(
            bool deviceLost, bool powerOff, bool safetyCutout,
            bool actuatorsOff, bool stoppedOrPaused, bool empty)
        {
            if (deviceLost) return ForceOutputHealth.Lost;
            if (powerOff) return ForceOutputHealth.PowerOff;
            if (safetyCutout) return ForceOutputHealth.SafetyCutout;
            if (actuatorsOff) return ForceOutputHealth.ActuatorsOff;
            if (empty) return ForceOutputHealth.EffectsGone;
            if (stoppedOrPaused) return ForceOutputHealth.Idle;
            return ForceOutputHealth.Producing;
        }

        /// <summary>
        /// Whether this state is worth telling the user about at all. Idle is not: it is what a
        /// working base looks like whenever the gate is demanding nothing, and reporting it would
        /// train someone to ignore the one message that matters.
        /// </summary>
        public static bool IsFault(ForceOutputHealth health)
        {
            return health == ForceOutputHealth.EffectsGone
                   || health == ForceOutputHealth.ActuatorsOff
                   || health == ForceOutputHealth.PowerOff
                   || health == ForceOutputHealth.SafetyCutout
                   || health == ForceOutputHealth.Lost;
        }

        /// <summary>Whether the plugin should try to fix it before reporting it.</summary>
        public static bool WorthRecovering(ForceOutputHealth health)
        {
            return health == ForceOutputHealth.EffectsGone
                   || health == ForceOutputHealth.ActuatorsOff;
        }

        /// <summary>
        /// What to tell the user. These are read by someone who has just lost their gearbox and
        /// wants to know whether to change a setting or to reach for the power switch, so each
        /// one says which.
        /// </summary>
        public static string Describe(ForceOutputHealth health)
        {
            switch (health)
            {
                case ForceOutputHealth.Producing:
                    return "The base is producing force.";

                case ForceOutputHealth.EffectsGone:
                    return "The base has discarded the gate's effects - it reset its force feedback " +
                           "engine. Rebuilding them; if force does not come back, power-cycle the base.";

                case ForceOutputHealth.ActuatorsOff:
                    return "The base has switched its actuators off, so it is producing no force " +
                           "however much is sent. This is the base, not a setting. Power-cycle it.";

                case ForceOutputHealth.Idle:
                    return "The base is playing no effects, which is normal while the gate is " +
                           "demanding nothing.";

                case ForceOutputHealth.PowerOff:
                    return "The base reports its force feedback power is off. Check its power supply, " +
                           "then power-cycle it.";

                case ForceOutputHealth.SafetyCutout:
                    return "The base's safety switch is open, so it will produce no force until it " +
                           "is closed.";

                case ForceOutputHealth.Lost:
                    return "The base is no longer connected.";

                default:
                    return "The base has not said whether it is producing force.";
            }
        }
    }
}
