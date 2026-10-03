using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl
    {
        private Window _tuningWindow;
        private bool _reviewingSetup;
        private bool _preparingCalibration;
        private bool _stopAfterCalibration;
        private bool _calibrationStarted;
        private bool? _setupGroupsExpanded;

        private void RefreshWorkspace()
        {
            if (Plugin == null || Plugin.Store == null || _boundSettings == null || MainTab == null) return;
            bool firmware = Plugin.CurrentOperatingMode == OperatingMode.Ab9HPattern;
            bool completed = Plugin.Store.SetupCompleted;
            bool virtualAvailable = Plugin.VirtualControlsAvailable;
            bool tuningAvailable = virtualAvailable || Plugin.NativeProfileUpdateInProgress;
            bool calibrating = _preparingCalibration || (AB9ShifterPlugin.Engine != null && AB9ShifterPlugin.Engine.IsCalibrating);
            bool baseMissing = BaseConnectionWarning.Visibility == Visibility.Visible;
            bool ready = virtualAvailable && !baseMissing && _boundSettings.PolarityConfirmed && SelectedOutputReady;
            bool expandSetup = !completed || _reviewingSetup;
            if (_setupGroupsExpanded != expandSetup)
            {
                _setupGroupsExpanded = expandSetup;
                PrepareGroup.IsExpanded = expandSetup;
                OutputGroup.IsExpanded = expandSetup;
                CalibrationGroup.IsExpanded = expandSetup;
                PrepareGroup.Header = completed ? "Prepare the base" : "1 · Prepare the base";
                OutputGroup.Header = completed ? "Base and output" : "2 · Base and output";
                CalibrationGroup.Header = completed ? "Polarity calibration" : "3 · Measure polarity";
            }
            MainTab.Visibility = completed && !firmware ? Visibility.Visible : Visibility.Collapsed;
            OptionsTab.Header = firmware ? "Mode" : completed ? "Options" : "Setup";
            SetupIntro.Visibility = !firmware && (!completed || _reviewingSetup) ? Visibility.Visible : Visibility.Collapsed;
            FinishSetupPanel.Visibility = !firmware && (!completed || _reviewingSetup) ? Visibility.Visible : Visibility.Collapsed;
            CompletedOptionsPanel.Visibility = firmware ? Visibility.Collapsed : Visibility.Visible;
            VirtualOptionsPanel.Visibility = firmware ? Visibility.Collapsed : Visibility.Visible;
            HPatternNotice.Visibility = firmware ? Visibility.Visible : Visibility.Collapsed;
            FinishSetupButton.IsEnabled = ready && !Plugin.NativeBusy && !calibrating;
            VirtualChecklistSection.Visibility = Plugin.CurrentOperatingMode == OperatingMode.GenericFfbStick
                ? Visibility.Visible : Visibility.Collapsed;
            VirtualEnableSection.IsEnabled = !calibrating && (Plugin.GearOutputAvailable || _boundSettings.Enabled);
            VirtualFreeStickSection.IsEnabled = virtualAvailable && !Plugin.NativeWriteBusy;
            VirtualOutputSection.IsEnabled = !Plugin.NativeWriteBusy && !calibrating;
            VirtualClutchSection.IsEnabled = !Plugin.NativeWriteBusy;
            // Keep Cancel reachable while probes are running. The Measure button has its
            // own busy gate, while NativeWriteBusy also includes calibration itself.
            VirtualCalibrationSection.IsEnabled = virtualAvailable && !Plugin.NativeBusy && (!baseMissing || calibrating);
            TuningButtons.IsEnabled = tuningAvailable && _boundSettings.PolarityConfirmed && (!Plugin.NativeWriteBusy || Plugin.NativeProfileUpdateInProgress) && !calibrating;
            if (_tuningWindow != null && _tuningWindow.Content is Grid)
                ((UIElement)((Grid)_tuningWindow.Content).Children[0]).IsEnabled = tuningAvailable && (!Plugin.NativeWriteBusy || Plugin.NativeProfileUpdateInProgress) && !calibrating;
            if (MainTab.Visibility != Visibility.Visible) WorkspaceTabs.SelectedItem = OptionsTab;
            MainModeText.Text = "Mode: " + ModeLabel(Plugin.CurrentOperatingMode) + "   ·   Output: " + SelectedOutputName;
            MainBaseConnectionWarning.Text = BaseConnectionWarning.Text;
            MainBaseConnectionWarning.Visibility = BaseConnectionWarning.Visibility;
            TabGateText.Text = ready
                ? UsesControlMapper
                    ? "Base polarity and Control Mapper roles are configured. Check its output and game bindings, then finish setup to open Main."
                    : "Base polarity and vJoy output are ready. Finish setup to open Main."
                : BaseConnectionWarning.Visibility == Visibility.Visible ? "Base is not found. Connect it to continue setup."
                : Plugin.NativeSetupRequired ? "Use Prepare base to configure the Moza AB9 before continuing."
                : !virtualAvailable ? "Choose a virtual mode and prepare the base to continue."
                : !_boundSettings.PolarityConfirmed ? "Measure polarity before finishing setup."
                : UsesControlMapper ? "Assign at least one available Control Mapper role for this pattern."
                : "Select an available vJoy device. Check its button count for this pattern.";
        }

        private void OnMainWorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool narrow = e.NewSize.Width < 780;
            MainWorkspaceGrid.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(380);
            MainWorkspaceGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetRow(MainLivePanel, 1);
            Grid.SetColumn(MainLivePanel, narrow ? 0 : 1);
            Grid.SetRow(MainProfilePanel, narrow ? 2 : 1);
            MainProfilePanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0, 0, 16, 0);
        }

        private static string ModeLabel(OperatingMode mode)
        {
            return NativeProfilePolicy.ModeLabel(mode);
        }

        private void OnOpenOptions(object sender, RoutedEventArgs e) { WorkspaceTabs.SelectedItem = OptionsTab; }

        private void OnReviewSetup(object sender, RoutedEventArgs e)
        {
            _reviewingSetup = true;
            RefreshWorkspace();
            WorkspaceTabs.SelectedItem = OptionsTab;
        }

        private void OnFinishSetup(object sender, RoutedEventArgs e)
        {
            if (Plugin == null || _boundSettings == null || !Plugin.VirtualControlsAvailable || !_boundSettings.PolarityConfirmed || !SelectedOutputReady ||
                BaseConnectionWarning.Visibility == Visibility.Visible || Plugin.NativeBusy || _preparingCalibration ||
                (AB9ShifterPlugin.Engine != null && AB9ShifterPlugin.Engine.IsCalibrating)) return;
            Plugin.Store.SetupCompleted = true;
            Plugin.SaveStore();
            _reviewingSetup = false;
            RefreshWorkspace();
            WorkspaceTabs.SelectedItem = MainTab;
        }

        private void OnOpenGeometry(object sender, RoutedEventArgs e) { ShowTuningDialog("Geometry", GeometryDialogHolder); }
        private void OnOpenFeel(object sender, RoutedEventArgs e) { ShowTuningDialog("Feel", FeelDialogHolder); }
        private void OnOpenEffects(object sender, RoutedEventArgs e) { ShowTuningDialog("Effects", EffectsDialogHolder); }

        private void ShowTuningDialog(string title, ContentControl holder)
        {
            if (_tuningWindow != null || Plugin == null || (!Plugin.VirtualControlsAvailable && !Plugin.NativeProfileUpdateInProgress) || !_boundSettings.PolarityConfirmed) return;
            FrameworkElement panel = holder.Content as FrameworkElement;
            if (panel == null) return;
            holder.Content = null;
            var window = new Window
            {
                Title = title + " · " + Plugin.Store.ActiveProfile + " · AB9 Active Shifter",
                Width = Math.Min(980, SystemParameters.WorkArea.Width - 60),
                Height = Math.Min(900, SystemParameters.WorkArea.Height - 60),
                MinWidth = 660,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = WorkspaceBackground(),
                Foreground = Foreground
            };
            Window owner = Window.GetWindow(this);
            if (owner != null) window.Owner = owner;
            window.Resources.MergedDictionaries.Add(Resources);
            window.SetBinding(DataContextProperty, new Binding("DataContext") { Source = this });
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var editor = new AdornerDecorator { Child = panel };
            shell.Children.Add(editor);
            var done = new Button { Content = "Done", Padding = new Thickness(24, 7, 24, 7), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
            done.Click += (s, e) => window.Close();
            Grid.SetRow(done, 1);
            shell.Children.Add(done);
            window.Content = shell;
            window.Loaded += (s, e) =>
            {
                foreach (var slider in FindTitledSliders(panel))
                {
                    string property;
                    if (_propertyBySlider.TryGetValue(slider, out property)) RefreshDirtyMarker(property);
                }
            };
            if (ReferenceEquals(holder, FeelDialogHolder))
            {
                // One read per opening, after the editor is visible. Staying in Feel (or on
                // Main) must not keep reopening the configuration port while driving.
                EventHandler refreshOnOpen = null;
                refreshOnOpen = (s, e) =>
                {
                    window.ContentRendered -= refreshOnOpen;
                    RefreshNativeHardware();
                };
                window.ContentRendered += refreshOnOpen;
            }
            _tuningWindow = window;
            try { window.ShowDialog(); }
            finally
            {
                foreach (var slider in FindTitledSliders(panel)) SetDirty(slider, false);
                editor.Child = null;
                window.Content = null;
                holder.Content = panel;
                _tuningWindow = null;
            }
        }

        private Brush WorkspaceBackground()
        {
            for (DependencyObject current = this; current != null; current = VisualTreeHelper.GetParent(current))
            {
                Brush brush = (current as Control)?.Background ?? (current as Panel)?.Background
                    ?? (current as Border)?.Background;
                if (brush != null && (!(brush is SolidColorBrush) || ((SolidColorBrush)brush).Color.A > 0))
                    return brush;
            }
            return SystemColors.ControlBrush;
        }

        private void BindGeometryUnits()
        {
            // Explicit source survives moving the existing controls into a modal namescope.
            // IndexSliders still runs once over the complete precreated logical tree.
            foreach (var slider in FindTitledSliders(this))
            {
                BindingExpression expression = BindingOperations.GetBindingExpression(slider, VisibilityProperty);
                Binding binding = expression != null ? expression.ParentBinding : null;
                if (binding == null || binding.ElementName != "GeometryPercentToggle") continue;
                BindingOperations.SetBinding(slider, VisibilityProperty, new Binding("IsChecked")
                {
                    Source = GeometryPercentToggle,
                    Converter = binding.Converter
                });
            }
        }
    }
}
