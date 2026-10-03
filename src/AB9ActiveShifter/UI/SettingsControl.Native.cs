using System;
using System.Windows;
using System.Windows.Controls;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl
    {
        private int _nativePollTicks;

        private async void RefreshNativeHardware()
        {
            if (Plugin == null) return;
            try { await Plugin.RefreshNativeAsync(); }
            catch (Exception ex) { Log.Error("Could not refresh AB9 native controls", ex); }
            RefreshNativeUi();
        }

        private void RefreshNativeUi()
        {
            if (Plugin == null || _boundSettings == null) return;
            var snapshot = Plugin.NativeSnapshot;
            NativeStatusText.Text = snapshot.Status;
            NativeOperationText.Text = Plugin.NativeOperationStatus ?? "";
            ProfileSection.IsEnabled = !Plugin.NativeWriteBusy;
            NativeRefreshButton.IsEnabled = !Plugin.NativeBusy;
            NativeControlPanel.Visibility = snapshot.CanManage ? Visibility.Visible : Visibility.Collapsed;
            NativeControlPanel.IsEnabled = Plugin.CanConfigureNative;
            NativeTunePanel.Visibility = _boundSettings.Ab9NativeProfile && snapshot.CanManage && snapshot.IsNative
                ? Visibility.Visible : Visibility.Collapsed;
            NativeTunePanel.IsEnabled = Plugin.CanConfigureNative;
            ProfileKindText.Text = _boundSettings.Ab9NativeProfile
                ? "AB9 native profile" + (Plugin.CanActivateProfile(Plugin.Store.FindActive())
                    ? " - firmware gate and onboard settings." : " - unavailable: connect a compatible AB9 and enable native mode.")
                : "Generic virtual profile - software gate for a DirectInput FFB stick.";
            foreach (ComboBoxItem item in ProfileCombo.Items)
            {
                string name = item.Tag as string;
                var profile = Plugin.Store.Profiles.Find(p => p != null && p.Name == name);
                item.IsEnabled = Plugin.CanActivateProfile(profile);
            }
            UpdateTabGate();
        }

        private void OnNativeRefresh(object sender, RoutedEventArgs e) { RefreshNativeHardware(); }

        private async void OnNativeSetup(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;
            await Plugin.SetupNativeAsync();
            RefreshNativeUi();
        }

        private async void OnVirtualSetup(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;
            await Plugin.SetupVirtualAsync();
            RefreshNativeUi();
        }

        private async void OnNativeApply(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;
            await Plugin.ApplyNativeProfileAsync();
            RefreshNativeUi();
        }
    }
}
