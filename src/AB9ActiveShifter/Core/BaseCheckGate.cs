namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// "Read the base before you open it." Whether the engine may open a base whose firmware can
    /// be running its own gate, kept as a small pure object so the rule can be tested without the
    /// base.
    ///
    /// It exists because the plugin checked the AB9's mode once per SimHub start and never again.
    /// A base switched off and on while SimHub runs comes back to an engine that simply reopens
    /// it - and on 2026-10-25 a power-up read found the base in native H-pattern mode, where the
    /// firmware runs its own gate. The engine never sees a power cycle that happens while it does
    /// not hold the device (lost, yielded to a game, stopped), so the only rule that covers every
    /// path is: no open without a read that recently found flight mode.
    ///
    /// The read happens off the engine thread (the CDC worker), so the engine asks and the plugin
    /// answers; this object is the meeting point. Calls arrive from both threads, hence the lock.
    /// The open path runs only while the base is not open, never on the 1 kHz tick.
    /// </summary>
    public sealed class BaseCheckGate
    {
        /// <summary>
        /// How long a read that found flight mode vouches for the base. A read this recent was
        /// answered by the same power-up the open will reach: switching the base off and having it
        /// back on USB took 13 s on this rig (2026-10-25, lost 14:21:29, reopened 14:21:42).
        /// </summary>
        public const int FreshForMs = 3000;

        /// <summary>
        /// How often a waiting engine asks again. A read that found no base - it is still off,
        /// or still booting - is simply not answered, and the next ask retries it.
        /// </summary>
        public const int AskAgainMs = 2000;

        private readonly object _sync = new object();
        private bool _checked;
        private int _checkedAt;
        private bool _asked;
        private int _askedAt;

        /// <summary>A read found the base in flight mode. Called by the plugin after the read.</summary>
        public void Checked(int nowTick)
        {
            lock (_sync)
            {
                _checked = true;
                _checkedAt = nowTick;
            }
        }

        /// <summary>
        /// Whether a read recent enough to vouch for the base has happened. Ticks are
        /// <c>Environment.TickCount</c>, compared by unchecked difference so the 25-day wrap is
        /// harmless; a read stamped after <paramref name="nowTick"/> waits for the next look.
        /// </summary>
        public bool MayOpen(int nowTick)
        {
            lock (_sync)
            {
                if (!_checked) return false;
                int age = unchecked(nowTick - _checkedAt);
                return age >= 0 && age <= FreshForMs;
            }
        }

        /// <summary>Whether a waiting engine should ask for a read now: at once, then every <see cref="AskAgainMs"/>.</summary>
        public bool ShouldAsk(int nowTick)
        {
            lock (_sync)
            {
                if (_asked)
                {
                    int since = unchecked(nowTick - _askedAt);
                    if (since >= 0 && since < AskAgainMs) return false;
                }
                _asked = true;
                _askedAt = nowTick;
                return true;
            }
        }

        /// <summary>The base was opened. The next time the engine waits, it asks at once.</summary>
        public void Opened()
        {
            lock (_sync) _asked = false;
        }
    }
}
