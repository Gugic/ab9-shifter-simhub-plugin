using System;
using System.Windows;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// Plots the lateral (X-axis) force felt sliding along the neutral channel, across the
    /// entire gate width - the light pull into each column, the humps between them, and the
    /// lockout gate before 7/R - for the Feel tab's SLIDING ACROSS THE GATE section. Samples
    /// ForceComposer.LateralGuide + BarrierForceIn directly (the same two calls ComposeNeutral
    /// itself combines for ConstantX), at the channel centre (y = AxisCenter, zero depth) since
    /// that is what "sliding along the tunnel" means. Column ownership for the guide is picked
    /// fresh at each x via GateGeometry.NearestColumn(x, Column.None) - the cold, non-hysteretic
    /// answer - since a static sweep has no history to be hysteretic about; the live dot uses
    /// the same pick, not the engine's own remembered guide column, so it always lands exactly
    /// on the drawn curve rather than occasionally just off it near a boundary.
    /// </summary>
    public sealed class SlidingAcrossGateVisualizer : ForceGraphVisualizerBase
    {
        private static readonly Brush LockoutBrush = MakeBrush(Color.FromArgb(0x40, 0xE8, 0x8A, 0x1A));
        private static readonly Brush LockoutEdgeBrush = MakeBrush(Color.FromRgb(0xE8, 0x8A, 0x1A));
        private static readonly Pen LockoutEdgePen = MakePen(LockoutEdgeBrush, 1, true);

        protected override void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom)
        {
            double midY = (top + bottom) / 2;

            DrawLabel(dc, "0", left - 10, midY - 7, TextAlign.Right);
            DrawLabel(dc, "+full", left - 10, top - 7, TextAlign.Right);
            DrawLabel(dc, "-full", left - 10, bottom - 7, TextAlign.Right);
            dc.DrawLine(AxisPen, new Point(left, midY), new Point(right, midY));
            DrawLabel(dc, "shape at full gain - see Overall gain for felt force", right, top - TopLabelSpace, TextAlign.Right);

            if (Settings == null) return;

            EngineConfig cfg = Settings.ToEngineConfig();
            GateGeometry geo = cfg.BuildGeometry();
            cfg.OverallGainPct = 100;
            cfg.PolarityConfirmed = true;
            ForceComposer composer = new ForceComposer(geo, cfg);

            int axisMax = GateGeometry.AxisMax;

            if (geo.HasLockout)
            {
                double from = MapX(geo.LockoutCentre - geo.LockoutHalfWidth, left, right, axisMax);
                double to = MapX(geo.LockoutCentre + geo.LockoutHalfWidth, left, right, axisMax);
                dc.DrawRectangle(LockoutBrush, null, new Rect(from, top, Math.Max(1, to - from), bottom - top));
                dc.DrawLine(LockoutEdgePen, new Point(from, top), new Point(from, bottom));
                dc.DrawLine(LockoutEdgePen, new Point(to, top), new Point(to, bottom));
            }

            for (int i = 0; i < geo.ColumnCount; i++)
            {
                Column col = (Column)i;
                double x = MapX(geo.ColumnTarget(col), left, right, axisMax);
                dc.DrawLine(GuidePen, new Point(x, top), new Point(x, bottom));

                string label = geo.LabelFor(geo.GearFor(col, ShiftDir.Fwd)) + "/" + geo.LabelFor(geo.GearFor(col, ShiftDir.Back));
                DrawLabel(dc, label, x, bottom + 4, TextAlign.Center);
            }

            const int samples = 161;
            Point[] points = new Point[samples];
            for (int i = 0; i < samples; i++)
            {
                int x = (int)Math.Round(axisMax * (double)i / (samples - 1));
                int force = LateralForceAt(composer, geo, x);
                points[i] = new Point(MapX(x, left, right, axisMax), MapForceBidirectional(force, top, bottom));
            }

            for (int i = 1; i < points.Length; i++)
            {
                dc.DrawLine(CurvePen, points[i - 1], points[i]);
            }

            // Only while actually sliding in the neutral channel: once inside a column this
            // axis is still governed by the same LateralGuide expression (see the invariant
            // that the lateral field must not depend on the latch), so the curve stays correct
            // there too, but the fore/aft axis has already switched to the slot detent - shown
            // on its own graph - so a dot here would only tell half the story while in a gear.
            // DeviceConnected too: a fresh EngineSnapshot defaults to State=Neutral with X at
            // centre, so before the engine ever connects this would otherwise draw a dot
            // implying a real stick position that is not there.
            EngineSnapshot snap = Snapshot;
            if (snap.DeviceConnected && snap.State == GateState.Neutral)
            {
                int liveForce = LateralForceAt(composer, geo, snap.X);
                Point livePoint = new Point(MapX(snap.X, left, right, axisMax), MapForceBidirectional(liveForce, top, bottom));
                dc.DrawEllipse(StickBrush, null, livePoint, 6, 6);
            }
        }

        private static int LateralForceAt(ForceComposer composer, GateGeometry geo, int x)
        {
            Column guideColumn = geo.NearestColumn(x, Column.None);
            int y = GateGeometry.AxisCenter;

            // The side latch supplied per sample, crest-relative: a one-way gate ignores it,
            // and a Both gate then draws what each side's approach actually feels - resisted
            // toward the crest from wherever you stand - instead of nothing, which is what a
            // history latch with no history would render.
            int side = x >= geo.LockoutCentre ? 1 : -1;
            return composer.LateralGuide(x, y, guideColumn) + composer.BarrierForceIn(x, y, side);
        }

        private static double MapX(int x, double left, double right, int axisMax)
        {
            return MapLinear(x, 0, axisMax, left, right);
        }
    }
}
