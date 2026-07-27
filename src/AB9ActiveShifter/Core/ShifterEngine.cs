using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AB9ActiveShifter.Device;
using AB9ActiveShifter.Output;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Owns the force feedback loop. One thread does all DirectInput and vJoy work, so
    /// device access is single-threaded by construction; everything else reads an immutable
    /// snapshot.
    ///
    /// The loop deliberately does not run off SimHub's DataUpdate: that only ticks while a
    /// game is running, and the shifter has to have forces whenever the user's hand is on
    /// the stick.
    /// </summary>
    public sealed class ShifterEngine : IDisposable
    {
        private const int WatchdogPeriodMs = 500;
        private const int WatchdogStaleMs = 1000;
        private const int SnapshotEveryTicks = 8;

        private static readonly int[] BackoffMs = { 1000, 2000, 5000 };

        private readonly object _deviceLock = new object();

        private Thread _thread;
        private volatile bool _running;
        private volatile EnginePhase _phase = EnginePhase.Stopped;
        private volatile EngineConfig _config = new EngineConfig();
        private volatile bool _configDirty = true;
        private volatile EngineSnapshot _snapshot = new EngineSnapshot();
        private volatile string _status = "Stopped";

        private EngineConfig _activeConfig;
        private GateGeometry _geometry;
        private GateStateMachine _stateMachine;
        private SequentialStateMachine _seqMachine;
        private ForceComposer _composer;

        // Sequential pulse bookkeeping, engine thread only. A pending press exists so that
        // re-firing the same button leaves a gap a 60 Hz game poll can actually see - an
        // off-and-on inside one tick reads as one long press.
        //
        // The pulse buttons sit ABOVE every gear button (the highest is 8, 7+R's reverse) so
        // no game binding can mean two things. On buttons 1/2 they collided with 1st and 2nd
        // gear: a game still carrying H-pattern bindings would read an upshift pulse as
        // "engage 1st", at any speed.
        private const int SeqUpButton = 9;
        private const int SeqDownButton = 10;
        private const int SeqRefireGapMs = 20;
        private int _pulseButton;
        private int _pulsePending;
        private long _pulseOffAtMs;
        private long _pulseOnAtMs;
        private ShiftDir _seqPushed = ShiftDir.None;

        private FfbDevice _device;
        private EffectSet _effects;
        private VJoyGearOutput _output;

        // Telemetry effects. The composer keeps carrier phases and lives on the engine
        // thread; the snapshot is written by SimHub's data thread and read here, whole.
        private readonly EffectComposer _gameEffects = new EffectComposer();
        private volatile TelemetryState _telemetry = TelemetryState.Inactive;

        private Timer _watchdog;
        private long _lastTickStamp;

        private int _calibrationRequest;
        private PolarityCalibrator _calibrator;
        private readonly Queue<CalibrationTarget> _calibrationQueue = new Queue<CalibrationTarget>();

        /// <summary>Raised on the engine thread when the selected gear changes (new, previous).</summary>
        public event Action<int, int> GearChanged;

        /// <summary>Raised on the engine thread as each calibration target finishes.</summary>
        public event Action<CalibrationResult> CalibrationCompleted;

        /// <summary>Raised on the engine thread when the whole calibration run ends.</summary>
        public event Action CalibrationFinished;

        public EngineSnapshot Snapshot { get { return _snapshot; } }

        /// <summary>
        /// Records every tick's inputs and outputs, for replaying a movement the hand complained
        /// about instead of reasoning about what it might have been.
        /// </summary>
        public TraceRecorder Trace { get { return _trace; } }

        private readonly TraceRecorder _trace = new TraceRecorder();

        /// <summary>Where traces are written. Documents, so it is writable and easy to find.</summary>
        public string TraceDirectory
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AB9ActiveShifter");
            }
        }

        /// <summary>Saves whatever has been recorded. Call off the engine thread.</summary>
        public string SaveTrace(string note)
        {
            _trace.Stop();
            EngineConfig cfg = _activeConfig ?? _config;
            return _trace.Save(TraceDirectory, cfg, note);
        }

        public bool IsRunning { get { return _running; } }

        public EngineConfig Config { get { return _config; } }

        public void ApplyConfig(EngineConfig config)
        {
            if (config == null) return;
            _config = config;
            _configDirty = true;
        }

        /// <summary>
        /// Hands the engine the latest game telemetry. Called from SimHub's data thread; the
        /// tick reads whichever snapshot is current and judges freshness from its capture
        /// stamp, so a game that stops updating cannot leave an effect running.
        /// </summary>
        public void SetTelemetry(TelemetryState telemetry)
        {
            if (telemetry != null) _telemetry = telemetry;
        }

        public void Start()
        {
            lock (_deviceLock)
            {
                if (_running) return;
                _running = true;
                _phase = EnginePhase.SearchDevice;
                _status = "Starting";

                _lastTickStamp = Stopwatch.GetTimestamp();
                _watchdog = new Timer(WatchdogTick, null, WatchdogPeriodMs, WatchdogPeriodMs);

                _thread = new Thread(RunLoop)
                {
                    Name = "AB9ShifterFFB",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _thread.Start();
            }

            Log.Info("FFB engine started.");
        }

        public void Stop(TimeSpan timeout)
        {
            Thread thread;
            lock (_deviceLock)
            {
                if (!_running && _thread == null)
                {
                    Teardown();
                    return;
                }

                _running = false;
                thread = _thread;
                _thread = null;
            }

            if (thread != null && thread.IsAlive && !thread.Join(timeout))
            {
                Log.Warn("FFB thread did not exit within " + timeout.TotalSeconds + "s; forcing teardown.");
            }

            lock (_deviceLock)
            {
                Teardown();
            }

            DisposeWatchdog();
            _phase = EnginePhase.Stopped;
            _status = "Stopped";
            PublishSnapshot(GateGeometry.AxisCenter, GateGeometry.AxisCenter, 0);
            Log.Info("FFB engine stopped.");
        }

        /// <summary>
        /// Asks the engine to measure effect polarity on the next tick. Both effect families are
        /// probed in turn; results arrive on <see cref="CalibrationCompleted"/>.
        /// </summary>
        public void RequestCalibration()
        {
            Interlocked.Exchange(ref _calibrationRequest, 1);
        }

        public bool IsCalibrating { get { return _calibrator != null; } }

        /// <summary>
        /// Kills output now. Called by the watchdog and at process exit. Buttons are always
        /// cleared first; the device teardown is best-effort because the loop may be wedged
        /// inside a driver call, in which case the lock will not be free.
        /// </summary>
        public void EmergencyStop(string reason)
        {
            Log.Error("Emergency stop: " + reason);
            _running = false;
            _phase = EnginePhase.Faulted;
            _status = "Stopped for safety: " + reason;

            try { if (_output != null) _output.ReleaseAll(); }
            catch { }

            if (Monitor.TryEnter(_deviceLock, 500))
            {
                try
                {
                    if (_effects != null) _effects.StopAll();
                    if (_device != null) _device.StopForces();
                }
                catch { }
                finally { Monitor.Exit(_deviceLock); }
            }
            else
            {
                Log.Error("Emergency stop could not take the device lock; buttons cleared, forces may persist " +
                          "until the device is released.");
            }
        }

        private void RunLoop()
        {
            NativeMethods.TimeBeginPeriod(1);
            _paceTimer = NativeMethods.TryCreateHighResolutionTimer();
            if (_paceTimer == null)
            {
                Log.Info("High-resolution timer unavailable; pacing with sleep and spin.");
            }

            var clock = Stopwatch.StartNew();

            long nextOpenAttemptMs = 0;
            int backoffIndex = 0;
            long tickCount = 0;
            long hzWindowStartMs = 0;
            int hzTicks = 0;
            double loopHz = 0;

            long periodTicks = Stopwatch.Frequency / Math.Max(1, _config.TickHz);
            long nextDue = Stopwatch.GetTimestamp() + periodTicks;

            try
            {
                while (_running)
                {
                    Volatile.Write(ref _lastTickStamp, Stopwatch.GetTimestamp());
                    long nowMs = clock.ElapsedMilliseconds;

                    if (_configDirty)
                    {
                        _configDirty = false;
                        ApplyConfigChange(_config);
                        periodTicks = Stopwatch.Frequency / Math.Max(1, _config.TickHz);
                    }

                    EngineConfig cfg = _activeConfig;

                    if (_phase != EnginePhase.Run)
                    {
                        if (nowMs >= nextOpenAttemptMs)
                        {
                            if (TryOpenDevice(cfg))
                            {
                                backoffIndex = 0;
                            }
                            else
                            {
                                int wait = BackoffMs[Math.Min(backoffIndex, BackoffMs.Length - 1)];
                                backoffIndex++;
                                nextOpenAttemptMs = nowMs + wait;
                            }
                        }

                        PublishSnapshot(GateGeometry.AxisCenter, GateGeometry.AxisCenter, 0);

                        // Nothing to drive while disconnected; do not burn a core spinning.
                        Thread.Sleep(25);
                        continue;
                    }

                    Tick(cfg, nowMs, tickCount, loopHz);

                    tickCount++;
                    hzTicks++;
                    if (nowMs - hzWindowStartMs >= 1000)
                    {
                        loopHz = hzTicks * 1000.0 / Math.Max(1, nowMs - hzWindowStartMs);
                        hzWindowStartMs = nowMs;
                        hzTicks = 0;
                    }

                    nextDue = PaceTick(nextDue, periodTicks);
                }
            }
            catch (Exception ex)
            {
                Log.Error("FFB loop terminated unexpectedly", ex);
                _status = "FFB loop crashed: " + ex.Message;
                _phase = EnginePhase.Faulted;
            }
            finally
            {
                lock (_deviceLock)
                {
                    Teardown();
                }

                if (_paceTimer != null)
                {
                    _paceTimer.Dispose();
                    _paceTimer = null;
                }

                NativeMethods.TimeEndPeriod(1);
                _running = false;
            }
        }

        private Microsoft.Win32.SafeHandles.SafeWaitHandle _paceTimer;

        private long PaceTick(long nextDue, long periodTicks)
        {
            while (true)
            {
                long remaining = nextDue - Stopwatch.GetTimestamp();
                if (remaining <= 0) break;

                long remainingUs = remaining * 1000000 / Stopwatch.Frequency;

                // The high-resolution timer sleeps fractions of a millisecond without spinning,
                // which is what makes a 1 kHz tick affordable; the sleep-and-spin path is the
                // fallback for old Windows builds.
                if (_paceTimer != null && remainingUs > 80)
                {
                    if (!NativeMethods.WaitMicroseconds(_paceTimer, remainingUs - 50))
                    {
                        _paceTimer.Dispose();
                        _paceTimer = null;
                        Log.Warn("High-resolution timer failed; pacing with sleep and spin.");
                    }
                    continue;
                }

                if (remainingUs > 1500) Thread.Sleep(1);
                else Thread.SpinWait(64);
            }

            long due = nextDue + periodTicks;
            long now = Stopwatch.GetTimestamp();

            // If we fell badly behind (device stall, thread pre-empted), resynchronise
            // instead of trying to catch up with a burst of ticks.
            if (due < now) due = now + periodTicks;
            return due;
        }

        /// <summary>
        /// Velocity estimation lives in its own Core class because it turned out to have a
        /// hardware failure mode worth unit-testing: adjacent-tick differencing aliases the
        /// device's ~500 Hz position refresh into a 2:1 sawtooth, which the yield rendered as
        /// a grinding texture. See <see cref="VelocityEstimator"/> for the measurement.
        /// </summary>
        private readonly VelocityEstimator _velocity = new VelocityEstimator();

        /// <summary>Forgets the current motion estimate, so a jump in position is not read as speed.</summary>
        private void ResetVelocity()
        {
            _velocity.Reset();
            _composeStamp = 0;
        }

        private long _composeStamp;

        /// <summary>
        /// Milliseconds since the previous force composition, for the attack shaping. The
        /// first tick after a reset reports the nominal period, so forces wind up softly from
        /// zero on enable. Clamped so a stalled tick cannot dump a whole attack at once.
        /// </summary>
        private double ComposeDelta(EngineConfig cfg)
        {
            long now = Stopwatch.GetTimestamp();
            long prev = _composeStamp;
            _composeStamp = now;

            if (prev == 0) return 1000.0 / Math.Max(1, cfg.TickHz);

            double dtMs = (now - prev) * 1000.0 / Stopwatch.Frequency;
            if (dtMs < 0.05) dtMs = 0.05;
            if (dtMs > 5.0) dtMs = 5.0;
            return dtMs;
        }

        private void UpdateVelocity(int x, int y)
        {
            _velocity.Update(x, y, Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        }

        private void Tick(EngineConfig cfg, long nowMs, long tickCount, double loopHz)
        {
            lock (_deviceLock)
            {
                if (_device == null || _effects == null) return;

                int rawX, rawY;
                string error;
                if (!_device.TryPoll(out rawX, out rawY, out error))
                {
                    HandleDeviceLoss(error);
                    return;
                }

                // Positions stay in the device's own coordinates throughout. Spring anchors are
                // sent back to the device in those coordinates, so mirroring the readings here
                // would put every anchor on the wrong side of the gate. Layout preference is
                // applied to the gear map instead.
                int x = rawX;
                int y = rawY;

                if (HandleCalibration(cfg, nowMs, x, y, loopHz)) return;

                UpdateVelocity(x, y);
                double dtMs = ComposeDelta(cfg);

                // The telemetry effects run on whatever snapshot is current; its age is what
                // silences them when the game pauses or goes away.
                TelemetryState telemetry = _telemetry;
                int telemetryAge = unchecked(Environment.TickCount - telemetry.CapturedAtTick);

                ForceFrame frame;
                GateState traceState;
                Column traceColumn;
                ShiftDir traceDir;
                int traceGear;

                bool gearChanged = false;

                if (cfg.Pattern == GatePattern.Sequential)
                {
                    // No grind in sequential - clutchless shifting is what a dog box is for -
                    // so the effects only contribute vibration here.
                    EffectOutput fx = _gameEffects.Step(cfg, telemetry, telemetryAge, dtMs, false);

                    SeqTransition st = _seqMachine.Update(y);
                    gearChanged = st.Shift != 0 || _seqPushed != st.Pushed;
                    _seqPushed = st.Pushed;

                    // Buttons before forces, pulses included: the game sees the shift at
                    // least as early as the hand feels the click.
                    StepPulse(cfg, nowMs, st.Shift);

                    frame = _composer.ComposeSequential(x, y, _velocity.X, _velocity.Y, dtMs, fx.VibY);

                    traceState = GateState.Neutral;
                    traceColumn = Column.None;
                    traceDir = st.Pushed;
                    traceGear = 0;
                }
                else
                {
                    // The grind wants to know whether the lever is pushing into a slot, which
                    // is read off the state machine BEFORE this tick's update - last tick's
                    // fact, one millisecond old - so that this tick's engage decision can
                    // depend on the answer. Depth feeds the grind's loudness: forcing the
                    // lever against the balk presses the teeth together harder.
                    bool approaching = _stateMachine.State == GateState.Traveling;
                    double slotDepth = approaching
                        ? _geometry.EngageFraction(_stateMachine.Direction, y)
                        : 0.0;
                    EffectOutput fx = _gameEffects.Step(
                        cfg, telemetry, telemetryAge, dtMs, approaching, slotDepth);

                    StateTransition t = _stateMachine.Update(x, y, !fx.BlockEngage);
                    gearChanged = t.GearChanged;

                    if (t.GearChanged)
                    {
                        // Buttons before forces: a game must see the gear change at least as
                        // early as the hand feels it, never later.
                        if (_output != null) _output.SetGear(t.Gear);

                        Action<int, int> handler = GearChanged;
                        if (handler != null)
                        {
                            try { handler(t.Gear, t.PreviousGear); }
                            catch (Exception ex) { Log.ErrorThrottled("gear-event", "Gear change handler threw", ex); }
                        }
                    }

                    frame = _composer.Compose(
                        t.State, t.Column, t.Direction, x, y, _velocity.X, _velocity.Y, dtMs,
                        fx.VibY, fx.MuteDetent);

                    traceState = t.State;
                    traceColumn = t.Column;
                    traceDir = t.Direction;
                    traceGear = t.Gear;
                }

                _effects.Apply(frame, nowMs);

                // After Apply, so what is recorded is what was actually sent.
                _trace.Add(nowMs, x, y, _velocity.X, _velocity.Y, dtMs,
                           traceState, traceColumn, traceDir, traceGear, frame.ConstantX, frame.ConstantY);

                if (_effects.IsFaulted)
                {
                    HandleDeviceLoss("Effect updates failed repeatedly; reopening the device.");
                    return;
                }

                if (tickCount % SnapshotEveryTicks == 0 || gearChanged)
                {
                    PublishSnapshot(x, y, loopHz);
                }
            }
        }

        /// <summary>
        /// Drives polarity measurement instead of the gate while calibration is running. Returns
        /// true when the tick was consumed.
        ///
        /// The calibrator's frames are sent to the device exactly as given, with no invert flags
        /// and no gain scaling applied. It is measuring the raw behaviour those settings exist to
        /// correct, so passing them through the composer would hide the very thing being measured.
        /// </summary>
        private bool HandleCalibration(EngineConfig cfg, long nowMs, int x, int y, double loopHz)
        {
            if (Interlocked.Exchange(ref _calibrationRequest, 0) == 1)
            {
                _calibrationQueue.Clear();
                _calibrationQueue.Enqueue(CalibrationTarget.ConstantX);
                _calibrationQueue.Enqueue(CalibrationTarget.ConstantY);
                _calibrationQueue.Enqueue(CalibrationTarget.SpringX);
                _calibrationQueue.Enqueue(CalibrationTarget.SpringY);
                _calibrator = null;

                // Drop any held gear or pulse: the probes move the stick, and a button left
                // down through that would look to a game like a shift the user never made.
                if (_output != null)
                {
                    _output.SetGear(0);
                    if (_pulseButton != 0) _output.SetButton(_pulseButton, false);
                }
                _pulseButton = 0;
                _pulsePending = 0;
                _stateMachine.Resync(x, y);
                _seqMachine.Resync(y);

                Log.Info("Polarity calibration requested (probe force " + cfg.CalibrationForcePct + "%).");
            }

            if (_calibrator == null && _calibrationQueue.Count == 0) return false;

            if (_calibrator == null)
            {
                int magnitude = (int)Math.Round(
                    GateGeometry.ForceMax * GateGeometry.Clamp(cfg.CalibrationForcePct, 1, 60) / 100.0);
                _calibrator = new PolarityCalibrator(_calibrationQueue.Dequeue(), magnitude);
            }

            ForceFrame frame = _calibrator.Step(x, y, nowMs);
            _effects.Apply(frame, nowMs);

            _status = _calibrator.StatusText;

            if (_calibrator.IsComplete)
            {
                CalibrationResult result = _calibrator.Result;
                _calibrator = null;

                if (result != null)
                {
                    Log.Info("Calibration: " + result.Message);
                    _status = result.Message;
                    RaiseCalibrationCompleted(result);
                }

                if (_calibrationQueue.Count == 0)
                {
                    // Back to a known state before the gate takes over again. In sequential
                    // the resync arms without firing, and no gear button is ever held.
                    _effects.Apply(ForceComposer.FreeFrame(), nowMs);

                    _stateMachine.Resync(x, y);
                    _seqMachine.Resync(y);
                    if (_output != null && cfg.Pattern != GatePattern.Sequential)
                    {
                        _output.SetGear(_stateMachine.CurrentGear);
                    }

                    RaiseCalibrationFinished();
                }
            }

            PublishSnapshot(x, y, loopHz);
            return true;
        }

        private void RaiseCalibrationCompleted(CalibrationResult result)
        {
            Action<CalibrationResult> handler = CalibrationCompleted;
            if (handler == null) return;
            try { handler(result); }
            catch (Exception ex) { Log.Error("Calibration result handler threw", ex); }
        }

        private void RaiseCalibrationFinished()
        {
            Action handler = CalibrationFinished;
            if (handler == null) return;
            try { handler(); }
            catch (Exception ex) { Log.Error("Calibration finished handler threw", ex); }
        }

        /// <summary>Abandons any calibration in progress and returns to the gate.</summary>
        public void CancelCalibration()
        {
            Interlocked.Exchange(ref _calibrationRequest, 0);
            lock (_deviceLock)
            {
                _calibrationQueue.Clear();
                _calibrator = null;
            }
        }

        private bool TryOpenDevice(EngineConfig cfg)
        {
            lock (_deviceLock)
            {
                Teardown();

                _phase = EnginePhase.OpenDevice;

                IntPtr hwnd = WindowHandleProvider.Get();
                if (hwnd == IntPtr.Zero)
                {
                    _status = "Waiting for a SimHub window to attach DirectInput to.";
                    _phase = EnginePhase.SearchDevice;
                    return false;
                }

                var device = new FfbDevice();
                string error;
                if (!device.Open(cfg, hwnd, out error))
                {
                    device.Dispose();
                    _status = error;
                    Log.WarnThrottled("device-open", error, 15);
                    _phase = EnginePhase.SearchDevice;
                    return false;
                }

                EffectSet effects = device.CreateEffects(_composer.DamperCoefficient, out error);
                if (effects == null)
                {
                    device.Dispose();
                    _status = error;
                    Log.WarnThrottled("effect-create", error, 15);
                    _phase = EnginePhase.SearchDevice;
                    return false;
                }

                _device = device;
                _effects = effects;

                if (_output == null) _output = new VJoyGearOutput(cfg.VJoyDeviceId);
                if (!_output.Connect())
                {
                    // Forces are still useful without vJoy, so keep running and say why.
                    Log.WarnThrottled("vjoy-connect", _output.LastError ?? "vJoy unavailable", 15);
                }

                int x, y;
                string pollError;
                if (_device.TryPoll(out x, out y, out pollError))
                {
                    _stateMachine.Resync(x, y);
                    _seqMachine.Resync(y);
                }

                Log.ResetThrottle("device-open");
                _phase = EnginePhase.Run;
                _status = BuildReadyStatus(cfg);
                return true;
            }
        }

        private string BuildReadyStatus(EngineConfig cfg)
        {
            string deviceName = _device != null ? _device.ProductName : "device";
            string vjoy = _output != null && _output.IsConnected
                ? "vJoy " + cfg.VJoyDeviceId
                : "no vJoy (" + (_output != null ? _output.LastError : "not connected") + ")";

            string gain = cfg.PolarityConfirmed
                ? cfg.OverallGainPct + "% gain"
                : EngineConfig.UnconfirmedGainCapPct + "% gain (capped until polarity is confirmed)";

            return "Running on " + deviceName + ", " + vjoy + ", " + gain + ".";
        }

        private void HandleDeviceLoss(string reason)
        {
            Log.WarnThrottled("device-loss", "Device lost: " + reason, 10);
            _status = reason;

            if (_output != null) _output.ReleaseAll();

            DisposeEffects();
            DisposeDevice();

            _phase = EnginePhase.SearchDevice;
        }

        private void ApplyConfigChange(EngineConfig cfg)
        {
            EngineConfig previous = _activeConfig;
            _activeConfig = cfg;

            _geometry = cfg.BuildGeometry();
            _composer = new ForceComposer(_geometry, cfg);

            // Force changes are picked up by rebuilding the composer alone. Only rebuild the
            // state machine when the gate itself moved, so dragging a force slider cannot
            // disturb a gear that is currently held.
            if (_stateMachine == null || !GeometryUnchanged(previous, cfg))
            {
                _stateMachine = new GateStateMachine(_geometry, cfg.MinEngageTicks);
                _seqMachine = new SequentialStateMachine(_geometry, cfg.MinEngageTicks);

                int x, y;
                string error;
                if (_device != null && _device.TryPoll(out x, out y, out error))
                {
                    _stateMachine.Resync(x, y);
                    _seqMachine.Resync(y);
                }

                // The rebuilt machine may disagree with what is currently held - new geometry
                // can put the stick outside the gear it was in. Push the truth to vJoy now,
                // or the old button would stay down with nothing left to release it. A pulse
                // in flight is cleared the same way, or switching pattern mid-press would
                // leave an up/down button held as a phantom gear.
                if (_output != null)
                {
                    if (_pulseButton != 0) _output.SetButton(_pulseButton, false);
                    _output.SetGear(cfg.Pattern == GatePattern.Sequential ? 0 : _stateMachine.CurrentGear);
                }

                _pulseButton = 0;
                _pulsePending = 0;
                _seqPushed = ShiftDir.None;
            }

            // Only a change of identity needs a reopen. Forces, damping and geometry are all
            // applied on the next tick, so the sliders stay live.
            bool needsReopen = previous != null &&
                               (previous.VendorId != cfg.VendorId ||
                                previous.ProductId != cfg.ProductId ||
                                previous.VJoyDeviceId != cfg.VJoyDeviceId);

            if (needsReopen && _phase == EnginePhase.Run)
            {
                Log.Info("Configuration change needs a device reopen.");
                if (previous.VJoyDeviceId != cfg.VJoyDeviceId && _output != null)
                {
                    _output.Disconnect();
                    _output = null;
                }

                DisposeEffects();
                DisposeDevice();
                _phase = EnginePhase.SearchDevice;
            }
        }

        /// <summary>
        /// Runs the sequential button pulse. A fresh shift presses its button and schedules the
        /// release; re-firing a button that is still down releases it first and delays the next
        /// press by a gap long enough for a game's input poll to observe, because an off-and-on
        /// inside one millisecond reads as one continuous press.
        /// </summary>
        private void StepPulse(EngineConfig cfg, long nowMs, int shift)
        {
            if (_output == null) return;

            int hold = Math.Max(SeqRefireGapMs, cfg.SeqPulseMs);

            if (shift != 0)
            {
                int button = shift > 0 ? SeqUpButton : SeqDownButton;

                if (_pulseButton == button)
                {
                    _output.SetButton(button, false);
                    _pulseButton = 0;
                    _pulsePending = button;
                    _pulseOnAtMs = nowMs + SeqRefireGapMs;
                }
                else
                {
                    if (_pulseButton != 0) _output.SetButton(_pulseButton, false);
                    _pulseButton = button;
                    _pulsePending = 0;
                    _pulseOffAtMs = nowMs + hold;
                    _output.SetButton(button, true);
                }
                return;
            }

            if (_pulsePending != 0 && nowMs >= _pulseOnAtMs)
            {
                _pulseButton = _pulsePending;
                _pulsePending = 0;
                _pulseOffAtMs = nowMs + hold;
                _output.SetButton(_pulseButton, true);
            }
            else if (_pulseButton != 0 && nowMs >= _pulseOffAtMs)
            {
                _output.SetButton(_pulseButton, false);
                _pulseButton = 0;
            }
        }

        /// <summary>True when nothing that changes where the gears are has moved.</summary>
        private static bool GeometryUnchanged(EngineConfig a, EngineConfig b)
        {
            if (a == null || b == null) return false;

            return a.Pattern == b.Pattern
                && a.ChannelHalfEnter == b.ChannelHalfEnter
                && a.ChannelHalfExit == b.ChannelHalfExit
                && a.ColumnEdgeEnter == b.ColumnEdgeEnter
                && a.ColumnEdgeExit == b.ColumnEdgeExit
                && a.ColumnInnerHalfEnter == b.ColumnInnerHalfEnter
                && a.ColumnInnerHalfExit == b.ColumnInnerHalfExit
                && a.EngageDepth == b.EngageDepth
                && a.ReleaseDepth == b.ReleaseDepth
                && a.LockoutHalfWidth == b.LockoutHalfWidth
                && a.DetentHysteresis == b.DetentHysteresis
                && a.MinEngageTicks == b.MinEngageTicks
                && a.MirrorColumns == b.MirrorColumns
                && a.MirrorSlots == b.MirrorSlots;
        }

        private void PublishSnapshot(int x, int y, double loopHz)
        {
            GateStateMachine sm = _stateMachine;
            GateGeometry geo = _geometry;
            EngineConfig cfg = _activeConfig;
            bool sequential = cfg != null && cfg.Pattern == GatePattern.Sequential;

            string label;
            if (sequential)
            {
                label = _seqPushed == ShiftDir.Fwd ? "+" : (_seqPushed == ShiftDir.Back ? "-" : "N");
            }
            else
            {
                int gear = sm != null ? sm.CurrentGear : 0;
                label = geo != null ? geo.LabelFor(gear) : "N";
            }

            var snapshot = new EngineSnapshot
            {
                Phase = _phase,
                DeviceConnected = _device != null && _device.IsOpen,
                VJoyConnected = _output != null && _output.IsConnected,
                RawX = x,
                RawY = y,
                X = x,
                Y = y,
                State = !sequential && sm != null ? sm.State : GateState.Neutral,
                Column = !sequential && sm != null ? sm.Column : Column.None,
                Gear = !sequential && sm != null ? sm.CurrentGear : 0,
                GearLabel = label,
                LoopHz = loopHz,
                StatusMessage = _status,
                DeviceName = _device != null ? (_device.ProductName ?? "") : ""
            };

            _snapshot = snapshot;
        }

        private void WatchdogTick(object state)
        {
            if (_phase != EnginePhase.Run) return;

            long last = Volatile.Read(ref _lastTickStamp);
            double staleMs = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
            if (staleMs > WatchdogStaleMs)
            {
                EmergencyStop("the FFB loop stopped responding for " + (int)staleMs + " ms");
            }
        }

        /// <summary>Must be called under <see cref="_deviceLock"/>.</summary>
        private void Teardown()
        {
            // Order matters and is the same on every path: buttons, then forces, then the
            // device handle. Releasing the device first could leave a gear button stuck.
            if (_output != null) _output.ReleaseAll();
            DisposeEffects();
            DisposeDevice();
        }

        private void DisposeEffects()
        {
            if (_effects == null) return;
            try { _effects.Dispose(); }
            catch (Exception ex) { Log.Error("Effect disposal failed", ex); }
            finally { _effects = null; }
        }

        private void DisposeDevice()
        {
            if (_device == null) return;
            try { _device.Dispose(); }
            catch (Exception ex) { Log.Error("Device disposal failed", ex); }
            finally { _device = null; }
        }

        private void DisposeWatchdog()
        {
            Timer w = _watchdog;
            _watchdog = null;
            if (w == null) return;
            try { w.Dispose(); }
            catch { }
        }

        public void Dispose()
        {
            Stop(TimeSpan.FromSeconds(2));

            if (_output != null)
            {
                _output.Disconnect();
                _output = null;
            }

            DisposeWatchdog();
        }
    }
}
