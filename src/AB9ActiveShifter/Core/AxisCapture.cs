using System;
using System.Collections.Generic;

namespace AB9ActiveShifter.Core
{
    public enum CapturePhase
    {
        /// <summary>Learning the noise floor while the pedals sit at rest.</summary>
        Baseline,

        /// <summary>Watching for an axis that clearly beats that noise floor.</summary>
        Waiting,

        /// <summary>An axis is latched; accumulating its travel until the pedal comes back.</summary>
        Tracking,

        Committed,
        Cancelled,
        TimedOut
    }

    /// <summary>
    /// "Press the pedal you want to use", turned into a device, an axis and a calibration.
    ///
    /// <para>
    /// Three phases rather than one, because an axis is not a button. A button is unambiguous the
    /// moment a new bit appears, but an axis has to have its travel *measured*, which means
    /// watching until the pedal is released - committing on the first movement would calibrate
    /// against a partial press and every later reading would run short.
    /// </para>
    /// <para>
    /// The shape of this - baseline window, noise-scaled trigger, peak tracking, commit on return -
    /// is taken from the capture in the Fanadapter plugin, which solved the same problem against a
    /// different transport. Kept free of I/O and of timers, so the awkward cases can be tested
    /// rather than reproduced by hand on the rig: a pedal that rests at full scale, a device that
    /// says nothing at all through the baseline window, a user who presses and never lets go.
    /// </para>
    /// </summary>
    public sealed class AxisCapture
    {
        /// <summary>How long to watch pedals at rest before believing anything is movement.</summary>
        public const int BaselineMs = 400;

        /// <summary>Give up if nothing is pressed by then.</summary>
        public const int DeadlineMs = 12000;

        /// <summary>Movement must beat the resting band by this much to read as intent.</summary>
        private const double TriggerNoiseMultiple = 5;
        private const double MinimumTrigger = 500;

        /// <summary>How close to the resting point counts as released.</summary>
        private const double ReturnNoiseMultiple = 2;
        private const double MinimumReturnBand = 200;

        /// <summary>
        /// Travel needed before a return to rest reads as a release rather than as noise that
        /// never left. Without it, jitter inside the resting band commits a zero-range binding.
        /// </summary>
        private const double PressedTravelMultiple = 1.5;

        /// <summary>Deadzone above the measured jitter, capped so it cannot eat real travel.</summary>
        private const int DeadzoneNoiseMultiple = 3;
        private const int MaximumDeadzone = 5000;

        /// <summary>Floor on a committed span, so a division downstream can never see zero.</summary>
        private const int MinimumRange = 256;

        private sealed class Rest
        {
            public readonly Dictionary<int, int> Min = new Dictionary<int, int>();
            public readonly Dictionary<int, int> Max = new Dictionary<int, int>();
        }

        private sealed class Latched
        {
            public string DeviceId;
            public int Axis;
            public double Mid;
            public double Noise;
            public int PeakHigh;
            public int PeakLow;
            public double Trigger;
        }

        private readonly Dictionary<string, Rest> _rest = new Dictionary<string, Rest>();
        private readonly long _baselineEndMs;
        private readonly long _deadlineMs;
        private Latched _latched;

        public AxisCapture(long nowMs)
        {
            _baselineEndMs = nowMs + BaselineMs;
            _deadlineMs = nowMs + DeadlineMs;
        }

        public CapturePhase Phase { get; private set; }

        /// <summary>The device the committed axis belongs to; null until then.</summary>
        public string DeviceId { get; private set; }

        /// <summary>Index of the committed axis within the device's flattened axis list.</summary>
        public int AxisIndex { get; private set; }

        public AxisCalibration Result { get; private set; }

        public bool IsFinished
        {
            get
            {
                return Phase == CapturePhase.Committed
                       || Phase == CapturePhase.Cancelled
                       || Phase == CapturePhase.TimedOut;
            }
        }

        /// <summary>What to tell the user right now. The whole UI of this feature is one line.</summary>
        public string Hint
        {
            get
            {
                switch (Phase)
                {
                    case CapturePhase.Baseline: return "Hands off the pedals...";
                    case CapturePhase.Waiting: return "Press and release the clutch pedal.";
                    case CapturePhase.Tracking: return "Hold it down, then let it come back up.";
                    case CapturePhase.Committed: return "Got it.";
                    case CapturePhase.Cancelled: return "Cancelled.";
                    default: return "Nothing moved. Check the pedals are connected and try again.";
                }
            }
        }

        public void Cancel()
        {
            if (!IsFinished) Phase = CapturePhase.Cancelled;
        }

        /// <summary>
        /// Advances the clock. Phase changes are time-driven rather than event-driven, so a
        /// device that reports nothing at all still leaves the baseline window and still reaches
        /// the deadline instead of hanging the UI forever.
        /// </summary>
        public void Tick(long nowMs)
        {
            if (IsFinished) return;

            if (Phase == CapturePhase.Baseline && nowMs >= _baselineEndMs)
            {
                Phase = CapturePhase.Waiting;
            }

            if (nowMs >= _deadlineMs)
            {
                // Commit whatever travel was measured rather than throwing it away: someone who
                // pressed and held past the deadline still told us everything about that axis.
                AxisCalibration partial = Build();
                if (partial != null) Commit(partial);
                else Phase = CapturePhase.TimedOut;
            }
        }

