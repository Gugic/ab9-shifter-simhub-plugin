using System;
using System.Windows;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// Plots the neutral channel's fore/aft gate wall - the force pushing back toward centre
    /// as the stick moves away from it while not lined up with a column - for the Feel tab's
    /// GATE WALLS section. Free corridor at zero, then a ramp up to the full wall, exactly
    /// ComposeNeutral's own Saturating(y - AxisCenter, WallForce, WallRamp, ChannelFreeDepth)
    /// call: WallForce and ChannelFreeDepthCeiling are already public (see items #1 and #2 of
    /// the enhancement brief), and the ramp formula itself is a three-line clamp-and-scale with
    /// no per-unit constants to drift, so this reads the real inputs rather than needing new
    /// ForceComposer surface the way the slot detent curve did for its own, more elaborate shape.
    /// </summary>
    public sealed class GateWallCurveVisualizer : ForceGraphVisualizerBase
    {
        protected override void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom)
        {
            DrawLabel(dc, "0", left - 10, bottom - 7, TextAlign.Right);
            DrawLabel(dc, "+full", left - 10, top - 7, TextAlign.Right);
            dc.DrawLine(AxisPen, new Point(left, bottom), new Point(right, bottom));
            DrawLabel(dc, "shape at full gain - see Overall gain for felt force", right, top - TopLabelSpace, TextAlign.Right);

            if (Settings == null) return;

            // Shown at full gain, like the slot detent curve: the free-corridor/ramp/plateau
            // shape is set entirely by this wall's own dials, and Overall gain (often capped
            // to 10% until polarity is confirmed) scales the whole gate uniformly afterward,
            // not this wall specifically. Plotting at the rig's actual gain left the plateau
            // barely above the baseline for the shipped defaults - caught by rendering this
            // offscreen before it was ever deployed.
            EngineConfig cfg = Settings.ToEngineConfig();
            GateGeometry geo = cfg.BuildGeometry();
            cfg.OverallGainPct = 100;
            cfg.PolarityConfirmed = true;
            ForceComposer composer = new ForceComposer(geo, cfg);

            int plateau = composer.WallForce;
            int ramp = Math.Max(1, cfg.WallRamp);
            int deadband = Math.Min(cfg.ChannelFreeDepth, composer.ChannelFreeDepthCeiling);
            double domain = Math.Max(2000, deadband + ramp + 500);

            DrawVerticalGuide(dc, MapX(deadband, left, right, domain), "free corridor ends", top, bottom);
            DrawVerticalGuide(dc, MapX(deadband + ramp, left, right, domain), "full wall", top, bottom);

            const int samples = 121;
            Point[] points = new Point[samples];
            for (int i = 0; i < samples; i++)
            {
                double depth = domain * i / (samples - 1);
                int force = WallForceAt(depth, plateau, ramp, deadband);
                points[i] = new Point(MapX(depth, left, right, domain), MapForceMagnitude(force, top, bottom));
            }

            for (int i = 1; i < points.Length; i++)
            {
                dc.DrawLine(CurvePen, points[i - 1], points[i]);
            }

            // Only while actually in the neutral channel: once lined up with and inside a
            // column, this fore/aft axis is the slot detent instead (see DetentCurveVisualizer),
            // a completely different force with its own graph. DeviceConnected too: a fresh
            // EngineSnapshot defaults to State=Neutral with Y at centre, so before the engine
            // ever connects this would otherwise draw a dot implying a real stick position
            // that is not there.
            EngineSnapshot snap = Snapshot;
            if (snap.DeviceConnected && snap.State == GateState.Neutral)
            {
                double liveDepth = Math.Abs(snap.Y - GateGeometry.AxisCenter);
                int liveForce = WallForceAt(liveDepth, plateau, ramp, deadband);
                Point livePoint = new Point(MapX(liveDepth, left, right, domain), MapForceMagnitude(liveForce, top, bottom));
                dc.DrawEllipse(StickBrush, null, livePoint, 6, 6);
            }
        }

        /// <summary>
        /// The wall's magnitude at a given depth, from <see cref="ForceComposer.Saturating"/>
        /// itself rather than a copy of its arithmetic - so this curve cannot drift away from
        /// the wall it draws. Saturating opposes the displacement, and this graph plots
        /// magnitude, hence the Abs.
        /// </summary>
        private static int WallForceAt(double depth, int plateau, int ramp, int deadband)
        {
            return Math.Abs(ForceComposer.Saturating((int)Math.Round(depth), plateau, ramp, deadband));
        }

        private static double MapX(double depth, double left, double right, double domain)
        {
            return MapLinear(depth, 0.0, domain, left, right);
        }
    }
}
