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
        private int _calibrationForcePct = 25;
        private bool _polarityConfirmed;
        private bool _invertConstantX;
        private bool _invertConstantY;
        private bool _invertSpringX;
        private bool _invertSpringY;
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
        private int _dampingPct = 25;
        private int _wallYieldPct = 45;
        private int _damperCoeff = 800;
        private int _detentResistPct = 22;
        private int _detentPullPct = 35;
        private int _detentHoldPct = 55;

        private int _channelHalfEnter = 2600;
        private int _channelHalfExit = 5200;
        private int _columnEdgeEnter = 2600;
        private int _columnEdgeExit = 5000;
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
        public int LockoutHalfWidth { get { return _lockoutHalfWidth; } set { Set(ref _lockoutHalfWidth, value); } }

        /// <summary>Force used when measuring polarity. Raise it if calibration is inconclusive.</summary>
        public int CalibrationForcePct { get { return _calibrationForcePct; } set { Set(ref _calibrationForcePct, value); } }

        public bool PolarityConfirmed { get { return _polarityConfirmed; } set { Set(ref _polarityConfirmed, value); } }

        // Measured per axis and per effect family: this base inverts constant force on X but not
        // on Y, and the spring on Y but not on X.
        public bool InvertConstantX { get { return _invertConstantX; } set { Set(ref _invertConstantX, value); } }
        public bool InvertConstantY { get { return _invertConstantY; } set { Set(ref _invertConstantY, value); } }
        public bool InvertSpringX { get { return _invertSpringX; } set { Set(ref _invertSpringX, value); } }
        public bool InvertSpringY { get { return _invertSpringY; } set { Set(ref _invertSpringY, value); } }

        /// <summary>Put first gear at the right-hand end of the gate instead of the left.</summary>
        public bool MirrorColumns { get { return _mirrorColumns; } set { Set(ref _mirrorColumns, value); } }

        /// <summary>Swap each gear pair, so odd gears sit toward the player.</summary>
        public bool MirrorSlots { get { return _mirrorSlots; } set { Set(ref _mirrorSlots, value); } }

        /// <summary>Release all forces, to check how the stick moves with nothing applied.</summary>
        public bool FreeStick { get { return _freeStick; } set { Set(ref _freeStick, value); } }

        public uint VJoyDeviceId { get { return _vJoyDeviceId; } set { Set(ref _vJoyDeviceId, value); } }
        public int VendorId { get { return _vendorId; } set { Set(ref _vendorId, value); } }
        public int ProductId { get { return _productId; } set { Set(ref _productId, value); } }
        public int TickHz { get { return _tickHz; } set { Set(ref _tickHz, value); } }

        // Walls are constant forces expressed as a percentage of full scale. Spring
        // coefficients used to live here and could not produce a usable wall; see ForceComposer.
        public int ColumnPinForcePct { get { return _columnPinForcePct; } set { Set(ref _columnPinForcePct, value); } }
        public int ChannelWallForcePct { get { return _channelWallForcePct; } set { Set(ref _channelWallForcePct, value); } }
        public int ChannelGuideForcePct { get { return _channelGuideForcePct; } set { Set(ref _channelGuideForcePct, value); } }
        public int ColumnDetentForcePct { get { return _columnDetentForcePct; } set { Set(ref _columnDetentForcePct, value); } }

        public int BarrierForcePct { get { return _barrierForcePct; } set { Set(ref _barrierForcePct, value); } }

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

        public int WallRamp { get { return _wallRamp; } set { Set(ref _wallRamp, value); } }

        /// <summary>How many milliseconds a wall takes to reach full force on contact. The hammer fix.</summary>
        public int WallAttackMs { get { return _wallAttackMs; } set { Set(ref _wallAttackMs, value); } }
        public int BarrierWidth { get { return _barrierWidth; } set { Set(ref _barrierWidth, value); } }
        public int WallBlend { get { return _wallBlend; } set { Set(ref _wallBlend, value); } }

        /// <summary>Free lateral corridor inside a slot. Widen it if a gear shakes when seated; zero rails the slot.</summary>
        public int SlotHalfWidth { get { return _slotHalfWidth; } set { Set(ref _slotHalfWidth, value); } }

        /// <summary>Free fore/aft depth of the neutral tunnel before its centring begins. Zero rails the tunnel.</summary>
        public int ChannelFreeDepth { get { return _channelFreeDepth; } set { Set(ref _channelFreeDepth, value); } }

        /// <summary>Velocity damping. This, not the device damper, is what settles a stiff wall.</summary>
        public int DampingPct { get { return _dampingPct; } set { Set(ref _dampingPct, value); } }

        /// <summary>How much of a wall's force is given up on the rebound. The anti-buzz control.</summary>
        public int WallYieldPct { get { return _wallYieldPct; } set { Set(ref _wallYieldPct, value); } }

        public int DamperCoeff { get { return _damperCoeff; } set { Set(ref _damperCoeff, value); } }
        public int DetentResistPct { get { return _detentResistPct; } set { Set(ref _detentResistPct, value); } }
        public int DetentPullPct { get { return _detentPullPct; } set { Set(ref _detentPullPct, value); } }
        public int DetentHoldPct { get { return _detentHoldPct; } set { Set(ref _detentHoldPct, value); } }

        public int ChannelHalfEnter { get { return _channelHalfEnter; } set { Set(ref _channelHalfEnter, value); } }
        public int ChannelHalfExit { get { return _channelHalfExit; } set { Set(ref _channelHalfExit, value); } }
        public int ColumnEdgeEnter { get { return _columnEdgeEnter; } set { Set(ref _columnEdgeEnter, value); } }
        public int ColumnEdgeExit { get { return _columnEdgeExit; } set { Set(ref _columnEdgeExit, value); } }
        public int ColumnInnerHalfEnter { get { return _columnInnerHalfEnter; } set { Set(ref _columnInnerHalfEnter, value); } }
        public int ColumnInnerHalfExit { get { return _columnInnerHalfExit; } set { Set(ref _columnInnerHalfExit, value); } }
        public int EngageDepth { get { return _engageDepth; } set { Set(ref _engageDepth, value); } }
        public int ReleaseDepth { get { return _releaseDepth; } set { Set(ref _releaseDepth, value); } }
        public int DetentHysteresis { get { return _detentHysteresis; } set { Set(ref _detentHysteresis, value); } }
        public int MinEngageTicks { get { return _minEngageTicks; } set { Set(ref _minEngageTicks, value); } }

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
                InvertSpringX = InvertSpringX,
                InvertSpringY = InvertSpringY,
                MirrorColumns = MirrorColumns,
                MirrorSlots = MirrorSlots,
                FreeStick = FreeStick,
                PolarityConfirmed = PolarityConfirmed,
                OverallGainPct = OverallGainPct,
                CalibrationForcePct = CalibrationForcePct,

                ChannelHalfEnter = ChannelHalfEnter,
                ChannelHalfExit = ChannelHalfExit,
                ColumnEdgeEnter = ColumnEdgeEnter,
                ColumnEdgeExit = ColumnEdgeExit,
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
                MouthShape = MouthShape,
                MouthDepth = MouthDepth,
                MouthOpenPct = MouthOpenPct,
                WallRamp = WallRamp,
                WallAttackMs = WallAttackMs,
                BarrierWidth = BarrierWidth,
                WallBlend = WallBlend,
                SlotHalfWidth = SlotHalfWidth,
                ChannelFreeDepth = ChannelFreeDepth,
                DamperCoeff = DamperCoeff,
                DetentResistPct = DetentResistPct,
                DetentPullPct = DetentPullPct,
                DetentHoldPct = DetentHoldPct,
                DampingPct = DampingPct,
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
                MouthShape = d.MouthShape;
                MouthDepth = d.MouthDepth;
                MouthOpenPct = d.MouthOpenPct;
                WallRamp = d.WallRamp;
                WallAttackMs = d.WallAttackMs;
                BarrierWidth = d.BarrierWidth;
                WallBlend = d.WallBlend;
                SlotHalfWidth = d.SlotHalfWidth;
                ChannelFreeDepth = d.ChannelFreeDepth;
                DamperCoeff = d.DamperCoeff;
                DetentResistPct = d.DetentResistPct;
                DetentPullPct = d.DetentPullPct;
                DetentHoldPct = d.DetentHoldPct;
                DampingPct = d.DampingPct;
                WallYieldPct = d.WallYieldPct;
            }

            if (scope == ResetScope.Geometry || scope == ResetScope.Everything)
            {
                ChannelHalfEnter = d.ChannelHalfEnter;
                ChannelHalfExit = d.ChannelHalfExit;
                ColumnEdgeEnter = d.ColumnEdgeEnter;
                ColumnEdgeExit = d.ColumnEdgeExit;
                ColumnInnerHalfEnter = d.ColumnInnerHalfEnter;
                ColumnInnerHalfExit = d.ColumnInnerHalfExit;
                EngageDepth = d.EngageDepth;
                ReleaseDepth = d.ReleaseDepth;
                DetentHysteresis = d.DetentHysteresis;
                MinEngageTicks = d.MinEngageTicks;
                TickHz = d.TickHz;
            }

            if (scope == ResetScope.Calibration || scope == ResetScope.Everything)
            {
                InvertConstantX = d.InvertConstantX;
                InvertConstantY = d.InvertConstantY;
                InvertSpringX = d.InvertSpringX;
                InvertSpringY = d.InvertSpringY;
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
