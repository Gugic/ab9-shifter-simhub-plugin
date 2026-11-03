using AB9ActiveShifter.Core;

namespace AB9ActiveShifter
{
    public static partial class DefaultProfiles
    {
        /// <summary>
        /// The user's short-throw H tune captured on 2026-10-08: tight slots, a detent
        /// crossover, and its own base resistance. Session and machine facts stay local.
        /// </summary>
        private static ShifterSettings SportShortThrow()
        {
            ShifterSettings s = ShortThrow();
            s.BaseFrictionPct = 35;
            s.DamperCoeff = 3515;
            s.DetentHoldPct = 50;
            s.DetentPullPct = 40;
            s.DetentResistPct = 25;
            s.FloatShiftingEnabled = true;
            s.MouthDepth = 5271;
            s.MouthOpenPct = 81;
            s.PatternWidthPct = 65;
            s.SlotHalfWidth = 468;
            return s;
        }

        /// <summary>
        /// The user's narrow five-speed sport tune captured on 2026-11-01. Mechanical tuning
        /// stays captured; Presets applies the shared balanced Effects tune.
        /// </summary>
        private static ShifterSettings FiveRSportShortThrow()
        {
            ShifterSettings s = SportShortThrow();
            s.Pattern = GatePattern.H5R;
            s.PatternWidthPct = 40;
            s.ReleaseDepth = 26731;
            return s;
        }

    }
}
