using System;
using System.Windows;
using AB9ActiveShifter.Core;
using SimHub.Plugins.UI;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// SimHub's slider with a separate raw binding. Rendering/coercing a 0-100 display must
    /// never write into a profile; only a user's value change or reset takes that path.
    /// </summary>
    public sealed class ScaledTitledSlider : TitledSlider
    {
        public static readonly DependencyProperty RawValueProperty = DependencyProperty.Register(
            nameof(RawValue), typeof(double), typeof(ScaledTitledSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRawChanged));
        public static readonly DependencyProperty RawMinimumProperty = DependencyProperty.Register(
            nameof(RawMinimum), typeof(double), typeof(ScaledTitledSlider), new PropertyMetadata(0.0, OnScaleChanged));
        public static readonly DependencyProperty RawMaximumProperty = DependencyProperty.Register(
            nameof(RawMaximum), typeof(double), typeof(ScaledTitledSlider), new PropertyMetadata(100.0, OnScaleChanged));
        public static readonly DependencyProperty RawResetValueProperty = DependencyProperty.Register(
            nameof(RawResetValue), typeof(double), typeof(ScaledTitledSlider), new PropertyMetadata(0.0, OnScaleChanged));
        public static readonly DependencyProperty RawStepProperty = DependencyProperty.Register(
            nameof(RawStep), typeof(double), typeof(ScaledTitledSlider), new PropertyMetadata(1.0, OnScaleChanged));
        public static readonly DependencyProperty NormalizeProperty = DependencyProperty.Register(
            nameof(Normalize), typeof(bool), typeof(ScaledTitledSlider), new PropertyMetadata(true, OnScaleChanged));
        private static readonly DependencyPropertyKey OutsideScaleKey = DependencyProperty.RegisterReadOnly(
            nameof(HasValueOutsideScale), typeof(bool), typeof(ScaledTitledSlider), new PropertyMetadata(false));
        public static readonly DependencyProperty HasValueOutsideScaleProperty = OutsideScaleKey.DependencyProperty;

        private bool _ready;
        private bool _updating;

        public ScaledTitledSlider()
        {
            StringFormat = "0.#";
            IsSnapToTickEnabled = false;
            _ready = true;
            RefreshDisplay();
        }

        public double RawValue { get { return (double)GetValue(RawValueProperty); } set { SetValue(RawValueProperty, value); } }
        public double RawMinimum { get { return (double)GetValue(RawMinimumProperty); } set { SetValue(RawMinimumProperty, value); } }
        public double RawMaximum { get { return (double)GetValue(RawMaximumProperty); } set { SetValue(RawMaximumProperty, value); } }
        public double RawResetValue { get { return (double)GetValue(RawResetValueProperty); } set { SetValue(RawResetValueProperty, value); } }
        public double RawStep { get { return (double)GetValue(RawStepProperty); } set { SetValue(RawStepProperty, value); } }
        public bool Normalize { get { return (bool)GetValue(NormalizeProperty); } set { SetValue(NormalizeProperty, value); } }
        public bool HasValueOutsideScale { get { return (bool)GetValue(HasValueOutsideScaleProperty); } }

        public double ToDisplayValue(double raw)
        {
            return Normalize ? TuningScale.SnapDisplay(Scale.ToDisplay(raw)) : raw;
        }

        public void RestoreRawValue(double raw)
        {
            SetCurrentValue(RawValueProperty, raw);
            RefreshDisplay();
        }

        private TuningScale Scale { get { return new TuningScale(RawMinimum, RawMaximum); } }

        private static void OnRawChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            ((ScaledTitledSlider)sender).RefreshDisplay();
        }

        private static void OnScaleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            ((ScaledTitledSlider)sender).RefreshDisplay();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (!_ready || _updating || e.Property != ValueProperty) return;
            double edited = (double)e.NewValue;
            // Reset keeps its exact raw default, even when that default's display rounds to
            // the same tenth as a nearby setting. Undo likewise uses RestoreRawValue.
            bool reset = Math.Abs(edited - ResetValue) < 1e-10 && Scale.Contains(RawResetValue);
            double raw = reset ? RawResetValue
                : Normalize ? Scale.ToRaw(TuningScale.SnapDisplay(edited), RawStep)
                : Math.Round(edited / RawStep) * RawStep;
            SetCurrentValue(RawValueProperty, raw);
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (!_ready || _updating || RawMinimum == RawMaximum) return;
            _updating = true;
            try
            {
                Minimum = Normalize ? 0 : Math.Min(RawMinimum, RawMaximum);
                Maximum = Normalize ? 100 : Math.Max(RawMinimum, RawMaximum);
                InputInterval = Normalize ? Scale.DisplayIncrement(RawStep) : RawStep;
                ResetValue = Normalize ? Scale.ToDisplay(RawResetValue) : RawResetValue;
                SetCurrentValue(ValueProperty, ToDisplayValue(RawValue));
                SetValue(OutsideScaleKey, !Scale.Contains(RawValue));
            }
            finally { _updating = false; }
        }
    }
}
