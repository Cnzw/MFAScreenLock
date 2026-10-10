using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MFAScreenLockApp
{
    public class OfflineEntry
    {
        public string id { get; set; }
        public string recordId { get; set; }
        public string accountRecordId { get; set; }
        public long firstUnlockMs { get; set; }
        public long lastActiveMs { get; set; }
        public long minutes { get; set; }
        public string reason { get; set; }
        public string source { get; set; }
        public string device { get; set; }
        public long queuedAtUtcMs { get; set; }
    }

    public static class OfflineQueue
    {
        private static readonly object sync = new object();

        public static string FilePath
        {
            get { return Path.Combine(FeishuConfig.DirPath, "offline_queue.json"); }
        }

        private static long NowUtcMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private static List<OfflineEntry> LoadList()
        {
            List<OfflineEntry> list = new List<OfflineEntry>();
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(json.Trim()))
                    {
                        List<OfflineEntry> l = new JavaScriptSerializer().Deserialize<List<OfflineEntry>>(json);
                        if (l != null)
                        {
                            list = l;
                        }
                    }
                }
            }
            catch
            {
                list = new List<OfflineEntry>();
            }
            int maxDays = FeishuConfig.Current.offlineQueueMaxDays;
            if (maxDays > 0 && list.Count > 0)
            {
                long cutoff = NowUtcMs() - (long)maxDays * 86400000L;
                list.RemoveAll(delegate(OfflineEntry e) { return e.queuedAtUtcMs < cutoff; });
            }
            return list;
        }

        private static void SaveList(List<OfflineEntry> list)
        {
            try
            {
                FeishuConfig.EnsureDir();
                if (list == null || list.Count == 0)
                {
                    if (File.Exists(FilePath))
                    {
                        File.Delete(FilePath);
                    }
                    return;
                }
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(list), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static int Count()
        {
            lock (sync)
            {
                return LoadList().Count;
            }
        }

        public static string Enqueue(string recordId, string accountRecordId, long firstUnlockMs, long lastActiveMs, long minutes, string reason, string source, string device)
        {
            lock (sync)
            {
                List<OfflineEntry> list = LoadList();
                OfflineEntry e = new OfflineEntry();
                e.id = Guid.NewGuid().ToString("N");
                e.recordId = recordId;
                e.accountRecordId = accountRecordId;
                e.firstUnlockMs = firstUnlockMs;
                e.lastActiveMs = lastActiveMs;
                e.minutes = minutes;
                e.reason = reason;
                e.source = source;
                e.device = device;
                e.queuedAtUtcMs = NowUtcMs();
                list.Add(e);
                SaveList(list);
                return e.id;
            }
        }

        public static bool TryFlushId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }
            lock (sync)
            {
                List<OfflineEntry> list = LoadList();
                OfflineEntry target = null;
                foreach (OfflineEntry e in list)
                {
                    if (e.id == id)
                    {
                        target = e;
                        break;
                    }
                }
                if (target == null)
                {
                    return true;
                }
                try
                {
                    FlushOne(target);
                }
                catch
                {
                    SaveList(list);
                    return false;
                }
                list.Remove(target);
                SaveList(list);
                return true;
            }
        }

        public static void TryFlushAll()
        {
            lock (sync)
            {
                List<OfflineEntry> list = LoadList();
                if (list.Count == 0)
                {
                    SaveList(list);
                    return;
                }
                if (FeishuClient.CheckFeishu() != FeishuClient.NetState.Online)
                {
                    SaveList(list);
                    return;
                }
                List<OfflineEntry> remaining = new List<OfflineEntry>();
                foreach (OfflineEntry e in list)
                {
                    try
                    {
                        FlushOne(e);
                    }
                    catch
                    {
                        remaining.Add(e);
                    }
                }
                SaveList(remaining);
            }
        }

        private static void FlushOne(OfflineEntry e)
        {
            string accId = e.accountRecordId;
            if (string.IsNullOrEmpty(accId))
            {
                FeishuAccountInfo acc = FeishuClient.GetAccount(FeishuConfig.Current.memberName);
                if (acc != null)
                {
                    accId = acc.RecordId;
                }
            }

            string sessionRecordId = e.recordId;
            if (!string.IsNullOrEmpty(sessionRecordId))
            {
                try
                {
                    FeishuClient.UpdateSession(sessionRecordId, UsageSession.BuildSettleFields(e.firstUnlockMs, e.lastActiveMs, e.minutes));
                }
                catch (FeishuApiException)
                {
                    sessionRecordId = null;
                }
            }

            if (string.IsNullOrEmpty(sessionRecordId))
            {
                sessionRecordId = FeishuClient.CreateSession(
                    UsageSession.BuildOfflineSessionFields(accId, BuildName(e), e.firstUnlockMs, e.lastActiveMs, e.minutes));
                e.recordId = sessionRecordId;
            }

            if (e.minutes > 0)
            {
                string note = (e.source == "离线补录") ? "离线补录" : null;
                FeishuClient.CreateLedger(UsageSession.BuildLedgerFields(sessionRecordId, e.minutes, note));
            }
        }

        private static string BuildName(OfflineEntry e)
        {
            DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(e.firstUnlockMs).ToLocalTime();
            string dev = string.IsNullOrEmpty(e.device) ? FeishuConfig.Current.ResolvedDeviceName : e.device;
            return dev + " " + t.ToString("yyyy-MM-dd HH:mm:ss") + " 离线补录";
        }
    }
}
