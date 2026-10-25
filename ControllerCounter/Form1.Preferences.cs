using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1
    {
        private static readonly string ViewPreferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerCounter", "view-settings.xml");
        private ViewPreferences viewPreferences = new ViewPreferences();
        private readonly List<CheckBox> columnChecks = new List<CheckBox>();
        private CheckBox automaticValueAxis;
        private NumericUpDown valueAxisStep, valueAxisMaximum;
        private CheckBox automaticValueMaximum;
        private Timer preferencesSaveTimer;
        private Label preferencesStatus;
        private bool applyingPreferences;

        private void InitializeViewPreferences()
        {
            string warning;
            viewPreferences = ViewPreferences.Load(ViewPreferencesPath, out warning);
            applyingPreferences = true;
            try
            {
                SelectSaved(historyRange, viewPreferences.Range);
                SelectSaved(historyUnit, viewPreferences.Unit);
                SelectSaved(historyMetric, viewPreferences.Metric);
                SelectSaved(historyChartType, viewPreferences.ChartType);
                SelectSaved(historyValueMode, viewPreferences.ValueMode);
                chartSpacing.Value = viewPreferences.TimeSpacing;
                for (int i = 0; i < columnChecks.Count; i++) columnChecks[i].Checked = viewPreferences.VisibleColumns[i];
                ApplyMetricAxisPreferences();
            }
            finally { applyingPreferences = false; }
            preferencesStatus.Text = warning ?? "";
            preferencesStatus.Visible = warning != null;
            preferencesSaveTimer = new Timer { Interval = 500 };
            preferencesSaveTimer.Tick += (s, e) => SaveViewPreferences();
            historyRange.SelectedIndexChanged += (s, e) => SchedulePreferencesSave();
            historyUnit.SelectedIndexChanged += (s, e) => SchedulePreferencesSave();
            historyMetric.SelectedIndexChanged += (s, e) => SchedulePreferencesSave();
            historyChartType.SelectedIndexChanged += (s, e) => SchedulePreferencesSave();
            historyValueMode.SelectedIndexChanged += (s, e) => SchedulePreferencesSave();
            chartSpacing.ValueChanged += (s, e) => SchedulePreferencesSave();
            foreach (var check in columnChecks) check.CheckedChanged += (s, e) => SchedulePreferencesSave();
            automaticValueAxis.CheckedChanged += (s, e) => StoreMetricAxisPreferences();
            valueAxisStep.ValueChanged += (s, e) => StoreMetricAxisPreferences();
            automaticValueMaximum.CheckedChanged += (s, e) => StoreMetricAxisPreferences();
            valueAxisMaximum.ValueChanged += (s, e) => StoreMetricAxisPreferences();
            // Flush changes if the application closes before the debounce timer fires.
            FormClosed += (s, e) => { SaveViewPreferences(); preferencesSaveTimer.Dispose(); };
        }

        private static void SelectSaved(ComboBox box, string value)
        {
            int index = box.Items.IndexOf(value ?? "");
            if (index >= 0) box.SelectedIndex = index;
        }

        private void ApplyMetricAxisPreferences()
        {
            bool previous = applyingPreferences;
            applyingPreferences = true;
            try
            {
                int metric = historyMetric.SelectedIndex;
                automaticValueAxis.Checked = viewPreferences.AutomaticAxis[metric];
                valueAxisStep.Value = viewPreferences.AxisSteps[metric];
                valueAxisStep.Enabled = !automaticValueAxis.Checked;
                automaticValueMaximum.Checked = viewPreferences.AutomaticMaximum[metric];
                valueAxisMaximum.Value = viewPreferences.AxisMaximums[metric];
                valueAxisMaximum.Enabled = !automaticValueMaximum.Checked;
            }
            finally { applyingPreferences = previous; }
        }

        private void StoreMetricAxisPreferences()
        {
            if (applyingPreferences) return;
            int metric = historyMetric.SelectedIndex;
            viewPreferences.AutomaticAxis[metric] = automaticValueAxis.Checked;
            viewPreferences.AxisSteps[metric] = valueAxisStep.Value;
            valueAxisStep.Enabled = !automaticValueAxis.Checked;
            viewPreferences.AutomaticMaximum[metric] = automaticValueMaximum.Checked;
            viewPreferences.AxisMaximums[metric] = valueAxisMaximum.Value;
            valueAxisMaximum.Enabled = !automaticValueMaximum.Checked;
            historyChart.Invalidate();
            SchedulePreferencesSave();
        }

        private void SchedulePreferencesSave()
        {
            if (applyingPreferences || preferencesSaveTimer == null) return;
            preferencesSaveTimer.Stop();
            preferencesSaveTimer.Start();
        }

        private void SaveViewPreferences()
        {
            preferencesSaveTimer.Stop();
            viewPreferences.Range = historyRange.Text;
            viewPreferences.Unit = historyUnit.Text;
            viewPreferences.Metric = historyMetric.Text;
            viewPreferences.ChartType = historyChartType.Text;
            viewPreferences.ValueMode = historyValueMode.Text;
            viewPreferences.TimeSpacing = chartSpacing.Value;
            for (int i = 0; i < columnChecks.Count; i++) viewPreferences.VisibleColumns[i] = columnChecks[i].Checked;
            try
            {
                viewPreferences.Save(ViewPreferencesPath);
                preferencesStatus.Visible = false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                preferencesStatus.Text = "表示設定を保存できませんでした: " + ex.Message;
                preferencesStatus.Visible = true;
            }
        }
    }
}
