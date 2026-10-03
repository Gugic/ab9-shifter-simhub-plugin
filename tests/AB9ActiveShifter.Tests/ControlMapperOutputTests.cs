using System;
using System.Collections.Generic;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Output;
using Newtonsoft.Json;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    public class ControlMapperOutputTests
    {
        private sealed class Roles : IControlMapperRoles
        {
            public readonly List<string> Calls = new List<string>();
            public readonly HashSet<string> Available = new HashSet<string>(StringComparer.Ordinal);
            public bool RefuseRelease;
            public bool ThrowAfterPress;
            public bool IsAvailable { get; set; } = true;
            public int RoleQueries;
            public ICollection<string> GetRoles() { RoleQueries++; return Available; }
            public bool StartRole(string role)
            {
                Calls.Add("press " + role);
                if (ThrowAfterPress) throw new InvalidOperationException("failed after press");
                return Available.Contains(role);
            }
            public bool StopRole(string role)
            {
                Calls.Add("release " + role);
                return !RefuseRelease;
            }
        }

        private static ControlMapperGearOutput Output(Roles mapper, bool neutral = false,
            params int[] buttons)
        {
            string[] roles = GearOutputConfig.CopyRoles(null);
            foreach (int button in buttons)
            {
                roles[button] = "role " + button;
                mapper.Available.Add(roles[button]);
            }
            var output = new ControlMapperGearOutput(mapper, roles, neutral);
            Assert.True(output.Connect());
            return output;
        }

        [Fact]
        public void ADisabledMapperIsReportedBeforeLookingForRolesAndCanConnectAfterEnabling()
        {
            var mapper = new Roles { IsAvailable = false };
            mapper.Available.Add("gear one");
            string[] roles = GearOutputConfig.CopyRoles(null);
            roles[1] = "gear one";
            var output = new ControlMapperGearOutput(mapper, roles, false);
            Assert.False(output.Connect());
            Assert.Contains("not loaded", output.LastError);
            Assert.Equal(0, mapper.RoleQueries);
            Assert.Empty(mapper.Calls);
            mapper.IsAvailable = true;
            Assert.True(output.Connect());
            output.SetGear(1);
            Assert.Equal(new[] { "press gear one" }, mapper.Calls);
        }

        [Fact]
        public void AnEnabledMapperWithoutRolesNeedsConfigurationRatherThanActivation()
        {
            var mapper = new Roles();
            var output = new ControlMapperGearOutput(mapper, null, false);
            Assert.False(output.Connect());
            Assert.Contains("enabled, but no roles", output.LastError);
            Assert.Contains("Configure Control Mapper", output.LastError);
            Assert.DoesNotContain("not loaded", output.LastError);
            Assert.Equal(1, mapper.RoleQueries);
        }

        [Fact]
        public void AMapperThatDisappearsInvalidatesTheConnectionAndStillAttemptsItsOwnCleanup()
        {
            var mapper = new Roles();
            var output = Output(mapper, false, 1);
            output.SetGear(1);
            mapper.IsAvailable = false;
            Assert.False(output.CheckConnection());
            Assert.False(output.IsConnected);
            Assert.Contains("no longer loaded", output.LastError);
            output.ReleaseAll();
            Assert.Equal(new[] { "press role 1", "release role 1" }, mapper.Calls);
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(7, 8)]
        [InlineData(11, 14)]
        public void ANewGearOrSelectorPositionReleasesTheOldRoleFirst(int first, int second)
        {
            var mapper = new Roles();
            var output = Output(mapper, false, first, second);
            output.SetGear(first);
            output.SetGear(first);
            output.SetGear(second);
            Assert.Equal(new[] { "press role " + first, "release role " + first, "press role " + second }, mapper.Calls);
        }

        [Fact]
        public void SequentialPressesKeepTheirDownUpLifetimeAndCleanupClearsBoth()
        {
            var mapper = new Roles();
            var output = Output(mapper, false, 9, 10);
            output.SetButton(9, true);
            output.SetButton(9, true);
            output.SetButton(9, false);
            output.SetButton(10, true);
            output.ReleaseAll();
            Assert.Equal(new[] { "press role 9", "release role 9", "press role 10", "release role 10" }, mapper.Calls);
        }

        [Fact]
        public void NeutralIsOptionalAndAnUnconditionalClearNeverPressesIt()
        {
            var mapper = new Roles();
            var output = Output(mapper, true, 0, 1);
            output.SetGear(0);
            output.SetGear(1);
            output.SetGear(0);
            output.ReleaseAll();
            output.ReleaseAll();
            Assert.Equal(new[] { "press role 0", "release role 0", "press role 1", "release role 1", "press role 0", "release role 0" }, mapper.Calls);
        }

        [Fact]
        public void SequentialAndPrndNeverPressTheHNeutralRole()
        {
            var mapper = new Roles();
            var output = Output(mapper, false, 0, 9, 11);
            output.SetGear(0);
            output.SetGear(11);
            output.SetGear(0);
            Assert.Equal(new[] { "press role 11", "release role 11" }, mapper.Calls);
        }

        [Fact]
        public void BlankMappingsDeliberatelySendNothing()
        {
            var mapper = new Roles();
            var output = Output(mapper, false, 1);
            output.SetGear(2);
            output.SetButton(9, true);
            output.ReleaseAll();
            Assert.Empty(mapper.Calls);
        }

        [Fact]
        public void OverlappingButtonsSharingARoleReleaseItOnlyOnceAfterTheLastButton()
        {
            var mapper = new Roles();
            mapper.Available.Add("shift");
            string[] roles = GearOutputConfig.CopyRoles(null);
            roles[9] = roles[10] = "shift";
            var output = new ControlMapperGearOutput(mapper, roles, false);
            Assert.True(output.Connect());
            output.SetButton(9, true);
            output.SetButton(10, true);
            output.SetButton(9, false);
            Assert.Equal(new[] { "press shift" }, mapper.Calls);
            output.ReleaseAll();
            Assert.Equal(new[] { "press shift", "release shift" }, mapper.Calls);
        }

        [Fact]
        public void AFailedReleaseCannotPressTheNextGearAndCleanupStillRetriesIt()
        {
            var mapper = new Roles();
            var output = Output(mapper, false, 1, 2);
            output.SetGear(1);
            mapper.RefuseRelease = true;
            output.SetGear(2);
            Assert.False(output.IsConnected);
            Assert.DoesNotContain("press role 2", mapper.Calls);
            mapper.RefuseRelease = false;
            output.ReleaseAll();
            Assert.Equal(new[] { "press role 1", "release role 1", "release role 1" }, mapper.Calls);
            Assert.True(output.Connect());
            output.SetGear(2);
            Assert.Equal("press role 2", mapper.Calls[mapper.Calls.Count - 1]);
        }

        [Fact]
        public void APressThatThrowsStillGetsAReleaseAttempt()
        {
            var mapper = new Roles();
            var output = Output(mapper, false, 1);
            mapper.ThrowAfterPress = true;
            output.SetGear(1);
            Assert.False(output.IsConnected);
            output.Disconnect();
            Assert.Equal(new[] { "press role 1", "release role 1" }, mapper.Calls);
        }

        [Fact]
        public void ReplacingAnOutputReleasesItsHeldRolesBeforeTheNewMappingPresses()
        {
            var mapper = new Roles();
            var oldOutput = Output(mapper, false, 1);
            oldOutput.SetGear(1);
            oldOutput.Disconnect();
            var replacement = Output(mapper, false, 2);
            replacement.SetGear(2);
            Assert.Equal(new[] { "press role 1", "release role 1", "press role 2" }, mapper.Calls);
        }

        [Fact]
        public void MissingRolesAreReportedAndCanConnectAfterTheyAppear()
        {
            var mapper = new Roles();
            string[] roles = GearOutputConfig.CopyRoles(null);
            roles[8] = "reverse";
            var output = new ControlMapperGearOutput(mapper, roles, false);
            Assert.False(output.Connect());
            Assert.Contains("reverse", output.LastError);
            Assert.Empty(mapper.Calls);
            mapper.Available.Add("reverse");
            Assert.True(output.Connect());
            Assert.Null(output.LastError);
            output.SetGear(8);
            Assert.Equal(new[] { "press reverse" }, mapper.Calls);
        }

        [Fact]
        public void DefaultsAndOldSettingsKeepDirectVJoy()
        {
            Assert.Equal(GearOutputMode.VJoy, new EngineConfig().OutputMode);
            ShifterSettings settings = JsonConvert.DeserializeObject<ShifterSettings>("{\"VJoyDeviceId\":3}");
            Assert.Equal(GearOutputMode.VJoy, settings.OutputMode);
            Assert.Equal(3u, settings.ToEngineConfig().VJoyDeviceId);
            settings.OutputModeIndex = 99;
            Assert.Equal(GearOutputMode.VJoy, settings.OutputMode);
        }

        [Fact]
        public void MappingsAreIndependentOfCallerArraysAndRunningSnapshots()
        {
            string[] roles = GearOutputConfig.CopyRoles(null);
            roles[1] = "one";
            var settings = new ShifterSettings { ControlMapperRoles = roles };
            roles[1] = "changed";
            EngineConfig first = settings.ToEngineConfig();
            string[] edited = settings.ControlMapperRoles;
            edited[1] = "two";
            Assert.Equal("one", settings.ControlMapperRoles[1]);
            settings.ControlMapperRoles = edited;
            Assert.Equal("one", first.ControlMapperRoles[1]);
            Assert.Equal("two", settings.ToEngineConfig().ControlMapperRoles[1]);
        }

        [Fact]
        public void OutputChoiceAndMappingsBelongToTheRigAndNeverTravelInAProfile()
        {
            var rig = new ShifterSettings { OutputMode = GearOutputMode.ControlMapper };
            string[] roles = rig.ControlMapperRoles;
            roles[1] = "my gear";
            rig.ControlMapperRoles = roles;
            var preset = DefaultProfiles.BuildPreset(DefaultProfiles.Preset(DefaultProfiles.SevenRName));
            ProfileTransfer.CopyMachineFacts(rig, preset.Settings);
            Assert.Equal(GearOutputMode.ControlMapper, preset.Settings.OutputMode);
            Assert.Equal("my gear", preset.Settings.ControlMapperRoles[1]);
            string file = ProfileTransfer.Export(preset);
            Assert.DoesNotContain("ControlMapperRoles", file);
            Assert.DoesNotContain("OutputMode", file);
            Assert.False(ProfileTransfer.IsTuning(nameof(ShifterSettings.OutputMode)));
            Assert.False(ProfileTransfer.IsTuning(nameof(ShifterSettings.ControlMapperRoles)));
        }

        [Theory]
        [InlineData(GatePattern.Sequential, 9, 0)]
        [InlineData(GatePattern.Prnd, 13, 9)]
        [InlineData(GatePattern.H6R, 8, 7)]
        [InlineData(GatePattern.H5R, 5, 6)]
        [InlineData(GatePattern.H6, 6, 8)]
        public void OnlyMappingsUsedByTheCurrentPatternAffectReadiness(GatePattern pattern, int used, int unused)
        {
            string[] roles = GearOutputConfig.CopyRoles(null);
            roles[used] = "exists";
            roles[unused] = "missing";
            string[] active = GearOutputConfig.RolesForPattern(roles, pattern);
            Assert.Null(GearOutputConfig.MappingProblem(active, new[] { "exists" }));
            Assert.Equal("missing", roles[unused]);
        }

        [Fact]
        public void OnlySelectedBackendSettingsRequireAnOutputChange()
        {
            var before = new EngineConfig();
            var after = new EngineConfig();
            after.ControlMapperRoles[1] = "one";
            Assert.False(GearOutputConfig.OutputChanged(before, after));
            after.OutputMode = GearOutputMode.ControlMapper;
            Assert.True(GearOutputConfig.OutputChanged(before, after));
            before.OutputMode = GearOutputMode.ControlMapper;
            Assert.True(GearOutputConfig.OutputChanged(before, after));
            before.ControlMapperRoles[1] = "one";
            after.VJoyDeviceId = 4;
            Assert.False(GearOutputConfig.OutputChanged(before, after));
            after.Pattern = GatePattern.Sequential;
            Assert.True(GearOutputConfig.OutputChanged(before, after));
        }

        [Theory]
        [InlineData(GatePattern.H7R, 1, 13)]
        [InlineData(GatePattern.Sequential, 9, 1)]
        [InlineData(GatePattern.Prnd, 13, 9)]
        public void ConfiguringAnotherPatternPreservesTheCurrentOutput(GatePattern pattern, int active, int other)
        {
            var before = new EngineConfig { OutputMode = GearOutputMode.ControlMapper, Pattern = pattern };
            before.ControlMapperRoles[active] = "held";
            var after = new EngineConfig
            {
                OutputMode = GearOutputMode.ControlMapper,
                Pattern = pattern,
                ControlMapperRoles = GearOutputConfig.CopyRoles(before.ControlMapperRoles)
            };
            after.ControlMapperRoles[other] = "configured for later";
            Assert.False(GearOutputConfig.OutputChanged(before, after));
            Assert.Equal("configured for later", after.ControlMapperRoles[other]);

            var mapper = new Roles();
            mapper.Available.Add("held");
            var connection = new GearOutputConnection(cfg => new ControlMapperGearOutput(mapper,
                GearOutputConfig.RolesForPattern(cfg.ControlMapperRoles, cfg.Pattern), false));
            connection.Configure(before);
            connection.Poll(0, true, active);
            var output = connection.Output;
            Assert.False(connection.Configure(after));
            Assert.Same(output, connection.Output);
            Assert.Equal(new[] { "press held" }, mapper.Calls);

            after.ControlMapperRoles[active] = "replacement";
            Assert.True(GearOutputConfig.OutputChanged(before, after));
        }
    }
}
