using System;
using System.Collections.Generic;
using AB9ActiveShifter.UI;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    // These are data-only host shapes. No SimHub instance, WPF window, restart or I/O is used.
    public class ControlMapperFeatureTests
    {
        private sealed class Activation
        {
            public string ClassName { get; set; }
            public bool IsEnabled { get; set; }
            public bool ShowInMainMenu { get; set; }
        }

        private sealed class HostModel
        {
            public List<object> EnabledPlugins { get; set; } = new List<object>();
            public bool IsKioskLockActive { get; set; }
        }

        private sealed class Host
        {
            public HostModel MainModel { get; set; } = new HostModel();
        }

        private sealed class ReadOnlyActivation
        {
            public string ClassName { get { return ControlMapperFeatureSettings.PluginClassName; } }
            public bool IsEnabled { get; private set; }
        }

        private static Host With(Activation activation)
        {
            var host = new Host();
            host.MainModel.EnabledPlugins.Add(activation);
            return host;
        }

        [Fact]
        public void ControlsAndEventsAndUnknownHostShapesCannotStandInForControlMapper()
        {
            Assert.Null(ControlMapperFeatureSettings.FromHost(null));
            Assert.Null(ControlMapperFeatureSettings.FromHost(new object()));
            Assert.Null(ControlMapperFeatureSettings.FromHost(new Host { MainModel = null }));
            var host = With(new Activation { ClassName = "SimHub.Plugins.OutputPlugins.KeyboardEmulatorPlugin", IsEnabled = true });
            Assert.Null(ControlMapperFeatureSettings.FromHost(host));
            host.MainModel.EnabledPlugins.Add(new Activation { ClassName = ControlMapperFeatureSettings.PluginClassName + "Other" });
            Assert.Null(ControlMapperFeatureSettings.FromHost(host));
        }

        [Fact]
        public void AnUnwritableActivationFallsBackToManualSetup()
        {
            var host = new Host();
            host.MainModel.EnabledPlugins.Add(new ReadOnlyActivation());
            Assert.Null(ControlMapperFeatureSettings.FromHost(host));
        }

        [Fact]
        public void EnablingChangesOnlyTheMapperBeforeRequestingANormalRestart()
        {
            var mapper = new Activation { ClassName = ControlMapperFeatureSettings.PluginClassName, ShowInMainMenu = false };
            var other = new Activation { ClassName = "another plugin", IsEnabled = false, ShowInMainMenu = true };
            var host = With(mapper);
            host.MainModel.EnabledPlugins.Insert(0, other);
            ControlMapperFeatureSettings feature = ControlMapperFeatureSettings.FromHost(host);
            Assert.False(feature.IsEnabled);
            int restarts = 0;
            feature.EnableAndRestart(() =>
            {
                Assert.True(mapper.IsEnabled);
                Assert.False(other.IsEnabled);
                restarts++;
            });
            Assert.True(feature.IsEnabled);
            Assert.Equal(1, restarts);
            Assert.False(mapper.ShowInMainMenu);
            Assert.True(other.ShowInMainMenu);
            Assert.Equal(new object[] { other, mapper }, host.MainModel.EnabledPlugins);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AFailedRestartRequestRestoresThePreviousFeatureSetting(bool wasEnabled)
        {
            var mapper = new Activation { ClassName = ControlMapperFeatureSettings.PluginClassName, IsEnabled = wasEnabled };
            var feature = ControlMapperFeatureSettings.FromHost(With(mapper));
            Assert.Throws<InvalidOperationException>(() => feature.EnableAndRestart(() => throw new InvalidOperationException("restart failed")));
            Assert.Equal(wasEnabled, mapper.IsEnabled);
        }

        [Fact]
        public void AnAlreadyEnabledFeatureCanRequestTheRestartNeededToLoadIt()
        {
            var mapper = new Activation { ClassName = ControlMapperFeatureSettings.PluginClassName, IsEnabled = true };
            var feature = ControlMapperFeatureSettings.FromHost(With(mapper));
            bool restarted = false;
            feature.EnableAndRestart(() => restarted = true);
            Assert.True(restarted);
            Assert.True(mapper.IsEnabled);
        }

        [Fact]
        public void KioskLockPreventsFeatureChangesAndRestart()
        {
            var mapper = new Activation { ClassName = ControlMapperFeatureSettings.PluginClassName };
            var host = With(mapper);
            host.MainModel.IsKioskLockActive = true;
            var feature = ControlMapperFeatureSettings.FromHost(host);
            Assert.False(feature.CanEnable);
            bool restarted = false;
            Assert.Throws<InvalidOperationException>(() => feature.EnableAndRestart(() => restarted = true));
            Assert.False(mapper.IsEnabled);
            Assert.False(restarted);
        }
    }
}
