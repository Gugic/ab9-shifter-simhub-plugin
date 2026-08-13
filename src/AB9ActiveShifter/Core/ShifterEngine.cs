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
        private PrndStateMachine _prndMachine;
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

        // The hard lockout's released flag: a level set from SimHub's action thread and read
        // once per tick, the _running/_freeStick shape rather than an Interlocked request.
        // Deliberately runtime state and not a setting - persisting it would fork a preset on
        // a keypress, churn the debounced save on every press, and stamp a stale answer back
        // on the next profile activation. It defaults to engaged and re-engages on every
        // start and every gate-moving config change.
        private volatile bool _lockoutReleased;

        // Auto re-arm bookkeeping, engine thread only: which side of the gate the lever held
        // when the release was granted, captured from the composer's side latch.
        private int _lockoutReleaseSide;

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

        // --- the clutch pedal, when it is read directly rather than from the game -------------
        //
        // A second DirectInput handle, held NON-exclusively so the game keeps its own pedals.
        // Polled well below the loop rate: a pedal moves at the speed of an ankle, and every
        // poll is time taken out of a one-millisecond budget that the gate needs more.
        private const int PedalPollEveryTicks = 10;

        private readonly PedalDevice _pedals = new PedalDevice();
        private readonly int[] _pedalAxes = new int[PedalDevice.AxisCount];
        private readonly TelemetryState _pedalTelemetry = new TelemetryState();
        private bool _pedalsOpen;
        private string _pedalDeviceOpened;

        /// <summary>
        /// How often a failing pedal open may be retried. The same schedule the base's own
        /// reconnect uses, and for the same reason: a device that is not there costs milliseconds
        /// to fail to open, and this runs on the force loop. Without it, an unplugged pedal set
        /// paced the whole gate - 81 Hz against 990.
        /// </summary>
        private readonly RetryBackoff _pedalOpenRetry = new RetryBackoff(BackoffMs);

        /// <summary>Which device id the backoff above is holding off on.</summary>
        private string _pedalOpenFor;

        /// <summary>Last raw reading of the bound axis, for the calibration bar in the UI.</summary>
        private volatile int _pedalRaw;

        /// <summary>Last scaled reading, 0..100, whether or not it is the clutch in use.</summary>
        private double _pedalPercent;

        private int _pedalCaptureRequest;
        private AxisCapture _pedalCapture;
        private volatile string _captureHint;

        /// <summary>Last raw value of the bound pedal axis, for the calibration bar.</summary>
        public int PedalRaw { get { return _pedalRaw; } }

        /// <summary>Last scaled pedal reading, 0..100, whatever the clutch source is.</summary>
        public double PedalPercent { get { return _pedalPercent; } }

        /// <summary>What the capture wants the user to do right now; null when not capturing.</summary>
        public string PedalCaptureHint { get { return _pedalCapture != null ? _captureHint : null; } }

        /// <summary>Raised on the engine thread when a pedal capture finishes, however it ended.</summary>
        public event Action<AxisCapture> PedalCaptureCompleted;

        /// <summary>
        /// Asks the engine to listen for a clutch pedal. The pedal device is opened for the
        /// duration even if the clutch source is still the game's telemetry, so the axis can be
        /// bound before the source is switched over.
        /// </summary>
        public void RequestPedalCapture()
        {
            Interlocked.Exchange(ref _pedalCaptureRequest, 1);
        }

        /// <summary>Abandons a capture in progress. Safe to call when none is running.</summary>
        public void CancelPedalCapture()
        {
            Interlocked.Exchange(ref _pedalCaptureRequest, 0);
            AxisCapture capture = _pedalCapture;
            if (capture != null) capture.Cancel();
        }

        /// <summary>Raised on the engine thread when the selected gear changes (new, previous).</summary>
        public event Action<int, int> GearChanged;

        /// <summary>
        /// Raised whenever the hard lockout's engaged state flips (true = engaged), from
        /// whichever thread flipped it - an action, the auto re-arm, or a config change.
        /// </summary>
        public event Action<bool> LockoutEngagedChanged;

        /// <summary>
        /// Whether a hard-mode lockout is currently armed. True whenever no hard mode is
        /// configured, so a dashboard can key on this alone.
        /// </summary>
        public bool LockoutEngaged { get { return !_lockoutReleased; } }

        /// <summary>
        /// Opens or closes a hard-mode lockout. Safe from any thread; a no-op when the state
        /// already matches, so a two-position switch bound to the explicit actions cannot
        /// double-fire the event.
        /// </summary>
        public void SetLockoutReleased(bool released)
        {
            if (_lockoutReleased == released) return;
            _lockoutReleased = released;

            Action<bool> handler = LockoutEngagedChanged;
            if (handler != null)
            {
                try { handler(!released); }
                catch (Exception ex) { Log.ErrorThrottled("lockout-event", "Lockout state handler threw", ex); }
            }
        }

        /// <summary>Flips the hard lockout's state - the one-key binding.</summary>
        public void ToggleLockoutRelease()
        {
            SetLockoutReleased(!_lockoutReleased);
        }

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

                // A hard gate arrives locked on every start; the hotkey is the only key.
                SetLockoutReleased(false);
                _lockoutReleaseSide = 0;

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
                        ApplyConfigChange(_config, nowMs);
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

                    WatchForceOutput(nowMs);

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

                NotePosition(rawX, rawY, nowMs);

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

                telemetry = ReadPedals(cfg, telemetry, tickCount, nowMs);

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

                    frame = _composer.ComposeSequential(x, y, _velocity.X, _velocity.Y, dtMs, fx.VibY,
                                                        st.Shift != 0);

                    traceState = GateState.Neutral;
                    traceColumn = Column.None;
                    traceDir = st.Pushed;
                    traceGear = 0;
                }
                else if (cfg.Pattern == GatePattern.Prnd)
                {
                    // No grind: there is no clutch and no synchro to balk on a selector, and
                    // nothing here that a refused engagement would even mean.
                    EffectOutput fx = _gameEffects.Step(cfg, telemetry, telemetryAge, dtMs, false);

                    StateTransition t = _prndMachine.Update(y);
                    gearChanged = t.GearChanged;

                    if (t.GearChanged)
                    {
                        // Buttons before forces, as everywhere. SetGear carries the position
                        // buttons too, so the release-before-press that stops a game seeing two
                        // positions at once is the same code that stops it seeing two gears.
                        if (_output != null) _output.SetGear(t.Gear);

                        Action<int, int> handler = GearChanged;
                        if (handler != null)
                        {
                            try { handler(t.Gear, t.PreviousGear); }
                            catch (Exception ex) { Log.ErrorThrottled("gear-event", "Gear change handler threw", ex); }
                        }
                    }

                    frame = _composer.ComposePrnd(x, y, _velocity.X, _velocity.Y, dtMs, fx.VibY);

                    traceState = t.State;
                    traceColumn = Column.None;
                    traceDir = ShiftDir.None;
                    traceGear = t.Gear;
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

                    // The hard lockout's refusal rides the same allowEngage the grind uses, and
                    // reads the machine BEFORE this tick's update - last tick's target, the
                    // grind's own one-millisecond-stale pattern. It can only ever block a new
                    // latch; a gear already held stays held whatever the gate does.
                    bool lockoutReleased = _lockoutReleased;
                    bool refused = _composer.LockoutRefusesEngage(_stateMachine.Column, _stateMachine.Direction);

                    StateTransition t = _stateMachine.Update(x, y, !(fx.BlockEngage || refused));
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
                        fx.VibY, fx.MuteDetent, lockoutReleased);

                    if (cfg.LockoutMode == LockoutMode.HotkeyAutoRearm && lockoutReleased)
                    {
                        StepAutoRearm(cfg, t);
                    }
                    else if (!lockoutReleased)
                    {
                        _lockoutReleaseSide = 0;
                    }

                    traceState = t.State;
                    traceColumn = t.Column;
                    traceDir = t.Direction;
                    traceGear = t.Gear;
                }

                // Between one gate and the next: nothing at all while the lever comes home, then
                // the new gate wound in rather than switched on, then the profile number pulsed
                // out. Applied here, to the finished frame, so it scales whatever the gate and
                // the carriers agreed on and cannot be confused with a force of its own.
                if (_transition.Active)
                {
                    _transition.Step(nowMs, x, y);
                    frame = ScaleFrame(frame, _transition.ForceScale);

                    if (_transition.PulseEnvelope > 0)
                    {
                        frame.ConstantY += ConfirmPulse(cfg, dtMs, _transition.PulseEnvelope);
                    }
                }

                _effects.Apply(frame, nowMs);

                // After Apply, so what is recorded is what was actually sent.
                _trace.Add(nowMs, x, y, _velocity.X, _velocity.Y, dtMs,
                           traceState, traceColumn, traceDir, traceGear, frame.ConstantX, frame.ConstantY);

                // What the stall watch arms on. Read one tick later, by which time it describes
                // the push the lever has just had a millisecond to answer.
                _lastCommandedForce = Math.Max(Math.Abs(frame.ConstantX), Math.Abs(frame.ConstantY));
                _lastTickEngaged = traceState == GateState.Engaged;

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
        /// <summary>
        /// Reads the clutch pedal, if it is being read directly, and returns the telemetry the
        /// effects should run on. Everything here is best-effort by design: losing the pedals
        /// must never stop the gate, so a failure falls back to whatever the game reported and
        /// says so once.
        /// </summary>
        private TelemetryState ReadPedals(EngineConfig cfg, TelemetryState telemetry,
                                          long tickCount, long nowMs)
        {
            bool wanted = cfg.ClutchSource == ClutchSource.Pedal || _pedalCaptureRequest != 0
                          || _pedalCapture != null;

            if (!wanted)
            {
                if (_pedalsOpen) ClosePedals();
                return telemetry;
            }

            // Reopen when the chosen device changes, so switching pedal sets in the picker takes
            // effect without restarting the plugin.
            if (_pedalsOpen && _pedalDeviceOpened != cfg.PedalDeviceId) ClosePedals();

            if (!_pedalsOpen)
            {
                // Picking a different device in the picker is a fresh situation, so it is tried
                // at once rather than serving out the previous device's backoff.
                if (_pedalOpenFor != cfg.PedalDeviceId)
                {
                    _pedalOpenFor = cfg.PedalDeviceId;
                    _pedalOpenRetry.Reset();
                }

                // Opening a DirectInput device that is not there costs milliseconds, and this is
                // the 1 kHz loop. Unthrottled it was the whole tick: a pedal set unplugged
                // mid-session - or a saved binding for pedals this machine no longer has - took
                // the loop from 990 Hz to 81, measured on the rig, which is 12 ms of a budget
                // the gate needs all of. The log line was throttled to thirty seconds from the
                // start, and that is exactly what hid it: every retry cost a tick and only one
                // in thirty thousand said so.
                if (!_pedalOpenRetry.Due(nowMs)) return telemetry;

                string error;
                if (!_pedals.Open(cfg.PedalDeviceId, WindowHandleProvider.Get(), out error))
                {
                    _pedalOpenRetry.Failed(nowMs);
                    Log.WarnThrottled("pedal-open", "Clutch pedal unavailable: " + error, 30);
                    return telemetry;
                }

                _pedalOpenRetry.Succeeded();
                _pedalsOpen = true;
                _pedalDeviceOpened = cfg.PedalDeviceId;
            }

            // An ankle does not need a kilohertz, and every poll is time the gate is not getting.
            if (tickCount % PedalPollEveryTicks != 0) return EffectiveTelemetry(cfg, telemetry);

            if (!_pedals.TryPoll(_pedalAxes))
            {
                Log.WarnThrottled("pedal-poll", "Lost the clutch pedal device; falling back to " +
                                                "the game's own clutch reading.", 30);
                ClosePedals();
                return telemetry;
            }

            StepPedalCapture(nowMs);

            int axis = cfg.PedalAxisIndex;
            if (axis >= 0 && axis < _pedalAxes.Length)
            {
                _pedalRaw = _pedalAxes[axis];
                _pedalPercent = cfg.PedalCalibration != null
                    ? cfg.PedalCalibration.ToPercent(_pedalRaw)
                    : 0.0;
            }

            return EffectiveTelemetry(cfg, telemetry);
        }

        /// <summary>
        /// The snapshot the effects see. When the pedal is the source, the clutch is swapped into
        /// a scratch instance the engine owns - never into the published snapshot, which the data
        /// thread is still writing and other readers expect to be whole.
        /// </summary>
        private TelemetryState EffectiveTelemetry(EngineConfig cfg, TelemetryState telemetry)
        {
            if (cfg.ClutchSource != ClutchSource.Pedal) return telemetry;

            _pedalTelemetry.CopyFromWithClutch(telemetry, _pedalPercent);
            return _pedalTelemetry;
        }

        private void StepPedalCapture(long nowMs)
        {
            if (Interlocked.Exchange(ref _pedalCaptureRequest, 0) == 1)
            {
                _pedalCapture = new AxisCapture(nowMs);
                Log.Info("Listening for a clutch pedal.");
            }

            AxisCapture capture = _pedalCapture;
            if (capture == null) return;

            capture.Observe(_pedalDeviceOpened, _pedalAxes, nowMs);
            _captureHint = capture.Hint;

            if (!capture.IsFinished) return;

            _pedalCapture = null;

            if (capture.Phase == CapturePhase.Committed)
            {
                Log.Info("Clutch pedal bound to axis " + capture.AxisIndex +
                         (capture.Result.Invert ? " (inverted)" : "") + ".");
            }
            else
            {
                Log.Info("Clutch pedal capture ended: " + capture.Phase + ".");
            }

            Action<AxisCapture> done = PedalCaptureCompleted;
            if (done != null) done(capture);
        }

        // --- switching gates ------------------------------------------------------------------
        private readonly ProfileSwitchTransition _transition = new ProfileSwitchTransition();
        private double _confirmPhase;

        /// <summary>Amplitude of the profile-confirmation pulse, in DirectInput units.</summary>
        private const int ConfirmPulseAmplitude = 2600;

        /// <summary>Pitch of that pulse. Low enough to read as a thump rather than a buzz.</summary>
        private const double ConfirmPulseHz = 28.0;

        /// <summary>
        /// Scales a finished frame. Springs are untouched because every frame ships them Off, and
        /// the damper is untouched because it opposes motion by construction - winding a
        /// stabiliser in alongside the force it stabilises would be backwards.
        /// </summary>
        private static ForceFrame ScaleFrame(ForceFrame frame, double scale)
        {
            if (scale >= 1.0) return frame;

            frame.ConstantX = (int)Math.Round(frame.ConstantX * scale);
            frame.ConstantY = (int)Math.Round(frame.ConstantY * scale);
            return frame;
        }

        /// <summary>
        /// One tick of the confirmation buzz. Keyed on time like every other carrier, so it can
        /// never join the position-to-force loop, and scaled by the same effective gain as the
        /// gate so the 10% unconfirmed-polarity cap covers it too.
        /// </summary>
        private int ConfirmPulse(EngineConfig cfg, double dtMs, double envelope)
        {
            _confirmPhase += 2.0 * Math.PI * ConfirmPulseHz * dtMs / 1000.0;
            if (_confirmPhase > 2.0 * Math.PI) _confirmPhase -= 2.0 * Math.PI;

            double amplitude = ConfirmPulseAmplitude * cfg.EffectiveGain * envelope;
            return (int)Math.Round(Math.Sin(_confirmPhase) * amplitude);
        }

        private void ClosePedals()
        {
            _pedals.Close();
            _pedalsOpen = false;
            _pedalDeviceOpened = null;
            _pedalRaw = 0;
            _pedalPercent = 0;

            // Closing is always a deliberate transition - the source was switched, the device was
            // changed, or a poll failed on a device that was working a moment ago - so the next
            // open is tried immediately rather than inheriting a stale backoff. A device that
            // opens and then fails to poll costs one attempt per poll interval, not one per tick.
            _pedalOpenRetry.Reset();
        }

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
                _prndMachine.Resync(y);

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
                    // the resync arms without firing, and no gear button is ever held; in PRND
                    // the lever is always somewhere, so the adopted position goes straight back.
                    _effects.Apply(ForceComposer.FreeFrame(), nowMs);

                    _stateMachine.Resync(x, y);
                    _seqMachine.Resync(y);
                    _prndMachine.Resync(y);
                    if (_output != null) _output.SetGear(CurrentHeldButton(cfg));

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
                    _prndMachine.Resync(y);
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

        private void ApplyConfigChange(EngineConfig cfg, long nowMs)
        {
            EngineConfig previous = _activeConfig;
            _activeConfig = cfg;

            _geometry = cfg.BuildGeometry();
            _composer = new ForceComposer(_geometry, cfg);

            // A profile that ships a hard gate arrives locked, and a gate that moved or
            // changed its mode re-arms: the release is a session grant against one specific
            // gate, not a standing exemption that survives the gate being rebuilt under it.
            bool rebuild = _stateMachine == null || !GeometryUnchanged(previous, cfg);
            if (rebuild || previous == null
                || previous.LockoutMode != cfg.LockoutMode
                || previous.PrndLockoutMode != cfg.PrndLockoutMode)
            {
                SetLockoutReleased(false);
                _lockoutReleaseSide = 0;
            }

            // Force changes are picked up by rebuilding the composer alone. Only rebuild the
            // state machine when the gate itself moved, so dragging a force slider cannot
            // disturb a gear that is currently held.
            if (rebuild)
            {
                _stateMachine = new GateStateMachine(_geometry, cfg.MinEngageTicks);
                _seqMachine = new SequentialStateMachine(_geometry, cfg.MinEngageTicks);
                _prndMachine = new PrndStateMachine(cfg.BuildPrndLane());

                int x, y;
                string error;
                if (_device != null && _device.TryPoll(out x, out y, out error))
                {
                    _stateMachine.Resync(x, y);
                    _seqMachine.Resync(y);
                    _prndMachine.Resync(y);
                }

                // The rebuilt machine may disagree with what is currently held - new geometry
                // can put the stick outside the gear it was in. Push the truth to vJoy now,
                // or the old button would stay down with nothing left to release it. A pulse
                // in flight is cleared the same way, or switching pattern mid-press would
                // leave an up/down button held as a phantom gear.
                if (_output != null)
                {
                    if (_pulseButton != 0) _output.SetButton(_pulseButton, false);
                    _output.SetGear(CurrentHeldButton(cfg));
                }

                _pulseButton = 0;
                _pulsePending = 0;
                _seqPushed = ShiftDir.None;

                // The gate itself moved, which is the only case worth easing into. A force slider
                // does not come through here, so dragging one still applies immediately - the
                // whole point of a live dial. Skipped on the very first build, when there is no
                // previous gate to have been thrown out of and nothing to confirm.
                if (previous != null)
                {
                    _transition.Begin(nowMs, cfg.ProfileConfirmPulses);
                }
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
        /// The self-re-arming hard mode: the gate closes itself the moment the released
        /// crossing completes, and not before - a granted pass keeps until it is spent. For a
        /// gap the completion is the composer's side latch landing opposite where the release
        /// was granted, which it can only do by fully exiting the band on the far side. For a
        /// slot it is the guarded gear latching (entry) or letting go (exit) - the collar
        /// seating again behind the shift. Re-engaging goes through SetLockoutReleased so the
        /// event fires, and the composer's next tick sees an ordinary arming edge: the lever
        /// is at a zero-force position at every one of these moments by construction.
        /// </summary>
        private void StepAutoRearm(EngineConfig cfg, StateTransition t)
        {
            if (_geometry == null || _composer == null) return;

            if (_geometry.LockoutIsSlot)
            {
                if (!t.GearChanged) return;

                int guarded = _geometry.GearFor(_geometry.LockoutSlotColumn, _geometry.LockoutSlotDir);
                bool entryCovered = cfg.LockoutSlotDirection != LockoutSlotDirection.Exit;
                bool exitCovered = cfg.LockoutSlotDirection != LockoutSlotDirection.Entry;

                if ((entryCovered && t.Gear == guarded)
                    || (exitCovered && t.PreviousGear == guarded && t.Gear != guarded))
                {
                    SetLockoutReleased(false);
                }

                return;
            }

            if (!_geometry.HasLockout) return;

            int side = _composer.LockoutSideLatch;
            if (side == 0) return;

            if (_lockoutReleaseSide == 0)
            {
                _lockoutReleaseSide = side;
            }
            else if (side == -_lockoutReleaseSide)
            {
                SetLockoutReleased(false);
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
                && a.ColumnInnerHalfEnter == b.ColumnInnerHalfEnter
                && a.ColumnInnerHalfExit == b.ColumnInnerHalfExit
                && a.EngageDepth == b.EngageDepth
                && a.ReleaseDepth == b.ReleaseDepth
                && a.LockoutHalfWidth == b.LockoutHalfWidth

                // Placement and gap direction both move LockoutCentre, which is a barrier
                // crest, which is a guide-column ownership boundary - geometry, exactly like
                // the width above. The slot gear, the modes, the slot direction and the PRND
                // lockout dials are deliberately NOT here: they move force only, and listing
                // one would make dragging its dial drop a held gear and fire the switch
                // transition.
                && a.LockoutPlacement == b.LockoutPlacement
                && a.LockoutGapDirection == b.LockoutGapDirection
                && a.DetentHysteresis == b.DetentHysteresis
                && a.MinEngageTicks == b.MinEngageTicks
                && a.MirrorColumns == b.MirrorColumns
                && a.MirrorSlots == b.MirrorSlots

                // Moves the PRND positions, so it moves where a button changes hands - geometry
                // by the only definition that matters here, even though it sits among the forces
                // in the UI.
                && a.PrndLaneHalfLength == b.PrndLaneHalfLength;
        }

        /// <summary>
        /// What vJoy should be holding right now, for whichever pattern is configured. Sequential
        /// holds nothing - its shifts are timed pulses on their own buttons - the H gate holds its
        /// gear, and PRND holds its position. One answer, so the three places that have to push
        /// the truth back to vJoy (a rebuilt gate, a finished calibration, a profile switch)
        /// cannot each get a different pattern's version of it wrong.
        /// </summary>
        private int CurrentHeldButton(EngineConfig cfg)
        {
            if (cfg == null) return 0;
            if (cfg.Pattern == GatePattern.Sequential) return 0;
            if (cfg.Pattern == GatePattern.Prnd) return _prndMachine != null ? _prndMachine.CurrentButton : 0;
            return _stateMachine != null ? _stateMachine.CurrentGear : 0;
        }

        private void PublishSnapshot(int x, int y, double loopHz)
        {
            GateStateMachine sm = _stateMachine;
            PrndStateMachine prnd = _prndMachine;
            GateGeometry geo = _geometry;
            EngineConfig cfg = _activeConfig;

            bool sequential = cfg != null && cfg.Pattern == GatePattern.Sequential;
            bool selector = cfg != null && cfg.Pattern == GatePattern.Prnd;
            bool gate = !sequential && !selector;

            string label;
            if (sequential)
            {
                label = _seqPushed == ShiftDir.Fwd ? "+" : (_seqPushed == ShiftDir.Back ? "-" : "N");
            }
            else if (selector)
            {
                label = prnd != null ? prnd.CurrentLabel : "-";
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

                // A selector is always in a position, so it is always Engaged and its button is
                // always held - which is what the Monitor tab should say, and what makes the gear
                // label light up the way a held gear does.
                State = selector ? GateState.Engaged : (gate && sm != null ? sm.State : GateState.Neutral),
                Column = gate && sm != null ? sm.Column : Column.None,
                Gear = selector
                    ? (prnd != null ? prnd.CurrentButton : 0)
                    : (gate && sm != null ? sm.CurrentGear : 0),
                GearLabel = label,
                LoopHz = loopHz,
                StatusMessage = _status,
                DeviceName = _device != null ? (_device.ProductName ?? "") : "",
                LockoutEngaged = !_lockoutReleased
            };

            _snapshot = snapshot;
        }

        // --- force-output watch -------------------------------------------------------------
        //
        // The base has twice been observed to stop producing torque while staying enumerated,
        // answering polls and accepting every effect write without error. Nothing in the write
        // path can see that: from the loop's side everything succeeded. On the second occasion
        // MOZA's own Cockpit also showed no axis movement, which puts the fault squarely in the
        // base's firmware and means the only remedy is a power cycle - see docs/hardware.md.
        //
        // The plugin cannot fix it. It can stop the user losing a race to it, which needs two
        // independent signals, because either alone can be wrong:
        //
        //   - what the device says about its own actuators, which is authoritative when the
        //     driver answers at all, and Unknown when it will not;
        //   - whether the reported position has moved, which caught the real outage when the
        //     first signal might not have, and which is only suggestive because a lever nobody
        //     is touching is also perfectly still.
        //
        // Neither ever stops the loop or cuts force. This watch reports; the watchdog is the
        // one that intervenes.
        //
        // The second signal was first written as stillness alone, and it was worse than useless:
        // it fired four times in five minutes against a base that was demonstrably healthy, for
        // the reason its own comment admitted - the lever was simply not being held. Worse, the
        // warnings were then read back as evidence and produced a confident diagnosis of a
        // hardware fault that the logs did not support. A detector that invents symptoms costs
        // more than no detector, so this one now has to be ARMED before it can fire.
        private const int HealthPollMs = 1000;

        // Armed only while the gate is pushing this hard. Below it the lever is free, so its
        // stillness says nothing about the base; above it a lever that never moves is either
        // being held or is not responding, and that is the case worth a sentence.
        private const int StallForceFloor = 1500;

        // Long enough that holding the lever against a wall while thinking does not trip it.
        private const int FrozenPositionMs = 30000;

        private long _nextHealthPollMs;
        private ForceOutputHealth _health = ForceOutputHealth.Unknown;
        private int _lastSeenX = int.MinValue;
        private int _lastSeenY = int.MinValue;
        private long _stalledSinceMs;
        private bool _frozenReported;

        // Last tick's commanded force and latch state, recorded after the frame was actually
        // sent. NotePosition runs before the frame is composed, so it reads these one tick
        // stale - a millisecond, against a thirty-second window.
        private int _lastCommandedForce;
        private bool _lastTickEngaged;

        /// <summary>
        /// Called once per tick from the Run phase; does real work about once a second. Cheap
        /// enough for that rate - one driver query - and deliberately not on the hot path of
        /// composing forces.
        /// </summary>
        private void WatchForceOutput(long nowMs)
        {
            if (nowMs < _nextHealthPollMs) return;
            _nextHealthPollMs = nowMs + HealthPollMs;

            FfbDevice device = _device;
            EffectSet effects = _effects;
            if (device == null) return;

            // The device's own Empty flag is not taken at face value - this base sets it while
            // producing force perfectly. The effects themselves are the corroborating witness.
            ForceOutputHealth now = device.ReadForceOutputHealth(
                effects != null && effects.AnyStillDownloaded());

            if (now == _health || now == ForceOutputHealth.Unknown) return;

            bool wasFault = ForceFeedbackHealth.IsFault(_health);
            _health = now;

            if (ForceFeedbackHealth.IsFault(now))
            {
                Log.Warn("Force output: " + ForceFeedbackHealth.Describe(now));
                _status = ForceFeedbackHealth.Describe(now);

                // One attempt each, then let the next poll say whether it took. Which attempt
                // depends on the fault: switching the actuators back on cannot re-download an
                // effect, and re-downloading effects does nothing for actuators that are off.
                if (now == ForceOutputHealth.ActuatorsOff) device.TryWakeForceFeedback();
                if (now == ForceOutputHealth.EffectsGone) RebuildEffects();
            }
            else if (wasFault)
            {
                // Only worth a line as a recovery. Saying "producing" every time the lever leaves
                // a wall would bury the one message that matters.
                //
                // The status has to be put back too, and forgetting that was its own bug: the
                // fault sentence is the last thing written to a field the Monitor tab reads
                // forever, so it outlived the fault it described and hid every status after it.
                Log.Info("Force output: recovered, the base is producing force again.");
                if (_activeConfig != null) _status = BuildReadyStatus(_activeConfig);
            }
        }

        /// <summary>
        /// Re-creates the gate's effects on the device handle we already hold, for the one fault
        /// where that is the repair: the base has thrown our effects away without dropping the
        /// handle, so there is nothing wrong with the connection and a full reopen would be a
        /// heavier interruption than the fault.
        ///
        /// Deliberately does not touch the gear buttons. The lever has not moved and the game's
        /// idea of which gear is engaged is still correct; force is what is missing, and clearing
        /// the gear would turn a silent loss of feel into a silent loss of drive.
        /// </summary>
        private void RebuildEffects()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                DisposeEffects();

                string error;
                _effects = _device.CreateEffects(_composer.DamperCoefficient, out error);

                if (_effects == null) Log.Warn("Force output: could not rebuild the effects - " + error);
                else Log.Info("Force output: effects rebuilt on the open device.");
            }
        }

        /// <summary>
        /// Called from the tick with each fresh reading. A base that has stopped reporting looks
        /// exactly like a lever nobody is touching, so stillness on its own is never reported:
        /// the clock runs only while the gate is shoving the lever and no gear is holding it.
        /// Warns once, and clears itself the moment anything moves.
        /// </summary>
        private void NotePosition(int x, int y, long nowMs)
        {
            if (x != _lastSeenX || y != _lastSeenY)
            {
                _lastSeenX = x;
                _lastSeenY = y;
                _stalledSinceMs = 0;
                _frozenReported = false;
                return;
            }

            // Two ways a healthy base sits perfectly still, both of them ordinary, and neither
            // says anything about its health:
            //
            //   - nothing is pushing the lever, so there is nothing for it to fail to do;
            //   - a gear is engaged, and the seated hold is supposed to keep the lever exactly
            //     where it is - a straight in fourth would otherwise trip this every lap.
            //
            // Disarmed rather than paused, so the window always measures one continuous stall.
            if (_lastCommandedForce < StallForceFloor || _lastTickEngaged)
            {
                _stalledSinceMs = 0;
                return;
            }

            // A base that was already frozen when the plugin started never moves, so the clock
            // starts on the first armed tick rather than on the first movement - otherwise the
            // one case where the user most needs telling is the one case this stays silent for.
            if (_stalledSinceMs == 0) _stalledSinceMs = nowMs;

            if (_frozenReported) return;
            if (nowMs - _stalledSinceMs < FrozenPositionMs) return;

            _frozenReported = true;
            Log.Warn("The base has not moved for " + ((nowMs - _stalledSinceMs) / 1000) +
                     " s while the gate was pushing it, and no gear is holding it there. If your " +
                     "hand was not on the lever this is nothing; if it was, the base has stopped " +
                     "responding and only a power cycle will bring it back - MOZA Cockpit will " +
                     "show it frozen too.");
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

            // The pedals last, and outside that ordering entirely: this handle is read-only and
            // non-exclusive, so it can neither hold a button down nor leave a force running. It
            // still has to go, or a disabled plugin keeps a handle on the user's pedal set.
            ClosePedals();
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
