using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ControllerCounter
{
    // Each record contains interval deltas, not cumulative session counters.
    internal sealed class HistoryRecord
    {
        public DateTimeOffset Time;
        public double[] Values = new double[6];
        public double TotalMeters { get { return Values[0] + Values[1] + Values[2] + Values[3] * 0.5 + (Values[4] + Values[5]) / 96 * 0.0254; } }
    }

    internal static class HistoryTimeline
    {
        // Pixel spacing is per aggregation bucket, including missing buckets.
        public static double GetOffset(DateTimeOffset time, DateTimeOffset start, TimeSpan unit, double spacing)
        {
            if (unit.Ticks <= 0) throw new ArgumentOutOfRangeException("unit");
            if (spacing <= 0 || double.IsInfinity(spacing) || double.IsNaN(spacing))
                throw new ArgumentOutOfRangeException("spacing");
            return (time - start).Ticks / (double)unit.Ticks * spacing;
        }
    }

    internal sealed class HistoryChartData
    {
        private readonly double[][] intervalValues, cumulativeValues;
        private readonly double[] intervalMax = new double[7], cumulativeMax = new double[7];
        public readonly DateTimeOffset[] Times;
        public readonly double[] Buckets;
        public int Count { get { return Times.Length; } }

        // Build once per history refresh, never during a paint or resize.
        public HistoryChartData(IList<HistoryRecord> records, TimeSpan unit)
        {
            Times = new DateTimeOffset[records.Count];
            Buckets = new double[records.Count];
            intervalValues = new double[records.Count][];
            cumulativeValues = new double[records.Count][];
            var totals = new double[7];
            for (int row = 0; row < records.Count; row++)
            {
                var record = records[row];
                Times[row] = record.Time;
                Buckets[row] = HistoryTimeline.GetOffset(record.Time, records[0].Time, unit, 1);
                intervalValues[row] = new double[7];
                cumulativeValues[row] = new double[7];
                for (int metric = 0; metric < 7; metric++)
                {
                    double value = metric == 6 ? record.TotalMeters : metric >= 4 ? record.Values[metric] / 96 * 0.0254 : record.Values[metric];
                    intervalValues[row][metric] = value;
                    totals[metric] += value;
                    cumulativeValues[row][metric] = totals[metric];
                    intervalMax[metric] = Math.Max(intervalMax[metric], value);
                    cumulativeMax[metric] = totals[metric];
                }
            }
        }

        public double Value(int row, int metric, bool cumulative)
        {
            return (cumulative ? cumulativeValues : intervalValues)[row][metric];
        }
        public double Maximum(int metric, bool cumulative) { return (cumulative ? cumulativeMax : intervalMax)[metric]; }

        // Binary search restricts paint work to visible samples plus line endpoints.
        public void VisibleRange(double fromBucket, double toBucket, out int first, out int end)
        {
            int low = 0, high = Count;
            while (low < high) { int mid = low + (high - low) / 2; if (Buckets[mid] < fromBucket) low = mid + 1; else high = mid; }
            first = Math.Max(0, low - 1);
            high = Count;
            while (low < high) { int mid = low + (high - low) / 2; if (Buckets[mid] <= toBucket) low = mid + 1; else high = mid; }
            end = Math.Min(Count, low + 1);
        }
    }

    internal sealed class HistoryStore
    {
        private readonly string directory;
        public HistoryStore(string directory) { this.directory = directory; }

        public void Append(HistoryRecord record)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, record.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (stream.Length == 0) writer.WriteLine("Time,Keys,LeftClicks,RightClicks,Wheel,MousePixels,DragPixels");
                writer.WriteLine(record.Time.ToString("o", CultureInfo.InvariantCulture) + "," +
                    string.Join(",", record.Values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
            }
        }

        public List<HistoryRecord> Read(DateTimeOffset from, DateTimeOffset to, out int invalidRows)
        {
            var result = new List<HistoryRecord>();
            invalidRows = 0;
            if (!Directory.Exists(directory)) return result;
            foreach (string file in Directory.EnumerateFiles(directory, "*.csv"))
            {
                DateTime date;
                if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                // Include adjacent dates for records written in other time zones.
                if (date < from.Date.AddDays(-1) || date > to.Date.AddDays(1)) continue;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("Time,")) continue;
                        var cells = line.Split(',');
                        var record = new HistoryRecord();
                        bool valid = cells.Length == 7 && DateTimeOffset.TryParseExact(cells[0], "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out record.Time);
                        for (int i = 0; valid && i < 6; i++)
                            valid = double.TryParse(cells[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out record.Values[i]) &&
                                !double.IsNaN(record.Values[i]) && !double.IsInfinity(record.Values[i]) && record.Values[i] >= 0;
                        if (!valid) { invalidRows++; continue; }
                        if (record.Time >= from && record.Time <= to) result.Add(record);
                    }
                }
            }
            return result.OrderBy(r => r.Time).ToList();
        }

        public static List<HistoryRecord> Aggregate(IEnumerable<HistoryRecord> records, TimeSpan unit)
        {
            if (unit.Ticks <= 0) throw new ArgumentOutOfRangeException("unit");
            return records.GroupBy(r => r.Time.LocalDateTime.Ticks / unit.Ticks)
                .OrderBy(g => g.Key).Select(g => new HistoryRecord
                {
                    Time = new DateTimeOffset(new DateTime(g.Key * unit.Ticks, DateTimeKind.Local)),
                    Values = Enumerable.Range(0, 6).Select(i => g.Sum(r => r.Values[i])).ToArray()
                }).ToList();
        }
    }
}
