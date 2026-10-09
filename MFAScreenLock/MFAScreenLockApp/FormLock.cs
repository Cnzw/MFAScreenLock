using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using Google.Authenticator;
using MFAScreenLockApp.Properties;
using System.IO;

namespace MFAScreenLockApp
{
    public enum LockScene { Unlock, Config, Exit, Preview }

    public partial class FormLock : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow(); //获得本窗体的句柄
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);//设置此窗体为活动窗体
        private IntPtr Handle1;

        public int ws = 0;
        private TwoFactorAuthenticator tfa = new TwoFactorAuthenticator();
        private HotKeyHandler hook = new HotKeyHandler();
        private Bitmap wallPaperBmp;
        private double wallPaperlig = -1;
        public bool previewMode = false;
        private int pwdEnableTime = 0;
        private int pwdEnableNow = 0;
        public bool debugMode = false;
        private bool windowOpen = true;
        public LockScene Scene = LockScene.Unlock;
        private bool dialogOpen = false;
        private bool feishuWorking = false;
        private Button btn_apply;
        private Panel applyPanel;
        private Label lbl_apply_minutes;
        private ComboBox cmb_apply_minutes;
        private Label lbl_apply_purpose;
        private TextBox txt_apply_purpose;
        private Button btn_apply_submit;
        private Button btn_apply_cancel;
        private string applyAccountRecordId;
        private string applyAccountName;
        private string applyRecordId;
        private string applySessionName;
        private bool applySubmitted;
        private bool applyFinished;
        private DateTime applyDeadline;
        private System.Threading.Timer applyPollTimer;
        private readonly object applyPollSync = new object();

        public FormLock()
        {
            InitializeComponent();
            btn_enter.Width = txt_pwdcode.Height;
            btn_enter.Height = txt_pwdcode.Height;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams paras = base.CreateParams;
                paras.ExStyle |= 0x02000000;
                return paras;
            }
        }

        public void setBackgroundImage(Bitmap newWallPaperBmp)
        {
            if (newWallPaperBmp != null)
            {
                Size toSize = SystemInformation.PrimaryMonitorSize;
                BackgroundImage = ShareClass.autoScaleBitmap(newWallPaperBmp, toSize);
            }
            BackgroundImageLayout = ShareClass.imageLayout();
        }

        private void FormLock_Load(object sender, EventArgs e)
        {
            if (!previewMode && Settings.Default.AccountSecretKey == "")
            {
                ws = 1;
                aClose();
                return;
            }
            loadFonts();
            hook.HookStart();
            Handle1 = this.Handle;
            lbl_user.Text = Environment.UserName;
            updatedate();
            txt_pwdcode.Focus();
            SetupFeishu();
        }

        private void SetupFeishu()
        {
            if (previewMode || Scene != LockScene.Unlock)
            {
                return;
            }
            if (!FeishuConfig.Current.IsUsable)
            {
                return;
            }
            FeishuGate.Reset();
            FeishuGate.PrefetchAsync(null);
            try
            {
                btn_apply = new Button();
                btn_apply.Text = "申请使用（飞书审批）";
                btn_apply.FlatStyle = FlatStyle.Popup;
                btn_apply.BackColor = Color.Transparent;
                btn_apply.ForeColor = lbl_info.ForeColor;
                btn_apply.Font = lbl_info.Font;
                btn_apply.Cursor = Cursors.Hand;
                btn_apply.Size = new Size(294, 40);
                btn_apply.Location = new Point(0, 182);
                btn_apply.Click += new EventHandler(btn_apply_Click);
                panel1.Controls.Add(btn_apply);

                applyPanel = new Panel();
                applyPanel.BackColor = Color.Transparent;
                applyPanel.Location = new Point(0, 136);
                applyPanel.Size = new Size(294, 175);
                applyPanel.Visible = false;

                lbl_apply_minutes = new Label();
                lbl_apply_minutes.Text = "预计使用时长：";
                lbl_apply_minutes.BackColor = Color.Transparent;
                lbl_apply_minutes.ForeColor = lbl_info.ForeColor;
                lbl_apply_minutes.Font = lbl_info.Font;
                lbl_apply_minutes.Location = new Point(0, 6);
                lbl_apply_minutes.Size = new Size(120, 26);
                applyPanel.Controls.Add(lbl_apply_minutes);

                cmb_apply_minutes = new ComboBox();
                cmb_apply_minutes.DropDownStyle = ComboBoxStyle.DropDownList;
                cmb_apply_minutes.Location = new Point(124, 3);
                cmb_apply_minutes.Size = new Size(130, 26);
                cmb_apply_minutes.Items.AddRange(new object[] { "30 分钟", "60 分钟", "90 分钟", "120 分钟" });
                int reqMin = FeishuConfig.Current.defaultRequestMinutes;
                int reqIdx = 0;
                if (reqMin >= 120) reqIdx = 3; else if (reqMin >= 90) reqIdx = 2; else if (reqMin >= 60) reqIdx = 1;
                cmb_apply_minutes.SelectedIndex = reqIdx;
                applyPanel.Controls.Add(cmb_apply_minutes);

                lbl_apply_purpose = new Label();
                lbl_apply_purpose.Text = "用途（选填）：";
                lbl_apply_purpose.BackColor = Color.Transparent;
                lbl_apply_purpose.ForeColor = lbl_info.ForeColor;
                lbl_apply_purpose.Font = lbl_info.Font;
                lbl_apply_purpose.Location = new Point(0, 40);
                lbl_apply_purpose.Size = new Size(120, 26);
                applyPanel.Controls.Add(lbl_apply_purpose);

                txt_apply_purpose = new TextBox();
                txt_apply_purpose.Location = new Point(0, 68);
                txt_apply_purpose.Size = new Size(290, 26);
                applyPanel.Controls.Add(txt_apply_purpose);

                btn_apply_submit = new Button();
                btn_apply_submit.Text = "提交申请";
                btn_apply_submit.Font = lbl_info.Font;
                btn_apply_submit.Cursor = Cursors.Hand;
                btn_apply_submit.Location = new Point(0, 104);
                btn_apply_submit.Size = new Size(140, 38);
                btn_apply_submit.Click += new EventHandler(btn_apply_submit_Click);
                applyPanel.Controls.Add(btn_apply_submit);

                btn_apply_cancel = new Button();
                btn_apply_cancel.Text = "取消";
                btn_apply_cancel.Font = lbl_info.Font;
                btn_apply_cancel.Cursor = Cursors.Hand;
                btn_apply_cancel.Location = new Point(150, 104);
                btn_apply_cancel.Size = new Size(140, 38);
                btn_apply_cancel.Click += new EventHandler(btn_apply_cancel_Click);
                applyPanel.Controls.Add(btn_apply_cancel);

                panel1.Controls.Add(applyPanel);
            }
            catch
            {
            }
            lbl_info.Text = "请输入动态密码，或点击下方按钮申请使用";
        }

        private void btn_apply_Click(object sender, EventArgs e)
        {
            enter();
        }

        private void updatedate()
        {
            DateTime now = DateTime.Now;
            lbl_time.Text = now.Hour.ToString().PadLeft(2, '0') + ":" + now.Minute.ToString().PadLeft(2, '0') + ":" + now.Second.ToString().PadLeft(2, '0');
            lbl_date.Text = now.Year.ToString() + " 年 " + now.Month.ToString() + " 月 " + now.Day.ToString() + " 日 ";
        }

        public void stopTimer()
        {
            timer1.Enabled = false;
            timer2.Enabled = false;
        }

        private void label5_Click(object sender, EventArgs e)
        {
            softkeyboard.Visible = !softkeyboard.Visible;
        }

        private void softkeyboardbtn_Click(object sender, EventArgs e)
        {
            Button button = (Button)sender;
            txt_pwdcode.Text += button.Text;
            txt_pwdcode.Focus();
        }

        private void button11_Click(object sender, EventArgs e)
        {
            string pwdcode = txt_pwdcode.Text;
            if (pwdcode.Length > 0)
            {
                txt_pwdcode.Text = pwdcode.Substring(0, pwdcode.Length - 1);
            }
            txt_pwdcode.Focus();
        }

        private void button12_Click(object sender, EventArgs e)
        {
            txt_pwdcode.Text = "";
            txt_pwdcode.Focus();
        }

        private bool pass(string pwd, string key = "")
        {
            if (Settings.Default.AccountSecretKey.Length == 0) return true;
            if (pwd.Length == 0) return false;
            if (Settings.Default.MachineName != Environment.MachineName) return false;
            if (Settings.Default.UserDomainName != Environment.UserDomainName) return false;
            if (Settings.Default.UserName != Environment.UserName) return false;
            if (key.Length == 0) key = Settings.Default.AccountSecretKey;
            return tfa.ValidateTwoFactorPIN(key, pwd);
        }

        public void fClose()
        {
            ws = 1;
            Close();
        }

        private void enter()
        {
            if (debugMode)
            {
                ws = 1;
                aClose();
            }
            if (previewMode)
            {
                return;
            }
            FeishuConfig cfg = FeishuConfig.Current;
            if (ws != 1 && Scene == LockScene.Unlock && cfg.IsUsable)
            {
                UnlockViaFeishu(cfg);
                return;
            }
            if (txt_pwdcode.Text.Length == 6)
            {
                if (pass(txt_pwdcode.Text))
                {
                    ws = 1;
                    aClose();
                }
            }
            else if (txt_pwdcode.Text.Length == txt_pwdcode.MaxLength)
            {
                if (txt_pwdcode.Text == Settings.Default.RecoveryCode)
                {
                    ws = 1;
                    aClose();
                }
            }
            if (ws != 1)
            {
                passwordError();
            }
        }

        private void UnlockViaFeishu(FeishuConfig cfg)
        {
            if (feishuWorking)
            {
                return;
            }
            if (ShareClass.inBypassWindow())
            {
                ws = 1;
                aClose();
                return;
            }
            NetState st = FeishuGate.State;
            if (st == NetState.Unknown)
            {
                feishuWorking = true;
                lbl_info.Text = "正在连接飞书，请稍候…";
                FeishuGate.PrefetchAsync(delegate
                {
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            feishuWorking = false;
                            UnlockViaFeishu(cfg);
                        }));
                    }
                    catch
                    {
                    }
                });
                return;
            }
            if (st == NetState.Online)
            {
                feishuWorking = true;
                try
                {
                    ShowApplyPanel();
                }
                finally
                {
                    feishuWorking = false;
                }
                return;
            }
            if (pass(txt_pwdcode.Text))
            {
                try
                {
                    UsageSession.StartOffline();
                }
                catch
                {
                }
                ws = 1;
                aClose();
            }
            else if (txt_pwdcode.Text.Length > 0)
            {
                passwordError();
            }
            else if (st == NetState.BadData)
            {
                lbl_info.Text = "飞书数据异常，请输入动态密码即可进入";
            }
            else
            {
                lbl_info.Text = "当前断网：请输入动态密码即可进入";
            }
        }

        private void ShowApplyPanel()
        {
            if (applyPanel == null)
            {
                lbl_info.Text = "申请界面初始化失败，请改用动态密码解锁";
                return;
            }
            applyFinished = false;
            applySubmitted = false;
            applyRecordId = null;
            applySessionName = null;
            txt_pwdcode.Visible = false;
            btn_enter.Visible = false;
            label5.Visible = false;
            if (btn_apply != null) btn_apply.Visible = false;
            applyPanel.Visible = true;
            applyPanel.BringToFront();
            cmb_apply_minutes.Enabled = true;
            txt_apply_purpose.Enabled = true;
            txt_apply_purpose.Text = "";
            btn_apply_submit.Enabled = false;
            btn_apply_cancel.Enabled = true;
            btn_apply_cancel.Text = "取消";
            lbl_info.Text = "正在读取余额…";
            System.Threading.Tasks.Task.Run(new Action(delegate
            {
                FeishuAccountInfo acc = null;
                string err = null;
                try
                {
                    acc = FeishuClient.GetAccount(FeishuConfig.Current.memberName);
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }
                SafeInvoke(delegate
                {
                    if (acc == null)
                    {
                        lbl_info.Text = err != null
                            ? ("读取余额失败：" + err)
                            : ("未在电脑表中找到「" + FeishuConfig.Current.memberName + "」");
                        return;
                    }
                    applyAccountRecordId = acc.RecordId;
                    applyAccountName = string.IsNullOrEmpty(acc.Name) ? FeishuConfig.Current.memberName : acc.Name;
                    lbl_info.Text = "电脑：" + applyAccountName + "\n当前余额：" + acc.Balance + " 分钟";
                    btn_apply_submit.Enabled = true;
                });
            }));
        }

        private void btn_apply_submit_Click(object sender, EventArgs e)
        {
            if (applySubmitted || applyPanel == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(applyAccountRecordId))
            {
                return;
            }
            int est = ApplyParseMinutes(cmb_apply_minutes.SelectedItem as string);
            string purpose = txt_apply_purpose.Text.Trim();
            applySubmitted = true;
            btn_apply_submit.Enabled = false;
            cmb_apply_minutes.Enabled = false;
            txt_apply_purpose.Enabled = false;
            lbl_info.Text = "正在提交申请…";
            System.Threading.Tasks.Task.Run(new Action(delegate
            {
                string err = null;
                string rid = null;
                string name = null;
                try
                {
                    name = UsageSession.NewSessionName();
                    rid = FeishuClient.CreateSession(UsageSession.BuildOnlineSessionFields(
                        name, applyAccountRecordId, FeishuClient.ToUnixMs(FeishuClient.ServerNow), est, purpose));
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }
                SafeInvoke(delegate
                {
                    if (err != null || string.IsNullOrEmpty(rid))
                    {
                        applySubmitted = false;
                        lbl_info.Text = "提交申请失败：" + (err == null ? "未知错误" : err);
                        btn_apply_submit.Enabled = true;
                        cmb_apply_minutes.Enabled = true;
                        txt_apply_purpose.Enabled = true;
                        return;
                    }
                    applyRecordId = rid;
                    applySessionName = name;
                    applyDeadline = DateTime.Now.AddMinutes(FeishuConfig.Current.pendingTimeoutMinutes);
                    lbl_info.Text = "已提交，等待家长批准…\n请在飞书点【批准】，批准后自动进入";
                    btn_apply_cancel.Text = "撤销申请";
                    StartApplyPoll();
                });
            }));
        }

        private void btn_apply_cancel_Click(object sender, EventArgs e)
        {
            ApplyCancel("用户取消");
        }

        private void StartApplyPoll()
        {
            int ms = Math.Max(2000, FeishuConfig.Current.pollSeconds * 1000);
            lock (applyPollSync)
            {
                applyPollTimer = new System.Threading.Timer(ApplyPoll, null, ms, ms);
            }
        }

        private void StopApplyPoll()
        {
            lock (applyPollSync)
            {
                if (applyPollTimer != null)
                {
                    try { applyPollTimer.Dispose(); }
                    catch { }
                    applyPollTimer = null;
                }
            }
        }

        private void ApplyPoll(object o)
        {
            lock (applyPollSync)
            {
                if (applyFinished)
                {
                    return;
                }
            }
            if (DateTime.Now > applyDeadline)
            {
                ApplyCancel("超时未批准");
                return;
            }
            string st;
            try
            {
                st = FeishuClient.GetSessionStatus(applyRecordId);
            }
            catch
            {
                return;
            }
            if (string.IsNullOrEmpty(st))
            {
                return;
            }
            if (st.IndexOf("已批准", StringComparison.Ordinal) >= 0)
            {
                ApplyApproved();
            }
            else if (st.IndexOf("已拒绝", StringComparison.Ordinal) >= 0)
            {
                ApplyRejected();
            }
            else if (st.IndexOf("已取消", StringComparison.Ordinal) >= 0)
            {
                ApplyFinishCanceled();
            }
        }

        private void ApplyApproved()
        {
            lock (applyPollSync)
            {
                if (applyFinished)
                {
                    return;
                }
                applyFinished = true;
            }
            StopApplyPoll();
            try
            {
                UsageSession.StartOnline(applyRecordId, applyAccountRecordId, applySessionName);
            }
            catch
            {
            }
            SafeInvoke(delegate
            {
                ws = 1;
                aClose();
            });
        }

        private void ApplyRejected()
        {
            lock (applyPollSync)
            {
                if (applyFinished)
                {
                    return;
                }
                applyFinished = true;
            }
            StopApplyPoll();
            SafeInvoke(delegate
            {
                ResetApplyPanel();
                lbl_info.Text = "家长拒绝了本次申请";
            });
        }

        private void ApplyFinishCanceled()
        {
            lock (applyPollSync)
            {
                if (applyFinished)
                {
                    return;
                }
                applyFinished = true;
            }
            StopApplyPoll();
            SafeInvoke(delegate
            {
                ResetApplyPanel();
                lbl_info.Text = "申请已被取消";
            });
        }

        private void ApplyCancel(string reason)
        {
            lock (applyPollSync)
            {
                if (applyFinished)
                {
                    return;
                }
                applyFinished = true;
            }
            StopApplyPoll();
            bool wasSubmitted = applySubmitted;
            string rid = applyRecordId;
            if (wasSubmitted && !string.IsNullOrEmpty(rid))
            {
                string cancelRid = rid;
                System.Threading.Tasks.Task.Run(new Action(delegate
                {
                    try
                    {
                        Dictionary<string, object> f = new Dictionary<string, object>();
                        f["状态"] = new object[] { "已取消" };
                        FeishuClient.UpdateSession(cancelRid, f);
                    }
                    catch
                    {
                    }
                }));
            }
            SafeInvoke(delegate
            {
                ResetApplyPanel();
                lbl_info.Text = wasSubmitted ? ("申请已撤销（" + reason + "）") : "请输入动态密码，或点击下方按钮申请使用";
            });
        }

        private void ResetApplyPanel()
        {
            applySubmitted = false;
            applyRecordId = null;
            applySessionName = null;
            if (applyPanel != null)
            {
                applyPanel.Visible = false;
            }
            txt_pwdcode.Visible = true;
            btn_enter.Visible = true;
            label5.Visible = true;
            if (btn_apply != null)
            {
                btn_apply.Visible = true;
            }
            if (cmb_apply_minutes != null) cmb_apply_minutes.Enabled = true;
            if (txt_apply_purpose != null) txt_apply_purpose.Enabled = true;
            if (btn_apply_submit != null) btn_apply_submit.Enabled = true;
            if (btn_apply_cancel != null)
            {
                btn_apply_cancel.Enabled = true;
                btn_apply_cancel.Text = "取消";
            }
            try { txt_pwdcode.Focus(); }
            catch { }
        }

        private static int ApplyParseMinutes(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }
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

        private void SafeInvoke(Action a)
        {
            try { BeginInvoke(a); }
            catch { }
        }

        private void txt_pwdcode_TextChanged(object sender, EventArgs e)
        {
            //enter();
        }

        private Color gColor()
        {
            if (wallPaperlig == -1)
            {
                wallPaperlig = ImageControl.CalculateAverageLightness(wallPaperBmp);
            }
            if (wallPaperlig > 0.5)
            {
                return Color.Black;
            }
            return Color.White;
        }
        public void loadFonts()
        {
            if (Settings.Default.FontTime != null)
            {
                lbl_time.Font = Settings.Default.FontTime;
            }
            if (Settings.Default.FontDate != null)
            {
                lbl_date.Font = Settings.Default.FontDate;
            }
            if (Settings.Default.FontUser != null)
            {
                lbl_user.Font = Settings.Default.FontUser;
            }
            if (Settings.Default.FontMenu != null)
            {
                Font = Settings.Default.FontMenu;
            }
            if (Settings.Default.FontInfo != null)
            {
                lbl_info.Font = Settings.Default.FontInfo;
            }
            if (Settings.Default.FontInput != null)
            {
                txt_pwdcode.Font = Settings.Default.FontInput;
            }

            bool nc = !Settings.Default.ColorAuto;
            if (nc && Settings.Default.ColorTime != null)
            {
                lbl_time.ForeColor = Settings.Default.ColorTime;
            }
            else
            {
                lbl_time.ForeColor = gColor();
            }
            if (nc && Settings.Default.ColorDate != null)
            {
                lbl_date.ForeColor = Settings.Default.ColorDate;
            }
            else
            {
                lbl_date.ForeColor = gColor();
            }
            if (nc && Settings.Default.ColorUser != null)
            {
                lbl_user.ForeColor = Settings.Default.ColorUser;
            }
            else
            {
                lbl_user.ForeColor = gColor();
            }
            if (nc && Settings.Default.ColorInfo != null)
            {
                lbl_info.ForeColor = Settings.Default.ColorInfo;
                btn_enter.ForeColor = Settings.Default.ColorInfo;
            }
            else
            {
                lbl_info.ForeColor = gColor();
                btn_enter.ForeColor = lbl_info.ForeColor;
            }
            //if (nc && Settings.Default.ColorInput != null)
            //{
            //    txt_pwdcode.ForeColor = Settings.Default.ColorInput;
            //}
            //else
            //{
            //    txt_pwdcode.ForeColor = gColor();
            //}
            if (nc && Settings.Default.ColorMenu != null)
            {
                ForeColor = Settings.Default.ColorMenu;
            }
            else
            {
                ForeColor = gColor();
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            updatedate();
            if (previewMode) return;
            if (ws != 1 && ShareClass.inBypassWindow())
            {
                ws = 1;
                aClose();
                return;
            }
            if (dialogOpen) return;
            this.Focus();
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if (previewMode) return;
            this.TopMost = true;
            if (dialogOpen) return;
            if (Handle1 != GetForegroundWindow())
            {
                SetForegroundWindow(Handle1);
            }
        }

        private void FormLock_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
        }

        private void FormLock_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ws != 1)
            {
                e.Cancel = true;
            }
            else
            {
                hook.HookStop();
            }
        }

        ~FormLock()
        {
            if (wallPaperBmp != null) wallPaperBmp.Dispose();
            Dispose();
        }

        private void txt_pwdcode_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Control || e.KeyCode == Keys.Enter)
            {
                enter();
            }
        }

        private void btn_enter_Click(object sender, EventArgs e)
        {
            enter();
        }

        private void passwordError()
        {
            txt_pwdcode.Font = new Font(this.Font.FontFamily, 20);
            txt_pwdcode.UseSystemPasswordChar = false;
            txt_pwdcode.Text = "密码不正确";
            txt_pwdcode.BackColor = Color.Tomato;
            txt_pwdcode.ForeColor = Color.Black;
            btn_enter.Text = "";
            txt_pwdcode.Enabled = btn_enter.Enabled = label5.Enabled = softkeyboard.Visible = false;
            pwdEnableTime = Settings.Default.ErrLock;
            pwdEnableNow = Settings.Default.ErrLock;
            timer_err.Enabled = true;
        }

        private void timer_err_Tick(object sender, EventArgs e)
        {
            pwdEnableNow--;
            if (pwdEnableNow < 0)
            {
                txt_pwdcode.Font = new Font("Consolas", 20);
                txt_pwdcode.UseSystemPasswordChar = true;
                txt_pwdcode.Text = "";
                txt_pwdcode.BackColor = SystemColors.Window;
                txt_pwdcode.ForeColor = SystemColors.WindowText;
                txt_pwdcode.Enabled = btn_enter.Enabled = label5.Enabled = true;
                btn_enter.Text = "→";
                timer_err.Enabled = false;
                pwdEnableNow = 0;
                txt_pwdcode.Focus();
            }
            else
            {
                txt_pwdcode.Text = "密码不正确 (" + pwdEnableNow.ToString() + ")";
            }
        }

        private void txt_pwdcode_SizeChanged(object sender, EventArgs e)
        {
            btn_enter.Width = txt_pwdcode.Height;
            btn_enter.Height = txt_pwdcode.Height;
        }

        private void windowTimer_Tick(object sender, EventArgs e)
        {
            if (windowOpen)
            {
                double newOpacity = Opacity + 0.02;
                if (newOpacity >= 1)
                {
                    newOpacity = 1;
                    windowTimer.Enabled = false;
                }
                Opacity = newOpacity;
            }
            else
            {
                double newOpacity = Opacity - 0.02;
                if (newOpacity <= 0)
                {
                    Opacity = 0;
                    windowTimer.Enabled = false;
                    Close();
                }
                else
                {
                    Opacity = newOpacity;
                }
            }
        }

        private void aClose()
        {
            txt_pwdcode.Enabled = btn_enter.Enabled = false;
            windowOpen = false;
            windowTimer.Enabled = true;
        }
    }
}
