using MFAScreenLockApp.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MFAScreenLockApp
{
    public partial class Form1 : Form
    {
        private Bitmap wallPaperBmp;
        private List<FormLockSub> formLockSubList = new List<FormLockSub>();
        private string[] args;
        private bool debugMode = false;
        private System.Windows.Forms.Timer timer_heartbeat;
        private DateTime lastHeartbeat = DateTime.MinValue;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            BeginInvoke(new Action(() => {
                Hide();
            }));
            版本ToolStripMenuItem.Text = "版本：" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string programname = Process.GetCurrentProcess().ProcessName;
            Process[] myProcesses = Process.GetProcessesByName(programname);//获取指定的进程名
            wallPaperBmp = ShareClass.gWallPaperBmp();
            args = Environment.GetCommandLineArgs();
            FeishuStartup(myProcesses.Length > 1);
            timer_heartbeat = new System.Windows.Forms.Timer();
            timer_heartbeat.Interval = 60000;
            timer_heartbeat.Tick += new EventHandler(timer_heartbeat_Tick);
            timer_heartbeat.Enabled = true;
            Application.ApplicationExit += new EventHandler(Application_ApplicationExit);
            if (loadConfig() && myProcesses.Length > 1) //如果可以获取到知道的进程名则说明已经启动
            {
                MessageBox.Show("程序已经启动，请查看任务栏中的图标。\n在图标上点击右键可以打开菜单。\n如果不需要验证后驻留后台，请添加 -e 参数。","程序已在运行",MessageBoxButtons.OK,MessageBoxIcon.Error);
                notifyIcon1.Visible = false;
                Application.Exit();
            }
            if (Settings.Default.Timeout >= 60)
            {
                timer_lock.Enabled = true;
            }
        }

        private void FeishuStartup(bool anotherInstance)
        {
            if (anotherInstance)
            {
                return;
            }
            if (!FeishuConfig.Current.IsUsable)
            {
                return;
            }
            bool keepSession = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--keep-session")
                {
                    keepSession = true;
                }
            }
            try
            {
                UsageSession.Load();
                if (!keepSession)
                {
                    UsageSession.RecoverOnStartup();
                }
            }
            catch
            {
            }
            System.Threading.Tasks.Task.Run(new Action(delegate
            {
                try { OfflineQueue.TryFlushAll(); }
                catch { }
            }));
        }

        private void timer_heartbeat_Tick(object sender, EventArgs e)
        {
            if (!FeishuConfig.Current.IsUsable)
            {
                return;
            }
            int minutes = FeishuConfig.Current.heartbeatMinutes;
            if (lastHeartbeat != DateTime.MinValue && (DateTime.Now - lastHeartbeat).TotalMinutes < minutes)
            {
                return;
            }
            if (!UsageSession.HasActive)
            {
                return;
            }
            lastHeartbeat = DateTime.Now;
            System.Threading.Tasks.Task.Run(new Action(delegate
            {
                try { UsageSession.DoHeartbeat(); }
                catch { }
                try { OfflineQueue.TryFlushAll(); }
                catch { }
            }));
        }

        private void SettleActiveSession(string reason)
        {
            if (!FeishuConfig.Current.IsUsable)
            {
                return;
            }
            if (!UsageSession.HasActive)
            {
                return;
            }
            double idle = SysLink.GetIdleTime() / 1000.0;
            System.Threading.Tasks.Task t = System.Threading.Tasks.Task.Run(new Action(delegate
            {
                try { UsageSession.Settle(reason, idle); }
                catch { }
            }));
            try { t.Wait(10000); }
            catch { }
        }

        private void Application_ApplicationExit(object sender, EventArgs e)
        {
            SettleActiveSession("关机");
        }

        private bool loadConfig()
        {
            if (args.Length > 2 && args[1] == "-r" && args[2] == Settings.Default.RecoveryCode)
            {
                DialogResult result = MessageBox.Show("你使用了一个有效的恢复代码。\n你要清除本程序绑定的验证器吗？\n清除后，本程序将立即清除所有设置并退出。", "恢复模式", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.Yes)
                {
                    Settings.Default.Reset();
                    Restart();
                    return false;
                }
            }
            if (Settings.Default.MachineName == "")
            {
                timer_lock.Enabled = false;
                FormUser formuser = new FormUser();
                formuser.ws = 0;
                bool bindMode = false;
                if (Settings.Default.MachineName.Length == 0)
                {
                    bindMode = true;
                }
                formuser.ShowDialog();
                if (!(bindMode && Settings.Default.MachineName.Length > 0))
                {
                    if (formuser.ws == 1)
                    {
                        notifyIcon1.Visible = false;
                        Application.Exit();
                    }
                    else
                    {
                        if (args.Length > 1 && args[1] == "-e")
                        {
                            notifyIcon1.Visible = false;
                            Application.Exit();
                        }
                        Close();
                    }
                }
                formuser.ws = 0;
                timer_lock.Enabled = true;
            }
            else if (args.Length > 1 && args[1] == "--restart")
            {
                
            }
            else if (ShareClass.inBypassWindow())
            {
                
            }
            else
            {
                locknow();
                return true;
            }
            return false;
        }

        private void locknow()
        {
            timer_lock.Enabled = false;
            lockallscreen(true, wallPaperBmp);
            FormLock formlock = new FormLock();
            formlock.debugMode = debugMode;
            formlock.Scene = LockScene.Unlock;
            formlock.setBackgroundImage(wallPaperBmp);
            if (debugMode)
            {
                formlock.TopMost = false;
            }
            formlock.ShowDialog();
            formlock.ws = 0;
            lockallscreen(false, wallPaperBmp);
            if (args.Length > 1 && args[1] == "-e")
            {
                notifyIcon1.Visible = false;
                Application.Exit();
            }
            else
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            timer_lock.Enabled = true;
        }

        private void lockallscreen(bool islock = true, Bitmap wallPaperBmp = null)
        {
            if (debugMode) return;
            if (islock)
            {
                Screen[] screens = Screen.AllScreens;
                foreach (Screen screen in screens)
                {
                    if (screen.Primary)
                    {
                        continue;
                    }
                    Rectangle area = screen.WorkingArea;
                    Rectangle bound = screen.Bounds;
                    FormLockSub locksub = new FormLockSub();
                    locksub.Top = area.Top;
                    locksub.Left = area.Left;
                    locksub.Show();
                    locksub.WindowState = FormWindowState.Maximized;
                    if (!screen.Primary)
                    {
                        locksub.setBackgroundImage(wallPaperBmp, bound.Size);
                    }
                    formLockSubList.Add(locksub);
                }
            }
            else
            {
                foreach (FormLockSub locksub in formLockSubList)
                {
                    locksub.aClose();
                }
                formLockSubList.RemoveAll(it => true);
            }
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.DrawString("请稍候", new Font(FontFamily.GenericSansSerif, 20), Brushes.Crimson, 30, 30);
        }

        private void 账户管理UToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openconfig(1);
        }

        private void openconfig(Int16 tabindex = 0)
        {
            if (Settings.Default.MachineName == "")
            {
                timer_lock.Enabled = false;
                FormUser formuser = new FormUser();
                formuser.tabControl1.SelectedIndex = tabindex;
                formuser.ShowDialog();
                formuser.ws = 0;
                timer_lock.Enabled = true;
            }
            else
            {
                lockallscreen(true, wallPaperBmp);
                timer_lock.Enabled = false;
                FormLock formlock = new FormLock();
                formlock.debugMode = debugMode;
                formlock.Scene = LockScene.Config;
                formlock.lbl_info.Text = "正在修改设置";
                formlock.setBackgroundImage(wallPaperBmp);
                formlock.ShowDialog();
                lockallscreen(false, wallPaperBmp);
                if (formlock.ws == 1)
                {
                    timer_lock.Enabled = false;
                    FormUser formuser = new FormUser();
                    if (tabindex == 2)
                    {
                        formuser.displayPreview = true;
                    }
                    else
                    {
                        formuser.tabControl1.SelectedIndex = tabindex;
                    }
                    formuser.ws = 0;
                    formuser.ShowDialog();
                    Restart("--restart --keep-session");
                }
                timer_lock.Enabled = true;
                formlock.ws = 0;
            }
        }

        private void 立即锁定LToolStripMenuItem_Click(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;
            Restart();
        }

        private void 退出EToolStripMenuItem_Click(object sender, EventArgs e)
        {
            lockallscreen(true, wallPaperBmp);
            timer_lock.Enabled = false;
            FormLock formlock = new FormLock();
            formlock.debugMode = debugMode;
            formlock.Scene = LockScene.Exit;
            formlock.lbl_info.Text = "正在尝试退出软件";
            formlock.setBackgroundImage(wallPaperBmp);
            formlock.ShowDialog();
            lockallscreen(false, wallPaperBmp);
            if (formlock.ws == 1)
            {
                notifyIcon1.Visible = false;
                Application.Exit();
            }
            formlock.ws = 0;
            timer_lock.Enabled = true;
        }

        private void Restart(string arguments = "")
        {
            if (string.IsNullOrEmpty(arguments))
            {
                SettleActiveSession("手动锁定");
            }
            notifyIcon1.Visible = false;
            Process ps = new Process();
            ps.StartInfo.FileName = Application.ExecutablePath.ToString();
            ps.StartInfo.Arguments = arguments;
            ps.Start();
            Application.Exit();
        }

        private void timer_lock_Tick(object sender, EventArgs e)
        {
            if (ShareClass.inBypassWindow())
            {
                return;
            }
            if (Settings.Default.TimeoutEnable == false)
            {
                timer_lock.Enabled = false;
                return;
            }
            int timeoutcfg = Settings.Default.Timeout;
            if (timeoutcfg >= 60 && SysLink.GetIdleTime() > Settings.Default.Timeout * 1000.0)
            {
                notifyIcon1.Visible = false;
                Restart();
            }
        }

        private void toolStripMenuItem2_Click(object sender, EventArgs e)
        {
            openconfig(0);
        }

        private void 帮助和关于HToolStripMenuItem_Click(object sender, EventArgs e)
        {
            About about = new About();
            about.wallPaperBmp = wallPaperBmp;
            about.Show();
        }

        private void 个性化ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openconfig(2);
        }
    }
}
