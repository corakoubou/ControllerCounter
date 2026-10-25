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
            filters.Controls.Add(new Label { Text = "時間行の間隔(px)", AutoSize = true });
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
            historyChart = new HistoryChartPanel { Dock = DockStyle.Fill, BackColor = Color.White };
            historyChart.Paint += PaintHistoryChart;
            historyChart.ClientSizeChanged += (s, e) => historyChart.Invalidate();
            decimal previousSpacing = chartSpacing.Value;
            chartSpacing.ValueChanged += (s, e) =>
            {
                // Preserve the topmost time position when changing the distance between buckets.
                int offset = (int)(historyChart.ScrollOffset / previousSpacing * chartSpacing.Value);
                previousSpacing = chartSpacing.Value;
                LayoutHistoryChart(false);
                historyChart.ScrollOffset = offset;
                historyChart.Invalidate();
            };
            historyMetric = HistoryChoice(MetricNames, 0);
            historyChartType = HistoryChoice(new[] { "折れ線グラフ", "横棒グラフ" }, 1);
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
                RefreshHistory(false);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                historyStatus.Text = "記録の保存に失敗しました。次回に再試行します: " + ex.Message;
                return false;
            }
        }

        private void RefreshHistory(bool resetScroll = true)
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
                if (chartRecords.Count > 0)
                {
                    chartFrom = chartRecords[0].Time;
                    chartTo = chartRecords[chartRecords.Count - 1].Time.Add(chartUnit);
                }
                historyGrid.Rows.Clear();
                foreach (var r in chartRecords)
                    historyGrid.Rows.Add(r.Time.ToString("MM/dd HH:mm"), r.Values[0], r.Values[1], r.Values[2], r.Values[3],
                        (r.Values[4] / 96 * 0.0254).ToString("F2"), (r.Values[5] / 96 * 0.0254).ToString("F2"), r.TotalMeters.ToString("F2"));
                var totals = Enumerable.Range(0, 6).Select(i => records.Sum(r => r.Values[i])).ToArray();
                historyStatus.Text = string.Format("期間合計: キー {0:N0} / 左 {1:N0} / 右 {2:N0} / ホイール {3:N0} / 移動 {4:F2}m / ドラッグ {5:F2}m / 総距離 {6:F2}m\n30秒ごとに自動保存（終了時も保存）。記録 {7:N0}件、読めない行 {8}件。空白の期間は記録なし。",
                    totals[0], totals[1], totals[2], totals[3], totals[4] / 96 * 0.0254, totals[5] / 96 * 0.0254,
                    records.Sum(r => r.TotalMeters), records.Count, invalid);
                historyDirty = false;
                LayoutHistoryChart(resetScroll);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                historyStatus.Text = "履歴の読み込みに失敗しました: " + ex.Message;
            }
        }

        private void LayoutHistoryChart(bool resetScroll)
        {
            if (historyChart == null || chartUnit.Ticks <= 0) return;
            int height = chartRecords.Count == 0 ? 0 :
                (int)Math.Ceiling(HistoryTimeline.GetOffset(chartTo, chartFrom, chartUnit, (double)chartSpacing.Value)) + 75;
            historyChart.ContentHeight = height;
            if (resetScroll) historyChart.ScrollOffset = 0;
            historyChart.Invalidate();
        }

        private sealed class HistoryChartPanel : Panel
        {
            private readonly VScrollBar scrollBar;
            private int contentHeight;

            public HistoryChartPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                // AutoScroll moves existing pixels, including our fixed title and axes.
                // Use an explicit scrollbar so every scroll redraws from data instead.
                AutoScroll = false;
                scrollBar = new VScrollBar { Dock = DockStyle.Right, Visible = false };
                scrollBar.ValueChanged += (s, e) => Invalidate();
                Controls.Add(scrollBar);
            }

            public int PlotClientWidth { get { return ClientSize.Width - (scrollBar.Visible ? scrollBar.Width : 0); } }
            public int ScrollOffset
            {
                get { return scrollBar.Value; }
                set { scrollBar.Value = Math.Max(0, Math.Min(value, Math.Max(0, contentHeight - ClientSize.Height))); }
            }
            public int ContentHeight
            {
                get { return contentHeight; }
                set { contentHeight = Math.Max(0, value); UpdateScrollRange(); }
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (scrollBar != null) UpdateScrollRange();
            }

            private void UpdateScrollRange()
            {
                int offset = Math.Min(ScrollOffset, Math.Max(0, contentHeight - ClientSize.Height));
                scrollBar.Minimum = 0;
                scrollBar.Maximum = Math.Max(0, contentHeight - 1);
                scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
                scrollBar.SmallChange = 20;
                scrollBar.Value = offset;
                scrollBar.Visible = contentHeight > ClientSize.Height;
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int lines = SystemInformation.MouseWheelScrollLines;
                int step = lines < 0 ? Math.Max(1, ClientSize.Height) : Math.Max(1, lines) * scrollBar.SmallChange;
                ScrollOffset -= e.Delta / SystemInformation.MouseWheelScrollDelta * step;
            }
        }

        private void PaintHistoryChart(object sender, PaintEventArgs e)
        {
            int metric = historyMetric.SelectedIndex;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawString(MetricNames[metric] + " （縦軸: 時刻・上から古い順 / 集計単位: " + historyUnit.Text + "）", Font, Brushes.Black, 8, 4);
            if (chartRecords.Count == 0)
            {
                e.Graphics.DrawString("この期間の記録はありません", Font, Brushes.Gray, 20, 35);
                return;
            }
            Func<HistoryRecord, double> value = r => metric == 6 ? r.TotalMeters : metric >= 4 ? r.Values[metric] / 96 * 0.0254 : r.Values[metric];
            double max = Math.Max(1, chartRecords.Max(value));
            var area = new Rectangle(115, 32, Math.Max(1, historyChart.PlotClientWidth - 145), Math.Max(10, historyChart.ClientSize.Height - 62));
            double spacing = (double)chartSpacing.Value;
            int scroll = historyChart.ScrollOffset;
            using (var gridPen = new Pen(Color.LightGray))
            using (var linePen = new Pen(Color.SteelBlue, 2))
            {
                // The value axis stays fixed while the time rows scroll vertically.
                for (int i = 0; i <= 4; i++)
                {
                    float x = area.Left + area.Width * i / 4f;
                    e.Graphics.DrawLine(gridPen, x, area.Top, x, area.Bottom);
                    string label = (max * i / 4).ToString("0.##");
                    e.Graphics.DrawString(label, Font, Brushes.Gray, x - e.Graphics.MeasureString(label, Font).Width / 2, area.Bottom + 4);
                }
                var clip = e.Graphics.Save();
                // Preserve the dirty-region clip supplied by WinForms. Replacing it can
                // copy unpainted pixels from the partial double buffer into other areas.
                e.Graphics.SetClip(new Rectangle(0, area.Top, area.Right + 1, area.Height), CombineMode.Intersect);
                int labelStep = Math.Max(1, (int)Math.Ceiling((Font.Height + 6) / spacing));
                int firstLabel = Math.Max(0, (int)(scroll / spacing) / labelStep * labelStep);
                for (int bucket = firstLabel; ; bucket += labelStep)
                {
                    float y = area.Top + (float)((bucket + 0.5) * spacing - scroll);
                    if (y > area.Bottom) break;
                    DateTimeOffset time = chartFrom.AddTicks(bucket * chartUnit.Ticks);
                    if (time >= chartTo) break;
                    e.Graphics.DrawString(time.ToLocalTime().ToString("MM/dd HH:mm"), Font, Brushes.Gray, 8, y - Font.Height / 2f);
                }
                e.Graphics.SetClip(area, CombineMode.Intersect);
                PointF? previous = null;
                HistoryRecord previousRecord = null;
                foreach (var record in chartRecords)
                {
                    var point = new PointF(area.Left + (float)(value(record) / max * area.Width),
                        area.Top + (float)(HistoryTimeline.GetOffset(record.Time, chartFrom, chartUnit, spacing) + spacing / 2 - scroll));
                    if (historyChartType.SelectedIndex == 1)
                    {
                        float barHeight = (float)Math.Max(2, Math.Min(60, spacing * 0.65));
                        if (point.Y + barHeight / 2 >= area.Top && point.Y - barHeight / 2 <= area.Bottom && value(record) > 0)
                            e.Graphics.FillRectangle(Brushes.SteelBlue, area.Left, point.Y - barHeight / 2, point.X - area.Left, barHeight);
                    }
                    else
                    {
                        // Only consecutive measured buckets are connected; gaps remain unmeasured.
                        if (previous.HasValue && record.Time - previousRecord.Time == chartUnit &&
                            point.Y >= area.Top && previous.Value.Y <= area.Bottom)
                            e.Graphics.DrawLine(linePen, previous.Value, point);
                        if (point.Y >= area.Top - 2 && point.Y <= area.Bottom + 2)
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
