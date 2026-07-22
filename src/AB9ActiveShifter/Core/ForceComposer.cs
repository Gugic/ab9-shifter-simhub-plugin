using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Maps gate state and stick position onto the forces for one tick.
    ///
    /// The gate is built from three pieces:
    ///   X spring  - soft detents onto each column while neutral, a stiff pin onto the
    ///               latched column once travelling, so the stick tracks the gate walls.
    ///   Y spring  - soft while lined up with a column so a gear can be selected, very
    ///               stiff between columns so it cannot be. This swap is what the hand
    ///               reads as the walls of the H.
    ///   X/Y constant force - the lockout push and the slot detent respectively.
    ///
    /// The lockout is a constant force rather than a spring on purpose. A DirectInput
    /// spring caps at coefficient 10000, which yields roughly one force unit per position
    /// unit; reaching 70% force would need ~22900 axis counts of travel, more than the
    /// whole lockout zone. A software-shaped constant force reaches the requested plateau
    /// in a settable ramp and then holds it, which is what a real lockout gate feels like.
    /// </summary>
    public sealed class ForceComposer
    {
        private readonly GateGeometry _geo;
        private readonly EngineConfig _cfg;

        private readonly int _neutralDetentCoeff;
        private readonly int _wallCoeff;
        private readonly int _channelGuideCoeff;
        private readonly int _channelWallCoeff;
        private readonly int _detentResistMax;
        private readonly int _detentPullMax;
        private readonly int _detentHold;
        private readonly int _lockoutForce;
        private readonly int _damperCoeff;
        private readonly int _constantSign;

        private Column _detentColumn = Column.C2;

        public ForceComposer(GateGeometry geometry, EngineConfig config)
        {
            _geo = geometry;
            _cfg = config;

            double gain = config.EffectiveGain;

            // A firmware that inverts effect direction is corrected by flipping the sign of
            // the spring coefficients; saturations stay positive because they are magnitude
            // clamps, not directions.
            int springSign = config.InvertSpringPolarity ? -1 : 1;
            _constantSign = config.InvertConstantPolarity ? -1 : 1;

            _neutralDetentCoeff = Scale(config.NeutralDetentCoeff, gain) * springSign;
            _wallCoeff = Scale(config.WallCoeff, gain) * springSign;
            _channelGuideCoeff = Scale(config.ChannelGuideCoeff, gain) * springSign;
            _channelWallCoeff = Scale(config.ChannelWallCoeff, gain) * springSign;

            _detentResistMax = Scale(config.DetentResistMax, gain);
            _detentPullMax = Scale(config.DetentPullMax, gain);
            _detentHold = Scale(config.DetentHold, gain);
            _damperCoeff = Scale(config.DamperCoeff, gain);

            // The lockout percentage is relative to the plugin's overall gain, so raising the
            // master force raises the lockout with it and the ratio the user tuned is kept.
            _lockoutForce = Scale(
                (int)Math.Round(GateGeometry.ForceMax * GateGeometry.Clamp(config.LockoutForcePct, 0, 100) / 100.0),
                gain);
        }

        /// <summary>Gain-scaled damping applied on every frame.</summary>
        public int DamperCoefficient { get { return _damperCoeff; } }

        /// <summary>The lockout plateau in DirectInput units, for display in the UI.</summary>
        public int LockoutForce { get { return _lockoutForce; } }

        private static int Scale(int value, double gain)
        {
            return (int)Math.Round(value * gain);
        }

        public ForceFrame Compose(GateState state, Column column, ShiftDir direction, int x, int y)
        {
            ForceFrame frame = state == GateState.Neutral
                ? ComposeNeutral(x)
                : ComposeInColumn(column, direction, y);

            frame.DamperCoefficient = _damperCoeff;
            return frame;
        }

        private ForceFrame ComposeNeutral(int x)
        {
            ForceFrame f = new ForceFrame();

            if (_geo.InLockoutZone(x))
            {
                // Past the lockout boundary the push-back is the only X force, so the feel is
                // exactly the shaped plateau and nothing fights it.
                f.SpringX = SpringPreset.Off;
                f.ConstantX = -LockoutMagnitude(x) * _constantSign;
            }
            else
            {
                _detentColumn = _geo.NearestMainColumn(x, _detentColumn);
                f.SpringX = SpringPreset.Centering(
                    GateGeometry.AxisToDi(_geo.ColumnTarget(_detentColumn)),
                    _neutralDetentCoeff,
                    _cfg.SpringDeadBand);
                f.ConstantX = 0;
            }

            bool alignedWithColumn = _geo.ColumnAt(x) != Column.None;
            f.SpringY = SpringPreset.Centering(
                0,
                alignedWithColumn ? _channelGuideCoeff : _channelWallCoeff,
                _cfg.ChannelDeadBand);

            f.ConstantY = 0;
            return f;
        }

        private ForceFrame ComposeInColumn(Column column, ShiftDir direction, int y)
        {
            ForceFrame f = new ForceFrame();

            f.SpringX = SpringPreset.Centering(
                GateGeometry.AxisToDi(_geo.ColumnTarget(column)),
                _wallCoeff,
                _cfg.SpringDeadBand);

            // The column wall holds the stick laterally, so the channel spring must be out of
            // the way; the slot detent alone shapes the fore/aft feel.
            f.SpringY = SpringPreset.Off;
            f.ConstantX = 0;
            f.ConstantY = DetentMagnitude(direction, y) * _constantSign;

            return f;
        }

        /// <summary>Ramps to the lockout plateau over LockoutRamp counts, then holds it.</summary>
        private int LockoutMagnitude(int x)
        {
            int ramp = Math.Max(1, _cfg.LockoutRamp);
            double t = GateGeometry.Clamp((x - _geo.LockoutStart) / (double)ramp, 0.0, 1.0);
            return (int)Math.Round(_lockoutForce * t);
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
