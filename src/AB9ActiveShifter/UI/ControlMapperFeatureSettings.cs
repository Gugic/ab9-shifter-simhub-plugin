using System;
using System.Collections;
using System.Reflection;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// The SDK exposes feature lookup and navigation, but no activation method. SimHub's
    /// public host model owns the Add/remove features setting and persists it on normal exit.
    /// Reflect only that public shape to avoid a reference to the host executable. An unknown
    /// host shape leaves manual activation available; never invoke private EnablePlugin or
    /// write PluginsActivation.json behind a running SimHub.
    /// </summary>
    public sealed class ControlMapperFeatureSettings
    {
        public const string PluginClassName = "SimHub.Plugins.OutputPlugins.ControlRemapper.ControlMapperPlugin";
        private readonly object _activation;
        private readonly PropertyInfo _enabled;
        private readonly object _model;
        private readonly PropertyInfo _kioskLock;

        private ControlMapperFeatureSettings(object activation, PropertyInfo enabled, object model, PropertyInfo kioskLock)
        {
            _activation = activation;
            _enabled = enabled;
            _model = model;
            _kioskLock = kioskLock;
        }

        public bool IsEnabled { get { return (bool)_enabled.GetValue(_activation); } }
        public bool CanEnable { get { return !(bool)_kioskLock.GetValue(_model); } }

        public static ControlMapperFeatureSettings FromHost(object host)
        {
            object model = Read(host, "MainModel");
            var activations = Read(model, "EnabledPlugins") as IEnumerable;
            PropertyInfo kioskLock = PublicProperty(model, "IsKioskLockActive");
            if (activations == null || kioskLock?.PropertyType != typeof(bool)) return null;
            foreach (object activation in activations)
            {
                if (!string.Equals(Read(activation, "ClassName") as string, PluginClassName, StringComparison.Ordinal)) continue;
                PropertyInfo enabled = PublicProperty(activation, "IsEnabled");
                if (enabled?.PropertyType != typeof(bool) || enabled.GetSetMethod() == null) return null;
                return new ControlMapperFeatureSettings(activation, enabled, model, kioskLock);
            }
            return null;
        }

        public void EnableAndRestart(Action restart)
        {
            if (restart == null) throw new ArgumentNullException(nameof(restart));
            if (!CanEnable) throw new InvalidOperationException("Unlock SimHub's kiosk mode before enabling Control Mapper.");
            bool previous = IsEnabled;
            try
            {
                _enabled.SetValue(_activation, true);
                if (!IsEnabled) throw new InvalidOperationException("SimHub did not enable the Control Mapper feature.");
                restart();
            }
            catch
            {
                _enabled.SetValue(_activation, previous);
                throw;
            }
        }

        private static PropertyInfo PublicProperty(object owner, string name)
        {
            PropertyInfo property = owner?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetGetMethod() != null && property.GetIndexParameters().Length == 0 ? property : null;
        }

        private static object Read(object owner, string name)
        {
            return PublicProperty(owner, name)?.GetValue(owner);
        }
    }
}
