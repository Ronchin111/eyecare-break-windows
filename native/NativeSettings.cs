using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace EyeCareNative
{
    sealed class SettingsForm : Form
    {
        readonly EyeContext context;
        readonly Preferences original;
        readonly FlowLayoutPanel body;
        readonly Label status;
        readonly Label feedback;
        readonly Label volumeText;
        readonly DataGridView grid;
        readonly Font scheduleHeaderFont;
        readonly List<Font> uiFonts = new List<Font>();
        readonly List<FlowLayoutPanel> sections = new List<FlowLayoutPanel>();
        FlowLayoutPanel section;
        FlowLayoutPanel landscapeRow;
        Panel footer;
        Button restoreSchedule;
        Button addSchedule;
        bool sizing;
        readonly RadioButton daily;
        readonly RadioButton today;
        readonly TrackBar volume;
        readonly Button preview;
        readonly Button saveButton;
        readonly CheckBox startupCheckbox;
        readonly Label startupHint;
        readonly Label editionBadge;
        readonly Panel headerPanel;
        readonly Label headerTitle;
        readonly Label headerSubtitle;
        readonly Button aboutButton;
        readonly List<Image> thumbnails = new List<Image>();
        readonly List<RadioButton> landscapeButtons = new List<RadioButton>();
        List<Interval> dailyDraft;
        List<Interval> todayDraft;
        bool todayEdited;
        readonly string draftDate;
        bool showingToday;
        bool suppress;
        bool dirty;
        string selected;
        public bool AllowDiscard;
        public bool OffscreenCapture;
        protected override bool ShowWithoutActivation { get { return OffscreenCapture; } }

        public SettingsForm(EyeContext owner)
        {
            context = owner; original = Store.Clone(owner.Settings); selected = original.landscape;
            draftDate = DateTime.Now.ToString("yyyy-MM-dd");
            dailyDraft = Copy(original.schedule.daily);
            todayDraft = Copy(original.schedule.@override != null && original.schedule.@override.date == draftDate ? original.schedule.@override.intervals : original.schedule.daily);
            showingToday = original.schedule.@override != null && original.schedule.@override.date == draftDate;
            Text = "护眼休息 · " + Program.Version;
            Icon = context.AppIcon;
            Font = OwnFont(10, FontStyle.Regular);
            BackColor = Color.FromArgb(243, 246, 244); ForeColor = Color.FromArgb(35, 49, 43);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, Math.Min(850, Screen.PrimaryScreen.WorkingArea.Height - 90)); MinimumSize = new Size(780, 600);
            body = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 20, 22, 8) }; Controls.Add(body);
            Section(null);
            headerPanel = new Panel { Height = 108, Margin = Padding.Empty }; section.Controls.Add(headerPanel);
            headerTitle = new Label { Text = "护眼休息", AutoSize = true, Font = OwnFont(20, FontStyle.Bold), ForeColor = ForeColor };
            headerSubtitle = new Label { Text = "每20分钟，留20秒给远方  ·  " + Program.Version + (context.IsCandidate ? "  ·  隔离预览" : "  ·  Windows原生轻量版"), AutoSize = true, Font = OwnFont(9, FontStyle.Regular), ForeColor = Color.FromArgb(103, 117, 109) };
            editionBadge = new Label { AutoSize = true, Font = OwnFont(10, FontStyle.Regular), ForeColor = Color.FromArgb(28, 102, 85) };
            headerPanel.Controls.AddRange(new Control[] { headerTitle, headerSubtitle, editionBadge });
            Section("提醒控制");
            status = AddLabel(context.Status, 11, Color.FromArgb(28, 102, 85));
            var pause = Row();
            foreach (int minutes in new[] { 30, 60, 120, 180 })
            {
                int duration = minutes;
                pause.Controls.Add(Button(minutes < 60 ? "暂停30分钟" : "暂停" + (minutes / 60) + "小时", () => context.Pause(duration)));
            }
            pause.Controls.Add(Button("恢复提醒", () => context.Resume()));
            Section("免提醒时段");
            var scope = Row();
            daily = new RadioButton { Text = "每天重复", AutoSize = true, Checked = !showingToday, Margin = new Padding(0, 6, 24, 6) };
            today = new RadioButton { Text = "仅今天", AutoSize = true, Checked = showingToday, Margin = new Padding(0, 6, 0, 6) };
            scope.Controls.Add(daily); scope.Controls.Add(today);
            AddLabel("使用24小时制，如12:00。结束早于开始时跨到次日；最多32个时段。", 9, Color.FromArgb(103, 117, 109));
            grid = new DataGridView { Height = 150, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = true, AllowUserToResizeRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(225, 232, 227), Margin = new Padding(0, 7, 0, 9) };
            scheduleHeaderFont = OwnFont(10, FontStyle.Regular);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = scheduleHeaderFont, Alignment = DataGridViewContentAlignment.MiddleCenter, WrapMode = DataGridViewTriState.False, Padding = new Padding(8, 8, 8, 8), BackColor = Color.FromArgb(235, 241, 237), ForeColor = ForeColor, SelectionBackColor = Color.FromArgb(235, 241, 237), SelectionForeColor = ForeColor };
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.DefaultCellStyle = new DataGridViewCellStyle { Font = Font, Padding = new Padding(6, 7, 6, 7), Alignment = DataGridViewContentAlignment.MiddleCenter, ForeColor = ForeColor, SelectionBackColor = Color.FromArgb(218, 237, 227), SelectionForeColor = ForeColor };
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.Columns.Add("start", "开始时间"); grid.Columns.Add("end", "结束时间");
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "remove", HeaderText = "操作", Text = "移除", UseColumnTextForButtonValue = true, FillWeight = 38, MinimumWidth = 80, FlatStyle = FlatStyle.Flat });
            section.Controls.Add(grid);
            grid.CellContentClick += (s, e) => { if (e.ColumnIndex == 2 && e.RowIndex >= 0 && !grid.Rows[e.RowIndex].IsNewRow) { grid.Rows.RemoveAt(e.RowIndex); MarkSchedule(); } };
            grid.CellValueChanged += (s, e) => MarkSchedule(); grid.UserDeletedRow += (s, e) => MarkSchedule();
            daily.CheckedChanged += (s, e) => { if (daily.Checked) SwitchScope(false); };
            today.CheckedChanged += (s, e) => { if (today.Checked) SwitchScope(true); };
            var scheduleActions = Row(); addSchedule = Button("添加时段", AddSchedule); scheduleActions.Controls.Add(addSchedule);
            restoreSchedule = Button("恢复每天的时段", () => Save(true)); scheduleActions.Controls.Add(restoreSchedule);
            AddLabel("点击“添加时段”后填写时间；“恢复每天的时段”会取消今天的单独安排，并保存当前设置。", 9, Color.FromArgb(103, 117, 109));
            Section("休息画面");
            landscapeRow = Row();
            for (int i = 0; i < Assets.Ids.Length; i++)
            {
                int index = i; var card = new Panel { Width = 130, Height = 112, Margin = new Padding(0, 2, 10, 8) };
                string file = Path.Combine(context.AssetsRoot, "thumbnails", Assets.Files[index]);
                Image image = Image.FromFile(file); thumbnails.Add(image);
                var picture = new PictureBox { Height = 78, Image = image, SizeMode = PictureBoxSizeMode.Zoom, Top = 0, Left = 0, Cursor = Cursors.Hand };
                var radio = new RadioButton { Text = Assets.Names[index], AutoSize = true, Top = 84, Left = 0, Checked = selected == Assets.Ids[index] };
                landscapeButtons.Add(radio);
                radio.CheckedChanged += (s, e) => { if (radio.Checked && !suppress) { foreach (Panel other in landscapeRow.Controls.OfType<Panel>()) foreach (RadioButton r in other.Controls.OfType<RadioButton>()) if (r != radio) r.Checked = false; selected = Assets.Ids[index]; dirty = true; } };
                picture.Click += (s, e) => radio.Checked = true; card.Controls.Add(picture); card.Controls.Add(radio); landscapeRow.Controls.Add(card);
            }
            var previewRow = Row(); preview = Button("体验所选画面20秒", () => context.Preview(selected)); previewRow.Controls.Add(preview);
            Section("声音与启动");
            AddLabel("开始提示约0.7秒，结束提示约2.6秒，两种声音共用音量。", 9, Color.FromArgb(103, 117, 109));
            if (original.silent) AddLabel("当前已沿用原设置的全局静音，日常提示音不会播放。", 9, Color.FromArgb(103, 117, 109));
            var volumeRow = Row(); volumeText = new Label { AutoSize = false, Width = 92, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Text = "音量 " + Math.Round(original.volume * 100) + "%", Margin = new Padding(0, 4, 8, 4) };
            volume = new TrackBar { Minimum = 10, Maximum = 100, Value = (int)Math.Round(original.volume * 100), TickStyle = TickStyle.None, Width = 300, Height = 36, AutoSize = false, Margin = new Padding(0, 4, 14, 4) };
            volume.ValueChanged += (s, e) => { dirty = true; volumeText.Text = "音量 " + volume.Value + "%"; };
            volumeRow.Controls.Add(volumeText); volumeRow.Controls.Add(volume); volumeRow.Controls.Add(Button("试听结束提示", () => context.TestSound(volume.Value / 100.0)));
            startupCheckbox = new CheckBox { Text = "登录电脑后自动运行", Checked = original.openAtLogin, Enabled = !context.IsCandidate, AutoSize = true, Padding = new Padding(0, 3, 0, 3), Margin = new Padding(0, 8, 0, 3) }; section.Controls.Add(startupCheckbox);
            startupCheckbox.CheckedChanged += (s, e) => dirty = true;
            startupHint = AddLabel(context.IsCandidate ? "隔离预览不修改登录启动设置。" : "修改后请保存，登录电脑时按此设置启动。", 9, Color.FromArgb(103, 117, 109));
            feedback = new Label { Text = context.IsCandidate ? "隔离预览 · 设置保存在独立数据中" : "修改后请保存设置", ForeColor = Color.FromArgb(103, 117, 109), Font = OwnFont(9, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft };
            saveButton = Button("保存设置", () => Save(false)); saveButton.BackColor = Color.FromArgb(28, 102, 85); saveButton.ForeColor = Color.White; saveButton.FlatAppearance.BorderSize = 0; saveButton.MinimumSize = new Size(132, 38);
            aboutButton = Button("关于", ShowAbout); aboutButton.MinimumSize = new Size(80, 38);
            footer = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.White };
            footer.Controls.Add(feedback); footer.Controls.Add(aboutButton); footer.Controls.Add(saveButton); Controls.Add(footer);
            footer.Paint += (s, e) => { using (var pen = new Pen(Color.FromArgb(225, 232, 227))) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            body.SizeChanged += (s, e) => LayoutSections();
            footer.SizeChanged += (s, e) => LayoutSections();
            LayoutSections(); LoadGrid(); dirty = false; RefreshStatus();
        }
        static List<Interval> Copy(List<Interval> values) { return values.Select(v => new Interval(v.start, v.end)).ToList(); }
        Font OwnFont(float size, FontStyle style) { var font = new Font("Microsoft YaHei UI", size, style); uiFonts.Add(font); return font; }
        void Section(string title)
        {
            section = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White, Padding = new Padding(18, 12, 18, 12), Margin = new Padding(0, 0, 0, 12) };
            sections.Add(section); body.Controls.Add(section);
            if (title != null) AddLabel(title, 12, ForeColor);
        }
        Label AddLabel(string text, float size, Color color)
        {
            var label = new Label { Text = text, AutoSize = true, ForeColor = color, Font = OwnFont(size, size >= 12 ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 4, 0, size >= 12 ? 8 : 5) };
            section.Controls.Add(label); return label;
        }
        FlowLayoutPanel Row()
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
            section.Controls.Add(row); return row;
        }
        Button Button(string text, Action action)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(112, 36), Padding = new Padding(12, 6, 12, 6), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = ForeColor, Margin = new Padding(0, 3, 8, 3), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = Color.FromArgb(205, 218, 210); b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 244, 239);
            b.Click += (s, e) => { try { action(); } catch (Exception error) { feedback.Text = error.Message; feedback.ForeColor = Color.Firebrick; } }; return b;
        }
        void LayoutSections()
        {
            if (sizing || footer == null) return;
            sizing = true; body.SuspendLayout();
            try
            {
                int width = Math.Max(1, body.ClientSize.Width - body.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
                foreach (var item in sections)
                {
                    item.MinimumSize = new Size(width, 0); item.MaximumSize = new Size(width, 0); item.Width = width;
                    int inner = Math.Max(1, width - item.Padding.Horizontal);
                    foreach (Control child in item.Controls)
                    {
                        if (child is Label) child.MaximumSize = new Size(inner, 0);
                        else if (child is FlowLayoutPanel) { child.MinimumSize = new Size(inner, 0); child.MaximumSize = new Size(inner, 0); child.Width = inner; }
                        else if (child == grid || child == headerPanel) child.Width = inner;
                    }
                }
                LayoutHeader();
                if (landscapeRow != null)
                {
                    int minimumCard = Math.Max(125, landscapeButtons.Max(radio => radio.GetPreferredSize(Size.Empty).Width) + 4);
                    int count = Math.Min(5, Math.Max(1, (landscapeRow.Width + 10) / (minimumCard + 10)));
                    int cardWidth = Math.Max(1, (landscapeRow.Width - (count - 1) * 10) / count);
                    int i = 0;
                    foreach (Panel card in landscapeRow.Controls.OfType<Panel>())
                    {
                        card.Width = cardWidth; card.Margin = new Padding(0, 2, (++i % count == 0) ? 0 : 10, 8);
                        foreach (PictureBox picture in card.Controls.OfType<PictureBox>()) picture.Width = cardWidth;
                        card.Height = card.Controls.OfType<RadioButton>().Max(radio => radio.Top + radio.GetPreferredSize(Size.Empty).Height) + 4;
                    }
                }
                saveButton.Location = new Point(Math.Max(16, footer.ClientSize.Width - saveButton.Width - 22), (footer.Height - saveButton.Height) / 2);
                aboutButton.Location = new Point(Math.Max(16, saveButton.Left - aboutButton.Width - 12), (footer.Height - aboutButton.Height) / 2);
                feedback.SetBounds(22, 8, Math.Max(1, aboutButton.Left - 38), footer.Height - 16);
            }
            finally { body.ResumeLayout(true); sizing = false; }
        }
        void MarkSchedule() { if (!suppress) { dirty = true; if (showingToday) todayEdited = true; } }
        void AddSchedule()
        {
            grid.EndEdit();
            if (grid.Rows.Count >= 32) throw new ArgumentException("最多添加32个免提醒时段。");
            int row = grid.Rows.Add("", "");
            MarkSchedule(); body.ScrollControlIntoView(grid);
            grid.CurrentCell = grid.Rows[row].Cells[0]; grid.Focus(); grid.BeginEdit(true);
        }
        List<Interval> ReadGrid()
        {
            grid.EndEdit(); var result = new List<Interval>();
            foreach (DataGridViewRow row in grid.Rows) if (!row.IsNewRow) result.Add(new Interval(Convert.ToString(row.Cells[0].Value).Trim(), Convert.ToString(row.Cells[1].Value).Trim()));
            return result;
        }
        void SwitchScope(bool value)
        {
            if (grid == null || showingToday == value) return;
            if (showingToday) todayDraft = ReadGrid(); else dailyDraft = ReadGrid();
            showingToday = value;
            if (value && !todayEdited && (original.schedule.@override == null || original.schedule.@override.date != draftDate)) todayDraft = Copy(dailyDraft);
            LoadGrid();
        }
        void LoadGrid()
        {
            suppress = true; grid.Rows.Clear(); foreach (Interval interval in showingToday ? todayDraft : dailyDraft) grid.Rows.Add(interval.start, interval.end); suppress = false;
        }
        void Save(bool restoreDaily)
        {
            if (showingToday) todayDraft = ReadGrid(); else dailyDraft = ReadGrid();
            var value = Store.Clone(context.Settings); value.schedule.daily = Copy(dailyDraft);
            bool stale = todayEdited && draftDate != DateTime.Now.ToString("yyyy-MM-dd");
            if (restoreDaily) value.schedule.@override = null;
            else if (todayEdited && !stale) value.schedule.@override = new DayOverride { date = draftDate, intervals = Copy(todayDraft) };
            value.volume = volume.Value / 100.0; value.landscape = selected; value.openAtLogin = startupCheckbox.Checked;
            context.Save(value); original.schedule = Store.Clone(value).schedule; dirty = false; todayEdited = false;
            feedback.ForeColor = Color.FromArgb(37, 95, 68); feedback.Text = stale ? "其他设置已保存；昨日时段草稿已过期，请重新打开面板编辑今天的时段。" : "已保存，立即生效。";
            if (restoreDaily) { daily.Checked = true; LoadGrid(); }
        }
        public void RefreshStatus()
        {
            status.Text = context.Status; preview.Enabled = context.CanPreview;
        }
        void LayoutHeader()
        {
            editionBadge.Text = "免费开源";
            editionBadge.Padding = new Padding(12, 9, 12, 9);
            editionBadge.BackColor = Color.FromArgb(235, 244, 239);
            editionBadge.Size = editionBadge.GetPreferredSize(Size.Empty);
            headerTitle.MaximumSize = new Size(Math.Max(1, headerPanel.ClientSize.Width - editionBadge.Width - 18), 0);
            headerTitle.Location = new Point(0, 4);
            editionBadge.Location = new Point(Math.Max(0, headerPanel.ClientSize.Width - editionBadge.Width), 0);
            headerSubtitle.MaximumSize = new Size(Math.Max(1, headerPanel.ClientSize.Width), 0);
            headerSubtitle.Location = new Point(0, Math.Max(headerTitle.Bottom, editionBadge.Bottom) + 10);
            headerPanel.Height = headerSubtitle.Bottom + 8;
        }
        void ShowAbout() { using (var about = new AboutForm(context)) about.ShowDialog(this); }
        public void CaptureAbout(string path)
        {
            using (var about = new AboutForm(context))
            {
                about.OffscreenCapture = true; about.Opacity = 0; about.StartPosition = FormStartPosition.Manual; about.Location = new Point(SystemInformation.VirtualScreen.Right + 100, 0); about.Show(); about.PerformLayout();
                using (var bitmap = new Bitmap(about.Width, about.Height)) { about.DrawToBitmap(bitmap, new Rectangle(0, 0, about.Width, about.Height)); bitmap.Save(path); }
            }
        }
        public void CaptureLower(string path)
        {
            body.ScrollControlIntoView(startupHint);
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path); }
        }
        public string[] CheckInteractions()
        {
            var checks = new List<string>();
            if (editionBadge.Text != "免费开源" || editionBadge.Right > headerPanel.ClientSize.Width || editionBadge.Left <= headerTitle.Right || editionBadge.Bounds.IntersectsWith(headerSubtitle.Bounds)) throw new Exception("Free edition header is clipped or overlaps");
            checks.Add("free edition badge fits header without overlap");
            if (!aboutButton.Visible || aboutButton.Right > saveButton.Left || aboutButton.Bottom > footer.Height) throw new Exception("About button is clipped");
            checks.Add("about action fits fixed footer beside save");
            Size restorePreferred = restoreSchedule.GetPreferredSize(Size.Empty);
            if (restoreSchedule.Width < restorePreferred.Width || restoreSchedule.Height < restorePreferred.Height) throw new Exception("Restore schedule button text is clipped");
            checks.Add("restore schedule caption fits its measured preferred size");
            Point savedScroll = body.AutoScrollPosition;
            body.ScrollControlIntoView(startupHint);
            Size startupPreferred = startupCheckbox.GetPreferredSize(Size.Empty);
            Rectangle startupBounds = body.RectangleToClient(startupCheckbox.RectangleToScreen(startupCheckbox.ClientRectangle));
            Rectangle hintBounds = body.RectangleToClient(startupHint.RectangleToScreen(startupHint.ClientRectangle));
            if (startupCheckbox.Width < startupPreferred.Width || startupCheckbox.Height < startupPreferred.Height || startupBounds.Top < 0 || startupBounds.Bottom > body.ClientSize.Height || hintBounds.Bottom > body.ClientSize.Height) throw new Exception("Startup option text cannot be displayed in full");
            checks.Add("startup label and separate hint fit and scroll fully into view");
            body.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            using (var graphics = grid.CreateGraphics()) foreach (DataGridViewColumn column in grid.Columns)
            {
                if (String.IsNullOrEmpty(column.HeaderText)) continue;
                Size text = TextRenderer.MeasureText(graphics, column.HeaderText, scheduleHeaderFont, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (text.Height + grid.ColumnHeadersDefaultCellStyle.Padding.Vertical + 2 > grid.ColumnHeadersHeight || text.Width + grid.ColumnHeadersDefaultCellStyle.Padding.Horizontal + 2 > column.Width) throw new Exception("Schedule header text is clipped");
            }
            checks.Add("schedule header text fits current DPI and padding");
            if (!saveButton.Visible || saveButton.Left < 0 || saveButton.Right > saveButton.Parent.ClientSize.Width || saveButton.Bottom > saveButton.Parent.ClientSize.Height) throw new Exception("Save button is outside visible footer"); checks.Add("save button visible inside footer");
            context.Pause(30); if (context.Settings.pausedUntil <= Store.Unix(DateTime.UtcNow)) throw new Exception("Pause action failed"); checks.Add("pause persists");
            context.Resume(); if (context.Settings.pausedUntil != 0) throw new Exception("Resume action failed"); checks.Add("resume persists");
            volume.Value = 45; saveButton.PerformClick(); if (context.Settings.volume != 0.45) throw new Exception("Volume save button failed"); checks.Add("volume saved via actual button click");
            daily.Checked = true; int previousRows = grid.Rows.Count; addSchedule.PerformClick(); if (grid.Rows.Count != previousRows + 1 || grid.CurrentCell.ColumnIndex != 0 || grid.CurrentCell.RowIndex != previousRows) throw new Exception("Add schedule button failed");
            grid.EndEdit(); grid.Rows[previousRows].Cells[0].Value = "12:00"; grid.Rows[previousRows].Cells[1].Value = "13:00"; MarkSchedule(); Save(false); if (!context.Settings.schedule.daily.Any(i => i.start == "12:00" && i.end == "13:00")) throw new Exception("Schedule save failed"); checks.Add("add schedule button inserts editable row and saves daily interval");
            today.Checked = true; grid.Rows.Clear(); grid.Rows.Add("23:00", "01:00"); MarkSchedule(); Save(false); if (context.Settings.schedule.@override == null || context.Settings.schedule.@override.intervals[0].end != "01:00") throw new Exception("Today save failed"); checks.Add("today overnight save");
            restoreSchedule.PerformClick(); if (context.Settings.schedule.@override != null) throw new Exception("Restore daily failed"); checks.Add("restore daily via visible button");
            int row = grid.Rows.Add("25:00", "13:00"); MarkSchedule(); bool rejected = false; try { Save(false); } catch { rejected = true; } grid.Rows.RemoveAt(row); if (!rejected) throw new Exception("Invalid time accepted"); checks.Add("invalid edit rejected without saving");
            landscapeButtons[2].Checked = true; Save(false); if (context.Settings.landscape != "bamboo") throw new Exception("Landscape save failed"); checks.Add("landscape save");
            return checks.ToArray();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            grid.EndEdit();
            if (dirty && !AllowDiscard && e.CloseReason == CloseReason.UserClosing && MessageBox.Show("还有未保存的设置，是否放弃这些修改？", "护眼休息", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) { foreach (Image image in thumbnails) image.Dispose(); foreach (var font in uiFonts) font.Dispose(); } base.Dispose(disposing); }
    }
}
