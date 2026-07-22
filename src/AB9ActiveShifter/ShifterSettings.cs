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
        // force. That must be a deliberate act, after the Pit House pre-flight steps, with
        // the user at the stick.
        private bool _enabled;
        private int _overallGainPct = 25;
        private int _lockoutForcePct = 70;
        private bool _polarityConfirmed;
        private bool _invertSpringPolarity;
        private bool _invertConstantPolarity;
        private bool _invertX;
        private bool _invertY;
        private uint _vJoyDeviceId = 1;
        private int _vendorId = 0x346E;
        private int _productId = 0x1000;
        private int _tickHz = 400;

        private int _wallCoeff = 8000;
        private int _neutralDetentCoeff = 600;
        private int _channelGuideCoeff = 600;
        private int _channelWallCoeff = 9500;
        private int _damperCoeff = 800;
        private int _detentResistMax = 2200;
        private int _detentPullMax = 3000;
        private int _detentHold = 1600;
        private int _springDeadBand = 150;
        private int _channelDeadBand = 430;

        private int _channelHalfEnter = 1400;
        private int _channelHalfExit = 2400;
        private int _columnEdgeEnter = 2600;
        private int _columnEdgeExit = 5000;
        private int _columnInnerHalfEnter = 1200;
        private int _columnInnerHalfExit = 2400;
        private int _engageDepth = 4000;
        private int _releaseDepth = 8000;
        private int _lockoutStart = 48000;
        private int _lockoutRamp = 2500;
        private int _detentHysteresis = 1500;
        private int _minEngageTicks = 2;

        public bool Enabled { get { return _enabled; } set { Set(ref _enabled, value); } }

        /// <summary>Master force scale. Capped to 10% until the polarity wizard has run.</summary>
        public int OverallGainPct { get { return _overallGainPct; } set { Set(ref _overallGainPct, value); } }

        /// <summary>Force needed to push through into the 7/R column, as a share of the overall gain.</summary>
        public int LockoutForcePct { get { return _lockoutForcePct; } set { Set(ref _lockoutForcePct, value); } }

        public bool PolarityConfirmed { get { return _polarityConfirmed; } set { Set(ref _polarityConfirmed, value); } }
        public bool InvertSpringPolarity { get { return _invertSpringPolarity; } set { Set(ref _invertSpringPolarity, value); } }
        public bool InvertConstantPolarity { get { return _invertConstantPolarity; } set { Set(ref _invertConstantPolarity, value); } }
        public bool InvertX { get { return _invertX; } set { Set(ref _invertX, value); } }
        public bool InvertY { get { return _invertY; } set { Set(ref _invertY, value); } }

        public uint VJoyDeviceId { get { return _vJoyDeviceId; } set { Set(ref _vJoyDeviceId, value); } }
        public int VendorId { get { return _vendorId; } set { Set(ref _vendorId, value); } }
        public int ProductId { get { return _productId; } set { Set(ref _productId, value); } }
        public int TickHz { get { return _tickHz; } set { Set(ref _tickHz, value); } }

        public int WallCoeff { get { return _wallCoeff; } set { Set(ref _wallCoeff, value); } }
        public int NeutralDetentCoeff { get { return _neutralDetentCoeff; } set { Set(ref _neutralDetentCoeff, value); } }
        public int ChannelGuideCoeff { get { return _channelGuideCoeff; } set { Set(ref _channelGuideCoeff, value); } }
        public int ChannelWallCoeff { get { return _channelWallCoeff; } set { Set(ref _channelWallCoeff, value); } }
        public int DamperCoeff { get { return _damperCoeff; } set { Set(ref _damperCoeff, value); } }
        public int DetentResistMax { get { return _detentResistMax; } set { Set(ref _detentResistMax, value); } }
        public int DetentPullMax { get { return _detentPullMax; } set { Set(ref _detentPullMax, value); } }
        public int DetentHold { get { return _detentHold; } set { Set(ref _detentHold, value); } }
        public int SpringDeadBand { get { return _springDeadBand; } set { Set(ref _springDeadBand, value); } }
        public int ChannelDeadBand { get { return _channelDeadBand; } set { Set(ref _channelDeadBand, value); } }

        public int ChannelHalfEnter { get { return _channelHalfEnter; } set { Set(ref _channelHalfEnter, value); } }
        public int ChannelHalfExit { get { return _channelHalfExit; } set { Set(ref _channelHalfExit, value); } }
        public int ColumnEdgeEnter { get { return _columnEdgeEnter; } set { Set(ref _columnEdgeEnter, value); } }
        public int ColumnEdgeExit { get { return _columnEdgeExit; } set { Set(ref _columnEdgeExit, value); } }
        public int ColumnInnerHalfEnter { get { return _columnInnerHalfEnter; } set { Set(ref _columnInnerHalfEnter, value); } }
        public int ColumnInnerHalfExit { get { return _columnInnerHalfExit; } set { Set(ref _columnInnerHalfExit, value); } }
        public int EngageDepth { get { return _engageDepth; } set { Set(ref _engageDepth, value); } }
        public int ReleaseDepth { get { return _releaseDepth; } set { Set(ref _releaseDepth, value); } }
        public int LockoutStart { get { return _lockoutStart; } set { Set(ref _lockoutStart, value); } }
        public int LockoutRamp { get { return _lockoutRamp; } set { Set(ref _lockoutRamp, value); } }
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

                InvertX = InvertX,
                InvertY = InvertY,
                InvertSpringPolarity = InvertSpringPolarity,
                InvertConstantPolarity = InvertConstantPolarity,
                PolarityConfirmed = PolarityConfirmed,
                OverallGainPct = OverallGainPct,

                ChannelHalfEnter = ChannelHalfEnter,
                ChannelHalfExit = ChannelHalfExit,
                ColumnEdgeEnter = ColumnEdgeEnter,
                ColumnEdgeExit = ColumnEdgeExit,
                ColumnInnerHalfEnter = ColumnInnerHalfEnter,
                ColumnInnerHalfExit = ColumnInnerHalfExit,
                EngageDepth = EngageDepth,
                ReleaseDepth = ReleaseDepth,
                LockoutStart = LockoutStart,
                LockoutRamp = LockoutRamp,
                DetentHysteresis = DetentHysteresis,
                MinEngageTicks = MinEngageTicks,

                NeutralDetentCoeff = NeutralDetentCoeff,
                WallCoeff = WallCoeff,
                ChannelGuideCoeff = ChannelGuideCoeff,
                ChannelWallCoeff = ChannelWallCoeff,
                DamperCoeff = DamperCoeff,
                SpringDeadBand = SpringDeadBand,
                ChannelDeadBand = ChannelDeadBand,
                DetentResistMax = DetentResistMax,
                DetentPullMax = DetentPullMax,
                DetentHold = DetentHold,
                LockoutForcePct = LockoutForcePct
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;

            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
