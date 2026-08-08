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
    /// Shared scaffolding for every visualization in the settings UI - the Feel tab's small
    /// force-curve graphs (DetentCurveVisualizer and its siblings) and the gate plan view
    /// (GateVisualizer): settings-driven redraw, a 33 ms/DispatcherPriority.Render live-snapshot
    /// poll, and the axis/label drawing primitives common to all of them. Extracted once a second
    /// visualizer needed the identical boilerplate, rather than upfront - verified to render
    /// pixel-identically to the pre-extraction version before anything was built on top of it.
    ///
    /// A subclass overrides DrawGraph with whatever it samples and however its own axes are
    /// scaled; this base only owns what every one of them needs regardless of shape.
    /// </summary>
    public abstract class ForceGraphVisualizerBase : FrameworkElement
    {
        protected const double Padding = 34;
        protected const double TopLabelSpace = 14;

        protected static readonly Brush AxisBrush = MakeBrush(Color.FromRgb(0x5A, 0x5F, 0x6A));
        protected static readonly Brush CurveBrush = MakeBrush(Color.FromRgb(0x36, 0xC7, 0x6A));
        protected static readonly Brush GuideBrush = MakeBrush(Color.FromRgb(0x6A, 0x70, 0x7C));
        protected static readonly Brush LabelBrush = MakeBrush(Color.FromRgb(0x9A, 0xA0, 0xAC));
        protected static readonly Brush StickBrush = MakeBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));

        protected static readonly Pen AxisPen = MakePen(AxisBrush, 1.5, false);
        protected static readonly Pen CurvePen = MakePen(CurveBrush, 3, false);
        protected static readonly Pen GuidePen = MakePen(GuideBrush, 1, true);

        private readonly DispatcherTimer _timer;
        private EngineSnapshot _snapshot = new EngineSnapshot();
        protected ShifterSettings Settings;

        protected ForceGraphVisualizerBase()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += OnTick;

            Loaded += (s, e) => _timer.Start();
            Unloaded += (s, e) => _timer.Stop();
        }

        public void Attach(ShifterSettings settings)
        {
            if (Settings != null) Settings.PropertyChanged -= OnSettingsChanged;
            Settings = settings;
            if (Settings != null) Settings.PropertyChanged += OnSettingsChanged;
            InvalidateVisual();
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            InvalidateVisual();
        }

        private void OnTick(object sender, EventArgs e)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            EngineSnapshot snap = engine != null ? engine.Snapshot : new EngineSnapshot();

            // Redraw only when something a graph actually draws has moved. Every OnRender here
            // rebuilds an EngineConfig, a GateGeometry and a ForceComposer, and the Feel tab
            // shows several of these at once - so an unconditional 30 Hz invalidate meant a
            // steady stream of allocations on the UI thread of a process whose other thread is
            // holding a 1 kHz deadline. A gen0 collection pauses that thread too. With a hand
            // off the stick nothing moves, which is most of the time a settings page is open.
            bool moved = snap.X != _snapshot.X
                         || snap.Y != _snapshot.Y
                         || snap.State != _snapshot.State
                         || snap.Column != _snapshot.Column
                         || snap.Gear != _snapshot.Gear
                         || snap.DeviceConnected != _snapshot.DeviceConnected;

            _snapshot = snap;
            if (moved) InvalidateVisual();
        }

        /// <summary>The live engine snapshot as of the last poll tick - never touches the device itself.</summary>
        protected EngineSnapshot Snapshot { get { return _snapshot; } }

        protected virtual double GraphHeight { get { return 220; } }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 460 : Math.Min(availableSize.Width, 620);
            return new Size(width, GraphHeight);
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

            DrawGraph(dc, left, right, top, bottom);
        }

        /// <summary>Draws everything past the shared padding/background - axes, curve, guides, live dot.</summary>
        protected abstract void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom);

        protected static double MapLinear(double value, double domainMin, double domainMax, double rangeMin, double rangeMax)
        {
            double span = domainMax - domainMin;
            double t = span > 0 ? (value - domainMin) / span : 0.0;
            return rangeMin + (rangeMax - rangeMin) * GateGeometry.Clamp(t, 0.0, 1.0);
        }

        /// <summary>Maps a signed force onto a vertical axis whose centre (0 force) sits at the midpoint between top and bottom.</summary>
        protected static double MapForceBidirectional(int force, double top, double bottom)
        {
            double t = GateGeometry.Clamp(force / (double)GateGeometry.ForceMax, -1.0, 1.0);
            double mid = (top + bottom) / 2;
            return mid - t * (mid - top);
        }

        /// <summary>Maps an unsigned force magnitude onto a vertical axis running from 0 force at the bottom to full scale at the top.</summary>
        protected static double MapForceMagnitude(int force, double top, double bottom)
        {
            double t = GateGeometry.Clamp(Math.Abs(force) / (double)GateGeometry.ForceMax, 0.0, 1.0);
            return bottom - t * (bottom - top);
        }

        protected void DrawVerticalGuide(DrawingContext dc, double x, string label, double top, double bottom)
        {
            dc.DrawLine(GuidePen, new Point(x, top), new Point(x, bottom));
            DrawLabel(dc, label, x, bottom + 4, TextAlign.Center);
        }

        protected enum TextAlign { Left, Center, Right }

        protected void DrawLabel(DrawingContext dc, string text, double x, double y, TextAlign align)
        {
            FormattedText t = Text(text, 10, LabelBrush);
            double drawX = align == TextAlign.Center ? x - t.Width / 2 : align == TextAlign.Right ? x - t.Width : x;
            dc.DrawText(t, new Point(drawX, y));
        }

        protected FormattedText Text(string value, double size, Brush brush, bool bold = false)
        {
            return new FormattedText(
                value,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"),
                             FontStyles.Normal,
                             bold ? FontWeights.Bold : FontWeights.Normal,
                             FontStretches.Normal),
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        protected static Brush MakeBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        protected static Pen MakePen(Brush brush, double thickness, bool dashed)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dashed) pen.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
            pen.Freeze();
            return pen;
        }
    }
}
