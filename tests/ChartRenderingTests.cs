// Windows-only regression harness. No input hooks, timers, or history files are used.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1 : Form
    {
        private int keyCount = 0, leftClickCount = 0, rightClickCount = 0, wheelCount = 0;
        private double mouseMoveDistance = 0, dragDistance = 0;
        private static int checks;
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);
        private void UpdateDisplay() { } // The harness has no live counter label.

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                using (var form = new Form1())
                {
                    form.ClientSize = new Size(480, 280);
                    form.historyChart = new HistoryChartPanel { Dock = DockStyle.Fill, BackColor = Color.White };
                    form.Controls.Add(form.historyChart);
                    form.historyChart.Paint += form.PaintHistoryChart;
                    form.automaticValueAxis = new CheckBox { Checked = true };
                    form.valueAxisStep = new NumericUpDown { DecimalPlaces = 3, Minimum = 0.001m, Maximum = 1000000000m, Value = 1 };
                    form.historyMetric = HistoryChoice(MetricNames, 0);
                    form.historyChartType = HistoryChoice(new[] { "Line", "Bar", "Vertical bar" }, 1);
                    form.historyValueMode = HistoryChoice(new[] { "Interval", "Cumulative" }, 0);
                    form.historyUnit = HistoryChoice(new[] { "1 minute" }, 0);
                    form.chartSpacing = new NumericUpDown { Minimum = 4, Maximum = 200, Value = 4 };
                    form.chartUnit = TimeSpan.FromMinutes(1);
                    var start = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
                    form.chartFrom = start;
                    form.chartTo = start.AddMinutes(100);
                    for (int i = 0; i < 100; i++)
                        form.chartRecords.Add(new HistoryRecord { Time = start.AddMinutes(i),
                            Values = new double[] { i % 20 + 1, 0, 0, 0, 0, 0 } });
                    form.chartData = new HistoryChartData(form.chartRecords, form.chartUnit);
                    form.Show();
                    Application.DoEvents();
                    Check(!form.historyChart.AutoScroll, "Native pixel-moving AutoScroll must stay disabled");
                    foreach (int spacing in new[] { 40, 12, 8, 4, 200, 4 })
                    {
                        form.chartSpacing.Value = spacing;
                        form.historyChart.HorizontalTimeline = false;
                        form.LayoutHistoryChart(false);
                        form.historyChart.ScrollOffset = int.MaxValue;
                        Check(form.historyChart.ScrollOffset == Math.Max(0, form.historyChart.ContentLength - form.historyChart.ClientSize.Height),
                            "Scroll offset is clamped after spacing changes");
                        foreach (int offset in new[] { 0, 37, form.historyChart.ScrollOffset })
                        {
                            form.historyChart.ScrollOffset = offset;
                            for (int type = 0; type < 3; type++)
                            for (int mode = 0; mode < 2; mode++)
                            {
                                form.historyChartType.SelectedIndex = type;
                                form.historyValueMode.SelectedIndex = mode;
                                form.historyChart.HorizontalTimeline = type == 2;
                                form.LayoutHistoryChart(false);
                                form.historyChart.ScrollOffset = offset;
                                // Simulate a small dirty region after scrolling/zooming. Outside
                                // pixels must remain untouched, including the fixed title and axes.
                                var dirty = new Rectangle(160, 95, 100, 35);
                                using (var frame = new Bitmap(480, 280))
                                using (var graphics = Graphics.FromImage(frame))
                                {
                                    graphics.Clear(Color.Magenta);
                                    graphics.SetClip(dirty);
                                    graphics.FillRectangle(Brushes.White, dirty);
                                    form.PaintHistoryChart(form.historyChart, new PaintEventArgs(graphics, dirty));
                                    for (int y = 0; y < frame.Height; y++)
                                        for (int x = 0; x < frame.Width; x++)
                                            if (!dirty.Contains(x, y) && frame.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb())
                                                throw new Exception("Partial paint escaped its dirty region at " + x + "," + y);
                                    checks++;
                                }
                            }
                        }
                    }
                    var originalHandle = form.Handle;
                    form.historyGrid = new HistoryGrid();
                    form.OnResizeBegin(EventArgs.Empty);
                    Check(form.isInteractiveResize, "Resize begins with painting suspended");
                    Check(form.Handle == originalHandle && IsWindowVisible(form.Handle), "Resizing keeps the top-level HWND visible");
                    Check(form.historyChart.DrawingSuspended && form.historyGrid.DrawingSuspended, "Heavy child controls suspend painting");
                    form.ClientSize = new Size(620, 340);
                    using (var frame = new Bitmap(620, 340))
                    using (var graphics = Graphics.FromImage(frame))
                    {
                        graphics.Clear(Color.Magenta);
                        form.PaintHistoryChart(form.historyChart, new PaintEventArgs(graphics, new Rectangle(0, 0, 620, 340)));
                        Check(frame.GetPixel(8, 8).ToArgb() == Color.Magenta.ToArgb(), "Chart skips painting during resize");
                    }
                    form.OnResizeEnd(EventArgs.Empty);
                    Check(!form.isInteractiveResize && !form.historyChart.DrawingSuspended && !form.historyGrid.DrawingSuspended,
                        "Resize end restores managed painting");
                    Check(form.Handle == originalHandle && IsWindowVisible(form.Handle), "Resize end preserves the same visible window");
                    form.historyGrid.Dispose();
                    Check(form.historyChart.ClientSize.Width == form.ClientSize.Width, "Deferred docking completes at resize end");
                    form.OnResizeEnd(EventArgs.Empty); // Safe if a duplicate completion event arrives.
                    form.LayoutHistoryChart(true);
                    Check(form.historyChart.ScrollOffset == 0, "Range reset returns to oldest time");
                    form.chartRecords.Clear();
                    form.chartData = new HistoryChartData(form.chartRecords, form.chartUnit);
                    form.LayoutHistoryChart(false);
                    Check(form.historyChart.ContentLength == 0 && form.historyChart.ScrollOffset == 0, "Empty data clears scroll range");
                    form.Close();
                }
                Console.WriteLine("PASS: " + checks + " Windows chart rendering checks");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
