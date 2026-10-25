using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ControllerCounter;

internal static class HistoryTests
{
    static int checks;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }
    static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ControllerCounterTests-" + Guid.NewGuid());
        try
        {
            var store = new HistoryStore(directory);
            var start = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
            int invalid;
            Check(store.Read(start, start.AddDays(2), out invalid).Count == 0, "Empty store");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            store.Append(new HistoryRecord { Time = start.AddSeconds(30), Values = new double[] { 2, 3, 4, 6, 96, 192 } });
            store.Append(new HistoryRecord { Time = start.AddSeconds(59), Values = new double[] { 5, 1, 0, 2, 48.5, 0 } });
            store.Append(new HistoryRecord { Time = start.AddMinutes(1), Values = new double[] { 1, 0, 0, 0, 0, 0 } });
            store.Append(new HistoryRecord { Time = start.AddDays(1), Values = new double[] { 10, 0, 0, 0, 0, 0 } });
            var reopened = new HistoryStore(directory);
            var records = reopened.Read(start, start.AddDays(2), out invalid);
            Check(records.Count == 4 && invalid == 0, "Persistence across reopen");
            Check(records[1].Values[4] == 48.5, "Invariant decimal round trip");
            Check(Directory.GetFiles(directory).Length == 2, "Daily rotation");
            var minutes = HistoryStore.Aggregate(records, TimeSpan.FromMinutes(1));
            Check(minutes.Count == 3 && minutes[0].Values[0] == 7, "Minute totals and boundary");
            Check(Math.Abs(records[0].TotalMeters - 12.0762) < 0.00001, "All metrics contribute to total distance");
            var hours = HistoryStore.Aggregate(records, TimeSpan.FromHours(1));
            Check(hours.Count == 2 && hours[0].Values[0] == 8, "Hourly aggregation");
            var days = HistoryStore.Aggregate(records, TimeSpan.FromDays(1));
            Check(days.Count == 2 && days.Sum(r => r.Values[0]) == 18, "Daily aggregation preserves totals");
            Check(reopened.Read(start.AddSeconds(59), start.AddMinutes(1), out invalid).Count == 2, "Time range bounds");
            File.AppendAllText(Directory.GetFiles(directory).First(), "broken,row\n2026-10-25T10:00:00.0000000+00:00,NaN,0,0,0,0,0\n");
            records = reopened.Read(start, start.AddDays(2), out invalid);
            Check(records.Count == 4 && invalid == 2, "Damaged records skipped and reported");
            Check(HistoryStore.Aggregate(new HistoryRecord[0], TimeSpan.FromDays(1)).Count == 0, "Empty aggregation");
            Check(HistoryTimeline.GetOffset(start.AddMinutes(1), start, TimeSpan.FromMinutes(1), 40) == 40,
                "One minute uses the configured pixel spacing");
            Check(HistoryTimeline.GetOffset(start.AddMinutes(2), start, TimeSpan.FromMinutes(1), 40) == 80,
                "Two minutes retain two bucket widths");
            Check(HistoryTimeline.GetOffset(start.AddMinutes(2), start, TimeSpan.FromMinutes(1), 100) == 200,
                "Zoom changes time spacing");
            Check(HistoryTimeline.GetOffset(start.AddMinutes(5), start, TimeSpan.FromMinutes(1), 40) == 200,
                "Missing buckets retain their time-axis width");
            Check(HistoryTimeline.GetOffset(start.AddHours(2), start, TimeSpan.FromHours(1), 60) == 120,
                "Hourly units use the same pixel spacing rule");
            Check(HistoryTimeline.GetOffset(start.AddDays(2), start, TimeSpan.FromDays(1), 60) == 120,
                "Daily units use the same pixel spacing rule");
            Check(HistoryTimeline.GetOffset(start.ToOffset(TimeSpan.FromHours(9)), start, TimeSpan.FromMinutes(1), 40) == 0,
                "Equivalent timestamps have the same position");
            Check(HistoryTimeline.GetOffset(start, start, TimeSpan.FromMinutes(1), 40) <
                HistoryTimeline.GetOffset(start.AddMinutes(1), start, TimeSpan.FromMinutes(1), 40),
                "Older time rows are above newer time rows");
            // The selected interval excludes the next day's sample. Cumulative values
            // start with this interval's activity, never previously recorded activity.
            var selected = reopened.Read(start.AddSeconds(59), start.AddMinutes(1), out invalid);
            var data = new HistoryChartData(HistoryStore.Aggregate(selected, TimeSpan.FromMinutes(1)), TimeSpan.FromMinutes(1));
            Check(data.Count == 2 && data.Value(0, 0, true) == 5 && data.Value(1, 0, true) == 6,
                "Cumulative values include only the selected period");
            Check(data.Value(1, 0, false) == 1 && data.Maximum(0, false) == 5 && data.Maximum(0, true) == 6,
                "Interval values remain unchanged when cumulative mode is available");
            Check(Math.Abs(data.Value(1, 6, true) - selected.Sum(r => r.TotalMeters)) < 0.000001,
                "Cumulative total distance includes all selected metrics");
            Check(Math.Abs(data.Value(1, 4, true) - 48.5 / 96 * 0.0254) < 0.000001,
                "Cumulative mouse distances retain meter conversion");
            var otherPeriod = new HistoryChartData(new[] { records.Last() }, TimeSpan.FromMinutes(1));
            Check(otherPeriod.Value(0, 0, true) == 10, "Changing the period resets cumulative values");
            var large = Enumerable.Range(0, 43200).Select(i => new HistoryRecord
            {
                Time = start.AddMinutes(i), Values = new double[] { 1, 0, 0, 0, 0, 0 }
            }).ToList();
            var largeData = new HistoryChartData(large, TimeSpan.FromMinutes(1));
            int first, end;
            largeData.VisibleRange(20000, 20060, out first, out end);
            Check(first == 19999 && end == 20062 && end - first == 63,
                "Thirty days of samples paint only visible rows and line endpoints");
            Check(largeData.Value(43200 - 1, 0, true) == 43200 && largeData.Maximum(0, true) == 43200,
                "Cumulative values and maximum are cached across the full period");
            largeData.VisibleRange(-10, 2, out first, out end);
            Check(first == 0 && end == 4, "Visible range clamps at the oldest sample");
            largeData.VisibleRange(43198, 44000, out first, out end);
            Check(first == 43197 && end == 43200, "Visible range clamps at the newest sample");
            var emptyData = new HistoryChartData(new HistoryRecord[0], TimeSpan.FromMinutes(1));
            emptyData.VisibleRange(0, 100, out first, out end);
            Check(first == 0 && end == 0 && emptyData.Maximum(0, true) == 0, "Empty cumulative series");
            var sparseData = new HistoryChartData(new[] { large[0], large[1000] }, TimeSpan.FromMinutes(1));
            sparseData.VisibleRange(400, 410, out first, out end);
            Check(first == 0 && end == 2 && sparseData.Buckets[1] == 1000,
                "Missing periods preserve spacing and both potential line endpoints");
            Console.WriteLine("Paint candidates for a 60-minute viewport: 63 / 43,200 records");
            Console.WriteLine("PASS: " + checks + " history checks");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
