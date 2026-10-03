using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter
{
    /// <summary>
    /// Optional SCS raw-data adapter, called only on SimHub's data thread. SimHub owns its
    /// reader assemblies, so cached reflection avoids a dependency on a particular ETS2Reader
    /// version. Missing/changed fields produce an unknown truck frame, never guessed ratios.
    /// Only copied RPMs cross to the engine; no reader object or array crosses that boundary.
    /// </summary>
    public sealed class ScsShiftTelemetryReader
    {
        private readonly Dictionary<Type, Dictionary<string, PropertyInfo>> _properties =
            new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        private ulong _timestamp;
        private bool _seeded;
        private int _capturedAt;
        private bool _wasScs;

        public ShiftTelemetry Read(object raw, string handlePositions, int now)
        {
            if (raw == null) return _wasScs ? Unknown(now) : null;
            string type = raw.GetType().FullName;
            if (!type.StartsWith("SCSSdkClient.", StringComparison.Ordinal)
                && !type.StartsWith("Ets2SdkClient.", StringComparison.Ordinal)) { _wasScs = false; return null; }
            _wasScs = true;
            return ReadScs(raw, handlePositions, now);
        }

        // Public to exercise the adapter with ordinary POCOs, without loading SimHub or I/O.
        public ShiftTelemetry ReadScs(object raw, string handlePositions, int now)
        {
            var targets = new double[8];
            try
            {
                object truck = Get(raw, "TruckValues");
                object constants = Get(truck, "ConstantsValues");
                object motor = Get(constants, "MotorValues");
                object wheelConstants = Get(constants, "WheelsValues");
                object current = Get(truck, "CurrentValues");
                object wheels = Get(current, "WheelsValues");
                object gear = Get(Get(current, "MotorValues"), "GearValues");
                var ratios = Get(motor, "GearRatiosForward") as float[];
                var reverse = Get(motor, "GearRatiosReverse") as float[];
                var gears = Get(motor, "SlotGear") as int[];
                var handles = Get(motor, "SlotHandlePosition") as uint[];
                var slotSelectors = Get(motor, "SlotSelectors") as uint[];
                var selectors = Get(gear, "HShifterSelector") as bool[];
                uint selectorCount = Convert.ToUInt32(Get(motor, "SelectorCount"), CultureInfo.InvariantCulture);
                if (selectorCount > 31 || (selectorCount > 0 && (selectors == null || selectors.Length < selectorCount)))
                    return Unknown(now);
                uint mask = 0;
                for (int i = 0; i < selectorCount; i++) if (selectors[i]) mask |= 1u << i;
                string identity = Convert.ToString(Get(constants, "Id"), CultureInfo.InvariantCulture) + "|"
                    + Join(ratios) + "|" + Join(reverse) + "|" + Join(gears) + "|" + Join(handles) + "|"
                    + Join(slotSelectors) + "|" + mask + "|" + handlePositions + "|" + Get(motor, "DifferentialRation");
                ulong timestamp = Convert.ToUInt64(Get(raw, "Timestamp"), CultureInfo.InvariantCulture);
                if (!_seeded || timestamp != _timestamp)
                {
                    _timestamp = timestamp;
                    _capturedAt = now;
                    _seeded = true;
                }
                if (!(Get(raw, "SdkActive") is bool active) || !active
                    || !(Get(raw, "Paused") is bool paused) || paused
                    || Get(raw, "Timestamp") == null) return new ShiftTelemetry(targets, identity, _capturedAt);

                double differential = Convert.ToDouble(Get(motor, "DifferentialRation"), CultureInfo.InvariantCulture);
                var powered = Get(wheelConstants, "Powered") as bool[];
                var simulated = Get(wheelConstants, "Simulated") as bool[];
                var velocities = Get(wheels, "Velocity") as float[];
                int count = Convert.ToInt32(Get(wheelConstants, "Count"), CultureInfo.InvariantCulture);
                if (!RevMatchModel.FinitePositive(differential) || powered == null || simulated == null || velocities == null
                    || count <= 0 || powered.Length < count || simulated.Length < count || velocities.Length < count
                    || gears == null || handles == null || slotSelectors == null
                    || handles.Length != gears.Length || slotSelectors.Length != gears.Length) return new ShiftTelemetry(targets, identity, _capturedAt);
                double wheelSpeed = 0;
                int driven = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!powered[i] || !simulated[i]) continue;
                    if (!RevMatchModel.Finite(velocities[i])) return new ShiftTelemetry(targets, identity, _capturedAt);
                    wheelSpeed += velocities[i];
                    driven++;
                }
                if (driven == 0) return new ShiftTelemetry(targets, identity, _capturedAt);
                wheelSpeed = wheelSpeed / driven * 60 * differential;
                uint[] positions = ParseHandlePositions(handlePositions);
                uint selectorMask = selectorCount == 0 ? 0 : (1u << (int)selectorCount) - 1;
                for (int button = 0; button < targets.Length; button++)
                {
                    if (positions[button] == 0) continue;
                    int selected = 0, matches = 0;
                    for (int j = 0; j < gears.Length; j++)
                    {
                        if (handles[j] != positions[button] || (slotSelectors[j] & selectorMask) != mask) continue;
                        selected = gears[j];
                        matches++;
                    }
                    // Ambiguous or neutral mapping is unavailable. Reverse ratios are signed
                    // in the SDK: a forward-rolling wheel must not grant a reverse shift.
                    if (matches != 1 || selected == 0 || selected == int.MinValue) continue;
                    float[] table = selected > 0 ? ratios : reverse;
                    int index = Math.Abs(selected) - 1;
                    if (table == null || index >= table.Length || !RevMatchModel.Finite(table[index])) continue;
                    double rpm = wheelSpeed * table[index];
                    if (RevMatchModel.FinitePositive(rpm)) targets[button] = rpm;
                }
                return new ShiftTelemetry(targets, identity, _capturedAt);
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is InvalidCastException
                || ex is FormatException || ex is OverflowException || ex is ArgumentException)
            { return Unknown(now); }
        }

        private ShiftTelemetry Unknown(int now) { return new ShiftTelemetry(new double[8], "SCS:unknown", now); }
        private static string Join<T>(T[] values) { return values == null ? "?" : string.Join(",", values); }

        private object Get(object owner, string name)
        {
            if (owner == null) return null;
            Type type = owner.GetType();
            Dictionary<string, PropertyInfo> properties;
            if (!_properties.TryGetValue(type, out properties))
            {
                properties = new Dictionary<string, PropertyInfo>();
                _properties.Add(type, properties);
            }
            PropertyInfo property;
            if (!properties.TryGetValue(name, out property))
            {
                property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                properties.Add(name, property);
            }
            return property == null ? null : property.GetValue(owner, null);
        }

        public static uint[] ParseHandlePositions(string text)
        {
            var positions = new uint[8];
            string[] values = (text ?? "").Split(',');
            for (int i = 0; i < Math.Min(positions.Length, values.Length); i++)
            {
                uint value;
                if (uint.TryParse(values[i].Trim(), out value) && value <= 32) positions[i] = value;
            }
            return positions;
        }
    }
}
