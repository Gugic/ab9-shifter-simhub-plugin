using System;
using System.Windows;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// Plots the fore/aft force of one stroke against how far the lever has travelled from
    /// centre - resist, the crossover into the snick, the seated hold, and, once the slot has
    /// been given a bottom, the free landing and the end-stop wall - for the Feel tab's SLOT
    /// DETENT section. Samples ForceComposer.StrokeForceAt directly rather than reimplementing
    /// the shape, so the plot cannot drift from what a real shift renders. A live dot tracks the
    /// stick's actual position on the curve, read from the engine snapshot on the same
    /// timer/interval GateVisualizer polls its own live stick position with.
    ///
    /// The axis is axis counts from centre rather than the engage fraction it used to be, and
    /// both halves of that matter. The fraction saturates at 1.2, so anything past the engage
    /// line - which is exactly where a short throw puts its landing and its wall - fell off the
    /// right-hand edge or piled up against it. Counts also make the graph say the thing the
    /// throw dial is for: the curve simply gets shorter as the throw is shortened.
    /// </summary>
    public sealed class DetentCurveVisualizer : ForceGraphVisualizerBase
    {
        /// <summary>
        /// Fine enough that the narrowest feature on the curve - a wall bite, which bottoms out
        /// at 60 counts - still lands on more than one sample at full travel.
        /// </summary>
        private const int Samples = 481;

        protected override void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom)
        {
            double midY = (top + bottom) / 2;

            DrawLabel(dc, "0", left - 10, midY - 7, TextAlign.Right);
            DrawLabel(dc, "+full", left - 10, top - 7, TextAlign.Right);
            DrawLabel(dc, "-full", left - 10, bottom - 7, TextAlign.Right);
            dc.DrawLine(AxisPen, new Point(left, midY), new Point(right, midY));

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

            int seat = composer.StrokeSeatDepth;
            int stop = composer.StrokeStopDepth;

            DrawVerticalGuide(dc, MapX(0, left, right), "centre", top, bottom);

            // The crossover fractions are the slot detent's own, so they mean nothing on a
            // sequential lever - its stroke is one rise to the firing line.
            if (Settings.IsHPattern)
            {
                DrawVerticalGuide(dc, MapX((int)(seat * 0.55), left, right), "crossover", top, bottom);
                DrawVerticalGuide(dc, MapX((int)(seat * 0.80), left, right), "snick", top, bottom);
            }

            DrawVerticalGuide(dc, MapX(seat, left, right), Settings.IsHPattern ? "seated" : "fires", top, bottom);

            // AxisCenter means "no bottom": the wall would begin at or past the end of travel and
            // is never met, so drawing a guide there would claim a stop the lever cannot reach.
            if (stop < GateGeometry.AxisCenter)
            {
                DrawVerticalGuide(dc, MapX(stop, left, right), "stop", top, bottom);
            }

            Point[] points = new Point[Samples];
            for (int i = 0; i < Samples; i++)
            {
                int depth = (int)Math.Round(GateGeometry.AxisCenter * (double)i / (Samples - 1));
                int force = composer.StrokeForceAt(ShiftDir.Fwd, GateGeometry.AxisCenter - depth, muted: false);
                points[i] = new Point(MapX(depth, left, right), MapForceBidirectional(force, top, bottom));
            }

            for (int i = 1; i < points.Length; i++)
            {
                dc.DrawLine(CurvePen, points[i - 1], points[i]);
            }

            DrawLiveDot(dc, geo, composer, left, right, top, bottom);
        }

        /// <summary>
        /// Only while the stroke this curve draws is the one being felt. On an H gate that means
        /// inside a column: in the neutral channel the fore/aft force is the gate wall
        /// (ComposeNeutral), not this curve at all, and a dot moving here while sliding along the
        /// tunnel would show motion against a shape that has nothing to do with what the hand is
        /// feeling. A sequential lever has no other fore/aft state, so it always qualifies.
        ///
        /// DeviceConnected matters too: a fresh EngineSnapshot defaults to State=Neutral (its
        /// first enum value) with X/Y at centre, so before the engine ever connects - or where
        /// GateVisualizer's own DrawStick would show its red disconnected marker - this would
        /// otherwise draw a dot implying a real, centred stick that is not there.
        /// </summary>
        private void DrawLiveDot(DrawingContext dc, GateGeometry geo, ForceComposer composer,
                                 double left, double right, double top, double bottom)
        {
            EngineSnapshot snap = Snapshot;
            if (!snap.DeviceConnected) return;

            if (Settings.IsHPattern && (snap.State == GateState.Neutral || snap.Column == Column.None)) return;

            int depth = Math.Abs(snap.Y - GateGeometry.AxisCenter);
            int force = composer.StrokeForceAt(ShiftDir.Fwd, GateGeometry.AxisCenter - depth, muted: false);

            Point live = new Point(MapX(depth, left, right), MapForceBidirectional(force, top, bottom));
            dc.DrawEllipse(StickBrush, null, live, 6, 6);
        }

        private static double MapX(int depth, double left, double right)
        {
            return MapLinear(depth, 0, GateGeometry.AxisCenter, left, right);
        }
    }
}
