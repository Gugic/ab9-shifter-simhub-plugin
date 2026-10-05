namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Records every float-context configuration edge, even while the force loop is stopped.
    /// Stamp before publishing a configuration; the engine serializes these calls. The tick
    /// can then discard learning and match hysteresis even if it missed an off/on or a
    /// profile/manual-ratio round trip. Force-only edits preserve the session.
    /// </summary>
    public sealed class FloatShiftConfigTracker
    {
        private bool _seeded, _enabled;
        private GatePattern _pattern;
        private string _profile, _manual;
        private int _epoch;

        public void Stamp(EngineConfig cfg)
        {
            if (!_seeded || _enabled != cfg.FloatShiftingEnabled || _pattern != cfg.Pattern
                || _profile != cfg.FloatProfileKey || _manual != cfg.FloatRpmAt100KmhText)
                _epoch = unchecked(_epoch + 1);
            _seeded = true;
            _enabled = cfg.FloatShiftingEnabled;
            _pattern = cfg.Pattern;
            _profile = cfg.FloatProfileKey;
            _manual = cfg.FloatRpmAt100KmhText;
            cfg.FloatConfigEpoch = _epoch;
        }
    }
}
