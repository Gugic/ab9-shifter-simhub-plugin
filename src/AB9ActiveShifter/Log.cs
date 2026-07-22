using System;
using System.Collections.Generic;

namespace AB9ActiveShifter
{
    /// <summary>
    /// Thin wrapper over SimHub's logger. Adds a tag so plugin lines are greppable in
    /// SimHub's log, and a rate limiter so a failure that repeats at the FFB tick rate
    /// cannot flood the log.
    /// </summary>
    internal static class Log
    {
        private const string Tag = "[AB9Shifter] ";

        private static readonly Dictionary<string, DateTime> LastLogged = new Dictionary<string, DateTime>();
        private static readonly object Sync = new object();

        internal static void Info(string message)
        {
            try { SimHub.Logging.Current.Info(Tag + message); }
            catch { /* logging must never take down the FFB loop */ }
        }

        internal static void Warn(string message)
        {
            try { SimHub.Logging.Current.Warn(Tag + message); }
            catch { }
        }

        internal static void Error(string message, Exception ex = null)
        {
            try
            {
                if (ex == null) SimHub.Logging.Current.Error(Tag + message);
                else SimHub.Logging.Current.Error(Tag + message + " :: " + ex.Message, ex);
            }
            catch { }
        }

        internal static void Debug(string message)
        {
            try { SimHub.Logging.Current.Debug(Tag + message); }
            catch { }
        }

        /// <summary>Logs at most once per <paramref name="seconds"/> for a given key.</summary>
        internal static void WarnThrottled(string key, string message, int seconds = 10)
        {
            if (!ShouldLog(key, seconds)) return;
            Warn(message);
        }

        internal static void ErrorThrottled(string key, string message, Exception ex = null, int seconds = 10)
        {
            if (!ShouldLog(key, seconds)) return;
            Error(message, ex);
        }

        private static bool ShouldLog(string key, int seconds)
        {
            DateTime now = DateTime.UtcNow;
            lock (Sync)
            {
                DateTime last;
                if (LastLogged.TryGetValue(key, out last) && (now - last).TotalSeconds < seconds)
                {
                    return false;
                }

                LastLogged[key] = now;
                return true;
            }
        }

        internal static void ResetThrottle(string key)
        {
            lock (Sync) { LastLogged.Remove(key); }
        }
    }
}
