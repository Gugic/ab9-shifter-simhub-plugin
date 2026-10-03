using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace AB9ActiveShifter.Updates
{
    /// <summary>Immutable display state; never read by the force loop or the telemetry thread.</summary>
    public sealed class UpdateState
    {
        public ReleaseInfo Release { get; }
        public string Status { get; }
        public DateTime? LastChecked { get; }
        public bool Busy { get; }
        public bool RestartRequired { get; }

        public UpdateState(ReleaseInfo release, string status, DateTime? lastChecked, bool busy, bool restartRequired)
        {
            Release = release;
            Status = status;
            LastChecked = lastChecked;
            Busy = busy;
            RestartRequired = restartRequired;
        }
    }

    /// <summary>
    /// One service per SimHub process, surviving End/Init at game change. All HTTP and file work
    /// runs on the thread pool; only the Options controls consume its snapshots.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Timer _timer;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly string _dllPath;
        private readonly string _installedVersion;
        private readonly object _timerSync = new object();
        private volatile UpdateState _state = new UpdateState(null, "Not checked yet.", null, false, false);
        private volatile bool _disposed;
        private bool _automatic;
        private int _operation;

        public UpdateState State { get { return _state; } }
        public event Action Changed;

        public UpdateService(string dllPath, string installedVersion)
        {
            _dllPath = dllPath;
            _installedVersion = installedVersion;
            _http = new HttpClient(new HttpClientHandler { SslProtocols = SslProtocols.Tls12 })
            {
                Timeout = Timeout.InfiniteTimeSpan,
                MaxResponseContentBufferSize = 1024 * 1024
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("AB9ActiveShifter/" + installedVersion.Split('+')[0]);
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            UpdateInstaller.CleanupAfterRestart(dllPath);
            _timer = new Timer(AutomaticCheck, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Configure(bool automatic)
        {
            lock (_timerSync)
            {
                if (_disposed || automatic == _automatic) return;
                _automatic = automatic;
                _timer.Change(automatic ? TimeSpan.Zero : Timeout.InfiniteTimeSpan,
                    automatic ? TimeSpan.FromHours(6) : Timeout.InfiniteTimeSpan);
            }
        }

        private void AutomaticCheck(object unused)
        {
            lock (_timerSync) { if (_disposed || !_automatic) return; }
            _ = CheckAsync();
        }

        public Task CheckAsync() { return Task.Run(CheckInBackgroundAsync); }

        private async Task CheckInBackgroundAsync()
        {
            if (_disposed || Interlocked.CompareExchange(ref _operation, 1, 0) != 0) return;
            UpdateState previous = State;
            if (previous.RestartRequired) { Interlocked.Exchange(ref _operation, 0); return; }
            try
            {
                Publish(new UpdateState(previous.Release, "Checking GitHub...", previous.LastChecked, true, false));
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    using (HttpResponseMessage response = await _http.GetAsync(ReleaseInfo.LatestApiUrl, timeout.Token).ConfigureAwait(false))
                    {
                        ReleaseInfo release = null;
                        if (response.StatusCode != HttpStatusCode.NotFound)
                        {
                            response.EnsureSuccessStatusCode();
                            release = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false), _installedVersion);
                        }
                        string status = release == null ? "No stable release has been published yet." :
                            release.IsNewer ? "Update available: v" + _installedVersion + " → v" + release.Version :
                            "Up to date. Latest stable release: v" + release.Version + ".";
                        Publish(new UpdateState(release, status, DateTime.Now, false, false));
                        Log.Info("Update check: " + status);
                    }
                }
            }
            catch (Exception ex)
            {
                string detail = ex is OperationCanceledException ? "The request timed out." : ex.Message;
                Publish(new UpdateState(previous.Release, "Could not check for updates. " + detail, previous.LastChecked, false, false));
                if (!_disposed) Log.Info("Update check failed: " + detail);
            }
            finally { Interlocked.Exchange(ref _operation, 0); }
        }

        public Task InstallAsync() { return Task.Run(InstallInBackgroundAsync); }

        private async Task InstallInBackgroundAsync()
        {
            if (_disposed || Interlocked.CompareExchange(ref _operation, 1, 0) != 0) return;
            UpdateState previous = State;
            try
            {
                if (previous.RestartRequired || previous.Release == null || !previous.Release.CanInstall) return;
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    await UpdateInstaller.InstallAsync(_http, previous.Release, _dllPath,
                        status => Publish(new UpdateState(previous.Release, status, previous.LastChecked, true, false)),
                        timeout.Token).ConfigureAwait(false);
                }
                Publish(new UpdateState(previous.Release, "Update installed — restart SimHub to load v" + previous.Release.Version + ".",
                    previous.LastChecked, false, true));
                Log.Info("Update installed: v" + previous.Release.Version + "; restart required.");
            }
            catch (Exception ex)
            {
                string detail = ex is UnauthorizedAccessException ? "SimHub cannot write to its plugin folder. Run SimHub as administrator or install the release manually." :
                    ex is OperationCanceledException ? "The download timed out." : ex.Message;
                Publish(new UpdateState(previous.Release, "Could not install update. " + detail, previous.LastChecked, false, false));
                if (!_disposed) Log.Info("Update install failed: " + detail);
            }
            finally { Interlocked.Exchange(ref _operation, 0); }
        }

        private void Publish(UpdateState state)
        {
            if (_disposed) return;
            _state = state;
            try { Changed?.Invoke(); }
            catch (Exception ex) { Log.Info("Update display notification failed: " + ex.Message); }
        }

        public void Dispose()
        {
            lock (_timerSync)
            {
                if (_disposed) return;
                _disposed = true;
                _timer.Dispose();
                _shutdown.Cancel();
                _http.Dispose();
            }
        }
    }
}