        /// <summary>One device's axes for this moment. Called once per device per poll.</summary>
        public void Observe(string deviceId, int[] axes, long nowMs)
        {
            if (IsFinished || axes == null || deviceId == null) return;

            Tick(nowMs);
            if (IsFinished) return;

            switch (Phase)
            {
                case CapturePhase.Baseline: LearnRest(deviceId, axes); break;
                case CapturePhase.Waiting: LookForPress(deviceId, axes); break;
                case CapturePhase.Tracking: Track(deviceId, axes); break;
            }
        }

        private Rest RestFor(string deviceId, int[] axes)
        {
            Rest r;
            if (_rest.TryGetValue(deviceId, out r)) return r;

            r = new Rest();
            for (int i = 0; i < axes.Length; i++)
            {
                r.Min[i] = axes[i];
                r.Max[i] = axes[i];
            }
            _rest[deviceId] = r;
            return r;
        }

        private void LearnRest(string deviceId, int[] axes)
        {
            Rest r = RestFor(deviceId, axes);

            for (int i = 0; i < axes.Length; i++)
            {
                int existing;
                if (!r.Min.TryGetValue(i, out existing) || axes[i] < existing) r.Min[i] = axes[i];
                if (!r.Max.TryGetValue(i, out existing) || axes[i] > existing) r.Max[i] = axes[i];
            }
        }

        private void LookForPress(string deviceId, int[] axes)
        {
            // A device that reported nothing through the whole baseline window has no resting
            // state yet - seed one from this first frame, or the very input that broke the
            // silence would be compared against nothing and ignored until the deadline.
            Rest r = RestFor(deviceId, axes);

            for (int i = 0; i < axes.Length; i++)
            {
                int lo, hi;
                if (!r.Min.TryGetValue(i, out lo)) lo = axes[i];
                if (!r.Max.TryGetValue(i, out hi)) hi = axes[i];

                double mid = (lo + hi) / 2.0;
                double noise = hi - lo;
                double trigger = Math.Max(noise * TriggerNoiseMultiple, MinimumTrigger);

                if (Math.Abs(axes[i] - mid) <= trigger) continue;

                _latched = new Latched
                {
                    DeviceId = deviceId,
                    Axis = i,
                    Mid = mid,
                    Noise = noise,
                    PeakHigh = axes[i],
                    PeakLow = axes[i],
                    Trigger = trigger
                };
                Phase = CapturePhase.Tracking;
                return;
            }
        }

        private void Track(string deviceId, int[] axes)
        {
            Latched t = _latched;
            if (t == null || deviceId != t.DeviceId || t.Axis >= axes.Length) return;

            int value = axes[t.Axis];
            if (value > t.PeakHigh) t.PeakHigh = value;
            if (value < t.PeakLow) t.PeakLow = value;

            double returnBand = Math.Max(t.Noise * ReturnNoiseMultiple, MinimumReturnBand);
            double travel = Math.Max(t.PeakHigh - t.Mid, t.Mid - t.PeakLow);

            // Both halves matter: real travel proves a press happened, and the return proves it
            // ended. Either alone commits on noise.
            bool pressed = travel > t.Trigger * PressedTravelMultiple;
            if (pressed && Math.Abs(value - t.Mid) <= returnBand)
            {
                Commit(Build());
            }
        }

        /// <summary>
        /// Turns accumulated travel into a calibration. Direction is whichever side of rest moved
        /// further: a pedal whose axis falls under the foot gets its range reversed and
        /// <see cref="AxisCalibration.Invert"/> set, so the scaled value still climbs 0 -> 100 as
        /// it is pressed.
        /// </summary>
        private AxisCalibration Build()
        {
            Latched t = _latched;
            if (t == null) return null;

            double up = t.PeakHigh - t.Mid;
            double down = t.Mid - t.PeakLow;
            bool ascending = up >= down;

            int rawMin, rawMax;
            if (ascending)
            {
                rawMin = Math.Max(0, (int)Math.Floor(t.Mid - t.Noise));
                rawMax = Math.Max(t.PeakHigh, rawMin + MinimumRange);
            }
            else
            {
                rawMin = Math.Max(0, t.PeakLow);
                rawMax = Math.Max((int)Math.Floor(t.Mid + t.Noise), rawMin + MinimumRange);
            }

            int range = rawMax - rawMin;
            int scaledNoise = range > 0
                ? (int)Math.Floor(t.Noise * AxisCalibration.ScaledMax / range)
                : 0;

            return new AxisCalibration
            {
                RawMin = rawMin,
                RawMax = rawMax,
                DeadzoneLow = Math.Min(scaledNoise * DeadzoneNoiseMultiple, MaximumDeadzone),
                DeadzoneHigh = AxisCalibration.ScaledMax,
                Invert = !ascending
            };
        }

        private void Commit(AxisCalibration calibration)
        {
            if (calibration == null)
            {
                Phase = CapturePhase.TimedOut;
                return;
            }

            DeviceId = _latched.DeviceId;
            AxisIndex = _latched.Axis;
            Result = calibration;
            _latched = null;
            Phase = CapturePhase.Committed;
        }
    }
}
