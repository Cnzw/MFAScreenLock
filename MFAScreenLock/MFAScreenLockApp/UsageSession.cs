using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MFAScreenLockApp
{
    public class SessionRecord
    {
        public string recordId { get; set; }
        public string accountRecordId { get; set; }
        public long firstUnlockMs { get; set; }
        public long lastHeartbeatMs { get; set; }
        public bool settled { get; set; }
        public bool offline { get; set; }
        public string device { get; set; }
        public string sessionName { get; set; }
    }

    public static class UsageSession
    {
        private static readonly object sync = new object();
        private static SessionRecord current;

        public static string FilePath
        {
            get { return Path.Combine(FeishuConfig.DirPath, "session.json"); }
        }

        public static SessionRecord Current
        {
            get { lock (sync) { return current; } }
        }

        public static bool HasActive
        {
            get { lock (sync) { return current != null && !current.settled; } }
        }

        public static void Load()
        {
            lock (sync)
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        current = null;
                        return;
                    }
                    string json = File.ReadAllText(FilePath, Encoding.UTF8);
                    if (string.IsNullOrEmpty(json.Trim()))
                    {
                        current = null;
                        return;
                    }
                    current = new JavaScriptSerializer().Deserialize<SessionRecord>(json);
                }
                catch
                {
                    current = null;
                }
            }
        }

        private static void SaveInternal()
        {
            try
            {
                FeishuConfig.EnsureDir();
                if (current == null)
                {
                    if (File.Exists(FilePath))
                    {
                        File.Delete(FilePath);
                    }
                    return;
                }
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(current), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static void Clear()
        {
            lock (sync)
            {
                current = null;
                SaveInternal();
            }
        }

        public static string NewSessionName()
        {
            DateTime t = FeishuClient.ServerNow;
            return FeishuConfig.Current.ResolvedDeviceName + " " + t.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static void StartOnline(string recordId, string accountRecordId, string sessionName)
        {
            lock (sync)
            {
                SessionRecord s = new SessionRecord();
                s.recordId = recordId;
                s.accountRecordId = accountRecordId;
                s.sessionName = sessionName;
                s.device = FeishuConfig.Current.ResolvedDeviceName;
                s.firstUnlockMs = FeishuClient.ToUnixMs(FeishuClient.ServerNow);
                s.lastHeartbeatMs = s.firstUnlockMs;
                s.settled = false;
                s.offline = false;
                current = s;
                SaveInternal();
            }
        }

        public static void StartOffline()
        {
            lock (sync)
            {
                SessionRecord s = new SessionRecord();
                s.recordId = null;
                s.accountRecordId = null;
                s.sessionName = NewSessionName();
                s.device = FeishuConfig.Current.ResolvedDeviceName;
                s.firstUnlockMs = FeishuClient.ToUnixMs(FeishuClient.ServerNow);
                s.lastHeartbeatMs = s.firstUnlockMs;
                s.settled = false;
                s.offline = true;
                current = s;
                SaveInternal();
            }
        }

        public static void DoHeartbeat()
        {
            SessionRecord s;
            lock (sync)
            {
                s = current;
                if (s == null || s.settled)
                {
                    return;
                }
                s.lastHeartbeatMs = FeishuClient.ToUnixMs(FeishuClient.ServerNow);
                SaveInternal();
            }
            if (s.offline || string.IsNullOrEmpty(s.recordId))
            {
                return;
            }
            try
            {
                Dictionary<string, object> f = new Dictionary<string, object>();
                f["最后心跳"] = s.lastHeartbeatMs;
                f["状态"] = new object[] { "使用中" };
                FeishuClient.UpdateSession(s.recordId, f);
            }
            catch
            {
            }
        }

        public static void Settle(string reason, double idleSeconds)
        {
            SessionRecord s;
            lock (sync)
            {
                s = current;
                if (s == null || s.settled)
                {
                    return;
                }
                s.settled = true;
            }

            long nowMs = FeishuClient.ToUnixMs(FeishuClient.ServerNow);
            long lastActiveMs = nowMs - (long)(idleSeconds * 1000.0);
            if (lastActiveMs < s.firstUnlockMs)
            {
                lastActiveMs = s.firstUnlockMs;
            }
            long minutes;
            if (!ActivityWatchClient.TryGetGameMinutes(s.firstUnlockMs, nowMs, out minutes))
            {
                minutes = (long)Math.Ceiling(Math.Max(0L, lastActiveMs - s.firstUnlockMs) / 60000.0);
            }

            lock (sync)
            {
                s.lastHeartbeatMs = lastActiveMs;
                SaveInternal();
            }

            string qid = OfflineQueue.Enqueue(s.recordId, s.accountRecordId, s.firstUnlockMs, lastActiveMs, minutes, reason, s.offline ? "离线补录" : "在线", s.device);

            lock (sync)
            {
                current = null;
                SaveInternal();
            }

            TryFlushNow(qid);
        }

        public static void RecoverOnStartup()
        {
            SessionRecord s;
            lock (sync)
            {
                s = current;
                if (s == null || s.settled)
                {
                    return;
                }
                s.settled = true;
                SaveInternal();
            }

            long minutes = (long)Math.Ceiling(Math.Max(0L, s.lastHeartbeatMs - s.firstUnlockMs) / 60000.0);
            OfflineQueue.Enqueue(s.recordId, s.accountRecordId, s.firstUnlockMs, s.lastHeartbeatMs, minutes, "启动补账", s.offline ? "离线补录" : "在线", s.device);

            lock (sync)
            {
                current = null;
                SaveInternal();
            }
        }

        private static void TryFlushNow(string qid)
        {
            try
            {
                if (FeishuClient.CheckFeishu() == FeishuClient.NetState.Online)
                {
                    OfflineQueue.TryFlushId(qid);
                }
            }
            catch
            {
            }
        }

        public static Dictionary<string, object> BuildOnlineSessionFields(string name, string accountRecordId, long requestMs, int estMinutes, string purpose)
        {
            Dictionary<string, object> f = new Dictionary<string, object>();
            f["会话"] = name;
            if (!string.IsNullOrEmpty(accountRecordId))
            {
                f["电脑"] = new object[] { new Dictionary<string, object> { { "id", accountRecordId } } };
            }
            f["申请时间"] = requestMs;
            if (estMinutes > 0)
            {
                f["预计时长(分钟)"] = estMinutes;
            }
            if (!string.IsNullOrEmpty(purpose))
            {
                f["用途"] = purpose;
            }
            f["状态"] = new object[] { "待批准" };
            f["来源"] = new object[] { "在线" };
            f["设备"] = FeishuConfig.Current.ResolvedDeviceName;
            return f;
        }

        public static Dictionary<string, object> BuildOfflineSessionFields(string accountRecordId, string name, long firstUnlockMs, long lastActiveMs, long minutes)
        {
            Dictionary<string, object> f = new Dictionary<string, object>();
            f["会话"] = name;
            if (!string.IsNullOrEmpty(accountRecordId))
            {
                f["电脑"] = new object[] { new Dictionary<string, object> { { "id", accountRecordId } } };
            }
            f["申请时间"] = firstUnlockMs;
            f["状态"] = new object[] { "已结束" };
            f["来源"] = new object[] { "离线补录" };
            f["实际时长(分钟)"] = minutes;
            f["已结算"] = true;
            f["首次解锁"] = firstUnlockMs;
            f["最后心跳"] = lastActiveMs;
            f["设备"] = FeishuConfig.Current.ResolvedDeviceName;
            return f;
        }

        public static Dictionary<string, object> BuildSettleFields(long firstUnlockMs, long lastActiveMs, long minutes)
        {
            Dictionary<string, object> f = new Dictionary<string, object>();
            f["状态"] = new object[] { "已结束" };
            f["实际时长(分钟)"] = minutes;
            f["已结算"] = true;
            f["首次解锁"] = firstUnlockMs;
            f["最后心跳"] = lastActiveMs;
            return f;
        }

        public static Dictionary<string, object> BuildLedgerFields(string sessionRecordId, long minutes, string note)
        {
            Dictionary<string, object> f = new Dictionary<string, object>();
            f["摘要"] = "游戏扣减 " + minutes + " 分钟";
            f["电脑"] = FeishuConfig.Current.memberName;
            f["类型"] = new object[] { "使用扣减" };
            f["数量(分钟)"] = -minutes;
            f["时间"] = FeishuClient.ToUnixMs(FeishuClient.ServerNow);
            if (!string.IsNullOrEmpty(note))
            {
                f["备注"] = note;
            }
            if (!string.IsNullOrEmpty(sessionRecordId))
            {
                f["关联记录"] = new object[] { new Dictionary<string, object> { { "id", sessionRecordId } } };
            }
            return f;
        }
    }
}
