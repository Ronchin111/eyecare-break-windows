using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("护眼休息")]
[assembly: System.Reflection.AssemblyVersion(EyeCareNative.Program.Version + ".0")]
[assembly: System.Reflection.AssemblyFileVersion(EyeCareNative.Program.Version + ".0")]

namespace EyeCareNative
{
    static class Program
    {
        public const string Version = "0.5.0";
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("wtsapi32.dll", SetLastError = true)] static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr buffer);
        public static bool SessionLocked()
        {
            IntPtr buffer = IntPtr.Zero; int bytes;
            try
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 25, out buffer, out bytes) || bytes < 20) return false;
                return Marshal.ReadInt32(buffer, 16) == 0;
            }
            finally { if (buffer != IntPtr.Zero) WTSFreeMemory(buffer); }
        }
        public static string Argument(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            if (i < 0) return null;
            if (i == args.Length - 1 || args[i + 1].StartsWith("--")) throw new ArgumentException("缺少参数：" + name);
            return args[i + 1];
        }
        [STAThread]
        static int Main(string[] args)
        {
            string data = null;
            try
            {
                string tests = Argument(args, "--self-test");
                if (tests != null) return NativeTests.Run(tests);
                string importCheck = Argument(args, "--verify-import");
                if (importCheck != null) return NativeTests.VerifyImport(importCheck, Argument(args, "--report"));
                bool candidate = args.Contains("--candidate");
                data = Path.GetFullPath(Argument(args, "--data-dir") ?? (candidate ? throwArgument() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EyeCareBreak")));
                string formal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StretchlyEyeCare");
                if (data.TrimEnd('\\').Equals(formal.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || data.StartsWith(formal.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("候选不能使用正式配置目录");
                string nativeFormal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EyeCareBreak");
                if (candidate && (data.TrimEnd('\\').Equals(nativeFormal.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || data.StartsWith(nativeFormal.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("隔离预览不能使用正式原生配置目录");
                string identity = BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(data.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
                bool acquired;
                using (var mutex = new Mutex(true, "Local\\EyeCareNativeCandidate_" + identity, out acquired))
                {
                    if (!acquired) { try { using (var request = EventWaitHandle.OpenExisting("Local\\EyeCareNativeOpen_" + identity)) request.Set(); } catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("程序已在运行，请双击托盘图标打开控制面板。", "护眼休息"); } return 0; }
                    Directory.CreateDirectory(data);
                    string config = Path.Combine(data, "native-settings.json");
                    Preferences settings;
                    if (File.Exists(config)) settings = Store.Load(config);
                    else { string import = Argument(args, "--import-settings"); settings = import != null ? Store.ImportElectron(import) : new Preferences(); Store.Save(config, settings); }
                    SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    using (var request = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\EyeCareNativeOpen_" + identity))
                    using (var context = new EyeContext(settings, config, args))
                    {
                        var waiter = ThreadPool.RegisterWaitForSingleObject(request, (state, timeout) => context.RequestSettingsWindow(), null, Timeout.Infinite, false);
                        try { Application.Run(context); } finally { waiter.Unregister(null); }
                    }
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception error)
            {
                if (data != null) try { File.AppendAllText(Path.Combine(data, "errors.log"), DateTime.Now.ToString("o") + " " + error + Environment.NewLine); } catch { }
                if (!args.Contains("--benchmark") && !args.Contains("--qa-dir") && !args.Contains("--self-test") && !args.Contains("--verify-import") && !args.Contains("--timed-smoke")) MessageBox.Show("未能完成操作，已有设置保留。\n" + error.Message, "护眼休息", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        static string throwArgument() { throw new ArgumentException("必须指定隔离数据目录"); }
    }

    sealed class CuePlayer : IDisposable
    {
        SoundPlayer player;
        MemoryStream stream;
        readonly string assets;
        public CuePlayer(string root) { assets = root; }
        public void Play(string name, double volume)
        {
            Dispose();
            byte[] bytes = File.ReadAllBytes(Path.Combine(assets, name + ".wav"));
            int index = 12;
            while (index + 8 <= bytes.Length)
            {
                int size = BitConverter.ToInt32(bytes, index + 4);
                if (System.Text.Encoding.ASCII.GetString(bytes, index, 4) == "data")
                {
                    for (int n = index + 8; n + 1 < Math.Min(bytes.Length, index + 8 + size); n += 2)
                    {
                        short sample = (short)(BitConverter.ToInt16(bytes, n) * Math.Max(0, Math.Min(1, volume)));
                        bytes[n] = (byte)(sample & 255); bytes[n + 1] = (byte)((sample >> 8) & 255);
                    }
                    break;
                }
                if (size < 0 || size > bytes.Length - index - 8) throw new InvalidDataException("声音文件无效");
                index += 8 + size + (size % 2);
            }
            stream = new MemoryStream(bytes, false); player = new SoundPlayer(stream); player.Load(); player.Play();
        }
        public void Dispose() { if (player != null) { player.Stop(); player.Dispose(); player = null; } if (stream != null) { stream.Dispose(); stream = null; } }
    }

    sealed class EyeContext : ApplicationContext
    {
        readonly string config;
        readonly string assets;
        readonly Engine engine;
        readonly Control dispatcher;
        readonly System.Windows.Forms.Timer timer;
        readonly CuePlayer sounds;
        readonly List<RestForm> restForms = new List<RestForm>();
        readonly NotifyIcon tray;
        readonly Icon appIcon;
        readonly bool benchmark;
        readonly string benchmarkMode;
        readonly DateTime benchmarkEnd;
        readonly string qaDirectory;
        readonly string timedSmoke;
        readonly DateTime smokeBegan;
        readonly List<object> smokeTransitions = new List<object>();
        bool smokeStarted;
        bool smokeCompleted;
        bool smokeReleased;
        DateTime smokeRestBegan;
        DateTime smokeRestEnded;
        SettingsForm settingsForm;
        Image restImage;
        string previewLandscape;
        bool quitting;
        bool restartImages;

        public EyeContext(Preferences settings, string path, string[] args)
        {
            candidateMode = args.Contains("--candidate");
            config = path; assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            timedSmoke = Program.Argument(args, "--timed-smoke"); smokeBegan = DateTime.UtcNow;
            sounds = new CuePlayer(assets); engine = timedSmoke == null ? new Engine(settings, DateTime.UtcNow) : new Engine(settings, DateTime.UtcNow, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20)); engine.Locked = Program.SessionLocked();
            dispatcher = new Control(); var handle = dispatcher.Handle;
            benchmarkMode = Program.Argument(args, "--benchmark"); benchmark = benchmarkMode != null;
            qaDirectory = Program.Argument(args, "--qa-dir");
            benchmarkEnd = DateTime.UtcNow.AddSeconds(20);
            if (benchmark && !new[] { "idle", "panel-hidden", "rest-hidden" }.Contains(benchmarkMode)) throw new ArgumentException("基准场景无效");
            appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray = new NotifyIcon { Icon = appIcon, Text = IsCandidate ? "护眼休息 · 隔离预览" : "护眼休息", Visible = !benchmark && qaDirectory == null && timedSmoke == null };
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开控制面板", null, (s, e) => OpenSettings());
            foreach (int minutes in new[] { 30, 60, 120, 180 })
            {
                int duration = minutes;
                menu.Items.Add("暂停" + minutes + "分钟", null, (s, e) => Pause(duration));
            }
            menu.Items.Add("恢复提醒", null, (s, e) => Resume()); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出（应急）", null, (s, e) => ExitThread()); tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => OpenSettings();
            SystemEvents.SessionSwitch += OnSession; SystemEvents.PowerModeChanged += OnPower; SystemEvents.DisplaySettingsChanged += OnDisplay;
            timer = new System.Windows.Forms.Timer { Interval = 1000 }; timer.Tick += (s, e) => Tick(); timer.Start();
            UpdateStatus();
            if (benchmarkMode == "panel-hidden") OpenSettings(false);
            if (benchmarkMode == "rest-hidden") ShowRest(true);
            if (!benchmark && args.Contains("--settings")) OpenSettings();
            if (qaDirectory != null) dispatcher.BeginInvoke(new Action(RunHiddenQA));
        }
        public Preferences Settings { get { return engine.Settings; } }
        public bool IsCandidate { get { return candidateMode; } }
        readonly bool candidateMode;
        public string Status { get { return engine.Status(DateTime.UtcNow); } }
        public string AssetsRoot { get { return assets; } }
        public Icon AppIcon { get { return appIcon; } }
        public bool CanPreview { get { return !engine.Locked && !engine.IsBlocked(DateTime.UtcNow) && engine.State != "rest"; } }
        public void Save(Preferences value)
        {
            StartupState before = null;
            if (!IsCandidate && (value.openAtLogin != engine.Settings.openAtLogin || StartupRegistration.IsEnabled(Application.ExecutablePath) != value.openAtLogin)) { before = StartupRegistration.Capture(); StartupRegistration.Set(value.openAtLogin, Application.ExecutablePath); }
            try { Store.Save(config, value); } catch { if (before != null) StartupRegistration.Restore(before); throw; }
            engine.Settings = value; Tick();
        }
        public void RequestSettingsWindow() { Dispatch(() => OpenSettings()); }
        void PersistPause(long until)
        {
            var value = Store.Clone(engine.Settings); value.pausedUntil = until; Save(value);
        }
        public void Pause(int minutes) { Safe(() => { if (!new[] { 30, 60, 120, 180 }.Contains(minutes)) throw new ArgumentException("暂停时长无效"); PersistPause(Store.Unix(DateTime.UtcNow.AddMinutes(minutes))); }); }
        public void Resume() { Safe(() => PersistPause(0)); }
        public void Preview(string id) { Safe(() => { engine.Preview(DateTime.UtcNow); previewLandscape = id; try { ShowRest(false); } catch { previewLandscape = null; engine.Reset(DateTime.UtcNow); throw; } PlayCue("start"); UpdateStatus(); }); }
        public void TestSound(double volume) { Safe(() => sounds.Play("end", volume)); }
        void PlayCue(string name) { if (!benchmark && timedSmoke == null && !engine.Settings.silent) try { sounds.Play(name, engine.Settings.volume); } catch (Exception error) { Log(error); } }
        public void OpenSettings(bool visible = true)
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(this); settingsForm.FormClosed += (s, e) => settingsForm = null;
            }
            if (visible) { settingsForm.OffscreenCapture = false; settingsForm.Opacity = 1; settingsForm.ShowInTaskbar = true; settingsForm.Show(); settingsForm.WindowState = FormWindowState.Normal; settingsForm.Activate(); }
            else { settingsForm.OffscreenCapture = true; settingsForm.Opacity = 0; settingsForm.ShowInTaskbar = false; settingsForm.StartPosition = FormStartPosition.Manual; settingsForm.Location = new Point(SystemInformation.VirtualScreen.Right + 100, 0); settingsForm.Show(); settingsForm.PerformLayout(); }
        }
        void Tick()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                if (timedSmoke != null && (now - smokeBegan).TotalSeconds >= 24) { FinishTimedSmoke(); ExitThread(); return; }
                if (timedSmoke != null && smokeCompleted) { UpdateStatus(); return; }
                if (benchmark && now >= benchmarkEnd) { ExitThread(); return; }
                engine.Tick(now);
                if (engine.Stopped) { CloseRest(); if (timedSmoke != null) { smokeReleased = restForms.Count == 0 && restImage == null; smokeTransitions.Add(new { state = "resources released", time = now }); } }
                if (engine.Completed) PlayCue("end");
                if (engine.Completed && timedSmoke != null) { smokeCompleted = true; smokeRestEnded = now; smokeTransitions.Add(new { state = "completed", time = now }); }
                if (engine.Started) { ShowRest(timedSmoke != null); PlayCue("start"); if (timedSmoke != null && !smokeStarted) { smokeStarted = true; smokeRestBegan = now; smokeTransitions.Add(new { state = "started", time = now }); } }
                if (restartImages && engine.State == "rest") { restartImages = false; CloseRest(); ShowRest(false); }
                foreach (RestForm form in restForms) form.UpdateCountdown(engine.Deadline);
                UpdateStatus();
            }
            catch (Exception error) { Log(error); CloseRest(); engine.Reset(DateTime.UtcNow); if (timedSmoke != null) { smokeTransitions.Add(new { state = "error", error = error.Message }); FinishTimedSmoke(); ExitThread(); } }
        }
        void UpdateStatus()
        {
            string text = "护眼休息 · " + Status;
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshStatus();
        }
        void ShowRest(bool hidden)
        {
            if (restForms.Count > 0) return;
            try
            {
                int index = Assets.Index(previewLandscape ?? engine.Settings.landscape); previewLandscape = null;
                restImage = Image.FromFile(Path.Combine(assets, Assets.Files[index]));
                foreach (Screen screen in Screen.AllScreens)
                {
                    var form = new RestForm(restImage, screen.Bounds, hidden); restForms.Add(form);
                    if (!hidden) form.Show(); else { var handle = form.Handle; form.CreateControl(); using (var pixels = new Bitmap(form.Width, form.Height)) form.DrawToBitmap(pixels, form.ClientRectangle); }
                }
            }
            catch { CloseRest(); throw; }
        }
        void CloseRest()
        {
            foreach (RestForm form in restForms.ToArray()) { form.AllowClose = true; form.Close(); form.Dispose(); }
            restForms.Clear(); if (restImage != null) { restImage.Dispose(); restImage = null; }
        }
        void Dispatch(Action action) { if (!quitting && !dispatcher.IsDisposed) try { dispatcher.BeginInvoke(new Action(() => Safe(action))); } catch (InvalidOperationException) { } }
        void OnSession(object sender, SessionSwitchEventArgs e) { Dispatch(() => { if (e.Reason == SessionSwitchReason.SessionLock) engine.Locked = true; if (e.Reason == SessionSwitchReason.SessionUnlock) engine.Locked = false; Tick(); }); }
        void OnPower(object sender, PowerModeChangedEventArgs e) { Dispatch(() => { if (e.Mode == PowerModes.Suspend) engine.Suspended = true; if (e.Mode == PowerModes.Resume) engine.Suspended = false; Tick(); }); }
        void OnDisplay(object sender, EventArgs e) { Dispatch(() => { restartImages = engine.State == "rest"; Tick(); }); }
        void RunHiddenQA()
        {
            try
            {
                Directory.CreateDirectory(qaDirectory); OpenSettings(false);
                var checks = settingsForm.CheckInteractions();
                settingsForm.RefreshStatus();
                using (var bitmap = new Bitmap(settingsForm.Width, settingsForm.Height)) { settingsForm.DrawToBitmap(bitmap, new Rectangle(0, 0, settingsForm.Width, settingsForm.Height)); bitmap.Save(Path.Combine(qaDirectory, "native-settings.png")); }
                settingsForm.CaptureLower(Path.Combine(qaDirectory, "native-settings-lower.png"));
                settingsForm.CaptureAbout(Path.Combine(qaDirectory, "native-about.png"));
                ShowRest(true);
                using (var bitmap = new Bitmap(restForms[0].Width, restForms[0].Height)) { restForms[0].DrawToBitmap(bitmap, restForms[0].ClientRectangle); bitmap.Save(Path.Combine(qaDirectory, "native-rest-hidden.png")); }
                CloseRest();
                File.WriteAllText(Path.Combine(qaDirectory, "ui-results.json"), Store.Json.Serialize(new { passed = true, interactions = checks, screenCount = Screen.AllScreens.Length, visibleWindows = 0, formalAutostartTouched = false, actualAudioListening = false, actualPowerEvents = false }));
            }
            catch (Exception error) { Log(error); Directory.CreateDirectory(qaDirectory); File.WriteAllText(Path.Combine(qaDirectory, "ui-results.json"), Store.Json.Serialize(new { passed = false, error = error.ToString() })); }
            finally { ExitThread(); }
        }
        void FinishTimedSmoke()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(timedSmoke)));
            double duration = smokeStarted && smokeCompleted ? (smokeRestEnded - smokeRestBegan).TotalSeconds : 0;
            File.WriteAllText(timedSmoke, Store.Json.Serialize(new { passed = smokeStarted && smokeCompleted && smokeReleased && duration >= 20 && duration < 23, observedRestSeconds = duration, transitions = smokeTransitions, scope = "actual UI timer with 1-second work interval and original 20-second rest; transparent/offscreen drawing, audio suppressed; not a full 20-minute work cycle or hardware-event test" }));
        }
        void Safe(Action action) { try { action(); } catch (Exception error) { Log(error); if (!benchmark) MessageBox.Show(error.Message, "护眼休息", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        void Log(Exception error) { try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(config), "errors.log"), DateTime.Now.ToString("o") + " " + error + Environment.NewLine); } catch { } }
        protected override void ExitThreadCore()
        {
            if (quitting) return; quitting = true; timer.Stop();
            SystemEvents.SessionSwitch -= OnSession; SystemEvents.PowerModeChanged -= OnPower; SystemEvents.DisplaySettingsChanged -= OnDisplay;
            CloseRest(); sounds.Dispose();
            if (settingsForm != null) { var form = settingsForm; form.AllowDiscard = true; form.Close(); form.Dispose(); }
            tray.Visible = false; tray.Dispose(); appIcon.Dispose(); dispatcher.Dispose(); timer.Dispose(); base.ExitThreadCore();
        }
    }

    sealed class RestForm : Form
    {
        readonly Image photo;
        readonly Font messageFont = new Font("Microsoft YaHei", 90, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Font countFont = new Font("Microsoft YaHei", 42, FontStyle.Regular, GraphicsUnit.Pixel);
        int seconds = 20;
        public bool AllowClose;
        public RestForm(Image image, Rectangle bounds, bool hidden)
        {
            photo = image; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = bounds;
            TopMost = !hidden; ShowInTaskbar = false; BackColor = Color.Black; DoubleBuffered = true; Text = "护眼休息";
        }
        public void UpdateCountdown(DateTime? deadline)
        {
            int value = deadline.HasValue ? Math.Max(0, (int)Math.Ceiling((deadline.Value - DateTime.UtcNow).TotalSeconds)) : 20;
            if (value != seconds) { seconds = value; Invalidate(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Max((float)Width / photo.Width, (float)Height / photo.Height);
            float width = photo.Width * scale, height = photo.Height * scale;
            e.Graphics.DrawImage(photo, (Width - width) / 2, (Height - height) / 2, width, height);
            using (var shade = new SolidBrush(Color.FromArgb(100, 0, 0, 0))) e.Graphics.FillRectangle(shade, ClientRectangle);
            TextRenderer.DrawText(e.Graphics, "请看向约6米外", messageFont, new Rectangle(10, Height / 2 - 100, Math.Max(1, Width - 20), 125), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, seconds + "秒", countFont, new Rectangle(0, Height / 2 + 40, Width, 75), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (!AllowClose && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { messageFont.Dispose(); countFont.Dispose(); } base.Dispose(disposing); }
    }
}
