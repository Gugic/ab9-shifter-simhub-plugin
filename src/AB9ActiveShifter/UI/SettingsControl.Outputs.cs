using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Output;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl
    {
        private bool _controlMapperReady;
        private bool _editingOutputRole;
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
            RefreshControlMapperRoles(true);
            UpdateTabGate();
        }

        private void RefreshControlMapperRoles(bool rebuild)
        {
            if (_boundSettings == null || ControlMapperHint == null || Plugin == null
                || Plugin.CurrentOperatingMode == OperatingMode.Ab9HPattern) return;
            _controlMapperReady = false;
            var available = new List<string>();
            string problem = null;
            try
            {
                available.AddRange(new SimHubControlMapperRoles(Plugin.PluginManager).GetRoles());
                available.Sort(StringComparer.OrdinalIgnoreCase);
                problem = GearOutputConfig.MappingProblem(
                    GearOutputConfig.RolesForPattern(_boundSettings.ControlMapperRoles, _boundSettings.Pattern), available);
                _controlMapperReady = problem == null;
                if (available.Count == 0)
                    problem = "Enable Control Mapper in SimHub's Add/remove features, configure its output and roles, then press Refresh roles.";
            }
            catch (Exception ex)
            {
                problem = "Could not read Control Mapper roles: " + ex.Message;
            }
            ControlMapperHint.Text = problem ?? "Roles are configured. Check Control Mapper's selected output and bind its keys or controller buttons in your game.";
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
