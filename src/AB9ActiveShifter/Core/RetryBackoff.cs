using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// "Do not try that again yet." A retry schedule with escalating waits, kept as a small pure
    /// object so the thing it guards can be tested without the device that made it fail.
    ///
    /// It exists because the absence of one was expensive. Opening a DirectInput device that is
    /// not there is not free - the failure costs milliseconds - and the clutch pedal's open was
    /// attempted on every tick of the 1 kHz loop whenever it failed. Its log line was throttled to
    /// thirty seconds from the start, so the loop rate was the only symptom: measured at 81 Hz
    /// against the 990 the same rig runs with the pedal plugged in. Everything about the gate's
    /// stability is argued from that loop rate.
    ///
    /// The lesson generalises past the pedal, which is why this is a named thing rather than two
    /// more fields: any I/O the tick can attempt and fail needs a gate like this, and a throttled
    /// log is not one - it hides the cost instead of paying it.
    /// </summary>
    public sealed class RetryBackoff
    {
        private readonly int[] _waitsMs;
        private int _index;
        private long _nextAtMs;

        /// <summary>
        /// Waits between attempts, in milliseconds, used in order and then held at the last.
        /// </summary>
        public RetryBackoff(params int[] waitsMs)
        {
            if (waitsMs == null || waitsMs.Length == 0) throw new ArgumentException("waitsMs");
            _waitsMs = waitsMs;
        }

        /// <summary>Whether another attempt is allowed now. True before the first failure.</summary>
        public bool Due(long nowMs)
        {
            return nowMs >= _nextAtMs;
        }

        /// <summary>The attempt failed: hold off for the next wait in the schedule.</summary>
        public void Failed(long nowMs)
        {
            int wait = _waitsMs[Math.Min(_index, _waitsMs.Length - 1)];
            if (_index < _waitsMs.Length) _index++;
            _nextAtMs = nowMs + wait;
        }

        /// <summary>The attempt worked: start again from the shortest wait next time.</summary>
        public void Succeeded()
        {
            Reset();
        }

        /// <summary>
        /// The situation changed - a different device was picked, a source was switched on - so
        /// the previous failure says nothing about this one and the next check is due at once.
        /// </summary>
        public void Reset()
        {
            _index = 0;
            _nextAtMs = 0;
        }
    }
}
