using System;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AB9ActiveShifter.Core;
using GameReaderCommon;
using SimHub.Plugins;

namespace AB9ActiveShifter
{
    /// <summary>
    /// SimHub entry point. Turns a MOZA AB9 in flight mode into an H-pattern shifter with a
    /// real 7+R gate, including the push-through lockout that the base's own shifter mode
    /// does not provide, and publishes the selected gear as vJoy buttons.
    /// </summary>
    [PluginDescription("Renders a 7+R H-pattern shift gate with a push-through lockout on a MOZA AB9 flight base, and outputs the selected gear as vJoy buttons.")]
    [PluginAuthor("simhub-ab9")]
    [PluginName("AB9 Active Shifter")]
    public class AB9ShifterPlugin : IPlugin, IDataPlugin, IWPFSettingsV2, IReusable
    {
        private const string SettingsKey = "GeneralSettings";

        /// <summary>
        /// The engine outlives the plugin object: SimHub rebuilds plugins at game change,
        /// and dropping force feedback every time the user switches game would be wrong.
        /// </summary>
        private static ShifterEngine _engine;

        private static readonly object EngineSync = new object();
        private bool _processExitHooked;

        public ShifterSettings Settings { get; private set; }

        /// <summary>Every profile plus which one is live. What actually gets persisted.</summary>
        public ProfileStore Store { get; private set; }

        /// <summary>Raised after the active profile changes, so the UI can rebind.</summary>
        public event Action ProfileChanged;

        public PluginManager PluginManager { get; set; }

        public string LeftMenuTitle { get { return "AB9 Shifter"; } }

        public ImageSource PictureIcon
        {
            get
            {
                try
                {
                    return new BitmapImage(
                        new Uri("pack://application:,,,/AB9ActiveShifter;component/Resources/menuicon.png"));
                }
                catch
                {
                    return null;
                }
            }
        }

        public static ShifterEngine Engine { get { return _engine; } }

        public void Init(PluginManager pluginManager)
        {
            Log.Info("Init (plugin instance created).");

            Store = this.ReadCommonSettings<ProfileStore>(SettingsKey, () => new ProfileStore());
            if (Store == null) Store = new ProfileStore();

            if (Store.FindActive() == null)
            {
                // Either a fresh install or a settings file from before profiles existed. A
                // legacy file deserialises into an empty store, so re-read it as the flat
                // settings it was and carry every tuned dial into the first profile.
                ShifterSettings legacy =
                    this.ReadCommonSettings<ShifterSettings>(SettingsKey, () => new ShifterSettings());

                Store.Profiles = new System.Collections.Generic.List<ShifterProfile>
                {
                    new ShifterProfile { Name = "Default", Settings = legacy ?? new ShifterSettings() }
                };
                Store.ActiveProfile = "Default";
                Log.Info("Settings migrated into profile 'Default'.");
            }

            ShifterProfile active = Store.FindActive();
            Store.ActiveProfile = active.Name;
            Settings = active.Settings;

            lock (EngineSync)
            {
                if (_engine == null)
                {
                    _engine = new ShifterEngine();
                    Log.Info("FFB engine created.");
                }
            }

            _engine.GearChanged -= OnGearChanged;
            _engine.GearChanged += OnGearChanged;

            _engine.CalibrationCompleted -= OnCalibrationCompleted;
            _engine.CalibrationCompleted += OnCalibrationCompleted;

            _engine.CalibrationFinished -= OnCalibrationFinished;
            _engine.CalibrationFinished += OnCalibrationFinished;

            Settings.PropertyChanged -= OnSettingsChanged;
            Settings.PropertyChanged += OnSettingsChanged;

            _engine.ApplyConfig(Settings.ToEngineConfig());

            AttachProperties();
            RegisterActions();

            if (!_processExitHooked)
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                _processExitHooked = true;
            }

            if (Settings.Enabled) _engine.Start();
            else Log.Info("Plugin is disabled in settings; engine not started.");
        }

