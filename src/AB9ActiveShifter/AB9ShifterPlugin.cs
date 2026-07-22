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

            Settings = this.ReadCommonSettings<ShifterSettings>(SettingsKey, () => new ShifterSettings());

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
            this.SaveCommonSettings(SettingsKey, Settings);
        }

        /// <summary>IReusable: the genuine shutdown, when SimHub is really done with us.</summary>
        public void FinalizePlugin()
        {
            Log.Info("FinalizePlugin (shutting the engine down).");

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
                try { this.SaveCommonSettings(SettingsKey, Settings); }
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
