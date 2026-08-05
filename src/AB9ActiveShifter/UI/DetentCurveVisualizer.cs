using System.Windows;
using System.Windows.Media;
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
    public sealed class DetentCurveVisualizer : ForceGraphVisualizerBase
    {
        private const double MaxFraction = 1.2;
        private const int Samples = 121;

        protected override void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom)
        {
            double midY = (top + bottom) / 2;

            DrawLabel(dc, "0", left - 10, midY - 7, TextAlign.Right);
            DrawLabel(dc, "+full", left - 10, top - 7, TextAlign.Right);
            DrawLabel(dc, "-full", left - 10, bottom - 7, TextAlign.Right);
            dc.DrawLine(AxisPen, new Point(left, midY), new Point(right, midY));

            DrawVerticalGuide(dc, MapX(0.0, left, right), "centre", top, bottom);
            DrawVerticalGuide(dc, MapX(0.55, left, right), "crossover", top, bottom);
            DrawVerticalGuide(dc, MapX(0.80, left, right), "seated", top, bottom);
            DrawVerticalGuide(dc, MapX(1.00, left, right), "engaged", top, bottom);

            DrawLabel(dc, "shape at full gain - see Overall gain for felt force", right, top - TopLabelSpace, TextAlign.Right);

            if (Settings == null) return;

            // The resist/pull/hold ratios and the crossover geometry are set entirely by this
            // section's three sliders; Overall gain and the unconfirmed-polarity cap scale the
            // whole gate uniformly afterward, not this shape specifically. Plotting at the
            // rig's actual (often 10%-capped) gain left the curve indistinguishable from flat
            // for any realistic tuning - confirmed by rendering it offscreen before deploying,
            // rather than only after asking for it to be checked on hardware.
            EngineConfig cfg = Settings.ToEngineConfig();
            GateGeometry geo = cfg.BuildGeometry();
            cfg.OverallGainPct = 100;
            cfg.PolarityConfirmed = true;
            ForceComposer composer = new ForceComposer(geo, cfg);

            Point[] points = new Point[Samples];
            for (int i = 0; i < Samples; i++)
            {
                double fraction = MaxFraction * i / (Samples - 1);
                int force = composer.DetentMagnitude(ShiftDir.Fwd, fraction, muted: false);
                points[i] = new Point(MapX(fraction, left, right), MapForceBidirectional(force, top, bottom));
            }

            for (int i = 1; i < points.Length; i++)
            {
                dc.DrawLine(CurvePen, points[i - 1], points[i]);
            }

            // Only while the stick is actually inside a column: in the neutral channel the
            // fore/aft force is the gate wall (ComposeNeutral), not this curve at all, and a
            // dot moving here while sliding along the tunnel would show motion against a shape
            // that has nothing to do with what the hand is actually feeling at that moment.
            // DeviceConnected matters too: a fresh EngineSnapshot defaults to State=Neutral
            // (its first enum value) with X/Y at centre, so before the engine ever connects -
            // or GateVisualizer's own DrawStick would show its red disconnected marker - this
            // would otherwise draw a dot implying a real, centred stick that is not there.
            EngineSnapshot snap = Snapshot;
            if (snap.DeviceConnected && snap.State != GateState.Neutral && snap.Column != Column.None)
            {
                ShiftDir dir = geo.DirectionOf(snap.Y);
                double liveFraction = geo.EngageFraction(dir, snap.Y);
                int liveForce = composer.DetentMagnitude(ShiftDir.Fwd, liveFraction, muted: false);
                Point livePoint = new Point(MapX(liveFraction, left, right), MapForceBidirectional(liveForce, top, bottom));
                dc.DrawEllipse(StickBrush, null, livePoint, 6, 6);
            }
        }

        private static double MapX(double fraction, double left, double right)
        {
            return MapLinear(fraction, 0.0, MaxFraction, left, right);
        }
    }
}
