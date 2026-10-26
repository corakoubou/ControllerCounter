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
            Check(Directory.GetFiles(Path.Combine(directory, "Windows"), "*.csv").Length == 2, "Daily rotation");
            var minutes = HistoryStore.Aggregate(records, TimeSpan.FromMinutes(1));
            Check(minutes.Count == 3 && minutes[0].Values[0] == 7, "Minute totals and boundary");
            Check(Math.Abs(records[0].TotalMeters - 12.0762) < 0.00001, "All metrics contribute to total distance");
            var hours = HistoryStore.Aggregate(records, TimeSpan.FromHours(1));
            Check(hours.Count == 2 && hours[0].Values[0] == 8, "Hourly aggregation");
            var days = HistoryStore.Aggregate(records, TimeSpan.FromDays(1));
            Check(days.Count == 2 && days.Sum(r => r.Values[0]) == 18, "Daily aggregation preserves totals");
            Check(reopened.Read(start.AddSeconds(59), start.AddMinutes(1), out invalid).Count == 2, "Time range bounds");
            File.AppendAllText(Directory.GetFiles(Path.Combine(directory, "Windows"), "*.csv").First(), "broken,row\n2026-10-25T10:00:00.0000000+00:00,NaN,0,0,0,0,0\n");
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
            foreach (var unit in new[] { TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(3), TimeSpan.FromHours(5), TimeSpan.FromHours(8) })
            {
                var boundary = new DateTimeOffset(new DateTime(start.LocalDateTime.Ticks / unit.Ticks * unit.Ticks, DateTimeKind.Local));
                var samples = new[]
                {
                    new HistoryRecord { Time = boundary.AddSeconds(1), Values = new double[] { 2, 0, 0, 0, 0, 0 } },
                    new HistoryRecord { Time = boundary.Add(unit).AddSeconds(-1), Values = new double[] { 3, 0, 0, 0, 0, 0 } },
                    new HistoryRecord { Time = boundary.Add(unit), Values = new double[] { 7, 0, 0, 0, 0, 0 } }
                };
                var buckets = HistoryStore.Aggregate(samples, unit);
                Check(buckets.Count == 2 && buckets[0].Values[0] == 5 && buckets[1].Values[0] == 7,
                    "Aggregation respects boundary for " + unit);
                var series = new HistoryChartData(buckets, unit);
                Check(series.Value(1, 0, true) == 12 && series.Buckets[1] == 1,
                    "Cumulative totals and chart spacing work for " + unit);
            }
            var manualAxis = ChartValueAxis.Create(2.34, false, 1m, 500);
            Check(manualAxis.Maximum == 3 && manualAxis.Ticks.SequenceEqual(new double[] { 0, 1, 2, 3 }),
                "Manual value axis draws the requested one-meter increments");
            var fractionalAxis = ChartValueAxis.Create(2.34, false, 0.5m, 500);
            Check(fractionalAxis.Maximum == 3 && fractionalAxis.Ticks.Count == 7 && fractionalAxis.Ticks[1] == 0.5,
                "Manual value axis supports fractional meter increments");
            Check(Math.Abs(ChartValueAxis.Create(0.07, false, 0.01m, 500, false, 0.07m).Maximum - 0.07) < 0.000001,
                "Decimal tick boundaries do not gain an extra interval");
            var automaticAxis = ChartValueAxis.Create(2.34, true, 1m, 500);
            Check(automaticAxis.Maximum == 3 && automaticAxis.Ticks.Count > 5,
                "Automatic value axis uses a rounded maximum with fine grid lines");
            var denseAxis = ChartValueAxis.Create(1000000000, false, 0.001m, 400);
            Check(denseAxis.IsThinned && denseAxis.Ticks.Count <= 102 && denseAxis.Maximum >= 1000000000,
                "Tiny requested intervals never create an unbounded paint loop");
            Check(ChartValueAxis.Create(0, false, 0.5m, 400).Maximum == 1,
                "A zero-data manual axis has a nonzero extent");
            var coarseGrid = ChartValueAxis.Create(21.3, false, 50m, 800, false, 50m);
            var mediumGrid = ChartValueAxis.Create(21.3, false, 10m, 800, false, 50m);
            var fineGrid = ChartValueAxis.Create(21.3, false, 1m, 800, false, 50m);
            Check(coarseGrid.Maximum == 50 && mediumGrid.Maximum == 50 && fineGrid.Maximum == 50,
                "Changing grid intervals never changes the explicitly set upper bound");
            Check(coarseGrid.Ticks.Count == 2 && mediumGrid.Ticks.Count == 6 && fineGrid.Ticks.Count == 51,
                "A fifty-meter scale can display one-meter grid lines");
            Check(ChartValueAxis.Create(21.3, false, 50m, 800).Maximum ==
                ChartValueAxis.Create(21.3, false, 1m, 800).Maximum,
                "Automatic upper bound is independent of manual grid interval");
            var overflowAxis = ChartValueAxis.Create(61, false, 1m, 800, false, 50m);
            Check(overflowAxis.Maximum >= 61 && overflowAxis.MaximumExpanded,
                "Data exceeding a fixed maximum remains visible and reports scale expansion");
            var shortAxis = ChartValueAxis.Create(1, true, 1m, 800);
            Check(shortAxis.Ticks.Select(t => t.ToString("0.#")).Distinct().Count() == shortAxis.Ticks.Count,
                "Fine automatic grids retain distinct one-decimal labels");
            string preferencesPath = Path.Combine(directory, "view-settings.xml");
            string preferencesWarning;
            var preferences = ViewPreferences.Load(preferencesPath, out preferencesWarning);
            Check(preferencesWarning == null && preferences.TimeSpacing == 40 && preferences.VisibleColumns.All(v => v),
                "Missing settings use defaults");
            preferences.Range = "過去7日"; preferences.Unit = "15分"; preferences.Metric = "ドラッグ (m)";
            preferences.ChartType = "縦棒グラフ"; preferences.ValueMode = "期間内の累計";
            preferences.TimeSpacing = 12;
            preferences.VisibleColumns[0] = false;
            preferences.AutomaticAxis[5] = false;
            preferences.AxisSteps[5] = 0.5m;
            preferences.AxisSteps[0] = 100;
            preferences.AutomaticMaximum[5] = false;
            preferences.AxisMaximums[5] = 50;
            preferences.Save(preferencesPath);
            var restored = ViewPreferences.Load(preferencesPath, out preferencesWarning);
            Check(preferencesWarning == null && restored.Range == preferences.Range && restored.Unit == preferences.Unit &&
                restored.Metric == preferences.Metric && restored.ChartType == preferences.ChartType && restored.ValueMode == preferences.ValueMode,
                "All selected graph options survive restart");
            Check(restored.TimeSpacing == 12 && !restored.VisibleColumns[0] && restored.VisibleColumns[1] &&
                !restored.AutomaticAxis[5] && restored.AxisSteps[5] == 0.5m && restored.AxisSteps[0] == 100,
                "Visibility, spacing and independent metric steps survive restart across cultures");
            Check(!restored.AutomaticMaximum[5] && restored.AxisMaximums[5] == 50 && restored.AutomaticMaximum[0],
                "Per-metric upper limits survive restart independently from grid steps");
            string legacyPath = Path.Combine(directory, "legacy-settings.xml");
            File.WriteAllText(legacyPath, "<ViewPreferences><TimeSpacing>12</TimeSpacing></ViewPreferences>");
            var legacyPreferences = ViewPreferences.Load(legacyPath, out preferencesWarning);
            Check(preferencesWarning == null && legacyPreferences.TimeSpacing == 12 && legacyPreferences.AutomaticMaximum.All(v => v),
                "Older settings restore with automatic upper limits");
            restored.TimeSpacing = 20;
            restored.Save(preferencesPath);
            Check(ViewPreferences.Load(preferencesPath, out preferencesWarning).TimeSpacing == 20 && Directory.GetFiles(directory, "*.tmp").Length == 0,
                "Updating preferences replaces the file without leftover temporary data");
            restored.TimeSpacing = -1; restored.AxisSteps = new decimal[] { -1 }; restored.VisibleColumns = new bool[] { false };
            restored.Normalize();
            Check(restored.TimeSpacing == 4 && restored.AxisSteps[0] == 1 && restored.AxisSteps.Length == 7 &&
                !restored.VisibleColumns[0] && restored.VisibleColumns[1], "Invalid or incomplete saved arrays are normalized");
            File.WriteAllText(preferencesPath, "<ViewPreferences><TimeSpacing>broken</TimeSpacing></ViewPreferences>");
            restored = ViewPreferences.Load(preferencesPath, out preferencesWarning);
            Check(preferencesWarning != null && restored.TimeSpacing == 40, "Corrupt settings fall back to defaults with a warning");
            string workDirectory = Path.Combine(directory, "work-history");
            var workStore = new HistoryStore(workDirectory);
            var activity = new WindowActivity();
            var artA = new WindowIdentity("CLIPStudioPaint", "作品A, \"試作\".clip* - CLIP STUDIO PAINT");
            var artB = new WindowIdentity("CLIPStudioPaint", "作品B.clip - CLIP STUDIO PAINT");
            var browser = new WindowIdentity("Browser", "参考資料");
            activity.Add(artA, 0, 2);
            activity.Add(artA, 1, 1);
            activity.BeginDrag(artA);
            activity.AddMovement(browser, 96, true);
            activity.EndDrag();
            activity.AddMovement(browser, 48, false);
            activity.Add(artB, 0, 3);
            activity.Add(artB, 3, 4);
            var firstBatch = activity.Snapshot(start);
            Check(firstBatch.Count == 3 && firstBatch.Single(r => r.WindowTitle == artA.Title).Values[5] == 96 &&
                firstBatch.Single(r => r.Application == "Browser").Values[4] == 48,
                "Drag movement stays with the start window while normal movement follows hover");
            Check(activity.Snapshot(start).Sum(r => r.Values[0]) == 5,
                "A failed save can retry the unchanged activity snapshot");
            workStore.AppendBatch(firstBatch);
            activity.Add(artA, 0, 1);
            activity.Commit(firstBatch);
            var remaining = activity.Snapshot(start.AddSeconds(30));
            Check(remaining.Count == 1 && remaining[0].Values[0] == 1,
                "Committing a snapshot preserves events received after that snapshot");
            workStore.AppendBatch(remaining);
            activity.Commit(remaining);
            Check(activity.Snapshot(start.AddMinutes(1)).Single().Values.All(v => v == 0),
                "Saved activity is not recorded twice");
            var workRecords = workStore.Read(start, start.AddMinutes(2), out invalid);
            Check(invalid == 0 && workRecords.Count == 4 && workRecords.Any(r => r.WindowTitle == artA.Title),
                "Unicode titles containing commas and quotes round-trip through history CSV");
            var filteredA = HistoryStore.Filter(workRecords, "clipstudiopaint", "作品A");
            Check(filteredA.Count == 2 && filteredA.Sum(r => r.Values[0]) == 3 && filteredA.Sum(r => r.Values[5]) == 96,
                "Case-insensitive app and partial artwork filters select only the requested artwork");
            var filteredB = HistoryStore.Filter(workRecords, "CLIP", "作品B");
            Check(filteredB.Count == 1 && filteredB[0].Values[0] == 3 && filteredB[0].Values[5] == 0,
                "Switching documents within one interval does not mix their counters");
            var filteredGraph = new HistoryChartData(HistoryStore.Aggregate(filteredA, TimeSpan.FromMinutes(1)), TimeSpan.FromMinutes(1));
            Check(filteredGraph.Value(0, 0, true) == 3 && Math.Abs(filteredGraph.Value(0, 5, true) - 0.0254) < 0.000001,
                "Filtered cumulative graphs include only matching activity");
            Check(HistoryStore.Filter(workRecords, "", "").Sum(r => r.Values[0]) == 6 &&
                HistoryStore.Filter(workRecords, "", "存在しない作品").Count == 0,
                "Clearing filters restores totals and unmatched filters return no records");
            File.WriteAllText(Path.Combine(workDirectory, "2026-10-25.csv"),
                "Time,Keys,LeftClicks,RightClicks,Wheel,MousePixels,DragPixels\n" +
                start.ToString("o", CultureInfo.InvariantCulture) + ",10,0,0,0,0,0\n");
            workRecords = workStore.Read(start, start.AddMinutes(2), out invalid);
            Check(workRecords.Sum(r => r.Values[0]) == 16 && HistoryStore.Filter(workRecords, "", "作品A").Sum(r => r.Values[0]) == 3,
                "Legacy history remains visible in all-records view without inventing artwork attribution");
            var normalizedTitle = new WindowIdentity("CLIP", "作品\r\nC");
            workStore.Append(new HistoryRecord { Time = start, Application = normalizedTitle.Application, WindowTitle = normalizedTitle.Title });
            Check(workStore.Read(start, start, out invalid).Any(r => r.WindowTitle == "作品  C") && invalid == 0,
                "Multiline captions are stored on one CSV line");
            var retryActivity = new WindowActivity();
            retryActivity.Add(artA, 5, 12);
            string blockedPath = Path.Combine(directory, "blocked-history");
            File.WriteAllText(blockedPath, "not a directory");
            bool failedWrite = false;
            try { new HistoryStore(blockedPath).AppendBatch(retryActivity.Snapshot(start)); }
            catch (IOException) { failedWrite = true; }
            Check(failedWrite && retryActivity.Snapshot(start).Sum(r => r.Values[5]) == 12,
                "A real storage failure leaves activity available for retry");
            var retried = retryActivity.Snapshot(start);
            workStore.AppendBatch(retried);
            retryActivity.Commit(retried);
            Check(retryActivity.Snapshot(start).Sum(r => r.Values[5]) == 0,
                "A successful retry commits only the saved activity");
            File.AppendAllText(Directory.GetFiles(Path.Combine(workDirectory, "Windows"), "*.csv").First(),
                start.ToString("o", CultureInfo.InvariantCulture) + ",1,0,0,0,0,0,\"CLIP\",\"unclosed title\n");
            workStore.Read(start, start.AddMinutes(2), out invalid);
            Check(invalid == 1, "Malformed quoted context rows are reported and skipped");
            preferences.ApplicationFilter = "CLIPStudioPaint";
            preferences.ArtworkFilter = "作品A, \"試作\"";
            preferences.Save(preferencesPath);
            restored = ViewPreferences.Load(preferencesPath, out preferencesWarning);
            Check(restored.ApplicationFilter == preferences.ApplicationFilter && restored.ArtworkFilter == preferences.ArtworkFilter,
                "App and artwork filters survive restart without losing Unicode or quotes");
            var gapStart = new DateTimeOffset(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Local));
            var sparseChartRows = new[]
            {
                new HistoryRecord { Time = gapStart.AddMinutes(1), Values = new double[] { 2, 0, 0, 0, 0, 0 } },
                new HistoryRecord { Time = gapStart.AddMinutes(4), Values = new double[] { 3, 0, 0, 0, 0, 0 } }
            };
            var filledRows = HistoryStore.FillChartGaps(sparseChartRows, TimeSpan.FromMinutes(1), gapStart, gapStart.AddMinutes(6));
            var filledChart = new HistoryChartData(filledRows, TimeSpan.FromMinutes(1));
            Check(filledRows.Count == 7 && filledRows[0].Values[0] == 0 && filledRows[2].Values[0] == 0 && filledRows[6].Values[0] == 0,
                "Charts fill leading, middle and trailing zero buckets");
            Check(filledRows.Select(r => r.Values[0]).SequenceEqual(new double[] { 0, 2, 0, 0, 3, 0, 0 }),
                "Interval plots include zero-valued points without interpolating activity");
            Check(Enumerable.Range(0, filledChart.Count).Select(i => filledChart.Value(i, 0, true))
                .SequenceEqual(new double[] { 0, 2, 2, 2, 5, 5, 5 }),
                "Cumulative plots carry the last total through every zero interval");
            Check(sparseChartRows.Length == 2 && filledChart.Maximum(0, true) == 5,
                "Chart filling preserves sparse table rows and period totals");
            var noMatches = HistoryStore.FillChartGaps(new HistoryRecord[0], TimeSpan.FromMinutes(1), gapStart, gapStart.AddMinutes(2));
            Check(noMatches.Count == 3 && noMatches.All(r => r.Values.All(v => v == 0)),
                "An unmatched artwork filter can show zero points across the recorded period");
            string deletionDirectory = Path.Combine(directory, "deletion-test");
            var deletionStore = new HistoryStore(deletionDirectory);
            deletionStore.Append(new HistoryRecord { Time = start, Application = "CLIP", WindowTitle = "作品A" });
            deletionStore.Append(new HistoryRecord { Time = start.AddDays(1), Application = "Browser", WindowTitle = "別作品" });
            File.WriteAllText(Path.Combine(deletionDirectory, "2026-10-25.csv"), "legacy history");
            File.WriteAllText(Path.Combine(deletionDirectory, "export.csv"), "export to preserve");
            File.WriteAllText(Path.Combine(deletionDirectory, "view-settings.xml"), "settings to preserve");
            Directory.CreateDirectory(Path.Combine(deletionDirectory, "backup"));
            File.WriteAllText(Path.Combine(deletionDirectory, "backup", "2026-10-25.csv"), "backup to preserve");
            Check(deletionStore.DeleteAll() == 3 && deletionStore.Read(start, start.AddDays(2), out invalid).Count == 0,
                "Delete all removes every legacy and artwork daily file, regardless of filters");
            Check(File.Exists(Path.Combine(deletionDirectory, "export.csv")) && File.Exists(Path.Combine(deletionDirectory, "view-settings.xml")) &&
                File.Exists(Path.Combine(deletionDirectory, "backup", "2026-10-25.csv")), "Deletion preserves settings, exports and unrelated backups");
            Check(deletionStore.DeleteAll() == 0, "Repeated deletion with no history is safe");
            activity.Add(artA, 0, 5);
            activity.ClearPending();
            Check(activity.Snapshot(start).All(r => r.Values.All(v => v == 0)), "Pending data cannot restore deleted history on the next save");
            activity.Add(artB, 0, 2);
            var afterDeletion = activity.Snapshot(start.AddMinutes(1));
            deletionStore.AppendBatch(afterDeletion);
            activity.Commit(afterDeletion);
            Check(deletionStore.Read(start, start.AddDays(2), out invalid).Sum(r => r.Values[0]) == 2,
                "Recording resumes with only new activity after deletion");
            Console.WriteLine("Paint candidates for a 60-minute viewport: 63 / 43,200 records");
            Console.WriteLine("PASS: " + checks + " history checks");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
