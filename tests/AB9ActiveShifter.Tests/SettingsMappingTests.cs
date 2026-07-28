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
            // SeqThrow re-expresses EngageDepth from the sequential hand's point of view:
            // counts from centre to the firing line. Writing it must move the re-arm line by
            // the same amount, because the hysteresis gap between the two is what stops a
            // lever resting on the threshold from machine-gunning shifts - shorten only the
            // firing line far enough and the release test would pass while still pushed,
            // re-arming and re-firing every wiggle.
            var s = new ShifterSettings();
            int gap = s.ReleaseDepth - s.EngageDepth;

            Assert.Equal(GateGeometry.AxisCenter - s.EngageDepth, s.SeqThrow);

            s.SeqThrow = 12000;

            Assert.Equal(GateGeometry.AxisCenter - 12000, s.EngageDepth);
            Assert.Equal(gap, s.ReleaseDepth - s.EngageDepth);

            // And back out again: lengthening the throw walks both lines home too.
            s.SeqThrow = 28767;

            Assert.Equal(GateGeometry.AxisCenter - 28767, s.EngageDepth);
            Assert.Equal(gap, s.ReleaseDepth - s.EngageDepth);
        }
    }
}
