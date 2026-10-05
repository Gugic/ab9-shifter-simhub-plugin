using System;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Device;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl
    {
        private int _basePollTicks;
        private bool _refreshingMode;
        private bool _checkingBase;
        private bool? _basePresent;
        private int _probedVendor;
        private int _probedProduct;

        private void RefreshNativeHardware() { RefreshBaseHardware(true); }

        private void RefreshBaseConnection() { RefreshBaseHardware(false); }

        private async void RefreshBaseHardware(bool readNativeSettings)
        {
            if (Plugin == null || _boundSettings == null || _checkingBase || _preparingCalibration || _stopAfterCalibration
                || (AB9ShifterPlugin.Engine != null && AB9ShifterPlugin.Engine.IsCalibrating)) return;
            int vendor = _boundSettings.VendorId;
            int product = _boundSettings.ProductId;
            _checkingBase = true;
            try
            {
                bool ab9 = vendor == Ab9NativeProtocol.VendorId && product == Ab9NativeProtocol.ProductId;
                if (ab9 && readNativeSettings) await Plugin.RefreshNativeAsync();
                string knownPort = ab9 ? Plugin.NativeSnapshot.Port : null;
                bool? present = await Task.Run(() =>
                {
                    bool? attached = FfbDeviceProbe.IsPresent(vendor, product);
                    if (knownPort != null)
                    {
                        // Enumerating port names never opens the configuration port. A cached
                        // native snapshot alone cannot prove the base is still attached.
                        try
                        {
                            if (Array.Exists(SerialPort.GetPortNames(), port =>
                                string.Equals(port, knownPort, StringComparison.OrdinalIgnoreCase))) return true;
                        }
                        catch { return attached == true ? true : (bool?)null; }
                    }
                    return attached;
                });
                if (_boundSettings != null && _boundSettings.VendorId == vendor && _boundSettings.ProductId == product)
                {
                    _probedVendor = vendor;
                    _probedProduct = product;
                    _basePresent = present;
                }
            }
            catch (Exception ex) { Log.Error("Could not refresh the selected base", ex); }
            finally { _checkingBase = false; }
            RefreshNativeUi();
        }

        private void RefreshNativeUi()
        {
            if (Plugin == null || _boundSettings == null) return;
            bool calibrating = _preparingCalibration || (AB9ShifterPlugin.Engine != null && AB9ShifterPlugin.Engine.IsCalibrating);
            bool matchesProbe = _probedVendor == _boundSettings.VendorId && _probedProduct == _boundSettings.ProductId;
            bool missing = matchesProbe && _basePresent == false;
            BaseConnectionWarning.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
            BaseConnectionWarning.Text = "Base is not found. Connect and power on the selected base. Plugin forces and gear presses are inactive while it is disconnected.";
            NativeStatusText.Text = Plugin.CurrentOperatingMode == OperatingMode.GenericFfbStick
                ? !matchesProbe || !_basePresent.HasValue ? "Checking for the selected DirectInput base…"
                    : _basePresent == true ? "DirectInput base found." : "Selected DirectInput base is disconnected."
                : Plugin.NativeSnapshot.Status;
            NativeOperationText.Text = Plugin.NativeOperationStatus ?? "";
            NativeSettingsStatus.Text = NativeOperationText.Text + (Plugin.NativeSettingsPending ? " · Changes pending" : "");
            NativeRefreshButton.IsEnabled = !Plugin.NativeBusy && !calibrating;
            bool onboard = Plugin.CurrentOperatingMode == OperatingMode.Ab9Native;
            Ab9PreparePanel.Visibility = onboard ? Visibility.Visible : Visibility.Collapsed;
            PrepareBaseButton.IsEnabled = onboard && !Plugin.NativeBusy && !calibrating;
            ProfileSection.IsEnabled = Plugin.CanActivateProfile(Plugin.Store.FindActive()) && !calibrating;
            OperatingModeCombo.IsEnabled = !Plugin.NativeWriteBusy && !calibrating;
            _refreshingMode = true;
            try
            {
                foreach (ComboBoxItem item in OperatingModeCombo.Items)
                {
                    OperatingMode mode;
                    if (!Enum.TryParse(item.Tag as string, out mode)) continue;
                    item.IsEnabled = NativeProfilePolicy.CanSelect(mode);
                    if (mode == Plugin.CurrentOperatingMode) OperatingModeCombo.SelectedItem = item;
                }
            }
            finally { _refreshingMode = false; }

            NativeHardwareExpander.Visibility = onboard ? Visibility.Visible : Visibility.Collapsed;
            NativeTunePanel.IsEnabled = Plugin.Ab9ModesAvailable && (!Plugin.NativeBusy || Plugin.NativeProfileUpdateInProgress) && !calibrating;
            NativeSettingsStatus.Visibility = onboard ? Visibility.Visible : Visibility.Collapsed;
            BaseEffectsHeading.Text = onboard ? "Base-driven effects" : "DirectInput base effects";
            BaseEffectsDescription.Text = onboard
                ? "The AB9 computes spring, damper, friction and inertia internally, avoiding the USB round trip. The custom gate, software stability and telemetry effects still use DirectInput. Changes apply automatically after you pause editing; successful updates keep the master switch as set."
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
            if (Plugin == null || Plugin.CurrentOperatingMode != OperatingMode.Ab9Native) return;
            try { await Plugin.PrepareSelectedModeAsync(); }
            catch (Exception ex) { Log.Error("Could not prepare the base", ex); }
            RefreshNativeUi();
        }
    }
}
