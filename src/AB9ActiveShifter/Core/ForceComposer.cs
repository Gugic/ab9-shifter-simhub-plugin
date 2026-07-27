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
        private readonly int _seqStopForce;
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

        /// <summary>
        /// How fast the absorber hands force back, in scale units per millisecond. Cuts are
        /// instant; recovery is slewed. The speed the yield keys on is an estimate that still
        /// carries some ripple from the device's ~500 Hz report quantisation, and an absorber
        /// that follows the estimate both ways renders that ripple as force texture - measured
        /// at 25-50% of the wall force at 250-500 Hz, felt as grinding against a running gear.
        /// Slewing only the recovery removes the texture without touching the feel: the
        /// same-direction test already returns full force the instant the wall is resisting
        /// again, so this can only ever deepen absorption, never soften a press.
        /// </summary>
        private readonly double _yieldRecoverPerMs;

        private double _yieldScaleX = 1.0;
        private double _yieldScaleY = 1.0;

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

        /// <summary>Widest mouth opening the settings ask for, before geometry trims it per flank.</summary>
        private readonly int _mouthOpening;

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
            _barrierForce = Force(config.BarrierForcePct, gain);
            _lockoutForce = Force(config.LockoutForcePct, gain);

            _detentResistMax = Force(config.DetentResistPct, gain);
            _detentPullMax = Force(config.DetentPullPct, gain);
            _detentHold = Force(config.DetentHoldPct, gain);
            _seqStopForce = Force(config.SeqStopForcePct, gain);
            _damperCoeff = Scale(config.DamperCoeff, gain);

            _dampingForce = Force(config.DampingPct, gain);
            _dampingPerCount = _dampingForce / (double)Math.Max(1, config.DampingReferenceSpeed);

            double yield = GateGeometry.Clamp(config.WallYieldPct, 0, 90) / 100.0;
            _yieldFloor = 1.0 - yield;
            _snickFloor = 1.0 - yield * 0.33;
            _yieldDeadband = Math.Max(0, config.YieldVelocityDeadband);
            _yieldBlend = Math.Max(1, config.YieldVelocityBlend);
            _yieldRecoverPerMs = config.YieldRecoveryMs <= 0 ? 1000.0 : 1.0 / config.YieldRecoveryMs;

            // The opening follows the reach, so the flank's slope is bounded whatever either dial is
            // set to. The rounded profile is scaled by 2/pi because a raised cosine's steepest point
            // is pi/2 times its average, and MouthSlopeMax is meant to be a ceiling on the steepest
            // point rather than on the average. Geometry trims further per flank; see MouthOpeningFor.
            double roundedScale = config.MouthShape == SlotMouthShape.Rounded ? 2.0 / Math.PI : 1.0;

            _mouthOpening = config.MouthShape == SlotMouthShape.Square
                ? 0
                : (int)Math.Round(Math.Max(0, config.MouthDepth) * EngineConfig.MouthSlopeMax * roundedScale
                                  * GateGeometry.Clamp(config.MouthOpenPct, 0, 100) / 100.0);

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
            int vx = 0, int vy = 0, double dtMs = 0, int vibY = 0, bool muteDetent = false)
        {
            if (_freeStick)
            {
                _shapedX = 0;
                _shapedY = 0;
                _yieldScaleX = 1.0;
                _yieldScaleY = 1.0;

                // Forget the guide column too. The lever can be moved anywhere while the forces are
                // off, and coming back with a stale column would aim a saturated wall at wherever
                // it used to be.
                _guideColumn = Column.None;
                return FreeFrame();
            }

            // A latched gear owns the lateral field: the wall points at the gear the lever is in,
            // so dragging sideways is pushed back to it rather than held in whichever slot the lever
            // was dragged to. Only in the tunnel does the guide follow the lever.
            //
            // This is the distinction I had wrong. Removing the latch entirely did fix the six
            // newton-metre step, but it also meant the wall followed the lever instead of the gear,
            // so a lever pushed across was held in the wrong slot. The step was never caused by
            // consulting the latch - it was caused by the two branches using different FORMULAS for
            // the same column. With one formula, the mouth of the column being entered gives
            // identical force either way, because there the latched column and the nearest column
            // are the same column. The only place they can differ is the tunnel, where the plateau
            // is the light detent, so the handover costs at most that.
            _guideColumn = state == GateState.Neutral
                ? _geo.GuideColumn(x, _guideColumn, _geo.InChannel(y))
                : column;

            ForceFrame frame = state == GateState.Neutral
                ? ComposeNeutral(x, y)
                : ComposeInColumn(column, direction, x, y, muteDetent);

            // The slot detent is the one force not shaped in time: the snick is a deliberate
            // transient, over in a few milliseconds by design, and it has to arrive whole to
            // read as a mechanism seating rather than a soft nudge. It also gets the milder
            // rebound floor, because it is supposed to do positive work.
            return Bound(frame, vx, vy, dtMs,
                         state == GateState.Neutral ? _yieldFloor : _snickFloor,
                         shapeY: state == GateState.Neutral,
                         vibY: vibY);
        }

        /// <summary>
        /// Forces for one sequential tick: the lever railed to the lateral centre and sprung
        /// back to the fore/aft centre, with a click at each shift threshold. No gate, no
        /// columns, no lockout - but the same stabiliser pipeline, and the measured polarity
        /// signs applied once at the same single place.
        /// </summary>
        public ForceFrame ComposeSequential(int x, int y, int vx = 0, int vy = 0, double dtMs = 0, int vibY = 0)
        {
            if (_freeStick)
            {
                _shapedX = 0;
                _shapedY = 0;
                _yieldScaleX = 1.0;
                _yieldScaleY = 1.0;
                _guideColumn = Column.None;
                return FreeFrame();
            }

            ForceFrame frame = new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off
            };

            // The lateral rail, at the wall's own stiffness: face derived from the plateau so
            // the one-stiffness rule holds here too.
            int face = GuideFace(_columnPinForce, 0);
            frame.ConstantX = Saturating(x - GateGeometry.AxisCenter, _columnPinForce, face, 0);

            frame.ConstantY = SequentialSpring(y);

            // The return spring keeps the snick's floor: pulling the lever home is its job, so
            // absorbing that assist would leave the lever limp on the way back. The end-stop is
            // the exception - it is a wall the lever gets banged against, so it takes the walls'
            // full absorption. The click is a drop and passes the time shaping instantly; the
            // build-up on the way out is slewed like any other wall.
            int stopStart = SequentialThreshold() + Math.Max(0, _cfg.SeqOvertravel);
            double floorY = Math.Abs(y - GateGeometry.AxisCenter) >= stopStart ? _yieldFloor : _snickFloor;

            return Bound(frame, vx, vy, dtMs, floorY, shapeY: true, vibY: vibY);
        }

        /// <summary>Distance from centre to the sequential firing line, in axis counts.</summary>
        private int SequentialThreshold()
        {
            return Math.Max(1, GateGeometry.AxisCenter - _geo.EngageDepth);
        }

        /// <summary>
        /// The sequential lever's fore/aft force: rises linearly from nothing at centre to the
        /// full resist at the shift threshold, drops to the lighter hold - the click - for the
        /// overtravel, then meets the end-stop wall, rising over the wall's bite on top of the
        /// hold. Everything is measured from the firing line, so shortening the throw moves the
        /// whole stroke together. Always toward centre, never over-centre: a sequential lever
        /// must come home on release, and a wall rather than a pocket means releasing inside
        /// the stop still returns it. The approach to the click is a shallow gradient by
        /// construction - full resist over the whole engage span - so no delay can pump it.
        /// </summary>
        private int SequentialSpring(int y)
        {
            ShiftDir dir = _geo.DirectionOf(y);
            int depth = Math.Abs(y - GateGeometry.AxisCenter);
            int threshold = SequentialThreshold();

            double magnitude;
            if (depth < threshold)
            {
                magnitude = _detentResistMax * (depth / (double)threshold);
            }
            else
            {
                magnitude = _detentHold;

                int intoStop = depth - threshold - Math.Max(0, _cfg.SeqOvertravel);
                if (intoStop > 0)
                {
                    double t = GateGeometry.Clamp(
                        intoStop / (double)Math.Max(1, _cfg.WallRamp), 0.0, 1.0);
                    magnitude += _seqStopForce * t;
                }
            }

            int force = (int)Math.Round(
                GateGeometry.Clamp(magnitude, 0, GateGeometry.ForceMax));

            // Fwd is low y; the restoring push is toward +y, and vice versa.
            return dir == ShiftDir.Fwd ? force : -force;
        }

        /// <summary>
        /// The shared back half of every composition: rebound yield, time shaping, damping, and
        /// the measured polarity signs applied once at the very end - the one place in the gate
        /// they are allowed to appear.
        ///
        /// Everything the hand can lean against is shaped in time, including the lockout. It
        /// was exempted at first on the theory that slewing a crossing hands a fast flick a
        /// discount, but the arithmetic does not support that: the lockout band is thousands
        /// of counts wide, so even a violent flick spends tens of milliseconds inside it while
        /// the attack lasts fifteen or twenty. What the exemption did buy was the one force in
        /// the gate still arriving raw - so the lockout rejected the lever hard where every
        /// wall had learned not to, and rang.
        /// </summary>
        private ForceFrame Bound(ForceFrame frame, int vx, int vy, double dtMs, double floorY, bool shapeY,
                                 int vibY = 0)
        {
            int boundedX = Yield(frame.ConstantX, vx, _yieldFloor, ref _yieldScaleX, dtMs);
            int boundedY = Yield(frame.ConstantY, vy, floorY, ref _yieldScaleY, dtMs);

            boundedX = ShapeInTime(ref _shapedX, boundedX, vx, dtMs);
            boundedY = shapeY
                ? ShapeInTime(ref _shapedY, boundedY, vy, dtMs)
                : Track(ref _shapedY, boundedY);

            // Damping joins after the yield and the time shaping - it opposes motion by
            // construction, so it can never be the assisting force the yield softens, and it
            // must keep its full bandwidth rather than being slewed.
            //
            // The telemetry vibration joins at the same point, for the mirror-image reason: a
            // carrier is keyed on time, not position, so it cannot form the loop those two
            // stages stabilise - and passing it through them would just filter the texture
            // away (a 15 ms attack is most of a cycle at 44 Hz, and half of every cycle
            // "assists"). It is still inside the final clamp and the polarity signs; being
            // zero-mean, a sign flip is only a phase shift.
            frame.ConstantX = Combine(boundedX, Damping(vx)) * _constantSignX;
            frame.ConstantY = Combine(Combine(boundedY, Damping(vy)), vibY) * _constantSignY;

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
        ///
        /// The scale is one-way in time: it drops to the speed's target instantly but climbs
        /// back at <see cref="_yieldRecoverPerMs"/>. Without that, the ripple in the speed
        /// estimate swept the scale across its whole blend range at 250-500 Hz and the wall
        /// force ground like gear teeth under every pressed slide. Holding the cut through the
        /// estimate's dips costs nothing: while it holds, the wall is by definition assisting,
        /// so a deeper cut is just more of the absorption the yield exists to provide.
        ///
        /// A dt of zero bypasses the memory and applies the speed's target directly, the same
        /// convention as the time shaping.
        /// </summary>
        private int Yield(int force, int velocity, double floor, ref double scale, double dtMs)
        {
            // Recovery happens on every tick, whatever else this one does; cuts are applied
            // after it, so a cut is instant and only the climb back is slewed.
            if (dtMs > 0)
                scale = Math.Min(1.0, scale + _yieldRecoverPerMs * dtMs);

            if (force == 0 || floor >= 1.0) return force;
            if (Math.Sign(force) != Math.Sign(velocity)) return force;

            int speed = Math.Abs(velocity);
            if (speed <= _yieldDeadband) return force;

            double t = GateGeometry.Clamp((speed - _yieldDeadband) / (double)_yieldBlend, 0.0, 1.0);
            double target = 1.0 - (1.0 - floor) * t;

            if (dtMs <= 0) return (int)Math.Round(force * target);

            // The floor differs between the wall and the snick; the state is shared per axis,
            // so it is clamped up to this call's floor rather than carrying a deeper cut across.
            scale = Math.Max(Math.Min(scale, target), floor);
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

            int depth = Math.Abs(y - GateGeometry.AxisCenter);
            int plateau = GuidePlateau(depth);
            if (plateau <= 0) return 0;

            int offset = x - _geo.ColumnTarget(_guideColumn);
            int corridor = SlotCorridor(_guideColumn) + MouthExtra(offset, depth, y);
            int face = GuideFace(plateau, corridor);

            return (int)Math.Round(Saturating(offset, plateau, face, corridor) * Relief(x, face));
        }

        /// <summary>
        /// How much of the lateral guide survives at this x: 1 out on the plate, 0 across every
        /// position the guide can change hands at, one wall face of flank in between.
        ///
        /// A MULTIPLIER on the finished force, and a function of position alone, and both of those
        /// are load-bearing. The obvious shape - truncate the plateau at a distance measured from the
        /// guide column - was built and refuted: the reach is then a property of WHICH column owns the
        /// field, and the latched column and the position-picked one differ. A flat plateau makes that
        /// handover free, because wherever both columns lie on the same side of the lever both
        /// saturate to the same value; a truncated one does not. Measured, that invented 10000 DI of
        /// history dependence - the full pin force at one physical position, selected by whether the
        /// lever had once dipped into the tunnel - and left a latched gear with no push-back at all
        /// over three quarters of the axis. A shared scalar cannot do either: the field becomes
        /// F_old(history) x Relief(x), so any two histories the old field made equal stay equal, and
        /// the wall still reaches the end of travel at full strength.
        ///
        /// The flank is one wall face, so its gradient is plateau/face, which
        /// <see cref="GuideFace"/> pins at pin force over the wall's bite - the same stiffness as
        /// every other lateral force, and independent of depth, so the flank adds no cross-gradient.
        /// </summary>
        private double Relief(int x, int face)
        {
            return GateGeometry.Clamp(
                _geo.HandoverClearance(x) / (double)Math.Max(1, face), 0.0, 1.0);
        }

        /// <summary>
        /// How much wider the slot is at this depth, on the flank the lever is on - the mouth shape.
        ///
        /// The shapes are rendered by moving the corridor's edge, never by adding a force. A
        /// chamfered divider end does not push a lever toward the next gear; it stops holding it
        /// back, and the hand's own lateral pressure does the rest. That is what makes the feature
        /// safe: nothing here can push outward, so there is no positive feedback to run away with,
        /// and the only gradient introduced is the flank's own slope, capped at
        /// <see cref="EngineConfig.MouthSlopeMax"/> - half the wall face at worst.
        /// </summary>
        private int MouthExtra(int offset, int depth, int y)
        {
            if (_mouthOpening <= 0 || offset == 0) return 0;

            // A slot that holds no gear has no mouth to shape - the divider runs straight
            // across it, and widening the corridor there would carve an entry into a wall
            // the state machine will never open.
            if (!_geo.SlotExists(_guideColumn, _geo.DirectionOf(y))) return 0;

            int side = offset > 0 ? 1 : -1;
            int reach = Math.Max(1, _cfg.MouthDepth);
            int into = depth - _geo.ChannelHalfEnter;
            if (into < 0 || into >= reach) return 0;

            double u = into / (double)reach;
            double profile;

            if (_cfg.MouthShape == SlotMouthShape.Angled)
            {
                // One flank only, and only where a next gear exists to be steered toward.
                if (side != _geo.SequentialBias(_guideColumn, _geo.DirectionOf(y))) return 0;
                profile = 1.0 - u;
            }
            else
            {
                // A raised cosine rather than a circular fillet. A true circle's flank goes vertical
                // where it meets the slot wall - an unbounded gradient at exactly the depth a hand
                // dwells. This leaves at zero slope on both ends instead, at the cost of peaking at
                // pi/2 times its average, which RoundedScale pays for by opening that much less.
                profile = 0.5 * (1.0 + Math.Cos(Math.PI * u));
            }

            return (int)Math.Round(MouthOpeningFor(side) * profile);
        }

        /// <summary>
        /// The widest this flank may open. Bounded by the neighbouring column's territory and, on a
        /// flank facing the lockout, by the gate's band - nothing belonging to a column may reach
        /// into the gate, or the toll's size would start depending on the mouth setting.
        /// </summary>
        private int MouthOpeningFor(int side)
        {
            int target = _geo.ColumnTarget(_guideColumn);
            int room = (_geo.ColumnSpacing / 2) - SlotCorridor(_guideColumn) - 200;

            int gapIndex = side > 0 ? (int)_guideColumn : (int)_guideColumn - 1;
            if (gapIndex == _geo.LockoutGapIndex)
            {
                int edge = side > 0
                    ? _geo.LockoutCentre - _geo.LockoutHalfWidth - target
                    : target - (_geo.LockoutCentre + _geo.LockoutHalfWidth);

                // Room for the wall's face as well as its corridor. Keeping only the corridor out of
                // the band is not enough: widening the corridor moves where the face begins, so the
                // force inside the gate's band changes even though nothing has crossed into it, and
                // the size of the toll would start depending on the mouth setting.
                int corridor = SlotCorridor(_guideColumn);
                room = Math.Min(room, edge - corridor - SlotRamp(corridor) - 100);
            }

            return GateGeometry.Clamp(Math.Min(_mouthOpening, room), 0, Math.Max(0, room));
        }

        /// <summary>
        /// How hard the lateral guide pushes at this depth: the light detent that parks the lever on
        /// a column in the tunnel, rising to the full slot wall by the time the channel is left.
        ///
        /// The rise happens ENTIRELY inside the channel's hysteresis band, and that is the whole
        /// point. There are two places a hand spends real time - sliding along the tunnel, and held
        /// in a slot - and in both of them the lateral field has to be a function of x alone. Any
        /// depth term there turns the lever's inevitable fore/aft wander into sideways force that
        /// arrives for no reason the hand can see.
        ///
        /// Both mistakes were made, one at a time, and both were felt immediately. Carrying the rise
        /// on past the exit band gave the slot walls a cross-gradient where they had none, so the
        /// wall grew as the lever was pushed in, and the guides leading down to each gear rang while
        /// the untouched deep walls stayed calm. Starting the rise at the centre line instead gave
        /// the TUNNEL a depth term, so sliding past the columns with the usual small fore/aft wander
        /// pushed and pulled the lever sideways at random.
        ///
        /// A transition has to exist somewhere - free to slide across, then held in a slot - and
        /// wherever it is, it is a cross-gradient. The band the state machine already calls "leaving
        /// the tunnel" is the honest place for it: a hand crosses that band on the way to a gear
        /// rather than dwelling in it. Widening the band is what buys the slope down, which is why
        /// the exit band is wider than it used to be.
        /// </summary>
        private int GuidePlateau(int depth)
        {
            if (depth <= _geo.ChannelHalfEnter) return _columnDetentForce;
            if (depth >= _geo.ChannelHalfExit) return _columnPinForce;

            int span = Math.Max(1, _geo.ChannelHalfExit - _geo.ChannelHalfEnter);
            double t = (depth - _geo.ChannelHalfEnter) / (double)span;
            return (int)Math.Round(Lerp(_columnDetentForce, _columnPinForce, t));
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
            double faded = 1.0 - _geo.SlotConfinementFactor(y);
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
            // a slot does, so the stick is free within it rather than pulled to its centre line -
            // unless the free depth has been dialled to zero, which turns the tunnel into a rail.
            // The force's own free depth is a separate dial from the state band on purpose, and
            // clamped to it from above: past the enter band the lever is leaving the tunnel, so a
            // force deadband wider than that would mean walls the state machine believes exist
            // and the hand never meets.
            double block = _geo.ChannelBlockFactor(x, _cfg.WallBlend, _geo.DirectionOf(y));
            int plateau = (int)Math.Round(_channelGuideForce + (_channelWallForce - _channelGuideForce) * block);

            f.ConstantY = Saturating(
                y - GateGeometry.AxisCenter,
                plateau,
                _cfg.WallRamp,
                GateGeometry.Clamp(_cfg.ChannelFreeDepth, 0, _geo.ChannelHalfEnter));

            return f;
        }

        private ForceFrame ComposeInColumn(Column column, ShiftDir direction, int x, int y, bool muteDetent)
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
            f.ConstantY = DetentMagnitude(direction, y, muteDetent);

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
            // Room for the rising face AND the relief flank, which is the same length, plus the
            // handover window they have to fit either side of. Halved for that reason: without it an
            // absurd bite makes the two ramps overlap and the wall never reaches full strength at all,
            // instead of merely reaching it late. Real bites are nowhere near this bound - at the
            // shipped geometry it allows 4711 - so this only ever catches a hostile setting.
            int room = (_geo.ColumnSpacing / 2) - corridor - _geo.DetentHysteresis;
            return Math.Min(_cfg.WallRamp, Math.Max(200, room / 2));
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

            for (int i = 0; i < _geo.ColumnCount - 1; i++)
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
        ///
        /// Muted - a grinding shift being balked - there is no crossover at all: the entry
        /// resistance rises and then simply stays, pushing the lever back out however deep it
        /// is held, the way a blocking synchro ring does. The moment the clutch goes down the
        /// normal profile returns and the pull arrives whole, like the snick it is.
        /// </summary>
        private int DetentMagnitude(ShiftDir direction, int y, bool muted)
        {
            double d = _geo.EngageFraction(direction, y);

            double restoring;
            if (muted)
            {
                restoring = _detentResistMax * Math.Min(1.0, d / 0.55);
            }
            else if (d < 0.55)
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
