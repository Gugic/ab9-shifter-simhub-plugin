namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// One pending onboard tune, replaced by each edit. The owner supplies a monotonic tick
    /// and drains only when hardware is available; neither a busy read nor an in-flight write
    /// consumes a newer edit. Profile/mode changes cancel the outgoing tune explicitly.
    /// </summary>
    public sealed class NativeSettingsDebounce
    {
        public const int QuietPeriodMs = 500;
        private Ab9NativeSettings _pending;
        private int _changedAt;

        public bool Pending { get { return _pending != null; } }

        public void Schedule(Ab9NativeSettings settings, int now)
        {
            _pending = settings.Copy();
            _changedAt = now;
        }

        public bool TryTake(int now, bool available, out Ab9NativeSettings settings)
        {
            settings = null;
            if (!available || !Pending || unchecked(now - _changedAt) < QuietPeriodMs) return false;
            settings = _pending;
            _pending = null;
            return true;
        }

        public void Cancel() { _pending = null; }
    }
}
