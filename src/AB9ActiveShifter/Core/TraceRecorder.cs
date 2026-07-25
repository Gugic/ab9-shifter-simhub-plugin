using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Records what the loop saw and what it did, one row per tick, so a complaint about feel can be
    /// replayed instead of guessed at.
    ///
    /// Every field is either an input to <see cref="ForceComposer.Compose"/> or its output, which is
    /// the point: a trace can be fed back through Core in a test and the forces recomputed exactly,
    /// so the question "what did the gate actually do to the lever there" has an answer rather than
    /// an opinion. Several rounds of tuning by description alone got the region right and the cause
    /// wrong more than once.
    ///
    /// The engine thread only ever writes into a preallocated buffer - no allocation, no file work,
    /// no locks - because it has a millisecond to spend and a stall there drops force to the hand.
    /// Saving happens later, off the loop.
    /// </summary>
    public sealed class TraceRecorder
    {
        /// <summary>One tick. A struct in a preallocated array, so recording allocates nothing.</summary>
        private struct Sample
        {
            public double Ms;
            public int X, Y, Vx, Vy, Fx, Fy;
            public byte State, Direction;

            /// <summary>Signed, because <see cref="Core.Column"/>.None is -1 and a byte turns it into 255.</summary>
            public sbyte Column;

            public sbyte Gear;
            public float DtMs;
        }

        /// <summary>Two minutes at 1 kHz. Recording stops rather than wrapping, so a trace has a start.</summary>
        public const int Capacity = 120000;

        private readonly Sample[] _samples = new Sample[Capacity];
        private volatile bool _recording;
        private int _count;
        private double _originMs;

        public bool IsRecording { get { return _recording; } }
        public int Count { get { return _count; } }
        public bool IsFull { get { return _count >= Capacity; } }

        /// <summary>Called from the UI. The loop notices on its next tick.</summary>
        public void Start()
        {
            _count = 0;
            _originMs = -1;
            _recording = true;
        }

        public void Stop()
        {
            _recording = false;
        }

        /// <summary>
        /// Engine thread. Deliberately does nothing that can block or allocate, and stops itself when
        /// full rather than overwriting, so what is saved is a contiguous stretch from the moment
        /// recording began.
        /// </summary>
        public void Add(double nowMs, int x, int y, int vx, int vy, double dtMs,
                        GateState state, Column column, ShiftDir direction, int gear,
                        int fx, int fy)
        {
            if (!_recording) return;

            int i = _count;
            if (i >= Capacity)
            {
                _recording = false;
                return;
            }

            if (_originMs < 0) _originMs = nowMs;

            _samples[i].Ms = nowMs - _originMs;
            _samples[i].X = x;
            _samples[i].Y = y;
            _samples[i].Vx = vx;
            _samples[i].Vy = vy;
            _samples[i].DtMs = (float)dtMs;
            _samples[i].State = (byte)state;
            _samples[i].Column = (sbyte)column;
            _samples[i].Direction = (byte)direction;
            _samples[i].Gear = (sbyte)gear;
            _samples[i].Fx = fx;
            _samples[i].Fy = fy;

            _count = i + 1;
        }

        /// <summary>
        /// Writes the trace as CSV and returns the path. Called off the engine thread, after
        /// recording has stopped. The header carries the settings that produced it, because a trace
        /// without its configuration cannot be replayed.
        /// </summary>
        public string Save(string directory, EngineConfig cfg, string note)
        {
            int n = Math.Min(_count, Capacity);
            Directory.CreateDirectory(directory);

            string name = "trace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv";
            string path = Path.Combine(directory, name);

            var sb = new StringBuilder(n * 64);
            sb.Append("# AB9 Active Shifter trace, ").Append(n).Append(" ticks").AppendLine();
            if (!string.IsNullOrEmpty(note)) sb.Append("# note: ").Append(note).AppendLine();
            sb.Append("# ").Append(Describe(cfg)).AppendLine();
            sb.AppendLine("# x,y in axis counts 0..65535 centre 32767; vx,vy counts/s; fx,fy DirectInput units as sent");
            sb.AppendLine("ms,x,y,vx,vy,dtMs,state,column,dir,gear,fx,fy");

            for (int i = 0; i < n; i++)
            {
                Sample s = _samples[i];
                sb.Append(s.Ms.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.X).Append(',').Append(s.Y).Append(',')
                  .Append(s.Vx).Append(',').Append(s.Vy).Append(',')
                  .Append(s.DtMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                  .Append((GateState)s.State).Append(',')
                  .Append((Column)s.Column).Append(',')
                  .Append((ShiftDir)s.Direction).Append(',')
                  .Append(s.Gear).Append(',')
                  .Append(s.Fx).Append(',').Append(s.Fy)
                  .AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            return path;
        }

        /// <summary>
        /// Every dial, by reflection over the config's public fields, in name=value form.
        ///
        /// Reflection rather than a hand-written list because the hand-written list went stale the
        /// first time it was used in anger: replaying a real trace reproduced the recorded lateral
        /// force only to within 431 DI, because the header had omitted the barrier width and the
        /// detent hysteresis, and the fore/aft force to within 1252, because it had omitted the
        /// detent profile. A header that does not carry every input is not a replayable trace, and
        /// the whole point of recording is to stop guessing. This runs once per save, off the loop.
        /// </summary>
        private static string Describe(EngineConfig c)
        {
            var fields = new List<FieldInfo>(
                typeof(EngineConfig).GetFields(BindingFlags.Public | BindingFlags.Instance));
            fields.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var sb = new StringBuilder();
            foreach (FieldInfo f in fields)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(f.Name).Append('=')
                  .Append(Convert.ToString(f.GetValue(c), CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
