using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AB9ActiveShifter.Core;
using AB9ActiveShifter.Output;
using SimHub.Plugins.UI;

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

            _undoMenuItem.Click += OnUndoSlider;
            _sliderContextMenu.Items.Add(_undoMenuItem);
            _sliderContextMenu.Opened += OnSliderContextMenuOpened;

            IndexSliders();

            VersionText.Text = "Version " + PluginInfo.Version;
            AboutVersionText.Text = "AB9 Active Shifter " + PluginInfo.Version;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += (s, e) => RefreshStatus();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }


        private ShifterSettings _boundSettings;
        private bool _refreshingProfiles;
        private bool _refreshingVJoy;

        // Slider undo and the modified-since-profile-opened marker. Built once from the
        // logical tree in the constructor - LogicalTreeHelper rather than VisualTreeHelper,
        // because at this point SHTabControl's tab content and SHSection's template have not
        // necessarily had ApplyTemplate() called yet, and the logical tree reflects the
        // XAML-declared content regardless of template realization timing.
        private readonly Dictionary<string, TitledSlider> _sliderByProperty = new Dictionary<string, TitledSlider>();
        private readonly Dictionary<TitledSlider, string> _originalTitle = new Dictionary<TitledSlider, string>();
        private readonly Dictionary<TitledSlider, double> _previousValue = new Dictionary<TitledSlider, double>();
        private readonly HashSet<TitledSlider> _gestureActive = new HashSet<TitledSlider>();
        private Dictionary<string, object> _profileBaseline = new Dictionary<string, object>();

        // Built and wired entirely in code, not XAML: a MenuItem's inline Click="..." handler
        // placed inside UserControl.Resources shifts the compiler's connectionId numbering and
        // throws an unrelated cast exception at load (see the comment in SettingsControl.xaml).
        // One shared instance handed to every slider; ContextMenu.PlacementTarget tells
        // OnSliderContextMenuOpened/OnUndoSlider which one was actually right-clicked.
        private readonly ContextMenu _sliderContextMenu = new ContextMenu();
        private readonly MenuItem _undoMenuItem = new MenuItem { Header = "Undo to previous value" };

        private static readonly Brush DirtyBrush = MakeDirtyBrush();

        private static Brush MakeDirtyBrush()
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(0xC7, 0x3B, 0x3B));
            brush.Freeze();
            return brush;
        }

        /// <summary>Last answer from the cheap repeated check on the chosen vJoy device.</summary>
        private bool _vjoyReady;
        private int _vjoyPollTicks;

        /// <summary>Set by "Measure again", so the collapsed calibration panel opens back up.</summary>
        private bool _recalibrating;

        public SettingsControl(AB9ShifterPlugin plugin) : this()
        {
            Plugin = plugin;

            StatusPanel.DataContext = _status;
            ReadingsPanel.DataContext = _status;

            BindActiveProfile();
            RefreshProfiles();
            RefreshVJoyDevices();
            UpdateCalibrationSection();

            plugin.ProfileChanged += OnProfileChanged;
        }

        /// <summary>Points every binding at the plugin's current settings object.</summary>
        private void BindActiveProfile()
        {
            if (_boundSettings != null) _boundSettings.PropertyChanged -= OnSettingsChanged;

            ShifterSettings previous = _boundSettings;
            _boundSettings = Plugin != null ? Plugin.Settings : null;
            DataContext = _boundSettings;

            if (_boundSettings != null)
            {
                _boundSettings.PropertyChanged += OnSettingsChanged;
                Visualizer.Attach(_boundSettings);
            }

            // A fresh baseline for "modified since I opened this profile" - not since the last
            // autosave, which fires a couple of seconds after every edit and would make the
            // marker flash and clear on its own. Only on an actual profile change: SimHub
            // re-fires Loaded on every navigation back to this page, and re-binding the SAME
            // settings object on one of those must not wipe markers for edits already sitting
            // on the current profile.
            if (!ReferenceEquals(previous, _boundSettings)) RefreshProfileBaseline();
        }

        /// <summary>
        /// Walks the logical tree once for every ui:TitledSlider, so the undo gesture and the
        /// dirty marker work generically without an x:Name or extra markup on each of the ~40
        /// sliders across every tab. Reads each slider's Value binding path to learn which
        /// ShifterSettings property it shows, rather than requiring one to be declared.
        /// </summary>
        private void IndexSliders()
        {
            foreach (TitledSlider slider in FindTitledSliders(this))
            {
                BindingExpression expr = BindingOperations.GetBindingExpression(slider, TitledSlider.ValueProperty);
                string property = expr != null && expr.ParentBinding != null && expr.ParentBinding.Path != null
                    ? expr.ParentBinding.Path.Path
                    : null;
                if (string.IsNullOrEmpty(property)) continue;

                _sliderByProperty[property] = slider;
                _originalTitle[slider] = slider.Title;
                slider.ContextMenu = _sliderContextMenu;
            }
        }

        private static IEnumerable<TitledSlider> FindTitledSliders(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                DependencyObject element = child as DependencyObject;
                if (element == null) continue;

                TitledSlider slider = element as TitledSlider;
                if (slider != null) yield return slider;

                foreach (TitledSlider nested in FindTitledSliders(element)) yield return nested;
            }
        }

        // ---------------------------------------------------------------- slider undo

        /// <summary>
        /// Brackets one adjustment: whichever event gets there first between a mouse press and a
        /// key press snapshots the value, and its matching end event closes the bracket. A held
        /// drag or a held +/- repeat is one adjustment this way, whatever the interaction,
        /// without guessing from a settle timer. Deliberately not also bracketed by keyboard
        /// focus - see the comment on the Style in SettingsControl.xaml for why that broke undo.
        /// </summary>
        private void BeginGesture(TitledSlider slider)
        {
            if (slider == null || _gestureActive.Contains(slider)) return;
            _gestureActive.Add(slider);
            _previousValue[slider] = slider.Value;
        }

        private void EndGesture(TitledSlider slider)
        {
            if (slider != null) _gestureActive.Remove(slider);
        }

        private void OnSliderGestureStartMouse(object sender, MouseButtonEventArgs e) { BeginGesture(sender as TitledSlider); }
        private void OnSliderGestureEndMouse(object sender, MouseButtonEventArgs e) { EndGesture(sender as TitledSlider); }
        private void OnSliderGestureStartKey(object sender, KeyEventArgs e) { BeginGesture(sender as TitledSlider); }
        private void OnSliderGestureEndKey(object sender, KeyEventArgs e) { EndGesture(sender as TitledSlider); }

        private void OnSliderContextMenuOpened(object sender, RoutedEventArgs e)
        {
            TitledSlider slider = _sliderContextMenu.PlacementTarget as TitledSlider;

            double previous = 0;
            bool hasPrevious = slider != null && _previousValue.TryGetValue(slider, out previous) && previous != slider.Value;
            _undoMenuItem.IsEnabled = hasPrevious;
            _undoMenuItem.Header = hasPrevious
                ? string.Format("Undo to previous value ({0:0.##})", previous)
                : "Undo to previous value";
        }

        private void OnUndoSlider(object sender, RoutedEventArgs e)
        {
            TitledSlider slider = _sliderContextMenu.PlacementTarget as TitledSlider;
            if (slider == null) return;

            double previous;
            if (_previousValue.TryGetValue(slider, out previous)) slider.Value = previous;
        }

        // ---------------------------------------------------------------- modified-since-open marker

        /// <summary>
        /// Snapshots every slider-bound property's current value as the baseline "as opened"
        /// state, and clears every dirty marker back to it. Called whenever the bound settings
        /// object changes - a profile switch or the initial load - never by the autosave, so
        /// the marker means "since you opened this profile," not "since the last debounced save."
        /// </summary>
        private void RefreshProfileBaseline()
        {
            _profileBaseline = new Dictionary<string, object>();
            if (Plugin == null || Plugin.Settings == null) return;

            foreach (KeyValuePair<string, TitledSlider> pair in _sliderByProperty)
            {
                PropertyInfo prop = typeof(ShifterSettings).GetProperty(pair.Key);
                if (prop == null) continue;

                _profileBaseline[pair.Key] = prop.GetValue(Plugin.Settings, null);
                SetDirty(pair.Value, false);
            }
        }

        private void RefreshDirtyMarker(string propertyName)
        {
            TitledSlider slider;
            if (string.IsNullOrEmpty(propertyName) || !_sliderByProperty.TryGetValue(propertyName, out slider)) return;

            PropertyInfo prop = typeof(ShifterSettings).GetProperty(propertyName);
            if (prop == null || Plugin == null || Plugin.Settings == null) return;

            object baseline;
            object current = prop.GetValue(Plugin.Settings, null);
            bool dirty = !_profileBaseline.TryGetValue(propertyName, out baseline) || !Equals(baseline, current);
            SetDirty(slider, dirty);
        }

        private readonly Dictionary<TitledSlider, Adorner> _dirtyAdorners = new Dictionary<TitledSlider, Adorner>();

        /// <summary>
        /// An Adorner paints the whole marked-up label directly over the control from outside
        /// its template: setting TitledSlider's Foreground externally had no visible effect
        /// (confirmed on hardware) - its template evidently hardcodes the title's brush rather
        /// than binding it to the control's own Foreground. The real Title is blanked while
        /// dirty so the (uncoloured) original text underneath cannot show through or double up
        /// with the red one drawn on top; restoring it is why the original has to be tracked.
        /// </summary>
        private void SetDirty(TitledSlider slider, bool dirty)
        {
            string original;
            if (!_originalTitle.TryGetValue(slider, out original)) return;

            Adorner existing;
            if (_dirtyAdorners.TryGetValue(slider, out existing))
            {
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(slider);
                if (layer != null) layer.Remove(existing);
                _dirtyAdorners.Remove(slider);
            }

            if (dirty)
            {
                slider.Title = string.Empty;

                AdornerLayer layer = AdornerLayer.GetAdornerLayer(slider);
                if (layer != null)
                {
                    Adorner adorner = new DirtyMarkerAdorner(slider, "* " + original, DirtyBrush);
                    layer.Add(adorner);
                    _dirtyAdorners[slider] = adorner;
                }
            }
            else
            {
                slider.Title = original;
            }
        }

        /// <summary>
        /// Paints the whole label in red over a slider's title area, independent of its
        /// template. Position and size are a best-effort match to TitledSlider's own title
        /// text - there is no source to read the real values from - so this is the one part of
        /// the feature most likely to need a visual nudge once seen on the actual control.
        /// </summary>
        private sealed class DirtyMarkerAdorner : Adorner
        {
            private readonly string _text;
            private readonly Brush _brush;

            public DirtyMarkerAdorner(UIElement adorned, string text, Brush brush) : base(adorned)
            {
                _text = text;
                _brush = brush;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                Typeface typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                FormattedText text = new FormattedText(
                    _text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 13, _brush, 1.0);
                drawingContext.DrawText(text, new Point(0, 0));
            }
        }

        private void OnProfileChanged()
        {
            BindActiveProfile();
            RefreshProfiles();
            RefreshLockoutSummary();
            RefreshWallRampSummary();
            RefreshChannelFreeDepthSummary();

            // The device number is stored per profile, so switching profile can change it.
            RefreshVJoyDevices();
            UpdateCalibrationSection();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // SimHub keeps this control alive across page navigation: every leave fires
            // Unloaded, every return fires Loaded, and the constructor only ever runs once.
            // Everything the constructor wired must be re-wired here, or the first navigation
            // away permanently disconnects the profile UI - Duplicate then created and
            // activated profiles the combo never showed (read, reasonably, as the button
            // doing nothing), and the dials stayed bound to the previously active profile.
            if (Plugin != null)
            {
                Plugin.ProfileChanged -= OnProfileChanged;
                Plugin.ProfileChanged += OnProfileChanged;
            }

            BindActiveProfile();
            RefreshStatus();
            RefreshLockoutSummary();
            RefreshWallRampSummary();
            RefreshChannelFreeDepthSummary();
            RefreshProfiles();
            RefreshVJoyDevices();
            UpdateCalibrationSection();
            _timer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();

            if (_boundSettings != null)
            {
                _boundSettings.PropertyChanged -= OnSettingsChanged;
                _boundSettings = null;
            }

            if (Plugin != null) Plugin.ProfileChanged -= OnProfileChanged;
        }

        private void RefreshProfiles()
        {
            if (Plugin == null || Plugin.Store == null || Plugin.Store.Profiles == null || ProfileCombo == null) return;

            _refreshingProfiles = true;
            try
            {
                ProfileCombo.Items.Clear();
                foreach (ShifterProfile p in Plugin.Store.Profiles)
                {
                    if (p != null) ProfileCombo.Items.Add(p.Name);
                }
                ProfileCombo.SelectedItem = Plugin.Store.ActiveProfile;
                ProfileCombo.Text = Plugin.Store.ActiveProfile;
            }
            finally
            {
                _refreshingProfiles = false;
            }
        }

        /// <summary>
        /// Asks vJoy what exists and offers it. Runs whenever the page appears and on demand,
        /// because a user creating a device in vJoyConf does it with this page open in front of
        /// them - and because the answer changes when another program grabs a device.
        /// </summary>
        private void RefreshVJoyDevices()
        {
            if (VJoyCombo == null || _boundSettings == null) return;

            _refreshingVJoy = true;
            try
            {
                uint selected = _boundSettings.VJoyDeviceId;
                VJoyProbeResult probe = VJoyDeviceProbe.Probe(selected);

                VJoyCombo.Items.Clear();

                _vjoyReady = false;

                if (!probe.DriverPresent || probe.Devices.Count == 0)
                {
                    // No choice to offer. Keep the saved number visible rather than blanking the
                    // control, so the setting is still legible when vJoy comes back.
                    ComboBoxItem placeholder = new ComboBoxItem
                    {
                        Content = "Device " + selected + " - vJoy not available",
                        Tag = selected
                    };
                    VJoyCombo.Items.Add(placeholder);
                    VJoyCombo.SelectedIndex = 0;
                    VJoyCombo.IsEnabled = false;
                    VJoyHint.Text = probe.Problem ?? "vJoy is not available.";
                    return;
                }

                VJoyCombo.IsEnabled = true;

                foreach (VJoyDeviceInfo device in probe.Devices)
                {
                    ComboBoxItem item = new ComboBoxItem { Content = device.Describe(), Tag = device.Id };
                    VJoyCombo.Items.Add(item);
                    if (device.Id == selected)
                    {
                        VJoyCombo.SelectedItem = item;
                        _vjoyReady = CanCarryGears(device);
                    }
                }

                if (VJoyCombo.SelectedItem == null && VJoyCombo.Items.Count > 0)
                {
                    VJoyCombo.SelectedIndex = 0;
                }

                VJoyHint.Text = HintFor(probe, selected);
            }
            catch (Exception ex)
            {
                Log.Error("Could not enumerate vJoy devices", ex);
                VJoyHint.Text = "Could not read the vJoy device list: " + ex.Message;
            }
            finally
            {
                _refreshingVJoy = false;
            }

            UpdateTabGate();
        }

        /// <summary>
        /// Whether gears can actually reach this device. A shortfall of buttons is a warning
        /// rather than a blocker - eight buttons still carries every gear, and the connect path
        /// treats it the same way - so it does not hold the gate shut.
        /// </summary>
        private static bool CanCarryGears(VJoyDeviceInfo device)
        {
            return device != null &&
                   (device.State == VJoyDeviceState.Free || device.State == VJoyDeviceState.Owned);
        }

        /// <summary>The sentence under the picker: what is wrong with the chosen device, if anything.</summary>
        private static string HintFor(VJoyProbeResult probe, uint selected)
        {
            VJoyDeviceInfo chosen = null;
            foreach (VJoyDeviceInfo d in probe.Devices)
            {
                if (d.Id == selected) { chosen = d; break; }
            }

            if (chosen == null) return "";

            switch (chosen.State)
            {
                case VJoyDeviceState.Missing:
                    return "Device " + selected + " has not been created. Make it in vJoyConf with at least " +
                           VJoyDeviceInfo.ButtonsNeeded + " buttons, or pick one from the list that exists.";

                case VJoyDeviceState.Busy:
                    return "Device " + selected + " is held by another program, so gears cannot be sent to it. " +
                           "Close that program or pick a free device.";

                case VJoyDeviceState.Unknown:
                    return "vJoy will not say what state device " + selected + " is in.";

                default:
                    return chosen.Buttons >= VJoyDeviceInfo.ButtonsNeeded
                        ? "Ready. Bind gears 1-7 and reverse to buttons 1-8, and the sequential up/down to 9 and 10."
                        : "Device " + selected + " has only " + chosen.Buttons + " buttons. Gears need 1-8 and " +
                          "sequential needs 9-10, so raise the count in vJoyConf.";
            }
        }

        private void OnVJoyDeviceSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingVJoy || _boundSettings == null) return;

            ComboBoxItem item = VJoyCombo.SelectedItem as ComboBoxItem;
            if (item == null || !(item.Tag is uint)) return;

            uint id = (uint)item.Tag;
            if (id == _boundSettings.VJoyDeviceId) return;

            // The engine notices the change and reconnects on its own; this only records it.
            _boundSettings.VJoyDeviceId = id;
            RefreshVJoyDevices();
        }

        private void OnRefreshVJoyDevices(object sender, RoutedEventArgs e)
        {
            // Forget a cached failure too: this button is what a user presses after installing
            // vJoy, and it should not need a SimHub restart to be believed.
            VJoyDeviceProbe.Forget();
            RefreshVJoyDevices();
        }

        /// <summary>
        /// Everything except Setup stays hidden until the shifter can actually do its job:
        /// polarity measured, so a force dial cannot be turned up on a base that might push the
        /// wrong way, and a vJoy device present, so a gear has somewhere to go. Both are things a
        /// user does once, and neither is discoverable from a page full of sliders.
        /// <para>
        /// Everything needed to satisfy those two conditions is on the Setup tab by construction -
        /// the vJoy picker and the base's vendor and product ids included - so the gate can never
        /// hide the control that opens it.
        /// </para>
        /// </summary>
        private void UpdateTabGate()
        {
            if (FeelTab == null || _boundSettings == null) return;

            bool polarity = _boundSettings.PolarityConfirmed;
            bool ready = polarity && _vjoyReady;

            Visibility visibility = ready ? Visibility.Visible : Visibility.Collapsed;
            FeelTab.Visibility = visibility;
            EffectsTab.Visibility = visibility;
            GeometryTab.Visibility = visibility;
            MonitorTab.Visibility = visibility;

            // Collapsing the selected tab would leave the page blank, so come home first.
            if (!ready)
            {
                TabControl tabs = FeelTab.Parent as TabControl;
                if (tabs != null && tabs.SelectedIndex != 0) tabs.SelectedIndex = 0;
            }

            TabGateText.Text = ready
                ? ""
                : "Feel, Effects, Geometry and Monitor appear once two things are true: " +
                  (polarity ? "polarity is measured (done)" : "polarity is measured (not yet - see below)") +
                  ", and " +
                  (_vjoyReady ? "a vJoy device is available (done)." : "a vJoy device is available (not yet - see above).");
        }

        /// <summary>
        /// The measurement is a one-off, so once it is made the section collapses to its result
        /// and a way back in. Leaving four paragraphs of explanation on the page forever suggests
        /// there is something left to do.
        /// </summary>
        private void UpdateCalibrationSection()
        {
            if (CalibrationDonePanel == null || _boundSettings == null) return;

            bool done = _boundSettings.PolarityConfirmed && !_recalibrating;

            CalibrationDonePanel.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
            CalibrationFullPanel.Visibility = done ? Visibility.Collapsed : Visibility.Visible;

            if (done)
            {
                CalibrationSummary.Text =
                    "Polarity measured on this base: a push is " +
                    (_boundSettings.InvertConstantX ? "inverted" : "normal") + " left/right and " +
                    (_boundSettings.InvertConstantY ? "inverted" : "normal") + " forward/back. " +
                    "The force cap is off. This is a property of the base, not of a profile, so it " +
                    "only needs measuring again if you change hardware or the gate pushes the wrong way.";
            }
        }

        private void OnRecalibrate(object sender, RoutedEventArgs e)
        {
            _recalibrating = true;
            UpdateCalibrationSection();
        }

        private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingProfiles || Plugin == null || Plugin.Store == null) return;

            string name = ProfileCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(name) && name != Plugin.Store.ActiveProfile)
            {
                Plugin.ActivateProfile(name);
            }
        }

        private void OnAddProfile(object sender, RoutedEventArgs e)
        {
            if (Plugin == null || Plugin.Store == null) return;

            // Duplicating a copy counts up instead of piling the word on: "5+R copy",
            // "5+R copy 2", never "5+R copy copy".
            string name = System.Text.RegularExpressions.Regex.Replace(
                Plugin.Store.ActiveProfile ?? "Profile", @"\s+copy(\s+\d+)?$", "");
            Plugin.AddProfileFromCurrent(name + " copy");
        }

        private void OnRenameProfile(object sender, RoutedEventArgs e)
        {
            if (Plugin == null || ProfileCombo == null) return;
            Plugin.RenameActiveProfile(ProfileCombo.Text);
        }

        private void OnDeleteProfile(object sender, RoutedEventArgs e)
        {
            if (Plugin == null || Plugin.Store == null ||
                Plugin.Store.Profiles == null || Plugin.Store.Profiles.Count <= 1)
            {
                return;
            }

            if (MessageBox.Show(
                    "Delete profile '" + Plugin.Store.ActiveProfile + "'?",
                    "AB9 Active Shifter", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                != MessageBoxResult.OK)
            {
                return;
            }

            Plugin.DeleteActiveProfile();
        }

        private void OnExportProfile(object sender, RoutedEventArgs e)
        {
            if (Plugin == null || Plugin.Store == null) return;

            ShifterProfile active = Plugin.Store.FindActive();
            if (active == null) return;

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export profile",
                FileName = SafeFileName(active.Name) + ProfileTransfer.FileExtension,
                DefaultExt = ".json",
                Filter = "AB9 shifter profile (*.json)|*.json|All files (*.*)|*.*",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName, ProfileTransfer.Export(active),
                    new System.Text.UTF8Encoding(false));
                Log.Info("Exported profile '" + active.Name + "'.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not export profile", ex);
                MessageBox.Show("Could not write that file.\n\n" + ex.Message,
                    "AB9 Active Shifter", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnImportProfile(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;

            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import profile",
                DefaultExt = ".json",
                Filter = "AB9 shifter profile (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true) return;

            ProfileImportResult result;
            try
            {
                result = ProfileTransfer.Import(System.IO.File.ReadAllText(dialog.FileName), Plugin.Settings);
            }
            catch (ProfileTransferException ex)
            {
                // Expected: the file is not one of ours, or is unreadable. Say what, not how.
                MessageBox.Show(ex.Message, "AB9 Active Shifter",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Could not import profile", ex);
                MessageBox.Show("Could not read that file.\n\n" + ex.Message,
                    "AB9 Active Shifter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string name = Plugin.AddImportedProfile(result.Profile);
            if (name == null) return;

            // Tell them what actually arrived. A silent import that quietly dropped or clamped
            // half a file is how someone ends up debugging a feel they never chose.
            string message = "Imported as '" + name + "' and made active.\n\n" +
                             result.Applied + " settings applied.";
            if (result.Clamped > 0)
            {
                message += "\n" + result.Clamped + " were outside the supported range and were " +
                           "brought back into it.";
            }
            if (result.Unknown > 0)
            {
                message += "\n" + result.Unknown + " were not recognised by this version and were ignored.";
            }
            message += "\n\nForces are off, and your own measured polarity has been kept.";

            MessageBox.Show(message, "AB9 Active Shifter", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>A profile name with anything Windows refuses in a filename taken out.</summary>
        private static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "profile";

            System.Text.StringBuilder sb = new System.Text.StringBuilder(name.Length);
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString().Trim();
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            RefreshLockoutSummary();
            RefreshWallRampSummary();
            RefreshChannelFreeDepthSummary();
            if (e != null) RefreshDirtyMarker(e.PropertyName);

            // Calibration writes the flag from the engine thread; this is how the gate and the
            // collapsed calibration panel find out that the measurement landed.
            if (e == null || e.PropertyName == "PolarityConfirmed" ||
                e.PropertyName == "InvertConstantX" || e.PropertyName == "InvertConstantY")
            {
                if (_boundSettings != null && _boundSettings.PolarityConfirmed) _recalibrating = false;
                UpdateCalibrationSection();
                UpdateTabGate();
            }
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

            // Another program can take the vJoy device while this page is open, so the gate has
            // to keep asking - but only every couple of seconds, and only about the one device
            // that is chosen. Rebuilding the whole list on a timer would fight the dropdown.
            if (++_vjoyPollTicks >= 10)
            {
                _vjoyPollTicks = 0;
                if (_boundSettings != null)
                {
                    bool ready = CanCarryGears(VJoyDeviceProbe.ProbeOne(_boundSettings.VJoyDeviceId));
                    if (ready != _vjoyReady)
                    {
                        _vjoyReady = ready;
                        UpdateTabGate();
                    }
                }
            }
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

            string capped = cfg.PolarityConfirmed
                ? ""
                : "  Gain is capped at " + EngineConfig.UnconfirmedGainCapPct +
                  "% until polarity is measured, so everything will feel light.";

            if (!s.IsHPattern)
            {
                LockoutSummary.Text = string.Format(
                    "Of what the base can produce: push resistance about {0:0}%, lateral rail about {1:0}%.{2}",
                    cfg.EffectiveGain * s.DetentResistPct,
                    cfg.EffectiveGain * s.ColumnPinForcePct,
                    capped);
                return;
            }

            double wall = cfg.EffectiveGain * s.ChannelWallForcePct;
            double lockout = cfg.EffectiveGain * s.LockoutForcePct;

            LockoutSummary.Text = s.HasLockout
                ? string.Format(
                    "Of what the base can produce: gate walls about {0:0}%, the R-column lockout about {1:0}%.{2}",
                    wall, lockout, capped)
                : string.Format(
                    "Of what the base can produce: gate walls about {0:0}%.{1}",
                    wall, capped);
        }

        /// <summary>
        /// The Wall bite distance slider accepts up to 6000, but the geometry silently clamps
        /// the effective bite lower whenever the corridor leaves no room for it (see
        /// <see cref="ForceComposer.WallRampCeiling"/>). Spelling that out here is what item
        /// #1 of the enhancement brief asked for: a user should see a value being overridden
        /// immediately, not discover it through a support conversation and empirical testing.
        /// </summary>
        private void RefreshWallRampSummary()
        {
            if (WallRampSummary == null || Plugin == null || Plugin.Settings == null) return;

            ShifterSettings s = Plugin.Settings;
            EngineConfig cfg = s.ToEngineConfig();
            ForceComposer composer = new ForceComposer(cfg.BuildGeometry(), cfg);
            int ceiling = composer.WallRampCeiling;

            WallRampSummary.Text = ceiling < s.WallRamp
                ? string.Format(
                    "Effective bite: {0} counts, capped down from {1} - the tightest column has no room for a longer bite at the current geometry. Lowering Slot width, free corridor (SlotHalfWidth) would raise this ceiling.",
                    ceiling, s.WallRamp)
                : string.Format("Effective bite: {0} counts.", s.WallRamp);
        }

        /// <summary>
        /// Same clamp-visibility treatment as <see cref="RefreshWallRampSummary"/>, for item #2
        /// of the enhancement brief: the Tunnel depth slider accepts up to 4000, but
        /// <see cref="ForceComposer.ChannelFreeDepthCeiling"/> silently clamps it to the neutral
        /// channel's own enter band. One mechanism for both dials rather than two one-off
        /// displays.
        /// </summary>
        private void RefreshChannelFreeDepthSummary()
        {
            if (ChannelFreeDepthSummary == null || Plugin == null || Plugin.Settings == null) return;

            ShifterSettings s = Plugin.Settings;
            EngineConfig cfg = s.ToEngineConfig();
            ForceComposer composer = new ForceComposer(cfg.BuildGeometry(), cfg);
            int ceiling = composer.ChannelFreeDepthCeiling;

            ChannelFreeDepthSummary.Text = ceiling < s.ChannelFreeDepth
                ? string.Format(
                    "Effective depth: {0} counts, capped down from {1} - the neutral channel's own enter band leaves no more free room at the current geometry. Raising Neutral tunnel half-depth (enter) (ChannelHalfEnter), under Advanced gate geometry, would allow more.",
                    ceiling, s.ChannelFreeDepth)
                : string.Format("Effective depth: {0} counts.", s.ChannelFreeDepth);
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

        private void OnResetEffects(object sender, RoutedEventArgs e)
        {
            Reset(ShifterSettings.ResetScope.Effects, "the telemetry effects (all back to off)");
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
            RefreshWallRampSummary();
            RefreshChannelFreeDepthSummary();
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
