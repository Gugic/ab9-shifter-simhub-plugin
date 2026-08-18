using System;
using System.Collections.Generic;
using System.Reflection;
using AB9ActiveShifter;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The profiles a fresh install comes up with. These used to be a JSON file copied in beside
    /// the DLL, where nothing checked them; now they are code, and what is checked here is the
    /// part that matters - that a machine which has never been measured cannot come up applying
    /// force, however the tuning beside it changes.
    /// </summary>
    public class DefaultProfilesTests
    {
        [Fact]
        public void NoShippedProfileComesUpApplyingForce()
        {
            // The whole safety story of a first start. A user who installs the plugin and walks
            // away must find the base exactly as they left it, whatever else is in these dials.
            foreach (ShifterProfile p in DefaultProfiles.Create().Profiles)
            {
                Assert.False(p.Settings.Enabled, p.Name + " ships with forces enabled");
                Assert.False(p.Settings.FreeStick, p.Name + " ships holding the device");
            }
        }

        [Fact]
        public void NoShippedProfileClaimsPolarityHasBeenMeasured()
        {
            // Polarity is a per-unit measured fact and the 10% cap is what guards an unmeasured
            // base. Shipping the flag set would hand full force to a base nobody has probed -
            // the exact case the cap exists for. The invert flags themselves may travel: they
            // are a starting guess that costs nothing while the cap is on.
            foreach (ShifterProfile p in DefaultProfiles.Create().Profiles)
            {
                Assert.False(p.Settings.PolarityConfirmed, p.Name + " ships polarity pre-confirmed");
            }
        }

        [Fact]
        public void ShippedGainIsCappedUntilPolarityIsMeasured()
        {
            // The profiles ask for 100% gain, which is only safe because the cap outranks them.
            // This is the arithmetic proof of the sentence above, at the layer that applies it.
            foreach (ShifterProfile p in DefaultProfiles.Create().Profiles)
            {
                EngineConfig cfg = p.Settings.ToEngineConfig();
                Assert.True(cfg.EffectiveGain <= 0.10 + 1e-9,
                    p.Name + " reaches the device at gain " + cfg.EffectiveGain);
            }
        }

        [Fact]
        public void NoShippedProfileNeedsItsReleaseDepthRepaired()
        {
            // GateGeometry quietly repairs a release depth that is not deeper than engage, to
            // engage + 1. That is a safety net for a hand-edited setting, not something a shipped
            // profile should ever land on: one axis count of hysteresis in 65535 is a gear that
            // re-registers on noise. Asserting the repair changes nothing says "these values are
            // self-consistent" more precisely than any inequality on the raw numbers would.
            //
            // This exists because the shipped H profiles did land on it - copied off the rig with
            // release 17789 against engage 20852, the wrong way round. Depth counts inward from
            // the extreme, so release must be the larger number, and Show-ProfileDeltas.ps1 will
            // happily reproduce the mistake from a settings file that still has it.
            foreach (ShifterProfile p in DefaultProfiles.Create().Profiles)
            {
                GateGeometry geo = p.Settings.ToEngineConfig().BuildGeometry();

                Assert.Equal(p.Settings.EngageDepth, geo.EngageDepth);
                Assert.True(geo.ReleaseDepth == p.Settings.ReleaseDepth,
                    p.Name + " ships engage " + p.Settings.EngageDepth + " / release " +
                    p.Settings.ReleaseDepth + ", which the geometry repaired to " + geo.ReleaseDepth);
            }
        }

        [Fact]
        public void TheStoreIsCoherent()
        {
            ProfileStore store = DefaultProfiles.Create();

            Assert.NotNull(store.FindActive());
            Assert.Equal(DefaultProfiles.ActiveName, store.FindActive().Name);

            HashSet<string> names = new HashSet<string>();
            foreach (ShifterProfile p in store.Profiles)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Name), "a shipped profile has no name");
                Assert.NotNull(p.Settings);
                Assert.True(names.Add(p.Name), "duplicate shipped profile name: " + p.Name);
            }
        }

        [Fact]
        public void EveryPatternIsRepresentedAndSaysSo()
        {
            // A profile whose name promises a pattern it does not render is worse than no
            // profile: the dropdown is the only place a user reads what they are switching to.
            ProfileStore store = DefaultProfiles.Create();

            Assert.Equal(GatePattern.Sequential, Find(store, Preset(DefaultProfiles.SequentialName)).Pattern);
            Assert.Equal(GatePattern.H7R, Find(store, Preset(DefaultProfiles.SevenRName)).Pattern);
            Assert.Equal(GatePattern.H5R, Find(store, Preset(DefaultProfiles.FiveRName)).Pattern);
            Assert.Equal(GatePattern.Prnd, Find(store, Preset(DefaultProfiles.PrndName)).Pattern);
            Assert.Equal(GatePattern.H6, Find(store, Preset(DefaultProfiles.TruckName)).Pattern);
        }

        [Fact]
        public void TheTruckPresetShipsTheLowRangeGateOneWay()
        {
            // Issue #28's box: six plain slots, a push-through gate between the first two
            // columns, paying only on the way DOWN into the low range - pulling out of low is
            // the routine upshift and must stay assisted, like leaving 7/R always has been.
            ShifterSettings s = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.TruckName));

            Assert.Equal(GatePattern.H6, s.Pattern);
            Assert.Equal(LockoutPlacement.Gap1, s.LockoutPlacement);
            Assert.Equal(LockoutGapDirection.TowardLow, s.LockoutGapDirection);
            Assert.Equal(LockoutMode.PushThrough, s.LockoutMode);

            GateGeometry geo = s.ToEngineConfig().BuildGeometry();
            Assert.True(geo.HasLockout, "the truck preset must actually build its gate");
            Assert.Equal(0, geo.LockoutGapIndex);
            Assert.False(geo.LockoutPlacementRepaired);
        }

        [Fact]
        public void TheTruckPresetKeepsTheSharedTuneExceptWhereItsRoadTestDisagreed()
        {
            // The truck used to be a straight copy of the 7+R gate, which was honest only while
            // nobody had driven the pattern. It has since come back from someone who does, tuned
            // against a real Eaton-Fuller box, and it is a different feel on purpose - "slow and
            // hard and deliberate" against a racing gate's fast and slick.
            //
            // So this test says two things at once: exactly which dials that road test moved,
            // and that it moved nothing else. The second half is the part worth keeping - the
            // truck is a feel, not a fork, so a change to the shared tune must still reach it.
            ShifterSettings gate = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.SevenRName));
            ShifterSettings truck = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.TruckName));

            // The pattern, its gate, and how wide it stands. Six slots across the whole stick is
            // a reach, and this box already asks for a long push fore and aft.
            Assert.Equal(GatePattern.H6, truck.Pattern);
            Assert.Equal(DefaultProfiles.NarrowWidthPct, truck.PatternWidthPct);
            Assert.Equal(LockoutPlacement.Gap1, truck.LockoutPlacement);
            Assert.Equal(LockoutGapDirection.TowardLow, truck.LockoutGapDirection);

            // A long deliberate push: the gear registers in the last 2767 counts of travel, a
            // throw of 30000 from centre against the racing gate's 11915, and the same 3000-count
            // release hysteresis every H profile here keeps.
            Assert.Equal(30000, truck.ThrowFromCentre);
            Assert.Equal(truck.EngageDepth + 3000, truck.ReleaseDepth);
            Assert.True(truck.ThrowFromCentre > gate.ThrowFromCentre * 2,
                "the truck throw is meant to be far longer than the racing gate's");

            // A slot the hand pushes into, and a box that holds the lever rather than the other
            // way round - the reverse of the loose gate's all-hold detent, and still no pull.
            Assert.Equal(0, gate.DetentResistPct);
            Assert.Equal(57, truck.DetentResistPct);
            Assert.Equal(25, truck.DetentHoldPct);
            Assert.Equal(0, truck.DetentPullPct);

            // A gentler gate on firmer columns: a low-range gate is crossed on nearly every
            // downshift, so it cannot cost what a 7/R gate crossed twice a session costs.
            Assert.True(truck.LockoutForcePct < gate.LockoutForcePct);
            Assert.True(truck.ColumnPinForcePct > gate.ColumnPinForcePct);
            Assert.Equal(80, truck.OverallGainPct);

            // ...and nothing else moved.
            HashSet<string> theRoadTest = new HashSet<string>(StringComparer.Ordinal)
            {
                "Pattern", "PatternIndex",
                "LockoutPlacement", "LockoutPlacementIndex",
                "LockoutGapDirection", "LockoutGapDirectionIndex",
                "OverallGainPct", "ColumnPinForcePct", "LockoutForcePct",
                "DetentResistPct", "DetentHoldPct",
                "EngageDepth", "ReleaseDepth", "ThrowFromCentre",
                "PatternWidthPct",
                "ClutchBitePointPct", "FxBiteEnabled", "FxEngineGainPct"
            };

            foreach (PropertyInfo prop in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (theRoadTest.Contains(prop.Name)) continue;
                if (prop.Name.EndsWith("Percent", StringComparison.Ordinal)) continue;

                Assert.Equal(prop.GetValue(gate, null), prop.GetValue(truck, null));
            }
        }


        [Fact]
        public void EveryShippedProfileIsMarkedAsAPreset()
        {
            // The prefix is what makes a profile immutable, always re-created, and sorted to the
            // end of the list. A shipped profile that missed it would look local: editable, and
            // gone for good once deleted.
            foreach (ShifterProfile p in DefaultProfiles.Create().Profiles)
            {
                Assert.True(DefaultProfiles.IsPreset(p.Name), p.Name + " does not read as a preset");
            }
        }

        [Fact]
        public void TheShortThrowProfileHasABottomAndIsActuallyLoose()
        {
            // Its name makes two promises, and both are settings rather than adjectives.
            //
            // A short throw is the end-stop, not the engage line: without SlotStopForcePct the
            // seated hold keeps pulling past the seat and the lever runs on to the base's own
            // mechanical stop, so moving the engage line alone changes only where the gear
            // registers. And the landing has to be longer than the wall bite, because the hold's
            // fade eats a bite's worth of it - a landing at or under WallRamp leaves no free
            // travel at all, which puts the hold and the stop wall either side of a single point
            // and rebuilds the interior equilibrium that free regions exist to prevent.
            //
            // "Loose" is the open corridors plus a detent carrying nothing but the hold. Closing
            // either free width turns this into the rail gate - a different feel with a different
            // stability budget, not a tighter version of this one.
            ShifterSettings s = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.ShortThrowName));

            Assert.Equal(GatePattern.H7R, s.Pattern);

            Assert.True(s.SlotStopForcePct > 0, "the short-throw profile ships with no slot bottom");
            Assert.True(s.SlotOvertravel > s.WallRamp,
                "the landing is all fade: " + s.SlotOvertravel + " against a " + s.WallRamp + " bite");

            Assert.True(s.SlotHalfWidth > 0, "the loose profile ships with railed slots");
            Assert.True(s.ChannelFreeDepth > 0, "the loose profile ships with a railed tunnel");
            Assert.Equal(0, s.DetentResistPct);
        }

        [Fact]
        public void TheTwoSevenRPresetsAreOneTuneWithTwoThrows()
        {
            // The claim the profile list makes by shipping two 7+R entries: they are the same
            // gate, and the thing a user picks between them is a throw length. It was not always
            // true - the long-throw profile carried an older, firmer tune with every stabiliser
            // off, a 6000 wall bite against 3816 and a lateral pin at 100 against 80, which reads
            // as the stabler shape and was jerky in the hand. Shipping two different answers to
            // "how should this gate feel" made the dropdown a guess.
            //
            // Only the four dials that say where the slot ends may differ, and the whole reason
            // ReleaseDepth is one of them is that it is measured from the extreme: the short-throw
            // lever comes to rest in its landing and the long-throw one at the base's mechanical
            // stop, so asking a hand for the same pull out of gear needs different numbers.
            ShifterSettings gate = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.SevenRName));
            ShifterSettings shortThrow =
                Find(DefaultProfiles.Create(), Preset(DefaultProfiles.ShortThrowName));

            HashSet<string> theThrow = new HashSet<string>(StringComparer.Ordinal)
            {
                "EngageDepth", "ReleaseDepth", "SlotOvertravel", "SlotStopForcePct",
                "ThrowFromCentre"   // the same stored fact as EngageDepth, read from the centre
            };

            foreach (PropertyInfo prop in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (theThrow.Contains(prop.Name)) continue;

                Assert.Equal(prop.GetValue(shortThrow, null), prop.GetValue(gate, null));
            }

            // ...and the throw really does differ, or the test above would pass on two copies.
            Assert.Equal(0, gate.SlotStopForcePct);
            Assert.True(shortThrow.SlotStopForcePct > 0);
        }

        [Fact]
        public void TheWideFiveRIsTheSevenRGateWithOnlyThePatternChanged()
        {
            // 5+R ships as a copy, so the two stay tuned alike by construction. If someone tunes
            // them apart on purpose this test is the place to say so - it failing means the claim
            // in DefaultProfiles' comment is no longer true.
            //
            // It is the WIDE one that carries the claim now. The plain 5+R is that same gate
            // narrowed, which is a difference of one dial and is pinned by the test below.
            ShifterSettings sevenR = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.SevenRName));
            ShifterSettings fiveR = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.FiveRWideName));

            foreach (PropertyInfo prop in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.Name == "Pattern" || prop.Name == "PatternIndex") continue;

                // The percent-of-column-spacing views are meant to differ here: they are the
                // same raw dial read against a different ColumnSpacing, which is exactly what
                // changing Pattern changes. Comparing them would fail this test for the one
                // property family whose whole point is to move with the pattern.
                if (prop.Name.EndsWith("Percent", StringComparison.Ordinal)) continue;

                Assert.Equal(prop.GetValue(sevenR, null), prop.GetValue(fiveR, null));
            }

            Assert.Equal(100, fiveR.PatternWidthPct);
        }

        [Fact]
        public void TheTwoFiveRPresetsAreOneGateAtTwoWidths()
        {
            // Why there are two. Three columns spread over the whole axis put 32767 counts
            // between them against a four-column gate's 21845 - half again the reach for every
            // shift - so the plain 5+R is narrowed and the wide one keeps the full sweep for
            // anyone who wants it. One dial apart, and nothing else: narrowing is a distance, not
            // a different tune.
            ShifterSettings narrow = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.FiveRName));
            ShifterSettings wide = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.FiveRWideName));

            foreach (PropertyInfo prop in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.Name == "PatternWidthPct") continue;
                if (prop.Name.EndsWith("Percent", StringComparison.Ordinal)) continue;

                Assert.Equal(prop.GetValue(wide, null), prop.GetValue(narrow, null));
            }

            Assert.Equal(100, wide.PatternWidthPct);
            Assert.Equal(DefaultProfiles.NarrowWidthPct, narrow.PatternWidthPct);
            Assert.True(narrow.ToEngineConfig().BuildGeometry().ColumnSpacing
                        < wide.ToEngineConfig().BuildGeometry().ColumnSpacing);
        }

        [Fact]
        public void EveryNarrowedPresetShipsTheWallItsEdgeNeeds()
        {
            // Load-bearing for the two narrowed presets and for nothing else. Outside the
            // outermost columns a narrowed pattern leaves bare travel with no gear in it, and the
            // neutral tunnel is deliberately free everywhere else - so a narrowed preset with no
            // edge wall is a lever that slides to the base's own stop with nothing to say the
            // gate ended. That is exactly what the first narrowed build did, reported from the
            // rig, and it is why these two must never ship narrowed with the wall at zero.
            foreach (ShifterProfile p in DefaultProfiles.Create().Profiles)
            {
                if (p.Settings.PatternWidthPct >= 100) continue;

                Assert.True(p.Settings.PatternEdgeForcePct > 0,
                    p.Name + " is narrowed to " + p.Settings.PatternWidthPct + "% with no edge wall");
            }
        }

        private static string Preset(string bareName)
        {
            return DefaultProfiles.Preset(bareName);
        }

        private static ShifterSettings Find(ProfileStore store, string name)
        {
            foreach (ShifterProfile p in store.Profiles)
            {
                if (p.Name == name) return p.Settings;
            }
            Assert.Fail("no shipped profile named " + name);
            return null;
        }
    }
}
