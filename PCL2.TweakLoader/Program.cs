using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace PclTweaks
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                if (new AssemblyName(e.Name).Name != "Mono.Cecil") return null;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PclTweaks.Mono.Cecil.dll"))
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return Assembly.Load(buffer.ToArray());
                }
            };
            return Run(args);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Contains("--prepare") || args.Contains("--launch"))
                {
                    var target = Value(args, "--target") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plain Craft Launcher 2.exe");
                    var settings = MainForm.LoadSettings();
                    var options = args.Contains("--prepare") ? new TweakOptions
                    {
                        DisableAutomaticUpdates = args.Contains("--disable-updates"),
                        DisableDownloadRestriction = args.Contains("--unrestrict-downloads")
                    } : settings.Options;
                    if (Value(args, "--target") == null && args.Contains("--launch") && !string.IsNullOrWhiteSpace(settings.LauncherPath))
                        target = settings.LauncherPath;
                    var executable = Launcher.Prepare(target, options);
                    if (args.Contains("--launch")) Launcher.Start(executable, Value(args, "--arguments") ?? "");
                    if (Value(args, "--result") is string result) File.WriteAllText(result, executable);
                    return 0;
                }
                using (var form = new MainForm())
                {
                    if (Value(args, "--screenshot") is string screenshot)
                    {
                        form.Show();
                        Application.DoEvents();
                        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                            bitmap.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        return 0;
                    }
                    Application.Run(form);
                }
                return 0;
            }
            catch (Exception e)
            {
                var errorFile = Value(args, "--error");
                if (errorFile != null) File.WriteAllText(errorFile, e.ToString());
                else MessageBox.Show(e.Message, "PCL2 第三方微调加载器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static string Value(string[] args, string flag)
        {
            var index = Array.IndexOf(args, flag);
            if (index < 0) return null;
            if (index + 1 == args.Length) throw new ArgumentException("参数缺少值：" + flag);
            return args[index + 1];
        }
    }
}
