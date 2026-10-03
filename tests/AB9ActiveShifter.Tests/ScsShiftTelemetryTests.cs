using System;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class ScsShiftTelemetryTests
    {
        // Schema-shaped POCOs: no game reader, device or other I/O is loaded by these tests.
        public class Root
        {
            public bool Paused { get; set; }
            public bool SdkActive { get; set; } = true;
            public ulong Timestamp { get; set; } = 1;
            public Truck TruckValues { get; set; } = new Truck();
        }
        public class Truck
        {
            public Constants ConstantsValues { get; set; } = new Constants();
            public Current CurrentValues { get; set; } = new Current();
        }
        public class Constants
        {
            public string Id { get; set; } = "truck";
            public Motor MotorValues { get; set; } = new Motor();
            public WheelConstants WheelsValues { get; set; } = new WheelConstants();
        }
        public class Motor
        {
            public uint SelectorCount { get; set; } = 2;
            public int[] SlotGear { get; set; } = { 1, 7, 2, 8, -1 };
            public uint[] SlotHandlePosition { get; set; } = { 2, 2, 3, 3, 1 };
            public uint[] SlotSelectors { get; set; } = { 0, 1, 0, 3, 0 };
            public float DifferentialRation { get; set; } = 4;
            public float[] GearRatiosForward { get; set; } = { 10, 8, 6, 5, 4, 3, 2, 1 };
            public float[] GearRatiosReverse { get; set; } = { -10 };
        }
        public class WheelConstants
        {
            public uint Count { get; set; } = 4;
            public bool[] Powered { get; set; } = { false, false, true, true };
            public bool[] Simulated { get; set; } = { true, true, true, true };
        }
        public class Current
        {
            public CurrentMotor MotorValues { get; set; } = new CurrentMotor();
            public Wheels WheelsValues { get; set; } = new Wheels();
        }
        public class CurrentMotor { public Gear GearValues { get; set; } = new Gear(); }
        public class Gear { public bool[] HShifterSelector { get; set; } = { false, false }; }
        public class Wheels { public float[] Velocity { get; set; } = { 999, 999, 2, 4 }; }

        [Fact]
        public void TargetUsesPoweredWheelRotationsPerSecondAndConfiguredHandlePositions()
        {
            ShiftTelemetry t = new ScsShiftTelemetryReader().ReadScs(new Root(), "3,2,0,0,0,0,0,1", 100);
            Assert.Equal(5760, t.TargetRpms[0]);
            Assert.Equal(7200, t.TargetRpms[1]);
            Assert.Equal(0, t.TargetRpms[2]);
            Assert.Equal(0, t.TargetRpms[7]); // forward motion cannot match reverse
        }

        [Theory]
        [InlineData(false, false, 7200, 5760)]
        [InlineData(true, false, 1440, 0)]
        [InlineData(true, true, 0, 720)]
        [InlineData(false, true, 0, 0)]
        public void RangeAndSplitterSelectTheGamesMappingWithoutGuessing(bool range, bool split, double first, double second)
        {
            var raw = new Root();
            raw.TruckValues.CurrentValues.MotorValues.GearValues.HShifterSelector = new[] { range, split };
            ShiftTelemetry t = new ScsShiftTelemetryReader().ReadScs(raw, "2,3", 0);
            Assert.Equal(first, t.TargetRpms[0]);
            Assert.Equal(second, t.TargetRpms[1]);
        }

        [Fact]
        public void ReverseOnlyMatchesBackwardRotation()
        {
            var raw = new Root();
            raw.TruckValues.CurrentValues.WheelsValues.Velocity = new float[] { 0, 0, -2, -4 };
            ShiftTelemetry t = new ScsShiftTelemetryReader().ReadScs(raw, "2,3,0,0,0,0,0,1", 0);
            Assert.Equal(0, t.TargetRpms[0]);
            Assert.Equal(7200, t.TargetRpms[7]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InvalidRatioSignsCannotMatchTheOppositeDirection(bool reverse)
        {
            var raw = new Root();
            if (reverse) raw.TruckValues.ConstantsValues.MotorValues.GearRatiosReverse[0] = 10;
            else
            {
                raw.TruckValues.ConstantsValues.MotorValues.GearRatiosForward[0] = -10;
                raw.TruckValues.CurrentValues.WheelsValues.Velocity = new float[] { 0, 0, -2, -4 };
            }
            var t = new ScsShiftTelemetryReader().ReadScs(raw, reverse ? "1" : "2", 0);
            Assert.Equal(0, t.TargetRpms[0]);
        }

        [Fact]
        public void RepeatedRawFramesKeepTheirCaptureTimeEvenWhenBindingsOrSelectorsChange()
        {
            var raw = new Root();
            var reader = new ScsShiftTelemetryReader();
            ShiftTelemetry first = reader.ReadScs(raw, "2,3", 100);
            Assert.Equal(100, reader.ReadScs(raw, "2,3", 500).CapturedAtTick);
            raw.Timestamp++;
            Assert.Equal(600, reader.ReadScs(raw, "2,3", 600).CapturedAtTick);
            raw.TruckValues.CurrentValues.MotorValues.GearValues.HShifterSelector[0] = true;
            ShiftTelemetry changed = reader.ReadScs(raw, "2,3", 700);
            Assert.NotEqual(first.TransmissionKey, changed.TransmissionKey);
            Assert.Equal(600, changed.CapturedAtTick);
            Assert.Equal(600, reader.ReadScs(raw, "3,2", 800).CapturedAtTick);
        }

        [Theory]
        [InlineData("paused")]
        [InlineData("sdk")]
        [InlineData("mapping")]
        [InlineData("selectors")]
        [InlineData("selectorCount")]
        [InlineData("wheels")]
        [InlineData("powered")]
        [InlineData("speed")]
        [InlineData("ratio")]
        [InlineData("differential")]
        [InlineData("duplicate")]
        public void InvalidTruckDataNeverInventsAMatchedGear(string fault)
        {
            var raw = new Root();
            Motor motor = raw.TruckValues.ConstantsValues.MotorValues;
            if (fault == "paused") raw.Paused = true;
            if (fault == "sdk") raw.SdkActive = false;
            if (fault == "mapping") motor.SlotHandlePosition = new uint[0];
            if (fault == "selectors") raw.TruckValues.CurrentValues.MotorValues.GearValues.HShifterSelector = null;
            if (fault == "selectorCount") motor.SelectorCount = 32;
            if (fault == "wheels") raw.TruckValues.CurrentValues.WheelsValues.Velocity = new float[0];
            if (fault == "powered") raw.TruckValues.ConstantsValues.WheelsValues.Powered = new bool[4];
            if (fault == "speed") raw.TruckValues.CurrentValues.WheelsValues.Velocity[2] = float.NaN;
            if (fault == "ratio") motor.GearRatiosForward[0] = float.PositiveInfinity;
            if (fault == "differential") motor.DifferentialRation = 0;
            if (fault == "duplicate") motor.SlotSelectors[1] = 0;
            ShiftTelemetry t = new ScsShiftTelemetryReader().ReadScs(raw, "2", 0);
            Assert.Equal(0, t.TargetRpms[0]);
        }

        [Fact]
        public void PublishingCopiesTheTargetsAndPedalSubstitutionKeepsTheirIdentity()
        {
            double[] targets = { 1234 };
            var t = new TelemetryState { Shift = new ShiftTelemetry(targets, "truck", 5), VehicleKey = "game/truck" };
            targets[0] = 0;
            var scratch = new TelemetryState();
            scratch.CopyFromWithClutch(t, 75);
            Assert.Same(t.Shift, scratch.Shift);
            Assert.Equal(1234, scratch.Shift.TargetRpms[0]);
            Assert.Equal(t.VehicleKey, scratch.VehicleKey);
            Assert.Equal(75, scratch.Clutch);
        }

        [Fact]
        public void ChangedOrUnrelatedRawSchemasDegradeToUnknown()
        {
            var reader = new ScsShiftTelemetryReader();
            Assert.Null(reader.Read(new object(), "2", 0));
            Assert.Equal(0, reader.ReadScs(new object(), "2", 0).TargetRpms[0]);
        }
    }
}
