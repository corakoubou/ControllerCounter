// Windows-only regression harness. No input hooks, timers, or history files are used.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1 : Form
    {
        private int keyCount = 0, leftClickCount = 0, rightClickCount = 0, wheelCount = 0;
        private double mouseMoveDistance = 0, dragDistance = 0;
        private static int checks;

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
                    form.historyMetric = HistoryChoice(MetricNames, 0);
                    form.historyChartType = HistoryChoice(new[] { "Line", "Bar" }, 1);
                    form.historyUnit = HistoryChoice(new[] { "1 minute" }, 0);
                    form.chartSpacing = new NumericUpDown { Minimum = 4, Maximum = 200, Value = 4 };
                    form.chartUnit = TimeSpan.FromMinutes(1);
                    var start = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
                    form.chartFrom = start;
                    form.chartTo = start.AddMinutes(100);
                    for (int i = 0; i < 100; i++)
                        form.chartRecords.Add(new HistoryRecord { Time = start.AddMinutes(i),
                            Values = new double[] { i % 20 + 1, 0, 0, 0, 0, 0 } });
                    form.Show();
                    Application.DoEvents();
                    Check(!form.historyChart.AutoScroll, "Native pixel-moving AutoScroll must stay disabled");
                    foreach (int spacing in new[] { 40, 12, 8, 4, 200, 4 })
                    {
                        form.chartSpacing.Value = spacing;
                        form.LayoutHistoryChart(false);
                        form.historyChart.ScrollOffset = int.MaxValue;
                        Check(form.historyChart.ScrollOffset == Math.Max(0, form.historyChart.ContentHeight - form.historyChart.ClientSize.Height),
                            "Scroll offset is clamped after spacing changes");
                        foreach (int offset in new[] { 0, 37, form.historyChart.ScrollOffset })
                        {
                            form.historyChart.ScrollOffset = offset;
                            for (int type = 0; type < 2; type++)
                            {
                                form.historyChartType.SelectedIndex = type;
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
                    form.LayoutHistoryChart(true);
                    Check(form.historyChart.ScrollOffset == 0, "Range reset returns to oldest time");
                    form.chartRecords.Clear();
                    form.LayoutHistoryChart(false);
                    Check(form.historyChart.ContentHeight == 0 && form.historyChart.ScrollOffset == 0, "Empty data clears scroll range");
                    form.Close();
                }
                Console.WriteLine("PASS: " + checks + " Windows chart rendering checks");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
