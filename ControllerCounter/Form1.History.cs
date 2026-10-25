using System;
using System.Collections.Generic;
using System.Drawing;
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
        private ComboBox historyRange, historyUnit, historyMetric;
        private DataGridView historyGrid;
        private Label historyStatus;
        private Panel historyChart;
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
            historyMetric = HistoryChoice(new[] { "キー入力", "左クリック", "右クリック", "ホイール", "マウス移動 (m)", "ドラッグ (m)", "総入力距離 (m)" }, 0);
            filters.Controls.Add(new Label { Text = "期間", AutoSize = true });
            filters.Controls.Add(historyRange);
            filters.Controls.Add(new Label { Text = "集計単位", AutoSize = true });
            filters.Controls.Add(historyUnit);
            filters.Controls.Add(historyMetric);
            var refresh = new Button { Text = "更新", AutoSize = true };
            refresh.Click += (s, e) => RefreshHistory();
            filters.Controls.Add(refresh);
            historyStatus = new Label { Dock = DockStyle.Bottom, Height = 58, AutoEllipsis = true };
            historyGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            foreach (string title in new[] { "期間開始", "キー", "左クリック", "右クリック", "ホイール", "移動(m)", "ドラッグ(m)", "総距離(m)" })
                historyGrid.Columns.Add(title, title);
            historyGrid.Columns[0].FillWeight = 180;
            historyChart = new Panel { Dock = DockStyle.Top, Height = 170, BackColor = Color.White };
            historyChart.Paint += PaintHistoryChart;
            historyChart.Resize += (s, e) => historyChart.Invalidate();
            page.Controls.Add(historyGrid);
            page.Controls.Add(historyChart);
            page.Controls.Add(filters);
            page.Controls.Add(historyStatus);
            historyRange.SelectedIndexChanged += (s, e) => RefreshHistory();
            historyUnit.SelectedIndexChanged += (s, e) => RefreshHistory();
            historyMetric.SelectedIndexChanged += (s, e) => historyChart.Invalidate();
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
                chartRecords = HistoryStore.Aggregate(records, units[historyUnit.SelectedIndex]);
                historyGrid.Rows.Clear();
                foreach (var r in chartRecords)
                    historyGrid.Rows.Add(r.Time.ToString("MM/dd HH:mm"), r.Values[0], r.Values[1], r.Values[2], r.Values[3],
                        (r.Values[4] / 96 * 0.0254).ToString("F2"), (r.Values[5] / 96 * 0.0254).ToString("F2"), r.TotalMeters.ToString("F2"));
                var totals = Enumerable.Range(0, 6).Select(i => records.Sum(r => r.Values[i])).ToArray();
                historyStatus.Text = string.Format("期間合計: キー {0:N0} / 左 {1:N0} / 右 {2:N0} / ホイール {3:N0} / 移動 {4:F2}m / ドラッグ {5:F2}m / 総距離 {6:F2}m\n30秒ごとに自動保存（終了時も保存）。記録 {7:N0}件、読めない行 {8}件。空白の期間は記録なし。",
                    totals[0], totals[1], totals[2], totals[3], totals[4] / 96 * 0.0254, totals[5] / 96 * 0.0254,
                    records.Sum(r => r.TotalMeters), records.Count, invalid);
                historyDirty = false;
                historyChart.Invalidate();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                historyStatus.Text = "履歴の読み込みに失敗しました: " + ex.Message;
            }
        }

        private void PaintHistoryChart(object sender, PaintEventArgs e)
        {
            if (chartRecords.Count == 0) { e.Graphics.DrawString("この期間の記録はありません", Font, Brushes.Gray, 20, 30); return; }
            int metric = historyMetric.SelectedIndex;
            Func<HistoryRecord, double> value = r => metric == 6 ? r.TotalMeters : metric >= 4 ? r.Values[metric] / 96 * 0.0254 : r.Values[metric];
            double max = Math.Max(1, chartRecords.Max(value));
            var area = new Rectangle(70, 20, Math.Max(1, historyChart.Width - 100), 115);
            e.Graphics.DrawLine(Pens.Gray, area.Left, area.Bottom, area.Right, area.Bottom);
            e.Graphics.DrawString(max.ToString("0.##"), Font, Brushes.Gray, 5, area.Top);
            e.Graphics.DrawString("0", Font, Brushes.Gray, 35, area.Bottom - 12);
            long first = chartRecords.First().Time.UtcTicks;
            long last = chartRecords.Last().Time.UtcTicks;
            foreach (var r in chartRecords)
            {
                float x = area.Left + (last == first ? area.Width / 2f : (float)((r.Time.UtcTicks - first) / (double)(last - first) * (area.Width - 6)));
                float height = (float)(value(r) / max * area.Height);
                e.Graphics.FillRectangle(Brushes.SteelBlue, x, area.Bottom - height, 5, Math.Max(1, height));
            }
            e.Graphics.DrawString(chartRecords.First().Time.ToString("MM/dd HH:mm"), Font, Brushes.Gray, area.Left, area.Bottom + 4);
            e.Graphics.DrawString(chartRecords.Last().Time.ToString("MM/dd HH:mm"), Font, Brushes.Gray, Math.Max(area.Left, area.Right - 110), area.Bottom + 4);
        }
    }
}
