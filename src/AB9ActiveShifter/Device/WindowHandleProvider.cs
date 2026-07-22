using System;
using System.Windows;
using System.Windows.Interop;

namespace AB9ActiveShifter.Device
{
    /// <summary>
    /// Supplies the top-level window handle DirectInput needs for SetCooperativeLevel.
    ///
    /// SimHub's main window is used when it exists. It can be null when SimHub starts
    /// minimised to tray, so a hidden 1x1 top-level window is created as a fallback -
    /// DirectInput only needs a valid HWND, not a visible one.
    /// </summary>
    internal static class WindowHandleProvider
    {
        private static readonly object Sync = new object();
        private static IntPtr _cached = IntPtr.Zero;
        private static HwndSource _fallback;

        internal static IntPtr Get()
        {
            if (_cached != IntPtr.Zero) return _cached;

            lock (Sync)
            {
                if (_cached != IntPtr.Zero) return _cached;

                Application app = Application.Current;
                if (app == null)
                {
                    Log.Warn("No WPF application context; cannot obtain a window handle for DirectInput.");
                    return IntPtr.Zero;
                }

                try
                {
                    _cached = (IntPtr)app.Dispatcher.Invoke(new Func<IntPtr>(CreateHandle));
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to obtain a window handle for DirectInput", ex);
                    return IntPtr.Zero;
                }
            }

            return _cached;
        }

        /// <summary>Must run on the UI thread.</summary>
        private static IntPtr CreateHandle()
        {
            Window main = Application.Current.MainWindow;
            if (main != null)
            {
                IntPtr h = new WindowInteropHelper(main).EnsureHandle();
                if (h != IntPtr.Zero)
                {
                    Log.Info("Using SimHub main window for DirectInput cooperative level.");
                    return h;
                }
            }

            _fallback = new HwndSource(0, 0, 0, 0, 0, "AB9ActiveShifterFfb", IntPtr.Zero);
            Log.Info("SimHub main window unavailable; created a hidden window for DirectInput.");
            return _fallback.Handle;
        }
    }
}
