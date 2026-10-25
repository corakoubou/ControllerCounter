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
        private ComboBox historyRange, historyUnit, historyMetric, historyChartType, historyValueMode;
        private HistoryGrid historyGrid;
        private Label historyStatus;
        private HistoryChartPanel historyChart;
        private NumericUpDown chartSpacing;
        private DateTimeOffset chartFrom, chartTo;
        private TimeSpan chartUnit;
        private static readonly string[] MetricNames = { "キー入力", "左クリック", "右クリック", "ホイール", "マウス移動 (m)", "ドラッグ (m)", "総入力距離 (m)" };
        private List<HistoryRecord> chartRecords = new List<HistoryRecord>();
        private HistoryChartData chartData = new HistoryChartData(new HistoryRecord[0], TimeSpan.FromMinutes(1));
        private bool historyDirty = true;

        private double[] CurrentCounters()
        {
            return new double[] { keyCount, leftClickCount, rightClickCount, wheelCount, mouseMoveDistance, dragDistance };
        }

        private void SetupHistoryUi(TabPage page)
        {
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, AutoSize = true };
            historyRange = HistoryChoice(new[] { "過去10分", "過去1時間", "過去6時間", "過去24時間", "過去7日", "過去30日" }, 3);
            historyUnit = HistoryChoice(new[] { "1分", "5分", "15分", "30分", "1時間", "3時間", "5時間", "8時間", "1日" }, 0);
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
            historyGrid = new HistoryGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, VirtualMode = true, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            foreach (string title in new[] { "期間開始", "キー", "左クリック", "右クリック", "ホイール", "移動(m)", "ドラッグ(m)", "総距離(m)" })
                historyGrid.Columns.Add(title, title);
            historyGrid.Columns[0].FillWeight = 180;
            historyGrid.CellValueNeeded += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= chartRecords.Count) return;
                var row = chartRecords[e.RowIndex];
                e.Value = e.ColumnIndex == 0 ? (object)row.Time.ToString("MM/dd HH:mm") :
                    e.ColumnIndex <= 4 ? (object)row.Values[e.ColumnIndex - 1] :
                    e.ColumnIndex == 7 ? row.TotalMeters.ToString("F2") :
                    (row.Values[e.ColumnIndex - 1] / 96 * 0.0254).ToString("F2");
            };
            historyChart = new HistoryChartPanel { Dock = DockStyle.Fill, BackColor = Color.White };
            historyChart.Paint += PaintHistoryChart;
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
            historyChartType = HistoryChoice(new[] { "折れ線グラフ", "横棒グラフ", "縦棒グラフ" }, 1);
            automaticValueAxis = new CheckBox { Text = "数値目盛り: 自動", Checked = true, AutoSize = true };
            valueAxisStep = new NumericUpDown { DecimalPlaces = 3, Minimum = 0.001m, Maximum = 1000000000m,
                Value = 1, Increment = 1, Width = 110, Enabled = false };
            historyMetric.SelectedIndexChanged += (s, e) => { ApplyMetricAxisPreferences(); historyChart.Invalidate(); };
            historyChartType.SelectedIndexChanged += (s, e) =>
            {
                historyChart.HorizontalTimeline = historyChartType.SelectedIndex == 2;
                LayoutHistoryChart(true);
            };
            historyValueMode = HistoryChoice(new[] { "区間の量", "期間内の累計" }, 0);
            historyValueMode.SelectedIndexChanged += (s, e) => historyChart.Invalidate();
            var graphOptions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
            graphOptions.Controls.Add(valueAxisStep);
            graphOptions.Controls.Add(new Label { Text = "数値の刻み", AutoSize = true });
            graphOptions.Controls.Add(automaticValueAxis);
            graphOptions.Controls.Add(historyValueMode);
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
                columnChecks.Add(check);
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
            preferencesStatus = new Label { Dock = DockStyle.Bottom, Height = 22, AutoEllipsis = true, Visible = false, ForeColor = Color.Firebrick };
            page.Controls.Add(preferencesStatus);
            historyRange.SelectedIndexChanged += (s, e) => RefreshHistory();
            historyUnit.SelectedIndexChanged += (s, e) => RefreshHistory();
            InitializeViewPreferences();
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
            if (applyingPreferences) return;
            if (isInteractiveResize)
            {
                historyRefreshPending = true;
                historyResetScrollPending |= resetScroll;
                return;
            }
            if (historyGrid == null) return;
            try
            {
                var now = DateTimeOffset.Now;
                var spans = new[] { TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30) };
                var units = new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromHours(5), TimeSpan.FromHours(8), TimeSpan.FromDays(1) };
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
                chartData = new HistoryChartData(chartRecords, chartUnit);
                historyGrid.RowCount = chartRecords.Count;
                historyGrid.Invalidate();
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
            historyChart.ContentLength = height + (historyChart.HorizontalTimeline && height > 0 ? 70 : 0);
            if (resetScroll) historyChart.ScrollOffset = 0;
            historyChart.Invalidate();
        }

        private sealed class HistoryGrid : DataGridView
        {
            public bool DrawingSuspended { get; set; }
            protected override void OnPaint(PaintEventArgs e) { if (!DrawingSuspended) base.OnPaint(e); }
            protected override void OnPaintBackground(PaintEventArgs e) { if (!DrawingSuspended) base.OnPaintBackground(e); }
        }

        private sealed class HistoryChartPanel : Panel
        {
            public bool DrawingSuspended { get; set; }
            protected override void OnPaint(PaintEventArgs e) { if (!DrawingSuspended) base.OnPaint(e); }
            protected override void OnPaintBackground(PaintEventArgs e) { if (!DrawingSuspended) base.OnPaintBackground(e); }
            private readonly VScrollBar verticalBar;
            private readonly HScrollBar horizontalBar;
            private int contentLength;
            private bool horizontalTimeline;
            private ScrollBar ActiveBar { get { return horizontalTimeline ? (ScrollBar)horizontalBar : verticalBar; } }
            private int ViewportLength { get { return horizontalTimeline ? ClientSize.Width : ClientSize.Height; } }

            public HistoryChartPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                AutoScroll = false; // Native AutoScroll would move the fixed title and axes.
                verticalBar = new VScrollBar { Dock = DockStyle.Right, Visible = false };
                horizontalBar = new HScrollBar { Dock = DockStyle.Bottom, Visible = false };
                verticalBar.ValueChanged += (s, e) => Invalidate();
                horizontalBar.ValueChanged += (s, e) => Invalidate();
                Controls.Add(verticalBar);
                Controls.Add(horizontalBar);
            }

            public bool HorizontalTimeline
            {
                get { return horizontalTimeline; }
                set { if (horizontalTimeline == value) return; horizontalTimeline = value; UpdateScrollRange(); ScrollOffset = 0; }
            }
            public int PlotClientWidth { get { return ClientSize.Width - (verticalBar.Visible ? verticalBar.Width : 0); } }
            public int PlotClientHeight { get { return ClientSize.Height - (horizontalBar.Visible ? horizontalBar.Height : 0); } }
            public int ScrollOffset
            {
                get { return ActiveBar.Value; }
                set { ActiveBar.Value = Math.Max(0, Math.Min(value, Math.Max(0, contentLength - ViewportLength))); }
            }
            public int ContentLength
            {
                get { return contentLength; }
                set { if (contentLength == value) return; contentLength = Math.Max(0, value); UpdateScrollRange(); }
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (verticalBar != null && horizontalBar != null) UpdateScrollRange();
            }

            private void UpdateScrollRange()
            {
                var bar = ActiveBar;
                int offset = Math.Min(bar.Value, Math.Max(0, contentLength - ViewportLength));
                // Set only changed properties to avoid repeated scrollbar/layout updates on resize.
                int maximum = Math.Max(0, contentLength - 1);
                if (bar.Maximum != maximum) bar.Maximum = maximum;
                int page = Math.Max(1, ViewportLength);
                if (bar.LargeChange != page) bar.LargeChange = page;
                if (bar.SmallChange != 20) bar.SmallChange = 20;
                if (bar.Value != offset) bar.Value = offset;
                verticalBar.Visible = !horizontalTimeline && contentLength > ViewportLength;
                horizontalBar.Visible = horizontalTimeline && contentLength > ViewportLength;
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines == 0) return;
                int step = lines < 0 ? Math.Max(1, ViewportLength) : lines * ActiveBar.SmallChange;
                ScrollOffset -= e.Delta / SystemInformation.MouseWheelScrollDelta * step;
            }
        }

        private void PaintHistoryChart(object sender, PaintEventArgs e)
        {
            if (isInteractiveResize) return;
            int metric = historyMetric.SelectedIndex;
            bool cumulative = historyValueMode.SelectedIndex == 1;
            bool verticalBars = historyChartType.SelectedIndex == 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            string axes = verticalBars ? "横軸: 時刻・左から古い順" : "縦軸: 時刻・上から古い順";
            string heading = MetricNames[metric] + " / " + historyValueMode.Text + " （" + axes + " / " + historyUnit.Text + "）";
            if (chartData.Count == 0)
            {
                e.Graphics.DrawString(heading, Font, Brushes.Black, 8, 4);
                e.Graphics.DrawString("この期間の記録はありません", Font, Brushes.Gray, 20, 35);
                return;
            }
            var area = new Rectangle(115, 32, Math.Max(1, historyChart.PlotClientWidth - 145), Math.Max(10, historyChart.PlotClientHeight - 62));
            var axis = ChartValueAxis.Create(chartData.Maximum(metric, cumulative), automaticValueAxis.Checked,
                valueAxisStep.Value, verticalBars ? area.Height : area.Width);
            double max = axis.Maximum;
            e.Graphics.DrawString(heading + (axis.IsThinned ? " / 数値目盛りを間引き表示" : ""), Font, Brushes.Black, 8, 4);
            double spacing = (double)chartSpacing.Value;
            int scroll = historyChart.ScrollOffset;
            int timeLength = verticalBars ? area.Width : area.Height;
            using (var gridPen = new Pen(Color.LightGray))
            using (var linePen = new Pen(Color.SteelBlue, 2))
            {
                float previousLabel = float.NegativeInfinity;
                foreach (double tick in axis.Ticks)
                {
                    string label = tick.ToString(automaticValueAxis.Checked ? "0.#" : "0.########");
                    float position = (float)(tick / max * (verticalBars ? area.Height : area.Width));
                    if (verticalBars)
                    {
                        float y = area.Bottom - position;
                        e.Graphics.DrawLine(gridPen, area.Left, y, area.Right, y);
                        if (position - previousLabel >= Font.Height + 4)
                        {
                            e.Graphics.DrawString(label, Font, Brushes.Gray, 8, y - Font.Height / 2f);
                            previousLabel = position;
                        }
                    }
                    else
                    {
                        float x = area.Left + position;
                        e.Graphics.DrawLine(gridPen, x, area.Top, x, area.Bottom);
                        float labelWidth = e.Graphics.MeasureString(label, Font).Width;
                        if (position - labelWidth / 2 >= previousLabel + 4)
                        {
                            e.Graphics.DrawString(label, Font, Brushes.Gray, x - labelWidth / 2, area.Bottom + 4);
                            previousLabel = position + labelWidth / 2;
                        }
                    }
                }
                var clip = e.Graphics.Save();
                e.Graphics.SetClip(verticalBars ? new Rectangle(area.Left, area.Top, area.Width, area.Height + 28) :
                    new Rectangle(0, area.Top, area.Right + 1, area.Height), CombineMode.Intersect);
                int labelStep = Math.Max(1, (int)Math.Ceiling((verticalBars ? 110 : Font.Height + 6) / spacing));
                int firstLabel = Math.Max(0, (int)(scroll / spacing) / labelStep * labelStep);
                for (int bucket = firstLabel; ; bucket += labelStep)
                {
                    float position = (float)((bucket + 0.5) * spacing - scroll);
                    if (position > timeLength) break;
                    DateTimeOffset time = chartFrom.AddTicks(bucket * chartUnit.Ticks);
                    if (time >= chartTo) break;
                    e.Graphics.DrawString(time.ToLocalTime().ToString("MM/dd HH:mm"), Font, Brushes.Gray,
                        verticalBars ? area.Left + position : 8, verticalBars ? area.Bottom + 4 : area.Top + position - Font.Height / 2f);
                }
                e.Graphics.SetClip(area, CombineMode.Intersect);
                int first, end;
                chartData.VisibleRange(scroll / spacing - 1, (scroll + timeLength) / spacing + 1, out first, out end);
                PointF? previous = null;
                int previousRow = -1;
                for (int row = first; row < end; row++)
                {
                    double value = chartData.Value(row, metric, cumulative);
                    float timePosition = (float)((chartData.Buckets[row] + 0.5) * spacing - scroll);
                    var point = verticalBars ? new PointF(area.Left + timePosition, area.Bottom - (float)(value / max * area.Height)) :
                        new PointF(area.Left + (float)(value / max * area.Width), area.Top + timePosition);
                    if (historyChartType.SelectedIndex != 0)
                    {
                        float thickness = (float)Math.Max(2, Math.Min(60, spacing * 0.65));
                        if (value > 0)
                        {
                            if (verticalBars) e.Graphics.FillRectangle(Brushes.SteelBlue, point.X - thickness / 2, point.Y, thickness, area.Bottom - point.Y);
                            else e.Graphics.FillRectangle(Brushes.SteelBlue, area.Left, point.Y - thickness / 2, point.X - area.Left, thickness);
                        }
                    }
                    else
                    {
                        if (previous.HasValue && chartData.Times[row] - chartData.Times[previousRow] == chartUnit)
                            e.Graphics.DrawLine(linePen, previous.Value, point);
                        e.Graphics.FillEllipse(Brushes.SteelBlue, point.X - 2, point.Y - 2, 4, 4);
                    }
                    previous = point;
                    previousRow = row;
                }
                e.Graphics.Restore(clip);
            }
        }
    }
}
