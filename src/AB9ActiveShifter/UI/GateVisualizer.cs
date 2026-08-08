using System;
using System.Windows;
using System.Windows.Media;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// The configured pattern drawn as a plan view, with the live stick position on top.
    ///
    /// It draws the gate's actual free space rather than a schematic of it: the neutral tunnel is
    /// as deep as <see cref="GateGeometry.ChannelHalfEnter"/> says, each slot is as wide as the
    /// corridor the lever really has, and the marks on top are the depths and widths the state
    /// machine keys on. That is the whole point of it - every geometry dial moves something here,
    /// so "what does this slider do" has a picture rather than a paragraph.
    ///
    /// The slot outlines come from <see cref="ForceComposer.SlotCorridorHalfWidthAt"/>, not from a
    /// second copy of the mouth arithmetic, for the same reason the Feel tab's curves sample the
    /// composer: a picture that re-derives the shape is the one place it can quietly stop matching
    /// the gate, and it would mislead exactly when someone is using it to diagnose a feel problem.
    /// Like those curves it builds its own composer from the settings, never the live engine's, so
    /// it renders with the plugin disabled and never reaches across to the engine thread.
    ///
    /// It never touches the device.
    /// </summary>
    public sealed class GateVisualizer : ForceGraphVisualizerBase
    {
        private const int AxisMax = GateGeometry.AxisMax;
        private const int AxisCenter = GateGeometry.AxisCenter;

        /// <summary>
        /// How many points each slot flank is drawn from. Spaced quadratically so they crowd the
        /// mouth, which is the only part of the outline that is not a straight line.
        /// </summary>
        private const int FlankSamples = 72;

        private static readonly Brush GateBrush = MakeBrush(Color.FromRgb(0x5A, 0x5F, 0x6A));
        private static readonly Brush FreeSpaceBrush = MakeBrush(Color.FromArgb(0x26, 0x8A, 0x92, 0xA4));
        private static readonly Color LockoutColor = Color.FromRgb(0xE8, 0x8A, 0x1A);
        private static readonly Brush LockoutEdgeBrush = MakeBrush(LockoutColor);
        private static readonly Brush ActiveBrush = MakeBrush(Color.FromRgb(0x36, 0xC7, 0x6A));
        private static readonly Brush DisconnectedBrush = MakeBrush(Color.FromRgb(0xC7, 0x3B, 0x3B));

        /// <summary>Everything the state machine keys on, as opposed to everything it can feel.</summary>
        private static readonly Brush MarkBrush = MakeBrush(Color.FromRgb(0x4A, 0x9E, 0xD8));

        private static readonly Pen GatePen = MakePen(GateBrush, 1.6, false);
        private static readonly Pen ActivePen = MakePen(ActiveBrush, 2.4, false);
        private static readonly Pen LockoutEdgePen = MakePen(MakeBrush(Color.FromArgb(0x80, 0xE8, 0x8A, 0x1A)), 1.6, false);
        private static readonly Pen MarkPen = MakePen(MarkBrush, 1.4, false);
        private static readonly Pen MarkDashPen = MakePen(MarkBrush, 1.4, true);
        private static readonly Pen OwnershipPen = MakePen(GateBrush, 1, true);

        protected override double GraphHeight { get { return 340; } }

        protected override void DrawGraph(DrawingContext dc, double left, double right, double top, double bottom)
        {
            EngineConfig cfg = Settings != null ? Settings.ToEngineConfig() : new EngineConfig();
            GateGeometry geo = cfg.BuildGeometry();
            EngineSnapshot snap = Snapshot;

            if (geo.Pattern == GatePattern.Sequential)
            {
                DrawSequential(dc, geo, snap, left, right, top, bottom);
                return;
            }

            var composer = new ForceComposer(geo, cfg);

            // Bottom to top: territory, then the gate itself, then what the state machine keys on.
            DrawOwnershipBoundaries(dc, geo, left, right, top, bottom);
            DrawLockoutBand(dc, geo, left, right, top, bottom);
            DrawFreeSpace(dc, geo, composer, snap, left, right, top, bottom);
            DrawTunnelExitEdges(dc, geo, left, right, top, bottom);
            DrawColumns(dc, geo, snap, left, right, top, bottom);
            DrawEngageNotches(dc, geo, composer, left, right, top, bottom);
            DrawHandoverWindows(dc, geo, left, right, top, bottom);

            DrawStick(dc, snap, left, right, top, bottom);
        }

        /// <summary>
        /// The gate's free space: the neutral tunnel across the whole width, and each slot that
        /// holds a gear opening off it. Filled as one region, so what is drawn is exactly where
        /// the lever is pushed nowhere sideways.
        /// </summary>
        private void DrawFreeSpace(DrawingContext dc, GateGeometry geo, ForceComposer composer, EngineSnapshot snap,
                                   double left, double right, double top, double bottom)
        {
            var free = new GeometryGroup { FillRule = FillRule.Nonzero };

            free.Children.Add(new RectangleGeometry(new Rect(
                left,
                MapY(AxisCenter - geo.ChannelHalfEnter, top, bottom),
                right - left,
                Math.Max(1, MapY(AxisCenter + geo.ChannelHalfEnter, top, bottom)
                            - MapY(AxisCenter - geo.ChannelHalfEnter, top, bottom)))));

            for (int i = 0; i < geo.ColumnCount; i++)
            {
                Column col = (Column)i;
                if (geo.SlotExists(col, ShiftDir.Fwd))
                    free.Children.Add(SlotOutline(geo, composer, col, ShiftDir.Fwd, left, right, top, bottom));
                if (geo.SlotExists(col, ShiftDir.Back))
                    free.Children.Add(SlotOutline(geo, composer, col, ShiftDir.Back, left, right, top, bottom));
            }

            free.Freeze();
            dc.DrawGeometry(FreeSpaceBrush, GatePen, free);

            // The engaged column, traced over its own outline. The corridor IS the slot here, so
            // lighting its edge says which one holds the gear without a second line down the
            // middle claiming a rail that only a zero-width corridor actually has.
            if (snap.Gear <= 0 || snap.Column == Column.None) return;

            foreach (ShiftDir dir in new[] { ShiftDir.Fwd, ShiftDir.Back })
            {
                if (geo.SlotExists(snap.Column, dir))
                    dc.DrawGeometry(null, ActivePen, SlotOutline(geo, composer, snap.Column, dir, left, right, top, bottom));
            }
        }

        /// <summary>
        /// One slot's corridor, from the tunnel's edge to the end of travel. Both flanks are asked
        /// of the composer at every depth, so the mouth's funnel is the funnel the lever meets.
        /// </summary>
        private static Geometry SlotOutline(GateGeometry geo, ForceComposer composer, Column col, ShiftDir dir,
                                            double left, double right, double top, double bottom)
        {
            int target = geo.ColumnTarget(col);
            int fromDepth = geo.ChannelHalfEnter;
            int toDepth = AxisCenter;

            var near = new Point[FlankSamples];
            var far = new Point[FlankSamples];

            for (int i = 0; i < FlankSamples; i++)
            {
                double t = i / (double)(FlankSamples - 1);
                int depth = (int)Math.Round(fromDepth + (toDepth - fromDepth) * t * t);
                int y = dir == ShiftDir.Fwd ? AxisCenter - depth : AxisCenter + depth;

                double screenY = MapY(y, top, bottom);
                near[i] = new Point(MapX(target - composer.SlotCorridorHalfWidthAt(col, -1, depth, y), left, right), screenY);
                far[i] = new Point(MapX(target + composer.SlotCorridorHalfWidthAt(col, +1, depth, y), left, right), screenY);
            }

            var outline = new StreamGeometry();
            using (StreamGeometryContext ctx = outline.Open())
            {
                ctx.BeginFigure(near[0], true, true);
                ctx.PolyLineTo(near, true, false);
                var farReversed = new Point[FlankSamples];
                for (int i = 0; i < FlankSamples; i++) farReversed[i] = far[FlankSamples - 1 - i];
                ctx.PolyLineTo(farReversed, true, false);
            }
            outline.Freeze();
            return outline;
        }

        /// <summary>
        /// Where the tunnel is finally behind you: the slot walls are fully in by here, and the
        /// lateral guide has stopped being able to change hands. Dashed, because unlike the enter
        /// band it bounds no free space - it is the far side of the hysteresis pair.
        /// </summary>
        private void DrawTunnelExitEdges(DrawingContext dc, GateGeometry geo, double left, double right, double top, double bottom)
        {
            foreach (int y in new[] { AxisCenter - geo.ChannelHalfExit, AxisCenter + geo.ChannelHalfExit })
            {
                double sy = MapY(y, top, bottom);
                dc.DrawLine(GuidePen, new Point(left, sy), new Point(right, sy));
            }

            DrawLabel(dc, "tunnel leaves", left, MapY(AxisCenter - geo.ChannelHalfExit, top, bottom) - 12, TextAlign.Left);
        }

        /// <summary>
        /// The gear labels, with the engaged one lit, plus the width of each mouth where it meets
        /// the tunnel - the band the fore/aft wall is fully open across, which is a different
        /// figure from the slot's own corridor and is the one the "column half-width" dials set.
        /// </summary>
        private void DrawColumns(DrawingContext dc, GateGeometry geo, EngineSnapshot snap,
                                 double left, double right, double top, double bottom)
        {
            for (int i = 0; i < geo.ColumnCount; i++)
            {
                Column col = (Column)i;
                double x = MapX(geo.ColumnTarget(col), left, right);

                bool fwd = geo.SlotExists(col, ShiftDir.Fwd);
                bool back = geo.SlotExists(col, ShiftDir.Back);

                if (fwd)
                {
                    int gear = geo.GearFor(col, ShiftDir.Fwd);
                    DrawGearLabel(dc, geo.LabelFor(gear), x, top - 17, snap.Gear > 0 && snap.Gear == gear);
                }

                if (back)
                {
                    int gear = geo.GearFor(col, ShiftDir.Back);
                    DrawGearLabel(dc, geo.LabelFor(gear), x, bottom + 4, snap.Gear > 0 && snap.Gear == gear);
                }

                // The doorway, marked on both tunnel edges a slot opens off.
                double half = MapX(geo.ColumnTarget(col) + geo.ColumnFreeHalfWidth(col), left, right) - x;
                if (fwd) DrawDoorway(dc, x, half, MapY(AxisCenter - geo.ChannelHalfEnter, top, bottom), -1);
                if (back) DrawDoorway(dc, x, half, MapY(AxisCenter + geo.ChannelHalfEnter, top, bottom), +1);
            }
        }

        private void DrawDoorway(DrawingContext dc, double centreX, double halfWidth, double y, int side)
        {
            dc.DrawLine(MarkPen, new Point(centreX - halfWidth, y), new Point(centreX + halfWidth, y));
            dc.DrawLine(MarkPen, new Point(centreX - halfWidth, y), new Point(centreX - halfWidth, y + 4 * side));
            dc.DrawLine(MarkPen, new Point(centreX + halfWidth, y), new Point(centreX + halfWidth, y + 4 * side));
        }

        /// <summary>
        /// The notches: how deep the lever has to go before the gear registers, and how far back
        /// out before it lets go. Both measure from the end of travel, so they sit near the ends
        /// of the drawing and move toward the middle as the throw is shortened.
        ///
        /// Drawn as wide as the slot is at that depth, from the composer, so a notch never
        /// stretches across a wall that is actually there.
        /// </summary>
        private void DrawEngageNotches(DrawingContext dc, GateGeometry geo, ForceComposer composer,
                                       double left, double right, double top, double bottom)
        {
            bool labelled = false;

            for (int i = 0; i < geo.ColumnCount; i++)
            {
                Column col = (Column)i;

                foreach (ShiftDir dir in new[] { ShiftDir.Fwd, ShiftDir.Back })
                {
                    if (!geo.SlotExists(col, dir)) continue;

                    int engageY = dir == ShiftDir.Fwd ? geo.EngageDepth : AxisMax - geo.EngageDepth;
                    int releaseY = dir == ShiftDir.Fwd ? geo.ReleaseDepth : AxisMax - geo.ReleaseDepth;

                    DrawNotch(dc, geo, composer, col, engageY, MarkPen, left, right, top, bottom);
                    DrawNotch(dc, geo, composer, col, releaseY, MarkDashPen, left, right, top, bottom);

                    if (!labelled && dir == ShiftDir.Fwd)
                    {
                        labelled = true;
                        double x = MapX(geo.ColumnTarget(col), left, right);
                        DrawLabel(dc, "engage", x + NotchReach(geo, composer, col, engageY, left, right) + 5,
                                  MapY(engageY, top, bottom) - 6, TextAlign.Left);
                        DrawLabel(dc, "release", x + NotchReach(geo, composer, col, releaseY, left, right) + 5,
                                  MapY(releaseY, top, bottom) - 6, TextAlign.Left);
                    }
                }
            }
        }

        private static void DrawNotch(DrawingContext dc, GateGeometry geo, ForceComposer composer, Column col, int y,
                                      Pen pen, double left, double right, double top, double bottom)
        {
            double centre = MapX(geo.ColumnTarget(col), left, right);
            double reach = NotchReach(geo, composer, col, y, left, right);
            double sy = MapY(y, top, bottom);

            dc.DrawLine(pen, new Point(centre - reach, sy), new Point(centre + reach, sy));
        }

        /// <summary>
        /// Half a notch's drawn width: the corridor at that depth, with a floor, because the notch
        /// is a fact about detection and has to stay visible when the corridor is drawn shut - a
        /// rail gate is a supported setting.
        /// </summary>
        private static double NotchReach(GateGeometry geo, ForceComposer composer, Column col, int y, double left, double right)
        {
            int depth = Math.Abs(y - AxisCenter);
            int target = geo.ColumnTarget(col);

            double from = MapX(target - composer.SlotCorridorHalfWidthAt(col, -1, depth, y), left, right);
            double to = MapX(target + composer.SlotCorridorHalfWidthAt(col, +1, depth, y), left, right);

            return Math.Max(5, (to - from) / 2);
        }

        /// <summary>
        /// The band either side of each barrier crest where the lateral guide is faded out so it
        /// can change which column it belongs to without the change arriving as a step. Drawn
        /// only inside the tunnel, because that is now the only place a handover can happen.
        /// </summary>
        private void DrawHandoverWindows(DrawingContext dc, GateGeometry geo, double left, double right, double top, double bottom)
        {
            if (geo.DetentHysteresis <= 0) return;

            double from = MapY(AxisCenter - geo.ChannelHalfEnter, top, bottom);
            double to = MapY(AxisCenter + geo.ChannelHalfEnter, top, bottom);

            for (int gap = 0; gap < geo.ColumnCount - 1; gap++)
            {
                int crest = geo.BarrierCentre(gap);
                foreach (int edge in new[] { crest - geo.DetentHysteresis, crest + geo.DetentHysteresis })
                {
                    double x = MapX(edge, left, right);
                    dc.DrawLine(MarkDashPen, new Point(x, from), new Point(x, to));
                }
            }
        }

        /// <summary>
        /// Where a push out of the tunnel stops selecting one column and starts selecting the
        /// next. Every position belongs to some column, so these run the full height: past one of
        /// them, a shove that beats the wall lands in the other column's slot.
        /// </summary>
        private void DrawOwnershipBoundaries(DrawingContext dc, GateGeometry geo, double left, double right, double top, double bottom)
        {
            for (int gap = 0; gap < geo.ColumnCount - 1; gap++)
            {
                int midpoint = (geo.ColumnTarget((Column)gap) + geo.ColumnTarget((Column)(gap + 1))) / 2;
                double x = MapX(midpoint, left, right);
                dc.DrawLine(OwnershipPen, new Point(x, top), new Point(x, bottom));
            }
        }

        /// <summary>
        /// Shades where the lockout gate actually is, rather than where a separate setting says
        /// to shade. The gate positions itself from the gate geometry, so asking the geometry is
        /// the only way for the drawing to stay honest when its width or the layout changes.
        ///
        /// It reaches only as deep as the gate does. Barriers fade out with depth as the slot
        /// walls fade in, so a band drawn the full height of the pattern would claim a toll down
        /// in the slots where there is none - and it buried the 7/R geometry behind it besides.
        /// The gradient is that fade: full across the tunnel's enter band, gone by its exit band.
        /// </summary>
        private void DrawLockoutBand(DrawingContext dc, GateGeometry geo, double left, double right, double top, double bottom)
        {
            if (!geo.HasLockout) return;

            double from = MapX(geo.LockoutCentre - geo.LockoutHalfWidth, left, right);
            double to = MapX(geo.LockoutCentre + geo.LockoutHalfWidth, left, right);
            if (to <= left || from >= right) return;

            double bandTop = MapY(AxisCenter - geo.ChannelHalfExit, top, bottom);
            double bandBottom = MapY(AxisCenter + geo.ChannelHalfExit, top, bottom);

            double holdsFrom = GateGeometry.Clamp(
                0.5 * (1.0 - geo.ChannelHalfEnter / (double)Math.Max(1, geo.ChannelHalfExit)), 0.0, 0.5);

            var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xE8, 0x8A, 0x1A), 0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x4A, 0xE8, 0x8A, 0x1A), holdsFrom));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x4A, 0xE8, 0x8A, 0x1A), 1 - holdsFrom));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xE8, 0x8A, 0x1A), 1));
            fade.Freeze();

            dc.DrawRectangle(fade, null, new Rect(from, bandTop, Math.Max(1, to - from), bandBottom - bandTop));
            dc.DrawLine(LockoutEdgePen, new Point(from, bandTop), new Point(from, bandBottom));
            dc.DrawLine(LockoutEdgePen, new Point(to, bandTop), new Point(to, bandBottom));

            FormattedText text = Text("LOCKOUT", 10, LockoutEdgeBrush);
            dc.DrawText(text, new Point(from + 4, bandTop - text.Height - 3));
        }

        /// <summary>
        /// One sprung track - up a gear away from you, down toward you - with the same two notches
        /// the H pattern gets: where a shift fires, and how far back the lever must come before it
        /// can fire again.
        /// </summary>
        private void DrawSequential(DrawingContext dc, GateGeometry geo, EngineSnapshot snap,
                                    double left, double right, double top, double bottom)
        {
            double cx = (left + right) / 2;
            double midY = MapY(AxisCenter, top, bottom);

            dc.DrawLine(MakePen(GateBrush, 10, false), new Point(cx, top), new Point(cx, bottom));
            dc.DrawLine(MakePen(GateBrush, 10, false), new Point(cx - 18, midY), new Point(cx + 18, midY));

            foreach (int y in new[] { geo.EngageDepth, AxisMax - geo.EngageDepth })
            {
                double sy = MapY(y, top, bottom);
                dc.DrawLine(MarkPen, new Point(cx - 26, sy), new Point(cx + 26, sy));
            }

            foreach (int y in new[] { geo.ReleaseDepth, AxisMax - geo.ReleaseDepth })
            {
                double sy = MapY(y, top, bottom);
                dc.DrawLine(MarkDashPen, new Point(cx - 26, sy), new Point(cx + 26, sy));
            }

            DrawLabel(dc, "shifts here", cx + 30, MapY(geo.EngageDepth, top, bottom) - 6, TextAlign.Left);
            DrawLabel(dc, "re-arms here", cx + 30, MapY(geo.ReleaseDepth, top, bottom) - 6, TextAlign.Left);

            DrawGearLabel(dc, "+", cx, top - 17, snap.GearLabel == "+");
            DrawGearLabel(dc, "-", cx, bottom + 4, snap.GearLabel == "-");

            DrawStick(dc, snap, left, right, top, bottom);
        }

        private void DrawStick(DrawingContext dc, EngineSnapshot snap, double left, double right, double top, double bottom)
        {
            bool live = snap.DeviceConnected;

            dc.DrawEllipse(live ? StickBrush : DisconnectedBrush, null,
                           new Point(MapX(snap.X, left, right), MapY(snap.Y, top, bottom)), 7, 7);

            string label = live ? snap.GearLabel : "--";
            FormattedText gear = Text(label, 30, snap.Gear > 0 ? ActiveBrush : LabelBrush, true);
            dc.DrawText(gear, new Point(right - gear.Width, top + 2));
        }

        private void DrawGearLabel(DrawingContext dc, string text, double centerX, double y, bool lit)
        {
            FormattedText t = Text(text, 13, lit ? ActiveBrush : LabelBrush, lit);
            dc.DrawText(t, new Point(centerX - t.Width / 2, y));
        }

        private static double MapX(double axisValue, double left, double right)
        {
            return MapLinear(axisValue, 0, AxisMax, left, right);
        }

        private static double MapY(double axisValue, double top, double bottom)
        {
            return MapLinear(axisValue, 0, AxisMax, top, bottom);
        }
    }
}
