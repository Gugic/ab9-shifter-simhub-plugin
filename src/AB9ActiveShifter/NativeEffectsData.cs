using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AB9ActiveShifter
{
    /// <summary>
    /// The portable part of a native effect profile. No output manager is deserialised:
    /// SimHub's manager converter can construct hardware transports, which a tune must never do.
    /// Keep this validator free of SimHub so imports remain testable without loading its UI.
    /// </summary>
    public static class NativeEffectsData
    {
        public const int MaximumLength = 262144;
        private static readonly HashSet<string> Containers = new HashSet<string>(StringComparer.Ordinal)
        {
            "ABSActiveEffectContainer", "TCActiveEffectContainer", "RPMContainer", "GearEffectContainer",
            "WheelsImpactContainer", "WheelsRumbleContainer", "WheelsVibrationContainer", "RoadTextureContainer",
            "WheelsSlipContainer", "WheelsLockContainer", "WheelsSpinAndLockContainer", "TractionLossContainer",
            "AccelerationGforceContainer", "DecelerationGforceContainer", "LateralGforceContainer",
            "SpeedContainer", "LocalizedSpeedContainer", "JumpContainer", "ImpactEffectContainer",
            "GearGrindingContainer", "GearMissedContainer", "VirtualABSEffectContainer", "CustomEffectContainer",
            "GroupContainer", "ConditionalGroupContainer", "AcceleratingGroupContainer", "DeceleratingGroupContainer",
            "BrakePressedGroupContainer", "ThrottlePressedGroupContainer", "TouchdownEffectContainer",
            "FlightsWingsLoadContainer", "StaticWindContainer",
            "AB9GrindEffectContainer", "AB9BiteEffectContainer", "AB9LimiterEffectContainer", "AB9PropertyEffectContainer"
        };
        private static readonly HashSet<string> Outputs = new HashSet<string>(StringComparer.Ordinal)
        { "SingleToneOutput", "ToneOutput", "NoToneOutput" };
        private static readonly HashSet<string> Filters = new HashSet<string>(StringComparer.Ordinal)
        { "SplineFilter", "SimpleGammaFilter", "SimpleGammaNoiseFilter", "PulseFilter", "GammaFilter" };

        public static string Validate(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            if (json.Length > MaximumLength) throw new JsonException("The effect profile is too large.");
            JObject root;
            using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 32 })
                root = JObject.Load(reader);
            if ((int?)root["Version"] != 1 || !(root["Profile"] is JObject)
                || !(root["Profile"]["EffectsContainers"] is JArray))
                throw new JsonException("The native effect profile has no effect list.");

            foreach (JObject obj in root.DescendantsAndSelf().OfType<JObject>().ToArray())
            {
                // Runtime/transport state is recreated locally, even when the native profile
                // exporter happened to include it. Never invoke its output-manager converter.
                obj.Remove("OutputManager");
                obj.Remove("IncludeOutputSettingsInProfile");
            }
            int containers = 0;
            foreach (JObject obj in root.DescendantsAndSelf().OfType<JObject>().ToArray())
            {
                foreach (JProperty property in obj.Properties().ToArray())
                {
                    if (property.Name.StartsWith("$", StringComparison.Ordinal))
                        throw new JsonException("CLR type metadata is not an effect setting.");
                    if (property.Name == "ContainerType")
                    {
                        if (!Containers.Contains((string)property.Value) || ++containers > 128)
                            throw new JsonException("This native effect type is not supported.");
                    }
                    if (property.Name == "OutputType" && !Outputs.Contains((string)property.Value))
                        throw new JsonException("This native output is not a lever tone.");
                    if (property.Name == "FilterType" && !Filters.Contains((string)property.Value))
                        throw new JsonException("This native response filter is not supported.");
                    if (property.Name == "TypeName" && (string)property.Value != "DeviceChannelActivationSettings")
                        throw new JsonException("This setting describes another output device.");
                    if (property.Value.Type == JTokenType.String && ((string)property.Value).Length > 8192)
                        throw new JsonException("An effect expression is too long.");
                    if (property.Value.Type != JTokenType.Float && property.Value.Type != JTokenType.Integer) continue;
                    double value = (double)property.Value;
                    if (double.IsNaN(value) || double.IsInfinity(value))
                        throw new JsonException("An effect value is not finite.");
                    double clamped = value;
                    if (property.Name == "Gain" || property.Name == "GlobalGain")
                        clamped = Math.Max(0, Math.Min(100, value));
                    if (property.Name == "Frequency" || property.Name == "HighFrequency")
                        clamped = Math.Max(Core.NativeEffectMixer.MinimumFrequency,
                            Math.Min(Core.NativeEffectMixer.MaximumFrequency, value));
                    if (property.Name == "Duration" || property.Name == "Delay")
                        clamped = Math.Max(0, Math.Min(2000, value));
                    if (clamped != value) property.Value = clamped;
                }
            }
            return JsonConvert.SerializeObject(root, Formatting.None);
        }
    }
}
