using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1
    {
        private const int WM_SETREDRAW = 0x000B;
        private bool isInteractiveResize;
        private bool historyRefreshPending;
        private bool historyResetScrollPending;
        private IntPtr resizeRedrawHandle;

        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
            if (isInteractiveResize) return;
            isInteractiveResize = true;
            // Freeze docking/layout as well as painting. Suppressing Paint alone
            // would leave the table's column/layout work running on every WM_SIZE.
            SuspendLayout();
            if (IsHandleCreated)
            {
                resizeRedrawHandle = Handle;
                SendMessage(resizeRedrawHandle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            FinishInteractiveResize(true);
        }

        private void FinishInteractiveResize(bool refresh)
        {
            if (!isInteractiveResize) return;
            isInteractiveResize = false;
            try
            {
                ResumeLayout(refresh);
                if (refresh)
                {
                    if (historyRefreshPending)
                    {
                        bool resetScroll = historyResetScrollPending;
                        historyRefreshPending = historyResetScrollPending = false;
                        RefreshHistory(resetScroll);
                    }
                    UpdateDisplay();
                }
            }
            finally
            {
                if (resizeRedrawHandle != IntPtr.Zero)
                {
                    // Do not address a replacement HWND if the original was destroyed.
                    if (IsHandleCreated && Handle == resizeRedrawHandle)
                    {
                        SendMessage(resizeRedrawHandle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                        if (refresh)
                            RedrawWindow(resizeRedrawHandle, IntPtr.Zero, IntPtr.Zero,
                                0x0001 | 0x0004 | 0x0080 | 0x0100); // INVALIDATE | ERASE | ALLCHILDREN | UPDATENOW
                    }
                    resizeRedrawHandle = IntPtr.Zero;
                }
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            // Also release the layout suspension if the window closes or recreates
            // its handle before the resize gesture produces ResizeEnd.
            FinishInteractiveResize(false);
            base.OnHandleDestroyed(e);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr window, IntPtr updateRect, IntPtr updateRegion, uint flags);
    }
}
