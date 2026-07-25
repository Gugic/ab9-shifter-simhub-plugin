using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Maps gate state and stick position onto the forces for one tick.
    ///
    /// Everything the hand reads as a wall is a shaped constant force, never a DirectInput
    /// condition effect. A spring's output is coefficient times displacement over 10000, so
    /// even at the maximum coefficient of 10000 the stick has to be pushed roughly 23000 axis
    /// counts - a third of full travel - before it reaches 70% force. Five hundred counts past
    /// a wall yields about 1.5%, which is why spring walls feel like nothing at all. A software
    /// profile reaches its plateau in a few hundred counts and holds it, so a wall feels like a
    /// wall.
    ///
    /// Stability comes from shape, not from damping. Past its short bite every wall is a flat
    /// plateau, and a flat force has no gradient for the loop's delay to pump - leaning on it
    /// is unconditionally calm, which is what made the original constant-force lockout the one
    /// part of the gate that never rang. A gradient rendered through USB delay oscillates at
    /// any damping; the bite only cages that flutter, so it is kept short.
    ///
    /// The gate is then just three kinds of force:
    ///
    ///   Lateral, in the neutral channel - a light detent onto the nearest column, a hump at
    ///   each gap between the ordinary columns, and the lockout gate before 7/R: a compact
    ///   band of flat one-way force toward the main gears, so crossing costs the same fight
    ///   at any speed. A dot on the channel, not a zone - the walls own the rest of the box.
    ///
    ///   Lateral, once in a column - a firm pin onto that column, so fore/aft travel tracks the
    ///   slot instead of wandering out of it. This is the vertical guide.
    ///
    ///   Fore/aft - held to the neutral channel in proportion to how far the stick is from a
    ///   column, so it is free to enter a gear when lined up and walled off when not. This is
    ///   the horizontal guide. Once in a column it gives way to the slot detent.
    ///
    /// Every force is rendered non-conservatively: full strength when it resists the stick's
    /// motion or the stick is holding still, reduced strength when it is accelerating the stick
    /// the way it is already going. A position-to-force loop over USB carries 5-10 ms of delay,
    /// and a stiff wall rendered through delay acts as negative damping - each overshoot comes
    /// back with interest and the wall rings. Giving back less energy on the rebound than was
    /// stored on the way in starves that cycle at its source, which is how mechanical gates
    /// stay quiet too: they are friction-damped and do not fling the lever back.
    /// </summary>
    public sealed class ForceComposer
    {
        private readonly GateGeometry _geo;
        private readonly EngineConfig _cfg;

        private readonly int _columnPinForce;
        private readonly int _channelWallForce;
        private readonly int _channelGuideForce;
        private readonly int _columnDetentForce;
        private readonly int _columnFunnelForce;
        private readonly int _barrierForce;
        private readonly int _lockoutForce;

        private readonly int _detentResistMax;
        private readonly int _detentPullMax;
        private readonly int _detentHold;
        private readonly int _damperCoeff;

        private readonly int _dampingForce;
        private readonly double _dampingPerCount;

        /// <summary>Force multiplier on a rebound: 1 - WallYieldPct.</summary>
        private readonly double _yieldFloor;

        /// <summary>
        /// Milder rebound floor for the slot detent. The snick is supposed to do positive work
        /// pulling the stick home, so it keeps most of its strength while assisting.
        /// </summary>
        private readonly double _snickFloor;

        private readonly int _yieldDeadband;
        private readonly int _yieldBlend;

        /// <summary>Force growth rate in DirectInput units per millisecond; 0 disables shaping.</summary>
        private readonly double _attackPerMs;

        /// <summary>
        /// While pressed and still, force deviations within this fraction of what is already
        /// being applied are held frozen instead of tracked. Proportional rather than absolute:
        /// a fixed band wide enough to steady a full-strength wall would swallow a light guide
        /// force whole, and freezing the gentle pull along the channel would make sliding across
        /// the gate feel notchy and sticky.
        /// </summary>
        private const int StaticHoldDivisor = 5;

        /// <summary>Smallest static hold band, so tiny forces still settle.</summary>
        private const int StaticHoldFloor = 200;

        private readonly int _constantSignX;
        private readonly int _constantSignY;
        private readonly bool _freeStick;

        private int _shapedX;
        private int _shapedY;

        /// <summary>
        /// The column the lateral field is measured from. None until a position is seen, because a
        /// guess is a force: this used to be seeded to C2, so the first tick after a settings change
        /// aimed the whole lateral field at the middle of the gate no matter where the lever was.
        /// </summary>
        private Column _guideColumn = Column.None;

        public ForceComposer(GateGeometry geometry, EngineConfig config)
        {
            _geo = geometry;
            _cfg = config;

            double gain = config.EffectiveGain;
            _freeStick = config.FreeStick;

            // An effect the firmware applies backwards is corrected by flipping its sign. The
            // two axes are independent: this base inverts constant force on X but not on Y.
            _constantSignX = config.InvertConstantX ? -1 : 1;
            _constantSignY = config.InvertConstantY ? -1 : 1;

            // Wall strengths are percentages of full scale, then scaled by the master gain, so
            // raising overall force raises the whole gate together and keeps the tuned ratios.
            _columnPinForce = Force(config.ColumnPinForcePct, gain);
            _channelWallForce = Force(config.ChannelWallForcePct, gain);
            _channelGuideForce = Force(config.ChannelGuideForcePct, gain);
            _columnDetentForce = Force(config.ColumnDetentForcePct, gain);
            _columnFunnelForce = Math.Min(
                Force(config.ColumnFunnelForcePct, gain), Force(config.ColumnPinForcePct, gain));
            _barrierForce = Force(config.BarrierForcePct, gain);
            _lockoutForce = Force(config.LockoutForcePct, gain);

            _detentResistMax = Force(config.DetentResistPct, gain);
            _detentPullMax = Force(config.DetentPullPct, gain);
            _detentHold = Force(config.DetentHoldPct, gain);
            _damperCoeff = Scale(config.DamperCoeff, gain);

            _dampingForce = Force(config.DampingPct, gain);
            _dampingPerCount = _dampingForce / (double)Math.Max(1, config.DampingReferenceSpeed);

            double yield = GateGeometry.Clamp(config.WallYieldPct, 0, 90) / 100.0;
            _yieldFloor = 1.0 - yield;
            _snickFloor = 1.0 - yield * 0.33;
            _yieldDeadband = Math.Max(0, config.YieldVelocityDeadband);
            _yieldBlend = Math.Max(1, config.YieldVelocityBlend);

            _attackPerMs = config.WallAttackMs <= 0
                ? 0
                : GateGeometry.ForceMax / (double)config.WallAttackMs;
        }

        /// <summary>Gain-scaled damping applied on every frame.</summary>
        public int DamperCoefficient { get { return _damperCoeff; } }

        /// <summary>The lockout gate's force in DirectInput units, for the UI.</summary>
        public int LockoutForce { get { return _lockoutForce; } }

        /// <summary>Peak force of the gate walls in DirectInput units, for the UI.</summary>
        public int WallForce { get { return _channelWallForce; } }

        private static int Force(int percent, double gain)
        {
            return (int)Math.Round(GateGeometry.ForceMax * GateGeometry.Clamp(percent, 0, 100) / 100.0 * gain);
        }

        private static int Scale(int value, double gain)
        {
            return (int)Math.Round(value * gain);
        }

        /// <summary>
        /// Forces for this tick. Velocities are axis counts per second, lightly smoothed by the
        /// caller; they drive both the damping term and the rebound yield.
        ///
        /// Composition happens in the gate's own frame; the measured polarity signs are applied
        /// once at the very end, so the yield's same-direction test compares like with like.
        /// </summary>
        public ForceFrame Compose(
            GateState state, Column column, ShiftDir direction, int x, int y,
            int vx = 0, int vy = 0, double dtMs = 0)
        {
            if (_freeStick)
            {
                _shapedX = 0;
                _shapedY = 0;

                // Forget the guide column too. The lever can be moved anywhere while the forces are
                // off, and coming back with a stale column would aim a saturated wall at wherever
                // it used to be.
                _guideColumn = Column.None;
                return FreeFrame();
            }

            // Advanced once, before the branch, so both branches measure from the same column and
            // the lateral field cannot depend on which one runs.
            _guideColumn = _geo.GuideColumn(x, _guideColumn, _geo.InChannel(y));

            ForceFrame frame = state == GateState.Neutral
                ? ComposeNeutral(x, y)
                : ComposeInColumn(column, direction, x, y);

            int boundedX = Yield(frame.ConstantX, vx, _yieldFloor);
            int boundedY = Yield(frame.ConstantY, vy, state == GateState.Neutral ? _yieldFloor : _snickFloor);

            // Everything the hand can lean against is shaped in time, including the lockout. It
            // was exempted at first on the theory that slewing a crossing hands a fast flick a
            // discount, but the arithmetic does not support that: the lockout band is thousands
            // of counts wide, so even a violent flick spends tens of milliseconds inside it while
            // the attack lasts fifteen or twenty. What the exemption did buy was the one force in
            // the gate still arriving raw - so the lockout rejected the lever hard where every
            // wall had learned not to, and rang.
            //
            // The slot detent is the exception that remains. The snick is a deliberate transient,
            // over in a few milliseconds by design, and it has to arrive whole to read as a
            // mechanism seating rather than a soft nudge.
            boundedX = ShapeInTime(ref _shapedX, boundedX, vx, dtMs);
            boundedY = state == GateState.Neutral
                ? ShapeInTime(ref _shapedY, boundedY, vy, dtMs)
                : Track(ref _shapedY, boundedY);

            // Damping joins after the yield and the time shaping - it opposes motion by
            // construction, so it can never be the assisting force the yield softens, and it
            // must keep its full bandwidth rather than being slewed.
            frame.ConstantX = Combine(boundedX, Damping(vx)) * _constantSignX;
            frame.ConstantY = Combine(boundedY, Damping(vy)) * _constantSignY;

            frame.DamperCoefficient = _damperCoeff;
            return frame;
        }

        /// <summary>Unshaped passthrough that keeps the shaping state continuous across mode changes.</summary>
        private static int Track(ref int shaped, int value)
        {
            shaped = value;
            return value;
        }

        /// <summary>
        /// The wall in time rather than in space, with three behaviours. Attack: force may
        /// only grow at the configured rate, so contact winds up like a real surface instead
        /// of landing as a delay-late hammer blow. Static hold: pressed against the same wall
        /// and effectively still, small force deviations are frozen rather than tracked -
        /// static friction, and the only thing that quiets a light press resting on the face,
        /// where the gradient is far too steep for any damping to stabilise through this much
        /// delay. Release: any drop, sign flip or let-go passes instantly, so a retreating
        /// stick is never chased by stale force. A dt of zero bypasses shaping entirely.
        /// </summary>
        private int ShapeInTime(ref int shaped, int target, int velocity, double dtMs)
        {
            if (_attackPerMs <= 0 || dtMs <= 0)
            {
                shaped = target;
                return shaped;
            }

            bool sameWall = target != 0 && shaped != 0 && Math.Sign(target) == Math.Sign(shaped);

            int holdBand = Math.Max(StaticHoldFloor, Math.Abs(shaped) / StaticHoldDivisor);

            if (sameWall
                && Math.Abs(velocity) <= _yieldDeadband
                && Math.Abs(target - shaped) <= holdBand)
            {
                return shaped;
            }

            // A sign flip or a fresh contact restarts the attack from nothing.
            int from = sameWall ? shaped : 0;

            if (Math.Abs(target) <= Math.Abs(from))
            {
                shaped = target;
                return shaped;
            }

            int step = Math.Max(1, (int)Math.Round(_attackPerMs * dtMs));
            int magnitude = Math.Min(Math.Abs(target), Math.Abs(from) + step);
            shaped = Math.Sign(target) * magnitude;
            return shaped;
        }

        /// <summary>
        /// The rebound absorber. A force resisting the stick's motion, or acting on a stick
        /// that is effectively still, passes through untouched - leaning on a wall stays solid.
        /// A force accelerating the stick along its existing motion is scaled toward the floor
        /// as speed grows, so a bounce off a wall returns less energy than the push stored.
        /// </summary>
        private int Yield(int force, int velocity, double floor)
        {
            if (force == 0 || floor >= 1.0) return force;
            if (Math.Sign(force) != Math.Sign(velocity)) return force;

            int speed = Math.Abs(velocity);
            if (speed <= _yieldDeadband) return force;

            double t = GateGeometry.Clamp((speed - _yieldDeadband) / (double)_yieldBlend, 0.0, 1.0);
            double scale = 1.0 - (1.0 - floor) * t;
            return (int)Math.Round(force * scale);
        }

        /// <summary>
        /// Force opposing motion, proportional to speed up to the configured ceiling. The other
        /// half of keeping a stiff wall quiet, and the part that also calms free travel.
        /// </summary>
        private int Damping(int velocity)
        {
            if (_dampingForce <= 0 || velocity == 0) return 0;

            double force = -velocity * _dampingPerCount;
            return (int)Math.Round(GateGeometry.Clamp(force, -_dampingForce, _dampingForce));
        }

        private static int Combine(int a, int b)
        {
            return GateGeometry.Clamp(a + b, -GateGeometry.ForceMax, GateGeometry.ForceMax);
        }

        /// <summary>Everything off, so the stick is as free as the hardware allows.</summary>
        public static ForceFrame FreeFrame()
        {
            return new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off,
                ConstantX = 0,
                ConstantY = 0,
                DamperCoefficient = 0
            };
        }

        /// <summary>
        /// The entire lateral field, in one expression, keyed on nothing but position and the guide
        /// column. Both states call it and get the same answer, which is the point.
        ///
        /// It used to be computed twice - a funnel-plus-confinement in the tunnel, a slot wall once
        /// a column was latched - and the two disagreed by up to 4924 measured DI units, nearly six
        /// newton-metres, at the same physical position. Which one you got depended on the state
        /// machine's latch, and because the channel bands are hysteretic, that in turn depended on
        /// how the lever had arrived. Travelling from one slot to another around a divider end is
        /// exactly the manoeuvre that crosses the boundary, and it was felt exactly there: the
        /// mouth rang while the deep walls, where the two branches happened to agree and both went
        /// flat, stayed calm.
        ///
        /// Every lateral force also now rises at one stiffness - the wall's own, pin force over its
        /// bite - because the face length is derived from the plateau rather than set by a separate
        /// dial. That structurally retires the steepest gradient in the gate: the funnel's, at 13.3
        /// DI per count against a wall face of 3.8, which existed only in the mouth and only
        /// because its ramp was a free parameter someone had turned to its floor.
        /// </summary>
        private int LateralGuide(int x, int y)
        {
            if (_guideColumn == Column.None) return 0;

            int corridor = SlotCorridor(_guideColumn);
            int plateau = GuidePlateau(Math.Abs(y - GateGeometry.AxisCenter));
            if (plateau <= 0) return 0;

            return Saturating(
                x - _geo.ColumnTarget(_guideColumn), plateau, GuideFace(plateau, corridor), corridor);
        }

        /// <summary>
        /// How hard the lateral guide pushes at this depth: the light detent that parks the lever on
        /// a column in the tunnel, growing through the funnel that steers an off-column entry into
        /// its slot, to the full slot wall below. Piecewise linear and continuous, so there is no
        /// depth at which the lever is handed a step.
        /// </summary>
        private int GuidePlateau(int depth)
        {
            int mouth = Math.Max(1, _geo.ChannelHalfExit);

            if (depth <= mouth)
            {
                return (int)Math.Round(Lerp(_columnDetentForce, _columnFunnelForce, depth / (double)mouth));
            }

            // The span is the channel's own width, deliberately NOT the wall's bite. The bite is a
            // lateral distance and this is a depth, and coupling them meant a long bite pushed the
            // slot wall's full strength tens of thousands of counts down the slot - the wall went
            // missing exactly where a gear is held.
            double t = GateGeometry.Clamp((depth - mouth) / (double)mouth, 0.0, 1.0);
            return (int)Math.Round(Lerp(_columnFunnelForce, _columnPinForce, t));
        }

        /// <summary>
        /// Face length for a given plateau, chosen so every lateral force in the gate has the same
        /// stiffness as the slot wall: plateau over face is always pin force over the wall's bite.
        /// A gentler force therefore gets a shorter face rather than a steeper one.
        /// </summary>
        private int GuideFace(int plateau, int corridor)
        {
            int ramp = SlotRamp(corridor);
            if (_columnPinForce <= 0) return ramp;

            return GateGeometry.Clamp(
                (int)Math.Round(plateau * ramp / (double)_columnPinForce), 1, ramp);
        }

        /// <summary>
        /// The humps and the lockout gate, faded out with depth. A plate has its gate cut into the
        /// tunnel, not into the slots, so below the channel the slot walls own the lateral axis
        /// alone. Applied in every state, like the guide, because anything indexed on the state
        /// machine puts the step back.
        /// </summary>
        private int BarrierForceIn(int x, int y)
        {
            double faded = 1.0 - _geo.SlotConfinementFactor(y, _cfg.WallRamp);
            if (faded <= 0.0) return 0;

            return (int)Math.Round(BarrierForceAt(x) * faded);
        }

        private ForceFrame ComposeNeutral(int x, int y)
        {
            ForceFrame f = new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off
            };

            f.ConstantX = Combine(LateralGuide(x, y), BarrierForceIn(x, y));

            // The horizontal guide. Lined up with a column this nearly vanishes so a gear can be
            // taken; between columns it is a full wall. The channel has width for the same reason
            // a slot does, so the stick is free within it rather than pulled to its centre line.
            double block = _geo.ChannelBlockFactor(x, _cfg.WallBlend);
            int plateau = (int)Math.Round(_channelGuideForce + (_channelWallForce - _channelGuideForce) * block);

            f.ConstantY = Saturating(
                y - GateGeometry.AxisCenter,
                plateau,
                _cfg.WallRamp,
                _geo.ChannelHalfEnter);

            return f;
        }

        private ForceFrame ComposeInColumn(Column column, ShiftDir direction, int x, int y)
        {
            ForceFrame f = new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off
            };

            // Laterally, exactly what the tunnel gets - the same expression, from the same guide
            // column, with the same barriers faded by the same depth. The latched column is
            // deliberately not consulted: while it was, this branch and the tunnel's disagreed by
            // nearly six newton-metres at the same position, and the mouth rang because of it.
            //
            // Fore and aft is the one thing a latch does change, and the only thing it changes:
            // the slot detent replaces the tunnel's gate wall, which is what makes a gear a place
            // the lever can go rather than a wall it bounces off.
            f.ConstantX = Combine(LateralGuide(x, y), BarrierForceIn(x, y));
            f.ConstantY = DetentMagnitude(direction, y);

            return f;
        }

        /// <summary>
        /// Restoring force toward a target: rises over <paramref name="ramp"/> counts to the
        /// plateau and then holds. The short ramp is what makes it read as a wall rather than a
        /// spring; the deadband keeps the stick from dithering when it is already on target.
        /// </summary>
        private static int Saturating(int displacement, int plateau, int ramp, int deadBand)
        {
            if (plateau <= 0) return 0;

            int magnitude = Math.Abs(displacement);
            if (magnitude <= deadBand) return 0;

            double t = GateGeometry.Clamp(
                (magnitude - deadBand) / (double)Math.Max(1, ramp), 0.0, 1.0);

            int force = (int)Math.Round(plateau * t);
            return displacement > 0 ? -force : force;
        }

        /// <summary>
        /// Half-width of the free corridor inside a slot, kept inside the band the state machine
        /// uses to decide the stick has left the column. A corridor wider than that would let the
        /// stick drift out of its own gear with no wall ever pushing back.
        /// </summary>
        /// <summary>
        /// Bite distance for a slot wall: the configured one, bounded only so the wall is at full
        /// strength before the neighbouring column's territory begins.
        /// </summary>
        private int SlotRamp(int corridor)
        {
            return Math.Min(_cfg.WallRamp, Math.Max(200, (_geo.ColumnSpacing / 2) - corridor));
        }

        private int SlotCorridor(Column column)
        {
            int limit = Math.Max(0, _geo.ColumnFreeHalfWidth(column) - 100);
            return GateGeometry.Clamp(_cfg.SlotHalfWidth, 0, limit);
        }

        /// <summary>Humps guarding the ordinary gaps, and the lockout gate guarding 7/R's gap.</summary>
        private int BarrierForceAt(int x)
        {
            int total = 0;

            for (int i = 0; i < GateGeometry.ColumnCount - 1; i++)
            {
                // Both the gate's position and which gap it guards come from the geometry, which
                // places it against the main section and follows the mirrored gear map.
                int d = x - _geo.BarrierCentre(i);

                total += i == _geo.LockoutGapIndex
                    ? LockoutGate(d, _lockoutForce, _geo.LockoutHalfWidth, _cfg.WallRamp, _geo.MirrorColumns)
                    : Hump(d, _barrierForce, _cfg.BarrierWidth);
            }

            return total;
        }

        /// <summary>
        /// The gate before 7/R: flat force across a compact band, pushing toward the main
        /// gears the whole way, free travel beyond. Flat because a gradient rings; one-way
        /// because an over-centre gate refunds past its crest the energy it charged before it,
        /// which lets a fast flick sail through for nearly nothing - measured by hand. With no
        /// refund, crossing costs the full fight at any speed, and the faces at both ends mean
        /// leaving 7/R winds up briefly and is then assisted, like a real range gate. It
        /// guards only the crossing; keeping the stick in the channel is the walls' job.
        /// </summary>
        private static int LockoutGate(int displacement, int strength, int halfWidth, int ramp, bool mirrored)
        {
            if (strength <= 0 || halfWidth <= 0) return 0;

            int m = Math.Abs(displacement);

            // The faces live *inside* the band, so the gate never reaches past the width it
            // declares. They used to overhang it by a whole bite distance, which ate the whole
            // clearance the gate is positioned with and started the toll on top of the 5/6
            // column - felt as a hard bump exactly where the hand expects to be resting on a
            // column. Capped at half the width so there is always a flat core between them.
            int face = GateGeometry.Clamp(ramp, 1, Math.Max(1, halfWidth / 2));

            double p = GateGeometry.Clamp((halfWidth - m) / (double)face, 0.0, 1.0);

            int force = (int)Math.Round(strength * p);
            return mirrored ? force : -force;
        }

        /// <summary>
        /// The force of pushing over a hump: zero at the crest, peaking at <paramref name="width"/>
        /// counts either side, fading away beyond. Smooth everywhere, so there is no step for the
        /// stick to chatter against, and it releases once the crest is behind you - which is what
        /// makes a lockout feel like it lets go when you are through.
        /// </summary>
        private static int Hump(int displacement, int strength, int width)
        {
            if (strength <= 0) return 0;

            double u = displacement / (double)Math.Max(1, width);
            double force = strength * u * Math.Exp(0.5 - (0.5 * u * u));

            return (int)Math.Round(force);
        }

        /// <summary>
        /// Slot detent along Y. Resists on the way in, flips over centre to pull the stick
        /// into the slot, then settles to a lighter seated hold.
        /// </summary>
        private int DetentMagnitude(ShiftDir direction, int y)
        {
            double d = _geo.EngageFraction(direction, y);

            double restoring;
            if (d < 0.55)
            {
                restoring = _detentResistMax * (d / 0.55);
            }
            else if (d < 0.80)
            {
                restoring = Lerp(_detentResistMax, -_detentPullMax, (d - 0.55) / 0.25);
            }
            else if (d < 1.00)
            {
                restoring = Lerp(-_detentPullMax, -_detentHold, (d - 0.80) / 0.20);
            }
            else
            {
                restoring = -_detentHold;
            }

            // "restoring" is positive toward the neutral channel; convert to axis direction.
            double signed = direction == ShiftDir.Fwd ? restoring : -restoring;
            return GateGeometry.Clamp((int)Math.Round(signed), -GateGeometry.ForceMax, GateGeometry.ForceMax);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * GateGeometry.Clamp(t, 0.0, 1.0);
        }
    }
}
