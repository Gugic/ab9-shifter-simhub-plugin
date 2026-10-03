using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AB9ActiveShifter.Updates
{
    /// <summary>
    /// Stages and validates a release before atomically replacing just the plugin DLL. Windows
    /// keeps the loaded image alive; the new DLL is used only after an explicit SimHub restart.
    /// </summary>
    public static class UpdateInstaller
    {
        public const string BackupSuffix = ".previous";

        public static async Task InstallAsync(HttpClient http, ReleaseInfo release, string dllPath,
            Action<string> status, CancellationToken cancellation)
        {
            if (release == null || !release.CanInstall) throw new InvalidOperationException("No installable update is available.");
            string backup = dllPath + BackupSuffix;
            if (File.Exists(backup)) throw new IOException("A previous update is pending. Restart SimHub first.");
            string staged = dllPath + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                status("Downloading v" + release.Version + "...");
                using (HttpResponseMessage response = await http.GetAsync(release.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength.HasValue &&
                        response.Content.Headers.ContentLength.Value != release.DownloadBytes)
                        throw new InvalidDataException("The download size does not match the release.");
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32768, true))
                    {
                        byte[] buffer = new byte[32768];
                        long total = 0;
                        int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
                        {
                            total += count;
                            if (total > release.DownloadBytes) throw new InvalidDataException("The download exceeds the release size.");
                            await output.WriteAsync(buffer, 0, count, cancellation).ConfigureAwait(false);
                        }
                        if (total != release.DownloadBytes) throw new InvalidDataException("The download is incomplete.");
                    }
                }

                cancellation.ThrowIfCancellationRequested();
                status("Verifying v" + release.Version + "...");
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(staged))
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (hash != release.Sha256) throw new InvalidDataException("The download checksum does not match GitHub's release asset.");
                }
                // Reads the assembly manifest without loading or executing downloaded code.
                AssemblyName assembly = AssemblyName.GetAssemblyName(staged);
                if (assembly.Name != "AB9ActiveShifter" || assembly.Version.ToString(3) != release.Version)
                    throw new InvalidDataException("The downloaded DLL has the wrong plugin name or version.");

                cancellation.ThrowIfCancellationRequested();
                status("Installing v" + release.Version + "...");
                // One atomic operation: a failed replacement leaves the original DLL in place.
                // Keep the previous image until the next process starts, never at a game change.
                File.Replace(staged, dllPath, backup);
            }
            finally
            {
                try { if (File.Exists(staged)) File.Delete(staged); }
                catch (Exception ex) { Log.Info("Could not remove update staging file: " + ex.Message); }
            }
        }

        public static void CleanupAfterRestart(string dllPath)
        {
            try
            {
                string backup = dllPath + BackupSuffix;
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch (Exception ex) { Log.Info("Could not remove previous update DLL: " + ex.Message); }
        }
    }
}
