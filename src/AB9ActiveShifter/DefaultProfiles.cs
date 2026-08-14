using System;
using System.Collections.Generic;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter
{
    /// <summary>
    /// What a machine with no saved settings starts with: six working profiles rather than bare
    /// defaults, written out to disk on that first start so they are ordinary settings from then
    /// on - editable, resettable, and never re-applied over anything a user has tuned.
    /// <para>
    /// Four of the six are the same tune. <see cref="LooseGate"/> is the gate that actually gets
    /// driven on the rig, and <see cref="Gate"/>, the 5+R copy of it, <see cref="ShortThrow"/> and
    /// the truck preset all start there; they differ by <em>where the slot ends</em> - and, for
    /// the truck, by which gap its lockout guards - and by nothing else. That is deliberate, and
    /// it is a correction: the two H profiles used to carry an older, firmer tune with every
    /// stabiliser off, which reads well on paper and is jerky in the hand. A shipped profile is a
    /// recommendation, so all four now make the same one, and the choice a user makes between
    /// them is a pattern, a throw length and a lockout rather than a quality of gate.
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
        public const string TruckName = "Truck 6-gear (low-range lockout)";

        private static readonly string[] BareNames =
        {
            SevenRName, ShortThrowName, FiveRName, SequentialName, PrndName, TruckName
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

            // The truck box, issue #28's request: six plain slots on buttons 1-6 and a gate
            // between the first two columns, guarding the way DOWN into the low range. One-way
            // on entry, because the danger is wandering into the creep gears at speed while
            // pulling OUT of low range is the routine 2-3 upshift - the same semantics as the
            // proven 7/R gate, mirrored onto the other end of the box. Every force dial is the
            // 7+R tune; only the pattern and the gate's place and direction differ.
            ShifterSettings truck = SettingsCloner.Clone(sevenR);
            truck.Pattern = GatePattern.H6;
            truck.LockoutPlacement = LockoutPlacement.Gap1;
            truck.LockoutGapDirection = LockoutGapDirection.TowardLow;

            return new List<ShifterProfile>
            {
                new ShifterProfile { Name = Preset(SevenRName), Settings = sevenR },
                new ShifterProfile { Name = Preset(ShortThrowName), Settings = ShortThrow() },
                new ShifterProfile { Name = Preset(FiveRName), Settings = fiveR },
                new ShifterProfile { Name = Preset(SequentialName), Settings = Sequential() },
                new ShifterProfile { Name = Preset(PrndName), Settings = Automatic() },
                new ShifterProfile { Name = Preset(TruckName), Settings = truck }
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

        /// <summary>
        /// The H-pattern gate as it is actually driven on the rig: everything except where the
        /// slot ends. <see cref="Gate"/> and <see cref="ShortThrow"/> are this plus a throw, and
        /// 5+R is a copy of the first of those.
        /// <para>
        /// "Loose" is two things at once and both are needed for it. The detent is all hold - no
        /// entry resistance, no pull - so the slot is free the whole way in and the only thing in
        /// it is the seat. And the free widths stay open, <c>SlotHalfWidth</c> 2400 and
        /// <c>ChannelFreeDepth</c> 2165, so the slots and the tunnel are corridors with room in
        /// them rather than rails. Close either and this stops being the tune; a rail gate is a
        /// different feel with a different stability budget, not a tighter version of this one.
        /// There is deliberately no snick, because the crossover is what makes one and the pull is
        /// at zero, so the profile never changes sign.
        /// </para>
        /// <para>
        /// The stabilisers earn their keep here. The wall bite is short - 3816 against the 6000
        /// this file used to ship - and a shorter face is exactly what the yield, the friction and
        /// the attack exist to survive; the lateral pin comes down to 80 to match. That trade is
        /// the whole difference between this and the older tune, which had a 6000 bite with every
        /// stabiliser at zero and a pin at 100. On paper the long bite is the stabler shape. In the
        /// hand it was jerky, and this is what replaced it.
        /// </para>
        /// </summary>
        private static ShifterSettings LooseGate()
        {
            ShifterSettings s = new ShifterSettings();

            s.OverallGainPct = 100;

            // Corridors, left open. Half the reason this tune is called loose.
            s.SlotHalfWidth = 2400;
            s.ChannelFreeDepth = 2165;

            // Moderate force, a short wall bite, and the full stabiliser stack that buys.
            // Software damping is deliberately zero: the damping this gate relies on is MOZA
            // Cockpit's Damper at ~15% - the install guide's one-time setting - which is real
            // damping at the servo loop, ahead of the delay, and free of the throw-weight cost
            // the software dial has. The tune used to carry a measured 10 here, the one number
            // in this file that argued with the lightest-possible-lever goal; leaning on the
            // Cockpit damper instead is what retired it.
            s.ColumnPinForcePct = 80;
            s.ChannelWallForcePct = 100;
            s.ChannelGuideForcePct = 20;
            s.WallRamp = 3816;
            s.WallBlend = 1559;
            s.WallAttackMs = 15;
            s.WallYieldPct = 10;
            s.WallFrictionPct = 5;
            s.DampingPct = 0;

            // All hold, no resistance and no pull.
            s.DetentResistPct = 0;
            s.DetentPullPct = 0;
            s.DetentHoldPct = 40;
            s.DetentHysteresis = 905;

            // The tunnel band, and the lockout: firmer and much wider than default, so crossing
            // into 7/R is a deliberate shove rather than a bump.
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
            // pattern on one of these profiles lands somewhere sane.
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

            // The grind, on and tuned low and slow - a rattle rather than a buzz - with a balk
            // wall well under default so a refused gear pushes back without feeling like a wall.
            s.GrindEnabled = true;
            s.GrindGainPct = 100;
            s.GrindFreqHz = 15;
            s.GrindWallPct = 42;
            s.GrindMinSpeedKmh = 11;
            s.GrindClutchMode = GrindClutchMode.Progressive;

            return s;
        }

        /// <summary>
        /// The long throw: <see cref="LooseGate"/> with no bottom in its slots, so the seated hold
        /// carries the lever on to the base's own mechanical stop and the gear is simply the
        /// deepest part of the push. 7+R; 5+R is a copy of it.
        /// </summary>
        private static ShifterSettings Gate()
        {
            ShifterSettings s = LooseGate();

            // Engage at 11915 counts from centre, release 3000 counts shallower. That gap is the
            // hysteresis: the lever has to be pulled back out meaningfully before the gear drops,
            // rather than falling out on the dither of a hand resting at the engage line.
            //
            // The rig this was copied from had these the wrong way round - release 17789 against
            // engage 20852 - which GateGeometry repairs to engage + 1, leaving one axis count of
            // hysteresis in 65535. That is a gear that re-registers on noise. Depth counts inward
            // from the extreme, so release must be the LARGER number; that is easy to get
            // backwards and a test checks it.
            //
            // SlotStopForcePct stays at its default zero, which is what makes this the long throw,
            // and SlotOvertravel is inert without it and stays at its default too.
            s.EngageDepth = 20852;
            s.ReleaseDepth = 23852;

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

            // The lateral rail and the stabilisers around it. Software damping is zero like
            // every preset's - the Cockpit damper carries it (see LooseGate) - and here that is
            // also a correction: this profile used to inherit the bare default of 25 by
            // omission, the only shipped tune with software damping on.
            s.ColumnPinForcePct = 80;
            s.WallRamp = 6000;
            s.WallBlend = 1559;
            s.WallAttackMs = 15;
            s.WallYieldPct = 15;
            s.WallFrictionPct = 7;
            s.DampingPct = 0;

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
        /// The short throw: <see cref="LooseGate"/> with a bottom in its slots, so the stroke
        /// stops where it says it does instead of running on to the base's own stop.
        ///
        /// <para>
        /// The engage line is barely different from <see cref="Gate"/>'s - 12006 counts from
        /// centre to the seat against 11915 - and that is the point. What makes a throw short is
        /// <c>SlotStopForcePct</c>, not the engage line: with no end-stop the seated hold keeps
        /// pulling past the seat and the lever runs on to the mechanical stop, so moving the
        /// engage line alone changes only where the gear <em>registers</em>. Given a bottom the
        /// stroke ends at depth 20844 of 32767 - about two thirds of travel - and the number a
        /// hand feels has finally moved. The stop wall arriving is also what marks the bottom, in
        /// place of the snick this tune does not have.
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
        /// <para>
        /// It also explains why the release line is part of this block rather than of the shared
        /// tune, which is not obvious from the raw numbers: depth is measured from the extreme, so
        /// two profiles whose levers come to rest in different places need different release
        /// depths to ask a hand for the same pull. Here the lever rests in the landing, 17028 to
        /// 20844 counts from centre, so the pull out of gear is 19578 to 23394 counts. On
        /// <see cref="Gate"/> the lever rests at the mechanical stop, and its release depth of
        /// 23852 <em>is</em> that pull. Copying 35317 across would have made every shift on the
        /// long-throw gate a pull of more than half the axis.
        /// </para>
        /// </summary>
        private static ShifterSettings ShortThrow()
        {
            ShifterSettings s = LooseGate();

            // The slot's bottom, which is the whole feature: seat at depth 12006, the hold fading
            // out over one wall bite, then a free landing, then the wall. The landing is the free
            // part only - the fade eats WallRamp of it - so 5022 against a 3816 bite leaves 1206
            // counts of genuinely free travel for the lever to rest in.
            s.EngageDepth = 20761;
            s.SlotOvertravel = 5022;
            s.SlotStopForcePct = 100;

            // Past centre, inside the tunnel. See the note above before changing it.
            s.ReleaseDepth = 35317;

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
