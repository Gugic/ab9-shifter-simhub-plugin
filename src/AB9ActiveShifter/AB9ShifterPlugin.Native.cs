using System;
using System.Threading.Tasks;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Device;

namespace AB9ActiveShifter
{
    public partial class AB9ShifterPlugin
    {
        // Like the force engine, this survives SimHub's game-change plugin instances. The
        // serial handle itself does not: every short configuration transaction releases it.
        private static readonly Ab9NativeDevice NativeDevice = new Ab9NativeDevice();
        private static volatile bool _ab9InNativeMode;
        private static volatile bool _ab9ModeUncertain;
        private static volatile bool _nativeOperation;
        private static volatile bool _nativeStartupCheck;
        private static bool _nativeStartupChecked;
        private string _lastVirtualProfile;
        public Ab9NativeSnapshot NativeSnapshot { get { return NativeDevice.Snapshot; } }
        public bool NativeBusy { get { return _nativeOperation || NativeDevice.IsBusy; } }
        public bool NativeWriteBusy { get { return _nativeOperation; } }
        public string NativeOperationStatus { get; private set; }

        public bool VirtualControlsAvailable
        {
            get
            {
                return Settings != null && !_nativeOperation && !_nativeStartupCheck && NativeProfilePolicy.CanRunVirtual(
                    Settings.Ab9NativeProfile, Settings.VendorId, Settings.ProductId, _ab9InNativeMode || _ab9ModeUncertain);
            }
        }

        public bool CanActivateProfile(ShifterProfile profile)
        {
            return !_nativeOperation && profile != null && profile.Settings != null && Settings != null
                && (!profile.Settings.Ab9NativeProfile || !NativeBusy)
                && NativeProfilePolicy.CanActivate(profile.Settings.Ab9NativeProfile,
                    Settings.VendorId, Settings.ProductId, NativeSnapshot);
        }

        public bool CanConfigureNative
        {
            get
            {
                return Settings != null && NativeSnapshot.CanManage && !NativeBusy && !_nativeStartupCheck
                    && Settings.VendorId == Ab9NativeProtocol.VendorId
                    && Settings.ProductId == Ab9NativeProtocol.ProductId;
            }
        }

        private void DetectNativeAtStartup()
        {
            if (Settings.VendorId != Ab9NativeProtocol.VendorId || Settings.ProductId != Ab9NativeProtocol.ProductId) return;
            lock (EngineSync)
            {
                if (_nativeStartupChecked || _nativeStartupCheck) return;
                _nativeStartupCheck = true;
            }
            // Init may run off the dispatcher. Reserve the gate before queuing the read,
            // then resume on the UI thread so its result can safely notify bound settings.
            OnUiThread(CompleteNativeStartupCheck);
        }

        private async void CompleteNativeStartupCheck()
        {
            try { await RefreshNativeAsync(); }
            catch (Exception ex) { Log.Error("Could not check the AB9 mode at startup", ex); }
            finally
            {
                // A read failure leaves the existing generic setup available. A failed mode
                // write is different: _ab9ModeUncertain keeps that AB9 blocked until readback.
                _nativeStartupChecked = true;
                _nativeStartupCheck = false;
                PushSettingsToEngine();
            }
        }

        public async Task RefreshNativeAsync()
        {
            if (NativeBusy) return;
            Ab9NativeSnapshot snapshot = await NativeDevice.RefreshAsync();
            ObserveNativeMode(snapshot);
        }

        private void ObserveNativeMode(Ab9NativeSnapshot snapshot)
        {
            // Retain the last known mode on a busy port/disconnect. Losing a configuration read
            // must never restart a virtual engine against a firmware gate.
            if (snapshot.InputMode.HasValue)
            {
                _ab9InNativeMode = snapshot.IsNative;
                _ab9ModeUncertain = false;
            }
            if (snapshot.IsNative && Settings != null && Settings.VendorId == Ab9NativeProtocol.VendorId
                && Settings.ProductId == Ab9NativeProtocol.ProductId) Settings.Enabled = false;
            if (!VirtualControlsAvailable) PushSettingsToEngine();
        }

