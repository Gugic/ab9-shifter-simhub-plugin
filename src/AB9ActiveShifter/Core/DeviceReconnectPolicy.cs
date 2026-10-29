namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Gates base opens after a loss or failed acquisition. Automatic retaking restores the
    /// pre-0.14 recovery behavior, with a delay even when every reopen succeeds. Opting out
    /// waits for the game to exit instead. Neither path attempts I/O on every engine tick.
    /// </summary>
    public sealed class DeviceReconnectPolicy
    {
        public const int GameLiveWindowMs = 2000;

        private readonly RetryBackoff _open = new RetryBackoff(1000, 2000, 5000);
        private readonly RetryBackoff _gameCheck = new RetryBackoff(5000, 15000, 30000);

        public bool TakeoverPending { get; private set; }

        public bool WaitingForGame(bool autoRetakeBase)
        {
            return TakeoverPending && !autoRetakeBase;
        }

        public void DeviceLost(DeviceFault fault, long nowMs)
        {
            Reset();
            OpenFailed(fault, nowMs);
        }

        public void OpenFailed(DeviceFault fault, long nowMs)
        {
            _open.Failed(nowMs);
            if (fault == DeviceFault.TakenByAnotherApp && !TakeoverPending)
            {
                TakeoverPending = true;
                // Do not interpret the telemetry gap during game startup as a game exit.
                _gameCheck.Reset();
                _gameCheck.Failed(nowMs);
            }
        }

        public bool CanAttemptOpen(long nowMs, bool autoRetakeBase, bool gameRunning, int telemetryAgeMs)
        {
            if (!_open.Due(nowMs)) return false;
            if (WaitingForGame(autoRetakeBase))
            {
                if (!_gameCheck.Due(nowMs)) return false;
                if (gameRunning && telemetryAgeMs >= 0 && telemetryAgeMs <= GameLiveWindowMs)
                {
                    _gameCheck.Failed(nowMs);
                    return false;
                }
                TakeoverPending = false;
                _gameCheck.Reset();
            }
            return true;
        }

        /// <summary>Internal native-write pauses must not erase a conservative wait.</summary>
        public void RestartClock(bool preserveTakeover)
        {
            if (preserveTakeover && TakeoverPending)
                DeviceLost(DeviceFault.TakenByAnotherApp, 0);
            else Reset();
        }

        /// <summary>A successful open, explicit restart or changed device starts a new attempt.</summary>
        public void Reset()
        {
            TakeoverPending = false;
            _open.Reset();
            _gameCheck.Reset();
        }
    }
}
