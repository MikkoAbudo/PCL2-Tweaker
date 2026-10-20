using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PclTweaks
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(26, 39, 57);
        private static readonly Color Muted = Color.FromArgb(89, 104, 123);
        private static readonly Color Accent = Color.FromArgb(38, 98, 224);
        private readonly TextBox path = new TextBox();
        private readonly CheckBox updates = new CheckBox();
        private readonly CheckBox downloads = new CheckBox();
        private readonly Label status = new Label();
        private readonly Button launch = new Button();
        private readonly Button save = new Button();
        private readonly Button browse = new Button();
        private readonly LoaderSettings settings;
        private bool busy;

        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PCL2.TweakLoader.settings.json");

        public static LoaderSettings LoadSettings() => File.Exists(SettingsPath)
            ? JsonFile.Read<LoaderSettings>(SettingsPath) : new LoaderSettings();

        public MainForm()
        {
            SuspendLayout();
            Text = "PCL2 第三方微调加载器";
            Font = new Font("Segoe UI", 10F);
            ForeColor = Ink;
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 558);
            MinimumSize = new Size(716, 597);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            try { settings = LoadSettings(); }
            catch { settings = new LoaderSettings(); }

            Controls.Add(Label("PCL2", 28, 22, 100, 26, 11, Accent, FontStyle.Bold));
            Controls.Add(Label("按需调整，轻松启动。", 26, 54, 650, 40, 22, Ink, FontStyle.Bold));
            Controls.Add(Label("选择微调选项，然后启动 Plain Craft Launcher 2。", 29, 101, 645, 26, 10, Muted));

            var panel = new Panel { Bounds = new Rectangle(28, 148, 644, 93), BackColor = Color.White };
            panel.Controls.Add(Label("启动器程序", 16, 12, 590, 21, 9, Muted, FontStyle.Bold));
            path.SetBounds(16, 44, 492, 28);
            path.Text = settings.LauncherPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plain Craft Launcher 2.exe");
            panel.Controls.Add(path);
            ConfigureButton(browse, "浏览…", 519, 40, 108, false);
            browse.Click += (_, __) => Browse();
            panel.Controls.Add(browse);
            Controls.Add(panel);

            Controls.Add(TweakCard(updates, "关闭自动更新",
                "跳过自动更新提示和安装，保留手动检查更新。", 258, settings.DisableAutomaticUpdates));
            Controls.Add(TweakCard(downloads, "关闭 RestrictMcDownload 下载限制",
                "恢复 Minecraft 下载和整合包安装功能。", 351, settings.DisableDownloadRestriction));
            Controls.Add(Label("选项在下次启动时生效，原始启动器文件保持不变。", 29, 446, 645, 25, 9, Muted));

            status.SetBounds(29, 485, 360, 55);
            status.Font = new Font("Segoe UI", 9);
            status.ForeColor = Muted;
            status.Text = "就绪 · 支持指定的 PCL2 2.99.0.0 构建";
            status.AccessibleName = "加载器状态";
            Controls.Add(status);
            ConfigureButton(save, "保存", 405, 491, 86, false);
            save.Click += (_, __) => SaveOnly();
            Controls.Add(save);
            ConfigureButton(launch, "启动 PCL2", 503, 491, 169, true);
            launch.Click += async (_, __) => await LaunchAsync();
            Controls.Add(launch);
            AcceptButton = launch;
            path.AccessibleName = "启动器程序路径";
            updates.CheckedChanged += (_, __) => SelectionChanged();
            downloads.CheckedChanged += (_, __) => SelectionChanged();
            path.TextChanged += (_, __) => SelectionChanged();
            ResumeLayout(true);
        }

        private static Label Label(string text, int x, int y, int width, int height, float size,
            Color color, FontStyle style = FontStyle.Regular) => new Label
        {
            Text = text, Bounds = new Rectangle(x, y, width, height),
            ForeColor = color, Font = new Font("Segoe UI", size, style)
        };

        private static Panel TweakCard(CheckBox toggle, string title, string description, int y, bool enabled)
        {
            var panel = new Panel { Bounds = new Rectangle(28, y, 644, 77), BackColor = Color.White };
            toggle.SetBounds(17, 14, 610, 26);
            toggle.Text = title;
            toggle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            toggle.Checked = enabled;
            toggle.Cursor = Cursors.Hand;
            panel.Controls.Add(toggle);
            panel.Controls.Add(Label(description, 37, 43, 590, 25, 9, Muted));
            return panel;
        }

        private static void ConfigureButton(Button button, string text, int x, int y, int width, bool primary)
        {
            button.Text = text;
            button.SetBounds(x, y, width, 38);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(209, 217, 230);
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        private void Browse()
        {
            using (var dialog = new OpenFileDialog { Title = "选择原始 Plain Craft Launcher 2.exe", Filter = "Windows 可执行程序 (*.exe)|*.exe", CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.FileName;
        }

        private void SelectionChanged()
        {
            if (busy) return;
            status.ForeColor = Muted;
            status.Text = "选项将在下次启动时生效。";
        }

        private void CaptureSettings()
        {
            settings.LauncherPath = Path.GetFullPath(path.Text.Trim().Trim('"'));
            settings.DisableAutomaticUpdates = updates.Checked;
            settings.DisableDownloadRestriction = downloads.Checked;
        }

        private void SaveOnly()
        {
            try
            {
                CaptureSettings();
                JsonFile.Write(SettingsPath, settings);
                status.ForeColor = Color.FromArgb(32, 121, 79);
                status.Text = "设置已保存。";
            }
            catch (Exception e) { ShowError(e); }
        }

        private async Task LaunchAsync()
        {
            if (busy) return;
            SetBusy(true);
            try
            {
                CaptureSettings();
                var options = settings.Options;
                status.ForeColor = Muted;
                status.Text = "正在准备启动器…";
                var executable = await Task.Run(() => Launcher.Prepare(settings.LauncherPath, options));
                JsonFile.Write(SettingsPath, settings);
                using (Launcher.Start(executable)) { }
                status.ForeColor = Color.FromArgb(32, 121, 79);
                status.Text = options.Any ? "已按所选微调选项启动。" : "已启动原版，所有微调均已关闭。";
            }
            catch (Exception e) { ShowError(e); }
            finally { SetBusy(false); }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            launch.Enabled = save.Enabled = browse.Enabled = path.Enabled = updates.Enabled = downloads.Enabled = !value;
            UseWaitCursor = value;
        }

        private void ShowError(Exception e)
        {
            status.ForeColor = Color.FromArgb(180, 57, 57);
            status.Text = "操作未能完成。";
            MessageBox.Show(this, e.Message, "PCL2 第三方微调加载器", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Let an in-progress atomic write finish before disposing the window.
            if (busy) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
