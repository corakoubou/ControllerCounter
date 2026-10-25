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
            Console.WriteLine("PASS: " + checks + " history checks");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
