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
        private static readonly NativeWritePause NativePause = new NativeWritePause();
        private static volatile bool _nativeStartupCheck;
        private static bool _nativeStartupChecked;
        private static AB9ShifterPlugin _nativeUpdateOwner;
        private readonly NativeSettingsDebounce _nativeEdits = new NativeSettingsDebounce();
        private System.Windows.Threading.DispatcherTimer _nativeEditTimer;
        private ShifterSettings _nativeEditSettings;
        private bool _nativeUpdatesStopped;
        private bool _nativeProfileUpdate;
        private bool _nativeReplayAfterHandoff;

        public Ab9NativeSnapshot NativeSnapshot { get { return NativeDevice.Snapshot; } }
        public bool NativeBusy { get { return NativePause.Active || NativeDevice.IsBusy; } }
        public bool NativeWriteBusy { get { return NativePause.Active || (_engine != null && _engine.IsCalibrating); } }
        public string NativeOperationStatus { get; private set; }
        public bool NativeProfileUpdateInProgress { get { return _nativeProfileUpdate && NativePause.Active; } }
        public bool NativeSettingsPending { get { return _nativeEdits.Pending; } }
        public OperatingMode CurrentOperatingMode { get { return Store?.SelectedOperatingMode ?? OperatingMode.GenericFfbStick; } }
        public bool Ab9ModesAvailable { get { return NativeSnapshot.CanManage; } }
        public bool CanConfigureNative { get { return Settings != null && Ab9ModesAvailable && !NativeBusy && !_nativeStartupCheck; } }

        public bool VirtualControlsAvailable
        {
            get
            {
                return Settings != null && !NativePause.Active && !_nativeStartupCheck && !_nativeReplayAfterHandoff
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
            try
            {
                await RefreshNativeAsync();
                if (CurrentOperatingMode == OperatingMode.Ab9Native && NativeSnapshot.CanManage
                    && !_nativeUpdatesStopped && BeginNativeOperation(true))
                    await CompleteNativeProfileApplyAsync();
            }
            catch (Exception ex) { Log.Error("Could not check the AB9 mode at startup", ex); }
            finally
            {
                _nativeStartupChecked = true;
                _nativeStartupCheck = false;
                (_nativeUpdateOwner ?? this).PushSettingsToEngine();
            }
        }

        public async Task RefreshNativeAsync()
        {
            if (NativeBusy || (NativeSettingsPending && NativeSnapshot.CanManage)) return;
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
                if (ready) NativeOperationStatus = "Onboard base effects are off for polarity measurement. Your base effects will be restored after calibration.";
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
                || !CanActivateProfile(Store.FindActive()) || !BeginNativeOperation(true)) return;
            CancelPendingNativeSettings();
            await CompleteNativeProfileApplyAsync();
        }

        // Keep one pause across the batch: edits made during readback replace the pending
        // tune, and only the final checked configuration may resume the user's session.
        private async Task CompleteNativeProfileApplyAsync(Ab9NativeSettings tune = null)
        {
            bool verified = false;
            ShifterSettings active = Settings;
            try
            {
                tune = tune ?? active.ToNativeSettings();
                while (true)
                {
                    if (!ReferenceEquals(active, Settings) || _nativeUpdatesStopped)
                    {
                        verified = false;
                        break;
                    }
                    // Polarity can be revoked while an edit waits for the port. Rebuild at
                    // the point of writing so a queued snapshot cannot bypass the 10% cap.
                    tune = active.ToNativeSettings();
                    verified = await WriteBaseAsync(tune, 0);
                    if (!verified) break;
                    tune = null;
                    while (_nativeEdits.Pending && ReferenceEquals(active, Settings) && !_nativeUpdatesStopped)
                    {
                        if (_nativeEdits.TryTake(Environment.TickCount, true, out tune)) break;
                        await Task.Delay(100);
                    }
                    if (!ReferenceEquals(active, Settings) || _nativeUpdatesStopped)
                    {
                        verified = false;
                        break;
                    }
                    if (tune == null) break;
                }
            }
            catch (Exception ex)
            {
                verified = false;
                NativeOperationStatus = "AB9 configuration failed: " + ex.Message + ". Forces remain off.";
                Log.Error("AB9 configuration failed", ex);
            }
            finally
            {
                if (!verified) CancelPendingNativeSettings();
                EndNativeOperation(verified);
            }
        }

        private bool BeginNativeOperation(bool preserveEnabled = false)
        {
            lock (EngineSync)
            {
                if (NativeBusy || (_engine != null && _engine.IsCalibrating))
                {
                    NativeOperationStatus = "AB9 configuration is busy; try again when the read finishes.";
                    return false;
                }
                if (!NativePause.TryBegin(preserveEnabled, Settings.Enabled)) return false;
                _nativeReplayAfterHandoff = false;
                _nativeProfileUpdate = preserveEnabled;
            }
            // Stop performs buttons-off -> effects-off -> unacquire before any serial write.
            if (!preserveEnabled)
            {
                CancelPendingNativeSettings();
                Settings.Enabled = false;
            }
            PushSettingsToEngine();
            return true;
        }

        private void EndNativeOperation(bool verified = false)
        {
            lock (EngineSync)
            {
                // Failures/setup remain off. Successful ordinary applies keep the current
                // request, never restore an earlier true over a later off or panic action.
                if (!NativePause.CanResume(verified, Settings.Enabled)) Settings.Enabled = false;
                _nativeProfileUpdate = false;
                NativePause.End();
                if (!_nativeUpdatesStopped) PushSettingsToEngine();
                else if (_nativeUpdateOwner != null && !ReferenceEquals(_nativeUpdateOwner, this))
                {
                    // A game change can replace the plugin while readback is outstanding.
                    // Do not let the old continuation restart a newly loaded session.
                    if (_nativeUpdateOwner.Settings != null) _nativeUpdateOwner.Settings.Enabled = false;
                    _nativeUpdateOwner.PushSettingsToEngine();
                }
            }
        }

        private void ScheduleNativeSettings(string property)
        {
            if (_nativeUpdatesStopped || CurrentOperatingMode != OperatingMode.Ab9Native) return;
            switch (property)
            {
                case nameof(ShifterSettings.BaseSpringPct):
                case nameof(ShifterSettings.DamperCoeff):
                case nameof(ShifterSettings.BaseFrictionPct):
                case nameof(ShifterSettings.BaseInertiaPct):
                case nameof(ShifterSettings.NativeTorquePct):
                case nameof(ShifterSettings.NativeOverallIntensityPct):
                case nameof(ShifterSettings.NativeGameGainPct):
                    break;
                default: return;
            }
            QueueCurrentNativeSettings();
        }

        private void QueueCurrentNativeSettings()
        {
            ShifterSettings edited = Settings;
            OnUiThread(() =>
            {
                if (_nativeUpdatesStopped || CurrentOperatingMode != OperatingMode.Ab9Native || Settings == null
                    || !ReferenceEquals(edited, Settings)) return;
                _nativeEditSettings = Settings;
                _nativeEdits.Schedule(Settings.ToNativeSettings(), Environment.TickCount);
                NativeOperationStatus = "Changes saved. Updating the AB9 after you pause editing...";
                if (_nativeEditTimer == null)
                {
                    _nativeEditTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    _nativeEditTimer.Tick += DrainNativeSettings;
                }
                _nativeEditTimer.Start();
            });
        }

        private async void DrainNativeSettings(object sender, EventArgs e)
        {
            if (!_nativeEdits.Pending) { _nativeEditTimer.Stop(); return; }
            if (_nativeUpdatesStopped || CurrentOperatingMode != OperatingMode.Ab9Native
                || !ReferenceEquals(_nativeEditSettings, Settings)
                || Settings.VendorId != Ab9NativeProtocol.VendorId || Settings.ProductId != Ab9NativeProtocol.ProductId)
            {
                CancelPendingNativeSettings();
                return;
            }
            Ab9NativeSettings tune;
            if (!_nativeEdits.TryTake(Environment.TickCount, CanConfigureNative && !NativeWriteBusy, out tune)) return;
            if (!BeginNativeOperation(true))
            {
                _nativeEdits.Schedule(tune, Environment.TickCount);
                return;
            }
            await CompleteNativeProfileApplyAsync(tune);
        }

        private void CancelPendingNativeSettings()
        {
            _nativeEditTimer?.Stop();
            _nativeEdits.Cancel();
            _nativeEditSettings = null;
        }

        private void StartNativeSettingsUpdates()
        {
            if (_nativeUpdateOwner != null)
            {
                _nativeReplayAfterHandoff = _nativeUpdateOwner.NativeSettingsPending
                    || _nativeUpdateOwner.NativeProfileUpdateInProgress || _nativeStartupCheck;
                _nativeUpdateOwner.StopNativeSettingsUpdates();
            }
            _nativeUpdateOwner = this;
            _nativeUpdatesStopped = false;
        }

        private void ReplayPendingNativeSettings()
        {
            if (!_nativeReplayAfterHandoff) return;
            if (CurrentOperatingMode == OperatingMode.Ab9Native) QueueCurrentNativeSettings();
            else _nativeReplayAfterHandoff = false;
        }

        private void StopNativeSettingsUpdates()
        {
            _nativeUpdatesStopped = true;
            CancelPendingNativeSettings();
            CancelNativeResume();
        }

        private void CancelNativeResume()
        {
            // Action callbacks can arrive before their dispatcher update. Cancel the pending
            // resume now, so completion cannot beat an off/panic request to the UI queue.
            lock (EngineSync) NativePause.CancelResume();
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
            try { await CompleteNativeProfileApplyAsync(); }
            catch (Exception ex) { Log.Error("Could not apply the AB9-native profile", ex); }
        }
    }
}
