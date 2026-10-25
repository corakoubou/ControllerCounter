using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1 : Form
    {
        private readonly WindowContextResolver windowContext = new WindowContextResolver();
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private const double KEY_MM = 1 * 1000;
        private const double CLICK_MM = 1 * 1000;
        private const double WHEEL_MM = 0.5 * 1000;

        private int keyCount = 0;
        private int leftClickCount = 0;
        private int rightClickCount = 0;
        private int wheelCount = 0;

        private double mouseMoveDistance = 0;
        private double dragDistance = 0;

        private Point? previousMousePoint = null;
        private bool isLeftDragging = false;

        private Label lblStatus;
        private Timer updateTimer;

        private IntPtr keyboardHookId = IntPtr.Zero;
        private IntPtr mouseHookId = IntPtr.Zero;

        private LowLevelKeyboardProc keyboardProc;
        private LowLevelMouseProc mouseProc;

        private const double DPI = 96.0;
        private Button btnExportCsv;
        private Button btnImportCsv;

        private HashSet<int> pressedKeys = new HashSet<int>();

        public Form1()
        {
            InitializeComponent();

            SetupUi();

            keyboardProc = KeyboardHookCallback;
            mouseProc = MouseHookCallback;

            keyboardHookId = SetKeyboardHook(keyboardProc);
            mouseHookId = SetMouseHook(mouseProc);

            updateTimer = new Timer();
            updateTimer.Interval = 100;
            updateTimer.Tick += (s, e) => UpdateDisplay();
            updateTimer.Start();
        }

        private double PixelsToMeters(double pixels)
        {
            return (pixels / DPI) * 0.0254;
        }

        private void SetupUi()
        {
            this.Text = "入力カウンター";
            this.Size = new Size(1000, 650);
            this.StartPosition = FormStartPosition.CenterScreen;

            var panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.Height = 50;

            btnExportCsv = new Button();
            btnExportCsv.Text = "CSV出力";
            btnExportCsv.Width = 100;
            btnExportCsv.Height = 30;
            btnExportCsv.Left = 10;
            btnExportCsv.Top = 10;
            btnExportCsv.Click += (s, e) => ExportCsvWithDialog();

            btnImportCsv = new Button();
            btnImportCsv.Text = "CSV読み込み";
            btnImportCsv.Width = 120;
            btnImportCsv.Height = 30;
            btnImportCsv.Left = 120;
            btnImportCsv.Top = 10;
            btnImportCsv.Click += (s, e) => ImportCsvWithDialog();

            panel.Controls.Add(btnExportCsv);
            panel.Controls.Add(btnImportCsv);

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Font = new Font("Meiryo", 12);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(20);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var currentPage = new TabPage("今回の入力");
            var historyPage = new TabPage("履歴・推移");
            currentPage.Controls.Add(lblStatus);
            currentPage.Controls.Add(panel);
            tabs.TabPages.Add(currentPage);
            tabs.TabPages.Add(historyPage);
            this.Controls.Add(tabs);
            SetupHistoryUi(historyPage);
            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedTab == historyPage && historyDirty) RefreshHistory(); };

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (isInteractiveResize) return;
            lblStatus.Text =
                $"キー入力数：{keyCount:N0}\n" +
                $"左クリック：{leftClickCount:N0}\n" +
                $"右クリック：{rightClickCount:N0}\n" +
                $"ホイール：{wheelCount:N0}\n" +
                $"\n" +
                $"通常マウス移動距離：{PixelsToMeters(mouseMoveDistance):F2} m\n" +
                $"ドラッグ距離：{PixelsToMeters(dragDistance):F2} m\n" +
                $"\n" +
                $"総入力距離：{GetTotalInputMeters():F2} m";
        }

        private double GetTotalInputMeters()
        {
            double totalMm = 0;

            // キー
            totalMm += keyCount * KEY_MM;

            // クリック
            totalMm += leftClickCount * CLICK_MM;
            totalMm += rightClickCount * CLICK_MM;

            // ホイール
            totalMm += wheelCount * WHEEL_MM;

            // mm → m
            double totalMeters = totalMm / 1000.0;

            // マウス移動距離追加
            totalMeters += PixelsToMeters(mouseMoveDistance);

            // ドラッグ距離追加
            totalMeters += PixelsToMeters(dragDistance);

            return totalMeters;
        }

        private void ExportCsvWithDialog()
        {
            var dialog = new SaveFileDialog();
            dialog.Filter = "CSVファイル (*.csv)|*.csv";
            dialog.FileName = "input_counter_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                ExportCsv(dialog.FileName);
                MessageBox.Show("CSV出力しました。", "完了");
            }
        }

        private void ExportCsv(string filePath)
        {
            var sb = new StringBuilder();

            sb.AppendLine("日時,キー入力数,左クリック,右クリック,ホイール,マウス移動距離px,ドラッグ距離px,マウス移動距離m,ドラッグ距離m");

            sb.AppendLine(
                Csv(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")) + "," +
                keyCount + "," +
                leftClickCount + "," +
                rightClickCount + "," +
                wheelCount + "," +
                mouseMoveDistance.ToString("F0") + "," +
                dragDistance.ToString("F0") + "," +
                PixelsToMeters(mouseMoveDistance).ToString("F2") + "," +
                PixelsToMeters(dragDistance).ToString("F2")
            );

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        }

        private void ImportCsvWithDialog()
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "CSVファイル (*.csv)|*.csv";

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    ImportCsv(dialog.FileName);
                    UpdateDisplay();
                    MessageBox.Show("CSV読み込みしました。", "完了");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                    ex is FormatException || ex is OverflowException)
                {
                    MessageBox.Show("CSVを読み込めませんでした: " + ex.Message, "読み込みエラー");
                }
            }
        }

        private void ImportCsv(string filePath)
        {
            var lines = File.ReadAllLines(filePath, new UTF8Encoding(true));

            if (lines.Length < 2)
            {
                MessageBox.Show("読み込めるデータがありません。");
                return;
            }

            string[] values = SplitCsvLine(lines[1]);
            if (values.Length < 7) throw new FormatException("必要な列がありません。");
            int importedKeys = int.Parse(values[1]);
            int importedLeft = int.Parse(values[2]);
            int importedRight = int.Parse(values[3]);
            int importedWheel = int.Parse(values[4]);
            double importedMove = double.Parse(values[5]);
            double importedDrag = double.Parse(values[6]);
            if (importedKeys < 0 || importedLeft < 0 || importedRight < 0 || importedWheel < 0 ||
                importedMove < 0 || importedDrag < 0 || double.IsNaN(importedMove) || double.IsNaN(importedDrag) ||
                double.IsInfinity(importedMove) || double.IsInfinity(importedDrag))
                throw new FormatException("入力数・距離は有限の非負値にしてください。");
            // Flush activity before replacing counters; imported totals are not new activity.
            if (!SaveHistorySample()) throw new IOException("履歴を保存できないため読み込みを中止しました。");
            keyCount = importedKeys;
            leftClickCount = importedLeft;
            rightClickCount = importedRight;
            wheelCount = importedWheel;
            mouseMoveDistance = importedMove;
            dragDistance = importedDrag;
        }

        private string Csv(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                value = value.Replace("\"", "\"\"");
                return "\"" + value + "\"";
            }

            return value;
        }

        private string[] SplitCsvLine(string line)
        {
            return line.Split(',');
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var result = MessageBox.Show(
                "終了前にCSV出力しますか？",
                "確認",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == DialogResult.Yes)
            {
                ExportCsvWithDialog();
            }

            if (!SaveHistorySample() && MessageBox.Show("履歴を保存できませんでした。終了しますか？", "保存エラー",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            recordTimer.Stop();
            recordTimer.Dispose();
            updateTimer.Stop();
            updateTimer.Dispose();
            UnhookWindowsHookEx(keyboardHookId);
            UnhookWindowsHookEx(mouseHookId);
            base.OnFormClosed(e);
        }

        // =========================
        // キーボードフック
        // =========================

        private IntPtr SetKeyboardHook(LowLevelKeyboardProc proc)
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var currentModule = currentProcess.MainModule;

            return SetWindowsHookEx(
                WH_KEYBOARD_LL,
                proc,
                GetModuleHandle(currentModule.ModuleName),
                0
            );
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                int keyCode = Marshal.ReadInt32(lParam);

                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    if (!pressedKeys.Contains(keyCode))
                    {
                        pressedKeys.Add(keyCode);
                        keyCount++;
                        windowActivity.Add(windowContext.Foreground(), 0, 1);
                    }
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    pressedKeys.Remove(keyCode);
                }
            }

            return CallNextHookEx(keyboardHookId, nCode, wParam, lParam);
        }

        // =========================
        // マウスフック
        // =========================

        private IntPtr SetMouseHook(LowLevelMouseProc proc)
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var currentModule = currentProcess.MainModule;

            return SetWindowsHookEx(
                WH_MOUSE_LL,
                proc,
                GetModuleHandle(currentModule.ModuleName),
                0
            );
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct =
                    Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                Point currentPoint = new Point(hookStruct.pt.x, hookStruct.pt.y);
                int message = wParam.ToInt32();
                var owner = isLeftDragging && message == WM_MOUSEMOVE ? WindowIdentity.Unknown :
                    windowContext.AtPoint(currentPoint.X, currentPoint.Y, message != WM_MOUSEMOVE);

                if (previousMousePoint.HasValue)
                {
                    double distance = GetDistance(previousMousePoint.Value, currentPoint);

                    if (distance > 0)
                    {
                        windowActivity.AddMovement(owner, distance, isLeftDragging);
                        if (isLeftDragging)
                        {
                            dragDistance += distance;
                        }
                        else
                        {
                            mouseMoveDistance += distance;
                        }
                    }
                }

                previousMousePoint = currentPoint;

                switch (message)
                {
                    case WM_LBUTTONDOWN:
                        leftClickCount++;
                        windowActivity.Add(owner, 1, 1);
                        windowActivity.BeginDrag(owner);
                        isLeftDragging = true;
                        break;

                    case WM_LBUTTONUP:
                        windowActivity.EndDrag();
                        isLeftDragging = false;
                        break;

                    case WM_RBUTTONDOWN:
                        rightClickCount++;
                        windowActivity.Add(owner, 2, 1);
                        break;

                    case WM_MOUSEWHEEL:
                        wheelCount++;
                        windowActivity.Add(owner, 3, 1);
                        break;
                }
            }

            return CallNextHookEx(mouseHookId, nCode, wParam, lParam);
        }

        private double GetDistance(Point p1, Point p2)
        {
            int dx = p2.X - p1.X;
            int dy = p2.Y - p1.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        // =========================
        // Windows API
        // =========================

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MOUSEWHEEL = 0x020A;

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

        private delegate IntPtr LowLevelMouseProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            Delegate lpfn,
            IntPtr hMod,
            uint dwThreadId
        );

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}