using System;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace AB9ActiveShifter.Device
{
    internal static class NativeMethods
    {
        /// <summary>
        /// Raises the system timer resolution to 1 ms. Without this, Thread.Sleep(1) can
        /// overshoot to ~15 ms and the FFB loop cannot hold its tick rate.
        /// </summary>
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        internal static extern uint TimeBeginPeriod(uint ms);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        internal static extern uint TimeEndPeriod(uint ms);

        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_ALL_ACCESS = 0x1F0003;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeWaitHandle CreateWaitableTimerExW(
            IntPtr securityAttributes, string name, uint flags, uint desiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(
            SafeWaitHandle timer, ref long dueTime, int period,
            IntPtr completionRoutine, IntPtr argToCompletionRoutine, bool resume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

        /// <summary>
        /// A high-resolution waitable timer (Windows 10 1803+), which can sleep for fractions
        /// of a millisecond without spinning. At a 1 kHz tick the alternative is either
        /// Thread.Sleep(1) - which overshoots the entire period - or burning a core in a spin
        /// wait. Returns null where unsupported; callers fall back to sleep-and-spin pacing.
        /// </summary>
        internal static SafeWaitHandle TryCreateHighResolutionTimer()
        {
            try
            {
                SafeWaitHandle handle = CreateWaitableTimerExW(
                    IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
                return handle == null || handle.IsInvalid ? null : handle;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Blocks for the given number of microseconds using the high-resolution timer.</summary>
        internal static bool WaitMicroseconds(SafeWaitHandle timer, long microseconds)
        {
            if (timer == null || timer.IsInvalid || microseconds <= 0) return false;

            // Negative due time = relative, in 100 ns units.
            long due = -(microseconds * 10);
            if (!SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) return false;

            return WaitForSingleObject(timer, 20) == 0;
        }
    }
}
