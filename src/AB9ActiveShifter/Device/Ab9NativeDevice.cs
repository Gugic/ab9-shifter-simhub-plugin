using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using AB9ActiveShifter.Core;
using Microsoft.Win32;

namespace AB9ActiveShifter.Device
{
    /// <summary>
    /// Short-lived CDC sessions on a worker, wholly separate from the force loop. Discovery is
    /// exact VID/PID + usbser + a live COM name: never probe another stick, AB6 or wheelbase.
    /// A session re-checks firmware before writing, reads every write back, then releases the
    /// port. It never impersonates Pit House, creates effects, or updates firmware.
    /// </summary>
    public sealed class Ab9NativeDevice
    {
        private readonly SemaphoreSlim _serial = new SemaphoreSlim(1, 1);
        private volatile Ab9NativeSnapshot _snapshot = new Ab9NativeSnapshot("Checking for a compatible AB9...");
        private int _busy;
        public Ab9NativeSnapshot Snapshot { get { return _snapshot; } }
        public bool IsBusy { get { return Volatile.Read(ref _busy) != 0; } }

        public Task<Ab9NativeSnapshot> RefreshAsync()
        {
            return RunAsync(null, null);
        }

        public Task<Ab9NativeSnapshot> ApplyAsync(Ab9NativeSettings settings, int? inputMode)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            // Capture and validate before scheduling: slider changes cannot mutate an in-flight write.
            var copy = settings.Copy();
            copy.TransactionWrites(inputMode);
            return RunAsync(copy, inputMode);
        }

        private async Task<Ab9NativeSnapshot> RunAsync(Ab9NativeSettings settings, int? mode)
        {
            if (!await _serial.WaitAsync(0).ConfigureAwait(false))
                return new Ab9NativeSnapshot("AB9 configuration is busy; refresh and try again.");
            Interlocked.Exchange(ref _busy, 1);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        _snapshot = Execute(settings, mode);
                    }
                    catch (Exception ex)
                    {
                        _snapshot = new Ab9NativeSnapshot("AB9 native controls unavailable: " + ex.Message +
                            " Close Cockpit, Pit House and AZOM's AB9 connection, then refresh.");
                        Log.Warn(_snapshot.Status);
                    }
                    return Snapshot;
                }).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
                _serial.Release();
            }
        }

        private static Ab9NativeSnapshot Execute(Ab9NativeSettings settings, int? desiredMode)
        {
            string name = FindPort();
            if (name == null) return new Ab9NativeSnapshot("No AB9 connected. Generic virtual profiles work with other DirectInput FFB sticks.");
            using (var port = new SerialPort(name, 115200, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 75,
                WriteTimeout = 500,
                DtrEnable = false,
                RtsEnable = false
            })
            {
                port.Open();
                Query(port, Ab9NativeProtocol.Frame(0x09), f => f.Group == 0x89 && f.Device == 0x21
                    && f.Payload.Length == 2 && (f.Payload[0] != 0 || f.Payload[1] != 0));
                var versionFrame = Query(port, Ab9NativeProtocol.Frame(0x04, 0, 0, 0, 0),
                    f => f.Group == 0x84 && f.Device == 0x21 && f.Payload.Length == 4);
                Version firmware = Ab9NativeProtocol.FirmwareFromReply(versionFrame.Payload);
                int inputMode = Read(port, Ab9Parameter.InputMode);
                if (!Ab9NativeProtocol.IsSupported(Ab9NativeProtocol.VendorId, Ab9NativeProtocol.ProductId, firmware))
                    return new Ab9NativeSnapshot("AB9 firmware " + firmware + "; native controls require " +
                        Ab9NativeProtocol.MinimumFirmware + " or newer. Configure the base in MOZA's apps.",
                        name, firmware, inputMode);

                if (settings != null)
                {
                    if (!desiredMode.HasValue && inputMode != 1)
                        throw new InvalidOperationException("An AB9 native profile requires native shifter mode.");
                    // Hardware spring is active even without DirectInput effects. Mute torque
                    // before changing it, and restore the requested torque only after mode setup.
                    foreach (var value in settings.TransactionWrites(desiredMode)) WriteChecked(port, value.Key, value.Value);
                }

                var values = new Dictionary<Ab9Parameter, int>();
                foreach (var parameter in Ab9NativeSettings.ReadParameters) values[parameter] = Read(port, parameter);
                int finalMode = values[Ab9Parameter.InputMode];
                return new Ab9NativeSnapshot("AB9 on " + name + ", firmware " + firmware + ": " +
                    (finalMode == 1 ? "native shifter mode." : "flight mode (virtual gate)."),
                    name, firmware, finalMode, Ab9NativeSettings.FromValues(values), true);
            }
        }

        private static int Read(SerialPort port, Ab9Parameter parameter)
        {
            int value = 0;
            Query(port, Ab9NativeProtocol.Read(parameter), frame => Ab9NativeProtocol.TryValue(frame, parameter, out value));
            return value;
        }

        private static void WriteChecked(SerialPort port, Ab9Parameter parameter, int value)
        {
            byte[] request = Ab9NativeProtocol.Write(parameter, value);
            port.Write(request, 0, request.Length);
            Thread.Sleep(20);
            int actual = Read(port, parameter);
            if (actual != value) throw new InvalidOperationException(parameter + " did not read back as requested. " +
                "Requested " + value + ", read " + actual + ". Virtual forces remain off.");
        }

        private static Ab9Frame Query(SerialPort port, byte[] request, Func<Ab9Frame, bool> accepts)
        {
            port.DiscardInBuffer();
            port.Write(request, 0, request.Length);
            var reader = new Ab9FrameReader();
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 450)
            {
                try { reader.Add((byte)port.ReadByte()); }
                catch (TimeoutException) { continue; }
                Ab9Frame frame;
                while (reader.TryTake(out frame)) if (accepts(frame)) return frame;
            }
            throw new TimeoutException("The AB9 did not answer a configuration read.");
        }

        private static string FindPort()
        {
            var live = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB"))
            {
                if (usb == null) return null;
                foreach (string device in usb.GetSubKeyNames())
                {
                    if (!string.Equals(device, "VID_346E&PID_1000&MI_00", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var instances = usb.OpenSubKey(device))
                    {
                        if (instances == null) continue;
                        foreach (string instance in instances.GetSubKeyNames())
                        {
                            using (var key = instances.OpenSubKey(instance))
                            using (var parameters = key?.OpenSubKey("Device Parameters"))
                            {
                                if (!string.Equals(key?.GetValue("Service") as string, "usbser", StringComparison.OrdinalIgnoreCase)) continue;
                                string port = parameters?.GetValue("PortName") as string;
                                if (port != null && live.Contains(port)) found.Add(port);
                            }
                        }
                    }
                }
            }
            if (found.Count > 1) throw new InvalidOperationException("Multiple AB9 bases are connected; connect one for native setup.");
            foreach (string port in found) return port;
            return null;
        }
    }
}