        /// <summary>
        /// Intentionally empty. The FFB loop runs on its own thread because it must work
        /// with no game running. Reserved for telemetry-driven effects (grind, synchro).
        /// </summary>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
        }

        /// <summary>
        /// Called at plugin manager stop, which SimHub also does on every game change, so
        /// this only persists settings. Real teardown happens in <see cref="FinalizePlugin"/>.
        /// </summary>
        public void End(PluginManager pluginManager)
        {
            Log.Info("End (saving settings; engine left running).");
            this.SaveCommonSettings(SettingsKey, Store);
        }

        /// <summary>IReusable: the genuine shutdown, when SimHub is really done with us.</summary>
        public void FinalizePlugin()
        {
            Log.Info("FinalizePlugin (shutting the engine down).");

            // Flush any edit the debounce was still holding; a duplicate save is harmless.
            SaveStore();

            ShifterEngine engine;
            lock (EngineSync)
            {
                engine = _engine;
                _engine = null;
            }

            if (engine != null)
            {
                engine.GearChanged -= OnGearChanged;
                engine.CalibrationCompleted -= OnCalibrationCompleted;
                engine.CalibrationFinished -= OnCalibrationFinished;
                engine.Dispose();
            }

            if (_processExitHooked)
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                _processExitHooked = false;
            }
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new UI.SettingsControl(this);
        }

        /// <summary>
        /// Makes the named profile live: rebinds the change notifications, pushes its
        /// configuration to the engine, and persists which profile is active. The engine
        /// handles the swap like any config change - gears release if the new geometry says
        /// the stick is not in one, and a sequential pulse in flight is cleared.
        /// </summary>
        public void ActivateProfile(string name)
        {
            if (Store == null || Store.Profiles == null) return;

            ShifterProfile target = null;
            foreach (ShifterProfile p in Store.Profiles)
            {
                if (p != null && p.Name == name && p.Settings != null) { target = p; break; }
            }
            if (target == null || target.Settings == Settings) return;

            if (Settings != null) Settings.PropertyChanged -= OnSettingsChanged;
            Settings = target.Settings;
            Settings.PropertyChanged += OnSettingsChanged;
            Store.ActiveProfile = target.Name;

            PushSettingsToEngine();
            SaveStore();
            RaiseProfileChanged();
            Log.Info("Profile '" + target.Name + "' activated.");
        }

        /// <summary>Adds a copy of the current profile under the given name and makes it live.</summary>
        public void AddProfileFromCurrent(string requestedName)
        {
            if (Store == null) return;
            if (Store.Profiles == null) Store.Profiles = new System.Collections.Generic.List<ShifterProfile>();

            var profile = new ShifterProfile
            {
                Name = Store.UniqueName(requestedName),
                Settings = SettingsCloner.Clone(Settings)
            };

            Store.Profiles.Add(profile);
            ActivateProfile(profile.Name);
        }

        /// <summary>Deletes the active profile. The last profile cannot be deleted.</summary>
        public void DeleteActiveProfile()
        {
            if (Store == null || Store.Profiles == null || Store.Profiles.Count <= 1) return;

            ShifterProfile active = Store.FindActive();
            if (active == null) return;

            Store.Profiles.Remove(active);
            ShifterProfile next = Store.FindActive();
            if (next != null) ActivateProfile(next.Name);
        }

        /// <summary>Renames the active profile, keeping names unique.</summary>
        public void RenameActiveProfile(string newName)
        {
            if (Store == null || string.IsNullOrWhiteSpace(newName)) return;

            ShifterProfile active = Store.FindActive();
            if (active == null || active.Name == newName.Trim()) return;

            active.Name = Store.UniqueName(newName);
            Store.ActiveProfile = active.Name;
            SaveStore();
            RaiseProfileChanged();
        }

        private void SaveStore()
        {
            try { this.SaveCommonSettings(SettingsKey, Store); }
            catch (Exception ex) { Log.Error("Could not save profiles", ex); }
        }

        private void RaiseProfileChanged()
        {
            Action handler = ProfileChanged;
            if (handler == null) return;
            try { handler(); }
            catch (Exception ex) { Log.Error("Profile change handler failed", ex); }
        }

        /// <summary>Rebuilds the engine configuration from the current settings.</summary>
        public void PushSettingsToEngine()
        {
            ShifterEngine engine = _engine;
            if (engine == null || Settings == null) return;

            engine.ApplyConfig(Settings.ToEngineConfig());

            if (Settings.Enabled && !engine.IsRunning) engine.Start();
            else if (!Settings.Enabled && engine.IsRunning) engine.Stop(TimeSpan.FromSeconds(2));
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            PushSettingsToEngine();
            ScheduleSave();
        }

        private System.Windows.Threading.DispatcherTimer _saveDebounce;

        /// <summary>
        /// Persists the store shortly after the last dial change. Without this, edits only
        /// reached disk on a clean SimHub exit - and the deploy script force-kills SimHub, so
        /// an afternoon of tuning could vanish with the process. Debounced so dragging a
        /// slider writes once, not per pixel.
        /// </summary>
        private void ScheduleSave()
        {
            System.Windows.Application app = System.Windows.Application.Current;
            if (app == null)
            {
                SaveStore();
                return;
            }

            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke((Action)ScheduleSave);
                return;
            }

            if (_saveDebounce == null)
            {
                _saveDebounce = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _saveDebounce.Tick += (s, e) =>
                {
                    _saveDebounce.Stop();
                    SaveStore();
                };
            }

            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        private void OnGearChanged(int gear, int previousGear)
        {
            try
            {
                this.TriggerEvent(gear > 0 ? "GearEngaged" : "GearReleased");
            }
            catch (Exception ex)
            {
                Log.ErrorThrottled("trigger-event", "Could not raise gear event", ex);
            }
        }

        /// <summary>
        /// Records what the measurement found. Raised on the engine thread, so it is marshalled to
        /// the UI thread before touching settings that WPF is bound to.
        /// </summary>
        private void OnCalibrationCompleted(CalibrationResult result)
        {
            if (result == null) return;

            OnUiThread(() =>
            {
                // Only a definite reading changes a flag; an inconclusive probe leaves whatever
                // was there rather than guessing.
                if (result.Outcome == CalibrationOutcome.Correct || result.Outcome == CalibrationOutcome.Inverted)
                {
                    bool inverted = result.Outcome == CalibrationOutcome.Inverted;
                    switch (result.Target)
                    {
                        case CalibrationTarget.ConstantX: Settings.InvertConstantX = inverted; break;
                        case CalibrationTarget.ConstantY: Settings.InvertConstantY = inverted; break;
                        case CalibrationTarget.SpringX: Settings.InvertSpringX = inverted; break;
                        case CalibrationTarget.SpringY: Settings.InvertSpringY = inverted; break;
                    }
                }

                LastCalibration[result.Target] = result;
            });
        }

        /// <summary>
        /// Lifts the force cap only when both effect families were measured conclusively. An
        /// inconclusive run leaves the cap on, because an unmeasured direction is exactly the case
        /// the cap exists for.
        /// </summary>
        private void OnCalibrationFinished()
        {
            OnUiThread(() =>
            {
                bool conclusive = true;
                foreach (CalibrationTarget target in (CalibrationTarget[])Enum.GetValues(typeof(CalibrationTarget)))
                {
                    CalibrationResult r;
                    if (!LastCalibration.TryGetValue(target, out r) ||
                        r.Outcome == CalibrationOutcome.Inconclusive ||
                        r.Outcome == CalibrationOutcome.Pending)
                    {
                        conclusive = false;
                        break;
                    }
                }

                Settings.PolarityConfirmed = conclusive;

                Log.Info(conclusive
                    ? "Calibration complete; force cap lifted."
                    : "Calibration incomplete; force cap stays on.");

                // Persist now rather than waiting for plugin shutdown. This result describes the
                // hardware and was earned by moving the stick; losing it to a crash would mean
                // running the gate backwards on the next start.
                try { this.SaveCommonSettings(SettingsKey, Store); }
                catch (Exception ex) { Log.Error("Could not save settings after calibration", ex); }
            });
        }

        /// <summary>Most recent measurement per effect family, for the settings page to display.</summary>
        public readonly System.Collections.Generic.Dictionary<CalibrationTarget, CalibrationResult> LastCalibration =
            new System.Collections.Generic.Dictionary<CalibrationTarget, CalibrationResult>();

        private static void OnUiThread(Action action)
        {
            System.Windows.Application app = System.Windows.Application.Current;
            if (app == null || app.Dispatcher.CheckAccess())
            {
                try { action(); }
                catch (Exception ex) { Log.Error("UI-thread action failed", ex); }
                return;
            }

            app.Dispatcher.BeginInvoke(action);
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            ShifterEngine engine = _engine;
            if (engine != null) engine.EmergencyStop("SimHub is exiting");
        }

        private void AttachProperties()
        {
            this.AttachDelegate("CurrentGear", () => Snapshot().GearLabel);
            this.AttachDelegate("GearIndex", () => Snapshot().Gear);
            this.AttachDelegate("InGear", () => Snapshot().Gear > 0);
            this.AttachDelegate("GateState", () => Snapshot().State.ToString());
            this.AttachDelegate("GateColumn", () => Snapshot().Column.ToString());
            this.AttachDelegate("DeviceConnected", () => Snapshot().DeviceConnected);
            this.AttachDelegate("DeviceName", () => Snapshot().DeviceName);
            this.AttachDelegate("VJoyConnected", () => Snapshot().VJoyConnected);
            this.AttachDelegate("StickX", () => Snapshot().X);
            this.AttachDelegate("StickY", () => Snapshot().Y);
            this.AttachDelegate("LoopHz", () => (int)Math.Round(Snapshot().LoopHz));
            this.AttachDelegate("StatusMessage", () => Snapshot().StatusMessage);

            this.AddEvent("GearEngaged");
            this.AddEvent("GearReleased");
        }

        private void RegisterActions()
        {
            this.AddAction("ToggleShifterFFB", (a, b) =>
            {
                Settings.Enabled = !Settings.Enabled;
                Log.Info("Shifter FFB toggled " + (Settings.Enabled ? "on" : "off") + ".");
            });

            this.AddAction("ReleaseAllGears", (a, b) =>
            {
                ShifterEngine engine = _engine;
                if (engine != null) engine.EmergencyStop("manual release requested");
            });
        }

        private static EngineSnapshot Snapshot()
        {
            ShifterEngine engine = _engine;
            return engine != null ? engine.Snapshot : new EngineSnapshot();
        }
    }
}
