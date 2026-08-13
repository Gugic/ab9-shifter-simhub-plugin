using System;
using System.Collections.Generic;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter
{
    /// <summary>
    /// What a machine with no saved settings starts with: five working profiles rather than bare
    /// defaults, written out to disk on that first start so they are ordinary settings from then
    /// on - editable, resettable, and never re-applied over anything a user has tuned.
    /// <para>
    /// Two of the five are 7+R. That is not a duplicate: <see cref="Gate"/> is the long-throw
    /// corridor gate with firm walls and the stabilisers off, and <see cref="ShortThrow"/> is the
    /// same pattern with a bottom in its slots and the resistance taken out. They are different
    /// feels, not different strengths, and neither is a starting point for the other.
    /// </para>
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
        /// <summary>
        /// What marks a profile as shipped rather than tuned here.
        /// <para>
        /// It is reserved, not a convention: <see cref="StripReservedPrefix"/> takes it off every
        /// name a user can supply - a new profile, a rename, a file imported from a stranger - so
        /// nothing but this class can mint one. That is what lets the prefix be an <em>identity</em>.
        /// Without the reservation an imported file could arrive named "(Preset) 7+R lockout" and
        /// be treated as immutable and regenerated over on the next start, which is a shared file
        /// deleting a tune - the one thing <see cref="ProfileTransfer"/> exists to prevent.
        /// </para>
        /// </summary>
        public const string PresetPrefix = "(Preset) ";

        // The bare names. A preset ships as PresetPrefix + one of these, and a fork of a preset
        // takes the bare name back - which is exactly what moves the fork into the local half of
        // the list, since only prefixed names sort to the end.
        public const string SevenRName = "7+R lockout";
        public const string ShortThrowName = "7+R lockout (short throw, loose)";
        public const string FiveRName = "5+R";
        public const string SequentialName = "Sequential";
        public const string PrndName = "Automatic (PRND)";

        private static readonly string[] BareNames =
        {
            SevenRName, ShortThrowName, FiveRName, SequentialName, PrndName
        };

        /// <summary>The profile a fresh install comes up in.</summary>
        public static string ActiveName { get { return Preset(SevenRName); } }

        /// <summary>The shipped name for a bare one.</summary>
        public static string Preset(string bareName) { return PresetPrefix + bareName; }

        /// <summary>
        /// The bare name behind a preset's, or null if this is not one of ours. Checking against
        /// the known list rather than the prefix alone matters: the prefix is reserved on input,
        /// but a settings file written by a future build - or edited by hand - can still contain
        /// a prefixed name this build knows nothing about, and treating that as a preset would
        /// mean deleting it on sight in <see cref="ProfileStore.EnsurePresets"/>.
        /// </summary>
        public static string BareOf(string name)
        {
            if (name == null || !name.StartsWith(PresetPrefix, StringComparison.Ordinal)) return null;

            string bare = name.Substring(PresetPrefix.Length);
            foreach (string known in BareNames)
            {
                if (string.Equals(known, bare, StringComparison.Ordinal)) return bare;
            }
            return null;
        }

        /// <summary>Whether this name belongs to a shipped preset, and so is immutable.</summary>
        public static bool IsPreset(string name) { return BareOf(name) != null; }

        /// <summary>
        /// A name with the reserved prefix taken off, however many times it was applied. Every
        /// path that lets a user supply a name runs through this - see <see cref="PresetPrefix"/>.
        /// </summary>
        public static string StripReservedPrefix(string name)
        {
            if (name == null) return null;

            string stripped = name;
            while (stripped.StartsWith(PresetPrefix, StringComparison.Ordinal))
            {
                stripped = stripped.Substring(PresetPrefix.Length);
            }
            return stripped;
        }

        /// <summary>
        /// Every preset, freshly built, in the order they are shown. Built rather than cached
        /// because a preset handed out must never be the same object twice: the caller owns it
        /// from then on, and a shared instance would let an edit to one install's list reach
        /// another's.
        /// </summary>
        public static List<ShifterProfile> Presets()
        {
            ShifterSettings sevenR = Gate();

            // 5+R is the same gate with a column taken out - every force dial identical, so it is
            // literally a copy. Tuning them apart is a user's business, not a shipped difference.
            ShifterSettings fiveR = SettingsCloner.Clone(sevenR);
            fiveR.Pattern = GatePattern.H5R;

            return new List<ShifterProfile>
            {
                new ShifterProfile { Name = Preset(SevenRName), Settings = sevenR },
                new ShifterProfile { Name = Preset(ShortThrowName), Settings = ShortThrow() },
                new ShifterProfile { Name = Preset(FiveRName), Settings = fiveR },
                new ShifterProfile { Name = Preset(SequentialName), Settings = Sequential() },
                new ShifterProfile { Name = Preset(PrndName), Settings = Automatic() }
            };
        }

        /// <summary>One preset, freshly built, or null if that is not a preset name.</summary>
        public static ShifterProfile BuildPreset(string presetName)
        {
            foreach (ShifterProfile p in Presets())
            {
                if (p.Name == presetName) return p;
            }
            return null;
        }

        public static ProfileStore Create()
        {
            return new ProfileStore { ActiveProfile = ActiveName, Profiles = Presets() };
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

            // A long push to engage, matching a real lever's travel. Release sits 3000 counts
            // shallower than engage, which is the hysteresis: the lever has to be pulled back out
            // meaningfully before the gear drops, rather than falling out on the dither of a hand
            // resting at the engage line.
            //
            // The rig this was copied from had these the wrong way round - release 17789 against
            // engage 20852 - which GateGeometry repairs to engage + 1, leaving one axis count of
            // hysteresis in 65535. That is a gear that re-registers on noise, and it is the most
            // likely cause of the intermittent-registration report that the wall-bite ceiling work
            // set out to explain. Depth counts inward from the extreme, so release must be the
            // LARGER number; that is easy to get backwards and a test now checks it.
            s.EngageDepth = 20852;
            s.ReleaseDepth = 23852;

            // Sequential dials still persist in an H profile; these are the values carried on the
            // rig, kept so switching pattern on this profile lands somewhere sane.
            s.ThrowFromCentre = 11915;
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
            s.ThrowFromCentre = 20530;
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

        /// <summary>
        /// The automatic's selector, as tuned on the rig. It began life as a copy of the 7+R gate
        /// with the pattern switched, which is why so much of what follows is gate tuning that a
        /// selector never renders - the lockout, the mouths, the neutral tunnel, the slot detent.
        /// It is kept rather than stripped for the same reason the sequential profile keeps its
        /// own: those dials persist with the profile, so leaving them tuned means switching this
        /// profile's pattern lands somewhere sane instead of on bare defaults.
        /// <para>
        /// Two things about it are knowingly imperfect and are shipped as they were measured
        /// rather than tidied, because a shipped profile that does not match the rig it came from
        /// is worse than an honest one:
        /// </para>
        /// <para>
        /// The notch is clamped away. <c>PrndNotchHalfWidth</c> asks for 6000 against a wall bite
        /// of 6000, and the ceiling is half the position spacing less pi wall bites - 10000 less
        /// 18850, so zero. Each position therefore has no free width at all and its detent has
        /// less room to rise in than one wall bite, which is the abrupt case
        /// ForceComposer.PrndNotchHalfWidthCeiling exists to report. Lengthening the lane or
        /// shortening the bite is what buys it back.
        /// </para>
        /// <para>
        /// The lever wobbles side to side when pushed off centre and released - a hand-driven
        /// oscillation the gate has too little dissipation to outpace, measured at 9.8 Hz and
        /// plus/minus half the axis. Rebound absorption above 50% stops it and buzzes on the walls
        /// instead, because that one dial spans a wall touch at ~30000 counts/s and a fling at
        /// 800000 with a blend that saturates by 22000. It sits at 15 here, which is the wobble
        /// end of that trade.
        /// </para>
        /// </summary>
        private static ShifterSettings Automatic()
        {
            ShifterSettings s = new ShifterSettings();
            s.Pattern = GatePattern.Prnd;

            s.OverallGainPct = 100;

            // The lane: nearly the full travel, a firm detent between positions, and a hard stop
            // past P and D.
            s.PrndLaneHalfLength = 30000;
            s.PrndDetentForcePct = 70;
            s.PrndNotchHalfWidth = 6000;
            s.PrndStopForcePct = 100;

            // The lateral rail and the stabilisers around it.
            s.ColumnPinForcePct = 80;
            s.WallRamp = 6000;
            s.WallBlend = 1559;
            s.WallAttackMs = 15;
            s.WallYieldPct = 15;
            s.WallFrictionPct = 7;

            // Gate dials that render nothing in PRND but persist with the profile. The tunnel
            // pair is deliberately left as measured: enter and leave are equal, which
            // GateGeometry repairs to a MinBandSpan gap the moment this profile is switched to an
            // H pattern. Inert here, and a thing to fix on the rig rather than in the paste.
            s.ChannelHalfEnter = 5000;
            s.ChannelHalfExit = 5000;
            s.ChannelFreeDepth = 2165;
            s.ChannelGuideForcePct = 20;
            s.ChannelWallForcePct = 100;
            s.ColumnDetentForcePct = 0;
            s.ColumnInnerHalfEnter = 2286;
            s.ColumnInnerHalfExit = 300;
            s.DetentHysteresis = 905;
            s.BarrierForcePct = 0;
            s.BarrierWidth = 400;
            s.LockoutForcePct = 90;
            s.LockoutHalfWidth = 6000;
            s.MouthShape = SlotMouthShape.Angled;
            s.MouthDepth = 12000;
            s.SlotHalfWidth = 2400;
            s.SlotStopForcePct = 100;
            s.DetentResistPct = 0;
            s.DetentPullPct = 0;
            s.DetentHoldPct = 20;
            s.EngageDepth = 20553;
            s.ReleaseDepth = 21535;
            s.SeqOvertravel = 500;
            s.SeqStopForcePct = 100;
            s.SeqPulseMs = 400;

            // Measured on this unit: constant force is inverted left/right and not fore/aft.
            s.InvertConstantX = true;

            ApplyEffects(s);

            s.FxCurbsFullAtG = 1.9434039659804043;
            s.FxShiftGainPct = 72;
            s.FxShiftFreqHz = 37;
            s.FxShiftDurationMs = 107;

            // The grind does nothing on a selector - there is no synchro to balk - but it travels
            // with the profile the same way the gate dials above do.
            s.GrindEnabled = true;
            s.GrindGainPct = 100;
            s.GrindFreqHz = 15;
            s.GrindWallPct = 42;
            s.GrindMinSpeedKmh = 11;
            s.GrindClutchMode = GrindClutchMode.Progressive;

            return s;
        }

        /// <summary>
        /// The same 7+R gate with a bottom in the slots and the resistance taken out: a short
        /// throw that stops where it says it does, and a lever that meets almost nothing on the
        /// way there. Copied off the rig, where it is the tune that gets driven.
        ///
        /// <para>
        /// The engage line is barely different from <see cref="Gate"/>'s - 12006 counts from
        /// centre to the seat against 11915 - and that is the point of the profile. What makes a
        /// throw short is <c>SlotStopForcePct</c>, not the engage line: with no end-stop the
        /// seated hold keeps pulling past the seat and the lever runs on to the base's own
        /// mechanical stop, so moving the engage line alone changes only where the gear
        /// <em>registers</em>. Given a bottom the stroke ends at depth 20844 of 32767 - about two
        /// thirds of travel - and the number a hand feels has finally moved.
        /// </para>
        /// <para>
        /// "Loose" is two things at once, and both are needed for it. The detent is all hold: no
        /// entry resistance, no pull, so the slot is free the whole way in and the only thing in
        /// it is the seat. And the free widths stay open - <c>SlotHalfWidth</c> 2400 and
        /// <c>ChannelFreeDepth</c> 2165 - so the slots and the tunnel are corridors with room in
        /// them rather than rails. Close either and the tune stops being this one; a rail gate is
        /// a different feel with a different stability budget, not a tighter version of this.
        /// There is deliberately no snick: the crossover is what makes one, and with the pull at
        /// zero the profile never changes sign. The stop wall arriving marks the bottom instead.
        /// </para>
        /// <para>
        /// The stabilisers earn their keep here where the corridor gate has them all off. The wall
        /// bite is shorter - 3816 against 6000 - and a shorter face is exactly what the yield, the
        /// friction and the attack exist to survive; the lateral pin comes down to 80 to match.
        /// </para>
        /// <para>
        /// The release line sits at 35317 - past the axis centre, 2550 counts into the far half of
        /// the neutral tunnel (centre ± 3268 here, so it lands 718 counts short of the far edge).
        /// That is the "released only by returning through the neutral channel" rule made literal
        /// on the fore/aft axis: the gear holds until the lever is demonstrably through neutral.
        /// It has a consequence worth knowing before anyone "corrects" it - a latched gear keeps
        /// its column wall across the whole gate, so changing column means pulling past centre
        /// first. 1-2 is natural; 1-3 is a deliberate over-pull.
        /// </para>
        /// </summary>
        private static ShifterSettings ShortThrow()
        {
            ShifterSettings s = new ShifterSettings();

            s.OverallGainPct = 100;

            // The slot's bottom, which is the whole feature: seat at depth 12006, the hold fading
            // out over one wall bite, then a free landing, then the wall. The landing is the free
            // part only - the fade eats WallRamp of it - so 5022 against a 3816 bite leaves 1206
            // counts of genuinely free travel for the lever to rest in.
            s.EngageDepth = 20761;
            s.SlotOvertravel = 5022;
            s.SlotStopForcePct = 100;

            // Past centre, inside the tunnel. See the note above before changing it.
            s.ReleaseDepth = 35317;

            // Corridors, left open. Half the reason this profile is called loose.
            s.SlotHalfWidth = 2400;
            s.ChannelFreeDepth = 2165;

            // Moderate force, a short wall bite, and the full stabiliser stack that buys.
            s.ColumnPinForcePct = 80;
            s.ChannelWallForcePct = 100;
            s.ChannelGuideForcePct = 20;
            s.WallRamp = 3816;
            s.WallBlend = 1559;
            s.WallAttackMs = 15;
            s.WallYieldPct = 10;
            s.WallFrictionPct = 5;
            s.DampingPct = 10;

            // All hold, no resistance and no pull.
            s.DetentResistPct = 0;
            s.DetentPullPct = 0;
            s.DetentHoldPct = 40;
            s.DetentHysteresis = 905;

            // A tighter tunnel band than the corridor gate's 2600/5200, and a firmer lockout.
            s.ChannelHalfEnter = 3268;
            s.ChannelHalfExit = 4051;
            s.LockoutForcePct = 90;
            s.LockoutHalfWidth = 6000;

            // Nothing in the tunnel between columns, and angled mouths feeding the slots.
            s.BarrierForcePct = 0;
            s.BarrierWidth = 400;
            s.ColumnDetentForcePct = 0;
            s.MouthShape = SlotMouthShape.Angled;
            s.MouthDepth = 12000;

            // Carried as measured. The inner-column pair is the wrong way round - exit inside
            // enter - which GateGeometry repairs to enter + 1, leaving a single count of lateral
            // hysteresis. It is a pure state band so the repair is the sanctioned one, and it
            // matters less than it reads: a latched gear is held by the map, not by this band.
            // Still a thing to fix on the rig rather than in the paste.
            s.ColumnInnerHalfEnter = 2286;
            s.ColumnInnerHalfExit = 300;

            // Sequential dials persist in an H profile; these are the rig's, kept so switching
            // pattern on this profile lands somewhere sane.
            s.SeqOvertravel = 500;
            s.SeqStopForcePct = 100;
            s.SeqPulseMs = 400;

            // Measured on this unit: constant force is inverted left/right and not fore/aft.
            s.InvertConstantX = true;

            ApplyEffects(s);

            s.FxCurbsFullAtG = 1.9434039659804043;
            s.FxEngineFreqAt1000Rpm = 12;
            s.FxShiftGainPct = 72;
            s.FxShiftFreqHz = 37;
            s.FxShiftDurationMs = 107;

            s.GrindEnabled = true;
            s.GrindGainPct = 100;
            s.GrindFreqHz = 15;
            s.GrindWallPct = 42;
            s.GrindMinSpeedKmh = 11;
            s.GrindClutchMode = GrindClutchMode.Progressive;

            return s;
        }

        /// <summary>The telemetry effects the profiles switch on, at their common settings.</summary>
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
