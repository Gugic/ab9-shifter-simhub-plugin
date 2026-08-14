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
        public void FiveRIsTheSevenRGateWithOnlyThePatternChanged()
        {
            // 5+R is shipped as a copy, so the two stay tuned alike by construction. If someone
            // tunes them apart on purpose this test is the place to say so - it failing means
            // the claim in DefaultProfiles' comment is no longer true.
            ShifterSettings sevenR = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.SevenRName));
            ShifterSettings fiveR = Find(DefaultProfiles.Create(), Preset(DefaultProfiles.FiveRName));

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
