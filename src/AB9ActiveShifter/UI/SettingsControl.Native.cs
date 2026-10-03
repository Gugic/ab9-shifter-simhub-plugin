using System;
using System.Windows;
using System.Windows.Controls;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl
    {
        private int _nativePollTicks;
        private bool _refreshingMode;

        private async void RefreshNativeHardware()
        {
            if (Plugin == null || _preparingCalibration || _stopAfterCalibration
                || (AB9ShifterPlugin.Engine != null && AB9ShifterPlugin.Engine.IsCalibrating)) return;
            try { await Plugin.RefreshNativeAsync(); }
            catch (Exception ex) { Log.Error("Could not refresh AB9 native controls", ex); }
            RefreshNativeUi();
        }

        private void RefreshNativeUi()
        {
            if (Plugin == null || _boundSettings == null) return;
            bool calibrating = _preparingCalibration || (AB9ShifterPlugin.Engine != null && AB9ShifterPlugin.Engine.IsCalibrating);
            NativeStatusText.Text = Plugin.NativeSnapshot.Status;
            NativeOperationText.Text = Plugin.NativeOperationStatus ?? "";
            NativeSettingsStatus.Text = NativeOperationText.Text + (Plugin.NativeSettingsPending ? " · Changes pending" : "");
            NativeRefreshButton.IsEnabled = !Plugin.NativeBusy && !calibrating;
            PrepareBaseButton.IsEnabled = !Plugin.NativeBusy && !calibrating;
            ProfileSection.IsEnabled = Plugin.CanActivateProfile(Plugin.Store.FindActive()) && !calibrating;
            OperatingModeCombo.IsEnabled = !Plugin.NativeBusy && !calibrating;
            _refreshingMode = true;
            try
            {
                foreach (ComboBoxItem item in OperatingModeCombo.Items)
                {
                    OperatingMode mode;
                    if (!Enum.TryParse(item.Tag as string, out mode)) continue;
                    item.IsEnabled = mode == OperatingMode.GenericFfbStick || Plugin.Ab9ModesAvailable;
                    if (mode == Plugin.CurrentOperatingMode) OperatingModeCombo.SelectedItem = item;
                }
            }
            finally { _refreshingMode = false; }

            bool onboard = Plugin.CurrentOperatingMode == OperatingMode.Ab9Native;
            NativeHardwareExpander.Visibility = onboard ? Visibility.Visible : Visibility.Collapsed;
            NativeTunePanel.IsEnabled = Plugin.Ab9ModesAvailable && (!Plugin.NativeBusy || Plugin.NativeProfileUpdateInProgress) && !calibrating;
            NativeSettingsStatus.Visibility = onboard ? Visibility.Visible : Visibility.Collapsed;
            BaseEffectsHeading.Text = onboard ? "Base-driven effects" : "DirectInput base effects";
            BaseEffectsDescription.Text = onboard
                ? "The AB9 computes spring, damper, friction and inertia internally, avoiding the USB round trip. The custom gate, software stability and telemetry effects still use DirectInput. Changes apply automatically after you pause editing; successful updates keep your force feedback toggle as set."
                : "DirectInput renders spring, damper, friction and inertia. This profile keeps the same values when you change between virtual modes.";
            BaseSpringSlider.IsEnabled = onboard || _boundSettings.BaseSpringPolarityConfirmed;
            BaseSpringCalibrationHint.Visibility = !onboard && !_boundSettings.BaseSpringPolarityConfirmed
                ? Visibility.Visible : Visibility.Collapsed;
            foreach (ComboBoxItem item in ProfileCombo.Items)
            {
                string name = item.Tag as string;
                var profile = Plugin.Store.Profiles.Find(p => p != null && p.Name == name);
                item.IsEnabled = Plugin.CanActivateProfile(profile);
            }
            RefreshWorkspace();
            RefreshUpdates();
        }

        private void OnNativeRefresh(object sender, RoutedEventArgs e) { RefreshNativeHardware(); }

        private async void OnOperatingModeSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingMode || Plugin == null) return;
            var item = OperatingModeCombo.SelectedItem as ComboBoxItem;
            OperatingMode mode;
            if (item == null || !Enum.TryParse(item.Tag as string, out mode) || mode == Plugin.CurrentOperatingMode) return;
            try { await Plugin.ChangeOperatingModeAsync(mode); }
            catch (Exception ex) { Log.Error("Could not change operating mode", ex); }
            RefreshNativeUi();
        }

        private async void OnPrepareSelectedMode(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;
            try { await Plugin.PrepareSelectedModeAsync(); }
            catch (Exception ex) { Log.Error("Could not prepare the base", ex); }
            RefreshNativeUi();
        }
    }
}
