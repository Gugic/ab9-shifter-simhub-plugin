using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AB9ActiveShifter.Core;
using GameReaderCommon;
using GameReaderCommon.Enums;
using GameReaderCommon.Feedback;
using Newtonsoft.Json;
using SimHub.Plugins.DataPlugins.ShakeItV3.Calibration;
using SimHub.Plugins.DataPlugins.ShakeItV3.Effects;
using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers.Attributes;
using SimHub.Plugins.DataPlugins.ShakeItV3.Filters;
using SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.Audio;
using SimHub.Plugins.Styles;
using SimHub.Plugins.UI;

namespace AB9ActiveShifter.Effects
{
    /// <summary>
    /// Shifter-specific sources inside native effect rows. Only the source/gear protection is
    /// ours: native filters, graphs, tests, gain, frequency, priority and mapping own the feedback.
    /// </summary>
    public abstract class AB9EffectContainer : EffectsContainerBase<SingleToneOutput>
    {
        private readonly EffectBase _effect;
        private readonly FilterBase _filter;

        protected AB9EffectContainer(bool pulse)
        {
            _filter = pulse ? (FilterBase)new PulseFilter { Duration = 60 } : new SimpleGammaFilter();
            _effect = new SourceEffect(this);
        }

        protected override IEnumerable<EffectBase> AvailableEffects { get { yield return _effect; } }
        public override FilterBase Filter { get { return _filter; } }
        [JsonIgnore] public NativeEffectsSettings NativeSettings { get { return ParentProfile == null ? null : ParentProfile.Settings as NativeEffectsSettings; } }
        [JsonIgnore] public ShifterSettings Owner { get { return NativeSettings == null ? null : NativeSettings.Owner; } }

        public override void SetAudioDefaults(SingleToneOutput output) { output.Frequency = 50; }
        protected abstract double ReadValue(GameData data);
        protected virtual void ResetSource() { }

        private sealed class SourceEffect : EffectBase
        {
            private readonly AB9EffectContainer _container;
            public SourceEffect(AB9EffectContainer container) : base("Lever", FFBPlacement.All) { _container = container; }
            public override bool GetIsSupportedOnCurrentGame(FeedbackCapabilities caps) { return true; }
            public override double GetEffectValue(GameData data, CalibratedFeedbackData feedback, EffectsContainerBase container)
            { return _container.ReadValue(data); }
            public override void Idle() { base.Idle(); _container.ResetSource(); }
            public override void ResetData() { base.ResetData(); _container.ResetSource(); }
        }

