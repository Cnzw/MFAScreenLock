using System;

namespace MFAScreenLockApp
{
    public enum NetState { Unknown, Online, Offline, BadData }

    public static class FeishuGate
    {
        private static readonly object sync = new object();
        private static NetState state = NetState.Unknown;

        public static NetState State
        {
            get { lock (sync) { return state; } }
        }

        public static void Reset()
        {
            lock (sync) { state = NetState.Unknown; }
        }

        public static void PrefetchAsync(Action onDone)
        {
            System.Threading.Tasks.Task.Run(new Action(delegate
            {
                NetState ns;
                try
                {
                    FeishuClient.NetState r = FeishuClient.CheckFeishu();
                    if (r == FeishuClient.NetState.Online)
                    {
                        ns = NetState.Online;
                    }
                    else if (r == FeishuClient.NetState.Offline)
                    {
                        ns = NetState.Offline;
                    }
                    else
                    {
                        ns = NetState.BadData;
                    }
                }
                catch
                {
                    ns = NetState.BadData;
                }
                lock (sync) { state = ns; }
                if (onDone != null)
                {
                    try { onDone(); }
                    catch { }
                }
            }));
        }
    }
}
