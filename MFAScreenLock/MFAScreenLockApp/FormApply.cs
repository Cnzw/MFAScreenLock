using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MFAScreenLockApp
{
    public class FormApply : Form
    {
        private Label lblTitle;
        private Label lblAccount;
        private Label lblMinutes;
        private ComboBox cmbMinutes;
        private Label lblPurpose;
        private TextBox txtPurpose;
        private Button btnSubmit;
        private Button btnCancel;

        private readonly FeishuConfig cfg;
        private FeishuAccountInfo account;
        private bool accountLoaded;
        private string sessionRecordId;
        private string createdName;
        private bool submitted;
        private DateTime deadline;
        private System.Threading.Timer pollTimer;
        private readonly object pollSync = new object();
        private bool finished;

        public bool Approved { get; private set; }

        public FormApply()
        {
            cfg = FeishuConfig.Current;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "申请使用电脑";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            TopMost = true;
            ClientSize = new Size(420, 300);
            Font = new Font("微软雅黑", 10F);

            lblTitle = new Label();
            lblTitle.Text = "向家长申请使用";
            lblTitle.Font = new Font("微软雅黑", 14F, FontStyle.Bold);
            lblTitle.Location = new Point(20, 15);
            lblTitle.Size = new Size(380, 30);

            lblAccount = new Label();
            lblAccount.Text = "正在读取余额…";
            lblAccount.Location = new Point(20, 55);
            lblAccount.Size = new Size(380, 50);

            lblMinutes = new Label();
            lblMinutes.Text = "预计使用时长：";
            lblMinutes.Location = new Point(20, 118);
            lblMinutes.Size = new Size(120, 26);

            cmbMinutes = new ComboBox();
            cmbMinutes.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMinutes.Location = new Point(145, 115);
            cmbMinutes.Size = new Size(130, 26);
            cmbMinutes.Items.AddRange(new object[] { "30 分钟", "60 分钟", "90 分钟", "120 分钟" });
            cmbMinutes.SelectedIndex = NearestIndex(cfg.defaultRequestMinutes);

            lblPurpose = new Label();
            lblPurpose.Text = "用途（选填）：";
            lblPurpose.Location = new Point(20, 158);
            lblPurpose.Size = new Size(120, 26);

            txtPurpose = new TextBox();
            txtPurpose.Location = new Point(145, 155);
            txtPurpose.Size = new Size(255, 26);

            btnSubmit = new Button();
            btnSubmit.Text = "提交申请";
            btnSubmit.Location = new Point(120, 235);
            btnSubmit.Size = new Size(120, 36);
            btnSubmit.Enabled = false;
            btnSubmit.Click += new EventHandler(btnSubmit_Click);

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(260, 235);
            btnCancel.Size = new Size(120, 36);
            btnCancel.Click += new EventHandler(btnCancel_Click);

            Controls.Add(lblTitle);
            Controls.Add(lblAccount);
            Controls.Add(lblMinutes);
            Controls.Add(cmbMinutes);
            Controls.Add(lblPurpose);
            Controls.Add(txtPurpose);
            Controls.Add(btnSubmit);
            Controls.Add(btnCancel);

            Load += new EventHandler(FormApply_Load);
            FormClosing += new FormClosingEventHandler(FormApply_FormClosing);
        }

        private static int NearestIndex(int minutes)
        {
            if (minutes >= 120) return 3;
            if (minutes >= 90) return 2;
            if (minutes >= 60) return 1;
            return 0;
        }

        private static int ParseMinutes(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            string digits = "";
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                {
                    digits += c;
                }
            }
            int v;
            return int.TryParse(digits, out v) ? v : 0;
        }

        private void FormApply_Load(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                FeishuAccountInfo acc = null;
                string err = null;
                try
                {
                    acc = FeishuClient.GetAccount(cfg.memberName);
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        account = acc;
                        accountLoaded = true;
                        if (acc == null)
                        {
                            lblAccount.Text = err != null
                                ? ("读取余额失败：" + err)
                                : ("未在电脑表中找到「" + cfg.memberName + "」，请联系家长先建立该电脑。");
                            btnSubmit.Enabled = false;
                            return;
                        }
                        lblAccount.Text = "电脑：" + (string.IsNullOrEmpty(acc.Name) ? cfg.memberName : acc.Name)
                            + "\n当前余额：" + acc.Balance + " 分钟";
                        btnSubmit.Enabled = true;
                    }));
                }
                catch
                {
                }
            });
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            if (submitted || !accountLoaded || account == null)
            {
                return;
            }
            int est = ParseMinutes(cmbMinutes.SelectedItem as string);
            string purpose = txtPurpose.Text.Trim();
            btnSubmit.Enabled = false;
            cmbMinutes.Enabled = false;
            txtPurpose.Enabled = false;

            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                string rid = null;
                string name = null;
                try
                {
                    name = UsageSession.NewSessionName();
                    Dictionary<string, object> f = UsageSession.BuildOnlineSessionFields(
                        name, account.RecordId, FeishuClient.ToUnixMs(FeishuClient.ServerNow), est, purpose);
                    rid = FeishuClient.CreateSession(f);
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (err != null || string.IsNullOrEmpty(rid))
                        {
                            submitted = false;
                            lblAccount.Text = "提交申请失败：" + (err == null ? "未知错误" : err);
                            btnSubmit.Enabled = true;
                            cmbMinutes.Enabled = true;
                            txtPurpose.Enabled = true;
                            return;
                        }
                        submitted = true;
                        sessionRecordId = rid;
                        createdName = name;
                        deadline = DateTime.Now.AddMinutes(cfg.pendingTimeoutMinutes);
                        lblTitle.Text = "已提交，等待家长批准…";
                        lblAccount.Text = "请家长在飞书里点击【批准】。\n批准后将自动进入桌面，无需输入动态密码。";
                        btnCancel.Text = "撤销申请";
                        StartPolling();
                    }));
                }
                catch
                {
                }
            });
        }

        private void StartPolling()
        {
            int ms = Math.Max(2000, cfg.pollSeconds * 1000);
            pollTimer = new System.Threading.Timer(Poll, null, ms, ms);
        }

        private void StopPolling()
        {
            lock (pollSync)
            {
                if (pollTimer != null)
                {
                    try { pollTimer.Dispose(); }
                    catch { }
                    pollTimer = null;
                }
            }
        }

        private void Poll(object o)
        {
            lock (pollSync)
            {
                if (finished) return;
            }
            if (DateTime.Now > deadline)
            {
                CancelApplication("超时未批准");
                return;
            }
            string status;
            try
            {
                status = FeishuClient.GetSessionStatus(sessionRecordId);
            }
            catch
            {
                return;
            }
            if (string.IsNullOrEmpty(status))
            {
                return;
            }
            if (status.IndexOf("已批准", StringComparison.Ordinal) >= 0)
            {
                FinishApproved();
            }
            else if (status.IndexOf("已拒绝", StringComparison.Ordinal) >= 0)
            {
                FinishRejected();
            }
            else if (status.IndexOf("已取消", StringComparison.Ordinal) >= 0)
            {
                FinishCanceled();
            }
        }

        private void FinishApproved()
        {
            lock (pollSync)
            {
                if (finished) return;
                finished = true;
            }
            StopPolling();
            try
            {
                UsageSession.StartOnline(sessionRecordId, account.RecordId, createdName);
            }
            catch
            {
            }
            SafeClose(true);
        }

        private void FinishRejected()
        {
            lock (pollSync)
            {
                if (finished) return;
                finished = true;
            }
            StopPolling();
            try
            {
                BeginInvoke(new Action(delegate
                {
                    MessageBox.Show(this, "家长拒绝了本次申请。", "申请被拒绝", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            }
            catch
            {
            }
            SafeClose(false);
        }

        private void FinishCanceled()
        {
            lock (pollSync)
            {
                if (finished) return;
                finished = true;
            }
            StopPolling();
            SafeClose(false);
        }

        private void CancelApplication(string reason)
        {
            lock (pollSync)
            {
                if (finished) return;
                finished = true;
            }
            StopPolling();
            if (submitted && !string.IsNullOrEmpty(sessionRecordId))
            {
                string rid = sessionRecordId;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Dictionary<string, object> f = new Dictionary<string, object>();
                        f["状态"] = new object[] { "已取消" };
                        FeishuClient.UpdateSession(rid, f);
                    }
                    catch
                    {
                    }
                });
            }
            SafeClose(false);
        }

        private void SafeClose(bool approved)
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    Approved = approved;
                    DialogResult = approved ? DialogResult.OK : DialogResult.Cancel;
                    Close();
                }));
            }
            catch
            {
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            CancelApplication("用户取消");
        }

        private void FormApply_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopPolling();
        }
    }
}
