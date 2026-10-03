using System;
using System.Threading.Tasks;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Device;

namespace AB9ActiveShifter
{
    public partial class AB9ShifterPlugin
    {
        // The worker survives SimHub game changes; each short serial transaction releases its port.
        private static readonly Ab9NativeDevice NativeDevice = new Ab9NativeDevice();
        private static volatile bool _ab9InNativeMode;
        private static volatile bool _ab9ModeUncertain;
        private static volatile bool _nativeOperation;
        private static volatile bool _nativeStartupCheck;
        private static bool _nativeStartupChecked;

        public Ab9NativeSnapshot NativeSnapshot { get { return NativeDevice.Snapshot; } }
        public bool NativeBusy { get { return _nativeOperation || NativeDevice.IsBusy; } }
        public bool NativeWriteBusy { get { return _nativeOperation || (_engine != null && _engine.IsCalibrating); } }
        public string NativeOperationStatus { get; private set; }
        public OperatingMode CurrentOperatingMode { get { return Store?.SelectedOperatingMode ?? OperatingMode.GenericFfbStick; } }
        public bool Ab9ModesAvailable { get { return NativeSnapshot.CanManage; } }
        public bool CanConfigureNative { get { return Settings != null && Ab9ModesAvailable && !NativeBusy && !_nativeStartupCheck; } }

        public bool VirtualControlsAvailable
        {
            get
            {
                return Settings != null && !_nativeOperation && !_nativeStartupCheck
                    && NativeProfilePolicy.CanRunVirtual(CurrentOperatingMode,
                        Settings.VendorId, Settings.ProductId, NativeSnapshot, _ab9InNativeMode || _ab9ModeUncertain);
            }
        }

        public bool CanActivateProfile(ShifterProfile profile)
        {
            return !NativeWriteBusy && profile?.Settings != null && Settings != null
                && (CurrentOperatingMode != OperatingMode.Ab9Native || !NativeBusy)
                && NativeProfilePolicy.CanActivate(CurrentOperatingMode);
        }

        private void DetectNativeAtStartup()
        {
            if (Settings.VendorId != Ab9NativeProtocol.VendorId || Settings.ProductId != Ab9NativeProtocol.ProductId) return;
            lock (EngineSync)
            {
                if (_nativeStartupChecked || _nativeStartupCheck) return;
                _nativeStartupCheck = true;
            }
            OnUiThread(CompleteNativeStartupCheck);
        }

        private async void CompleteNativeStartupCheck()
        {
            try { await RefreshNativeAsync(); }
            catch (Exception ex) { Log.Error("Could not check the AB9 mode at startup", ex); }
            finally
            {
                _nativeStartupChecked = true;
                _nativeStartupCheck = false;
                PushSettingsToEngine();
            }
        }

        public async Task RefreshNativeAsync()
        {
            if (NativeBusy) return;
            ObserveNativeMode(await NativeDevice.RefreshAsync());
        }

        private void ObserveNativeMode(Ab9NativeSnapshot snapshot)
        {
            // A failed read never proves that a formerly active firmware gate is now a flight stick.
            if (snapshot.InputMode.HasValue)
            {
                _ab9InNativeMode = snapshot.IsNative;
                _ab9ModeUncertain = false;
            }
            if (Settings != null && (snapshot.IsNative || (CurrentOperatingMode == OperatingMode.Ab9Native && !snapshot.CanManage))
                && Settings.VendorId == Ab9NativeProtocol.VendorId && Settings.ProductId == Ab9NativeProtocol.ProductId)
                Settings.Enabled = false;
            if (!VirtualControlsAvailable) PushSettingsToEngine();
        }

        /// <summary>Changes the rig's provider only after the selected AB9 configuration reads back.</summary>
        public async Task<bool> ChangeOperatingModeAsync(OperatingMode mode)
        {
            if (Store == null || Settings == null || !Enum.IsDefined(typeof(OperatingMode), mode)) return false;
            if (!BeginNativeOperation()) return false;
            bool succeeded = false;
            OperatingMode previous = CurrentOperatingMode;
            try
            {
                // Refresh at the point of intent. The discovery worker rechecks exact USB identity
                // and firmware again immediately before every hardware write.
                Ab9NativeSnapshot snapshot = await NativeDevice.RefreshAsync();
                ObserveNativeMode(snapshot);
                if (mode != OperatingMode.GenericFfbStick && !snapshot.CanManage)
                {
                    NativeOperationStatus = snapshot.Status;
                    return false;
                }

                if (mode == OperatingMode.Ab9Native)
                {
                    // New hardware cannot inherit polarity measured on a different stick.
                    if (Settings.VendorId != Ab9NativeProtocol.VendorId || Settings.ProductId != Ab9NativeProtocol.ProductId)
                        InvalidatePolarity();
                    if (!await WriteBaseAsync(Settings.ToNativeSettings(), 0)) return false;
                }
                else if (mode == OperatingMode.Ab9HPattern)
                {
                    // The firmware owns this mode. Preserve its current stored layout and forces;
                    // profile tuning, vJoy and DirectInput are all unavailable afterwards.
                    if (!await WriteBaseAsync(snapshot.Settings.Copy(), 1)) return false;
                }
                else
                {
                    // Generic is always selectable, including with no AB9 attached. Only an AB9
                    // still present after a managed mode needs its onboard effects neutralised.
                    if (snapshot.CanManage && previous != OperatingMode.GenericFfbStick
                        && Settings.VendorId == Ab9NativeProtocol.VendorId && Settings.ProductId == Ab9NativeProtocol.ProductId)
                        if (!await WriteBaseAsync(Ab9NativeSettings.GenericSetup(), 0)) return false;
                }

                if (mode != OperatingMode.GenericFfbStick)
                {
                    Settings.VendorId = Ab9NativeProtocol.VendorId;
                    Settings.ProductId = Ab9NativeProtocol.ProductId;
                }
                Store.SelectedOperatingMode = mode;
                succeeded = true;
            }
            catch (Exception ex)
            {
                NativeOperationStatus = "Mode change failed: " + ex.Message + ". Forces remain off.";
                Log.Error("Could not change the operating mode", ex);
            }
            finally { EndNativeOperation(); }
            if (!succeeded) return false;
            PushSettingsToEngine();
            SaveStore();
            RaiseProfileChanged();
            NativeOperationStatus = mode == OperatingMode.Ab9HPattern
                ? "AB9 H-pattern is active. The firmware owns the gate and its buttons."
                : "Mode ready. Virtual forces are off; complete setup or enable the shifter when ready.";
            return true;
        }

