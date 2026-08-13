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
    [PluginDescription("Renders an H-pattern, sequential or PRND shift gate in force feedback on an AB9 flight base, plays telemetry effects through the lever, and outputs the selected gear as vJoy buttons. Unofficial third-party plugin, not affiliated with MOZA. Drives a 12 Nm device - see the Setup tab.")]
    [PluginAuthor("Gugic")]
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

        /// <summary>Last car-model telemetry value seen, so auto-switch acts on a change, not every tick.</summary>
        private volatile string _lastCarModel;

        /// <summary>
        /// Whether the settings page is open. Only <see cref="DataUpdate"/> reads it, to decide
        /// whether to look up the current car for a user who has not listed any yet - see the
        /// gate there. Set from the UI thread, read from the data thread, one writer each way,
        /// and a tick either side of the truth costs nothing.
        /// </summary>
        private volatile bool _watchingCarModel;

        /// <summary>Told by the settings page when it appears and disappears.</summary>
        public void WatchCarModel(bool watching)
        {
            _watchingCarModel = watching;
        }

        /// <summary>
        /// The most recent car-model value seen, for the Setup tab's "add last used vehicle"
        /// convenience button. Written from <see cref="DataUpdate"/> (SimHub's data thread), read
        /// from the UI thread - a single writer and a plain reference read/write needs no lock,
        /// and a stale read for a moment is harmless for a display value.
        /// </summary>
        public string LastCarModel { get { return _lastCarModel; } }

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

            // The factory runs only when there is nothing saved, which is the signal that this is
            // a first start: take the shipped profiles and write them out below, so from the next
            // start they are ordinary saved settings with no special case anywhere.
            //
            // Nothing is riding on that being true, deliberately. The write below saves whatever
            // Store ends up holding after the file has been read, so if SimHub ever called this
            // factory on a start that did have settings, the worst it could do is rewrite the
            // settings that were just loaded. It can never put the shipped tuning over a user's.
            bool firstStart = false;
            Store = this.ReadCommonSettings<ProfileStore>(SettingsKey, () =>
            {
                firstStart = true;
                return DefaultProfiles.Create();
            });
            if (Store == null) Store = new ProfileStore();

            if (Store.FindActive() == null)
            {
                // A settings file from before profiles existed: it deserialises into an empty
                // store, so re-read it as the flat settings it was and carry every tuned dial
                // into the first profile. A fresh install never lands here - the factory above
                // already gave it profiles.
                ShifterSettings legacy =
                    this.ReadCommonSettings<ShifterSettings>(SettingsKey, () => new ShifterSettings());

                Store.Profiles = new System.Collections.Generic.List<ShifterProfile>
                {
                    new ShifterProfile { Name = "Default", Settings = legacy ?? new ShifterSettings() }
                };
                Store.ActiveProfile = "Default";
                Log.Info("Settings migrated into profile 'Default'.");
            }

            // The shipped presets, rebuilt on every start rather than only on the first. They are
            // the fixed starting points a tune is measured against, so they have to be present and
            // current whatever the file says - a user who never sees a first start still gets them,
            // and a retune of one that shipped earlier reaches an install that already exists.
            //
            // This cannot disturb anything tuned here: presets carry a reserved prefix no local
            // profile is allowed to hold, so there is no name for the two to collide on and nothing
            // to migrate. Everything already in the file stays exactly as it is.
            Store.EnsurePresets(DefaultProfiles.Presets());

            ShifterProfile active = Store.FindActive();
            Store.ActiveProfile = active.Name;
            Settings = active.Settings;

            // The live switches belong to the session, not to a profile. A settings file written
            // before that was true has no value here, so adopt whatever the profile that happens
            // to be active says - which is exactly what used to decide it - and from then on the
            // store is the authority and activating a profile can never change it.
            if (!Store.SessionEnabled.HasValue) Store.SessionEnabled = Settings.Enabled;
            if (!Store.SessionFreeStick.HasValue) Store.SessionFreeStick = Settings.FreeStick;

            Settings.ApplyLiveSwitches(Store.SessionEnabled, Store.SessionFreeStick);

            if (firstStart)
            {
                // Write them out now rather than waiting for shutdown, so the file exists from
                // the first run and a crash before then cannot cost the user their starting
                // point. Forces are off and the polarity cap is on, as the defaults say.
                Log.Info("No saved settings; installed the shipped profiles and made '" +
                         Store.ActiveProfile + "' active.");
                SaveStore();
            }

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

            _engine.PedalCaptureCompleted -= OnPedalCaptureCompleted;
            _engine.PedalCaptureCompleted += OnPedalCaptureCompleted;

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
        /// Feeds the telemetry effects. The FFB loop itself deliberately does not run off this
        /// - it must work with no game running - so this only publishes a snapshot the engine
        /// reads at its own pace. On SimHub's critical path: no locks, one small allocation.
        /// </summary>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            ShifterEngine engine = _engine;
            if (engine == null) return;

            if (data == null || !data.GameRunning || data.NewData == null)
            {
                engine.SetTelemetry(TelemetryState.Inactive);
                return;
            }

            StatusDataBase d = data.NewData;

            // Gated, because the read below is a property-system lookup and a feature nobody has
            // configured must not cost anything on SimHub's critical path - the custom-property
            // effect further down is gated on its own enable flag for the same reason. Two ways
            // through: some profile actually lists a car, or the settings page is open and its
            // "add last used vehicle" button needs something to offer. The second matters because
            // that button is how the first list gets populated, so gating on the list alone would
            // leave a user who has none with no way to learn what their game calls the car.
            //
            // Past the gate this is a string compare, and the match and switch only run on a
            // genuine change, marshalled off this thread. No locks, no file I/O, no touching the
            // settings UI from here.
            ProfileStore store = Store;
            try
            {
                if (store != null && (store.AnyCarModels || _watchingCarModel))
                {
                    object rawCar = pluginManager.GetPropertyValue("DataCorePlugin.GameData.CarModel");
                    string carModel = rawCar == null ? null : Convert.ToString(rawCar, System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(carModel) && carModel != _lastCarModel)
                    {
                        _lastCarModel = carModel;
                        if (store.AnyCarModels) OnUiThread(() => TryAutoSwitchProfile(carModel));
                    }
                }
            }
            catch
            {
                // No car-model property on this game, or it is not a string. Auto-switch simply
                // never fires - not a reason to disturb telemetry processing.
            }

            // The custom effect's source property is sampled here, where the property system
            // lives; the engine only ever sees the value.
            double custom = 0;
            ShifterSettings settings = Settings;
            if (settings != null && settings.FxCustomEnabled && !string.IsNullOrWhiteSpace(settings.FxCustomProperty))
            {
                try
                {
                    object raw = pluginManager.GetPropertyValue(settings.FxCustomProperty.Trim());
                    if (raw != null)
                    {
                        custom = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    custom = 0;
                }
            }

            engine.SetTelemetry(new TelemetryState
            {
                GameRunning = true,
                Rpms = d.Rpms,
                MaxRpm = d.MaxRpm,
                SpeedKmh = d.SpeedKmh,
                Clutch = d.Clutch,
                Gear = d.Gear,
                AbsActive = d.ABSActive != 0,
                TcActive = d.TCActive != 0,
                HeaveG = d.AccelerationHeave ?? 0.0,
                CustomValue = custom,
                CapturedAtTick = Environment.TickCount
            });
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
                engine.PedalCaptureCompleted -= OnPedalCaptureCompleted;
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

            // The session's switches, not the outgoing profile's: an activation must never be the
            // thing that decides whether the shifter is running. Applied before the swap so the
            // engine sees one coherent config rather than the new gate with the old switch.
            target.Settings.ApplyLiveSwitches(Store.SessionEnabled, Store.SessionFreeStick);

            if (Settings != null) Settings.PropertyChanged -= OnSettingsChanged;

            Settings = target.Settings;
            Settings.PropertyChanged += OnSettingsChanged;
            Store.ActiveProfile = target.Name;

            PushSettingsToEngine();
            SaveStore();
            RaiseProfileChanged();
            Log.Info("Profile '" + target.Name + "' activated.");
        }

        /// <summary>
        /// Moves one step around the profile cycle. Called from a bound hotkey, so it can arrive
        /// at any moment - mid-corner, mid-shift, with a gear held - and everything that makes
        /// that safe is already in the config swap: rebuilding the gate releases the held gear
        /// button and clears any sequential pulse in flight before the new forces are applied.
        /// </summary>
        public void CycleProfile(int direction)
        {
            try
            {
                if (Store == null) return;

                string next = Store.NextInCycle(Store.ActiveProfile, direction);
                if (string.IsNullOrEmpty(next)) return;

                ActivateProfile(next);
            }
            catch (Exception ex)
            {
                // An action runs on SimHub's thread; letting this escape would take a keypress
                // handler down with it rather than just failing to switch.
                Log.ErrorThrottled("cycle-profile", "Could not switch profile", ex);
            }
        }

        /// <summary>
        /// A convenience switch, not a safety one: activates whichever profile lists this car,
        /// if any, and never touches the active profile otherwise. Runs only on a car-model
        /// change (see <see cref="DataUpdate"/>), so a profile picked by hand stands until the
        /// car itself changes again - it never fights a manual switch mid-session.
        /// </summary>
        private void TryAutoSwitchProfile(string carModel)
        {
            if (Store == null) return;

            ShifterProfile match = Store.FindByCarModel(carModel);
            if (match != null && match.Name != Store.ActiveProfile)
            {
                ActivateProfile(match.Name);
            }
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

        /// <summary>
        /// Adds an imported profile and makes it live. Its name is always made unique, so an
        /// import can only ever add - a shared file never lands on top of a tune you already
        /// have, however it happens to be named.
        /// </summary>
        public string AddImportedProfile(ShifterProfile imported)
        {
            if (Store == null || imported == null || imported.Settings == null) return null;
            if (Store.Profiles == null) Store.Profiles = new System.Collections.Generic.List<ShifterProfile>();

            imported.Name = Store.UniqueName(imported.Name);
            Store.Profiles.Add(imported);
            ActivateProfile(imported.Name);

            Log.Info("Imported profile '" + imported.Name + "'.");
            return imported.Name;
        }

        /// <summary>
        /// Deletes the active profile. The last profile cannot be deleted, and neither can a
        /// preset - it would be back at the next start anyway, so refusing is the honest answer
        /// rather than letting a row vanish and quietly return.
        /// </summary>
        public void DeleteActiveProfile()
        {
            if (Store == null || Store.Profiles == null || Store.Profiles.Count <= 1) return;
            if (DefaultProfiles.IsPreset(Store.ActiveProfile)) return;

            ShifterProfile active = Store.FindActive();
            if (active == null) return;

            Store.Profiles.Remove(active);
            ShifterProfile next = Store.FindActive();
            if (next != null) ActivateProfile(next.Name);
        }

        /// <summary>
        /// Renames the active profile, keeping names unique. A preset cannot be renamed: its name
        /// is its identity, and <see cref="ProfileStore.EnsurePresets"/> would put it back under
        /// the shipped one at the next start. Tune it instead - the first change forks it into a
        /// local profile, and that one renames freely.
        /// </summary>
        public void RenameActiveProfile(string newName)
        {
            if (Store == null || string.IsNullOrWhiteSpace(newName)) return;
            if (DefaultProfiles.IsPreset(Store.ActiveProfile)) return;

            ShifterProfile active = Store.FindActive();
            if (active == null || active.Name == newName.Trim()) return;

            active.Name = Store.UniqueName(newName);
            Store.ActiveProfile = active.Name;
            SaveStore();
            RaiseProfileChanged();
        }

        /// <summary>
        /// Writes the profile store out. Internal rather than private because the settings page
        /// edits the store directly for things that are not per-profile settings - which profiles
        /// a hotkey cycles through being the only one so far.
        /// </summary>
        internal void SaveStore()
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

            EngineConfig cfg = Settings.ToEngineConfig();

            // How many times the lever thumps after a switch, so the profile can be counted by
            // hand. The count is the profile's own place in the store, which is a fact only the
            // plugin knows - ShifterSettings has no idea it lives in a list.
            cfg.ProfileConfirmPulses = Store != null && Store.ConfirmProfileSwitch
                ? Store.IndexOfActive() + 1
                : 0;

            engine.ApplyConfig(cfg);

            if (Settings.Enabled && !engine.IsRunning) engine.Start();
            else if (!Settings.Enabled && engine.IsRunning) engine.Stop(TimeSpan.FromSeconds(2));
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            // Ticking Enabled or FreeStick is a decision about the session, so it is recorded
            // where the session lives. Without this the store would keep whatever was migrated at
            // startup and the switch would spring back on the next profile activation.
            if (Store != null && Settings != null)
            {
                if (e == null || e.PropertyName == null || e.PropertyName == "Enabled")
                {
                    Store.SessionEnabled = Settings.Enabled;
                }
                if (e == null || e.PropertyName == null || e.PropertyName == "FreeStick")
                {
                    Store.SessionFreeStick = Settings.FreeStick;
                }
            }

            ForkActivePresetIfTuned(e == null ? null : e.PropertyName);

            PushSettingsToEngine();
            ScheduleSave();
        }

        /// <summary>
        /// A preset is a fixed starting point, so the first tuning change made while one is active
        /// silently moves the edit onto a local profile and puts the preset back untouched. The
        /// user is left editing what they were editing, under a name that is now theirs.
        /// <para>
        /// It fires from the change notification rather than from anything in the UI because that
        /// is the one funnel every dial passes through: sliders, checkboxes, the pattern dropdown,
        /// a bound SimHub action, and the derived dials that write two properties at once. A guard
        /// on the settings page would miss all but the first.
        /// </para>
        /// <para>
        /// Two things deliberately do not fork. A notification with no property name is a bulk
        /// re-read - migration, or the session switches being applied on activation - and not an
        /// edit at all. And anything <see cref="ProfileTransfer.IsTuning"/> rejects is a fact about
        /// this machine rather than a tune: polarity calibration writes its measured result through
        /// exactly this path, and a preset that forked itself the moment calibration finished would
        /// be a copy nobody asked for, holding the one flag that lifts the 10% force cap.
        /// </para>
        /// </summary>
        private void ForkActivePresetIfTuned(string changedProperty)
        {
            if (Store == null || Settings == null) return;
            if (!DefaultProfiles.IsPreset(Store.ActiveProfile)) return;
            if (!ProfileTransfer.IsTuning(changedProperty)) return;

            string preset = Store.ActiveProfile;
            string local = Store.ForkPreset(preset, DefaultProfiles.BuildPreset(preset));
            if (local == null) return;

            // Settings is untouched on purpose - ForkPreset renames the profile around it, so the
            // object the settings page is bound to is still the one being edited. No reactivation.
            Log.Info("'" + preset + "' is a preset and cannot be changed; the edit moved to '" +
                     local + "'.");
            RaiseProfileChanged();
        }

        /// <summary>
        /// For edits that touch the store outside a settings dial - the vehicle-model list is on
        /// <see cref="ShifterProfile"/>, not <see cref="ShifterSettings"/>, so nothing fires
        /// <see cref="OnSettingsChanged"/> for it. Same debounce either way.
        /// <para>
        /// Assigning a car model to a preset is an edit like any other, so it forks first - and it
        /// has to happen before the save, or the store would be written with the model hung on a
        /// profile that is about to be replaced by a fresh one.
        /// </para>
        /// </summary>
        public void ScheduleProfilesSave()
        {
            ForkActivePresetIfTuned("CarModels");
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
        /// Stores a captured clutch pedal. Raised on the engine thread, so like the polarity
        /// result it is marshalled before touching settings WPF is bound to.
        /// <para>
        /// A capture that did not commit - cancelled, or nothing moved - deliberately leaves the
        /// existing binding alone. Clearing it would mean an accidental press of the button loses
        /// a working calibration and the clutch silently reverts to reading zero.
        /// </para>
        /// </summary>
        private void OnPedalCaptureCompleted(AxisCapture capture)
        {
            if (capture == null || capture.Phase != CapturePhase.Committed || capture.Result == null)
            {
                return;
            }

            OnUiThread(() =>
            {
                Settings.ApplyPedalCapture(capture.DeviceId, capture.AxisIndex, capture.Result);
                PushSettingsToEngine();
                SaveStore();
            });
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
                        // Spring polarity is measured but has nowhere to go: the gate is built
                        // from constant forces only. The probe still runs, because a base that
                        // answers predictably on both effect families is the evidence the force
                        // cap waits for.
                        case CalibrationTarget.SpringX:
                        case CalibrationTarget.SpringY:
                            break;
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
                CopyPolarityToEveryProfile();

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

        /// <summary>
        /// Polarity describes the base, not a profile, so a measurement made in one is true in all
        /// of them. Profiles carry their own copy because settings are stored per profile, and
        /// leaving the others stale meant a user who calibrated in 7+R and then switched to
        /// Sequential silently dropped back to the 10% cap - with the tuning tabs disappearing
        /// with it, once they were gated on the same flag.
        /// </summary>
        private void CopyPolarityToEveryProfile()
        {
            if (Store == null || Store.Profiles == null || Settings == null) return;

            foreach (ShifterProfile profile in Store.Profiles)
            {
                if (profile == null || profile.Settings == null || profile.Settings == Settings) continue;

                profile.Settings.InvertConstantX = Settings.InvertConstantX;
                profile.Settings.InvertConstantY = Settings.InvertConstantY;
                profile.Settings.PolarityConfirmed = Settings.PolarityConfirmed;
                profile.Settings.CalibrationForcePct = Settings.CalibrationForcePct;
            }
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

            // Bindable to a wheel button or a key in SimHub's own Controls page, which is the
            // point: switching between an H gate and a sequential lever is a thing done between
            // sessions, or between cars, without reaching for a mouse.
            this.AddAction("NextProfile", (a, b) => CycleProfile(1));
            this.AddAction("PreviousProfile", (a, b) => CycleProfile(-1));
        }

        private static EngineSnapshot Snapshot()
        {
            ShifterEngine engine = _engine;
            return engine != null ? engine.Snapshot : new EngineSnapshot();
        }
    }
}
