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
        private ForceComposer _composer;

        private FfbDevice _device;
        private EffectSet _effects;
        private VJoyGearOutput _output;

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
        /// Smoothing applied to the measured velocity. A raw tick-to-tick difference is too
        /// noisy to act on - the axis jitters by a few counts at rest, which at 400 Hz reads as
        /// thousands of counts per second. But smoothing is also phase lag, and phase-lagged
        /// damping stops damping at exactly the frequencies where a wall rings (a lagged
        /// opposing force arrives partly in phase with the motion). 0.45 keeps the lag near a
        /// single tick; the noise floor that remains is handled by the composer's velocity
        /// deadband instead of by more smoothing.
        /// </summary>
        private const double VelocitySmoothing = 0.45;

        private long _velocityStamp;
        private int _velocityLastX;
        private int _velocityLastY;
        private int _velocityX;
        private int _velocityY;
        private bool _velocityPrimed;

        /// <summary>Forgets the current motion estimate, so a jump in position is not read as speed.</summary>
        private void ResetVelocity()
        {
            _velocityPrimed = false;
            _velocityX = 0;
            _velocityY = 0;
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
            long now = Stopwatch.GetTimestamp();

            if (!_velocityPrimed)
            {
                _velocityPrimed = true;
                _velocityStamp = now;
                _velocityLastX = x;
                _velocityLastY = y;
                _velocityX = 0;
                _velocityY = 0;
                return;
            }

            double dt = (now - _velocityStamp) / (double)Stopwatch.Frequency;
            _velocityStamp = now;

            // A stalled or absurdly long tick says nothing useful about speed; keep the last
            // estimate and resynchronise from this position.
            if (dt < 0.0002 || dt > 0.05)
            {
                _velocityLastX = x;
                _velocityLastY = y;
                return;
            }

            double rawX = (x - _velocityLastX) / dt;
            double rawY = (y - _velocityLastY) / dt;

            _velocityLastX = x;
            _velocityLastY = y;

            _velocityX += (int)Math.Round((rawX - _velocityX) * VelocitySmoothing);
            _velocityY += (int)Math.Round((rawY - _velocityY) * VelocitySmoothing);
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

                StateTransition t = _stateMachine.Update(x, y);

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

                UpdateVelocity(x, y);

                double dtMs = ComposeDelta(cfg);
                ForceFrame frame = _composer.Compose(
                    t.State, t.Column, t.Direction, x, y, _velocityX, _velocityY, dtMs);
                _effects.Apply(frame, nowMs);

                // After Apply, so what is recorded is what was actually sent.
                _trace.Add(nowMs, x, y, _velocityX, _velocityY, dtMs,
                           t.State, t.Column, t.Direction, t.Gear, frame.ConstantX, frame.ConstantY);

                if (_effects.IsFaulted)
                {
                    HandleDeviceLoss("Effect updates failed repeatedly; reopening the device.");
                    return;
                }

                if (tickCount % SnapshotEveryTicks == 0 || t.GearChanged)
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

                // Drop any held gear: the probes move the stick, and a button left down through
                // that would look to a game like a gear change the user never made.
                if (_output != null) _output.SetGear(0);
                _stateMachine.Resync(x, y);

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
                    // Back to a known state before the gate takes over again.
                    _effects.Apply(ForceComposer.FreeFrame(), nowMs);

                    _stateMachine.Resync(x, y);
                    if (_output != null) _output.SetGear(_stateMachine.CurrentGear);

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
                if (_device.TryPoll(out x, out y, out pollError)) _stateMachine.Resync(x, y);

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

                int x, y;
                string error;
                if (_device != null && _device.TryPoll(out x, out y, out error)) _stateMachine.Resync(x, y);

                // The rebuilt machine may disagree with what is currently held - new geometry
                // can put the stick outside the gear it was in. Push the truth to vJoy now,
                // or the old button would stay down with nothing left to release it.
                if (_output != null) _output.SetGear(_stateMachine.CurrentGear);
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

        /// <summary>True when nothing that changes where the gears are has moved.</summary>
        private static bool GeometryUnchanged(EngineConfig a, EngineConfig b)
        {
            if (a == null || b == null) return false;

            return a.ChannelHalfEnter == b.ChannelHalfEnter
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
            var snapshot = new EngineSnapshot
            {
                Phase = _phase,
                DeviceConnected = _device != null && _device.IsOpen,
                VJoyConnected = _output != null && _output.IsConnected,
                RawX = x,
                RawY = y,
                X = x,
                Y = y,
                State = sm != null ? sm.State : GateState.Neutral,
                Column = sm != null ? sm.Column : Column.None,
                Gear = sm != null ? sm.CurrentGear : 0,
                GearLabel = GateGeometry.GearLabel(sm != null ? sm.CurrentGear : 0),
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
