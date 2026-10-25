using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1
    {
        private readonly HistoryStore historyStore = new HistoryStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerCounter", "History"));
        private Timer recordTimer;
        private double[] recordedCounters = new double[6];
        private ComboBox historyRange, historyUnit, historyMetric, historyChartType;
        private DataGridView historyGrid;
        private Label historyStatus;
        private HistoryChartPanel historyChart;
        private NumericUpDown chartSpacing;
        private DateTimeOffset chartFrom, chartTo;
        private TimeSpan chartUnit;
        private static readonly string[] MetricNames = { "キー入力", "左クリック", "右クリック", "ホイール", "マウス移動 (m)", "ドラッグ (m)", "総入力距離 (m)" };
        private List<HistoryRecord> chartRecords = new List<HistoryRecord>();
        private bool historyDirty = true;

        private double[] CurrentCounters()
        {
            return new double[] { keyCount, leftClickCount, rightClickCount, wheelCount, mouseMoveDistance, dragDistance };
        }

        private void SetupHistoryUi(TabPage page)
        {
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, AutoSize = true };
            historyRange = HistoryChoice(new[] { "過去10分", "過去1時間", "過去6時間", "過去24時間", "過去7日", "過去30日" }, 3);
            historyUnit = HistoryChoice(new[] { "1分", "5分", "1時間", "1日" }, 0);
            filters.Controls.Add(new Label { Text = "期間", AutoSize = true });
            filters.Controls.Add(historyRange);
            filters.Controls.Add(new Label { Text = "集計単位", AutoSize = true });
            filters.Controls.Add(historyUnit);
            chartSpacing = new NumericUpDown { Minimum = 4, Maximum = 200, Value = 40, Increment = 4, Width = 65 };
            filters.Controls.Add(new Label { Text = "時間目盛りの間隔(px)", AutoSize = true });
            filters.Controls.Add(chartSpacing);
            var refresh = new Button { Text = "更新", AutoSize = true };
            refresh.Click += (s, e) => RefreshHistory();
            filters.Controls.Add(refresh);
            historyStatus = new Label { Dock = DockStyle.Bottom, Height = 58, AutoEllipsis = true };
            historyGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            foreach (string title in new[] { "期間開始", "キー", "左クリック", "右クリック", "ホイール", "移動(m)", "ドラッグ(m)", "総距離(m)" })
                historyGrid.Columns.Add(title, title);
            historyGrid.Columns[0].FillWeight = 180;
            historyChart = new HistoryChartPanel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
            historyChart.Paint += PaintHistoryChart;
            historyChart.Scroll += (s, e) => historyChart.Invalidate();
            historyChart.ClientSizeChanged += (s, e) => historyChart.Invalidate();
            decimal previousSpacing = chartSpacing.Value;
            chartSpacing.ValueChanged += (s, e) =>
            {
                // Preserve the leftmost time position when changing the distance between buckets.
                int offset = (int)(-historyChart.AutoScrollPosition.X / previousSpacing * chartSpacing.Value);
                previousSpacing = chartSpacing.Value;
                LayoutHistoryChart(false);
                historyChart.AutoScrollPosition = new Point(offset, 0);
                historyChart.Invalidate();
            };
            historyMetric = HistoryChoice(MetricNames, 0);
            historyChartType = HistoryChoice(new[] { "折れ線グラフ", "棒グラフ" }, 0);
            historyMetric.SelectedIndexChanged += (s, e) => historyChart.Invalidate();
            historyChartType.SelectedIndexChanged += (s, e) => historyChart.Invalidate();
            var graphOptions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
            graphOptions.Controls.Add(historyChartType);
            graphOptions.Controls.Add(historyMetric);
            graphOptions.Controls.Add(new Label { Text = "グラフの項目・種類", AutoSize = true });

            var visibility = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            visibility.Controls.Add(new Label { Text = "履歴表の表示項目", AutoSize = true });
            for (int i = 0; i < MetricNames.Length; i++)
            {
                int metric = i;
                var check = new CheckBox { Text = MetricNames[i], Checked = true, AutoSize = true };
                check.CheckedChanged += (s, e) =>
                {
                    historyGrid.Columns[metric + 1].Visible = check.Checked;
                };
                visibility.Controls.Add(check);
            }
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                Size = new Size(900, 420), SplitterDistance = 240, Panel1MinSize = 80, Panel2MinSize = 60 };
            split.Panel1.Controls.Add(historyChart);
            split.Panel1.Controls.Add(graphOptions);
            split.Panel2.Controls.Add(historyGrid);
            page.Controls.Add(split);
            page.Controls.Add(visibility);
            page.Controls.Add(filters);
            page.Controls.Add(historyStatus);
            historyRange.SelectedIndexChanged += (s, e) => RefreshHistory();
            historyUnit.SelectedIndexChanged += (s, e) => RefreshHistory();
            LayoutHistoryChart(true);
            recordedCounters = CurrentCounters();
            recordTimer = new Timer { Interval = 30000 };
            recordTimer.Tick += (s, e) => SaveHistorySample();
            recordTimer.Start();
            RefreshHistory();
        }

        private static ComboBox HistoryChoice(string[] items, int selected)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            box.Items.AddRange(items);
            box.SelectedIndex = selected;
            return box;
        }

        private bool SaveHistorySample()
        {
            var current = CurrentCounters();
            var record = new HistoryRecord { Time = DateTimeOffset.Now,
                Values = current.Select((v, i) => Math.Max(0, v - recordedCounters[i])).ToArray() };
            try
            {
                historyStore.Append(record);
                recordedCounters = current;
                historyDirty = true;
                RefreshHistory();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                historyStatus.Text = "記録の保存に失敗しました。次回に再試行します: " + ex.Message;
                return false;
            }
        }

        private void RefreshHistory()
        {
            if (historyGrid == null) return;
            try
            {
                var now = DateTimeOffset.Now;
                var spans = new[] { TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30) };
                var units = new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromHours(1), TimeSpan.FromDays(1) };
                int invalid;
                var records = historyStore.Read(now - spans[historyRange.SelectedIndex], now, out invalid);
                chartUnit = units[historyUnit.SelectedIndex];
                chartTo = now;
                var rangeStart = (now - spans[historyRange.SelectedIndex]).LocalDateTime;
                chartFrom = new DateTimeOffset(new DateTime(rangeStart.Ticks / chartUnit.Ticks * chartUnit.Ticks, DateTimeKind.Local));
                chartRecords = HistoryStore.Aggregate(records, chartUnit);
                historyGrid.Rows.Clear();
                foreach (var r in chartRecords)
                    historyGrid.Rows.Add(r.Time.ToString("MM/dd HH:mm"), r.Values[0], r.Values[1], r.Values[2], r.Values[3],
                        (r.Values[4] / 96 * 0.0254).ToString("F2"), (r.Values[5] / 96 * 0.0254).ToString("F2"), r.TotalMeters.ToString("F2"));
                var totals = Enumerable.Range(0, 6).Select(i => records.Sum(r => r.Values[i])).ToArray();
                historyStatus.Text = string.Format("期間合計: キー {0:N0} / 左 {1:N0} / 右 {2:N0} / ホイール {3:N0} / 移動 {4:F2}m / ドラッグ {5:F2}m / 総距離 {6:F2}m\n30秒ごとに自動保存（終了時も保存）。記録 {7:N0}件、読めない行 {8}件。空白の期間は記録なし。",
                    totals[0], totals[1], totals[2], totals[3], totals[4] / 96 * 0.0254, totals[5] / 96 * 0.0254,
                    records.Sum(r => r.TotalMeters), records.Count, invalid);
                historyDirty = false;
                LayoutHistoryChart(true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                historyStatus.Text = "履歴の読み込みに失敗しました: " + ex.Message;
            }
        }

        private void LayoutHistoryChart(bool scrollToLatest)
        {
            if (historyChart == null || chartUnit.Ticks <= 0) return;
            int oldWidth = historyChart.AutoScrollMinSize.Width;
            int scroll = -historyChart.AutoScrollPosition.X;
            bool atEnd = oldWidth == 0 || scroll + historyChart.ClientSize.Width >= oldWidth - 4;
            int width = (int)Math.Ceiling(HistoryTimeline.GetX(chartTo, chartFrom, chartUnit, (double)chartSpacing.Value)) + 120;
            historyChart.AutoScrollMinSize = new Size(width, 0);
            if (scrollToLatest && atEnd)
                historyChart.AutoScrollPosition = new Point(Math.Max(0, width - historyChart.ClientSize.Width), 0);
            historyChart.Invalidate();
        }

        private sealed class HistoryChartPanel : Panel
        {
            public HistoryChartPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        }

        private void PaintHistoryChart(object sender, PaintEventArgs e)
        {
            int metric = historyMetric.SelectedIndex;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawString(MetricNames[metric] + " （横軸: 時刻 / 集計単位: " + historyUnit.Text + "）", Font, Brushes.Black, 8, 4);
            if (chartRecords.Count == 0)
            {
                e.Graphics.DrawString("この期間の記録はありません", Font, Brushes.Gray, 20, 35);
                return;
            }
            Func<HistoryRecord, double> value = r => metric == 6 ? r.TotalMeters : metric >= 4 ? r.Values[metric] / 96 * 0.0254 : r.Values[metric];
            double max = Math.Max(1, chartRecords.Max(value));
            var area = new Rectangle(85, 28, Math.Max(1, historyChart.ClientSize.Width - 110), Math.Max(10, historyChart.ClientSize.Height - 58));
            double spacing = (double)chartSpacing.Value;
            int scroll = -historyChart.AutoScrollPosition.X;
            using (var gridPen = new Pen(Color.LightGray))
            using (var linePen = new Pen(Color.SteelBlue, 2))
            {
                for (int i = 0; i <= 2; i++)
                {
                    float y = area.Bottom - area.Height * i / 2f;
                    e.Graphics.DrawLine(gridPen, area.Left, y, area.Right, y);
                    e.Graphics.DrawString((max * i / 2).ToString("0.##"), Font, Brushes.Gray, 4, y - Font.Height / 2f);
                }
                var clip = e.Graphics.Save();
                e.Graphics.SetClip(new Rectangle(area.Left, area.Top - 3, area.Width + 1, area.Height + 28));
                // Skip labels to avoid overlap when zoomed out, while keeping each bucket's spacing exact.
                int labelStep = Math.Max(1, (int)Math.Ceiling(110 / spacing));
                int firstLabel = Math.Max(0, (int)(scroll / spacing) / labelStep * labelStep);
                for (int bucket = firstLabel; ; bucket += labelStep)
                {
                    float x = area.Left + (float)(bucket * spacing - scroll);
                    if (x > area.Right) break;
                    DateTimeOffset time = chartFrom.AddTicks(bucket * chartUnit.Ticks);
                    if (time > chartTo) break;
                    e.Graphics.DrawLine(gridPen, x, area.Top, x, area.Bottom);
                    e.Graphics.DrawString(time.ToLocalTime().ToString("MM/dd HH:mm"), Font, Brushes.Gray, x, area.Bottom + 4);
                }
                e.Graphics.SetClip(area);
                PointF? previous = null;
                HistoryRecord previousRecord = null;
                foreach (var record in chartRecords)
                {
                    var point = new PointF(area.Left + (float)(HistoryTimeline.GetX(record.Time, chartFrom, chartUnit, spacing) - scroll),
                        area.Bottom - (float)(value(record) / max * area.Height));
                    if (historyChartType.SelectedIndex == 1)
                    {
                        float barWidth = (float)Math.Max(2, Math.Min(60, spacing * 0.65));
                        if (point.X + barWidth / 2 >= area.Left && point.X - barWidth / 2 <= area.Right)
                            e.Graphics.FillRectangle(Brushes.SteelBlue, point.X - barWidth / 2, point.Y, barWidth, Math.Max(1, area.Bottom - point.Y));
                    }
                    else
                    {
                        // Only consecutive measured buckets are connected; gaps remain unmeasured.
                        if (previous.HasValue && record.Time - previousRecord.Time == chartUnit &&
                            point.X >= area.Left && previous.Value.X <= area.Right)
                            e.Graphics.DrawLine(linePen, previous.Value, point);
                        if (point.X >= area.Left - 2 && point.X <= area.Right + 2)
                            e.Graphics.FillEllipse(Brushes.SteelBlue, point.X - 2, point.Y - 2, 4, 4);
                    }
                    previous = point;
                    previousRecord = record;
                }
                e.Graphics.Restore(clip);
            }
        }
    }
}