        /// <summary>One setup action for the selected provider; generic sticks retain their manual checklist.</summary>
        public async Task<bool> PrepareSelectedModeAsync()
        {
            if (CurrentOperatingMode != OperatingMode.GenericFfbStick)
                return await ChangeOperatingModeAsync(CurrentOperatingMode);
            if (Settings == null) return false;
            Settings.Enabled = false;
            PushSettingsToEngine();
            NativeOperationStatus = "Select the FFB device, configure its own centring off, choose vJoy, then measure polarity.";
            return true;
        }

        /// <summary>Temporarily neutralises onboard conditions so polarity probes measure DI alone.</summary>
        public async Task<bool> PrepareCalibrationAsync()
        {
            if (Settings == null || CurrentOperatingMode == OperatingMode.Ab9HPattern) return false;
            if (CurrentOperatingMode == OperatingMode.GenericFfbStick)
                return await PrepareSelectedModeAsync();
            if (!CanConfigureNative || !BeginNativeOperation()) return false;
            try
            {
                bool ready = await WriteBaseAsync(Ab9NativeSettings.GenericSetup(), 0);
                if (ready) NativeOperationStatus = "Onboard base effects are off for polarity measurement. Apply your base effects after calibration.";
                return ready;
            }
            finally { EndNativeOperation(); }
        }

        // Kept as entry points while the UI moves its old setup buttons into the mode selector.
        public async Task SetupVirtualAsync() { await ChangeOperatingModeAsync(OperatingMode.Ab9Native); }
        public async Task SetupNativeAsync() { await ChangeOperatingModeAsync(OperatingMode.Ab9HPattern); }

        public async Task ApplyNativeProfileAsync()
        {
            if (Settings == null || CurrentOperatingMode != OperatingMode.Ab9Native
                || !CanActivateProfile(Store.FindActive()) || !BeginNativeOperation()) return;
            try { await WriteBaseAsync(Settings.ToNativeSettings(), 0); }
            catch (Exception ex)
            {
                NativeOperationStatus = "AB9 configuration failed: " + ex.Message + ". Forces remain off.";
                Log.Error("AB9 configuration failed", ex);
            }
            finally { EndNativeOperation(); }
        }

        private bool BeginNativeOperation()
        {
            lock (EngineSync)
            {
                if (NativeBusy || (_engine != null && _engine.IsCalibrating))
                {
                    NativeOperationStatus = "AB9 configuration is busy; try again when the read finishes.";
                    return false;
                }
                _nativeOperation = true;
            }
            // Stop performs buttons-off -> effects-off -> unacquire before any serial write.
            Settings.Enabled = false;
            PushSettingsToEngine();
            return true;
        }

        private void EndNativeOperation()
        {
            Settings.Enabled = false;
            _nativeOperation = false;
        }

        private async Task<bool> WriteBaseAsync(Ab9NativeSettings tune, int mode)
        {
            _ab9ModeUncertain = true;
            NativeOperationStatus = "Applying and checking AB9 settings...";
            Ab9NativeSnapshot snapshot = await NativeDevice.ApplyAsync(tune, mode);
            ObserveNativeMode(snapshot);
            NativeOperationStatus = snapshot.CanManage ? "AB9 settings applied and read back." : snapshot.Status;
            return snapshot.CanManage && snapshot.InputMode == mode;
        }

        private void InvalidatePolarity()
        {
            Settings.Enabled = false;
            Settings.PolarityConfirmed = false;
            Settings.BaseSpringPolarityConfirmed = false;
            LastCalibration.Clear();
        }

        private async void ApplyNativeProfileInBackground()
        {
            try { await ApplyNativeProfileAsync(); }
            catch (Exception ex) { Log.Error("Could not apply the AB9-native profile", ex); }
        }
    }
}
