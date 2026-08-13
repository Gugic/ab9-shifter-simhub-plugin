using System.Collections.Generic;
using AB9ActiveShifter;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Presets: the shipped profiles, always present, never edited in place.
    ///
    /// The whole design rests on one decision - a preset's name carries a prefix no local profile
    /// is allowed to hold. That is what makes the two sets impossible to collide, which in turn is
    /// what lets presets be rebuilt on every start without a migration step and without ever
    /// touching something a user tuned. Most of what is checked here is that reservation holding
    /// on every path a name can arrive by.
    /// </summary>
    public class PresetProfileTests
    {
        private static ProfileStore StoreWithLocals(params string[] names)
        {
            var store = new ProfileStore { Profiles = new List<ShifterProfile>() };
            foreach (string name in names)
            {
                store.Profiles.Add(new ShifterProfile { Name = name, Settings = new ShifterSettings() });
            }
            store.ActiveProfile = names.Length > 0 ? names[0] : null;
            return store;
        }

        [Fact]
        public void PresetsArriveWithoutDisturbingAnythingTunedHere()
        {
            // The upgrade path, and the reason there is no migration to write. An install that
            // predates presets keeps every profile it had, in order, with its settings object
            // untouched - the presets simply appear underneath.
            ProfileStore store = StoreWithLocals("My gate", "7+R lockout", "Wet setup");
            ShifterSettings mine = store.Profiles[1].Settings;

            store.EnsurePresets(DefaultProfiles.Presets());

            Assert.Equal("My gate", store.Profiles[0].Name);
            Assert.Equal("7+R lockout", store.Profiles[1].Name);
            Assert.Equal("Wet setup", store.Profiles[2].Name);
            Assert.Same(mine, store.Profiles[1].Settings);

            // A local profile may hold the bare name of a preset - that is exactly what a fork
            // produces - and it is a different profile from the preset beside it.
            Assert.False(DefaultProfiles.IsPreset("7+R lockout"));
            Assert.True(store.NameTaken(DefaultProfiles.Preset(DefaultProfiles.SevenRName)));
        }

        [Fact]
        public void PresetsSitAtTheEndOfTheList()
        {
            ProfileStore store = StoreWithLocals("Mine");
            store.EnsurePresets(DefaultProfiles.Presets());

            Assert.Equal(1, store.FirstPresetIndex());
            for (int i = store.FirstPresetIndex(); i < store.Profiles.Count; i++)
            {
                Assert.True(DefaultProfiles.IsPreset(store.Profiles[i].Name));
            }
        }

        [Fact]
        public void ADeletedPresetComesBackAndAStalePresetIsRefreshed()
        {
            // "Always available" is the point of the feature, so it is re-created rather than
            // merely protected. A stale copy under a preset's name is replaced for the same
            // reason: a preset is immutable, so anything sitting there is either identical to
            // what ships or left over from an older build.
            ProfileStore store = DefaultProfiles.Create();
            string name = DefaultProfiles.Preset(DefaultProfiles.SevenRName);

            store.Profiles.RemoveAll(p => p.Name == name);
            store.Profiles.Add(new ShifterProfile
            {
                Name = DefaultProfiles.Preset(DefaultProfiles.FiveRName),
                Settings = new ShifterSettings { OverallGainPct = 3 }
            });

            store.EnsurePresets(DefaultProfiles.Presets());

            Assert.True(store.NameTaken(name));
            Assert.Equal(
                DefaultProfiles.BuildPreset(DefaultProfiles.Preset(DefaultProfiles.FiveRName))
                    .Settings.OverallGainPct,
                Find(store, DefaultProfiles.Preset(DefaultProfiles.FiveRName)).Settings.OverallGainPct);
        }

        [Fact]
        public void APrefixedNameThisBuildDoesNotKnowIsLeftAlone()
        {
            // A settings file can outlive the build that wrote it. If a later version ships a
            // preset this one has never heard of, downgrading must not delete it - so the prefix
            // alone is not enough to mark something for replacement; the bare name has to be one
            // we actually ship.
            ProfileStore store = StoreWithLocals("Mine");
            store.Profiles.Add(new ShifterProfile
            {
                Name = DefaultProfiles.PresetPrefix + "Something From The Future",
                Settings = new ShifterSettings()
            });

            store.EnsurePresets(DefaultProfiles.Presets());

            Assert.True(store.NameTaken(DefaultProfiles.PresetPrefix + "Something From The Future"));
        }

        [Fact]
        public void TheReservedPrefixCannotBeTakenByAnyNameAUserSupplies()
        {
            // Add, rename and import all mint names through UniqueName, which is why the
            // reservation lives there rather than at three call sites. Without it a file from a
            // stranger could arrive named "(Preset) 7+R lockout", be treated as immutable, and be
            // silently replaced by EnsurePresets on the next start - a shared file deleting a tune.
            ProfileStore store = DefaultProfiles.Create();

            Assert.Equal("7+R lockout", store.UniqueName(DefaultProfiles.Preset(DefaultProfiles.SevenRName)));
            Assert.Equal("Anything", store.UniqueName(DefaultProfiles.PresetPrefix + "Anything"));

            // Stacked prefixes are stripped to nothing, not down to one.
            Assert.Equal("x", store.UniqueName(
                DefaultProfiles.PresetPrefix + DefaultProfiles.PresetPrefix + "x"));

            // And a name that is nothing but the prefix still has to come out usable.
            Assert.False(string.IsNullOrWhiteSpace(store.UniqueName(DefaultProfiles.PresetPrefix)));
            Assert.False(DefaultProfiles.IsPreset(store.UniqueName(DefaultProfiles.PresetPrefix)));
        }

        [Fact]
        public void ForkingAPresetKeepsTheSettingsObjectAndPutsThePresetBack()
        {
            // The property the settings page depends on. A fork fires from the first change of a
            // dial, which is very often the first pixel of a slider drag - so the object the
            // DataContext is bound to has to survive it. Renaming the profile around it does; a
            // clone-and-swap would leave the rest of that drag writing into a discarded profile.
            ProfileStore store = DefaultProfiles.Create();
            string name = DefaultProfiles.Preset(DefaultProfiles.SevenRName);
            store.ActiveProfile = name;

            ShifterSettings edited = Find(store, name).Settings;
            edited.OverallGainPct = 42;

            string local = store.ForkPreset(name, DefaultProfiles.BuildPreset(name));

            Assert.Equal("7+R lockout", local);
            Assert.Equal(local, store.ActiveProfile);
            Assert.Same(edited, Find(store, local).Settings);
            Assert.Equal(42, Find(store, local).Settings.OverallGainPct);

            // The preset is back, in its own right, carrying the shipped value rather than the
            // edited one - it has to be rebuilt rather than copied, because by the time a fork
            // runs the change has already been applied and there is no pristine copy in memory.
            Assert.True(store.NameTaken(name));
            Assert.NotEqual(42, Find(store, name).Settings.OverallGainPct);
            Assert.NotSame(edited, Find(store, name).Settings);
        }

        [Fact]
        public void ForkingCountsUpWhenTheBareNameIsAlreadyTaken()
        {
            // The numbering the user asked for, and the reason a second and third attempt from
            // the same preset do not collide.
            ProfileStore store = DefaultProfiles.Create();
            store.Profiles.Insert(0, new ShifterProfile
            {
                Name = "7+R lockout",
                Settings = new ShifterSettings()
            });

            string name = DefaultProfiles.Preset(DefaultProfiles.SevenRName);

            store.ActiveProfile = name;
            Assert.Equal("7+R lockout 2", store.ForkPreset(name, DefaultProfiles.BuildPreset(name)));

            store.ActiveProfile = name;
            Assert.Equal("7+R lockout 3", store.ForkPreset(name, DefaultProfiles.BuildPreset(name)));
        }

        [Fact]
        public void AForkLandsAboveThePresetsAndThePresetsStayLast()
        {
            ProfileStore store = DefaultProfiles.Create();
            string name = DefaultProfiles.Preset(DefaultProfiles.PrndName);
            store.ActiveProfile = name;

            string local = store.ForkPreset(name, DefaultProfiles.BuildPreset(name));

            int localIndex = IndexOf(store, local);
            Assert.True(localIndex < store.FirstPresetIndex());

            for (int i = store.FirstPresetIndex(); i < store.Profiles.Count; i++)
            {
                Assert.True(DefaultProfiles.IsPreset(store.Profiles[i].Name));
            }
        }

        [Fact]
        public void ForkingSomethingThatIsNotAPresetDoesNothing()
        {
            ProfileStore store = StoreWithLocals("Mine");
            Assert.Null(store.ForkPreset("Mine", DefaultProfiles.BuildPreset("Mine")));
            Assert.Null(store.ForkPreset(null, null));
            Assert.Single(store.Profiles);
        }

        [Fact]
        public void AMachineFactIsNotATuningChangeAndMustNotForkAPreset()
        {
            // What the fork asks before it fires. Polarity calibration writes its measured result
            // through the same change notification a slider does, and a preset that forked itself
            // the moment calibration finished would be a copy nobody asked for - holding the one
            // flag that lifts the 10% force cap. Same list a shared file refuses, deliberately:
            // "is this part of a tune" has one answer.
            Assert.False(ProfileTransfer.IsTuning("PolarityConfirmed"));
            Assert.False(ProfileTransfer.IsTuning("InvertConstantX"));
            Assert.False(ProfileTransfer.IsTuning("VJoyDeviceId"));
            Assert.False(ProfileTransfer.IsTuning("PedalDeviceId"));
            Assert.False(ProfileTransfer.IsTuning("Enabled"));
            Assert.False(ProfileTransfer.IsTuning("FreeStick"));

            // A bulk notification is a re-read, not an edit.
            Assert.False(ProfileTransfer.IsTuning(null));
            Assert.False(ProfileTransfer.IsTuning(""));

            // And the things that are tuning, including the vehicle list, which lives on the
            // profile rather than on the settings and so never appears in NotShared.
            Assert.True(ProfileTransfer.IsTuning("OverallGainPct"));
            Assert.True(ProfileTransfer.IsTuning("Pattern"));
            Assert.True(ProfileTransfer.IsTuning("EngageDepth"));
            Assert.True(ProfileTransfer.IsTuning("CarModels"));
        }

        private static ShifterProfile Find(ProfileStore store, string name)
        {
            foreach (ShifterProfile p in store.Profiles)
            {
                if (p.Name == name) return p;
            }
            Assert.Fail("no profile named " + name);
            return null;
        }

        private static int IndexOf(ProfileStore store, string name)
        {
            for (int i = 0; i < store.Profiles.Count; i++)
            {
                if (store.Profiles[i].Name == name) return i;
            }
            return -1;
        }
    }
}
