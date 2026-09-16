using MFAScreenLockApp.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MFAScreenLockApp
{
    class ShareClass
    {
        public static Bitmap gWallPaperBmp()
        {
            //string path = AppDomain.CurrentDomain.BaseDirectory + Settings.Default.Background;
            if (Settings.Default.Background.Length > 0)
            {
                if (File.Exists(Settings.Default.Background))
                {
                    FileStream fileStream = File.OpenRead(Settings.Default.Background);
                    Int32 filelength = 0;
                    filelength = (int)fileStream.Length;
                    Byte[] image = new Byte[filelength];
                    fileStream.Read(image, 0, filelength);
                    Image result = Image.FromStream(fileStream);
                    fileStream.Close();
                    return new Bitmap(result);
                }
            }
            return SysLink.GetwallPaper();
        }

        public static Image autoScaleBitmap(Bitmap wallPaperBmp, Size size)
        {
            if (Settings.Default.Scale == 0)
            {
                return null;
            }
            else if (Settings.Default.Scale == 5)
            {
                return ImageControl.scaleBitmap(wallPaperBmp, size.Width, size.Height);
            }
            return wallPaperBmp;
        }

        public static ImageLayout imageLayout()
        {
            switch (Settings.Default.Scale)
            {
                case 0:
                    return ImageLayout.None;
                case 1:
                    return ImageLayout.None;
                case 2:
                    return ImageLayout.Center;
                case 3:
                    return ImageLayout.Stretch;
                case 4:
                    return ImageLayout.Zoom;
                default:
                    return ImageLayout.Stretch;
            }
        }

        private static string cronExprCache = null;
        private static List<int>[] cronSetsCache = null;
        private static bool[] cronStarCache = null;

        public static bool inBypassWindow()
        {
            try
            {
                if (!Settings.Default.BypassEnable) return false;
                return cronMatch(Settings.Default.BypassCron, DateTime.Now);
            }
            catch
            {
                return false;
            }
        }

        private static bool cronMatch(string expr, DateTime now)
        {
            if (expr != cronExprCache)
            {
                cronExprCache = expr;
                cronSetsCache = null;
                cronStarCache = null;
                string[] fields = (expr ?? "").Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 5)
                {
                    int[] lo = new int[] { 0, 0, 1, 1, 0 };
                    int[] hi = new int[] { 59, 23, 31, 12, 7 };
                    List<int>[] sets = new List<int>[5];
                    bool[] star = new bool[5];
                    bool ok = true;
                    for (int i = 0; i < 5 && ok; i++)
                    {
                        ok = parseCronField(fields[i], lo[i], hi[i], out sets[i], out star[i]);
                    }
                    if (ok)
                    {
                        cronSetsCache = sets;
                        cronStarCache = star;
                    }
                }
            }
            if (cronSetsCache == null) return false;
            if (!cronSetsCache[0].Contains(now.Minute)) return false;
            if (!cronSetsCache[1].Contains(now.Hour)) return false;
            if (!cronSetsCache[3].Contains(now.Month)) return false;
            int dow = (int)now.DayOfWeek;
            bool dowHit = cronSetsCache[4].Contains(dow) || cronSetsCache[4].Contains(dow == 0 ? 7 : dow);
            bool domHit = cronSetsCache[2].Contains(now.Day);
            if (cronStarCache[2] && cronStarCache[4]) return true;
            if (cronStarCache[2]) return dowHit;
            if (cronStarCache[4]) return domHit;
            return domHit || dowHit;
        }

        private static bool parseCronField(string field, int lo, int hi, out List<int> values, out bool isStar)
        {
            values = new List<int>();
            isStar = (field == "*" || field.StartsWith("*/"));
            foreach (string part in field.Split(','))
            {
                if (part.Length == 0) return false;
                int step = 1;
                string body = part;
                int slash = part.IndexOf('/');
                if (slash >= 0)
                {
                    body = part.Substring(0, slash);
                    if (!int.TryParse(part.Substring(slash + 1), out step) || step <= 0) return false;
                }
                int from;
                int to;
                if (body == "*")
                {
                    from = lo;
                    to = hi;
                }
                else
                {
                    int dash = body.IndexOf('-');
                    if (dash > 0)
                    {
                        if (!int.TryParse(body.Substring(0, dash), out from)) return false;
                        if (!int.TryParse(body.Substring(dash + 1), out to)) return false;
                    }
                    else
                    {
                        if (!int.TryParse(body, out from)) return false;
                        to = from;
                    }
                }
                if (from < lo || to > hi || from > to) return false;
                for (int v = from; v <= to; v += step)
                {
                    if (!values.Contains(v)) values.Add(v);
                }
            }
            return values.Count > 0;
        }
    }
}
