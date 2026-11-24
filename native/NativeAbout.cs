using System;
using System.Drawing;
using System.Windows.Forms;

namespace EyeCareNative
{
    sealed class AboutForm : Form
    {
        public bool OffscreenCapture;
        protected override bool ShowWithoutActivation { get { return OffscreenCapture; } }
        readonly Font bodyFont = new Font("Microsoft YaHei UI", 10);
        readonly Font titleFont = new Font("Microsoft YaHei UI", 18, FontStyle.Bold);
        public AboutForm(EyeContext context)
        {
            Text = "关于护眼休息"; Icon = context.AppIcon; Font = bodyFont;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 445); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Color.White; ForeColor = Color.FromArgb(35, 49, 43);
            var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(26), AutoScroll = true };
            Controls.Add(content);
            content.Controls.Add(new Label { Text = "护眼休息", AutoSize = true, Font = titleFont, Margin = new Padding(0, 0, 0, 10) });
            content.Controls.Add(Label("版本 " + Program.Version, 12));
            content.Controls.Add(Label("护眼休息——每20分钟提醒你休息20秒的 Windows 原生轻量护眼工具。基于开源 Stretchly 理念重写，5 张高清风景休息画面、免打扰时段、开机自启。", 20));
            content.Controls.Add(Label("免费开源 · 全部功能永久免费", 20));
            content.Controls.Add(Label("开发者：Ronchin", 10));
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 10, 24, 10) };
            var close = new Button { Text = "关闭", AutoSize = true, Padding = new Padding(18, 5, 18, 5), FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK }; footer.Controls.Add(close); Controls.Add(footer); AcceptButton = close; CancelButton = close;
        }
        Label Label(string text, int bottom) { return new Label { Text = text, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(0, 0, 0, bottom) }; }
        protected override void Dispose(bool disposing) { if (disposing) { bodyFont.Dispose(); titleFont.Dispose(); } base.Dispose(disposing); }
    }
}
