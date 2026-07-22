using System;
using System.Diagnostics;
using System.Threading;
using AB9ActiveShifter.Device;
using AB9ActiveShifter.Output;

namespace AB9ActiveShifter.Core
{
    public enum PolarityTest
    {
        None = 0,

        /// <summary>Centring spring on Y: does the stick pull back to the middle, or run away from it?</summary>
        Spring = 1,

        /// <summary>Steady push on Y: does it push toward the player, or away?</summary>
        Constant = 2
    }

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

        /// <summary>
        /// Fixed, deliberately modest force used by the polarity wizard. Independent of the
        /// user's gain so the test is always perceptible, and bounded so it is always safe.
        /// </summary>
        private const double PolarityTestGain = 0.25;
        private const int PolarityTestSpringCoefficient = 3500;
        private const int PolarityTestConstantMagnitude = 2500;
        private const int PolarityTestDurationMs = 2500;

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

        private int _polarityTestRequest;
        private PolarityTest _activeTest = PolarityTest.None;
        private long _testEndsAtMs;

        /// <summary>Raised on the engine thread when the selected gear changes (new, previous).</summary>
        public event Action<int, int> GearChanged;

        public EngineSnapshot Snapshot { get { return _snapshot; } }

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
            PublishSnapshot(GateGeometry.AxisCenter, GateGeometry.AxisCenter,
                            GateGeometry.AxisCenter, GateGeometry.AxisCenter, 0);
            Log.Info("FFB engine stopped.");
        }

        public void RequestPolarityTest(PolarityTest kind)
        {
            Interlocked.Exchange(ref _polarityTestRequest, (int)kind);
        }

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

                        PublishSnapshot(GateGeometry.AxisCenter, GateGeometry.AxisCenter,
                                        GateGeometry.AxisCenter, GateGeometry.AxisCenter, 0);

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

                NativeMethods.TimeEndPeriod(1);
                _running = false;
            }
        }

        private static long PaceTick(long nextDue, long periodTicks)
        {
            while (true)
            {
                long remaining = nextDue - Stopwatch.GetTimestamp();
                if (remaining <= 0) break;

                double remainingMs = remaining * 1000.0 / Stopwatch.Frequency;
                if (remainingMs > 1.5) Thread.Sleep(1);
                else Thread.SpinWait(64);
            }

            long due = nextDue + periodTicks;
            long now = Stopwatch.GetTimestamp();

            // If we fell badly behind (device stall, thread pre-empted), resynchronise
            // instead of trying to catch up with a burst of ticks.
            if (due < now) due = now + periodTicks;
            return due;
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

                int x = cfg.InvertX ? GateGeometry.AxisMax - rawX : rawX;
                int y = cfg.InvertY ? GateGeometry.AxisMax - rawY : rawY;

                if (HandlePolarityTest(cfg, nowMs, rawX, rawY, x, y, loopHz)) return;

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

                ForceFrame frame = _composer.Compose(t.State, t.Column, t.Direction, x, y);
                _effects.Apply(frame, nowMs);

                if (_effects.IsFaulted)
                {
                    HandleDeviceLoss("Effect updates failed repeatedly; reopening the device.");
                    return;
                }

                if (tickCount % SnapshotEveryTicks == 0 || t.GearChanged)
                {
                    PublishSnapshot(rawX, rawY, x, y, loopHz);
                }
            }
        }

        /// <summary>
        /// Applies the wizard's test force instead of the gate while a test is running.
        /// Returns true when the tick was consumed by the test.
        /// </summary>
        private bool HandlePolarityTest(EngineConfig cfg, long nowMs, int rawX, int rawY, int x, int y, double loopHz)
        {
            int requested = Interlocked.Exchange(ref _polarityTestRequest, 0);
            if (requested != (int)PolarityTest.None)
            {
                _activeTest = (PolarityTest)requested;
                _testEndsAtMs = nowMs + PolarityTestDurationMs;

                // Start from a clean slate so the test force is the only thing being felt.
                _stateMachine.Resync(x, y);
                if (_output != null) _output.SetGear(0);
                Log.Info("Polarity test started: " + _activeTest);
            }

            if (_activeTest == PolarityTest.None) return false;

            if (nowMs >= _testEndsAtMs)
            {
                _activeTest = PolarityTest.None;
                _effects.Apply(new ForceFrame
                {
                    SpringX = SpringPreset.Off,
                    SpringY = SpringPreset.Off,
                    DamperCoefficient = _composer.DamperCoefficient
                }, nowMs);
                _stateMachine.Resync(x, y);
                _status = "Polarity test finished.";
                Log.Info("Polarity test finished.");
                return true;
            }

            int springSign = cfg.InvertSpringPolarity ? -1 : 1;
            int constantSign = cfg.InvertConstantPolarity ? -1 : 1;

            var frame = new ForceFrame
            {
                SpringX = SpringPreset.Off,
                SpringY = SpringPreset.Off,
                DamperCoefficient = _composer.DamperCoefficient
            };

            if (_activeTest == PolarityTest.Spring)
            {
                frame.SpringY = SpringPreset.Centering(
                    0,
                    (int)Math.Round(PolarityTestSpringCoefficient * PolarityTestGain) * springSign,
                    300);
                _status = "Testing spring: the stick should pull toward centre.";
            }
            else
            {
                frame.ConstantY =
                    (int)Math.Round(PolarityTestConstantMagnitude * PolarityTestGain) * constantSign;
                _status = "Testing push: the stick should push toward you.";
            }

            _effects.Apply(frame, nowMs);
            PublishSnapshot(rawX, rawY, x, y, loopHz);
            return true;
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
                    int nx = cfg.InvertX ? GateGeometry.AxisMax - x : x;
                    int ny = cfg.InvertY ? GateGeometry.AxisMax - y : y;
                    _stateMachine.Resync(nx, ny);
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

                int x, y;
                string error;
                if (_device != null && _device.TryPoll(out x, out y, out error))
                {
                    _stateMachine.Resync(cfg.InvertX ? GateGeometry.AxisMax - x : x,
                                         cfg.InvertY ? GateGeometry.AxisMax - y : y);
                }

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
                && a.LockoutStart == b.LockoutStart
                && a.DetentHysteresis == b.DetentHysteresis
                && a.MinEngageTicks == b.MinEngageTicks
                && a.InvertX == b.InvertX
                && a.InvertY == b.InvertY;
        }

        private void PublishSnapshot(int rawX, int rawY, int x, int y, double loopHz)
        {
            GateStateMachine sm = _stateMachine;
            var snapshot = new EngineSnapshot
            {
                Phase = _phase,
                DeviceConnected = _device != null && _device.IsOpen,
                VJoyConnected = _output != null && _output.IsConnected,
                RawX = rawX,
                RawY = rawY,
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
