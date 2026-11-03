using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class TuningScaleTests
    {
        [Fact]
        public void EveryCountSurvivesADisplayRoundTrip()
        {
            foreach (var scale in new[] { new TuningScale(0, 65535), new TuningScale(60, 6000), new TuningScale(28000, 500) })
            {
                int first = (int)System.Math.Min(scale.First, scale.Last);
                int last = (int)System.Math.Max(scale.First, scale.Last);
                for (int raw = first; raw <= last; raw++)
                    Assert.Equal(raw, scale.ToRaw(scale.ToDisplay(raw)));
            }
        }

        [Fact]
        public void RawMappingRetainsPrecisionForExistingValuesAndUndo()
        {
            var distance = new TuningScale(0, 65535);
            Assert.Equal(6554, distance.ToRaw(10));
            Assert.Equal(6560, distance.ToRaw(10.01));
            var damper = new TuningScale(0, 100);
            Assert.Equal(35.15, damper.ToRaw(35.15, 0.01), 8);
        }

        [Fact]
        public void ReadoutsAndButtonIncrementsUseCleanTenths()
        {
            var width = new TuningScale(30, 100);
            Assert.Equal(28.6, TuningScale.SnapDisplay(width.ToDisplay(50)), 8);
            Assert.Equal(1.5, width.DisplayIncrement(1), 8);
            Assert.Equal(1.7, new TuningScale(0, 60).DisplayIncrement(1), 8);
            Assert.Equal(2.5, new TuningScale(0, 40).DisplayIncrement(1), 8);
            Assert.Equal(0.1, new TuningScale(0, 65535).DisplayIncrement(1), 8);
            Assert.Equal(10.1, TuningScale.SnapDisplay(10.05), 8);
        }

        [Fact]
        public void UserEditsSnapBeforeTheyBecomeSavedCounts()
        {
            var distance = new TuningScale(0, 65535);
            Assert.Equal(6554, distance.ToRaw(TuningScale.SnapDisplay(10.04)));
            Assert.Equal(6619, distance.ToRaw(TuningScale.SnapDisplay(10.06)));
        }

        [Fact]
        public void DisplayBoundsDoNotTurnAnOutlyingSavedValueIntoAValidEdit()
        {
            var scale = new TuningScale(1000, 30000);
            Assert.False(scale.Contains(35317));
            Assert.Equal(100, scale.ToDisplay(35317));
            Assert.Equal(30000, scale.ToRaw(100));
        }
    }
}
