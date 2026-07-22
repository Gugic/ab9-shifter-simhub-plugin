using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// Draws the 7+R gate with the lockout band shaded and the live stick position on top.
    /// Reads the engine snapshot on a timer; it never touches the device.
    /// </summary>
    public sealed class GateVisualizer : FrameworkElement
    {
        private const double Padding = 26;
        private const int AxisMax = GateGeometry.AxisMax;

        private static readonly Brush GateBrush = MakeBrush(Color.FromRgb(0x5A, 0x5F, 0x6A));
        private static readonly Brush LockoutBrush = MakeBrush(Color.FromArgb(0x40, 0xE8, 0x8A, 0x1A));
        private static readonly Brush LockoutEdgeBrush = MakeBrush(Color.FromRgb(0xE8, 0x8A, 0x1A));
        private static readonly Brush LabelBrush = MakeBrush(Color.FromRgb(0x9A, 0xA0, 0xAC));
        private static readonly Brush ActiveBrush = MakeBrush(Color.FromRgb(0x36, 0xC7, 0x6A));
        private static readonly Brush StickBrush = MakeBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
        private static readonly Brush DisconnectedBrush = MakeBrush(Color.FromRgb(0xC7, 0x3B, 0x3B));

        private static readonly Pen GatePen = MakePen(GateBrush, 10);
        private static readonly Pen ActivePen = MakePen(ActiveBrush, 10);
        private static readonly Pen LockoutEdgePen = MakePen(LockoutEdgeBrush, 2);

        private readonly DispatcherTimer _timer;
        private EngineSnapshot _snapshot = new EngineSnapshot();
        private ShifterSettings _settings;

        private static readonly string[] ForwardLabels = { "1", "3", "5", "7" };
        private static readonly string[] BackLabels = { "2", "4", "6", "R" };

        public GateVisualizer()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timer.Tick += OnTick;

            Loaded += (s, e) => _timer.Start();
            Unloaded += (s, e) => _timer.Stop();
        }

        public void Attach(ShifterSettings settings)
        {
            _settings = settings;
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
            return new Size(width, 300);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 2 * Padding || h <= 2 * Padding) return;

            // Transparent hit area keeps the element sized inside the layout.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            double left = Padding;
            double right = w - Padding;
            double top = Padding;
            double bottom = h - Padding;
            double midY = (top + bottom) / 2;

            EngineSnapshot snap = _snapshot;
            int activeColumn = snap.Column == Column.None ? -1 : (int)snap.Column;

            DrawLockoutBand(dc, left, right, top, bottom);

            // Neutral channel.
            dc.DrawLine(GatePen, new Point(left, midY), new Point(right, midY));

            // Column slots, with the engaged one highlighted.
            for (int i = 0; i < GateGeometry.ColumnCount; i++)
            {
                double x = MapX(i * (double)AxisMax / (GateGeometry.ColumnCount - 1), left, right);
                bool active = i == activeColumn && snap.Gear > 0;
                dc.DrawLine(active ? ActivePen : GatePen, new Point(x, top), new Point(x, bottom));

                DrawLabel(dc, ForwardLabels[i], x, top - 16, IsGearLit(snap, i, ShiftDir.Fwd));
                DrawLabel(dc, BackLabels[i], x, bottom + 4, IsGearLit(snap, i, ShiftDir.Back));
            }

            DrawStick(dc, snap, left, right, top, bottom);
        }

        private void DrawLockoutBand(DrawingContext dc, double left, double right, double top, double bottom)
        {
            int lockoutStart = _settings != null ? _settings.LockoutStart : 48000;
            double x = MapX(lockoutStart, left, right);
            if (x >= right) return;

            dc.DrawRectangle(LockoutBrush, null, new Rect(x, top - 8, right - x + 8, bottom - top + 16));
            dc.DrawLine(LockoutEdgePen, new Point(x, top - 8), new Point(x, bottom + 8));

            FormattedText text = Text("LOCKOUT", 10, LockoutEdgeBrush);
            dc.DrawText(text, new Point(x + 6, top - 8 - text.Height - 2));
        }

        private void DrawStick(DrawingContext dc, EngineSnapshot snap, double left, double right, double top, double bottom)
        {
            bool live = snap.DeviceConnected;
            double px = MapX(snap.X, left, right);
            double py = top + (bottom - top) * (snap.Y / (double)AxisMax);

            Brush fill = live ? StickBrush : DisconnectedBrush;
            dc.DrawEllipse(fill, null, new Point(px, py), 8, 8);

            string label = live ? snap.GearLabel : "--";
            FormattedText gear = Text(label, 30, snap.Gear > 0 ? ActiveBrush : LabelBrush, true);
            dc.DrawText(gear, new Point(right - gear.Width, top + 2));
        }

        private bool IsGearLit(EngineSnapshot snap, int columnIndex, ShiftDir dir)
        {
            bool mirrorColumns = _settings != null && _settings.MirrorColumns;
            bool mirrorSlots = _settings != null && _settings.MirrorSlots;
            return snap.Gear > 0 &&
                   snap.Gear == GateGeometry.GearOf((Column)columnIndex, dir, mirrorColumns, mirrorSlots);
        }

        private void DrawLabel(DrawingContext dc, string text, double centerX, double y, bool lit)
        {
            FormattedText t = Text(text, 13, lit ? ActiveBrush : LabelBrush, lit);
            dc.DrawText(t, new Point(centerX - t.Width / 2, y));
        }

        private static double MapX(double axisValue, double left, double right)
        {
            return left + (right - left) * (axisValue / AxisMax);
        }

        private FormattedText Text(string value, double size, Brush brush, bool bold = false)
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

        private static Brush MakeBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen MakePen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }
    }
}
