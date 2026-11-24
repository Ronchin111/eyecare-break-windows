using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EyeCareNative
{
    static class NativeTests
    {
        static readonly List<object> results = new List<object>();
        static int failed;
        static readonly DateTime Start = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void Test(string name, Action action)
        {
            try { action(); results.Add(new { name = name, passed = true }); }
            catch (Exception error) { failed++; results.Add(new { name = name, passed = false, error = error.Message }); }
        }
        static void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, "Expected rejection"); }
        static Engine New() { return new Engine(new Preferences(), Start); }
        static bool Quiet(Schedule schedule, int day, int hour, int minute) { return QuietHours.Until(schedule, new DateTime(2026, 11, day, hour, minute, 0)).HasValue; }
        public static int VerifyImport(string source, string output)
        {
            byte[] before = File.ReadAllBytes(source);
            var raw = Store.Json.Deserialize<Dictionary<string, object>>(System.Text.Encoding.UTF8.GetString(before));
            var imported = Store.ImportElectron(source);
            bool schedule = !raw.ContainsKey("eyeCareSchedule") || Store.Json.Serialize(Store.Json.Deserialize<Schedule>(Store.Json.Serialize(raw["eyeCareSchedule"]))) == Store.Json.Serialize(imported.schedule);
            bool volume = !raw.ContainsKey("volume") || imported.volume == Convert.ToDouble(raw["volume"]);
            bool landscape = !raw.ContainsKey("eyeCareLandscape") || imported.landscape == Convert.ToString(raw["eyeCareLandscape"]);
            bool pause = !raw.ContainsKey("eyeCarePausedUntil") || imported.pausedUntil == Convert.ToInt64(raw["eyeCarePausedUntil"]);
            bool startup = !raw.ContainsKey("openAtLogin") || imported.openAtLogin == Convert.ToBoolean(raw["openAtLogin"]);
            bool silence = !raw.ContainsKey("silentNotifications") || imported.silent == Convert.ToBoolean(raw["silentNotifications"]);
            bool unchanged = before.SequenceEqual(File.ReadAllBytes(source));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, Store.Json.Serialize(new { passed = schedule && volume && landscape && pause && startup && silence && unchanged, schedule = schedule, volume = volume, landscape = landscape, pause = pause, startupIntent = startup, globalSilence = silence, sourceUnchanged = unchanged, privateFieldsCopied = false }));
            return schedule && volume && landscape && pause && startup && silence && unchanged ? 0 : 1;
        }
        public static int Run(string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            Test("legacy settings remain readable without an expiry dependency", () => { string path = Path.Combine(Path.GetDirectoryName(output), "legacy-settings.json"); var raw = Store.Json.Deserialize<Dictionary<string, object>>(Store.Json.Serialize(new Preferences { volume = 0.45, landscape = "bamboo" })); raw["firstRunDate"] = "2020-01-01"; File.WriteAllText(path, Store.Json.Serialize(raw)); byte[] before = File.ReadAllBytes(path); var value = Store.Load(path); Check(value.volume == 0.45 && value.landscape == "bamboo" && before.SequenceEqual(File.ReadAllBytes(path)), "legacy configuration changed during load"); });
            Test("20分钟之前不开始", () => { var e = New(); e.Tick(Start.AddMinutes(20).AddMilliseconds(-1)); Check(e.State == "idle" && !e.Started, "early rest"); });
            Test("20分钟到点开始且只发一次事件", () => { var e = New(); e.Tick(Start.AddMinutes(20)); Check(e.Started && e.State == "rest", "not started"); e.Tick(Start.AddMinutes(20).AddSeconds(1)); Check(!e.Started, "duplicate cue"); });
            Test("完整20秒后结束并重计20分钟", () => { var e = New(); e.Tick(Start.AddMinutes(20)); e.Tick(Start.AddMinutes(20).AddSeconds(19)); Check(e.State == "rest", "ended early"); e.Tick(Start.AddMinutes(20).AddSeconds(20)); Check(e.Completed && e.Stopped && e.Deadline == Start.AddMinutes(40).AddSeconds(20), "finish"); });
            Test("锁屏继续计时到点等待", () => { var e = New(); e.Locked = true; e.Tick(Start.AddMinutes(20)); Check(e.State == "waiting" && !e.Started, "lock"); });
            Test("解锁后完整休息20秒", () => { var e = New(); e.Locked = true; e.Tick(Start.AddMinutes(25)); e.Locked = false; e.Tick(Start.AddMinutes(26)); Check(e.Started && e.Deadline == Start.AddMinutes(26).AddSeconds(20), "unlock"); });
            Test("休息中锁屏重置为解锁后完整20秒", () => { var e = New(); e.Tick(Start.AddMinutes(20)); e.Locked = true; e.Tick(Start.AddMinutes(20).AddSeconds(5)); Check(e.Stopped && !e.Completed && e.State == "waiting", "interrupt"); e.Locked = false; e.Tick(Start.AddMinutes(21)); Check(e.Deadline == Start.AddMinutes(21).AddSeconds(20), "restart"); });
            Test("锁屏未到点解锁保持原截止", () => { var e = New(); e.Locked = true; e.Tick(Start.AddMinutes(5)); e.Locked = false; e.Tick(Start.AddMinutes(7)); Check(e.Deadline == Start.AddMinutes(20), "lock shifted deadline"); });
            Test("睡眠暂停且恢复后重计", () => { var e = New(); e.Suspended = true; e.Tick(Start.AddMinutes(10)); Check(e.State == "blocked", "suspend"); e.Suspended = false; e.Tick(Start.AddMinutes(30)); Check(e.Deadline == Start.AddMinutes(50), "resume"); });
            Test("休息中睡眠不发完成音", () => { var e = New(); e.Tick(Start.AddMinutes(20)); e.Suspended = true; e.Tick(Start.AddMinutes(20).AddSeconds(5)); Check(e.Stopped && !e.Completed, "completion cue on suspend"); });
            foreach (int duration in new[] { 30, 60, 120, 180 })
            {
                int minutes = duration;
                Test("有限暂停" + minutes + "分钟", () => { var e = New(); e.Settings.pausedUntil = Store.Unix(Start.AddMinutes(minutes)); e.Tick(Start); Check(e.State == "blocked", "not paused"); e.Tick(Start.AddMinutes(minutes)); Check(e.Deadline == Start.AddMinutes(minutes + 20), "pause resume"); });
            }
            Test("手动恢复不越过免提醒时段", () => { var e = New(); e.Settings.schedule.daily.Add(new Interval("09:00", "10:00")); e.Settings.pausedUntil = 0; e.Tick(Start); Check(e.State == "blocked", "quiet bypassed"); });
            Test("安静时段结束后重计20分钟", () => { var e = New(); e.Settings.schedule.daily.Add(new Interval("09:00", "10:00")); e.Tick(Start); e.Tick(Start.AddHours(1)); Check(e.Deadline == Start.AddHours(1).AddMinutes(20), "quiet reset"); });
            Test("暂停和安静时段重叠取较晚", () => { var e = New(); e.Settings.schedule.daily.Add(new Interval("09:00", "10:00")); e.Settings.pausedUntil = Store.Unix(Start.AddMinutes(90)); e.Tick(Start); e.Tick(Start.AddMinutes(60)); Check(e.State == "blocked", "overlap"); e.Tick(Start.AddMinutes(90)); Check(e.Deadline == Start.AddMinutes(110), "overlap reset"); });
            Test("跨午夜每日时段", () => { var s = new Schedule(); s.daily.Add(new Interval("23:00", "01:00")); Check(Quiet(s, 18, 23, 30) && Quiet(s, 19, 0, 30) && !Quiet(s, 19, 1, 0), "overnight"); });
            Test("仅今天为空覆盖可取消每日及昨夜尾段", () => { var s = new Schedule(); s.daily.Add(new Interval("23:00", "01:00")); s.@override = new DayOverride { date = "2026-11-18" }; Check(!Quiet(s, 18, 0, 30) && !Quiet(s, 18, 23, 30) && Quiet(s, 19, 0, 30), "override subtraction"); });
            Test("仅今天跨夜持续到次日结束", () => { var s = new Schedule { @override = new DayOverride { date = "2026-11-18", intervals = new List<Interval> { new Interval("23:30", "02:00") } } }; Check(Quiet(s, 19, 1, 30) && !Quiet(s, 19, 2, 0), "override overnight"); });
            Test("重叠时段合并且边界包含开始不含结束", () => { var s = new Schedule(); s.daily.Add(new Interval("12:00", "13:00")); s.daily.Add(new Interval("12:30", "14:00")); Check(Quiet(s, 18, 12, 0) && QuietHours.Until(s, new DateTime(2026, 11, 18, 13, 30, 0)) == new DateTime(2026, 11, 18, 14, 0, 0) && !Quiet(s, 18, 14, 0), "merge"); });
            Test("相同开始结束拒绝", () => { var p = new Preferences(); p.schedule.daily.Add(new Interval("12:00", "12:00")); Reject(() => QuietHours.Validate(p)); });
            Test("错误时间拒绝", () => { var p = new Preferences(); p.schedule.daily.Add(new Interval("25:00", "12:00")); Reject(() => QuietHours.Validate(p)); });
            Test("超过32时段拒绝", () => { var p = new Preferences(); for (int i = 0; i < 33; i++) p.schedule.daily.Add(new Interval("12:00", "13:00")); Reject(() => QuietHours.Validate(p)); });
            Test("非法音量拒绝", () => { var p = new Preferences { volume = Double.NaN }; Reject(() => QuietHours.Validate(p)); });
            Test("非法画面拒绝", () => { var p = new Preferences { landscape = "not-found" }; Reject(() => QuietHours.Validate(p)); });
            Test("预览是完整20秒并重计", () => { var e = New(); e.Preview(Start.AddSeconds(10)); Check(e.Started && e.Deadline == Start.AddSeconds(30), "preview deadline"); e.Tick(Start.AddSeconds(30)); Check(e.Completed && e.Deadline == Start.AddSeconds(30).AddMinutes(20), "preview next"); });
            Test("暂停中不能预览", () => { var e = New(); e.Settings.pausedUntil = Store.Unix(Start.AddMinutes(30)); Reject(() => e.Preview(Start)); });
            Test("锁屏中不能预览", () => { var e = New(); e.Locked = true; Reject(() => e.Preview(Start)); });
            Test("重复预览拒绝避免重复开始音", () => { var e = New(); e.Preview(Start); Reject(() => e.Preview(Start.AddSeconds(1))); });
            Test("JSON往返保留时段和画面", () => { var p = new Preferences { landscape = "bamboo", volume = 0.45, openAtLogin = true }; p.schedule.daily.Add(new Interval("12:00", "13:00")); var copy = Store.Clone(p); QuietHours.Validate(copy); Check(copy.landscape == "bamboo" && copy.volume == 0.45 && copy.openAtLogin && copy.schedule.daily[0].start == "12:00", "roundtrip"); });
            Test("设置保存备份和重新读取", () => { string p = Path.Combine(Path.GetDirectoryName(output), "save-test-" + Guid.NewGuid().ToString("N") + ".json"); var value = new Preferences(); Store.Save(p, value); value.landscape = "ferns"; Store.Save(p, value); Check(Store.Load(p).landscape == "ferns" && Directory.GetFiles(Path.GetDirectoryName(p), Path.GetFileName(p) + ".*.bak").Length == 1, "atomic save"); });
            Test("损坏设置不可静默重置", () => { string p = Path.Combine(Path.GetDirectoryName(output), "corrupt-test-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(p, "{"); Reject(() => Store.Load(p)); Check(File.ReadAllText(p) == "{", "overwrote corrupt data"); });
            Test("导入Electron仅取指定字段且不改原件", () => { string p = Path.Combine(Path.GetDirectoryName(output), "import-test-" + Guid.NewGuid().ToString("N") + ".json"); string json = "{\"volume\":0.6,\"eyeCareLandscape\":\"bamboo\",\"openAtLogin\":true,\"eyeCarePausedUntil\":0,\"eyeCareSchedule\":{\"daily\":[{\"start\":\"12:00\",\"end\":\"13:00\"}],\"override\":null},\"unrelated\":\"do-not-copy\"}"; File.WriteAllText(p, json); var value = Store.ImportElectron(p); Check(value.landscape == "bamboo" && value.volume == 0.6 && value.openAtLogin && !Store.Json.Serialize(value).Contains("do-not-copy") && File.ReadAllText(p) == json, "import"); });
            Test("等待解锁被暂停取消", () => { var e = New(); e.Locked = true; e.Tick(Start.AddMinutes(20)); e.Settings.pausedUntil = Store.Unix(Start.AddMinutes(90)); e.Tick(Start.AddMinutes(21)); e.Locked = false; e.Tick(Start.AddMinutes(22)); Check(e.State == "blocked" && !e.Started, "pending rest survived pause"); });
            Test("导入全局静音并保持原件", () => { string p = Path.Combine(Path.GetDirectoryName(output), "silence-test-" + Guid.NewGuid().ToString("N") + ".json"); string json = "{\"silentNotifications\":true}"; File.WriteAllText(p, json); Check(Store.ImportElectron(p).silent && File.ReadAllText(p) == json, "silence lost"); });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, Store.Json.Serialize(new { version = Program.Version, total = results.Count, passed = results.Count - failed, failed = failed, tests = results }));
            return failed == 0 ? 0 : 1;
        }
    }
}
