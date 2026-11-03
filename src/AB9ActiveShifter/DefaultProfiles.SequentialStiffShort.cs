namespace AB9ActiveShifter
{
    public static partial class DefaultProfiles
    {
        /// <summary>
        /// The user's Sequential (stiff, short) 2 tune captured on 2026-10-29. Its stroke
        /// and resistance stay captured; Presets applies the shared balanced Effects tune.
        /// </summary>
        private static ShifterSettings SequentialStiffShort()
        {
            ShifterSettings s = Sequential();
            s.ColumnPinForcePct = 100;
            s.WallRamp = 6000;
            s.DetentResistPct = 45;
            s.EngageDepth = 25215;
            s.FxLimiterEnabled = true;
            s.ReleaseDepth = 25715;
            s.SeqClickPct = 70;
            return s;
        }

    }
}
