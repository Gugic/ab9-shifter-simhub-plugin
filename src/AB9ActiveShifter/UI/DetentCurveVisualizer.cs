using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// Plots the slot detent's force against how far the stick has travelled into a gear -
    /// resist, then the crossover into the pull, then the seated hold - for the Feel tab's
    /// SLOT DETENT section. Samples ForceComposer.DetentMagnitude directly across the fraction
    /// it already takes, rather than reimplementing the shape, so the plot cannot drift from
    /// what a real shift renders. A live dot tracks the stick's actual position on the curve,
    /// read from the engine snapshot on the same timer/interval GateVisualizer polls its own
    /// live stick position with.
    /// </summary>
    public sealed class DetentCurveVisualizer : FrameworkElement
    {
        private const double Padding = 34;
        private const double TopLabelSpace = 14;
        private const double MaxFraction = 1.2;
        private const int Samples = 121;

        private static readonly Brush AxisBrush = MakeBrush(Color.FromRgb(0x5A, 0x5F, 0x6A));
        private static readonly Brush CurveBrush = MakeBrush(Color.FromRgb(0x36, 0xC7, 0x6A));
        private static readonly Brush GuideBrush = MakeBrush(Color.FromRgb(0x6A, 0x70, 0x7C));
        private static readonly Brush LabelBrush = MakeBrush(Color.FromRgb(0x9A, 0xA0, 0xAC));
        private static readonly Brush StickBrush = MakeBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));

        private static readonly Pen AxisPen = MakePen(AxisBrush, 1.5, false);
        private static readonly Pen CurvePen = MakePen(CurveBrush, 3, false);
        private static readonly Pen GuidePen = MakePen(GuideBrush, 1, true);

        private readonly DispatcherTimer _timer;
        private EngineSnapshot _snapshot = new EngineSnapshot();
        private ShifterSettings _settings;

        public DetentCurveVisualizer()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += OnTick;

            Loaded += (s, e) => _timer.Start();
            Unloaded += (s, e) => _timer.Stop();
        }

        public void Attach(ShifterSettings settings)
        {
            if (_settings != null) _settings.PropertyChanged -= OnSettingsChanged;
            _settings = settings;
            if (_settings != null) _settings.PropertyChanged += OnSettingsChanged;
            InvalidateVisual();
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            InvalidateVisual();
        }

        private void OnTick(object sender, EventArgs e)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            _snapshot = engine != null ? engine.Snapshot : new EngineSnapshot();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 460 : Math.Min(availableSize.Width, 620);
            return new Size(width, 220);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 2 * Padding || h <= 2 * Padding + TopLabelSpace) return;

            // Transparent hit area keeps the element sized inside the layout, same as GateVisualizer.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            double left = Padding;
            double right = w - Padding;
            double top = Padding + TopLabelSpace;
            double bottom = h - Padding;
            double midY = (top + bottom) / 2;

            DrawLabel(dc, "0", left - 10, midY - 7, TextAlign.Right);
            DrawLabel(dc, "+full", left - 10, top - 7, TextAlign.Right);
            DrawLabel(dc, "-full", left - 10, bottom - 7, TextAlign.Right);
            dc.DrawLine(AxisPen, new Point(left, midY), new Point(right, midY));

            DrawFractionGuide(dc, 0.0, "centre", left, right, top, bottom);
            DrawFractionGuide(dc, 0.55, "crossover", left, right, top, bottom);
            DrawFractionGuide(dc, 0.80, "seated", left, right, top, bottom);
            DrawFractionGuide(dc, 1.00, "engaged", left, right, top, bottom);

            DrawLabel(dc, "shape at full gain - see Overall gain for felt force", right, top - TopLabelSpace, TextAlign.Right);

            if (_settings == null) return;

            // The resist/pull/hold ratios and the crossover geometry are set entirely by this
            // section's three sliders; Overall gain and the unconfirmed-polarity cap scale the
            // whole gate uniformly afterward, not this shape specifically. Plotting at the
            // rig's actual (often 10%-capped) gain left the curve indistinguishable from flat
            // for any realistic tuning - confirmed by rendering it offscreen before deploying,
            // rather than only after asking for it to be checked on hardware.
            EngineConfig cfg = _settings.ToEngineConfig();
            GateGeometry geo = cfg.BuildGeometry();
            cfg.OverallGainPct = 100;
            cfg.PolarityConfirmed = true;
            ForceComposer composer = new ForceComposer(geo, cfg);

            Point[] points = new Point[Samples];
            for (int i = 0; i < Samples; i++)
            {
                double fraction = MaxFraction * i / (Samples - 1);
                int force = composer.DetentMagnitude(ShiftDir.Fwd, fraction, muted: false);
                points[i] = new Point(MapX(fraction, left, right), MapY(force, top, bottom));
            }

            for (int i = 1; i < points.Length; i++)
            {
                dc.DrawLine(CurvePen, points[i - 1], points[i]);
            }

            // Only while the stick is actually inside a column: in the neutral channel the
            // fore/aft force is the gate wall (ComposeNeutral), not this curve at all, and a
            // dot moving here while sliding along the tunnel would show motion against a shape
            // that has nothing to do with what the hand is actually feeling at that moment.
            EngineSnapshot snap = _snapshot;
            if (snap.State != GateState.Neutral && snap.Column != Column.None)
            {
                ShiftDir dir = geo.DirectionOf(snap.Y);
                double liveFraction = geo.EngageFraction(dir, snap.Y);
                int liveForce = composer.DetentMagnitude(ShiftDir.Fwd, liveFraction, muted: false);
                Point livePoint = new Point(MapX(liveFraction, left, right), MapY(liveForce, top, bottom));
                dc.DrawEllipse(StickBrush, null, livePoint, 6, 6);
            }
        }

        private void DrawFractionGuide(DrawingContext dc, double fraction, string label, double left, double right, double top, double bottom)
        {
            double x = MapX(fraction, left, right);
            dc.DrawLine(GuidePen, new Point(x, top), new Point(x, bottom));
            DrawLabel(dc, label, x, bottom + 4, TextAlign.Center);
        }

        private static double MapX(double fraction, double left, double right)
        {
            return left + (right - left) * (fraction / MaxFraction);
        }

        private static double MapY(int force, double top, double bottom)
        {
            double t = GateGeometry.Clamp(force / (double)GateGeometry.ForceMax, -1.0, 1.0);
            double mid = (top + bottom) / 2;
            return mid - t * (mid - top);
        }

        private enum TextAlign { Left, Center, Right }

        private void DrawLabel(DrawingContext dc, string text, double x, double y, TextAlign align)
        {
            FormattedText t = Text(text, 10, LabelBrush);
            double drawX = align == TextAlign.Center ? x - t.Width / 2 : align == TextAlign.Right ? x - t.Width : x;
            dc.DrawText(t, new Point(drawX, y));
        }

        private FormattedText Text(string value, double size, Brush brush)
        {
            return new FormattedText(
                value,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private static Brush MakeBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen MakePen(Brush brush, double thickness, bool dashed)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dashed) pen.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
            pen.Freeze();
            return pen;
        }
    }
}
