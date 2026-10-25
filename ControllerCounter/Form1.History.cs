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
        private ComboBox historyRange, historyUnit;
        private DataGridView historyGrid;
        private Label historyStatus;
        private FlowLayoutPanel historyCharts;
        private readonly List<Panel> metricCharts = new List<Panel>();
        private NumericUpDown chartHeight, chartSpacing;
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
            chartHeight = new NumericUpDown { Minimum = 100, Maximum = 500, Value = 140, Increment = 20, Width = 60 };
            chartSpacing = new NumericUpDown { Minimum = 0, Maximum = 80, Value = 6, Increment = 2, Width = 60 };
            filters.Controls.Add(new Label { Text = "高さ(px)", AutoSize = true });
            filters.Controls.Add(chartHeight);
            filters.Controls.Add(new Label { Text = "間隔(px)", AutoSize = true });
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
            // A scrollable vertical stack keeps the metrics aligned on the same time axis.
            historyCharts = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Gainsboro };
            for (int i = 0; i < MetricNames.Length; i++)
            {
                var chart = new HistoryChartPanel { Height = (int)chartHeight.Value, BackColor = Color.White, Tag = i };
                chart.Paint += PaintHistoryChart;
                metricCharts.Add(chart);
                historyCharts.Controls.Add(chart);
            }
            historyCharts.ClientSizeChanged += (s, e) => LayoutHistoryCharts();
            chartHeight.ValueChanged += (s, e) => LayoutHistoryCharts();
            chartSpacing.ValueChanged += (s, e) => LayoutHistoryCharts();

            var visibility = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            visibility.Controls.Add(new Label { Text = "表示項目（グラフ・履歴表）", AutoSize = true });
            for (int i = 0; i < MetricNames.Length; i++)
            {
                int metric = i;
                var check = new CheckBox { Text = MetricNames[i], Checked = true, AutoSize = true };
                check.CheckedChanged += (s, e) =>
                {
                    historyGrid.Columns[metric + 1].Visible = check.Checked;
                    metricCharts[metric].Visible = check.Checked;
                    LayoutHistoryCharts();
                };
                visibility.Controls.Add(check);
            }
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                Size = new Size(900, 420), SplitterDistance = 240, Panel1MinSize = 80, Panel2MinSize = 60 };
            split.Panel1.Controls.Add(historyCharts);
            split.Panel2.Controls.Add(historyGrid);
            page.Controls.Add(split);
            page.Controls.Add(visibility);
            page.Controls.Add(filters);
            page.Controls.Add(historyStatus);
            historyRange.SelectedIndexChanged += (s, e) => RefreshHistory();
            historyUnit.SelectedIndexChanged += (s, e) => RefreshHistory();
            LayoutHistoryCharts();
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
                foreach (var chart in metricCharts) chart.Invalidate();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                historyStatus.Text = "履歴の読み込みに失敗しました: " + ex.Message;
            }
        }

        private void LayoutHistoryCharts()
        {
            if (historyCharts == null) return;
            historyCharts.SuspendLayout();
            int width = Math.Max(120, historyCharts.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            foreach (var chart in metricCharts)
            {
                chart.Size = new Size(width, (int)chartHeight.Value);
                chart.Margin = new Padding(0, 0, 0, (int)chartSpacing.Value);
                chart.Invalidate();
            }
            historyCharts.ResumeLayout();
        }

        private sealed class HistoryChartPanel : Panel
        {
            public HistoryChartPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        }

        private void PaintHistoryChart(object sender, PaintEventArgs e)
        {
            var chart = (Panel)sender;
            int metric = (int)chart.Tag;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawString(MetricNames[metric], Font, Brushes.Black, 8, 4);
            if (chartRecords.Count == 0)
            {
                e.Graphics.DrawString("この期間の記録はありません", Font, Brushes.Gray, 20, 35);
                return;
            }
            Func<HistoryRecord, double> value = r => metric == 6 ? r.TotalMeters : metric >= 4 ? r.Values[metric] / 96 * 0.0254 : r.Values[metric];
            double max = Math.Max(1, chartRecords.Max(value));
            var area = new Rectangle(85, 28, Math.Max(1, chart.ClientSize.Width - 110), Math.Max(20, chart.ClientSize.Height - 54));
            using (var gridPen = new Pen(Color.LightGray))
            using (var linePen = new Pen(Color.SteelBlue, 2))
            {
                for (int i = 0; i <= 2; i++)
                {
                    float y = area.Bottom - area.Height * i / 2f;
                    e.Graphics.DrawLine(gridPen, area.Left, y, area.Right, y);
                    e.Graphics.DrawString((max * i / 2).ToString("0.##"), Font, Brushes.Gray, 4, y - Font.Height / 2f);
                }
                PointF? previous = null;
                HistoryRecord previousRecord = null;
                double duration = Math.Max(1, (chartTo - chartFrom).TotalSeconds);
                var clip = e.Graphics.Save();
                e.Graphics.SetClip(new Rectangle(area.Left - 3, area.Top - 3, area.Width + 6, area.Height + 6));
                foreach (var record in chartRecords)
                {
                    var point = new PointF(area.Left + (float)((record.Time - chartFrom).TotalSeconds / duration * area.Width),
                        area.Bottom - (float)(value(record) / max * area.Height));
                    // Missing buckets are unmeasured: never draw a line across a gap.
                    if (previous.HasValue && record.Time.LocalDateTime.Ticks - previousRecord.Time.LocalDateTime.Ticks == chartUnit.Ticks)
                        e.Graphics.DrawLine(linePen, previous.Value, point);
                    e.Graphics.FillEllipse(Brushes.SteelBlue, point.X - 2, point.Y - 2, 4, 4);
                    previous = point;
                    previousRecord = record;
                }
                e.Graphics.Restore(clip);
            }
            e.Graphics.DrawString(chartFrom.ToString("MM/dd HH:mm"), Font, Brushes.Gray, area.Left, area.Bottom + 4);
            e.Graphics.DrawString(chartTo.ToString("MM/dd HH:mm"), Font, Brushes.Gray, Math.Max(area.Left, area.Right - 110), area.Bottom + 4);
        }
    }
}
