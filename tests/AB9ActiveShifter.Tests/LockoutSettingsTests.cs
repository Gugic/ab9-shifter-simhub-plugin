using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The lockout dials' plumbing through ShifterSettings: the combo adapters, the derived
    /// visibility facts the XAML binds, the flat copy into EngineConfig, and the Forces reset.
    /// The dials themselves are pinned in the force and geometry suites; this file is about a
    /// value surviving the trip from a slider to the engine and back to defaults.
    /// </summary>
    public class LockoutSettingsTests
    {
        [Fact]
        public void EveryNewLockoutDialReachesTheEngineConfigUnchanged()
        {
            var s = new ShifterSettings
            {
                LockoutPlacement = LockoutPlacement.Gap2,
                LockoutGapDirection = LockoutGapDirection.Both,
                LockoutSlotGear = 3,
                LockoutSlotDirection = LockoutSlotDirection.Exit,
                LockoutMode = LockoutMode.HotkeyAutoRearm,
                PrndLockoutGap = PrndLockoutGap.RN,
                PrndLockoutDirection = PrndLockoutDirection.TowardP,
                PrndLockoutMode = LockoutMode.HotkeyToggle,
                PrndLockoutForcePct = 42,
                PrndLockoutHalfWidth = 987
            };

            EngineConfig cfg = s.ToEngineConfig();

            Assert.Equal(LockoutPlacement.Gap2, cfg.LockoutPlacement);
            Assert.Equal(LockoutGapDirection.Both, cfg.LockoutGapDirection);
            Assert.Equal(3, cfg.LockoutSlotGear);
            Assert.Equal(LockoutSlotDirection.Exit, cfg.LockoutSlotDirection);
            Assert.Equal(LockoutMode.HotkeyAutoRearm, cfg.LockoutMode);
            Assert.Equal(PrndLockoutGap.RN, cfg.PrndLockoutGap);
            Assert.Equal(PrndLockoutDirection.TowardP, cfg.PrndLockoutDirection);
            Assert.Equal(LockoutMode.HotkeyToggle, cfg.PrndLockoutMode);
            Assert.Equal(42, cfg.PrndLockoutForcePct);
            Assert.Equal(987, cfg.PrndLockoutHalfWidth);
        }

        [Fact]
        public void ResettingForcesPutsTheWholeLockoutConfigurationBack()
        {
            var s = new ShifterSettings
            {
                LockoutPlacement = LockoutPlacement.Slot,
                LockoutGapDirection = LockoutGapDirection.TowardLow,
                LockoutSlotGear = 2,
                LockoutSlotDirection = LockoutSlotDirection.Both,
                LockoutMode = LockoutMode.HotkeyToggle,
                PrndLockoutGap = PrndLockoutGap.ND,
                PrndLockoutDirection = PrndLockoutDirection.Both,
                PrndLockoutMode = LockoutMode.HotkeyAutoRearm,
                PrndLockoutForcePct = 11,
                PrndLockoutHalfWidth = 3333
            };

            s.ResetToDefaults(ShifterSettings.ResetScope.Forces);
            var d = new ShifterSettings();

            Assert.Equal(d.LockoutPlacement, s.LockoutPlacement);
            Assert.Equal(d.LockoutGapDirection, s.LockoutGapDirection);
            Assert.Equal(d.LockoutSlotGear, s.LockoutSlotGear);
            Assert.Equal(d.LockoutSlotDirection, s.LockoutSlotDirection);
            Assert.Equal(d.LockoutMode, s.LockoutMode);
            Assert.Equal(d.PrndLockoutGap, s.PrndLockoutGap);
            Assert.Equal(d.PrndLockoutDirection, s.PrndLockoutDirection);
            Assert.Equal(d.PrndLockoutMode, s.PrndLockoutMode);
            Assert.Equal(d.PrndLockoutForcePct, s.PrndLockoutForcePct);
            Assert.Equal(d.PrndLockoutHalfWidth, s.PrndLockoutHalfWidth);
        }

        [Fact]
        public void SwitchingPatternKeepsTheLockoutChoiceBecauseItLivesInTheMap()
        {
            // Placement is map-relative, so experimenting with patterns must not eat the tune:
            // a truck profile flipped to 7+R and back keeps its Gap1 gate, and the geometry -
            // not the settings - answers what each pattern makes of it.
            var s = new ShifterSettings
            {
                Pattern = GatePattern.H6,
                LockoutPlacement = LockoutPlacement.Gap1,
                LockoutGapDirection = LockoutGapDirection.TowardLow
            };

            s.Pattern = GatePattern.H7R;
            Assert.Equal(LockoutPlacement.Gap1, s.LockoutPlacement);

            s.Pattern = GatePattern.H6;
            Assert.Equal(LockoutPlacement.Gap1, s.LockoutPlacement);
            Assert.Equal(LockoutGapDirection.TowardLow, s.LockoutGapDirection);
        }

        [Fact]
        public void TheTruckPatternCountsAsAnHPatternAndNothingElse()
        {
            var s = new ShifterSettings { Pattern = GatePattern.H6 };

            Assert.True(s.IsHPattern);
            Assert.False(s.IsSequential);
            Assert.False(s.IsPrnd);
        }

        [Fact]
        public void TheLockoutIndexAdaptersClampAndRoundTrip()
        {
            var s = new ShifterSettings();

            s.LockoutPlacementIndex = 99;
            Assert.Equal(LockoutPlacement.Slot, s.LockoutPlacement);
            s.LockoutPlacementIndex = -5;
            Assert.Equal(LockoutPlacement.PatternDefault, s.LockoutPlacement);

            s.LockoutSlotGearIndex = 7;
            Assert.Equal(8, s.LockoutSlotGear);
            s.LockoutSlotGearIndex = -3;
            Assert.Equal(1, s.LockoutSlotGear);
            s.LockoutSlotGear = 5;
            Assert.Equal(4, s.LockoutSlotGearIndex);

            s.LockoutModeIndex = 2;
            Assert.Equal(LockoutMode.HotkeyAutoRearm, s.LockoutMode);
            Assert.Equal(2, s.LockoutModeIndex);

            s.PrndLockoutGapIndex = 3;
            Assert.Equal(PrndLockoutGap.ND, s.PrndLockoutGap);
        }

        [Fact]
        public void ChoosingASlotPlacementFlipsTheDerivedVisibilityFacts()
        {
            var s = new ShifterSettings();

            // The default: a gap (the pattern's own), dials shown, gap wording.
            Assert.True(s.ShowsLockoutDials);
            Assert.True(s.IsLockoutOnGap);
            Assert.False(s.IsLockoutOnSlot);

            s.LockoutPlacement = LockoutPlacement.Slot;
            Assert.True(s.IsLockoutOnSlot);
            Assert.False(s.IsLockoutOnGap);
            Assert.True(s.ShowsLockoutDials);

            s.LockoutPlacement = LockoutPlacement.Off;
            Assert.False(s.ShowsLockoutDials);
            Assert.False(s.IsLockoutOnGap);
            Assert.False(s.IsLockoutOnSlot);

            Assert.False(s.IsLockoutHardMode);
            s.LockoutMode = LockoutMode.HotkeyToggle;
            Assert.True(s.IsLockoutHardMode);

            Assert.False(s.HasPrndLockout);
            s.PrndLockoutGap = PrndLockoutGap.PR;
            Assert.True(s.HasPrndLockout);
        }
    }
}
