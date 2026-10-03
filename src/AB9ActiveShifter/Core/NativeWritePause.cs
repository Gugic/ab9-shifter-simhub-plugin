using System.Threading;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// An onboard write suspends output without changing the user's session switch. Setup
    /// stays off; ordinary applies may resume only an already-enabled, uncancelled session.
    /// The plugin settles the switch before releasing this gate and pushing the engine config.
    /// </summary>
    public sealed class NativeWritePause
    {
        private const int OffAfterWrite = 1;
        private const int ResumeAllowed = 2;
        private int _state;

        public bool Active { get { return Volatile.Read(ref _state) != 0; } }

        public bool TryBegin(bool preserveEnabled, bool enabled)
        {
            int state = preserveEnabled && enabled ? ResumeAllowed : OffAfterWrite;
            return Interlocked.CompareExchange(ref _state, state, 0) == 0;
        }

        public void CancelResume()
        {
            Interlocked.CompareExchange(ref _state, OffAfterWrite, ResumeAllowed);
        }

        public bool CanResume(bool verified, bool currentlyEnabled)
        {
            return verified && currentlyEnabled && Volatile.Read(ref _state) == ResumeAllowed;
        }

        public void End() { Interlocked.Exchange(ref _state, 0); }
    }
}
