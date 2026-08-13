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
        private readonly int _homeSpringForce;

        private readonly int _detentResistMax;
        private readonly int _detentPullMax;
        private readonly int _detentHold;
        private readonly int _grindWallForce;
        private readonly int _slotStopForce;
        private readonly int _seqStopForce;
        private readonly int _seqClickForce;
        private readonly int _prndDetentForce;
        private readonly int _prndStopForce;
        private readonly int _damperCoeff;

        // The PRND lockout band, resolved once: which device gap it owns (-1 for none), where
        // its crest is, and its clamped width. Everything label-relative comes in through the
        // resolution so MirrorSlots moves the band with P, R, N and D.
        private readonly int _prndLockoutForce;
        private readonly int _prndLockoutGapIndex = -1;
        private readonly int _prndLockoutCentre;
        private readonly int _prndLockoutHalfWidth;
        private readonly int _prndLockoutBlockSign;

        /// <summary>The lane band's own side latch, the fore/aft twin of <see cref="_lockoutSide"/>.</summary>
        private int _prndSide;

        /// <summary>
        /// Where the PRND positions are. Built from the same config the state machine builds its
        /// own from, so the detent the hand feels and the position the button reports cannot drift
        /// apart - the fore/aft twin of the rule that the lateral field and ColumnAt share one
        /// ownership answer.
        /// </summary>
        private readonly PrndLane _lane;

        /// <summary>How long the sequential click's kick lasts, in milliseconds.</summary>
        private const double SeqClickMs = 25.0;

        /// <summary>Milliseconds left of the kick fired by the last sequential shift.</summary>
        private double _seqClickLeftMs;

        /// <summary>Which way the firing stroke was travelling: -1 toward low y, +1 toward high.</summary>
        private int _seqClickSign;

        private readonly int _dampingForce;
        private readonly double _dampingPerCount;

        /// <summary>The gate surfaces' mu: friction cap as a fraction of the engaged force.</summary>
        private readonly double _wallFriction;

        /// <summary>
        /// Speed at which wall friction reaches its full Coulomb value, in counts per second.
        /// Below it friction is viscous - continuous through zero velocity, because a sign-flip
        /// at tremor speed would be a relay, the exact disease the yield deadband cures. Above
        /// it friction is flat, so it has no velocity gradient left to excite anything. Sits
        /// above the measured tremor envelope (~3700) and below the yield deadband, so the
        /// band the yield deliberately leaves untouched is exactly the band friction damps.
        /// </summary>
        private const int FrictionSaturationSpeed = 8000;

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

        /// <summary>
        /// "Still" for the static hold, in counts per second: the measured envelope of a hand
        /// holding against force is under 3700, so 4000 covers tremor and nothing else. This is
        /// deliberately NOT the yield's deadband. The two answer different questions - "is the
        /// hand at rest" versus "is this a launch" - and when they shared one constant, raising
        /// the yield's threshold to hand-adjustment speed would have let the freeze span real
        /// slow retreats, quantising the face into 20%-band force steps on the way out.
        /// </summary>
        private const int StaticHoldStillSpeed = 4000;

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

        /// <summary>
        /// Which side of the lockout gate the lever belongs to: -1 low-x, +1 high-x, 0 until a
        /// position is seen. The whole rendering of a Both-direction toll. A toll both ways
        /// cannot be a function of position alone - a position-only field refunds one crossing
        /// whatever it charges the other - so the band's sign comes from this latch instead:
        /// force always pushes back toward the side the lever entered from. The side re-derives
        /// only while the lever is OUTSIDE the band, so it can flip only after a complete
        /// crossing, at a position where the band's own faces have already tapered the force to
        /// zero - the same zero-crossing argument that makes ChannelBlockFactor's direction
        /// keying safe. A retreat re-derives the same side, so nothing is ever refunded.
        /// </summary>
        private int _lockoutSide;

        /// <summary>
        /// A hard-mode gate that has been re-engaged over a lever inside its band: armed, but
        /// holding fire until the lever is clear. With the attack off - the default - firing
        /// under the hand would be a full-strength step from nothing; waiting until the band's
        /// edge makes the moment of arming force-free by the band's own shape, and the wall is
        /// then met through its spatial face like any wall.
        /// </summary>
        private bool _lockoutHoldFire;

        /// <summary>Last tick's released flag, for the re-engage edge.</summary>
        private bool _lastLockoutReleased;

        /// <summary>Whether any tick has been composed, so the first counts as an engage edge.</summary>
        private bool _lockoutSeen;

        /// <summary>True while a hard-mode gate exerts nothing: released by the hotkey, or armed but holding fire.</summary>
        private bool _lockoutQuiet;

        /// <summary>
        /// The side a hard Both gate does NOT refuse: captured from the side latch at each
        /// arming edge, changed only by the next one. Deliberately not the live latch - an
        /// overpowered crossing flips the latch when the band is exited, and refusal keyed on
        /// the latch would lift the moment the fight was won. Locked means locked until the
        /// key turns; the crossed-to side becomes home only when the gate is re-engaged.
        /// </summary>
        private int _lockoutPermittedSide;

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

            // The hard modes pin the gate to full strength - that is what "hard" means - but
            // through the same Force path as everything else, so the overall gain and the 10%
            // unconfirmed-polarity cap still apply. Never a way around the cap.
            _lockoutForce = Force(
                config.LockoutMode == LockoutMode.PushThrough ? config.LockoutForcePct : 100, gain);
            _homeSpringForce = Force(config.HomeSpringPct, gain);

            _detentResistMax = Force(config.DetentResistPct, gain);
            _detentPullMax = Force(config.DetentPullPct, gain);
            _detentHold = Force(config.DetentHoldPct, gain);
            _grindWallForce = Force(config.GrindWallPct, gain);
            _slotStopForce = Force(config.SlotStopForcePct, gain);
            _seqStopForce = Force(config.SeqStopForcePct, gain);
            _seqClickForce = Force(config.SeqClickPct, gain);
            _prndDetentForce = Force(config.PrndDetentForcePct, gain);
            _prndStopForce = Force(config.PrndStopForcePct, gain);
            _lane = config.BuildPrndLane();

            // The lane's lockout, resolved like the gate's: label gap to device gap through the
            // mirror, strength pinned to full scale in the hard modes (through the gain and the
            // polarity cap, like everything), and the width clamped so the band is zero at both
            // neighbouring positions' notch edges - a position must stay a free region - and
            // floored so its faces never exceed the wall's own stiffness.
            _prndLockoutForce = Force(
                config.PrndLockoutMode == LockoutMode.PushThrough ? config.PrndLockoutForcePct : 100, gain);

            if (config.PrndLockoutGap != PrndLockoutGap.Off)
            {
                int labelGap = (int)config.PrndLockoutGap - 1;
                int deviceGap = config.MirrorSlots ? PrndLane.PositionCount - 2 - labelGap : labelGap;

                int notch = GateGeometry.Clamp(config.PrndNotchHalfWidth, 0, PrndNotchHalfWidthCeiling);
                int ceiling = (_lane.Spacing / 2) - notch;

                if (ceiling > 0 && _prndLockoutForce > 0)
                {
                    _prndLockoutGapIndex = deviceGap;
                    _prndLockoutCentre = _lane.CrestY(deviceGap);
                    _prndLockoutHalfWidth = GateGeometry.Clamp(
                        config.PrndLockoutHalfWidth,
                        Math.Min(2 * Math.Max(1, config.WallRamp), ceiling),
                        ceiling);

                    // Which device direction the paying crossing moves in: D sits at high y
                    // unmirrored and low y mirrored, and Both leaves the sign to the latch.
                    if (config.PrndLockoutDirection == PrndLockoutDirection.Both)
                    {
                        _prndLockoutBlockSign = 0;
                    }
                    else
                    {
                        bool towardD = config.PrndLockoutDirection == PrndLockoutDirection.TowardD;
                        _prndLockoutBlockSign = towardD == !config.MirrorSlots ? 1 : -1;
                    }
                }
            }

            _damperCoeff = Scale(config.DamperCoeff, gain);

            _dampingForce = Force(config.DampingPct, gain);
            _dampingPerCount = _dampingForce / (double)Math.Max(1, config.DampingReferenceSpeed);

            // No gain factor: friction is a fraction of the applied force, which already
            // carries the gain, the polarity cap and every percentage above it.
            _wallFriction = GateGeometry.Clamp(config.WallFrictionPct, 0, 100) / 100.0;

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
            int vx = 0, int vy = 0, double dtMs = 0, int vibY = 0, bool muteDetent = false,
            bool lockoutReleased = false)
        {
            if (_freeStick)
            {
                _shapedX = 0;
                _shapedY = 0;
                _yieldScaleX = 1.0;
                _yieldScaleY = 1.0;

                // Forget the guide column too. The lever can be moved anywhere while the forces are
                // off, and coming back with a stale column would aim a saturated wall at wherever
                // it used to be. The lockout's side latch is forgotten for the same reason:
                // coming back with a stale side would be coming back with a stale wall.
                _guideColumn = Column.None;
                _lockoutSide = 0;
                _lockoutHoldFire = false;
                _lockoutSeen = false;
                _lockoutPermittedSide = 0;
                return FreeFrame();
            }

            StepLockout(x, y, lockoutReleased);

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
            //
            // A balked slot is the exception to the exception: while the grind is rejecting
            // the gear there is no snick to protect - the detent has become a wall being
            // leaned on - so it takes the attack and the walls' full absorption like every
            // wall. The moment the clutch unmutes it, the snick's exemptions return with it.
            bool wallLike = state == GateState.Neutral || muteDetent;
            return Bound(frame, vx, vy, dtMs,
                         wallLike ? _yieldFloor : _snickFloor,
                         shapeY: wallLike,
                         vibY: vibY);
        }

        /// <summary>
        /// Forces for one sequential tick: the lever railed to the lateral centre and sprung
        /// back to the fore/aft centre, with a click at each shift threshold. No gate, no
        /// columns, no lockout - but the same stabiliser pipeline, and the measured polarity
        /// signs applied once at the same single place.
        ///
        /// <paramref name="clickNow"/> is the state machine's shift firing this tick. It
        /// starts the click's kick: a short burst in the stroke's own direction - the
        /// mechanism's stored energy letting go as the dogs drop in - which then throws the
        /// lever onto the end-stop wall, and the two together are the thunk. The kick is
        /// time-keyed rather than a spatial over-centre because a sequential lever must
        /// always come home to re-arm: a pocket in the spring profile could hold a released
        /// lever, a 25 ms burst cannot. It joins beside the telemetry carrier, after the
        /// yield and the attack - it assists motion by definition, so the absorber would eat
        /// it, and the attack would blunt most of it.
        /// </summary>
        public ForceFrame ComposeSequential(int x, int y, int vx = 0, int vy = 0, double dtMs = 0, int vibY = 0,
                                            bool clickNow = false)
        {
            if (_freeStick)
            {
                _shapedX = 0;
                _shapedY = 0;
                _yieldScaleX = 1.0;
                _yieldScaleY = 1.0;
                _seqClickLeftMs = 0;
                _guideColumn = Column.None;
                return FreeFrame();
            }

            if (clickNow && _seqClickForce > 0)
            {
                _seqClickLeftMs = SeqClickMs;

                // Fwd is low y; assisting a forward stroke pushes toward -y.
                _seqClickSign = _geo.DirectionOf(y) == ShiftDir.Fwd ? -1 : 1;
            }

            int click = 0;
            if (_seqClickLeftMs > 0)
            {
                click = _seqClickSign * (int)Math.Round(_seqClickForce * (_seqClickLeftMs / SeqClickMs));
                if (dtMs > 0) _seqClickLeftMs = Math.Max(0, _seqClickLeftMs - dtMs);
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
            int stopStart = ThrowThreshold() + Math.Max(0, _cfg.SeqOvertravel);
            double floorY = Math.Abs(y - GateGeometry.AxisCenter) >= stopStart ? _yieldFloor : _snickFloor;

            return Bound(frame, vx, vy, dtMs, floorY, shapeY: true, vibY: Combine(vibY, click));
        }

        /// <summary>
        /// Forces for one PRND tick: the lever railed to the lateral centre, and a lane of four
        /// detented positions fore and aft. No gate, no columns, no gear - but the same stabiliser
        /// pipeline and the same single place the measured polarity signs are applied.
        /// </summary>
        public ForceFrame ComposePrnd(int x, int y, int vx = 0, int vy = 0, double dtMs = 0, int vibY = 0,
                                      bool lockoutReleased = false)
        {
            if (_freeStick)
            {
                _shapedX = 0;
                _shapedY = 0;
                _yieldScaleX = 1.0;
                _yieldScaleY = 1.0;
                _guideColumn = Column.None;
                _prndSide = 0;
                _lockoutHoldFire = false;
                _lockoutSeen = false;
                _lockoutPermittedSide = 0;
                return FreeFrame();
            }

            StepPrndLockout(y, lockoutReleased);

            ForceFrame frame = new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off
            };

            // The same lateral rail the sequential lever gets, at the wall's own stiffness.
            int face = GuideFace(_columnPinForce, 0);
            frame.ConstantX = Saturating(x - GateGeometry.AxisCenter, _columnPinForce, face, 0);

            frame.ConstantY = PrndLaneForce(y);

            return Bound(frame, vx, vy, dtMs, _yieldFloor, shapeY: true, vibY: vibY);
        }

        /// <summary>
        /// The PRND lane's fore/aft force: a free notch at each position, a smooth hump between
        /// each pair, and a wall past either end.
        ///
        /// The hump is a raised cosine across the room between the notches - zero at the notch's
        /// edge, zero again at the crest, peaking halfway. Both zeros are load-bearing. The one at
        /// the notch edge is what keeps a selected position a REGION rather than a point, the same
        /// rule that makes slots corridors. The one at the crest is what makes the handover free:
        /// this function picks the nearest position, and a nearest-anything field flips at the
        /// midpoint - which on the lateral axis is a step of twice the plateau and cost a whole
        /// relief window to pay for. Here the force is already nothing where the flip happens, so
        /// there is nothing to pay. Pinned by <c>NoSingleCountOfTheLaneEverStepsTheForce</c>.
        ///
        /// Raised cosine rather than a half sine for the same reason the rounded slot mouth is one:
        /// a sine leaves at full slope where it meets the free space, so the notch edge and the
        /// crest would both be corners - the two places a hand dwells. This leaves at zero slope at
        /// both ends and pays for it by peaking at pi/2 times its average, which is exactly what
        /// <see cref="PrndNotchHalfWidthCeiling"/>'s span floor is sized for.
        ///
        /// The notch is clamped so the hump keeps at least the wall's own stiffness - see
        /// <see cref="PrndNotchHalfWidthCeiling"/>, which the Feel tab prints rather than clamping
        /// silently. Public because the Feel tab's stroke curve plots this rather than a copy.
        /// </summary>
        public int PrndLaneForce(int y)
        {
            return PrndLaneForce(y, _prndSide);
        }

        /// <summary>
        /// The same force with the band's side latch supplied, so a graph can sweep a
        /// Both-direction band from either approach without touching live state - the lane's
        /// twin of the <see cref="BarrierForceIn(int, int, int)"/> overload.
        /// </summary>
        public int PrndLaneForce(int y, int lockoutSide)
        {
            int fromCentre = y - GateGeometry.AxisCenter;

            // Past either end of the lane: a wall home, never a pocket.
            int overshoot = Math.Abs(fromCentre) - _lane.HalfLength;
            if (overshoot > 0)
            {
                double climb = GateGeometry.Clamp(
                    overshoot / (double)Math.Max(1, _cfg.WallRamp), 0.0, 1.0);
                int wall = (int)Math.Round(_prndStopForce * climb);
                return fromCentre > 0 ? -wall : wall;
            }

            int nearest = NearestPrndPosition(y);
            int offset = y - _lane.PositionY(nearest);

            // The gap this point lies in. In the locked one the cosine hump is REPLACED by the
            // gate band - the exact precedent of the H gate's own dispatch - centred on the
            // crest, zero before both neighbouring notches by the width clamp, so a position
            // stays a free region. The crest deliberately carries the band's flat core: that IS
            // the toll, and the nearest-position flip there is free for the band because the
            // band is one continuous function of the crest offset, not a nearest-field. A hard
            // band that is released or holding fire leaves the gap simply free.
            int gapIndex = offset > 0 ? nearest : nearest - 1;
            if (gapIndex == _prndLockoutGapIndex)
            {
                if (_lockoutQuiet) return 0;

                int pushSign = _prndLockoutBlockSign != 0 ? -_prndLockoutBlockSign : lockoutSide;
                return LockoutGate(y - _prndLockoutCentre, _prndLockoutForce,
                    _prndLockoutHalfWidth, _cfg.WallRamp, pushSign);
            }

            if (_prndDetentForce <= 0) return 0;

            int free = GateGeometry.Clamp(_cfg.PrndNotchHalfWidth, 0, PrndNotchHalfWidthCeiling);
            int span = Math.Max(1, (_lane.Spacing / 2) - free);

            int into = Math.Abs(offset) - free;
            if (into <= 0) return 0;

            double u = GateGeometry.Clamp(into / (double)span, 0.0, 1.0);
            double profile = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * u));

            int force = (int)Math.Round(_prndDetentForce * profile);
            return offset > 0 ? -force : force;
        }

        /// <summary>
        /// Per-tick state for the lane's band: the fore/aft side latch and the hard modes'
        /// arming, the exact machinery the gap band runs on the other axis. The lane's lockout
        /// never touches the selector's state machine - a selector is always in exactly one
        /// position and its buttons follow the lever - so everything here is force only.
        /// </summary>
        private void StepPrndLockout(int y, bool released)
        {
            if (_prndLockoutGapIndex < 0)
            {
                _lockoutQuiet = false;
                return;
            }

            bool insideBand = Math.Abs(y - _prndLockoutCentre) < _prndLockoutHalfWidth;

            if (_prndSide == 0 || !insideBand)
            {
                _prndSide = y >= _prndLockoutCentre ? 1 : -1;
            }

            StepArming(_cfg.PrndLockoutMode, released, !insideBand);
        }

        /// <summary>The lane band's side latch, for the engine's auto re-arm - the fore/aft twin of <see cref="LockoutSideLatch"/>.</summary>
        public int PrndLockoutSideLatch { get { return _prndSide; } }

        /// <summary>
        /// The widest the lane's lockout band can be: to the notch edges of the two positions
        /// bounding its gap, never into them. Zero means the lane has no room for a band at
        /// all - the lane itself is the thing to lengthen, and the Feel tab says so.
        /// </summary>
        public int PrndLockoutHalfWidthCeiling
        {
            get
            {
                int notch = GateGeometry.Clamp(_cfg.PrndNotchHalfWidth, 0, PrndNotchHalfWidthCeiling);
                return Math.Max(0, (_lane.Spacing / 2) - notch);
            }
        }

        /// <summary>The band half-width actually in force after the clamps, 0 when there is no band.</summary>
        public int PrndLockoutEffectiveHalfWidth { get { return _prndLockoutHalfWidth; } }

        /// <summary>
        /// Nearest by distance, and deliberately not the state machine's answer. The force has to
        /// be a function of position alone - the same rule the lateral field follows, and for the
        /// same reason: a force that consulted the held position would give two different answers
        /// at one place depending how the lever arrived there.
        /// </summary>
        private int NearestPrndPosition(int y)
        {
            int best = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < PrndLane.PositionCount; i++)
            {
                int distance = Math.Abs(y - _lane.PositionY(i));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// The widest PRND notch that still leaves the hump beside it room to rise at no more than
        /// the wall's own stiffness.
        ///
        /// A raised cosine's steepest point is pi/2 times its average, so a hump spanning pi wall
        /// bites has exactly the gradient a wall face of one bite would - the same correction the
        /// rounded slot mouth applies with its 2/pi factor, arrived at from the other end. Zero when the
        /// lane is so short that even a notchless detent cannot have that much room, which is
        /// honest rather than helpful: at that point the lane itself is the thing to lengthen, and
        /// the Feel tab says so.
        ///
        /// Exposed for the same reason WallRampCeiling and ChannelFreeDepthCeiling are - a dial
        /// bounded by geometry rather than by its slider has to say so on screen.
        /// </summary>
        public int PrndNotchHalfWidthCeiling
        {
            get
            {
                int floor = (int)Math.Round(Math.PI * Math.Max(1, _cfg.WallRamp));
                return Math.Max(0, (_lane.Spacing / 2) - floor);
            }
        }

        /// <summary>
        /// The throw: distance from centre to the line a push registers at, in axis counts. One
        /// number for both patterns because it is one stored fact - <see cref="EngineConfig.EngageDepth"/>
        /// measures the same line from the end of travel instead. The sequential lever fires here;
        /// an H slot seats here. The PRND lane does not use it: its length is its own dial, because
        /// a selector lane is a fraction of the length a gear lever wants.
        /// </summary>
        private int ThrowThreshold()
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
            int threshold = ThrowThreshold();

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

            // Friction and damping join after the yield and the time shaping - they oppose
            // motion by construction, so they can never be the assisting force the yield
            // softens, and they must keep their full bandwidth rather than being slewed.
            // Friction takes the SHAPED force as its normal load on purpose: the attack ramps
            // the wall in, so friction winds up with it instead of arriving as its own step,
            // and a yielded wall grips proportionally less.
            //
            // The telemetry vibration joins at the same point, for the mirror-image reason: a
            // carrier is keyed on time, not position, so it cannot form the loop those two
            // stages stabilise - and passing it through them would just filter the texture
            // away (a 15 ms attack is most of a cycle at 44 Hz, and half of every cycle
            // "assists"). It is still inside the final clamp and the polarity signs; being
            // zero-mean, a sign flip is only a phase shift. Friction never keys on it: a
            // carrier is not a load the lever is pressed against.
            frame.ConstantX = Combine(Combine(boundedX, Friction(boundedX, vx)), Damping(vx)) * _constantSignX;
            frame.ConstantY = Combine(Combine(Combine(boundedY, Friction(boundedY, vy)), Damping(vy)), vibY)
                              * _constantSignY;

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
                && Math.Abs(velocity) <= StaticHoldStillSpeed
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
        /// The rebound absorber. A force resisting the stick's motion passes through whole - a
        /// wall being pushed on is never soft. A force accelerating the stick along its existing
        /// motion, faster than the deadband, is scaled toward the floor as speed grows, so a
        /// bounce off a wall returns less energy than the push stored.
        ///
        /// The deadband is the lean-or-launch line, and everything inside it - still, tremor,
        /// a hand adjusting its grip, either direction - gets the HELD scale, never a fresh cut
        /// and never an instant restore. Both halves of that were learned from real traces. A
        /// fresh cut at leaning speed turns the absorber into a relay: every micro-reversal
        /// fires it, every firing steps the force by the yield fraction, and the step kicks the
        /// lever into a bigger reversal - measured as 26 Hz chatter in a slot and a 12 Hz
        /// rebound off the lockout when the deadband sat at tremor level. An instant restore
        /// inside the deadband reopens the opposite hole: the speed estimate ripples under the
        /// device's ~500 Hz report quantisation, and a dip below the deadband would strobe a
        /// held cut back to full at that rate - the gear-grinding texture the slewed recovery
        /// exists to prevent.
        ///
        /// The scale is one-way in time: it drops to the speed's target instantly but climbs
        /// back at <see cref="_yieldRecoverPerMs"/>. Holding the cut through the estimate's
        /// dips costs nothing a hand can feel: a lean without a recent bounce has a scale of
        /// one, and after a caught bounce the wall firms back up over the recovery time.
        ///
        /// A dt of zero bypasses the memory - the speed's target directly while assisting,
        /// whole force otherwise - the same convention as the time shaping.
        /// </summary>
        private int Yield(int force, int velocity, double floor, ref double scale, double dtMs)
        {
            // Recovery happens on every tick, whatever else this one does; cuts are applied
            // after it, so a cut is instant and only the climb back is slewed.
            if (dtMs > 0)
                scale = Math.Min(1.0, scale + _yieldRecoverPerMs * dtMs);

            if (force == 0 || floor >= 1.0) return force;

            int speed = Math.Abs(velocity);
            bool assisting = Math.Sign(force) == Math.Sign(velocity);

            if (speed <= _yieldDeadband)
            {
                // Inside the deadband the sign of the velocity is tremor, not intent, so it
                // must not select between two different forces - that selection was the relay.
                if (dtMs <= 0) return force;
                return (int)Math.Round(force * scale);
            }

            if (!assisting) return force;

            double t = GateGeometry.Clamp((speed - _yieldDeadband) / (double)_yieldBlend, 0.0, 1.0);
            double target = 1.0 - (1.0 - floor) * t;

            if (dtMs <= 0) return (int)Math.Round(force * target);

            // The floor differs between the wall and the snick; the state is shared per axis,
            // so it is clamped up to this call's floor rather than carrying a deeper cut across.
            scale = Math.Max(Math.Min(scale, target), floor);
            return (int)Math.Round(force * scale);
        }

        /// <summary>
        /// Kinetic friction at the walls: force opposing motion, capped at the surface's mu
        /// times the wall force currently applied on this axis - so it is exactly zero in free
        /// travel, the corridors and the channel, and costs nothing in lightness. Viscous up to
        /// <see cref="FrictionSaturationSpeed"/>, Coulomb-flat beyond.
        ///
        /// This is the dissipation for the band the other stabilisers deliberately leave
        /// alone. Below the yield deadband nothing may cut, because leaning must be solid;
        /// the static hold only guards a hand already settled; global damping is banned from
        /// free travel. What remained there was a face gradient rendered through the loop's
        /// delay - negative damping, ~0.011 DI per count/s at the shipped stiffness - and a
        /// hand, and that hunted: with the yield relay fixed, the lockout trace still showed
        /// a 17.7 Hz, 8000-count cycle riding the entry face. At the default mu this term is
        /// roughly seventeen times the delay's negative damping, which is what lets a lean
        /// settle onto a face instead of orbiting it.
        /// </summary>
        private int Friction(int applied, int velocity)
        {
            if (_wallFriction <= 0 || applied == 0 || velocity == 0) return 0;

            double cap = Math.Abs(applied) * _wallFriction;
            double force = -velocity * (cap / FrictionSaturationSpeed);
            return (int)Math.Round(GateGeometry.Clamp(force, -cap, cap));
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
        ///
        /// Takes the guide column as a parameter rather than reading the private _guideColumn
        /// field directly - MouthExtra and MouthOpeningFor below do the same - so the Feel tab's
        /// Sliding Across The Gate visualization can sweep every column across the whole gate
        /// width for a static plot without touching the live per-tick state Compose() tracks.
        /// Public for the same reason.
        /// </summary>
        public int LateralGuide(int x, int y, Column guideColumn)
        {
            if (guideColumn == Column.None) return 0;

            int depth = Math.Abs(y - GateGeometry.AxisCenter);
            int plateau = GuidePlateau(depth);
            if (plateau <= 0) return 0;

            int offset = x - _geo.ColumnTarget(guideColumn);
            int corridor = SlotCorridor(guideColumn) + MouthExtra(offset, depth, y, guideColumn);
            int face = GuideFace(plateau, corridor);

            return (int)Math.Round(Saturating(offset, plateau, face, corridor) * Relief(x, y, face));
        }

        /// <summary>
        /// How much of the lateral guide survives here: 1 out on the plate, 0 across every position
        /// the guide can change hands at, one wall face of flank in between - and 1 again, whatever
        /// x says, once the tunnel has been left behind.
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
        /// F_old(history) x Relief(position), so any two histories the old field made equal stay equal.
        ///
        /// The depth term is the fix for what the window cost when it had none. Relief applied at
        /// every depth means the slot walls are holed wherever the tunnel needs a handover, and a
        /// gear latched at full deflection then has three places across the gate with no lateral
        /// wall at all - measured on the rig at exactly 0 DI, at the two ordinary gaps and across
        /// the lockout's whole doorway, and a hand hunting sideways settles into every one of them.
        /// They read as extra half-slots that do not change gear, which is precisely what the
        /// absolute latch promises cannot exist.
        ///
        /// Fading it out is only sound because the handover is gone by then: GuideColumn freezes
        /// the pick outside the tunnel, so below <see cref="GateGeometry.ChannelHalfEnter"/> the
        /// field has nothing left to discontinue. The fade rides SlotConfinementFactor - the same
        /// band, the same shape as the plateau's own rise - so the window is at full strength
        /// through every depth a handover can still happen at, and the two depth terms are bounded
        /// by the same span rather than by two spans that could be typed apart.
        ///
        /// The flank is one wall face, so its gradient is plateau/face, which
        /// <see cref="GuideFace"/> pins at pin force over the wall's bite - the same stiffness as
        /// every other lateral force, and independent of depth, so the flank adds no cross-gradient.
        /// </summary>
        private double Relief(int x, int y, int face)
        {
            double window = GateGeometry.Clamp(
                _geo.HandoverClearance(x) / (double)Math.Max(1, face), 0.0, 1.0);

            // 1 anywhere inside the tunnel's enter band, 0 by its exit band: exactly the depths
            // where the pick is still live, and exactly the band the plateau rises across.
            double inTunnel = 1.0 - _geo.SlotConfinementFactor(y);

            return 1.0 - ((1.0 - window) * inTunnel);
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
        private int MouthExtra(int offset, int depth, int y, Column guideColumn)
        {
            if (_mouthOpening <= 0 || offset == 0) return 0;

            // A slot that holds no gear has no mouth to shape - the divider runs straight
            // across it, and widening the corridor there would carve an entry into a wall
            // the state machine will never open.
            if (!_geo.SlotExists(guideColumn, _geo.DirectionOf(y))) return 0;

            int side = offset > 0 ? 1 : -1;
            int reach = Math.Max(1, _cfg.MouthDepth);
            int into = depth - _geo.ChannelHalfEnter;
            if (into < 0 || into >= reach) return 0;

            double u = into / (double)reach;
            double profile;

            if (_cfg.MouthShape == SlotMouthShape.Angled)
            {
                // One flank only, and only where a next gear exists to be steered toward.
                if (side != _geo.SequentialBias(guideColumn, _geo.DirectionOf(y))) return 0;
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

            return (int)Math.Round(MouthOpeningFor(side, guideColumn) * profile);
        }

        /// <summary>
        /// The widest this flank may open. Bounded by the neighbouring column's territory and, on a
        /// flank facing the lockout, by the gate's band - nothing belonging to a column may reach
        /// into the gate, or the toll's size would start depending on the mouth setting.
        /// </summary>
        private int MouthOpeningFor(int side, Column guideColumn)
        {
            int target = _geo.ColumnTarget(guideColumn);
            int room = (_geo.ColumnSpacing / 2) - SlotCorridor(guideColumn) - 200;

            int gapIndex = side > 0 ? (int)guideColumn : (int)guideColumn - 1;
            if (gapIndex == _geo.LockoutGapIndex)
            {
                int edge = side > 0
                    ? _geo.LockoutCentre - _geo.LockoutHalfWidth - target
                    : target - (_geo.LockoutCentre + _geo.LockoutHalfWidth);

                // Room for the wall's face as well as its corridor. Keeping only the corridor out of
                // the band is not enough: widening the corridor moves where the face begins, so the
                // force inside the gate's band changes even though nothing has crossed into it, and
                // the size of the toll would start depending on the mouth setting.
                int corridor = SlotCorridor(guideColumn);
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
        /// Per-tick lockout state: the side latch, and a hard gate's arming. Runs in both state
        /// branches at every depth, before anything is composed - anything indexed on the state
        /// machine puts a step back, and the latch updating at depth is deliberate: a diagonal
        /// dive under the band flips the side silently where the depth fade already has the
        /// force at zero, so surfacing on the far side owes the return toll instead of being
        /// assisted back across.
        /// </summary>
        private void StepLockout(int x, int y, bool released)
        {
            if (_geo.LockoutIsSlot)
            {
                StepArming(_cfg.LockoutMode, released, SlotLockoutClearAt(x, y));
                return;
            }

            if (!_geo.HasLockout)
            {
                _lockoutQuiet = false;
                return;
            }

            bool insideBand = Math.Abs(x - _geo.LockoutCentre) < _geo.LockoutHalfWidth;

            // The side re-derives only while the lever is outside the band: entering keeps the
            // side of entry until the lever has fully exited on the other side, and the flip
            // lands where the band's own faces have the force at zero. Full-band hysteresis,
            // so tremor on an edge cannot be a relay - the force there is nothing.
            if (_lockoutSide == 0 || !insideBand)
            {
                _lockoutSide = x >= _geo.LockoutCentre ? 1 : -1;
            }

            StepArming(_cfg.LockoutMode, released, !insideBand);
        }

        /// <summary>
        /// The hard modes' arming, shared by the gap band and the slot lockout. An arming edge -
        /// the hotkey re-engaging, a fresh composer after a config swap, the first tick after
        /// free stick - over a lever inside the guarded region holds fire until the lever is
        /// clear, so full force can never materialise under the hand (the attack is off by
        /// default, so nothing else would soften it). "Clear" always means a position where the
        /// lockout's own shape is zero, so the moment of firing is force-free by construction.
        /// </summary>
        private void StepArming(LockoutMode mode, bool released, bool clear)
        {
            if (mode == LockoutMode.PushThrough)
            {
                _lockoutQuiet = false;
                _lockoutSeen = true;
                return;
            }

            bool engaged = !released;
            bool wasEngaged = _lockoutSeen && !_lastLockoutReleased;

            if (engaged && !wasEngaged)
            {
                _lockoutHoldFire = !clear;

                // The refusal's home side is set when the key turns, not by the fight: an
                // overpowered crossing must stay refused until the next release.
                _lockoutPermittedSide = _lockoutSide;
            }

            if (_lockoutHoldFire && clear) _lockoutHoldFire = false;

            _lastLockoutReleased = released;
            _lockoutSeen = true;
            _lockoutQuiet = released || _lockoutHoldFire;
        }

        /// <summary>
        /// Whether a slot lockout's shapes are all zero here: the channel, another column, the
        /// other direction - and for an exit toll, the seat above its band and the mouth below
        /// it. The entry balk rises from the mouth itself, so with entry covered only leaving
        /// the slot's territory counts as clear.
        /// </summary>
        private bool SlotLockoutClearAt(int x, int y)
        {
            if (_geo.InChannel(y)) return true;
            if (_geo.ColumnAt(x) != _geo.LockoutSlotColumn) return true;

            ShiftDir dir = _geo.DirectionOf(y);
            if (dir != _geo.LockoutSlotDir) return true;

            if (_cfg.LockoutSlotDirection == LockoutSlotDirection.Exit)
            {
                double d = _geo.EngageFraction(dir, y);
                return d <= SlotExitBandFoot || d >= SlotExitBandTop;
            }

            return false;
        }

        /// <summary>
        /// The side latch, for the engine's auto re-arm: it lands opposite the permitted side
        /// exactly when a crossing has fully completed, band exited and all.
        /// </summary>
        public int LockoutSideLatch { get { return _lockoutSide; } }

        /// <summary>The permitted side as captured at the last arming edge, 0 before any.</summary>
        public int LockoutPermittedSide { get { return _lockoutPermittedSide; } }

        /// <summary>
        /// Whether a hard lockout refuses to let this slot latch. Feeds the state machine's
        /// allowEngage beside the grind's rejection, and ONLY that: refusal can block a new
        /// latch, never drop a gear already held - the lever has not moved, and a release goes
        /// through the neutral channel absolutely, so an armed gate must not turn a held gear
        /// into a phantom. Exit tolls never refuse either: refusing a release would hold a
        /// button down while the lever physically leaves, and the buttons follow truth.
        /// </summary>
        public bool LockoutRefusesEngage(Column column, ShiftDir direction)
        {
            if (_cfg.LockoutMode == LockoutMode.PushThrough) return false;
            if (_lockoutQuiet) return false;
            if (column == Column.None || direction == ShiftDir.None) return false;

            if (_geo.LockoutIsSlot)
            {
                return column == _geo.LockoutSlotColumn
                    && direction == _geo.LockoutSlotDir
                    && _cfg.LockoutSlotDirection != LockoutSlotDirection.Exit;
            }

            if (!_geo.HasLockout) return false;

            // One-way: the refused side is fixed by the direction dial, whatever the lever
            // does - the accidental-gear guarantee. Both: the side opposite the captured home.
            int columnSide = _geo.ColumnTarget(column) >= _geo.LockoutCentre ? 1 : -1;
            int refusedSide = _geo.LockoutBlockSign != 0 ? _geo.LockoutBlockSign : -_lockoutPermittedSide;

            return refusedSide != 0 && columnSide == refusedSide;
        }

        /// <summary>
        /// The humps, the lockout gate, and the neutral home spring, all faded out with depth. A
        /// plate has its gate cut into the tunnel, not into the slots, so below the channel the
        /// slot walls own the lateral axis alone. Applied in every state, like the guide, because
        /// anything indexed on the state machine puts the step back.
        ///
        /// Public alongside LateralGuide for the Feel tab's Sliding Across The Gate
        /// visualization - the two together are exactly ComposeNeutral's own ConstantX.
        /// </summary>
        public int BarrierForceIn(int x, int y)
        {
            return BarrierForceIn(x, y, _lockoutSide);
        }

        /// <summary>
        /// The same field with the side latch supplied, so a graph can sweep a Both-direction
        /// gate from either approach without touching live state - the same reason
        /// <see cref="LateralGuide"/> takes its guide column as a parameter.
        /// </summary>
        public int BarrierForceIn(int x, int y, int lockoutSide)
        {
            double faded = 1.0 - _geo.SlotConfinementFactor(y);
            if (faded <= 0.0) return 0;

            return (int)Math.Round((BarrierForceAt(x, lockoutSide) + HomeSpringAt(x)) * faded);
        }

        /// <summary>
        /// The neutral spring: a flat pull toward the home column - the 3/4 gate, where a real
        /// H lever rests - dead across that column's own width, rising over one face, saturated
        /// everywhere beyond. The deadband is what keeps it off the oscillator list: the
        /// equilibrium is a flat region, not a point, the same trick the column detent uses.
        /// Continuous in x and anchored to one fixed column, so unlike the nearest-column guide
        /// it has no handover to relieve.
        ///
        /// The face keeps the one-stiffness rule without GuideFace's upper clamp: a spring set
        /// stronger than the slot wall gets a LONGER face at the same slope, never a steeper
        /// one, because that clamp's assumption - every plateau is at most the pin force - is
        /// the one thing this dial can break.
        /// </summary>
        private int HomeSpringAt(int x)
        {
            if (_homeSpringForce <= 0) return 0;

            int ramp = SlotRamp(0);
            int face = _columnPinForce > 0
                ? Math.Max(1, (int)Math.Round(ramp * (double)_homeSpringForce / _columnPinForce))
                : ramp;

            Column home = _geo.HomeColumn;
            return Saturating(
                x - _geo.ColumnTarget(home),
                _homeSpringForce,
                face,
                _geo.ColumnFreeHalfWidth(home));
        }

        private ForceFrame ComposeNeutral(int x, int y)
        {
            ForceFrame f = new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off
            };

            f.ConstantX = Combine(LateralGuide(x, y, _guideColumn), BarrierForceIn(x, y));

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
            f.ConstantX = Combine(LateralGuide(x, y, _guideColumn), BarrierForceIn(x, y));
            f.ConstantY = SlotForceAt(direction, y, muteDetent, column);

            return f;
        }

        /// <summary>
        /// The whole fore/aft force inside a slot: the detent's approach shape, and - once the
        /// slot has been given a bottom - the seat and the end-stop wall past it.
        ///
        /// With <see cref="EngineConfig.SlotStopForcePct"/> at zero this is exactly
        /// <see cref="DetentMagnitude"/> and the gate is what it has always been: past the engage
        /// line the seated hold keeps pulling, so the lever runs on to the base's own mechanical
        /// stop. That is why shortening <see cref="EngineConfig.EngageDepth"/> on its own only
        /// makes a gear <em>register</em> earlier - a seated gear still ends up at full deflection,
        /// and there is no such thing as a short throw. The sequential lever has had a bottom since
        /// it shipped (<see cref="EngineConfig.SeqOvertravel"/> and SeqStopForcePct); the H slots
        /// simply never got one.
        ///
        /// Given a bottom, the shape past the engage line is a corridor, not a pull toward a point:
        /// the hold fades out over one wall bite, the landing beyond it carries no fore/aft force at
        /// all, and then the wall rises toward neutral. That is deliberate and it is the same rule
        /// <see cref="EngineConfig.SlotHalfWidth"/> and <see cref="EngineConfig.ChannelFreeDepth"/>
        /// follow - a restoring force about an interior equilibrium is an oscillator, and a free
        /// region makes the equilibrium a place rather than a point. It only works because the base
        /// does not self-centre once MOZA Cockpit's Spring is at 0 (see docs/hardware.md): nothing
        /// pushes the lever back out of the landing, so nothing has to hold it in.
        ///
        /// The fade is never shorter than the wall bite, whatever the landing is set to. At a
        /// landing of zero the hold would otherwise have to reach nothing within a count or two of
        /// the engage line, which is a full-strength step rather than a face - the failure the
        /// tunnel band's <see cref="GateGeometry.MinBandSpan"/> exists to prevent, in the other axis.
        /// Where that clamp bites, <see cref="SlotStopDepth"/> says so.
        ///
        /// Public because the Feel tab's detent curve plots this rather than a copy of it.
        /// </summary>
        public int SlotForceAt(ShiftDir direction, int y, bool muted, Column column = Column.None)
        {
            double d = _geo.EngageFraction(direction, y);

            // A hard entry lockout renders exactly as the grind's balk: the detent becomes a
            // border. When both are active at once the taller wall wins - max, not sum,
            // because a border is not taller for having two reasons, and one wall means one
            // attack and one yield floor.
            bool hardBalk = SlotHardEntryLive(column, direction);
            bool border = muted || hardBalk;
            int wall = Math.Max(muted ? _grindWallForce : 0, hardBalk ? _lockoutForce : 0);

            int detent = DetentMagnitude(direction, d, border, wall);

            if (!border)
            {
                detent = GateGeometry.Clamp(detent + SlotLockoutBoost(column, direction, d),
                    -GateGeometry.ForceMax, GateGeometry.ForceMax);
            }

            // A balked shift has no seat to reach: the balk wall is what stops the lever,
            // a third of the way in, and stacking a second wall behind it would say nothing.
            if (_slotStopForce <= 0 || border) return detent;

            int depth = Math.Abs(y - GateGeometry.AxisCenter);
            int seat = ThrowThreshold();
            if (depth <= seat) return detent;

            int face = Math.Max(1, _cfg.WallRamp);
            int landing = SlotLanding();

            double keep = 1.0 - GateGeometry.Clamp((depth - seat) / (double)face, 0.0, 1.0);
            int held = (int)Math.Round(detent * keep);

            int into = depth - (seat + landing);
            if (into <= 0) return held;

            double t = GateGeometry.Clamp(into / (double)face, 0.0, 1.0);
            int stop = (int)Math.Round(_slotStopForce * t);

            return Combine(held, direction == ShiftDir.Fwd ? stop : -stop);
        }

        /// <summary>Landing past the engage line, never shorter than the fade that precedes it.</summary>
        private int SlotLanding()
        {
            return Math.Max(Math.Max(1, _cfg.WallRamp), Math.Max(0, _cfg.SlotOvertravel));
        }

        /// <summary>
        /// Where the exit toll's band lives, in engage fraction. The foot sits at the start of
        /// the detent's crossover so the toll ends exactly where the ordinary fight begins; the
        /// top stops short of the seat so a seated gear rests untouched - the seat stays a free
        /// region, an arming edge over a seated lever has nothing to step, and the gear does not
        /// spend all day pressed harder into its stop.
        /// </summary>
        private const double SlotExitBandFoot = 0.55;
        private const double SlotExitBandRise = 0.65;
        private const double SlotExitBandFall = 0.90;
        private const double SlotExitBandTop = 0.95;

        /// <summary>Entry toll shape: spent entirely before the crossover so the snick arrives whole.</summary>
        private const double SlotEntryBandRise = 0.30;
        private const double SlotEntryBandFall = 0.45;
        private const double SlotEntryBandEnd = 0.55;

        /// <summary>Whether a hard entry lockout is armed, fired, and aimed at this slot.</summary>
        private bool SlotHardEntryLive(Column column, ShiftDir direction)
        {
            return _geo.LockoutIsSlot
                && _cfg.LockoutMode != LockoutMode.PushThrough
                && !_lockoutQuiet
                && column == _geo.LockoutSlotColumn
                && direction == _geo.LockoutSlotDir
                && _cfg.LockoutSlotDirection != LockoutSlotDirection.Exit;
        }

        /// <summary>
        /// The slot lockout's push-through shapes, added onto the ordinary detent for the one
        /// guarded slot. Both are bands, and both of their edges land where the detent is doing
        /// nothing unusual, so no single count of depth ever steps the stroke.
        ///
        /// Entry: extra fight on the way in, spent entirely before the crossover begins - the
        /// snick must arrive whole, and the crossover point must not move, because that is
        /// where a gear starts to feel taken. Exit: a band of into-slot force between the
        /// crossover and the seat. Leaving the gear crosses it and pays; entering crosses it
        /// too and is assisted - the one-way toll's own character, "entering is assisted,
        /// leaving costs", the same trade the gap gate makes at its faces. The seat itself is
        /// deliberately outside the band: a seated gear rests in a free region, not under a
        /// permanent extra load pressing it into the stop.
        ///
        /// In the hard modes the strength is already pinned to full scale by the ctor, and the
        /// entry half never reaches here - a hard entry balks instead. A hard exit stays
        /// force-only: refusing a release would hold a button while the lever leaves.
        /// </summary>
        private int SlotLockoutBoost(Column column, ShiftDir direction, double d)
        {
            if (!_geo.LockoutIsSlot || _lockoutForce <= 0) return 0;
            if (column != _geo.LockoutSlotColumn || direction != _geo.LockoutSlotDir) return 0;
            if (_cfg.LockoutMode != LockoutMode.PushThrough && _lockoutQuiet) return 0;

            bool coversEntry = _cfg.LockoutSlotDirection != LockoutSlotDirection.Exit;
            bool coversExit = _cfg.LockoutSlotDirection != LockoutSlotDirection.Entry;
            bool hard = _cfg.LockoutMode != LockoutMode.PushThrough;

            // Positive is toward the neutral channel, matching DetentMagnitude's convention.
            double restoring = 0;

            if (coversEntry && !hard)
            {
                if (d < SlotEntryBandRise)
                {
                    restoring += _lockoutForce * (d / SlotEntryBandRise);
                }
                else if (d < SlotEntryBandFall)
                {
                    restoring += _lockoutForce;
                }
                else if (d < SlotEntryBandEnd)
                {
                    restoring += _lockoutForce * (1.0 - (d - SlotEntryBandFall) / (SlotEntryBandEnd - SlotEntryBandFall));
                }
            }

            if (coversExit)
            {
                if (d >= SlotExitBandRise && d < SlotExitBandFall)
                {
                    restoring -= _lockoutForce;
                }
                else if (d >= SlotExitBandFoot && d < SlotExitBandRise)
                {
                    restoring -= _lockoutForce * ((d - SlotExitBandFoot) / (SlotExitBandRise - SlotExitBandFoot));
                }
                else if (d >= SlotExitBandFall && d < SlotExitBandTop)
                {
                    restoring -= _lockoutForce * (1.0 - (d - SlotExitBandFall) / (SlotExitBandTop - SlotExitBandFall));
                }
            }

            double signed = direction == ShiftDir.Fwd ? restoring : -restoring;
            return (int)Math.Round(signed);
        }

        /// <summary>
        /// The fore/aft force one stroke renders at this depth, whichever pattern is configured -
        /// the slot for an H gate, the sprung lever for a sequential one.
        ///
        /// Public, and a dispatcher rather than two call sites, because the Feel tab's detent
        /// curve plots it: that graph exists to answer "what will this dial actually do", and it
        /// used to draw <see cref="DetentMagnitude"/> on every pattern - which is the right answer
        /// for an H slot and the wrong function entirely for a sequential lever, whose stroke is
        /// <see cref="SequentialSpring"/> and shares none of the H crossover geometry.
        /// </summary>
        public int StrokeForceAt(ShiftDir direction, int y, bool muted, Column column = Column.None)
        {
            if (_cfg.Pattern == GatePattern.Sequential) return SequentialSpring(y);
            if (_cfg.Pattern == GatePattern.Prnd) return PrndLaneForce(y);
            return SlotForceAt(direction, y, muted, column);
        }

        /// <summary>
        /// Where a gear seats, or a sequential shift fires, as a distance from centre: the throw,
        /// in one word.
        /// </summary>
        public int StrokeSeatDepth { get { return ThrowThreshold(); } }

        /// <summary>
        /// Where the stroke meets its end-stop wall, as a distance from centre - and
        /// <see cref="GateGeometry.AxisCenter"/> itself, meaning the end of travel, when it has no
        /// bottom to meet. The Feel tab prints this beside the throw slider, because the two
        /// numbers a hand actually cares about are where the gear seats and where the lever stops,
        /// and neither is the number on any one dial: the landing carries its own floor, and a wall
        /// asked for past the end of travel is simply never met.
        /// </summary>
        public int StrokeStopDepth
        {
            get
            {
                bool sequential = _cfg.Pattern == GatePattern.Sequential;

                int force = sequential ? _seqStopForce : _slotStopForce;
                if (force <= 0) return GateGeometry.AxisCenter;

                int landing = sequential ? Math.Max(0, _cfg.SeqOvertravel) : SlotLanding();
                return Math.Min(GateGeometry.AxisCenter, ThrowThreshold() + landing);
            }
        }

        /// <summary>
        /// Restoring force toward a target: rises over <paramref name="ramp"/> counts to the
        /// plateau and then holds. The short ramp is what makes it read as a wall rather than a
        /// spring; the deadband keeps the stick from dithering when it is already on target.
        /// </summary>
        /// <summary>
        /// The shape every wall in this gate is made of: a free deadband, a linear rise over the
        /// bite distance, then a flat plateau - opposing the displacement, hence the sign flip.
        /// <para>
        /// Public so the Feel tab's Gate Walls graph plots this function rather than a copy of
        /// it. It was a copy: three lines, provably identical at the time, and the one curve on
        /// that tab which could quietly stop matching the gate it claims to draw. A graph whose
        /// whole promise is "this is the real force" cannot be the one place the real force is
        /// re-derived, and the drift would show up exactly where someone was trying to diagnose
        /// a feel problem with it.
        /// </para>
        /// </summary>
        public static int Saturating(int displacement, int plateau, int ramp, int deadBand)
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
            return Math.Min(_cfg.WallRamp, SlotRampCeiling(corridor));
        }

        /// <summary>
        /// The room-bound half of <see cref="SlotRamp"/>, without the configured WallRamp mixed
        /// in - so a caller can tell whether a given bite is actually being cut down, rather than
        /// just seeing the already-clamped result.
        ///
        /// Room for the rising face AND the relief flank, which is the same length, plus the
        /// handover window they have to fit either side of. Halved for that reason: without it an
        /// absurd bite makes the two ramps overlap and the wall never reaches full strength at all,
        /// instead of merely reaching it late. Real bites are nowhere near this bound - at the
        /// shipped geometry it allows 4711 - so this only ever catches a hostile setting.
        /// </summary>
        private int SlotRampCeiling(int corridor)
        {
            int room = (_geo.ColumnSpacing / 2) - corridor - _geo.DetentHysteresis;
            return Math.Max(200, room / 2);
        }

        /// <summary>
        /// The tightest bite ceiling <see cref="SlotRamp"/> enforces across every column in this
        /// geometry - the number the Feel tab shows next to the Wall bite distance slider. A
        /// column's corridor narrows this (see <see cref="SlotCorridor"/>), and edge and inner
        /// columns generally allow different corridors for the same SlotHalfWidth, so the
        /// binding ceiling is whichever column is tightest, not any one column in particular.
        /// Independent of the configured WallRamp: compare against it to tell whether the
        /// ceiling is actually biting rather than sitting above the request unnoticed.
        /// </summary>
        public int WallRampCeiling
        {
            get
            {
                int worst = int.MaxValue;
                for (int i = 0; i < _geo.ColumnCount; i++)
                {
                    int ceiling = SlotRampCeiling(SlotCorridor((Column)i));
                    if (ceiling < worst) worst = ceiling;
                }
                return worst;
            }
        }

        /// <summary>
        /// How deep ChannelFreeDepth can reach before <see cref="ComposeNeutral"/> clamps it to
        /// the neutral channel's own enter band - a force deadband wider than that would be a
        /// wall the state machine believes exists and the hand never meets. Exposed alongside
        /// <see cref="WallRampCeiling"/> so the Feel tab has one mechanism for "this dial has a
        /// computed ceiling that isn't obvious from the slider," rather than a second one-off
        /// display for the same class of silently-clamped dial. Unlike the wall bite, this
        /// ceiling is a single geometry fact, not a per-column minimum.
        /// </summary>
        public int ChannelFreeDepthCeiling { get { return _geo.ChannelHalfEnter; } }

        private int SlotCorridor(Column column)
        {
            int limit = Math.Max(0, _geo.ColumnFreeHalfWidth(column) - 100);
            return GateGeometry.Clamp(_cfg.SlotHalfWidth, 0, limit);
        }

        /// <summary>
        /// Half the free corridor's width at a given fore/aft depth, on one flank - the normal
        /// slot corridor plus whatever the mouth shaping currently adds there. side is +1 or -1
        /// for which flank; only its sign matters, matching MouthExtra's own convention. For
        /// the Feel tab's Slot Mouths visualization, which draws the widening funnel this
        /// produces as the stick approaches from the tunnel - the exact corridor boundary
        /// LateralGuide renders, not a redrawn approximation of it.
        /// </summary>
        public int SlotCorridorHalfWidthAt(Column column, int side, int depth, int y)
        {
            return SlotCorridor(column) + MouthExtra(side, depth, y, column);
        }

        /// <summary>Humps guarding the ordinary gaps, and the lockout gate guarding its configured gap.</summary>
        private int BarrierForceAt(int x, int lockoutSide)
        {
            int total = 0;

            for (int i = 0; i < _geo.ColumnCount - 1; i++)
            {
                // Both the gate's position and which gap it guards come from the geometry, which
                // places it against the approach column and follows the mirrored gear map.
                int d = x - _geo.BarrierCentre(i);

                if (i == _geo.LockoutGapIndex)
                {
                    if (_lockoutQuiet) continue;

                    // One-way: constant sign, pushing back toward the approach side. Both: the
                    // side latch decides - the band pushes back toward wherever the lever
                    // entered from, which is what makes each crossing pay in full.
                    int pushSign = _geo.LockoutBlockSign != 0 ? -_geo.LockoutBlockSign : lockoutSide;
                    total += LockoutGate(d, _lockoutForce, _geo.LockoutHalfWidth, _cfg.WallRamp, pushSign);
                }
                else
                {
                    total += Hump(d, _barrierForce, _cfg.BarrierWidth);
                }
            }

            return total;
        }

        /// <summary>
        /// The lockout gate: flat force across a compact band, pushing toward the approach side
        /// the whole way, free travel beyond. Flat because a gradient rings; no refund past the
        /// crest because an over-centre gate refunds past its crest the energy it charged before
        /// it, which lets a fast flick sail through for nearly nothing - measured by hand. With
        /// no refund, crossing costs the full fight at any speed, and the faces at both ends
        /// mean leaving the guarded column winds up briefly and is then assisted, like a real
        /// range gate. It guards only the crossing; keeping the stick in the channel is the
        /// walls' job. A Both-direction gate keeps the same shape and takes its sign from the
        /// side latch instead, which flips only where this very shape is already zero.
        /// </summary>
        private static int LockoutGate(int displacement, int strength, int halfWidth, int ramp, int pushSign)
        {
            if (strength <= 0 || halfWidth <= 0 || pushSign == 0) return 0;

            int m = Math.Abs(displacement);

            // The faces live *inside* the band, so the gate never reaches past the width it
            // declares. They used to overhang it by a whole bite distance, which ate the whole
            // clearance the gate is positioned with and started the toll on top of the 5/6
            // column - felt as a hard bump exactly where the hand expects to be resting on a
            // column. Capped at half the width so there is always a flat core between them.
            int face = GateGeometry.Clamp(ramp, 1, Math.Max(1, halfWidth / 2));

            double p = GateGeometry.Clamp((halfWidth - m) / (double)face, 0.0, 1.0);

            return pushSign * (int)Math.Round(strength * p);
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
        /// resistance rises with the balk wall stacked on top, and simply stays, pushing the
        /// lever back out however deep it is held - a border, the way a blocking synchro ring
        /// stops the lever a third of the way in, not a lean. The moment the clutch goes down
        /// the normal profile returns and the pull arrives whole, like the snick it is.
        ///
        /// Takes the engage fraction directly rather than a raw y, so the Feel tab's detent
        /// curve visualization can sample this exact formula across 0..1.2 for a plot, instead
        /// of a separate reimplementation that could drift from what a real shift actually
        /// feels. Public for the same reason.
        /// </summary>
        public int DetentMagnitude(ShiftDir direction, double engageFraction, bool muted)
        {
            return DetentMagnitude(direction, engageFraction, muted, muted ? _grindWallForce : 0);
        }

        /// <summary>
        /// The same curve with the balk wall's strength supplied, because two things can turn
        /// the detent into a border now - the grind, and a hard slot lockout - and when both
        /// are live the taller wall wins rather than the two stacking.
        /// </summary>
        private int DetentMagnitude(ShiftDir direction, double engageFraction, bool muted, int wallForce)
        {
            double d = engageFraction;

            double restoring;
            if (muted)
            {
                restoring = (_detentResistMax + wallForce) * Math.Min(1.0, d / 0.55);
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
