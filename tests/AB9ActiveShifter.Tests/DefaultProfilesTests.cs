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

            Assert.Equal(GatePattern.Sequential, Find(store, "Sequential").Pattern);
            Assert.Equal(GatePattern.H7R, Find(store, "7+R lockout").Pattern);
            Assert.Equal(GatePattern.H5R, Find(store, "5+R").Pattern);
        }

        [Fact]
        public void FiveRIsTheSevenRGateWithOnlyThePatternChanged()
        {
            // 5+R is shipped as a copy, so the two stay tuned alike by construction. If someone
            // tunes them apart on purpose this test is the place to say so - it failing means
            // the claim in DefaultProfiles' comment is no longer true.
            ShifterSettings sevenR = Find(DefaultProfiles.Create(), "7+R lockout");
            ShifterSettings fiveR = Find(DefaultProfiles.Create(), "5+R");

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
