using System;
using System.Windows;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// Plots the PRND lane's fore/aft force across the whole of the stick's travel, for the Feel
    /// tab's PRND LANE section: the free notch at each position, the hump between each pair, and
    /// the wall past either end. Samples ForceComposer.PrndLaneForce rather than redrawing the
    /// shape, like every other visualization here, so the picture cannot quietly stop matching
    /// what the lever renders.
    ///
    /// The four positions are labelled where they actually are, which is what makes this worth
    /// having: the lane length is the throw dial, shared with the other patterns, and shortening
    /// it visibly pulls P and D in toward the middle.
    /// </summary>
    public sealed class PrndLaneVisualizer : ForceGraphVisualizerBase
    {
        /// <summary>
        /// Fine enough that the narrowest feature - a wall bite, which bottoms out at 60 counts -
        /// still lands on more than one sample across the full 65535 of travel.
        /// </summary>
        private const int Samples = 721;

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
            PrndLane lane = cfg.BuildPrndLane();

            // Full gain for the same reason the other curves use it: this section's dials set the
            // shape, and Overall gain with its polarity cap scales the whole lever afterward -
            // plotted at a 10%-capped gain the curve would be indistinguishable from flat.
            cfg.OverallGainPct = 100;
            cfg.PolarityConfirmed = true;
            ForceComposer composer = new ForceComposer(geo, cfg);

            for (int i = 0; i < PrndLane.PositionCount; i++)
            {
                DrawVerticalGuide(dc, MapX(lane.PositionY(i), left, right), lane.LabelFor(i), top, bottom);
            }

            Point[] points = new Point[Samples];
            for (int i = 0; i < Samples; i++)
            {
                int y = (int)Math.Round(GateGeometry.AxisMax * (double)i / (Samples - 1));
                points[i] = new Point(
                    MapX(y, left, right),
                    MapForceBidirectional(composer.PrndLaneForce(y), top, bottom));
            }

            for (int i = 1; i < points.Length; i++)
            {
                dc.DrawLine(CurvePen, points[i - 1], points[i]);
            }

            // A selector is always in a position, so unlike the H detent curve there is no state
            // that makes the dot meaningless - only a disconnected base, where a fresh snapshot's
            // centred X/Y would imply a stick that is not there.
            EngineSnapshot snap = Snapshot;
            if (!snap.DeviceConnected) return;

            Point live = new Point(
                MapX(snap.Y, left, right),
                MapForceBidirectional(composer.PrndLaneForce(snap.Y), top, bottom));
            dc.DrawEllipse(StickBrush, null, live, 6, 6);
        }

        private static double MapX(int y, double left, double right)
        {
            return MapLinear(y, 0, GateGeometry.AxisMax, left, right);
        }
    }
}
