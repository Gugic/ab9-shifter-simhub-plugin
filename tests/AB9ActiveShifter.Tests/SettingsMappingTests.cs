using AB9ActiveShifter;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The settings POCO's derived dials. No I/O here - ShifterSettings is plain state - but
    /// a mapping bug is a force bug once it reaches the engine, so the arithmetic is pinned.
    /// </summary>
    public class SettingsMappingTests
    {
        [Fact]
        public void TheThrowDialMovesTheFireAndRearmLinesTogether()
        {
            // ThrowFromCentre re-expresses EngageDepth from the hand's point of view: counts
            // from centre to the line a push registers at. Writing it must move the release
            // line by the same amount, because the hysteresis gap between the two is what stops
            // a lever resting on the threshold from machine-gunning shifts - shorten only the
            // firing line far enough and the release test would pass while still pushed,
            // re-arming and re-firing every wiggle.
            var s = new ShifterSettings();
            int gap = s.ReleaseDepth - s.EngageDepth;

            Assert.Equal(GateGeometry.AxisCenter - s.EngageDepth, s.ThrowFromCentre);

            s.ThrowFromCentre = 12000;

            Assert.Equal(GateGeometry.AxisCenter - 12000, s.EngageDepth);
            Assert.Equal(gap, s.ReleaseDepth - s.EngageDepth);

            // And back out again: lengthening the throw walks both lines home too.
            s.ThrowFromCentre = 28767;

            Assert.Equal(GateGeometry.AxisCenter - 28767, s.EngageDepth);
            Assert.Equal(gap, s.ReleaseDepth - s.EngageDepth);
        }

        // ---------------------------------------------------------------- percent-of-spacing views

        /// <summary>Default pattern's spacing: AxisMax(65535) / (4 columns - 1), the figure
        /// ForceComposer.SlotRamp's own comment already quotes as what the shipped geometry allows.</summary>
        private const int DefaultSpacing = 21845;

        [Fact]
        public void WallRampPercentReflectsTheDefaultsSpacing()
        {
            var s = new ShifterSettings();

            Assert.Equal(600, s.WallRamp);
            Assert.Equal(600 * 100.0 / DefaultSpacing, s.WallRampPercent, 6);
        }

        [Fact]
        public void SettingThePercentWritesBackTheRoundedRawCount()
        {
            var s = new ShifterSettings();

            s.WallRampPercent = 5.0;

            // 5% of 21845 is 1092.25, which Math.Round takes to 1092.
            Assert.Equal(1092, s.WallRamp);
        }

        [Fact]
        public void ChangingTheRawValueAlsoNotifiesThePercentView()
        {
            // The hidden slider of the pair (whichever mode is not currently displayed) must
            // not go stale: WPF only refreshes a binding on a PropertyChanged for its own
            // path, not merely because Visibility later reveals it.
            var s = new ShifterSettings();
            var seen = new System.Collections.Generic.List<string>();
            s.PropertyChanged += (sender, e) => seen.Add(e.PropertyName);

            s.WallRamp = 900;

            Assert.Contains("WallRamp", seen);
            Assert.Contains("WallRampPercent", seen);
        }

        [Fact]
        public void ChangingThePercentValueAlsoNotifiesTheRawProperty()
        {
            var s = new ShifterSettings();
            var seen = new System.Collections.Generic.List<string>();
            s.PropertyChanged += (sender, e) => seen.Add(e.PropertyName);

            s.WallRampPercent = 8.0;

            Assert.Contains("WallRamp", seen);
            Assert.Contains("WallRampPercent", seen);
        }

        [Fact]
        public void SwitchingPatternRescalesThePercentViewNotTheStoredCount()
        {
            // H5R has three columns instead of four, so its spacing (65535 / 2 = 32767) is
            // wider than H6R/H7R's. The raw count carried a tuned feel on the old pattern; it
            // is the PERCENT that should move here, not the stored value, because rescaling
            // the stored count automatically is a separate, higher-risk change with its own
            // tests, not this display-only toggle.
            var s = new ShifterSettings { WallRamp = 2000 };
            double percentBefore = s.WallRampPercent;

            s.Pattern = GatePattern.H5R;

            Assert.Equal(2000, s.WallRamp);
            Assert.NotEqual(percentBefore, s.WallRampPercent);
            Assert.Equal(2000 * 100.0 / 32767, s.WallRampPercent, 6);
        }

        [Fact]
        public void EveryLateralDialGetsTheSamePercentTreatment()
        {
            // Pinned on a second dial, not just WallRamp, so a copy-paste slip in one of the
            // other eight properties (wrong backing field, wrong notification name) would
            // still be caught even though most of the coverage above only exercises WallRamp.
            var s = new ShifterSettings();

            Assert.Equal(400, s.DetentHysteresis);
            Assert.Equal(400 * 100.0 / DefaultSpacing, s.DetentHysteresisPercent, 6);

            s.DetentHysteresisPercent = 10.0;
            Assert.Equal((int)System.Math.Round(10.0 / 100.0 * DefaultSpacing), s.DetentHysteresis);
        }
    }
}
