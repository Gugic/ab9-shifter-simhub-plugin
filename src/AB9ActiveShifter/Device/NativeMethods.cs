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
    }
}
