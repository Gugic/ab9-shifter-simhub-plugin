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
        /// While pressed and still, force deviations up to this are held frozen instead of
        /// tracked. Sized to cover the face of a default wall (a few tens of counts of micro
        /// motion at the steepest gradient), well below any deliberate change of pressure.
        /// </summary>
        private const int StaticHoldBand = 2000;

        private readonly int _constantSignX;
        private readonly int _constantSignY;
        private readonly bool _freeStick;

        private int _shapedX;
        private int _shapedY;

        private Column _detentColumn = Column.C2;

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
                return FreeFrame();
            }

            ForceFrame frame = state == GateState.Neutral
                ? ComposeNeutral(x, y)
                : ComposeInColumn(column, direction, x, y);

            int boundedX = Yield(frame.ConstantX, vx, _yieldFloor);
            int boundedY = Yield(frame.ConstantY, vy, state == GateState.Neutral ? _yieldFloor : _snickFloor);

            // Time shaping applies to walls only - the surfaces a hand rests against. In the
            // neutral channel that is the fore/aft wall; in a column it is the slot wall. The
            // lockout, the humps and the detents are crossings: they exist to charge a price
            // for passing, and slewing them would hand a fast flick a discount.
            bool wallX = state != GateState.Neutral;
            bool wallY = state == GateState.Neutral;

            boundedX = wallX ? ShapeInTime(ref _shapedX, boundedX, vx, dtMs) : Track(ref _shapedX, boundedX);
            boundedY = wallY ? ShapeInTime(ref _shapedY, boundedY, vy, dtMs) : Track(ref _shapedY, boundedY);

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

            if (sameWall
                && Math.Abs(velocity) <= _yieldDeadband
                && Math.Abs(target - shaped) <= StaticHoldBand)
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

        private ForceFrame ComposeNeutral(int x, int y)
        {
            ForceFrame f = new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off
            };

            // Sliding along the channel is meant to feel like a real shifter: mostly free, with
            // a light pull into each column and a hump to climb between them.
            _detentColumn = _geo.NearestColumn(x, _detentColumn);

            int lateral = Saturating(
                x - _geo.ColumnTarget(_detentColumn),
                _columnDetentForce,
                _cfg.DetentRamp,
                _cfg.WallDeadBand);

            lateral += BarrierForceAt(x);

            f.ConstantX = GateGeometry.Clamp(lateral, -GateGeometry.ForceMax, GateGeometry.ForceMax);

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

            // The vertical guide: the two walls of the slot, with a free corridor between them.
            // Deliberately not a pull toward the centre line - that would put an equilibrium in
            // the middle of the slot for the stick to hunt around. Barriers are a neutral-channel
            // affair and stay out of it; once committed to a gear there is nothing to push through.
            //
            // The ramp is clamped so the wall reaches full strength before the state machine's
            // exit band. Otherwise a firm sideways lean could drop the gear while the wall was
            // still building, and the abrupt swap to neutral forces mid-lean is itself a source
            // of oscillation - as well as a gear falling out for no visible reason.
            int corridor = SlotCorridor(column);
            int ramp = Math.Min(
                _cfg.WallRamp,
                Math.Max(200, _geo.ColumnExitHalfWidth(column) - corridor - 150));

            f.ConstantX = Saturating(
                x - _geo.ColumnTarget(column),
                _columnPinForce,
                ramp,
                corridor);

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
        private int SlotCorridor(Column column)
        {
            int limit = Math.Max(0, _geo.ColumnFreeHalfWidth(column) - 100);
            return GateGeometry.Clamp(_cfg.SlotHalfWidth, 0, limit);
        }

        /// <summary>Humps guarding the ordinary gaps, and the lockout gate guarding 7/R's gap.</summary>
        private int BarrierForceAt(int x)
        {
            int total = 0;

            // The 7/R column sits at the far end of the gear map, which mirroring moves to the
            // other physical end of the gate - the lockout guards wherever 7/R actually is.
            int lockoutGap = _cfg.MirrorColumns ? 0 : GateGeometry.ColumnCount - 2;

            for (int i = 0; i < GateGeometry.ColumnCount - 1; i++)
            {
                int d = x - _geo.BarrierCentre(i);

                total += i == lockoutGap
                    ? LockoutGate(d, _lockoutForce, _cfg.LockoutHalfWidth, _cfg.WallRamp, _cfg.MirrorColumns)
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

            // The entry face is capped against the gate's own width, so a long wall bite
            // cannot stretch the band toward the columns.
            int face = Math.Max(Math.Min(ramp, halfWidth), 1);

            double p = GateGeometry.Clamp((halfWidth + face - m) / (double)face, 0.0, 1.0);

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
