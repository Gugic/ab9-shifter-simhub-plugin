using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter
{
    /// <summary>
    /// Everything the user can change, persisted by SimHub as JSON. Raises change
    /// notifications so the settings UI can two-way bind and push a new
    /// <see cref="EngineConfig"/> to the running loop.
    /// </summary>
    public class ShifterSettings : INotifyPropertyChanged
    {
        // Off by default on purpose: enabling takes the base exclusively and starts applying
        // force. That must be a deliberate act, after the MOZA Cockpit setup and the rest of
        // the pre-flight steps, with the user at the stick.
        private bool _enabled;
        private int _overallGainPct = 25;
        private int _lockoutForcePct = 70;
        private int _calibrationForcePct = 10;
        private bool _polarityConfirmed;
        private bool _invertConstantX;
        private bool _invertConstantY;
        private bool _mirrorColumns;
        private bool _mirrorSlots;
        private bool _freeStick;
        private uint _vJoyDeviceId = 1;
        private int _vendorId = 0x346E;
        private int _productId = 0x1000;
        private int _tickHz = 1000;

        private int _columnPinForcePct = 90;
        private int _channelWallForcePct = 90;
        private int _channelGuideForcePct = 5;
        private int _columnDetentForcePct = 12;
        private int _barrierForcePct = 15;
        private int _homeSpringPct;
        private int _lockoutHalfWidth = 2200;
        private SlotMouthShape _mouthShape = SlotMouthShape.Square;
        private int _mouthDepth = 5000;
        private int _mouthOpenPct = 100;
        private int _wallRamp = 600;
        private int _wallAttackMs = 0;
        private int _barrierWidth = 2500;
        private int _wallBlend = 1500;
        private int _slotHalfWidth = 1100;
        private int _channelFreeDepth = 2600;
        private GatePattern _pattern = GatePattern.H7R;
        private int _seqPulseMs = 120;
        private int _seqOvertravel = 2500;
        private int _seqStopForcePct = 90;
        private int _seqClickPct = 60;
        private int _prndLaneHalfLength = 12000;
        private int _prndDetentForcePct = 45;
        private int _prndNotchHalfWidth = 600;
        private int _prndStopForcePct = 90;

        // Telemetry effects, all off by default: they are additions to the gate, not part of it.
        private bool _fxEngineEnabled;
        private int _fxEngineGainPct = 25;
        private int _fxEngineFreqAt1000Rpm = 17;
        private bool _fxLimiterEnabled;
        private int _fxLimiterGainPct = 45;
        private int _fxLimiterFreqHz = 55;
        private int _fxLimiterFromPct = 96;
        private bool _fxAbsEnabled;
        private int _fxAbsGainPct = 40;
        private int _fxAbsFreqHz = 44;
        private bool _fxTcEnabled;
        private int _fxTcGainPct = 35;
        private int _fxTcFreqHz = 60;
        private bool _fxCurbsEnabled;
        private int _fxCurbsGainPct = 45;
        private int _fxCurbsFreqHz = 40;
        private double _fxCurbsFullAtG = 1.0;
        private bool _fxShiftEnabled;
        private int _fxShiftGainPct = 45;
        private int _fxShiftFreqHz = 44;
        private int _fxShiftDurationMs = 80;
        private bool _fxCustomEnabled;
        private string _fxCustomProperty = "";
        private int _fxCustomGainPct = 30;
        private int _fxCustomFreqHz = 44;
        private bool _grindEnabled;
        private int _grindGainPct = 60;
        private int _grindFreqHz = 33;
        private int _grindWallPct = 70;
        private int _grindClutchThresholdPct = 25;
        private int _grindMinSpeedKmh;
        private bool _grindRejectsGear = true;
        private GrindClutchMode _grindClutchMode = GrindClutchMode.Threshold;
        private int _clutchBitePointPct = 25;
        private bool _fxBiteEnabled;
        private int _fxBiteGainPct = 35;
        private int _fxBiteFreqHz = 50;
        private int _fxBiteDurationMs = 60;
        private ClutchSource _clutchSource = ClutchSource.GameTelemetry;
        private string _pedalDeviceId = "";
        private int _pedalAxisIndex = -1;
        private int _pedalRawMin;
        private int _pedalRawMax;
        private int _pedalDeadzoneLow;
        private int _pedalDeadzoneHigh = AxisCalibration.ScaledMax;
        private bool _pedalInvert;
        private int _dampingPct = 25;
        private int _wallFrictionPct = 15;
        private int _wallYieldPct = 45;
        private int _damperCoeff = 800;
        private int _detentResistPct = 22;
        private int _detentPullPct = 35;
        private int _detentHoldPct = 55;
        private int _slotStopForcePct;
        private int _slotOvertravel = 4000;

        private int _channelHalfEnter = 2600;
        private int _channelHalfExit = 5200;
        private int _columnEdgeEnter = 2600;
        private int _columnInnerHalfEnter = 1200;
        private int _columnInnerHalfExit = 2400;
        private int _engageDepth = 4000;
        private int _releaseDepth = 8000;
        private int _detentHysteresis = 400;
        private int _minEngageTicks = 2;

        public bool Enabled { get { return _enabled; } set { Set(ref _enabled, value); } }

        /// <summary>Master force scale. Capped to 10% until the polarity wizard has run.</summary>
        public int OverallGainPct { get { return _overallGainPct; } set { Set(ref _overallGainPct, value); } }

        /// <summary>Force needed to push through into the 7/R column, as a share of the overall gain.</summary>
        public int LockoutForcePct { get { return _lockoutForcePct; } set { Set(ref _lockoutForcePct, value); } }

        /// <summary>Half-width of the lockout gate: a dot on the neutral channel, not a zone.</summary>
        public int LockoutHalfWidth
        {
            get { return _lockoutHalfWidth; }
            set { Set(ref _lockoutHalfWidth, value); OnChanged("LockoutHalfWidthPercent"); }
        }

        /// <summary>Force used when measuring polarity. Raise it if calibration is inconclusive.</summary>
        public int CalibrationForcePct { get { return _calibrationForcePct; } set { Set(ref _calibrationForcePct, value); } }

        public bool PolarityConfirmed { get { return _polarityConfirmed; } set { Set(ref _polarityConfirmed, value); } }

        // Measured per axis and per effect family: this base inverts constant force on X but not
        // on Y, and the spring on Y but not on X.
        public bool InvertConstantX { get { return _invertConstantX; } set { Set(ref _invertConstantX, value); } }
        public bool InvertConstantY { get { return _invertConstantY; } set { Set(ref _invertConstantY, value); } }

        /// <summary>Put first gear at the right-hand end of the gate instead of the left.</summary>
        public bool MirrorColumns { get { return _mirrorColumns; } set { Set(ref _mirrorColumns, value); } }

        /// <summary>Swap each gear pair, so odd gears sit toward the player.</summary>
        public bool MirrorSlots { get { return _mirrorSlots; } set { Set(ref _mirrorSlots, value); } }

        /// <summary>Release all forces, to check how the stick moves with nothing applied.</summary>
        public bool FreeStick { get { return _freeStick; } set { Set(ref _freeStick, value); } }

        public uint VJoyDeviceId { get { return _vJoyDeviceId; } set { Set(ref _vJoyDeviceId, value); } }
        public int VendorId { get { return _vendorId; } set { Set(ref _vendorId, value); } }
        public int ProductId { get { return _productId; } set { Set(ref _productId, value); } }

        /// <summary>
        /// The USB ids as hex, which is how the label, the docs, Device Manager and MOZA all
        /// quote them. The stored value is an int; binding a text box straight to it renders
        /// 13422 for the AB9 and then rejects "346E" when a user types back what the label
        /// told them to. Unparseable text is ignored rather than throwing, because every
        /// half-finished keystroke arrives here.
        /// </summary>
        public string VendorIdHex
        {
            get { return _vendorId.ToString("X4"); }
            set
            {
                int parsed;
                if (!TryParseHex(value, out parsed) || parsed == _vendorId) return;
                _vendorId = parsed;
                OnChanged("VendorIdHex");
                OnChanged("VendorId");
            }
        }

        public string ProductIdHex
        {
            get { return _productId.ToString("X4"); }
            set
            {
                int parsed;
                if (!TryParseHex(value, out parsed) || parsed == _productId) return;
                _productId = parsed;
                OnChanged("ProductIdHex");
                OnChanged("ProductId");
            }
        }

        private static bool TryParseHex(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(2);

            return int.TryParse(
                trimmed,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }
        public int TickHz { get { return _tickHz; } set { Set(ref _tickHz, value); } }

        // Walls are constant forces expressed as a percentage of full scale. Spring
        // coefficients used to live here and could not produce a usable wall; see ForceComposer.
        public int ColumnPinForcePct { get { return _columnPinForcePct; } set { Set(ref _columnPinForcePct, value); } }
        public int ChannelWallForcePct { get { return _channelWallForcePct; } set { Set(ref _channelWallForcePct, value); } }
        public int ChannelGuideForcePct { get { return _channelGuideForcePct; } set { Set(ref _channelGuideForcePct, value); } }
        public int ColumnDetentForcePct { get { return _columnDetentForcePct; } set { Set(ref _columnDetentForcePct, value); } }

        public int BarrierForcePct { get { return _barrierForcePct; } set { Set(ref _barrierForcePct, value); } }

        /// <summary>Neutral spring toward the 3/4 column. Zero - the default - is off.</summary>
        public int HomeSpringPct { get { return _homeSpringPct; } set { Set(ref _homeSpringPct, value); } }

        /// <summary>Shape of the slot mouths. Square is today's rectangular notch, and the default.</summary>
        public SlotMouthShape MouthShape { get { return _mouthShape; } set { Set(ref _mouthShape, value); } }

        /// <summary>Adapter for the combo box, which binds an index rather than an enum.</summary>
        public int MouthShapeIndex
        {
            get { return (int)_mouthShape; }
            set
            {
                SlotMouthShape shape = (SlotMouthShape)GateGeometry.Clamp(value, 0, 2);
                if (shape == _mouthShape) return;
                _mouthShape = shape;
                OnChanged("MouthShapeIndex");
                OnChanged("MouthShape");
            }
        }

        /// <summary>How far down the slot the mouth shaping reaches. Too shallow and it does nothing.</summary>
        public int MouthDepth { get { return _mouthDepth; } set { Set(ref _mouthDepth, value); } }

        /// <summary>How much of the safe opening to use, as a percentage.</summary>
        public int MouthOpenPct { get { return _mouthOpenPct; } set { Set(ref _mouthOpenPct, value); } }

        public int WallRamp { get { return _wallRamp; } set { Set(ref _wallRamp, value); OnChanged("WallRampPercent"); } }

        /// <summary>How many milliseconds a wall takes to reach full force on contact. The hammer fix.</summary>
        public int WallAttackMs { get { return _wallAttackMs; } set { Set(ref _wallAttackMs, value); } }
        public int BarrierWidth { get { return _barrierWidth; } set { Set(ref _barrierWidth, value); OnChanged("BarrierWidthPercent"); } }
        public int WallBlend { get { return _wallBlend; } set { Set(ref _wallBlend, value); OnChanged("WallBlendPercent"); } }

        /// <summary>Free lateral corridor inside a slot. Widen it if a gear shakes when seated; zero rails the slot.</summary>
        public int SlotHalfWidth { get { return _slotHalfWidth; } set { Set(ref _slotHalfWidth, value); OnChanged("SlotHalfWidthPercent"); } }

        /// <summary>Free fore/aft depth of the neutral tunnel before its centring begins. Zero rails the tunnel.</summary>
        public int ChannelFreeDepth { get { return _channelFreeDepth; } set { Set(ref _channelFreeDepth, value); } }

        /// <summary>Which shift pattern this profile renders.</summary>
        public GatePattern Pattern
        {
            get { return _pattern; }
            set
            {
                if (_pattern == value) return;
                Set(ref _pattern, value);

                // Derived facts the UI keys section visibility on.
                OnChanged("IsHPattern");
                OnChanged("IsSequential");
                OnChanged("IsPrnd");
                OnChanged("HasLockout");

                // ColumnSpacing depends on pattern, so every percent-of-spacing view changes
                // even though none of the underlying raw counts did.
                OnChanged("WallRampPercent");
                OnChanged("SlotHalfWidthPercent");
                OnChanged("LockoutHalfWidthPercent");
                OnChanged("BarrierWidthPercent");
                OnChanged("WallBlendPercent");
                OnChanged("ColumnEdgeEnterPercent");
                OnChanged("ColumnInnerHalfEnterPercent");
                OnChanged("ColumnInnerHalfExitPercent");
                OnChanged("DetentHysteresisPercent");
            }
        }

        /// <summary>
        /// Whether the gate machinery (mouths, humps, walls) applies at all. Named for what it
        /// gates rather than for the enum, and listed positively rather than as "not sequential":
        /// that is how it was written, and adding PRND would silently have handed every H-only
        /// control to a pattern with no columns in it.
        /// </summary>
        public bool IsHPattern
        {
            get
            {
                return _pattern == GatePattern.H7R
                    || _pattern == GatePattern.H6R
                    || _pattern == GatePattern.H5R;
            }
        }

        /// <summary>For the sequential-only controls.</summary>
        public bool IsSequential { get { return _pattern == GatePattern.Sequential; } }

        /// <summary>For the PRND-only controls, and for hiding the slot detent that has no slot.</summary>
        public bool IsPrnd { get { return _pattern == GatePattern.Prnd; } }

        /// <summary>Whether this pattern has a lockout gate for its sliders to mean anything.</summary>
        public bool HasLockout { get { return _pattern == GatePattern.H7R || _pattern == GatePattern.H6R; } }

        /// <summary>Exposes the pattern as an index for the XAML combo box.</summary>
        public int PatternIndex
        {
            get { return (int)_pattern; }
            set { Pattern = (GatePattern)value; }
        }

        /// <summary>How long a sequential shift holds its button, in milliseconds.</summary>
        public int SeqPulseMs { get { return _seqPulseMs; } set { Set(ref _seqPulseMs, value); } }

        /// <summary>Stroke remaining past the sequential click before the end-stop wall.</summary>
        public int SeqOvertravel { get { return _seqOvertravel; } set { Set(ref _seqOvertravel, value); } }

        /// <summary>The end-stop wall at the bottom of a sequential stroke.</summary>
        public int SeqStopForcePct { get { return _seqStopForcePct; } set { Set(ref _seqStopForcePct, value); } }

        /// <summary>The click's kick: a 25 ms burst in the stroke's direction when a shift fires.</summary>
        public int SeqClickPct { get { return _seqClickPct; } set { Set(ref _seqClickPct, value); } }

        /// <summary>Distance from centre to each end of the PRND lane. Its own dial, not the throw.</summary>
        public int PrndLaneHalfLength { get { return _prndLaneHalfLength; } set { Set(ref _prndLaneHalfLength, value); } }

        /// <summary>How hard it is to move between PRND positions.</summary>
        public int PrndDetentForcePct { get { return _prndDetentForcePct; } set { Set(ref _prndDetentForcePct, value); } }

        /// <summary>Free half-width of the notch at each PRND position. Zero pulls to a line.</summary>
        public int PrndNotchHalfWidth { get { return _prndNotchHalfWidth; } set { Set(ref _prndNotchHalfWidth, value); } }

        /// <summary>The wall past either end of the PRND lane.</summary>
        public int PrndStopForcePct { get { return _prndStopForcePct; } set { Set(ref _prndStopForcePct, value); } }

        /// <summary>
        /// The throw: axis counts from centre to the line a push registers at. The same stored
        /// fact as <see cref="EngageDepth"/>, which measures from the end of travel instead -
        /// re-expressed the way a hand thinks about a lever, so a longer number is a longer pull.
        /// Writing it moves the release line (<see cref="ReleaseDepth"/>) by the same amount,
        /// keeping the hysteresis gap: moving only the firing line would shrink the gap and,
        /// shortened far enough, let a lever resting on the threshold machine-gun shifts.
        /// <para>
        /// Both patterns want this number and it means the same thing in each - the sequential
        /// lever fires there, an H gear seats there. On an H pattern it is only half a short
        /// throw, though: without <see cref="SlotStopForcePct"/> the seated hold keeps pulling
        /// past that line, so the lever still ends up at the base's own mechanical stop and only
        /// the <em>registration</em> moved. See ForceComposer.SlotForceAt.
        /// </para>
        /// </summary>
        public int ThrowFromCentre
        {
            get { return GateGeometry.AxisCenter - _engageDepth; }
            set
            {
                int depth = GateGeometry.AxisCenter - value;
                int delta = depth - _engageDepth;
                if (delta == 0) return;

                _engageDepth = depth;
                _releaseDepth = Math.Max(_releaseDepth + delta, depth + 500);

                OnChanged("ThrowFromCentre");
                OnChanged("EngageDepth");
                OnChanged("ReleaseDepth");
            }
        }

        // Telemetry effects: per-effect enable, volume and pitch, mirrored into EngineConfig.

        public bool FxEngineEnabled { get { return _fxEngineEnabled; } set { Set(ref _fxEngineEnabled, value); } }
        public int FxEngineGainPct { get { return _fxEngineGainPct; } set { Set(ref _fxEngineGainPct, value); } }

        /// <summary>The engine carrier's frequency at 1000 rpm; pitch scales with the revs.</summary>
        public int FxEngineFreqAt1000Rpm { get { return _fxEngineFreqAt1000Rpm; } set { Set(ref _fxEngineFreqAt1000Rpm, value); } }

        public bool FxLimiterEnabled { get { return _fxLimiterEnabled; } set { Set(ref _fxLimiterEnabled, value); } }
        public int FxLimiterGainPct { get { return _fxLimiterGainPct; } set { Set(ref _fxLimiterGainPct, value); } }
        public int FxLimiterFreqHz { get { return _fxLimiterFreqHz; } set { Set(ref _fxLimiterFreqHz, value); } }
        public int FxLimiterFromPct { get { return _fxLimiterFromPct; } set { Set(ref _fxLimiterFromPct, value); } }

        public bool FxAbsEnabled { get { return _fxAbsEnabled; } set { Set(ref _fxAbsEnabled, value); } }
        public int FxAbsGainPct { get { return _fxAbsGainPct; } set { Set(ref _fxAbsGainPct, value); } }
        public int FxAbsFreqHz { get { return _fxAbsFreqHz; } set { Set(ref _fxAbsFreqHz, value); } }

        public bool FxTcEnabled { get { return _fxTcEnabled; } set { Set(ref _fxTcEnabled, value); } }
        public int FxTcGainPct { get { return _fxTcGainPct; } set { Set(ref _fxTcGainPct, value); } }
        public int FxTcFreqHz { get { return _fxTcFreqHz; } set { Set(ref _fxTcFreqHz, value); } }

        public bool FxCurbsEnabled { get { return _fxCurbsEnabled; } set { Set(ref _fxCurbsEnabled, value); } }
        public int FxCurbsGainPct { get { return _fxCurbsGainPct; } set { Set(ref _fxCurbsGainPct, value); } }
        public int FxCurbsFreqHz { get { return _fxCurbsFreqHz; } set { Set(ref _fxCurbsFreqHz, value); } }

        /// <summary>Vertical shake, in G, at which the curb rattle reaches full volume.</summary>
        public double FxCurbsFullAtG { get { return _fxCurbsFullAtG; } set { Set(ref _fxCurbsFullAtG, value); } }

        public bool FxShiftEnabled { get { return _fxShiftEnabled; } set { Set(ref _fxShiftEnabled, value); } }
        public int FxShiftGainPct { get { return _fxShiftGainPct; } set { Set(ref _fxShiftGainPct, value); } }
        public int FxShiftFreqHz { get { return _fxShiftFreqHz; } set { Set(ref _fxShiftFreqHz, value); } }
        public int FxShiftDurationMs { get { return _fxShiftDurationMs; } set { Set(ref _fxShiftDurationMs, value); } }

        public bool FxCustomEnabled { get { return _fxCustomEnabled; } set { Set(ref _fxCustomEnabled, value); } }

        /// <summary>Full SimHub property name whose 0..100 value drives the custom effect.</summary>
        public string FxCustomProperty { get { return _fxCustomProperty; } set { Set(ref _fxCustomProperty, value); } }
        public int FxCustomGainPct { get { return _fxCustomGainPct; } set { Set(ref _fxCustomGainPct, value); } }
        public int FxCustomFreqHz { get { return _fxCustomFreqHz; } set { Set(ref _fxCustomFreqHz, value); } }

        public bool GrindEnabled { get { return _grindEnabled; } set { Set(ref _grindEnabled, value); } }
        public int GrindGainPct { get { return _grindGainPct; } set { Set(ref _grindGainPct, value); } }
        public int GrindFreqHz { get { return _grindFreqHz; } set { Set(ref _grindFreqHz, value); } }

        /// <summary>The balk wall stacked on the entry resistance while a shift is rejected.</summary>
        public int GrindWallPct { get { return _grindWallPct; } set { Set(ref _grindWallPct, value); } }

        /// <summary>Clutch positions below this percentage count as "clutch up" - grind territory.</summary>
        public int GrindClutchThresholdPct { get { return _grindClutchThresholdPct; } set { Set(ref _grindClutchThresholdPct, value); } }

        /// <summary>No grind below this speed. Zero grinds whenever the engine turns.</summary>
        public int GrindMinSpeedKmh { get { return _grindMinSpeedKmh; } set { Set(ref _grindMinSpeedKmh, value); } }

        /// <summary>Whether a grinding shift is balked: no registration until the clutch goes down.</summary>
        public bool GrindRejectsGear { get { return _grindRejectsGear; } set { Set(ref _grindRejectsGear, value); } }

        /// <summary>Whether the grind is one line or a fade across the pedal's travel.</summary>
        public GrindClutchMode GrindClutchMode
        {
            get { return _grindClutchMode; }
            set { Set(ref _grindClutchMode, value); OnChanged("GrindClutchModeIndex"); }
        }

        /// <summary>The combo box's view of the mode, because WPF binds an index more happily.</summary>
        public int GrindClutchModeIndex
        {
            get { return (int)_grindClutchMode; }
            set { GrindClutchMode = (GrindClutchMode)value; }
        }

        /// <summary>
        /// Where the clutch starts to bite, as a percentage of travel. A property of the car, not
        /// of the pedals, so it is set rather than measured - and deliberately not owned by the
        /// grind, because it is the one point on a clutch's travel that means anything.
        /// </summary>
        public int ClutchBitePointPct { get { return _clutchBitePointPct; } set { Set(ref _clutchBitePointPct, value); } }

        /// <summary>A pulse through the lever as the clutch crosses its bite point.</summary>
        public bool FxBiteEnabled { get { return _fxBiteEnabled; } set { Set(ref _fxBiteEnabled, value); } }
        public int FxBiteGainPct { get { return _fxBiteGainPct; } set { Set(ref _fxBiteGainPct, value); } }
        public int FxBiteFreqHz { get { return _fxBiteFreqHz; } set { Set(ref _fxBiteFreqHz, value); } }
        public int FxBiteDurationMs { get { return _fxBiteDurationMs; } set { Set(ref _fxBiteDurationMs, value); } }

        /// <summary>Whether the clutch comes from the game or straight off the pedal's axis.</summary>
        public ClutchSource ClutchSource
        {
            get { return _clutchSource; }
            set { Set(ref _clutchSource, value); OnChanged("ClutchSourceIndex"); }
        }

        public int ClutchSourceIndex
        {
            get { return (int)_clutchSource; }
            set { ClutchSource = (ClutchSource)value; }
        }

        // The pedal binding. Machine facts, all of them: they describe this rig's hardware, so
        // they are excluded from a shared profile exactly like the measured polarity and the
        // device ids. Stored flat rather than as a nested object so the settings file stays a
        // flat map and a renamed field degrades to "lost that one value" instead of "lost the
        // whole calibration".
        public string PedalDeviceId { get { return _pedalDeviceId; } set { Set(ref _pedalDeviceId, value); } }
        public int PedalAxisIndex { get { return _pedalAxisIndex; } set { Set(ref _pedalAxisIndex, value); } }
        public int PedalRawMin { get { return _pedalRawMin; } set { Set(ref _pedalRawMin, value); } }
        public int PedalRawMax { get { return _pedalRawMax; } set { Set(ref _pedalRawMax, value); } }
        public int PedalDeadzoneLow { get { return _pedalDeadzoneLow; } set { Set(ref _pedalDeadzoneLow, value); } }
        public int PedalDeadzoneHigh { get { return _pedalDeadzoneHigh; } set { Set(ref _pedalDeadzoneHigh, value); } }
        public bool PedalInvert { get { return _pedalInvert; } set { Set(ref _pedalInvert, value); } }

        /// <summary>
        /// Applies the session's live switches to this profile as it becomes active.
        /// <para>
        /// <see cref="Enabled"/> and <see cref="FreeStick"/> are the odd pair among the settings:
        /// they say whether the shifter is running <em>right now</em>, which is a fact about the
        /// session rather than about this gate. Everything else here is tuning, and switching
        /// profile is supposed to change the tuning and nothing else. The authority for these two
        /// is <see cref="ProfileStore.SessionEnabled"/>; the copy here exists only because the UI
        /// binds it and <see cref="ToEngineConfig"/> reads it.
        /// </para>
        /// <para>
        /// The direction matters and was got wrong once: taking the value from the profile being
        /// <em>left</em> makes an arbitrary profile the authority and writes its answer onto the
        /// one being opened, which destroys a stored flag the moment you start on a disabled
        /// profile. Pulling from the store instead means an activation can never decide anything.
        /// </para>
        /// </summary>
        public void ApplyLiveSwitches(bool? enabled, bool? freeStick)
        {
            if (enabled.HasValue) Enabled = enabled.Value;
            if (freeStick.HasValue) FreeStick = freeStick.Value;
        }

        /// <summary>True once a pedal has actually been captured; the source is unusable before.</summary>
        public bool PedalCalibrated { get { return _pedalAxisIndex >= 0 && _pedalRawMax > _pedalRawMin; } }

        /// <summary>Stores what a capture measured, as the flat fields the settings file holds.</summary>
        public void ApplyPedalCapture(string deviceId, int axisIndex, AxisCalibration calibration)
        {
            if (calibration == null) return;

            PedalDeviceId = deviceId ?? "";
            PedalAxisIndex = axisIndex;
            PedalRawMin = calibration.RawMin;
            PedalRawMax = calibration.RawMax;
            PedalDeadzoneLow = calibration.DeadzoneLow;
            PedalDeadzoneHigh = calibration.DeadzoneHigh;
            PedalInvert = calibration.Invert;
            OnChanged("PedalCalibrated");
        }

        /// <summary>Velocity damping. This, not the device damper, is what settles a stiff wall.</summary>
        public int DampingPct { get { return _dampingPct; } set { Set(ref _dampingPct, value); } }

        /// <summary>
        /// Friction at the walls, as a share of the force being applied. Zero in free travel by
        /// construction, so it settles a lean on a face without costing any throw lightness.
        /// </summary>
        public int WallFrictionPct { get { return _wallFrictionPct; } set { Set(ref _wallFrictionPct, value); } }

        /// <summary>How much of a wall's force is given up on the rebound. The anti-buzz control.</summary>
        public int WallYieldPct { get { return _wallYieldPct; } set { Set(ref _wallYieldPct, value); } }

        public int DamperCoeff { get { return _damperCoeff; } set { Set(ref _damperCoeff, value); } }
        public int DetentResistPct { get { return _detentResistPct; } set { Set(ref _detentResistPct, value); } }
        public int DetentPullPct { get { return _detentPullPct; } set { Set(ref _detentPullPct, value); } }
        public int DetentHoldPct { get { return _detentHoldPct; } set { Set(ref _detentHoldPct, value); } }

        /// <summary>
        /// The wall at the bottom of an H slot. Zero - the default - gives the slot no bottom of
        /// its own, which is the gate as it has always been and the reason a short throw is not
        /// simply a matter of shortening <see cref="ThrowFromCentre"/>.
        /// </summary>
        public int SlotStopForcePct { get { return _slotStopForcePct; } set { Set(ref _slotStopForcePct, value); } }

        /// <summary>Free landing between where a gear seats and where the lever meets that wall.</summary>
        public int SlotOvertravel { get { return _slotOvertravel; } set { Set(ref _slotOvertravel, value); } }

        public int ChannelHalfEnter { get { return _channelHalfEnter; } set { Set(ref _channelHalfEnter, value); } }
        public int ChannelHalfExit { get { return _channelHalfExit; } set { Set(ref _channelHalfExit, value); } }
        public int ColumnEdgeEnter { get { return _columnEdgeEnter; } set { Set(ref _columnEdgeEnter, value); OnChanged("ColumnEdgeEnterPercent"); } }
        public int ColumnInnerHalfEnter { get { return _columnInnerHalfEnter; } set { Set(ref _columnInnerHalfEnter, value); OnChanged("ColumnInnerHalfEnterPercent"); } }
        public int ColumnInnerHalfExit { get { return _columnInnerHalfExit; } set { Set(ref _columnInnerHalfExit, value); OnChanged("ColumnInnerHalfExitPercent"); } }
        public int EngageDepth
        {
            get { return _engageDepth; }
            set { Set(ref _engageDepth, value); OnChanged("ThrowFromCentre"); }
        }

        public int ReleaseDepth { get { return _releaseDepth; } set { Set(ref _releaseDepth, value); } }
        public int DetentHysteresis { get { return _detentHysteresis; } set { Set(ref _detentHysteresis, value); OnChanged("DetentHysteresisPercent"); } }
        public int MinEngageTicks { get { return _minEngageTicks; } set { Set(ref _minEngageTicks, value); } }

        /// <summary>
        /// <see cref="GateGeometry.ColumnSpacing"/> for the current pattern, without building a
        /// full geometry just to read one derived int.
        /// </summary>
        private int ColumnSpacing { get { return ToEngineConfig().BuildGeometry().ColumnSpacing; } }

        // Percent-of-column-spacing views onto the lateral geometry dials above, for the Feel
        // tab's display toggle. ColumnSpacing genuinely differs by pattern - about 21845 counts
        // on the four-column H6R/H7R and 32767 on the three-column H5R - so the same raw axis
        // count is a different fraction of the room between columns depending which pattern a
        // profile was tuned on. WallRamp's ceiling (see ForceComposer.WallRampCeiling) is
        // computed from exactly this room, which is why a bite that never gets silently
        // clamped on one pattern can start being clamped on another at the same raw count: the
        // percentage is what actually carries a tuned feel across patterns, not the count.
        //
        // Only the genuinely lateral (X-axis) dials get this treatment. ChannelHalfEnter,
        // ChannelHalfExit, EngageDepth, ReleaseDepth and ChannelFreeDepth are fore/aft (Y-axis)
        // depths: the Y-axis travel range does not change between patterns, only the column
        // count does, so those dials mean the same thing on every pattern already and gained
        // nothing from a percent view. WallRamp is the one exception worth flagging: it is also
        // the fore/aft wall bite in ForceComposer.ComposeNeutral, so this percentage describes
        // only its lateral (slot-wall) meaning, not that second use.
        //
        // Display-only: switching Pattern does not rescale the stored raw counts, so the
        // *percentage* shown for an unchanged raw value moves when the pattern changes,
        // exactly like it would if measured by hand. Automatically rescaling the raw counts to
        // preserve the percentage across a pattern switch is a separate, higher-risk change -
        // it would be writing new numbers into force-affecting settings automatically - and
        // belongs in its own change with its own tests, not folded in here.
        public double WallRampPercent
        {
            get { return PercentOfSpacing(_wallRamp); }
            set { SetPercentOfSpacing(ref _wallRamp, value, "WallRamp", "WallRampPercent"); }
        }

        public double SlotHalfWidthPercent
        {
            get { return PercentOfSpacing(_slotHalfWidth); }
            set { SetPercentOfSpacing(ref _slotHalfWidth, value, "SlotHalfWidth", "SlotHalfWidthPercent"); }
        }

        public double LockoutHalfWidthPercent
        {
            get { return PercentOfSpacing(_lockoutHalfWidth); }
            set { SetPercentOfSpacing(ref _lockoutHalfWidth, value, "LockoutHalfWidth", "LockoutHalfWidthPercent"); }
        }

        public double BarrierWidthPercent
        {
            get { return PercentOfSpacing(_barrierWidth); }
            set { SetPercentOfSpacing(ref _barrierWidth, value, "BarrierWidth", "BarrierWidthPercent"); }
        }

        public double WallBlendPercent
        {
            get { return PercentOfSpacing(_wallBlend); }
            set { SetPercentOfSpacing(ref _wallBlend, value, "WallBlend", "WallBlendPercent"); }
        }

        public double ColumnEdgeEnterPercent
        {
            get { return PercentOfSpacing(_columnEdgeEnter); }
            set { SetPercentOfSpacing(ref _columnEdgeEnter, value, "ColumnEdgeEnter", "ColumnEdgeEnterPercent"); }
        }

        public double ColumnInnerHalfEnterPercent
        {
            get { return PercentOfSpacing(_columnInnerHalfEnter); }
            set { SetPercentOfSpacing(ref _columnInnerHalfEnter, value, "ColumnInnerHalfEnter", "ColumnInnerHalfEnterPercent"); }
        }

        public double ColumnInnerHalfExitPercent
        {
            get { return PercentOfSpacing(_columnInnerHalfExit); }
            set { SetPercentOfSpacing(ref _columnInnerHalfExit, value, "ColumnInnerHalfExit", "ColumnInnerHalfExitPercent"); }
        }

        public double DetentHysteresisPercent
        {
            get { return PercentOfSpacing(_detentHysteresis); }
            set { SetPercentOfSpacing(ref _detentHysteresis, value, "DetentHysteresis", "DetentHysteresisPercent"); }
        }

        private double PercentOfSpacing(int raw)
        {
            int spacing = ColumnSpacing;
            return spacing > 0 ? raw * 100.0 / spacing : 0.0;
        }

        /// <summary>
        /// Writes the raw field from a percentage of the current spacing, and notifies both
        /// names - the raw property (so anything bound to it, including the hidden slider the
        /// display toggle is not currently showing, stays in sync) and this percent property
        /// itself (so the slider snaps to the value the rounded raw count actually corresponds
        /// to, rather than the fractional percent the user may have dragged to).
        /// </summary>
        private void SetPercentOfSpacing(ref int field, double percent, string rawPropertyName, string percentPropertyName)
        {
            int raw = (int)Math.Round(percent / 100.0 * ColumnSpacing);
            if (field == raw) return;

            field = raw;
            OnChanged(rawPropertyName);
            OnChanged(percentPropertyName);
        }

        public EngineConfig ToEngineConfig()
        {
            return new EngineConfig
            {
                VendorId = VendorId,
                ProductId = ProductId,
                VJoyDeviceId = VJoyDeviceId,
                TickHz = TickHz,

                InvertConstantX = InvertConstantX,
                InvertConstantY = InvertConstantY,
                MirrorColumns = MirrorColumns,
                MirrorSlots = MirrorSlots,
                FreeStick = FreeStick,
                PolarityConfirmed = PolarityConfirmed,
                OverallGainPct = OverallGainPct,
                CalibrationForcePct = CalibrationForcePct,

                ChannelHalfEnter = ChannelHalfEnter,
                ChannelHalfExit = ChannelHalfExit,
                ColumnEdgeEnter = ColumnEdgeEnter,
                ColumnInnerHalfEnter = ColumnInnerHalfEnter,
                ColumnInnerHalfExit = ColumnInnerHalfExit,
                EngageDepth = EngageDepth,
                ReleaseDepth = ReleaseDepth,
                DetentHysteresis = DetentHysteresis,
                MinEngageTicks = MinEngageTicks,

                ColumnPinForcePct = ColumnPinForcePct,
                ChannelWallForcePct = ChannelWallForcePct,
                ChannelGuideForcePct = ChannelGuideForcePct,
                ColumnDetentForcePct = ColumnDetentForcePct,
                BarrierForcePct = BarrierForcePct,
                HomeSpringPct = HomeSpringPct,
                MouthShape = MouthShape,
                MouthDepth = MouthDepth,
                MouthOpenPct = MouthOpenPct,
                WallRamp = WallRamp,
                WallAttackMs = WallAttackMs,
                BarrierWidth = BarrierWidth,
                WallBlend = WallBlend,
                SlotHalfWidth = SlotHalfWidth,
                ChannelFreeDepth = ChannelFreeDepth,
                Pattern = Pattern,
                SeqPulseMs = SeqPulseMs,
                SeqOvertravel = SeqOvertravel,
                SeqStopForcePct = SeqStopForcePct,
                SeqClickPct = SeqClickPct,
                PrndLaneHalfLength = PrndLaneHalfLength,
                PrndDetentForcePct = PrndDetentForcePct,
                PrndNotchHalfWidth = PrndNotchHalfWidth,
                PrndStopForcePct = PrndStopForcePct,

                FxEngineEnabled = FxEngineEnabled,
                FxEngineGainPct = FxEngineGainPct,
                FxEngineFreqAt1000Rpm = FxEngineFreqAt1000Rpm,
                FxLimiterEnabled = FxLimiterEnabled,
                FxLimiterGainPct = FxLimiterGainPct,
                FxLimiterFreqHz = FxLimiterFreqHz,
                FxLimiterFromPct = FxLimiterFromPct,
                FxAbsEnabled = FxAbsEnabled,
                FxAbsGainPct = FxAbsGainPct,
                FxAbsFreqHz = FxAbsFreqHz,
                FxTcEnabled = FxTcEnabled,
                FxTcGainPct = FxTcGainPct,
                FxTcFreqHz = FxTcFreqHz,
                FxCurbsEnabled = FxCurbsEnabled,
                FxCurbsGainPct = FxCurbsGainPct,
                FxCurbsFreqHz = FxCurbsFreqHz,
                FxCurbsFullAtG = FxCurbsFullAtG,
                FxShiftEnabled = FxShiftEnabled,
                FxShiftGainPct = FxShiftGainPct,
                FxShiftFreqHz = FxShiftFreqHz,
                FxShiftDurationMs = FxShiftDurationMs,
                FxCustomEnabled = FxCustomEnabled,
                FxCustomGainPct = FxCustomGainPct,
                FxCustomFreqHz = FxCustomFreqHz,
                GrindEnabled = GrindEnabled,
                GrindGainPct = GrindGainPct,
                GrindFreqHz = GrindFreqHz,
                GrindWallPct = GrindWallPct,
                GrindClutchThresholdPct = GrindClutchThresholdPct,
                GrindMinSpeedKmh = GrindMinSpeedKmh,
                GrindRejectsGear = GrindRejectsGear,
                GrindClutchMode = GrindClutchMode,
                ClutchBitePointPct = ClutchBitePointPct,
                FxBiteEnabled = FxBiteEnabled,
                FxBiteGainPct = FxBiteGainPct,
                FxBiteFreqHz = FxBiteFreqHz,
                FxBiteDurationMs = FxBiteDurationMs,
                ClutchSource = ClutchSource,
                PedalDeviceId = PedalDeviceId,
                PedalAxisIndex = PedalAxisIndex,
                PedalCalibration = PedalCalibrated
                    ? new AxisCalibration
                    {
                        RawMin = PedalRawMin,
                        RawMax = PedalRawMax,
                        DeadzoneLow = PedalDeadzoneLow,
                        DeadzoneHigh = PedalDeadzoneHigh,
                        Invert = PedalInvert
                    }
                    : null,
                DamperCoeff = DamperCoeff,
                DetentResistPct = DetentResistPct,
                DetentPullPct = DetentPullPct,
                DetentHoldPct = DetentHoldPct,
                SlotStopForcePct = SlotStopForcePct,
                SlotOvertravel = SlotOvertravel,
                DampingPct = DampingPct,
                WallFrictionPct = WallFrictionPct,
                WallYieldPct = WallYieldPct,
                LockoutForcePct = LockoutForcePct,
                LockoutHalfWidth = LockoutHalfWidth
            };
        }

        /// <summary>
        /// Which group of settings a reset should touch. Measured polarity is deliberately not
        /// part of "forces" or "geometry": it describes the hardware, not a preference, and
        /// throwing it away would silently re-arm the force cap and send the gate backwards.
        /// </summary>
        public enum ResetScope
        {
            Forces,
            Geometry,
            Calibration,

            /// <summary>The telemetry effects and the grind. Separate from Forces on purpose:
            /// resetting the gate should not silently turn a tuned effect set off.</summary>
            Effects,

            Everything
        }

        public void ResetToDefaults(ResetScope scope)
        {
            var d = new ShifterSettings();

            if (scope == ResetScope.Forces || scope == ResetScope.Everything)
            {
                OverallGainPct = d.OverallGainPct;
                LockoutForcePct = d.LockoutForcePct;
                LockoutHalfWidth = d.LockoutHalfWidth;
                ColumnPinForcePct = d.ColumnPinForcePct;
                ChannelWallForcePct = d.ChannelWallForcePct;
                ChannelGuideForcePct = d.ChannelGuideForcePct;
                ColumnDetentForcePct = d.ColumnDetentForcePct;
                BarrierForcePct = d.BarrierForcePct;
                HomeSpringPct = d.HomeSpringPct;
                MouthShape = d.MouthShape;
                MouthDepth = d.MouthDepth;
                MouthOpenPct = d.MouthOpenPct;
                WallRamp = d.WallRamp;
                WallAttackMs = d.WallAttackMs;
                BarrierWidth = d.BarrierWidth;
                WallBlend = d.WallBlend;
                SlotHalfWidth = d.SlotHalfWidth;
                ChannelFreeDepth = d.ChannelFreeDepth;
                SeqPulseMs = d.SeqPulseMs;
                SeqOvertravel = d.SeqOvertravel;
                SeqStopForcePct = d.SeqStopForcePct;
                SeqClickPct = d.SeqClickPct;
                PrndLaneHalfLength = d.PrndLaneHalfLength;
                PrndDetentForcePct = d.PrndDetentForcePct;
                PrndNotchHalfWidth = d.PrndNotchHalfWidth;
                PrndStopForcePct = d.PrndStopForcePct;
                DamperCoeff = d.DamperCoeff;
                DetentResistPct = d.DetentResistPct;
                DetentPullPct = d.DetentPullPct;
                DetentHoldPct = d.DetentHoldPct;
                SlotStopForcePct = d.SlotStopForcePct;
                SlotOvertravel = d.SlotOvertravel;
                DampingPct = d.DampingPct;
                WallFrictionPct = d.WallFrictionPct;
                WallYieldPct = d.WallYieldPct;
            }

            if (scope == ResetScope.Geometry || scope == ResetScope.Everything)
            {
                ChannelHalfEnter = d.ChannelHalfEnter;
                ChannelHalfExit = d.ChannelHalfExit;
                ColumnEdgeEnter = d.ColumnEdgeEnter;
                ColumnInnerHalfEnter = d.ColumnInnerHalfEnter;
                ColumnInnerHalfExit = d.ColumnInnerHalfExit;
                EngageDepth = d.EngageDepth;
                ReleaseDepth = d.ReleaseDepth;
                DetentHysteresis = d.DetentHysteresis;
                MinEngageTicks = d.MinEngageTicks;
                TickHz = d.TickHz;
            }

            if (scope == ResetScope.Effects || scope == ResetScope.Everything)
            {
                FxEngineEnabled = d.FxEngineEnabled;
                FxEngineGainPct = d.FxEngineGainPct;
                FxEngineFreqAt1000Rpm = d.FxEngineFreqAt1000Rpm;
                FxLimiterEnabled = d.FxLimiterEnabled;
                FxLimiterGainPct = d.FxLimiterGainPct;
                FxLimiterFreqHz = d.FxLimiterFreqHz;
                FxLimiterFromPct = d.FxLimiterFromPct;
                FxAbsEnabled = d.FxAbsEnabled;
                FxAbsGainPct = d.FxAbsGainPct;
                FxAbsFreqHz = d.FxAbsFreqHz;
                FxTcEnabled = d.FxTcEnabled;
                FxTcGainPct = d.FxTcGainPct;
                FxTcFreqHz = d.FxTcFreqHz;
                FxCurbsEnabled = d.FxCurbsEnabled;
                FxCurbsGainPct = d.FxCurbsGainPct;
                FxCurbsFreqHz = d.FxCurbsFreqHz;
                FxCurbsFullAtG = d.FxCurbsFullAtG;
                FxShiftEnabled = d.FxShiftEnabled;
                FxShiftGainPct = d.FxShiftGainPct;
                FxShiftFreqHz = d.FxShiftFreqHz;
                FxShiftDurationMs = d.FxShiftDurationMs;
                FxCustomEnabled = d.FxCustomEnabled;
                FxCustomProperty = d.FxCustomProperty;
                FxCustomGainPct = d.FxCustomGainPct;
                FxCustomFreqHz = d.FxCustomFreqHz;
                GrindEnabled = d.GrindEnabled;
                GrindGainPct = d.GrindGainPct;
                GrindFreqHz = d.GrindFreqHz;
                GrindWallPct = d.GrindWallPct;
                GrindClutchThresholdPct = d.GrindClutchThresholdPct;
                GrindMinSpeedKmh = d.GrindMinSpeedKmh;
                GrindRejectsGear = d.GrindRejectsGear;
                GrindClutchMode = d.GrindClutchMode;
                ClutchBitePointPct = d.ClutchBitePointPct;
                FxBiteEnabled = d.FxBiteEnabled;
                FxBiteGainPct = d.FxBiteGainPct;
                FxBiteFreqHz = d.FxBiteFreqHz;
                FxBiteDurationMs = d.FxBiteDurationMs;
            }

            if (scope == ResetScope.Calibration || scope == ResetScope.Everything)
            {
                InvertConstantX = d.InvertConstantX;
                InvertConstantY = d.InvertConstantY;
                PolarityConfirmed = d.PolarityConfirmed;
                CalibrationForcePct = d.CalibrationForcePct;
            }

            if (scope == ResetScope.Everything)
            {
                MirrorColumns = d.MirrorColumns;
                MirrorSlots = d.MirrorSlots;
                FreeStick = d.FreeStick;
                VJoyDeviceId = d.VJoyDeviceId;
                VendorId = d.VendorId;
                ProductId = d.ProductId;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;
            OnChanged(propertyName);
        }

        protected void OnChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
