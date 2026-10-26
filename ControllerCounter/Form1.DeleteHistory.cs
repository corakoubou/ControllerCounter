using System;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace ControllerCounter
{
    public partial class Form1
    {
        private void DeleteAllHistory()
        {
            // Modal dialogs pump UI messages: stop auto-save until both answers
            // are known, so confirmation cannot race with a history write.
            bool filterWasPending = filterRefreshTimer.Enabled;
            recordTimer.Stop();
            filterRefreshTimer.Stop();
            try
            {
                if (MessageBox.Show(this,
                    "保存済みの履歴をすべて削除します。期間・アプリ・作品名のフィルターに関係なく、過去の履歴全体が対象です。\n" +
                    "今回の入力数と未保存分もリセットします。表示設定は残ります。\n\n本当に削除してよいですか？",
                    "履歴削除の確認（1/2）", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                if (MessageBox.Show(this,
                    "最終確認です。すべての履歴は削除すると元に戻せません。\n\n本当に削除してよいですか？",
                    "履歴削除の最終確認（2/2）", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

                historyStore.DeleteAll();
                windowActivity.ClearPending();
                keyCount = leftClickCount = rightClickCount = wheelCount = 0;
                mouseMoveDistance = dragDistance = 0;
                historyDirty = true;
                UpdateDisplay();
                RefreshHistory();
                historyStatus.Text = "すべての履歴を削除しました。今後の操作から新しく記録します。";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                historyDirty = true;
                RefreshHistory();
                MessageBox.Show(this, "履歴をすべて削除できませんでした。一部は削除されている可能性があります。\n" + ex.Message,
                    "削除エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Input capture stays active; saving resumes after cancellation or deletion.
                if (!IsDisposed)
                {
                    recordTimer.Start();
                    if (filterWasPending) filterRefreshTimer.Start();
                }
            }
        }
    }
}