        /// <summary>One-click flight configuration. Leaves virtual forces off for calibration.</summary>
        public async Task SetupVirtualAsync()
        {
            if (!CanConfigureNative) return;
            if (!await ConfigureBaseAsync(Ab9NativeSettings.VirtualSetup(), 0)) return;
            if (NativeSnapshot.CanManage && NativeSnapshot.InputMode == 0)
            {
                if (Settings.Ab9NativeProfile)
                {
                    ShifterProfile previous = Store.Profiles.Find(p => p != null && p.Name == _lastVirtualProfile
                        && p.Settings != null && !p.Settings.Ab9NativeProfile);
                    ShifterProfile target = previous ?? Store.Profiles.Find(p => p?.Settings != null && !p.Settings.Ab9NativeProfile);
                    if (target != null) ActivateProfile(target.Name);
                }
                NativeOperationStatus = "Virtual setup verified. Select a virtual profile, measure polarity, then enable its forces.";
            }
        }

        /// <summary>Sets up the firmware gate and creates a clearly marked native profile.</summary>
        public async Task SetupNativeAsync()
        {
            if (!CanConfigureNative) return;
            if (!Settings.Ab9NativeProfile) _lastVirtualProfile = Store.ActiveProfile;
            var tune = Settings.Ab9NativeProfile ? Settings.ToNativeSettings() : new ShifterSettings().ToNativeSettings();
            if (!await ConfigureBaseAsync(tune, 1)) return;
            if (!NativeSnapshot.CanManage || !NativeSnapshot.IsNative) return;
            if (!Settings.Ab9NativeProfile)
            {
                var profile = new ShifterProfile
                {
                    Name = Store.UniqueName("AB9 native H pattern"),
                    Settings = SettingsCloner.Clone(Settings)
                };
                profile.Settings.Ab9NativeProfile = true;
                profile.Settings.ReadNativeSettings(NativeSnapshot.Settings);
                Store.Profiles.Add(profile);
                ActivateProfile(profile.Name, false);
            }
            NativeOperationStatus = "Native H pattern verified. This profile uses the AB9's own buttons; virtual forces and vJoy are off.";
        }

        public async Task ApplyNativeProfileAsync()
        {
            if (Settings == null || !Settings.Ab9NativeProfile || !CanActivateProfile(Store.FindActive())) return;
            await ConfigureBaseAsync(Settings.ToNativeSettings(), null);
        }

        private async Task<bool> ConfigureBaseAsync(Ab9NativeSettings settings, int? mode)
        {
            lock (EngineSync)
            {
                if (NativeBusy) { NativeOperationStatus = "AB9 configuration is busy; try again when the read finishes."; return false; }
                _nativeOperation = true;
            }
            _ab9ModeUncertain = true;
            NativeOperationStatus = "Applying and checking AB9 settings...";
            try
            {
                // This invokes the existing buttons-off -> forces-off -> unacquire teardown
                // before any serial gain or mode change, and prevents hotkeys from restarting it.
                Settings.Enabled = false;
                PushSettingsToEngine();
                Ab9NativeSnapshot snapshot = await NativeDevice.ApplyAsync(settings, mode);
                ObserveNativeMode(snapshot);
                NativeOperationStatus = snapshot.CanManage ? "AB9 settings applied and read back." : snapshot.Status;
                return snapshot.CanManage && (!mode.HasValue || snapshot.InputMode == mode);
            }
            catch (Exception ex)
            {
                NativeOperationStatus = "AB9 configuration failed: " + ex.Message + ". Virtual forces remain off.";
                Log.Error("AB9 configuration failed", ex);
                return false;
            }
            finally
            {
                // An action key pressed while a mode switch awaited readback cannot re-arm it.
                Settings.Enabled = false;
                _nativeOperation = false;
            }
        }

        private async void ApplyNativeProfileInBackground()
        {
            try { await ApplyNativeProfileAsync(); }
            catch (Exception ex) { Log.Error("Could not apply the native profile", ex); }
        }
    }
}
