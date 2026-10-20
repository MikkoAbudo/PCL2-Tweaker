using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace PclTweaks
{
    [DataContract]
    public sealed class LoaderSettings
    {
        [DataMember] public string LauncherPath { get; set; }
        [DataMember] public bool DisableAutomaticUpdates { get; set; } = true;
        [DataMember] public bool DisableDownloadRestriction { get; set; } = true;
        public TweakOptions Options => new TweakOptions
        {
            DisableAutomaticUpdates = DisableAutomaticUpdates,
            DisableDownloadRestriction = DisableDownloadRestriction
        };
    }

    [DataContract]
    internal sealed class GeneratedFile
    {
        [DataMember] public string OriginalPath { get; set; }
        [DataMember] public string OriginalHash { get; set; }
        [DataMember] public string GeneratedHash { get; set; }
        [DataMember] public bool DisableAutomaticUpdates { get; set; }
        [DataMember] public bool DisableDownloadRestriction { get; set; }
        [DataMember] public int PatchRevision { get; set; }
    }

    internal static class JsonFile
    {
        public static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        public static void Write<T>(string path, T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                AtomicWrite(path, stream.ToArray());
            }
        }

        public static void AtomicWrite(string path, byte[] bytes)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    public static class Launcher
    {
        public static string OutputPath(string original) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(original)),
            Path.GetFileNameWithoutExtension(original) + ".tweaked.exe");

        public static string Prepare(string original, TweakOptions options)
        {
            original = Path.GetFullPath(original);
            if (!File.Exists(original)) throw new FileNotFoundException("请选择存在的启动器程序。", original);
            // With both switches off, launch the selected original exactly as supplied.
            if (!options.Any) return original;
            var source = PatchEngine.ReadOriginal(original);
            var output = OutputPath(original);
            using (var gate = new Mutex(false, @"Local\PCL2TweakLoader-" + PatchEngine.Hash(System.Text.Encoding.UTF8.GetBytes(output.ToUpperInvariant()))))
            {
                if (!gate.WaitOne(0)) throw new IOException("另一个加载器正在准备此启动器，请稍后重试。");
                try
                {
                    var manifest = output + ".tweaks.json";
                    if (File.Exists(output))
                    {
                        if (!File.Exists(manifest)) throw new IOException("输出文件已存在，且并非由本加载器创建：" + output);
                        var existing = JsonFile.Read<GeneratedFile>(manifest);
                        var actual = PatchEngine.Hash(File.ReadAllBytes(output));
                        if (!string.Equals(existing.OriginalPath, original, StringComparison.OrdinalIgnoreCase) ||
                            existing.OriginalHash != PatchEngine.SupportedHash || existing.GeneratedHash != actual)
                            throw new IOException("生成的启动器已被其他程序修改。请先移走该文件及对应的 .tweaks.json 文件，再重试。");
                        if (existing.DisableAutomaticUpdates == options.DisableAutomaticUpdates &&
                            existing.DisableDownloadRestriction == options.DisableDownloadRestriction &&
                            existing.PatchRevision == PatchEngine.PatchRevision)
                            return output;
                    }

                    EnsureNotRunning(output);
                    var patched = PatchEngine.Patch(source, options);
                    // Replace only our generated file. Source bytes are never written back.
                    JsonFile.AtomicWrite(output, patched);
                    JsonFile.Write(manifest, new GeneratedFile
                    {
                        OriginalPath = original, OriginalHash = PatchEngine.SupportedHash,
                        GeneratedHash = PatchEngine.Hash(patched),
                        DisableAutomaticUpdates = options.DisableAutomaticUpdates,
                        DisableDownloadRestriction = options.DisableDownloadRestriction,
                        PatchRevision = PatchEngine.PatchRevision
                    });
                    // Keep any app configuration next to the generated sibling executable.
                    if (File.Exists(original + ".config")) File.Copy(original + ".config", output + ".config", true);
                    else if (File.Exists(output + ".config")) File.Delete(output + ".config");
                    return output;
                }
                finally { gate.ReleaseMutex(); }
            }
        }

        public static Process Start(string executable, string arguments = "")
        {
            EnsureNotRunning(executable);
            return Process.Start(new ProcessStartInfo(executable, arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable)),
                UseShellExecute = true
            });
        }

        private static void EnsureNotRunning(string path)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
            {
                using (process)
                {
                    try
                    {
                        if (string.Equals(process.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("请先关闭正在运行的启动器，再重新启动或更改微调选项。");
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        throw new IOException("同名启动器正在运行，请先关闭后再更改微调选项。");
                    }
                    catch (InvalidOperationException) { /* Process exited while being inspected. */ }
                }
            }
        }
    }
}
