using System;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1
    {
        private bool isInteractiveResize;
        private bool historyRefreshPending;
        private bool historyResetScrollPending;

        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
            if (isInteractiveResize) return;
            isInteractiveResize = true;
            // Keep the top-level HWND visible: WM_SETREDRAW can remove WS_VISIBLE
            // and temporarily remove the taskbar button. Suppress managed painting
            // on the expensive child controls instead, and defer docking/layout.
            if (historyChart != null) historyChart.DrawingSuspended = true;
            if (historyGrid != null) historyGrid.DrawingSuspended = true;
            SuspendLayout();
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
                if (historyChart != null) historyChart.DrawingSuspended = false;
                if (historyGrid != null) historyGrid.DrawingSuspended = false;
                if (refresh && IsHandleCreated && !IsDisposed)
                {
                    Invalidate(true);
                    Update();
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

    }
}
