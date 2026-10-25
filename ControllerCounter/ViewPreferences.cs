using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Xml;
using System.Xml.Serialization;

namespace ControllerCounter
{
    public sealed class ViewPreferences
    {
        public string Range = "過去24時間";
        public string Unit = "1分";
        public string Metric = "キー入力";
        public string ChartType = "横棒グラフ";
        public string ValueMode = "区間の量";
        public decimal TimeSpacing = 40;
        public bool[] VisibleColumns = { true, true, true, true, true, true, true };
        public bool[] AutomaticAxis = { true, true, true, true, true, true, true };
        public decimal[] AxisSteps = { 1, 1, 1, 1, 1, 1, 1 };

        public void Normalize()
        {
            TimeSpacing = Math.Max(4, Math.Min(200, TimeSpacing));
            var columns = new bool[7]; var automatic = new bool[7]; var steps = new decimal[7];
            for (int i = 0; i < 7; i++)
            {
                columns[i] = VisibleColumns == null || i >= VisibleColumns.Length || VisibleColumns[i];
                automatic[i] = AutomaticAxis == null || i >= AutomaticAxis.Length || AutomaticAxis[i];
                decimal step = AxisSteps == null || i >= AxisSteps.Length ? 1 : AxisSteps[i];
                steps[i] = step >= 0.001m && step <= 1000000000m ? step : 1;
            }
            VisibleColumns = columns; AutomaticAxis = automatic; AxisSteps = steps;
        }

        public static ViewPreferences Load(string path, out string warning)
        {
            warning = null;
            if (!File.Exists(path)) return new ViewPreferences();
            try
            {
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                {
                    var preferences = (ViewPreferences)new XmlSerializer(typeof(ViewPreferences)).Deserialize(reader);
                    preferences.Normalize();
                    return preferences;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException ||
                ex is InvalidOperationException || ex is XmlException)
            {
                warning = "表示設定を読み込めないため初期値を使用します: " + ex.Message;
                return new ViewPreferences();
            }
        }

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true }))
                    new XmlSerializer(typeof(ViewPreferences)).Serialize(writer, this);
                // Keep the previous complete settings file if serialization fails.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed class ChartValueAxis
    {
        public double Maximum { get; private set; }
        public bool IsThinned { get; private set; }
        public readonly List<double> Ticks = new List<double>();

        public static ChartValueAxis Create(double dataMaximum, bool automatic, decimal requestedStep, int pixels)
        {
            var axis = new ChartValueAxis();
            if (automatic)
            {
                axis.Maximum = Math.Max(1, dataMaximum);
                for (int i = 0; i <= 4; i++) axis.Ticks.Add(axis.Maximum * i / 4);
                return axis;
            }
            if (requestedStep <= 0) throw new ArgumentOutOfRangeException("requestedStep");
            double step = (double)requestedStep;
            double quotient = Math.Max(0, dataMaximum) / step;
            // Avoid a spurious extra tick from values such as 0.07 / 0.01.
            double nearest = Math.Round(quotient);
            if (Math.Abs(quotient - nearest) < 0.00000001) quotient = nearest;
            double count = Math.Max(1, Math.Ceiling(quotient));
            axis.Maximum = count * step;
            // Bound rendering even if a very small step is chosen for a large total.
            int limit = Math.Max(1, pixels / 4);
            double stride = Math.Max(1, Math.Ceiling(count / limit));
            axis.IsThinned = stride > 1;
            for (int i = 0; i <= limit; i++)
            {
                double index = i * stride;
                if (index > count) break;
                axis.Ticks.Add(index * step);
            }
            if (axis.Ticks[axis.Ticks.Count - 1] < axis.Maximum) axis.Ticks.Add(axis.Maximum);
            return axis;
        }
    }
}
