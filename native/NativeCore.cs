using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace EyeCareNative
{
    public sealed class Interval
    {
        public string start { get; set; }
        public string end { get; set; }
        public Interval() { start = "12:00"; end = "13:00"; }
        public Interval(string a, string b) { start = a; end = b; }
    }

    public sealed class DayOverride
    {
        public string date { get; set; }
        public List<Interval> intervals { get; set; }
        public DayOverride() { intervals = new List<Interval>(); }
    }

    public sealed class Schedule
    {
        public List<Interval> daily { get; set; }
        public DayOverride @override { get; set; }
        public Schedule() { daily = new List<Interval>(); }
    }

    public sealed class Preferences
    {
        public int schema { get; set; }
        public Schedule schedule { get; set; }
        public double volume { get; set; }
        public string landscape { get; set; }
        public long pausedUntil { get; set; }
        public bool openAtLogin { get; set; }
        public bool silent { get; set; }
        public Preferences() { schema = 1; schedule = new Schedule(); volume = 0.7; landscape = "forest"; }
    }

    public sealed class Span
    {
        public DateTime Start;
        public DateTime End;
        public Span(DateTime start, DateTime end) { Start = start; End = end; }
    }

    public static class QuietHours
    {
        public static int Minute(string value)
        {
            DateTime parsed;
            if (value == null || !DateTime.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("请填写有效的24小时开始和结束时间");
            return parsed.Hour * 60 + parsed.Minute;
        }

        public static void ValidateIntervals(List<Interval> intervals)
        {
            if (intervals == null || intervals.Count > 32) throw new ArgumentException("最多设置32个免提醒时段");
            foreach (Interval interval in intervals)
            {
                if (interval == null || Minute(interval.start) == Minute(interval.end)) throw new ArgumentException("开始和结束时间不能相同");
            }
        }

        public static void Validate(Preferences settings)
        {
            if (settings == null || settings.schema != 1 || settings.schedule == null) throw new ArgumentException("设置格式无效");
            ValidateIntervals(settings.schedule.daily);
            if (settings.schedule.@override != null)
            {
                DateTime.ParseExact(settings.schedule.@override.date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                ValidateIntervals(settings.schedule.@override.intervals);
            }
            if (Double.IsNaN(settings.volume) || Double.IsInfinity(settings.volume) || settings.volume < 0.1 || settings.volume > 1) throw new ArgumentException("音量应在10%至100%之间");
            if (!Assets.Ids.Contains(settings.landscape)) throw new ArgumentException("休息画面无效");
            if (settings.pausedUntil < 0) throw new ArgumentException("暂停截止时间无效");
        }

        public static List<Span> Spans(Schedule schedule, DateTime now)
        {
            var result = new List<Span>();
            DateTime? todayOverride = schedule.@override == null ? (DateTime?)null : DateTime.ParseExact(schedule.@override.date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (int offset = -2; offset <= 8; offset++)
            {
                DateTime day = now.Date.AddDays(offset);
                foreach (Interval interval in schedule.daily)
                {
                    var span = MakeSpan(day, interval);
                    if (!todayOverride.HasValue || span.End <= todayOverride.Value || span.Start >= todayOverride.Value.AddDays(1)) result.Add(span);
                    else
                    {
                        if (span.Start < todayOverride.Value) result.Add(new Span(span.Start, todayOverride.Value));
                        if (span.End > todayOverride.Value.AddDays(1)) result.Add(new Span(todayOverride.Value.AddDays(1), span.End));
                    }
                }
            }
            if (todayOverride.HasValue) foreach (Interval interval in schedule.@override.intervals) result.Add(MakeSpan(todayOverride.Value, interval));
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<Span>();
            foreach (Span span in result)
            {
                if (merged.Count > 0 && span.Start <= merged[merged.Count - 1].End)
                {
                    var previous = merged[merged.Count - 1];
                    if (span.End > previous.End) previous.End = span.End;
                }
                else merged.Add(new Span(span.Start, span.End));
            }
            return merged;
        }

        static Span MakeSpan(DateTime day, Interval interval)
        {
            int start = Minute(interval.start), end = Minute(interval.end);
            return new Span(day.AddMinutes(start), day.AddMinutes(end).AddDays(end < start ? 1 : 0));
        }

        public static DateTime? Until(Schedule schedule, DateTime now)
        {
            foreach (Span span in Spans(schedule, now)) if (span.Start <= now && now < span.End) return span.End;
            return null;
        }
    }

    public static class Assets
    {
        public static readonly string[] Ids = { "forest", "sunlit-forest", "bamboo", "mountain-lake", "ferns" };
        public static readonly string[] Names = { "林间倒影", "晨光森林", "静谧竹林", "青山静湖", "青翠蕨叶" };
        public static readonly string[] Files = { "forest-hd.jpg", "sunlit-forest.jpg", "bamboo.jpg", "alpine-lake.jpg", "ferns.jpg" };
        public static int Index(string id) { int i = Array.IndexOf(Ids, id); return i < 0 ? 0 : i; }
    }

    public static class Store
    {
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long Unix(DateTime utc) { return (long)(utc.ToUniversalTime() - Epoch).TotalMilliseconds; }
        public static Preferences Clone(Preferences value) { return Json.Deserialize<Preferences>(Json.Serialize(value)); }
        public static Preferences Load(string path)
        {
            string json = File.ReadAllText(path);
            var raw = Json.DeserializeObject(json) as Dictionary<string, object>;
            var value = Json.Deserialize<Preferences>(json);
            QuietHours.Validate(value);
            return value;
        }
        public static void Save(string path, Preferences value)
        {
            QuietHours.Validate(value);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, Json.Serialize(value), new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + "." + DateTime.UtcNow.Ticks + ".bak", true);
            else File.Move(temp, path);
        }
        public static Preferences ImportElectron(string path)
        {
            var raw = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            var result = new Preferences();
            if (raw.ContainsKey("eyeCareSchedule")) result.schedule = Json.Deserialize<Schedule>(Json.Serialize(raw["eyeCareSchedule"]));
            if (raw.ContainsKey("volume")) result.volume = Convert.ToDouble(raw["volume"], CultureInfo.InvariantCulture);
            if (raw.ContainsKey("eyeCareLandscape")) result.landscape = Convert.ToString(raw["eyeCareLandscape"], CultureInfo.InvariantCulture);
            if (raw.ContainsKey("eyeCarePausedUntil")) result.pausedUntil = Convert.ToInt64(raw["eyeCarePausedUntil"], CultureInfo.InvariantCulture);
            if (raw.ContainsKey("openAtLogin")) result.openAtLogin = Convert.ToBoolean(raw["openAtLogin"], CultureInfo.InvariantCulture);
            if (raw.ContainsKey("silentNotifications")) result.silent = Convert.ToBoolean(raw["silentNotifications"], CultureInfo.InvariantCulture);
            QuietHours.Validate(result);
            return result;
        }
    }

    public sealed class Engine
    {
        public readonly TimeSpan WorkDuration;
        public readonly TimeSpan RestDuration;
        public Preferences Settings;
        public string State = "idle";
        public DateTime? Deadline;
        public bool Locked;
        public bool Suspended;
        public bool Started;
        public bool Completed;
        public bool Stopped;
        public Engine(Preferences settings, DateTime utc) : this(settings, utc, TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(20)) { }
        public Engine(Preferences settings, DateTime utc, TimeSpan work, TimeSpan rest) { Settings = settings; WorkDuration = work; RestDuration = rest; Deadline = utc.Add(work); }
        public bool IsBlocked(DateTime utc) { return Suspended || Settings.pausedUntil > Store.Unix(utc) || QuietHours.Until(Settings.schedule, utc.ToLocalTime()).HasValue; }
        public void Tick(DateTime utc)
        {
            Started = Completed = Stopped = false;
            if (IsBlocked(utc))
            {
                Stopped = State == "rest";
                State = "blocked"; Deadline = null; return;
            }
            if (State == "blocked") { State = "idle"; Deadline = utc.Add(WorkDuration); }
            if (Locked && State == "rest") { State = "waiting"; Deadline = null; Stopped = true; return; }
            if (State == "waiting") { if (!Locked) Begin(utc); return; }
            if (!Deadline.HasValue || utc < Deadline.Value) return;
            if (State == "rest") { State = "idle"; Deadline = utc.Add(WorkDuration); Completed = true; Stopped = true; }
            else if (Locked) { State = "waiting"; Deadline = null; }
            else Begin(utc);
        }
        void Begin(DateTime utc) { State = "rest"; Deadline = utc.Add(RestDuration); Started = true; }
        public void Preview(DateTime utc)
        {
            if (Locked || IsBlocked(utc) || State == "rest") throw new InvalidOperationException("当前状态不能预览");
            Started = Completed = Stopped = false; Begin(utc);
        }
        public void Reset(DateTime utc) { State = "idle"; Deadline = utc.Add(WorkDuration); Started = Completed = Stopped = false; }
        public string Status(DateTime utc)
        {
            if (State == "waiting") return "锁屏到点，解锁后完整休息20秒";
            if (Suspended) return "睡眠暂停，恢复后重新计时";
            if (Settings.pausedUntil > Store.Unix(utc)) return "临时暂停至 " + Store.Epoch.AddMilliseconds(Settings.pausedUntil).ToLocalTime().ToString("HH:mm");
            var quiet = QuietHours.Until(Settings.schedule, utc.ToLocalTime());
            if (quiet.HasValue) return "免提醒时段，至 " + quiet.Value.ToString("HH:mm");
            if (State == "rest") return "休息中 " + Math.Max(0, Math.Ceiling((Deadline.Value - utc).TotalSeconds)) + "秒";
            int seconds = Deadline.HasValue ? Math.Max(0, (int)Math.Ceiling((Deadline.Value - utc).TotalSeconds)) : 1200;
            return (Locked ? "锁屏计时 " : "下次休息 ") + (seconds / 60) + "分" + (seconds % 60) + "秒";
        }
    }
}
