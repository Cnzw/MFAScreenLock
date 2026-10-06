using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MFAScreenLockApp
{
    public class FeishuTables
    {
        public string sessions { get; set; }
        public string accounts { get; set; }
        public string ledger { get; set; }
    }

    public class FeishuConfig
    {
        public bool enabled { get; set; }
        public string appId { get; set; }
        public string appSecret { get; set; }
        public string appToken { get; set; }
        public FeishuTables tables { get; set; }
        public string memberName { get; set; }
        public string deviceName { get; set; }
        public int pollSeconds { get; set; }
        public int heartbeatMinutes { get; set; }
        public int defaultRequestMinutes { get; set; }
        public int offlineQueueMaxDays { get; set; }
        public int pendingTimeoutMinutes { get; set; }
        public bool awDisabled { get; set; }
        public string awBaseUrl { get; set; }
        public string awGameCategory { get; set; }

        private static FeishuConfig cached;
        private static readonly object cacheLock = new object();

        public static string DirPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NyarukoMFALock");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(DirPath, "feishu.json"); }
        }

        public static FeishuConfig Current
        {
            get
            {
                lock (cacheLock)
                {
                    if (cached == null)
                    {
                        cached = Load();
                        if (cached == null)
                        {
                            cached = new FeishuConfig();
                        }
                    }
                    return cached;
                }
            }
        }

        public static void Reload()
        {
            lock (cacheLock)
            {
                cached = null;
            }
        }

        public static void EnsureDir()
        {
            try
            {
                if (!Directory.Exists(DirPath))
                {
                    Directory.CreateDirectory(DirPath);
                }
            }
            catch
            {
            }
        }

        private static FeishuConfig Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                if (string.IsNullOrEmpty(json.Trim()))
                {
                    return null;
                }
                FeishuConfig c = new JavaScriptSerializer().Deserialize<FeishuConfig>(json);
                if (c == null)
                {
                    return null;
                }
                if (c.tables == null)
                {
                    c.tables = new FeishuTables();
                }
                if (c.pollSeconds <= 0) c.pollSeconds = 5;
                if (c.heartbeatMinutes <= 0) c.heartbeatMinutes = 10;
                if (c.defaultRequestMinutes <= 0) c.defaultRequestMinutes = 60;
                if (c.offlineQueueMaxDays <= 0) c.offlineQueueMaxDays = 30;
                if (c.pendingTimeoutMinutes <= 0) c.pendingTimeoutMinutes = 10;
                if (string.IsNullOrEmpty(c.awBaseUrl)) c.awBaseUrl = "http://127.0.0.1:5600";
                if (string.IsNullOrEmpty(c.awGameCategory)) c.awGameCategory = "Games";
                return c;
            }
            catch
            {
                return null;
            }
        }

        public bool IsUsable
        {
            get
            {
                return enabled
                    && !string.IsNullOrEmpty(appId)
                    && !string.IsNullOrEmpty(appSecret)
                    && !string.IsNullOrEmpty(appToken)
                    && tables != null
                    && !string.IsNullOrEmpty(tables.sessions)
                    && !string.IsNullOrEmpty(tables.accounts)
                    && !string.IsNullOrEmpty(tables.ledger)
                    && !string.IsNullOrEmpty(memberName);
            }
        }

        public string DeviceName
        {
            get { return string.IsNullOrEmpty(deviceName) ? Environment.MachineName : deviceName; }
        }

        public bool IsAwEnabled
        {
            get { return !awDisabled; }
        }

        public string AwBaseUrl
        {
            get { return string.IsNullOrEmpty(awBaseUrl) ? "http://127.0.0.1:5600" : awBaseUrl; }
        }

        public string AwGameCategory
        {
            get { return string.IsNullOrEmpty(awGameCategory) ? "Games" : awGameCategory; }
        }
    }
}
