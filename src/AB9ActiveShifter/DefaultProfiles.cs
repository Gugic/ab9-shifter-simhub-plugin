using System.Collections.Generic;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter
{
    /// <summary>
    /// What a machine with no saved settings starts with: three working profiles rather than bare
    /// defaults, written out to disk on that first start so they are ordinary settings from then
    /// on - editable, resettable, and never re-applied over anything a user has tuned.
    /// <para>
    /// These numbers were measured, not chosen. They are the tuning of the rig this plugin was
    /// developed on, and most of them are far from the constants in <see cref="EngineConfig"/>:
    /// the walls are firmer and the stabilisers mostly off, because a gate assembled from
    /// conservative defaults feels vague. The defaults in <c>EngineConfig</c> are still the right
    /// starting point for a dial considered alone - they are what a reset returns to - but a
    /// coherent gate is a set of dials chosen together, and that is what this file is.
    /// </para>
    /// <para>
    /// Expressed as differences from a bare <see cref="ShifterSettings"/> so the tuning is legible
    /// as tuning, and so a dial that gains a better default here inherits it. Kept in code rather
    /// than in a JSON file beside the DLL for one blunt reason: renaming a setting silently drops
    /// its value from a JSON, and breaks the build here.
    /// </para>
    /// <para>
    /// Two things are deliberately left at their defaults and must stay that way, both pinned by
    /// tests: <c>Enabled</c> is false, because forces must never come on by themselves, and
    /// <c>PolarityConfirmed</c> is false, because polarity is a per-unit measured fact and the 10%
    /// cap exists to guard an unmeasured base. <c>InvertConstantX</c> IS carried, as this rig
    /// measured it - a starting guess that costs nothing while the cap is on, and that calibration
    /// overwrites the moment it runs.
    /// </para>
    /// </summary>
    public static class DefaultProfiles
    {
        /// <summary>The profile a fresh install comes up in.</summary>
        public const string ActiveName = "7+R lockout";

        public static ProfileStore Create()
        {
            ShifterSettings sevenR = Gate();

            // 5+R is the same gate with a column taken out - every force dial identical, so it is
            // literally a copy. Tuning them apart is a user's business, not a shipped difference.
            ShifterSettings fiveR = SettingsCloner.Clone(sevenR);
            fiveR.Pattern = GatePattern.H5R;

            return new ProfileStore
            {
                ActiveProfile = ActiveName,
                Profiles = new List<ShifterProfile>
                {
                    new ShifterProfile { Name = "Sequential", Settings = Sequential() },
                    new ShifterProfile { Name = ActiveName, Settings = sevenR },
                    new ShifterProfile { Name = "5+R", Settings = fiveR }
                }
            };
        }

        /// <summary>The H-pattern gate, tuned on the rig. 7+R; 5+R is a copy of it.</summary>
        private static ShifterSettings Gate()
        {
            ShifterSettings s = new ShifterSettings();

            // Full software gain, with the base's own Damper doing the settling. The plugin's
            // damping is off: it thickens the lever, and real damping in the servo loop ahead of
            // the USB round trip does the same job without that cost.
            s.OverallGainPct = 100;
            s.DampingPct = 0;

            // Walls: firm, wide-biting, and with the stabilisers off. A long bite (WallRamp) is
            // what buys the stability that a steep face would need an absorber to survive, so the
            // yield and the friction have nothing left to do here.
            s.ColumnPinForcePct = 100;
            s.ChannelWallForcePct = 100;
            s.WallRamp = 6000;
            s.WallBlend = 1559;
            s.WallYieldPct = 0;
            s.WallFrictionPct = 0;

            // The neutral tunnel is nearly bare: no hump between columns, no detent holding the
            // lever at a column, so sliding across the gate is free and the only thing a hand
            // meets on the way to a slot is the lockout.
            s.BarrierForcePct = 0;
            s.BarrierWidth = 400;
            s.ColumnDetentForcePct = 0;
            s.ChannelFreeDepth = 2165;

            // The lockout, the headline: firmer and much wider than default, so crossing into 7/R
            // is a deliberate shove rather than a bump.
            s.LockoutForcePct = 80;
            s.LockoutHalfWidth = 6000;

            // Slots: a wide free corridor, angled mouths feeding the lever in, and a detent that
            // is all seated hold and resistance with no pull - the snick comes from the wall
            // shape, not from a magnet at the bottom of the slot.
            s.SlotHalfWidth = 2400;
            s.MouthShape = SlotMouthShape.Angled;
            s.DetentResistPct = 15;
            s.DetentPullPct = 0;
            s.DetentHoldPct = 40;

            // A long push to engage, matching a real lever's travel.
            s.EngageDepth = 20852;
            s.ReleaseDepth = 17789;

            // Sequential dials still persist in an H profile; these are the values carried on the
            // rig, kept so switching pattern on this profile lands somewhere sane.
            s.SeqThrow = 11915;
            s.SeqOvertravel = 500;
            s.SeqStopForcePct = 100;
            s.SeqPulseMs = 400;

            // Measured on this unit: constant force is inverted left/right and not fore/aft.
            s.InvertConstantX = true;

            ApplyEffects(s);

            // The grind, on and tuned low and slow - a rattle rather than a buzz - with a balk
            // wall well under default so a refused gear pushes back without feeling like a wall.
            s.GrindEnabled = true;
            s.GrindGainPct = 100;
            s.GrindFreqHz = 15;
            s.GrindWallPct = 42;
            s.GrindMinSpeedKmh = 11;

            s.FxLimiterEnabled = true;
            s.FxCurbsGainPct = 100;
            s.FxEngineGainPct = 59;

            return s;
        }

        /// <summary>The sequential lever: a different feel entirely, not a gate with one column.</summary>
        private static ShifterSettings Sequential()
        {
            ShifterSettings s = new ShifterSettings();
            s.Pattern = GatePattern.Sequential;

            s.OverallGainPct = 100;
            s.DampingPct = 0;

            // The stroke: shorter throw than default, a firm end stop, and a quick pulse. The
            // click's kick is raised - it is the whole feedback of a sequential shift.
            s.SeqThrow = 20530;
            s.SeqOvertravel = 2010;
            s.SeqStopForcePct = 100;
            s.SeqPulseMs = 100;
            s.SeqClickPct = 71;

            // The lever is sprung, so unlike the gate it leans on its spring continuously: it
            // keeps a slow wall attack and some yield, which the H profiles do not need.
            s.ColumnPinForcePct = 100;
            s.ChannelWallForcePct = 100;
            s.WallRamp = 6000;
            s.WallBlend = 6000;
            s.WallAttackMs = 16;
            s.WallYieldPct = 20;
            s.WallFrictionPct = 0;

            // Push resistance and click strength are the detent dials in sequential mode.
            s.DetentResistPct = 60;
            s.DetentPullPct = 100;
            s.DetentHoldPct = 35;

            s.EngageDepth = 12237;
            s.ReleaseDepth = 12737;

            // Gate dials that do not apply to a sequential lever but persist with the profile.
            s.BarrierForcePct = 6;
            s.BarrierWidth = 1350;
            s.ColumnDetentForcePct = 14;
            s.SlotHalfWidth = 2400;
            s.MouthShape = SlotMouthShape.Angled;
            s.LockoutForcePct = 100;
            s.LockoutHalfWidth = 4838;

            s.InvertConstantX = true;

            ApplyEffects(s);

            s.FxEngineGainPct = 100;
            s.FxEngineFreqAt1000Rpm = 14;
            s.FxCurbsGainPct = 64;
            s.FxShiftGainPct = 100;

            return s;
        }

        /// <summary>The telemetry effects both profiles switch on, at their common settings.</summary>
        private static void ApplyEffects(ShifterSettings s)
        {
            s.FxEngineEnabled = true;
            s.FxEngineFreqAt1000Rpm = 12;
            s.FxEngineGainPct = 59;

            s.FxCurbsEnabled = true;
            s.FxCurbsGainPct = 100;

            s.FxShiftEnabled = true;
        }
    }
}
