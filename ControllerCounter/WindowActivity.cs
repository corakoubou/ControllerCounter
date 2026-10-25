using System;
using System.Collections.Generic;
using System.Linq;

namespace ControllerCounter
{
    internal sealed class WindowIdentity
    {
        public static readonly WindowIdentity Unknown = new WindowIdentity("", "");
        public readonly string Application, Title;
        public WindowIdentity(string application, string title)
        {
            Application = application ?? "";
            Title = (title ?? "").Replace('\r', ' ').Replace('\n', ' ');
        }
        public string Key { get { return Application + "\0" + Title; } }
    }

    internal sealed class WindowActivity
    {
        private readonly Dictionary<string, HistoryRecord> pending = new Dictionary<string, HistoryRecord>();
        private WindowIdentity dragOwner;

        public void BeginDrag(WindowIdentity owner) { dragOwner = owner ?? WindowIdentity.Unknown; }
        public void EndDrag() { dragOwner = null; }
        public void AddMovement(WindowIdentity hover, double pixels, bool dragging)
        {
            Add(dragging ? dragOwner ?? hover : hover, dragging ? 5 : 4, pixels);
        }
        public void Add(WindowIdentity owner, int metric, double amount)
        {
            if (amount <= 0) return;
            owner = owner ?? WindowIdentity.Unknown;
            HistoryRecord record;
            if (!pending.TryGetValue(owner.Key, out record))
                pending.Add(owner.Key, record = new HistoryRecord { Application = owner.Application, WindowTitle = owner.Title });
            record.Values[metric] += amount;
        }
        public List<HistoryRecord> Snapshot(DateTimeOffset time)
        {
            var result = pending.Values.Select(r => new HistoryRecord { Time = time, Application = r.Application,
                WindowTitle = r.WindowTitle, Values = (double[])r.Values.Clone() }).ToList();
            if (result.Count == 0) result.Add(new HistoryRecord { Time = time });
            return result;
        }
        public void Commit(IEnumerable<HistoryRecord> saved)
        {
            foreach (var record in saved)
            {
                string key = new WindowIdentity(record.Application, record.WindowTitle).Key;
                HistoryRecord current;
                if (!pending.TryGetValue(key, out current)) continue;
                for (int i = 0; i < 6; i++) current.Values[i] = Math.Max(0, current.Values[i] - record.Values[i]);
                if (current.Values.All(v => v == 0)) pending.Remove(key);
            }
        }
    }
}
