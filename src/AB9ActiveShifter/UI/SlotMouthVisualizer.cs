using System;
using System.Windows;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// Draws one slot's corridor from above - depth into the slot along X, lateral room either
    /// side of the column's centreline along Y - for the Feel tab's SLOT MOUTHS section. Unlike
    /// the force-curve visualizations, there is no single scalar output here: the shape itself
    /// is the point, so this samples ForceComposer.SlotCorridorHalfWidthAt directly for both
    /// flanks rather than reimplementing MouthExtra's own funnel geometry.
    ///
    /// Illustrates a representative column (the live one if the stick is actually in a
    /// column's territory, otherwise C2/gear 3-4) rather than every column at once - the
    /// clamped opening near the lockout can differ slightly, but one honest cross-section
    /// says more than an attempt to overlay several at once would.
    /// </summary>
    public sealed class SlotMouthVisualizer : ForceGraphVisualizerBase
    {
        private static readonly Brush CorridorFillBrush = MakeBrush(Color.FromArgb(0x30, 0x36, 0xC7, 0x6A));

        protected override void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom)
        {
            DrawLabel(dc, "shown from above: depth right, lateral room up/down", right, top - TopLabelSpace, TextAlign.Right);

            if (Settings == null) return;

            EngineConfig cfg = Settings.ToEngineConfig();
            GateGeometry geo = cfg.BuildGeometry();
            ForceComposer composer = new ForceComposer(geo, cfg);

            // The live column if the stick is actually associated with one; otherwise a
            // representative inner column that holds a gear on every pattern, so the default
            // view is never a missing slot's flat, uninformative line.
            EngineSnapshot snap = Snapshot;
            bool live = snap.DeviceConnected && snap.Column != Column.None;
            Column column = live ? snap.Column : Column.C2;
            ShiftDir dir = live ? geo.DirectionOf(snap.Y) : ShiftDir.Fwd;

            string gearLabel = geo.LabelFor(geo.GearFor(column, ShiftDir.Fwd)) + "/" + geo.LabelFor(geo.GearFor(column, ShiftDir.Back));
            DrawLabel(dc, "gear " + gearLabel, left, top - TopLabelSpace, TextAlign.Left);

            double domainMin = 0;
            double domainMax = geo.ChannelHalfEnter + Math.Max(1, cfg.MouthDepth) + 500;

            const int samples = 121;
            double[] depths = new double[samples];
            int[] upper = new int[samples];
            int[] lower = new int[samples];
            int maxWidth = 1;
            for (int i = 0; i < samples; i++)
            {
                double depth = domainMin + (domainMax - domainMin) * i / (samples - 1);
                depths[i] = depth;
                upper[i] = composer.SlotCorridorHalfWidthAt(column, +1, (int)Math.Round(depth), CenterYFor(geo, dir, depth));
                lower[i] = composer.SlotCorridorHalfWidthAt(column, -1, (int)Math.Round(depth), CenterYFor(geo, dir, depth));
                maxWidth = Math.Max(maxWidth, Math.Max(upper[i], lower[i]));
            }

            double yDomain = maxWidth * 1.15;

            DrawLabel(dc, "0", left - 10, (top + bottom) / 2 - 7, TextAlign.Right);
            dc.DrawLine(AxisPen, new Point(left, (top + bottom) / 2), new Point(right, (top + bottom) / 2));

            DrawVerticalGuide(dc, MapX(geo.ChannelHalfEnter, left, right, domainMin, domainMax), "tunnel edge", top, bottom);
            DrawVerticalGuide(dc, MapX(geo.ChannelHalfEnter + cfg.MouthDepth, left, right, domainMin, domainMax), "mouth ends", top, bottom);

            Point[] upperPoints = new Point[samples];
            Point[] lowerPoints = new Point[samples];
            for (int i = 0; i < samples; i++)
            {
                double x = MapX(depths[i], left, right, domainMin, domainMax);
                upperPoints[i] = new Point(x, MapY(upper[i], top, bottom, yDomain));
                lowerPoints[i] = new Point(x, MapY(-lower[i], top, bottom, yDomain));
            }

            StreamGeometry fill = new StreamGeometry();
            using (StreamGeometryContext ctx = fill.Open())
            {
                ctx.BeginFigure(upperPoints[0], true, true);
                ctx.PolyLineTo(upperPoints, true, false);
                Point[] lowerReversed = new Point[samples];
                for (int i = 0; i < samples; i++) lowerReversed[i] = lowerPoints[samples - 1 - i];
                ctx.PolyLineTo(lowerReversed, true, false);
            }
            fill.Freeze();
            dc.DrawGeometry(CorridorFillBrush, null, fill);

            for (int i = 1; i < samples; i++)
            {
                dc.DrawLine(CurvePen, upperPoints[i - 1], upperPoints[i]);
                dc.DrawLine(CurvePen, lowerPoints[i - 1], lowerPoints[i]);
            }

            if (live)
            {
                double liveDepth = Math.Abs(snap.Y - GateGeometry.AxisCenter);
                double liveOffset = snap.X - geo.ColumnTarget(column);
                Point livePoint = new Point(
                    MapX(liveDepth, left, right, domainMin, domainMax),
                    MapY(liveOffset, top, bottom, yDomain));
                dc.DrawEllipse(StickBrush, null, livePoint, 6, 6);
            }
        }

        /// <summary>A y with the right depth and side of centre for DirectionOf/SlotExists to read correctly, without needing the real live y for the static sweep.</summary>
        private static int CenterYFor(GateGeometry geo, ShiftDir dir, double depth)
        {
            int y = GateGeometry.AxisCenter + (int)Math.Round(depth);
            return dir == ShiftDir.Fwd ? GateGeometry.AxisCenter - (int)Math.Round(depth) : y;
        }

        private static double MapX(double depth, double left, double right, double domainMin, double domainMax)
        {
            return MapLinear(depth, domainMin, domainMax, left, right);
        }

        private static double MapY(double lateral, double top, double bottom, double domain)
        {
            double mid = (top + bottom) / 2;
            double t = GateGeometry.Clamp(lateral / domain, -1.0, 1.0);
            return mid - t * (mid - top);
        }
    }
}