        protected UserControl Editor(params FrameworkElement[] controls)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 12, 8), DataContext = Owner };
            foreach (FrameworkElement control in controls) panel.Children.Add(control);
            return new UserControl { Content = panel };
        }

        protected static TextBlock Note(string text)
        { return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 8), MaxWidth = 500 }; }

        protected static TitledSlider Slider(string title, string property, double min, double max)
        {
            var slider = new TitledSlider { Title = title, Minimum = min, Maximum = max, TickFrequency = 1 };
            slider.SetBinding(TitledSlider.ValueProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            return slider;
        }
    }

    [ShakeItContainerMetadata(1, "Clutch grind", "Feedback when an H-pattern shift meets the clutch protection", "AB9 shifter")]
    public sealed class AB9GrindEffectContainer : AB9EffectContainer
    {
        public AB9GrindEffectContainer() : base(false) { Gain = 60; }
        protected override double ReadValue(GameData data)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            return engine == null || Owner == null || !Owner.IsHPattern ? 0 : engine.GrindEffectLevel;
        }
        public override void SetAudioDefaults(SingleToneOutput output) { output.Frequency = 33; }
        protected override UserControl GetEditControl()
        {
            var reject = new SHToggleCheckbox { Content = "Reject the gear while grinding (registers only once the clutch is down)" };
            reject.SetBinding(CheckBox.IsCheckedProperty, new Binding("GrindRejectsGear") { Mode = BindingMode.TwoWay });
            var mode = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
            mode.Items.Add("One threshold - grind or no grind");
            mode.Items.Add("Fade across the pedal from the bite point");
            mode.SetBinding(ComboBox.SelectedIndexProperty, new Binding("GrindClutchModeIndex") { Mode = BindingMode.TwoWay });
            TitledSlider threshold = Slider("Clutch counts as pressed above (%)", "GrindClutchThresholdPct", 5, 90);
            void refresh() { threshold.Visibility = Owner != null && Owner.GrindClutchMode == GrindClutchMode.Progressive ? Visibility.Collapsed : Visibility.Visible; }
            mode.SelectionChanged += delegate { refresh(); };
            threshold.Loaded += delegate { refresh(); };
            return Editor(Note("H-pattern only. Push into a gear with the clutch up to feel the grind. Gear rejection and the balk wall follow the lever immediately; volume, frequency and priority are tuned in this effect row. The fading mode uses the bite point on Setup."),
                reject, Slider("Balk wall (%)", "GrindWallPct", 0, 100), Note("How the clutch decides"), mode,
                threshold, Slider("Only grind above (km/h)", "GrindMinSpeedKmh", 0, 40));
        }
    }

    [ShakeItContainerMetadata(2, "Clutch bite point", "A pulse when the clutch crosses the bite point in either direction", "AB9 shifter")]
    public sealed class AB9BiteEffectContainer : AB9EffectContainer
    {
        private bool _seeded;
        private int _sequence;
        public AB9BiteEffectContainer() : base(true) { Gain = 35; }
        protected override double ReadValue(GameData data)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            if (engine == null) return 0;
            int sequence = engine.BiteEffectSequence;
            bool changed = _seeded && sequence != _sequence;
            _sequence = sequence;
            _seeded = true;
            return changed ? 1 : 0;
        }
        protected override void ResetSource() { _seeded = false; }
        protected override UserControl GetEditControl()
        { return Editor(Note("Set the car's clutch bite point on Setup. A crossing in either direction triggers this effect; its pulse duration is tuned in Response filter.")); }
    }

    [ShakeItContainerMetadata(3, "Rev limiter", "Feedback once RPM reaches a share of the game's reported redline", "AB9 shifter")]
    public sealed class AB9LimiterEffectContainer : AB9EffectContainer
    {
        public AB9LimiterEffectContainer() : base(false) { Gain = 45; }
        protected override double ReadValue(GameData data)
        {
            var telemetry = data == null ? null : data.NewData;
            return Owner != null && telemetry != null && telemetry.MaxRpm >= 1000
                && telemetry.Rpms >= telemetry.MaxRpm * Owner.FxLimiterFromPct / 100.0 ? 1 : 0;
        }
        public override void SetAudioDefaults(SingleToneOutput output) { output.Frequency = 55; }
        protected override UserControl GetEditControl()
        { return Editor(Slider("Starts at (% of redline)", "FxLimiterFromPct", 80, 100), Note("Silent when the game supplies no plausible redline.")); }
    }

    [ShakeItContainerMetadata(4, "Custom property", "A SimHub property scaled 0-100 drives the effect's strength", "AB9 shifter")]
    public sealed class AB9PropertyEffectContainer : AB9EffectContainer
    {
        public AB9PropertyEffectContainer() : base(false) { Gain = 35; }
        protected override double ReadValue(GameData data)
        { return NativeSettings == null ? 0 : GateGeometry.Clamp(NativeSettings.SampledCustomValue, 0, 100) / 100; }
        protected override UserControl GetEditControl()
        {
            var property = new TextBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
            property.SetBinding(TextBox.TextProperty, new Binding("FxCustomProperty") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return Editor(Note("Property name (0-100)"), property, Note("Any SimHub property can drive this row. Use Custom effect from Add effect for native formulas and advanced sources."));
        }
    }
}
