using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Turns the polled positions into the velocity the force model keys on.
    ///
    /// The obvious estimator - difference adjacent ticks, smooth a little - is wrong on this
    /// hardware in a way that was felt before it was found. While constant-force writes are in
    /// flight every millisecond, distinct position values only arrive at about 500 Hz, so
    /// alternate 1 kHz polls return an unchanged snapshot on both axes. Adjacent-tick
    /// differencing turns a smooth 17000 count/s sweep into a 2:1 sawtooth at 250-500 Hz
    /// (measured: raw deltas of -34, -1, -36, -2 during one such sweep), and anything that
    /// keys force on speed renders that sawtooth as a grinding texture - the rebound absorber
    /// at 59% was measured rippling the wall force by 25-50% at that rate, felt as pushing
    /// the lever against a running gear.
    ///
    /// So the difference is taken across a ~4 ms window instead: wide enough to always span
    /// two fresh reports, so the stale-then-jump pattern sums to its true mean, and an exact
    /// null for the 2 ms report clock. A light EMA on top handles residual jitter. The group
    /// delay is 2-3 ms - comparable to the position-to-torque round trip itself - and the
    /// consumers are the yield and the damping, which act over tens of milliseconds. More EMA
    /// smoothing instead of the window was considered and rejected: smoothing is phase lag at
    /// every frequency, while the window cancels the one artifact frequency outright.
    /// </summary>
    public sealed class VelocityEstimator
    {
        /// <summary>Age of the reference sample the difference is taken against.</summary>
        private const double WindowMs = 4.0;

        /// <summary>
        /// EMA weight on top of the windowed difference. Kept light: smoothing is also phase
        /// lag, and a badly lagged velocity engages the yield after the launch it should have
        /// absorbed.
        /// </summary>
        private const double Smoothing = 0.45;

        /// <summary>
        /// A gap longer than this says nothing about speed - the thread stalled or the device
        /// went away - so the history is dropped and the last estimate is kept, the same
        /// resynchronise-don't-spike behaviour the old estimator had.
        /// </summary>
        private const double StallMs = 50.0;

        private const int Capacity = 16;

        private readonly double[] _ms = new double[Capacity];
        private readonly int[] _xs = new int[Capacity];
        private readonly int[] _ys = new int[Capacity];
        private int _head;
        private int _count;

        /// <summary>Estimated speeds in axis counts per second.</summary>
        public int X { get; private set; }
        public int Y { get; private set; }

        /// <summary>Forgets the current motion estimate, so a jump in position is not read as speed.</summary>
        public void Reset()
        {
            _head = 0;
            _count = 0;
            X = 0;
            Y = 0;
        }

        public void Update(int x, int y, double nowMs)
        {
            if (_count > 0)
            {
                double newest = _ms[(_head + Capacity - 1) % Capacity];
                if (nowMs - newest > StallMs || nowMs < newest)
                {
                    _head = 0;
                    _count = 0;
                }
            }

            _ms[_head] = nowMs;
            _xs[_head] = x;
            _ys[_head] = y;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;

            // Reference: the stored sample whose age is nearest the window. At a 1 ms tick this
            // is simply the sample four ticks back; at a slower tick it is whichever ring entry
            // lands closest, so the window degrades gracefully rather than assuming the rate.
            int best = -1;
            double bestErr = double.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - 1 - i + Capacity * 2) % Capacity;
                double err = Math.Abs((nowMs - _ms[idx]) - WindowMs);
                if (err < bestErr)
                {
                    bestErr = err;
                    best = idx;
                }
            }

            double dt = nowMs - _ms[best];
            if (dt < 1.0) return; // not enough history yet to say anything new

            double rawX = (x - _xs[best]) * 1000.0 / dt;
            double rawY = (y - _ys[best]) * 1000.0 / dt;

            X += (int)Math.Round((rawX - X) * Smoothing);
            Y += (int)Math.Round((rawY - Y) * Smoothing);
        }
    }
}
