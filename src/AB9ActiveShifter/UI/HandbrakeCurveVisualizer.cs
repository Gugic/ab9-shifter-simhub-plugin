using System;
using System.Windows;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>Samples the real handbrake model; never duplicates its force or output curves.</summary>
    public sealed class HandbrakeCurveVisualizer : ForceGraphVisualizerBase
    {
        protected override void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom)
        {
            if (Settings == null) return;
            EngineConfig cfg = Settings.ToEngineConfig();
            cfg.OverallGainPct = 100;
            cfg.PolarityConfirmed = true;
            var model = new HandbrakeModel(cfg);
            DrawLabel(dc, "pull (green), return (grey), braking (dashed) — full gain", left, top - TopLabelSpace, TextAlign.Left);
            if (!model.IsValid) return;
            DrawVerticalGuide(dc, left, "released", top, bottom);
            DrawVerticalGuide(dc, right, "full pull", top, bottom);
            DrawLabel(dc, "100%", left - 5, top, TextAlign.Right);
            DrawLabel(dc, "0", left - 5, bottom - 10, TextAlign.Right);
            Point lastPull = new Point(left, bottom), lastReturn = lastPull, lastBrake = lastPull;
            for (int i = 1; i <= 240; i++)
            {
                int y = model.RestY + model.PullSign * (int)Math.Round(model.Travel * i / 240.0);
                double px = left + (right - left) * i / 240.0;
                Point pull = new Point(px, bottom - Math.Abs(model.Resistance(y)) / 10000.0 * (bottom - top));
                Point returning = new Point(px, bottom - Math.Abs(model.Resistance(y, -model.PullSign * 20000)) / 10000.0 * (bottom - top));
                Point brake = new Point(px, bottom - model.BrakeValue(y) / 10000.0 * (bottom - top));
                dc.DrawLine(CurvePen, lastPull, pull);
                dc.DrawLine(AxisPen, lastReturn, returning);
                dc.DrawLine(GuidePen, lastBrake, brake);
                lastPull = pull; lastReturn = returning; lastBrake = brake;
            }
            if (Snapshot.DeviceConnected)
            {
                double p = model.PullFraction(Snapshot.RawY);
                dc.DrawEllipse(StickBrush, null, new Point(left + p * (right - left),
                    bottom - model.BrakeValue(Snapshot.RawY) / 10000.0 * (bottom - top)), 5, 5);
            }
        }
    }
}
