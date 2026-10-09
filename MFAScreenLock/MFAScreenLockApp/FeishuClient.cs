using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace MFAScreenLockApp
{
    public class FeishuNetworkException : Exception
    {
        public FeishuNetworkException(string message, Exception inner) : base(message, inner) { }
    }

    public class FeishuApiException : Exception
    {
        public FeishuApiException(string message) : base(message) { }
    }

    public class FeishuAccountInfo
    {
        public string RecordId;
        public string Name;
        public double Balance;
    }

    public static class FeishuClient
    {
        public enum NetState { Offline, Online, BadData }

        private const string BaseUrl = "https://open.feishu.cn/open-apis";

        private static readonly object sync = new object();
        private static HttpClient http;
        private static string token;
        private static DateTime tokenExpireUtc = DateTime.MinValue;
        private static double clockOffsetSeconds;
        private static bool clockCalibrated;

        private static HttpClient Http
        {
            get
            {
                if (http == null)
                {
                    try
                    {
                        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    }
                    catch
                    {
                    }
                    http = new HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(6);
                }
                return http;
            }
        }

        public static bool HasServerClock
        {
            get { lock (sync) { return clockCalibrated; } }
        }

        public static double ClockOffsetSeconds
        {
            get { lock (sync) { return clockOffsetSeconds; } }
        }

        public static DateTime ServerNow
        {
            get { lock (sync) { return DateTime.Now.AddSeconds(clockOffsetSeconds); } }
        }

        public static long ToUnixMs(DateTime dt)
        {
            return (long)(dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        public static NetState CheckFeishu()
        {
            try
            {
                GetToken();
                return NetState.Online;
            }
            catch (FeishuNetworkException)
            {
                return NetState.Offline;
            }
            catch
            {
                return NetState.BadData;
            }
        }

        private static string GetToken()
        {
            lock (sync)
            {
                if (token != null && DateTime.UtcNow < tokenExpireUtc)
                {
                    return token;
                }
            }
            FeishuConfig cfg = FeishuConfig.Current;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["app_id"] = cfg.appId;
            body["app_secret"] = cfg.appSecret;
            string resp = SendRaw("POST", "/auth/v3/tenant_access_token/internal", Serialize(body), false);
            Dictionary<string, object> d = ParseObject(resp);
            if (d == null)
            {
                throw new FeishuApiException("token 响应无法解析");
            }
            object code;
            if (d.TryGetValue("code", out code) && AsDouble(code) != 0)
            {
                throw new FeishuApiException("token 获取失败: " + resp);
            }
            string t = AsString(Get(d, "tenant_access_token"));
            double expire = AsDouble(Get(d, "expire"));
            if (string.IsNullOrEmpty(t))
            {
                throw new FeishuApiException("token 为空: " + resp);
            }
            lock (sync)
            {
                token = t;
                tokenExpireUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expire - 300));
            }
            return t;
        }

        private static string SendRaw(string method, string path, string jsonBody, bool withAuth)
        {
            HttpRequestMessage req = new HttpRequestMessage(new HttpMethod(method), BaseUrl + path);
            if (jsonBody != null)
            {
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }
            if (withAuth)
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GetToken());
            }
            try
            {
                using (HttpResponseMessage resp = Http.SendAsync(req).GetAwaiter().GetResult())
                {
                    Calibrate(resp);
                    string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return text;
                }
            }
            catch (FeishuNetworkException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FeishuNetworkException("飞书网络不可达: " + ex.Message, ex);
            }
        }

        private static void Calibrate(HttpResponseMessage resp)
        {
            if (resp.Headers.Date.HasValue)
            {
                DateTime serverLocal = resp.Headers.Date.Value.LocalDateTime;
                double off = (serverLocal - DateTime.Now).TotalSeconds;
                lock (sync)
                {
                    clockOffsetSeconds = off;
                    clockCalibrated = true;
                }
            }
        }

        private static string Api(string method, string path, string jsonBody)
        {
            string resp = SendRaw(method, path, jsonBody, true);
            CheckApi(resp);
            return resp;
        }

        private static void CheckApi(string resp)
        {
            Dictionary<string, object> d = ParseObject(resp);
            if (d == null)
            {
                throw new FeishuApiException("空响应");
            }
            object code;
            if (!d.TryGetValue("code", out code) || AsDouble(code) != 0)
            {
                throw new FeishuApiException("API 错误: " + resp);
            }
        }

        public static FeishuAccountInfo GetAccount(string name)
        {
            FeishuConfig cfg = FeishuConfig.Current;
            Dictionary<string, object> cond = new Dictionary<string, object>();
            cond["field_name"] = "电脑名";
            cond["operator"] = "is";
            cond["value"] = new object[] { name };
            Dictionary<string, object> filter = new Dictionary<string, object>();
            filter["conjunction"] = "and";
            filter["conditions"] = new object[] { cond };
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["filter"] = filter;
            string path = "/bitable/v1/apps/" + cfg.appToken + "/tables/" + cfg.tables.accounts + "/records/search";
            Dictionary<string, object> d = ParseObject(Api("POST", path, Serialize(body)));
            List<Dictionary<string, object>> items = Items(d);
            if (items.Count == 0)
            {
                return null;
            }
            Dictionary<string, object> rec = items[0];
            FeishuAccountInfo info = new FeishuAccountInfo();
            info.RecordId = AsString(Get(rec, "record_id"));
            Dictionary<string, object> f = AsDict(Get(rec, "fields"));
            if (f != null)
            {
                info.Name = AsString(Get(f, "电脑名"));
                info.Balance = AsDouble(Get(f, "余额(分钟)"));
            }
            return info;
        }

        public static string CreateSession(Dictionary<string, object> fields)
        {
            FeishuConfig cfg = FeishuConfig.Current;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["fields"] = fields;
            string path = "/bitable/v1/apps/" + cfg.appToken + "/tables/" + cfg.tables.sessions + "/records";
            Dictionary<string, object> d = ParseObject(Api("POST", path, Serialize(body)));
            Dictionary<string, object> rec = AsDict(Get(AsDict(Get(d, "data")), "record"));
            return AsString(Get(rec, "record_id"));
        }

        public static void UpdateSession(string recordId, Dictionary<string, object> fields)
        {
            FeishuConfig cfg = FeishuConfig.Current;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["fields"] = fields;
            string path = "/bitable/v1/apps/" + cfg.appToken + "/tables/" + cfg.tables.sessions + "/records/" + recordId;
            ApiPut(path, Serialize(body));
        }

        public static string GetSessionStatus(string recordId)
        {
            FeishuConfig cfg = FeishuConfig.Current;
            string path = "/bitable/v1/apps/" + cfg.appToken + "/tables/" + cfg.tables.sessions + "/records/" + recordId;
            Dictionary<string, object> d = ParseObject(Api("GET", path, null));
            Dictionary<string, object> data = AsDict(Get(d, "data"));
            Dictionary<string, object> rec = AsDict(Get(data, "record"));
            Dictionary<string, object> f = AsDict(Get(rec, "fields"));
            return AsString(Get(f, "状态"));
        }

        public static string CreateLedger(Dictionary<string, object> fields)
        {
            FeishuConfig cfg = FeishuConfig.Current;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["fields"] = fields;
            string path = "/bitable/v1/apps/" + cfg.appToken + "/tables/" + cfg.tables.ledger + "/records";
            Dictionary<string, object> d = ParseObject(Api("POST", path, Serialize(body)));
            Dictionary<string, object> rec = AsDict(Get(AsDict(Get(d, "data")), "record"));
            return AsString(Get(rec, "record_id"));
        }

        private static string ApiPut(string path, string jsonBody)
        {
            string resp = SendRaw("PUT", path, jsonBody, true);
            CheckApi(resp);
            return resp;
        }

        private static string Serialize(object o)
        {
            return new JavaScriptSerializer().Serialize(o);
        }

        private static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            object o;
            try
            {
                o = new JavaScriptSerializer().DeserializeObject(text);
            }
            catch
            {
                throw new FeishuApiException("JSON 解析失败");
            }
            return o as Dictionary<string, object>;
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

        private static Dictionary<string, object> AsDict(object o)
        {
            return o as Dictionary<string, object>;
        }

        private static List<Dictionary<string, object>> Items(Dictionary<string, object> d)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            object items = Get(AsDict(Get(d, "data")), "items");
            IEnumerable e = items as IEnumerable;
            if (e != null)
            {
                foreach (object it in e)
                {
                    Dictionary<string, object> one = it as Dictionary<string, object>;
                    if (one != null)
                    {
                        list.Add(one);
                    }
                }
            }
            return list;
        }

        private static object Unwrap(object v)
        {
            if (v == null)
            {
                return null;
            }
            if (v is string)
            {
                return v;
            }
            Dictionary<string, object> d = v as Dictionary<string, object>;
            if (d != null)
            {
                object t;
                if (d.TryGetValue("text", out t)) return Unwrap(t);
                if (d.TryGetValue("name", out t)) return Unwrap(t);
                if (d.TryGetValue("value", out t)) return Unwrap(t);
                return d;
            }
            IEnumerable e = v as IEnumerable;
            if (e != null)
            {
                foreach (object item in e)
                {
                    return Unwrap(item);
                }
                return null;
            }
            return v;
        }

        private static string AsString(object v)
        {
            object u = Unwrap(v);
            if (u == null)
            {
                return null;
            }
            string s = u as string;
            return s != null ? s : u.ToString();
        }

        private static double AsDouble(object v)
        {
            object u = Unwrap(v);
            if (u == null)
            {
                return 0;
            }
            if (u is double) return (double)u;
            if (u is int) return (int)u;
            if (u is long) return (long)u;
            if (u is decimal) return (double)(decimal)u;
            double d;
            if (double.TryParse(u.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }
            return 0;
        }
    }
}
