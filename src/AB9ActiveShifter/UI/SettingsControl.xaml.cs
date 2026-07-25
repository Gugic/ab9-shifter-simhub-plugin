using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl : UserControl
    {
        private readonly StatusModel _status = new StatusModel();
        private readonly DispatcherTimer _timer;

        public AB9ShifterPlugin Plugin { get; private set; }

        public SettingsControl()
        {
            InitializeComponent();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += (s, e) => RefreshStatus();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public SettingsControl(AB9ShifterPlugin plugin) : this()
        {
            Plugin = plugin;

            DataContext = plugin.Settings;
            StatusPanel.DataContext = _status;
            ReadingsPanel.DataContext = _status;
            Visualizer.Attach(plugin.Settings);

            plugin.Settings.PropertyChanged += OnSettingsChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
            RefreshLockoutSummary();
            _timer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();

            if (Plugin != null && Plugin.Settings != null)
            {
                Plugin.Settings.PropertyChanged -= OnSettingsChanged;
            }
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            RefreshLockoutSummary();
        }

        private void RefreshStatus()
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            EngineSnapshot snap = engine != null ? engine.Snapshot : new EngineSnapshot();
            _status.Update(snap);

            bool calibrating = engine != null && engine.IsCalibrating;
            CalibrateButton.IsEnabled = !calibrating;
            CancelCalibrationButton.IsEnabled = calibrating;

            RefreshCalibrationResults();
        }

        /// <summary>
        /// The lockout is a share of the overall gain, so the number that actually matters is
        /// the product. Spell it out rather than making the user multiply.
        /// </summary>
        private void RefreshLockoutSummary()
        {
            if (LockoutSummary == null || Plugin == null || Plugin.Settings == null) return;

            ShifterSettings s = Plugin.Settings;
            EngineConfig cfg = s.ToEngineConfig();

            double wall = cfg.EffectiveGain * s.ChannelWallForcePct;
            double lockout = cfg.EffectiveGain * s.LockoutForcePct;

            string capped = cfg.PolarityConfirmed
                ? ""
                : "  Gain is capped at " + EngineConfig.UnconfirmedGainCapPct +
                  "% until polarity is measured, so everything will feel light.";

            LockoutSummary.Text = string.Format(
                "Of what the base can produce: gate walls about {0:0}%, the 7/R lockout about {1:0}%.{2}",
                wall, lockout, capped);
        }

        private void OnCalibrate(object sender, RoutedEventArgs e)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            if (engine == null || !engine.IsRunning)
            {
                MessageBox.Show(
                    "The force feedback engine is not running. Enable the plugin on the Setup tab and wait for the base to connect.",
                    "AB9 Active Shifter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!engine.Snapshot.DeviceConnected)
            {
                MessageBox.Show(
                    "The base is not connected yet:\n\n" + engine.Snapshot.StatusMessage,
                    "AB9 Active Shifter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Plugin != null) Plugin.LastCalibration.Clear();
            _renderedCalibrationCount = -1;
            CalibrationResultBorder.Visibility = Visibility.Collapsed;

            engine.RequestCalibration();
        }

        private void OnCancelCalibration(object sender, RoutedEventArgs e)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            if (engine != null) engine.CancelCalibration();
        }

        private int _renderedCalibrationCount = -1;

        private void RefreshCalibrationResults()
        {
            if (Plugin == null || CalibrationResultPanel == null) return;

            int count = Plugin.LastCalibration.Count;
            if (count == _renderedCalibrationCount) return;
            _renderedCalibrationCount = count;

            CalibrationResultPanel.Children.Clear();

            if (count == 0)
            {
                CalibrationResultBorder.Visibility = Visibility.Collapsed;
                return;
            }

            CalibrationResultBorder.Visibility = Visibility.Visible;

            foreach (CalibrationTarget target in (CalibrationTarget[])Enum.GetValues(typeof(CalibrationTarget)))
            {
                CalibrationResult result;
                if (!Plugin.LastCalibration.TryGetValue(target, out result)) continue;

                CalibrationResultPanel.Children.Add(new TextBlock
                {
                    Text = result.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                    Foreground = ResultBrush(result.Outcome)
                });
            }
        }

        private void OnToggleRecord(object sender, RoutedEventArgs e)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            if (engine == null) return;

            if (engine.Trace.IsRecording)
            {
                try
                {
                    string path = engine.SaveTrace(null);
                    RecordStatus.Text = "Saved " + engine.Trace.Count + " ticks to " + path;
                }
                catch (Exception ex)
                {
                    RecordStatus.Text = "Could not save the trace: " + ex.Message;
                }

                RecordButton.Content = "Start recording";
                return;
            }

            engine.Trace.Start();
            RecordButton.Content = "Stop and save";
            RecordStatus.Text = "Recording. Make the movement that misbehaves, then stop.";
        }

        private void OnOpenTraces(object sender, RoutedEventArgs e)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            if (engine == null) return;

            try
            {
                System.IO.Directory.CreateDirectory(engine.TraceDirectory);
                System.Diagnostics.Process.Start(engine.TraceDirectory);
            }
            catch (Exception ex)
            {
                RecordStatus.Text = "Could not open the folder: " + ex.Message;
            }
        }

        private void OnResetForces(object sender, RoutedEventArgs e)
        {
            Reset(ShifterSettings.ResetScope.Forces, "force settings");
        }

        private void OnResetGeometry(object sender, RoutedEventArgs e)
        {
            Reset(ShifterSettings.ResetScope.Geometry, "gate geometry");
        }

        private void OnResetEverything(object sender, RoutedEventArgs e)
        {
            Reset(ShifterSettings.ResetScope.Everything,
                  "every setting, including the measured polarity (you will need to calibrate again)");
        }

        private void Reset(ShifterSettings.ResetScope scope, string description)
        {
            if (Plugin == null || Plugin.Settings == null) return;

            if (MessageBox.Show(
                    "Reset " + description + " to defaults?",
                    "AB9 Active Shifter", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                != MessageBoxResult.OK)
            {
                return;
            }

            Plugin.Settings.ResetToDefaults(scope);
            RefreshLockoutSummary();
        }

        private static Brush ResultBrush(CalibrationOutcome outcome)
        {
            switch (outcome)
            {
                case CalibrationOutcome.Correct: return new SolidColorBrush(Color.FromRgb(0x36, 0xC7, 0x6A));
                case CalibrationOutcome.Inverted: return new SolidColorBrush(Color.FromRgb(0xE8, 0x8A, 0x1A));
                case CalibrationOutcome.Inconclusive: return new SolidColorBrush(Color.FromRgb(0xC7, 0x3B, 0x3B));
                default: return SystemColors.ControlTextBrush;
            }
        }

        /// <summary>Bindable view of the engine snapshot for the status panels.</summary>
        private sealed class StatusModel : INotifyPropertyChanged
        {
            private string _status = "Not started";
            private string _deviceText = "Base: -";
            private string _vJoyText = "vJoy: -";
            private string _gearText = "Gear: -";
            private string _loopText = "Loop: -";
            private string _axisText = "X: -   Y: -";
            private string _stateText = "State: -";

            public string Status { get { return _status; } private set { Set(ref _status, value, "Status"); } }
            public string DeviceText { get { return _deviceText; } private set { Set(ref _deviceText, value, "DeviceText"); } }
            public string VJoyText { get { return _vJoyText; } private set { Set(ref _vJoyText, value, "VJoyText"); } }
            public string GearText { get { return _gearText; } private set { Set(ref _gearText, value, "GearText"); } }
            public string LoopText { get { return _loopText; } private set { Set(ref _loopText, value, "LoopText"); } }
            public string AxisText { get { return _axisText; } private set { Set(ref _axisText, value, "AxisText"); } }
            public string StateText { get { return _stateText; } private set { Set(ref _stateText, value, "StateText"); } }

            public void Update(EngineSnapshot snap)
            {
                Status = snap.StatusMessage ?? "";

                DeviceText = snap.DeviceConnected
                    ? "Base: connected" + (string.IsNullOrEmpty(snap.DeviceName) ? "" : " (" + snap.DeviceName + ")")
                    : "Base: not connected";

                VJoyText = snap.VJoyConnected ? "vJoy: connected" : "vJoy: not connected";
                GearText = "Gear: " + snap.GearLabel;
                LoopText = snap.LoopHz > 0 ? "Loop: " + Math.Round(snap.LoopHz) + " Hz" : "Loop: idle";
                AxisText = "X: " + snap.X + "   Y: " + snap.Y;
                StateText = "State: " + snap.State + (snap.Column == Column.None ? "" : " in column " + snap.Column);
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private void Set(ref string field, string value, string name)
            {
                if (field == value) return;
                field = value;

                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
