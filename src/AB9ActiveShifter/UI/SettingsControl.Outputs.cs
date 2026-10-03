using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Output;
using SimHub.Plugins.OutputPlugins.ControlRemapper;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl
    {
        private bool _controlMapperReady;
        private bool _editingOutputRole;
        private string _controlMapperActionError;
        private readonly List<OutputRoleEntry> _outputRoleEntries = new List<OutputRoleEntry>();

        private bool UsesControlMapper
        {
            get { return _boundSettings != null && _boundSettings.OutputMode == GearOutputMode.ControlMapper; }
        }

        private bool SelectedOutputReady { get { return UsesControlMapper ? _controlMapperReady : _vjoyReady; } }

        private string SelectedOutputName
        {
            get { return UsesControlMapper ? "Control Mapper" : "vJoy " + _boundSettings.VJoyDeviceId; }
        }

        private void RefreshOutputSettings()
        {
            if (_boundSettings == null || VJoyPanel == null) return;
            VJoyPanel.Visibility = UsesControlMapper ? Visibility.Collapsed : Visibility.Visible;
            ControlMapperPanel.Visibility = UsesControlMapper ? Visibility.Visible : Visibility.Collapsed;
            OutputBindingSummary.Text = UsesControlMapper
                ? "Control Mapper roles: configure this pattern's assignments in Options → Base and output, then bind the resulting keys or controller buttons in your game. Blank mappings send nothing."
                : "vJoy buttons: gears 1–7 → 1–7, reverse → 8; sequential up/down → 9/10; P/R/N/D → 11/12/13/14. The truck pattern uses buttons 1–6.";
            if (Plugin.CurrentOperatingMode != OperatingMode.Ab9HPattern)
            {
                if (UsesControlMapper) RefreshControlMapperRoles(true);
                else RefreshVJoyDevices();
            }
            UpdateTabGate();
        }

        private void OnRefreshControlMapperRoles(object sender, RoutedEventArgs e)
        {
            _controlMapperActionError = null;
            RefreshControlMapperRoles(true);
            UpdateTabGate();
        }

        private void OnEnableControlMapper(object sender, RoutedEventArgs e)
        {
            try
            {
                if (new SimHubControlMapperRoles(Plugin.PluginManager).IsAvailable)
                {
                    OnRefreshControlMapperRoles(sender, e);
                    return;
                }
                ControlMapperFeatureSettings feature = ControlMapperFeatureSettings.FromHost(Window.GetWindow(this));
                if (feature == null)
                    throw new InvalidOperationException("This SimHub version does not expose its feature setting. Enable Control Mapper in Add/remove features, then restart SimHub.");
                feature.EnableAndRestart(() => Plugin.PluginManager.RequestApplicationExit(true));
            }
            catch (Exception ex)
            {
                _controlMapperActionError = "Could not enable Control Mapper: " + ex.GetBaseException().Message;
                RefreshControlMapperRoles(false);
                UpdateTabGate();
            }
        }

        private void OnConfigureControlMapper(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!new SimHubControlMapperRoles(Plugin.PluginManager).IsAvailable)
                    throw new InvalidOperationException("Enable Control Mapper and restart SimHub first.");
                _controlMapperActionError = null;
                Plugin.PluginManager.ShowPluginUI<ControlMapperPlugin>();
            }
            catch (Exception ex)
            {
                _controlMapperActionError = "Could not open Control Mapper: " + ex.GetBaseException().Message;
                RefreshControlMapperRoles(false);
            }
        }

        private void RefreshControlMapperRoles(bool rebuild)
        {
            if (_boundSettings == null || ControlMapperHint == null || Plugin == null
                || Plugin.CurrentOperatingMode == OperatingMode.Ab9HPattern) return;
            _controlMapperReady = false;
            var available = new List<string>();
            string problem = null;
            ControlMapperStatus.Text = "Control Mapper: checking availability…";
            EnableControlMapperButton.Visibility = Visibility.Visible;
            EnableControlMapperButton.IsEnabled = false;
            ConfigureControlMapperButton.IsEnabled = false;
            try
            {
                var mapper = new SimHubControlMapperRoles(Plugin.PluginManager);
                bool loaded = mapper.IsAvailable;
                if (loaded) available.AddRange(mapper.GetRoles());
                available.Sort(StringComparer.OrdinalIgnoreCase);
                problem = GearOutputConfig.ControlMapperProblem(loaded,
                    GearOutputConfig.RolesForPattern(_boundSettings.ControlMapperRoles, _boundSettings.Pattern), available);
                _controlMapperReady = problem == null;
                ConfigureControlMapperButton.IsEnabled = loaded;
                EnableControlMapperButton.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
                if (loaded)
                    ControlMapperStatus.Text = "Control Mapper: enabled. " + available.Count + " roles available.";
                else
                {
                    ControlMapperFeatureSettings feature = ControlMapperFeatureSettings.FromHost(Window.GetWindow(this));
                    ControlMapperStatus.Text = feature == null ? "Control Mapper: not loaded."
                        : feature.IsEnabled ? "Control Mapper: enabled in features, but not loaded. Restart SimHub to load it."
                        : "Control Mapper: disabled in SimHub's features.";
                    EnableControlMapperButton.Content = feature?.IsEnabled == true
                        ? "Restart SimHub to load Control Mapper" : "Enable Control Mapper and restart SimHub";
                    EnableControlMapperButton.IsEnabled = feature?.CanEnable == true;
                    EnableControlMapperButton.ToolTip = feature == null
                        ? "Enable Control Mapper in SimHub's Add/remove features, then restart SimHub."
                        : !feature.CanEnable ? "Unlock SimHub's kiosk mode first."
                        : "Enables SimHub's Control Mapper feature and restarts SimHub to load it. Your output and role configuration stays yours to choose.";
                }
            }
            catch (Exception ex)
            {
                ControlMapperStatus.Text = "Control Mapper: availability check failed.";
                problem = "Could not check Control Mapper: " + ex.GetBaseException().Message;
            }
            ControlMapperHint.Text = _controlMapperActionError ?? problem
                ?? "Roles are configured. Check Control Mapper's selected output and bind its keys or controller buttons in your game.";
            if (!rebuild) return;

            available.Insert(0, "");
            _outputRoleEntries.Clear();
            string[] roles = _boundSettings.ControlMapperRoles;
            for (int button = 0; button < GearOutputConfig.RoleCount; button++)
            {
                if (GearOutputConfig.UsesButton(_boundSettings.Pattern, button))
                    _outputRoleEntries.Add(new OutputRoleEntry(this, button, roles[button], available));
            }
            ControlMapperMappings.ItemsSource = null;
            ControlMapperMappings.ItemsSource = _outputRoleEntries;
        }

        private sealed class OutputRoleEntry : INotifyPropertyChanged
        {
            private readonly SettingsControl _owner;
            private readonly int _button;
            private string _role;

            public OutputRoleEntry(SettingsControl owner, int button, string role, List<string> available)
            {
                _owner = owner;
                _button = button;
                _role = role;
                Label = GearOutputConfig.Label(button);
                AvailableRoles = available;
            }

            public string Label { get; private set; }
            public List<string> AvailableRoles { get; private set; }
            public string Role
            {
                get { return _role; }
                set
                {
                    value = value ?? "";
                    if (_role == value || _owner._boundSettings == null) return;
                    _role = value;
                    string[] roles = _owner._boundSettings.ControlMapperRoles;
                    roles[_button] = value;
                    _owner._editingOutputRole = true;
                    try { _owner._boundSettings.ControlMapperRoles = roles; }
                    finally { _owner._editingOutputRole = false; }
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role)));
                }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
