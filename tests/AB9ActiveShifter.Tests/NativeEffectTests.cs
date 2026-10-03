using System;
using AB9ActiveShifter.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class NativeEffectTests
    {
        private static NativeEffectFrame Frame(params NativeEffectTone[] tones)
        { return new NativeEffectFrame(1, 0, tones); }

        private static NativeEffectTone Tone(int id = 1, double level = 1, double frequency = 10)
        { return new NativeEffectTone { Id = id, Level = level, Frequency = frequency }; }

        [Fact]
        public void NativeCarriersKeepThePolarityCapAndTheGrindBudget()
        {
            foreach (bool grind in new[] { false, true })
            {
                var mixer = new NativeEffectMixer();
                NativeEffectTone tone = Tone();
                tone.Grind = grind;
                int peak = 0;
                for (int i = 0; i < 100; i++) peak = Math.Max(peak, Math.Abs(mixer.Step(Frame(tone), 1, 0, 1, 0.1, true)));
                Assert.Equal(grind ? 450 : 300, peak);
            }
        }

        [Fact]
        public void AStackOfNativeEffectsCannotEscapeTheVibrationBudget()
        {
            NativeEffectTone[] tones = new NativeEffectTone[NativeEffectMixer.MaxTones];
            for (int i = 0; i < tones.Length; i++) tones[i] = Tone(i + 1, 10000);
            var mixer = new NativeEffectMixer();
            int peak = 0;
            for (int i = 0; i < 100; i++) peak = Math.Max(peak, Math.Abs(mixer.Step(Frame(tones), 1, 0, 1, 1, true)));
            Assert.Equal(EffectComposer.VibTotalMax, peak);
            mixer = new NativeEffectMixer();
            peak = 0;
            for (int i = 0; i < 100; i++) peak = Math.Max(peak, Math.Abs(mixer.Step(Frame(tones), 1, 0, 1, 0.1, true)));
            Assert.Equal(EffectComposer.VibTotalMax / 10, peak);
        }

        [Fact]
        public void EitherStaleNativeDataOrStaleTelemetrySilencesTheNextTick()
        {
            var mixer = new NativeEffectMixer();
            NativeEffectFrame frame = Frame(Tone());
            Assert.NotEqual(0, mixer.Step(frame, 1, 0, 1, 1, true));
            Assert.Equal(0, mixer.Step(frame, 1, 501, 1, 1, true));
            Assert.Equal(0, mixer.Step(frame, 1, 0, 1, 1, false));
        }

        [Fact]
        public void AnOutgoingProfileCannotPlayAgainstTheIncomingGate()
        {
            var mixer = new NativeEffectMixer();
            Assert.NotEqual(0, mixer.Step(Frame(Tone()), 1, 0, 1, 1, true));
            Assert.Equal(0, mixer.Step(Frame(Tone()), 2, 0, 1, 1, true));
        }

        [Fact]
        public void OverlappingEffectsKeepTheirSeparateFrequencies()
        {
            var combined = new NativeEffectMixer();
            var low = new NativeEffectMixer();
            var high = new NativeEffectMixer();
            NativeEffectTone a = Tone(1, 0.2, 10), b = Tone(2, 0.2, 37);
            for (int i = 0; i < 100; i++)
            {
                int both = combined.Step(Frame(a, b), 1, 0, 1, 1, true);
                int separate = low.Step(Frame(a), 1, 0, 1, 1, true) + high.Step(Frame(b), 1, 0, 1, 1, true);
                Assert.InRange(both - separate, -1, 1);
            }
        }

        [Fact]
        public void NativePrioritySuppressesOrdinaryEffects()
        {
            NativeEffectTone priority = Tone(2, 0.2, 20);
            priority.Priority = true;
            Assert.Equal(new NativeEffectMixer().Step(Frame(priority), 1, 0, 1, 1, true),
                new NativeEffectMixer().Step(Frame(Tone(), priority), 1, 0, 1, 1, true));
        }

        [Fact]
        public void ATestCanPlayWithNoGameButCannotKeepOrdinaryEffectsAlive()
        {
            NativeEffectTone test = Tone(2, 0.2, 20);
            test.Test = true;
            Assert.Equal(new NativeEffectMixer().Step(Frame(test), 1, 0, 1, 0.1, false),
                new NativeEffectMixer().Step(Frame(Tone(), test), 1, 0, 1, 0.1, false));
            Assert.Equal(0, new NativeEffectMixer().Step(Frame(test), 1, 501, 1, 1, false));
        }

        [Fact]
        public void ConsumingTheNativeTestRequestDoesNotSilenceTheRunningTest()
        {
            var start = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
            Assert.True(NativeEffectMixer.IsTestActive(false, start, 2000, start.AddMilliseconds(250)));
            Assert.False(NativeEffectMixer.IsTestActive(false, start, 2000, start.AddMilliseconds(2000)));
            Assert.False(NativeEffectMixer.IsTestActive(false, null, 2000, start));
        }

        [Fact]
        public void ADelayedToneStartsItsDelayAgainAfterSilence()
        {
            var mixer = new NativeEffectMixer();
            NativeEffectTone tone = Tone();
            tone.DelayMs = 10;
            for (int i = 0; i < 9; i++) Assert.Equal(0, mixer.Step(Frame(tone), 1, 0, 1, 1, true));
            Assert.NotEqual(0, mixer.Step(Frame(tone), 1, 0, 1, 1, true));
            mixer.Step(Frame(), 1, 0, 1, 1, true);
            Assert.Equal(0, mixer.Step(Frame(tone), 1, 0, 1, 1, true));
        }

        [Fact]
        public void MalformedTonesAreSilentAndFrequencyStaysInsideTheHardwareRange()
        {
            Assert.Equal(0, new NativeEffectMixer().Step(Frame(Tone(level: double.NaN)), 1, 0, 1, 1, true));
            Assert.Equal(0, new NativeEffectMixer().Step(Frame(Tone(frequency: double.PositiveInfinity)), 1, 0, 1, 1, true));
            Assert.Equal(new NativeEffectMixer().Step(Frame(Tone(frequency: 130)), 1, 0, 1, 1, true),
                new NativeEffectMixer().Step(Frame(Tone(frequency: 100000)), 1, 0, 1, 1, true));
        }

        [Fact]
        public void NativeModeRetainsGrindRefusalWithoutPlayingTheLegacyCarrier()
        {
            var cfg = new EngineConfig
            {
                NativeEffectsEnabled = true,
                GrindEnabled = true,
                GrindRejectsGear = true,
                FxEngineEnabled = true,
                FxAbsEnabled = true,
                OverallGainPct = 100,
                PolarityConfirmed = true
            };
            var telemetry = new TelemetryState { GameRunning = true, Rpms = 3000, Clutch = 0, SpeedKmh = 60, AbsActive = true };
            EffectOutput output = new EffectComposer().Step(cfg, telemetry, 0, 1, true);
            Assert.True(output.BlockEngage);
            Assert.True(output.MuteDetent);
            Assert.True(output.GrindLevel > 0);
            Assert.Equal(0, output.VibY);
        }

        private static string ProfileJson(JObject container = null)
        {
            return new JObject
            {
                ["Version"] = 1,
                ["GlobalGain"] = 100,
                ["Profile"] = new JObject
                {
                    ["EffectsContainers"] = new JArray(container ?? new JObject
                    {
                        ["ContainerType"] = "ABSActiveEffectContainer",
                        ["Gain"] = 50,
                        ["Output"] = new JObject { ["OutputType"] = "SingleToneOutput", ["Frequency"] = 10 }
                    })
                }
            }.ToString();
        }

        [Fact]
        public void NativeProfileTransferKeepsTheWholeTreeInsteadOfTruncatingItToAPropertyName()
        {
            string native = ProfileJson();
            Assert.True(native.Length > 200);
            string exported = ProfileTransfer.Export(new ShifterProfile { Name = "Native", Settings = new ShifterSettings { NativeEffectsJson = native } });
            ShifterSettings imported = ProfileTransfer.Import(exported, new ShifterSettings()).Profile.Settings;
            Assert.True(JToken.DeepEquals(JObject.Parse(native), JObject.Parse(imported.NativeEffectsJson)));
        }

        [Fact]
        public void ASharedNativeProfileCannotConstructAnotherHardwareOutput()
        {
            JObject root = JObject.Parse(ProfileJson());
            root["Profile"]["OutputManager"] = new JObject { ["TypeName"] = "VibrationOutputManager" };
            Assert.Null(JObject.Parse(NativeEffectsData.Validate(root.ToString()))["Profile"]["OutputManager"]);
        }

        [Theory]
        [InlineData("ContainerType", "SomeThirdPartyTransport")]
        [InlineData("OutputType", "RpmBankOutput")]
        [InlineData("FilterType", "SomeThirdPartyFilter")]
        [InlineData("$type", "System.IO.FileInfo")]
        [InlineData("TypeName", "SomeHardwareSettings")]
        public void ImportedEffectTypesAreRestricted(string key, string value)
        {
            JObject container = JObject.Parse(ProfileJson())["Profile"]["EffectsContainers"][0] as JObject;
            container[key] = value;
            Assert.Throws<JsonException>(() => NativeEffectsData.Validate(ProfileJson(container)));
        }

        [Fact]
        public void AnOldSharedProfileMigratesItsOwnDialsInsteadOfInheritingLocalNativeEffects()
        {
            string json = ProfileTransfer.Export(new ShifterProfile { Name = "Old", Settings = new ShifterSettings { FxAbsEnabled = true } });
            var local = new ShifterSettings { NativeEffectsJson = ProfileJson() };
            ShifterSettings imported = ProfileTransfer.Import(json, local).Profile.Settings;
            Assert.Null(imported.NativeEffectsJson);
            Assert.True(imported.FxAbsEnabled);
        }
    }
}
