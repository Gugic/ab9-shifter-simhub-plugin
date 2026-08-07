using System.Collections.Generic;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// Walking the profile cycle, which is what a bound hotkey does. Worth testing away from the
    /// UI because the press arrives at arbitrary moments and the awkward cases are all silent
    /// ones: a cycle naming a profile that was renamed, a cycle of one, a press while sitting on
    /// a profile that is not in the cycle at all.
    /// </summary>
    public class ProfileCycleTests
    {
        private static ProfileStore Store(params string[] names)
        {
            var store = new ProfileStore { Profiles = new List<ShifterProfile>() };
            foreach (string n in names)
            {
                store.Profiles.Add(new ShifterProfile { Name = n, Settings = new ShifterSettings() });
            }
            store.ActiveProfile = names.Length > 0 ? names[0] : null;
            return store;
        }

        [Fact]
        public void AnEmptyCycleWalksEveryProfile()
        {
            // What someone gets who binds the key and never opens the membership list. Any other
            // reading of "no profiles chosen" would make the hotkey do nothing out of the box.
            ProfileStore s = Store("Sequential", "7+R lockout", "5+R");

            Assert.Equal("7+R lockout", s.NextInCycle("Sequential", 1));
            Assert.Equal("5+R", s.NextInCycle("7+R lockout", 1));
            Assert.Equal("Sequential", s.NextInCycle("5+R", 1));
        }

        [Fact]
        public void ItWrapsBothWays()
        {
            ProfileStore s = Store("A", "B", "C");

            Assert.Equal("C", s.NextInCycle("A", -1));   // back off the front, round to the end
            Assert.Equal("A", s.NextInCycle("C", 1));    // forward off the end, round to the front
        }

        [Fact]
        public void AChosenCycleIsWalkedInItsOwnOrder()
        {
            // The whole point of the feature the user asked for: two profiles out of several,
            // toggled with one key. The order is the list's, not the store's.
            ProfileStore s = Store("Sequential", "7+R lockout", "5+R");
            s.CycleProfiles = new List<string> { "7+R lockout", "Sequential" };

            Assert.Equal("Sequential", s.NextInCycle("7+R lockout", 1));
            Assert.Equal("7+R lockout", s.NextInCycle("Sequential", 1));

            // 5+R is excluded, so it is never reached in either direction.
            Assert.Equal("7+R lockout", s.NextInCycle("Sequential", -1));
        }

        [Fact]
        public void ARenamedOrDeletedProfileInTheCycleIsSkipped()
        {
            // A cycle list can name something that no longer exists. Skipping it keeps the key
            // working; activating a missing profile, or refusing to move, would both look like
            // a broken binding rather than a stale list.
            ProfileStore s = Store("A", "B");
            s.CycleProfiles = new List<string> { "A", "GoneAway", "B" };

            Assert.Equal(new List<string> { "A", "B" }, s.CycleOrder());
            Assert.Equal("B", s.NextInCycle("A", 1));
            Assert.Equal("A", s.NextInCycle("B", 1));
        }

        [Fact]
        public void ACycleNamingNothingThatExistsFallsBackToEveryProfile()
        {
            ProfileStore s = Store("A", "B");
            s.CycleProfiles = new List<string> { "Ghost", "Phantom" };

            Assert.Equal(new List<string> { "A", "B" }, s.CycleOrder());
        }

        [Fact]
        public void ACycleOfOneGoesNowhere()
        {
            // Returning the profile already active would push a config swap - which rebuilds the
            // gate and releases the held gear - every time the key was pressed.
            ProfileStore s = Store("A", "B");
            s.CycleProfiles = new List<string> { "A" };

            Assert.Null(s.NextInCycle("A", 1));
            Assert.Null(s.NextInCycle("A", -1));
        }

        [Fact]
        public void PressingWhileOnAProfileOutsideTheCycleStillLandsSomewhere()
        {
            ProfileStore s = Store("A", "B", "C");
            s.CycleProfiles = new List<string> { "A", "B" };

            Assert.Equal("A", s.NextInCycle("C", 1));
            Assert.Equal("B", s.NextInCycle("C", -1));
        }

        [Fact]
        public void AnEmptyStoreIsSafeToCycle()
        {
            var empty = new ProfileStore();
            Assert.Null(empty.NextInCycle("anything", 1));
            Assert.Empty(empty.CycleOrder());
        }

        [Fact]
        public void TheCycleNeverRepeatsAProfileListedTwice()
        {
            ProfileStore s = Store("A", "B");
            s.CycleProfiles = new List<string> { "A", "B", "A" };

            Assert.Equal(new List<string> { "A", "B" }, s.CycleOrder());
        }
    }
}
