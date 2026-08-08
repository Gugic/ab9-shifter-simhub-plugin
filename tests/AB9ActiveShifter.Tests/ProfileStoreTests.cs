using System.Collections.Generic;
using AB9ActiveShifter;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The car-model auto-switch match, kept as a pure function on <see cref="ProfileStore"/> so
    /// it is testable without SimHub, the engine, or the settings UI in the loop.
    /// </summary>
    public class ProfileStoreTests
    {
        private static ProfileStore StoreWith(params ShifterProfile[] profiles)
        {
            return new ProfileStore
            {
                Profiles = new List<ShifterProfile>(profiles),
                ActiveProfile = profiles.Length > 0 ? profiles[0].Name : null
            };
        }

        [Fact]
        public void FindByCarModelMatchesCaseInsensitively()
        {
            ProfileStore store = StoreWith(
                new ShifterProfile { Name = "5+R", Settings = new ShifterSettings(), CarModels = new List<string> { "ks_toyota_ae86_drift" } });

            ShifterProfile match = store.FindByCarModel("KS_TOYOTA_AE86_DRIFT");

            Assert.NotNull(match);
            Assert.Equal("5+R", match.Name);
        }

        [Fact]
        public void FindByCarModelReturnsNullWhenNoProfileClaimsTheCar()
        {
            ProfileStore store = StoreWith(
                new ShifterProfile { Name = "5+R", Settings = new ShifterSettings(), CarModels = new List<string> { "ks_toyota_ae86_drift" } },
                new ShifterProfile { Name = "6+R", Settings = new ShifterSettings(), CarModels = new List<string> { "bmw_m3_e30_drift" } });

            Assert.Null(store.FindByCarModel("porsche_911_carrera_rsr"));
        }

        [Fact]
        public void FindByCarModelReturnsNullForNullOrEmptyInput()
        {
            ProfileStore store = StoreWith(
                new ShifterProfile { Name = "5+R", Settings = new ShifterSettings(), CarModels = new List<string> { "ks_toyota_ae86_drift" } });

            Assert.Null(store.FindByCarModel(null));
            Assert.Null(store.FindByCarModel(""));
        }

        [Fact]
        public void FindByCarModelIgnoresProfilesWithNoVehicleList()
        {
            // The default: a profile nobody has opted into auto-switching must never match.
            ProfileStore store = StoreWith(
                new ShifterProfile { Name = "5+R", Settings = new ShifterSettings() });

            Assert.Null(store.FindByCarModel("anything"));
        }

        [Fact]
        public void FindByCarModelPrefersTheEarlierProfileOnADuplicateEntry()
        {
            // Deliberately simple: resolving a genuine conflict is the user's call, not something
            // to guess at. The rule just has to be stable and documented, which "first wins" is.
            ProfileStore store = StoreWith(
                new ShifterProfile { Name = "First", Settings = new ShifterSettings(), CarModels = new List<string> { "shared_car" } },
                new ShifterProfile { Name = "Second", Settings = new ShifterSettings(), CarModels = new List<string> { "shared_car" } });

            Assert.Equal("First", store.FindByCarModel("shared_car").Name);
        }

        [Fact]
        public void NewProfileStartsWithNoVehicleModels()
        {
            // So a freshly duplicated or default profile never accidentally auto-switches.
            var profile = new ShifterProfile { Name = "Fresh", Settings = new ShifterSettings() };
            Assert.Empty(profile.CarModels);
        }
    }
}
