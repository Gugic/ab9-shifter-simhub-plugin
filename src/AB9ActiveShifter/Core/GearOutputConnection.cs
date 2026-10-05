using System;
using AB9ActiveShifter.Output;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// Owns the selected output independently of base acquisition. All driver work stays
    /// behind IGearOutput, so reconnect, ownership checks and replacement can use a fake.
    /// The caller supplies time and whether a fresh base position permits publishing.
    /// </summary>
    public sealed class GearOutputConnection
    {
        private const int HealthCheckMs = 1000;
        private readonly Func<EngineConfig, IGearOutput> _factory;
        private readonly RetryBackoff _retry = new RetryBackoff(1000, 2000, 5000, 15000);
        private EngineConfig _config;
        private long _nextHealthAtMs;

        public GearOutputConnection(Func<EngineConfig, IGearOutput> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public IGearOutput Output { get; private set; }

        public void RestartClock()
        {
            _retry.Reset();
            _nextHealthAtMs = 0;
        }

        public bool Configure(EngineConfig config)
        {
            bool changed = GearOutputConfig.OutputChanged(_config, config);
            if (changed) Disconnect();
            _config = config;
            return changed;
        }

        /// <returns>True when a connection was acquired this call.</returns>
        public bool Poll(long nowMs, bool canPublish, int heldButton)
        {
            if (_config == null) return false;
            if (Output == null) Output = _factory(_config);
            if (Output.IsConnected && nowMs >= _nextHealthAtMs)
            {
                _nextHealthAtMs = nowMs + HealthCheckMs;
                Output.CheckConnection();
            }
            if (Output.IsConnected || !_retry.Due(nowMs)) return false;
            if (!Output.Connect())
            {
                _retry.Failed(nowMs);
                return false;
            }
            _retry.Succeeded();
            _nextHealthAtMs = nowMs + HealthCheckMs;
            if (canPublish) Output.SetGear(heldButton);
            return true;
        }

        public void Disconnect()
        {
            if (Output != null) Output.Disconnect();
            Output = null;
            RestartClock();
        }
    }
}
