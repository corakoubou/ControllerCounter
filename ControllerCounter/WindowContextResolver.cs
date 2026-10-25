using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace ControllerCounter
{
    internal sealed class WindowContextResolver
    {
        private sealed class CacheEntry
        {
            public uint ProcessId;
            public long Timestamp;
            public WindowIdentity Identity;
        }
        private readonly Dictionary<IntPtr, CacheEntry> cache = new Dictionary<IntPtr, CacheEntry>();
        public WindowIdentity Foreground() { return Resolve(GetForegroundWindow(), true); }
        public WindowIdentity AtPoint(int x, int y, bool refresh)
        {
            var window = WindowFromPoint(new NativePoint { X = x, Y = y });
            var root = GetAncestor(window, 2); // GA_ROOT: caption of the containing window.
            return Resolve(root == IntPtr.Zero ? window : root, refresh);
        }
        private WindowIdentity Resolve(IntPtr window, bool refresh)
        {
            if (window == IntPtr.Zero) return WindowIdentity.Unknown;
            long now = Stopwatch.GetTimestamp();
            CacheEntry previous;
            cache.TryGetValue(window, out previous);
            // High-frequency mouse moves reuse the caption for at most 100ms.
            if (!refresh && previous != null && now - previous.Timestamp < Stopwatch.Frequency / 10) return previous.Identity;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            string application = previous != null && previous.ProcessId == pid ? previous.Identity.Application : "";
            if (application.Length == 0 && pid != 0)
            {
                try { using (var process = Process.GetProcessById((int)pid)) application = process.ProcessName; }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is Win32Exception || ex is SecurityException) { }
            }
            // GetWindowText reads a foreign caption without messaging a hung application.
            var caption = new StringBuilder(2048);
            GetWindowText(window, caption, caption.Capacity);
            var identity = new WindowIdentity(application, caption.ToString());
            if (cache.Count >= 256) cache.Clear();
            cache[window] = new CacheEntry { ProcessId = pid, Timestamp = now, Identity = identity };
            return identity;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    }
}
