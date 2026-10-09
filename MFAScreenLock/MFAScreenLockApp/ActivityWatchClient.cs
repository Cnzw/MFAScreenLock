using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MFAScreenLockApp
{
    public static class ActivityWatchClient
    {
        private static readonly object sync = new object();
        private static HttpClient http;

        private class AwEvent
        {
            public double ts;
            public double dur;
            public Dictionary<string, object> data;
        }

        private static HttpClient Http
        {
            get
            {
                if (http == null)
                {
                    http = new HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(2);
                }
                return http;
            }
        }

        public static bool TryGetGameMinutes(long startMs, long endMs, out long minutes)
        {
            minutes = 0;
            FeishuConfig cfg = FeishuConfig.Current;
            if (!cfg.IsAwEnabled || endMs <= startMs)
            {
                return false;
            }
            try
            {
                List<Regex> rules = LoadGameRules(cfg);
                if (rules.Count == 0)
                {
                    return false;
                }

                string winBucket = FindBucketId(cfg, "currentwindow");
                string afkBucket = FindBucketId(cfg, "afkstatus");
                if (string.IsNullOrEmpty(winBucket) || string.IsNullOrEmpty(afkBucket))
                {
                    return false;
                }

                List<AwEvent> winEvents = ParseEvents(GetEvents(cfg, winBucket, startMs, endMs));
                if (winEvents.Count == 0)
                {
                    return false;
                }
                List<AwEvent> afkEvents = ParseEvents(GetEvents(cfg, afkBucket, startMs, endMs));

                List<double[]> notAfk = BuildIntervals(afkEvents, startMs, endMs, delegate(AwEvent ev)
                {
                    return AsString(Get(ev.data, "status")) == "not-afk";
                });
                List<double[]> gameIv = BuildIntervals(winEvents, startMs, endMs, delegate(AwEvent ev)
                {
                    string app = AsString(Get(ev.data, "app"));
                    string title = AsString(Get(ev.data, "title"));
                    for (int i = 0; i < rules.Count; i++)
                    {
                        if ((!string.IsNullOrEmpty(app) && rules[i].IsMatch(app)) ||
                            (!string.IsNullOrEmpty(title) && rules[i].IsMatch(title)))
                        {
                            return true;
                        }
                    }
                    return false;
                });

                double totalMs = 0;
                for (int i = 0; i < gameIv.Count; i++)
                {
                    totalMs += OverlapMs(gameIv[i][0], gameIv[i][1], notAfk);
                }
                minutes = (long)Math.Ceiling(totalMs / 60000.0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<Regex> LoadGameRules(FeishuConfig cfg)
        {
            List<Regex> rules = new List<Regex>();
            object o = GetJson(cfg.ResolvedAwBaseUrl + "/api/0/settings/classes");
            Dictionary<string, object> wrapper = o as Dictionary<string, object>;
            object items = wrapper != null ? Get(wrapper, "value") : o;
            IEnumerable e = items as IEnumerable;
            if (e == null)
            {
                return rules;
            }
            string want = cfg.ResolvedAwGameCategory;
            foreach (object it in e)
            {
                Dictionary<string, object> cls = it as Dictionary<string, object>;
                if (cls == null)
                {
                    continue;
                }
                if (!NamePathContains(Get(cls, "name"), want))
                {
                    continue;
                }
                Dictionary<string, object> rule = Get(cls, "rule") as Dictionary<string, object>;
                if (rule == null || AsString(Get(rule, "type")) != "regex")
                {
                    continue;
                }
                string pattern = AsString(Get(rule, "regex"));
                if (string.IsNullOrEmpty(pattern))
                {
                    continue;
                }
                try
                {
                    RegexOptions opt = AsBool(Get(rule, "ignore_case")) ? RegexOptions.IgnoreCase : RegexOptions.None;
                    rules.Add(new Regex(pattern, opt));
                }
                catch
                {
                }
            }
            return rules;
        }

        private static bool NamePathContains(object nameObj, string want)
        {
            IEnumerable e = nameObj as IEnumerable;
            if (e == null)
            {
                return false;
            }
            foreach (object n in e)
            {
                string ns = n as string;
                if (ns != null && ns.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string FindBucketId(FeishuConfig cfg, string type)
        {
            Dictionary<string, object> buckets = GetJson(cfg.ResolvedAwBaseUrl + "/api/0/buckets/") as Dictionary<string, object>;
            if (buckets == null)
            {
                return null;
            }
            string hostname = Environment.MachineName;
            string fallback = null;
            foreach (KeyValuePair<string, object> kv in buckets)
            {
                Dictionary<string, object> b = kv.Value as Dictionary<string, object>;
                if (b == null || AsString(Get(b, "type")) != type)
                {
                    continue;
                }
                if (fallback == null)
                {
                    fallback = kv.Key;
                }
                string h = AsString(Get(b, "hostname"));
                if (!string.IsNullOrEmpty(hostname) && !string.IsNullOrEmpty(h) &&
                    string.Equals(h, hostname, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Key;
                }
            }
            return fallback;
        }

        private static List<Dictionary<string, object>> GetEvents(FeishuConfig cfg, string bucketId, long startMs, long endMs)
        {
            string url = cfg.ResolvedAwBaseUrl + "/api/0/buckets/" + Uri.EscapeDataString(bucketId) +
                "/events?start=" + ToIso(startMs) + "&end=" + ToIso(endMs) + "&limit=-1";
            object o = GetJson(url);
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            IEnumerable e = o as IEnumerable;
            if (e != null)
            {
                foreach (object it in e)
                {
                    Dictionary<string, object> d = it as Dictionary<string, object>;
                    if (d != null)
                    {
                        list.Add(d);
                    }
                }
            }
            return list;
        }

        private static List<AwEvent> ParseEvents(List<Dictionary<string, object>> raws)
        {
            List<AwEvent> list = new List<AwEvent>();
            foreach (Dictionary<string, object> r in raws)
            {
                AwEvent ev = new AwEvent();
                ev.ts = ParseIso(Get(r, "timestamp"));
                ev.dur = AsDouble(Get(r, "duration"));
                ev.data = Get(r, "data") as Dictionary<string, object>;
                list.Add(ev);
            }
            list.Sort(delegate(AwEvent a, AwEvent b) { return a.ts.CompareTo(b.ts); });
            return list;
        }

        private static List<double[]> BuildIntervals(List<AwEvent> evs, long startMs, long endMs, Func<AwEvent, bool> keep)
        {
            List<double[]> raw = new List<double[]>();
            for (int i = 0; i < evs.Count; i++)
            {
                if (!keep(evs[i]))
                {
                    continue;
                }
                double ts = evs[i].ts;
                double d = evs[i].dur * 1000.0;
                if (d <= 0 && i + 1 < evs.Count)
                {
                    d = evs[i + 1].ts - ts;
                }
                if (d <= 0)
                {
                    continue;
                }
                double a = Math.Max(ts, (double)startMs);
                double b = Math.Min(ts + d, (double)endMs);
                if (b > a)
                {
                    raw.Add(new double[] { a, b });
                }
            }
            return Merge(raw);
        }

        private static List<double[]> Merge(List<double[]> list)
        {
            List<double[]> merged = new List<double[]>();
            if (list.Count == 0)
            {
                return merged;
            }
            list.Sort(delegate(double[] x, double[] y) { return x[0].CompareTo(y[0]); });
            double cs = list[0][0];
            double ce = list[0][1];
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i][0] <= ce)
                {
                    if (list[i][1] > ce)
                    {
                        ce = list[i][1];
                    }
                }
                else
                {
                    merged.Add(new double[] { cs, ce });
                    cs = list[i][0];
                    ce = list[i][1];
                }
            }
            merged.Add(new double[] { cs, ce });
            return merged;
        }

        private static double OverlapMs(double aStart, double aEnd, List<double[]> intervals)
        {
            double sum = 0;
            for (int i = 0; i < intervals.Count; i++)
            {
                double lo = Math.Max(aStart, intervals[i][0]);
                double hi = Math.Min(aEnd, intervals[i][1]);
                if (hi > lo)
                {
                    sum += hi - lo;
                }
            }
            return sum;
        }

        private static object GetJson(string url)
        {
            HttpResponseMessage resp = Http.GetAsync(url).GetAwaiter().GetResult();
            using (resp)
            {
                string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }
                return new JavaScriptSerializer().DeserializeObject(text);
            }
        }

        private static string ToIso(long unixMs)
        {
            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(unixMs);
            return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private static double ParseIso(object v)
        {
            string s = v as string;
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
            {
                DateTime utc = dto.UtcDateTime;
                return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            }
            return 0;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            if (d == null)
            {
                return null;
            }
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        private static string AsString(object v)
        {
            if (v == null)
            {
                return null;
            }
            string s = v as string;
            if (s != null)
            {
                return s;
            }
            IEnumerable e = v as IEnumerable;
            if (e != null)
            {
                foreach (object item in e)
                {
                    return AsString(item);
                }
                return null;
            }
            return v.ToString();
        }

        private static double AsDouble(object v)
        {
            if (v == null)
            {
                return 0;
            }
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is decimal) return (double)(decimal)v;
            double d;
            if (double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }
            return 0;
        }

        private static bool AsBool(object v)
        {
            if (v is bool)
            {
                return (bool)v;
            }
            if (v == null)
            {
                return false;
            }
            bool b;
            return bool.TryParse(v.ToString(), out b) && b;
        }
    }
}
