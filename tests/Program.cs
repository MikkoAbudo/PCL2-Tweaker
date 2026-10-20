using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PclTweaks;

internal static class Program
{
    private static int assertions;
    private static readonly Dictionary<string, byte[]> Dependencies = new Dictionary<string, byte[]>();
    private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>();

    [STAThread]
    private static int Main()
    {
        try
        {
            var originalPath = Path.GetFullPath("Plain Craft Launcher 2.exe");
            var original = File.ReadAllBytes(originalPath);
            var artifacts = Path.GetFullPath("tests/artifacts");
            Directory.CreateDirectory(artifacts);
            ReadDependencies(original);
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePacked;
            using (var source = ModuleDefinition.ReadModule(new MemoryStream(original)))
            {
                foreach (var automaticUpdates in new[] { false, true })
                foreach (var downloads in new[] { false, true })
                {
                    var options = new TweakOptions { DisableAutomaticUpdates = automaticUpdates, DisableDownloadRestriction = downloads };
                    var patched = PatchEngine.Patch(original, options);
                    var name = "updates-" + automaticUpdates + "_downloads-" + downloads;
                    File.WriteAllBytes(Path.Combine(artifacts, name + ".exe"), patched);
                    if (!options.Any) Assert(patched.SequenceEqual(original), "all-off bytes match original");
                    using (var target = ModuleDefinition.ReadModule(new MemoryStream(patched)))
                    {
                        Assert(target.EntryPoint.FullName == source.EntryPoint.FullName, "entry point preserved");
                        Assert(target.Resources.Count == source.Resources.Count, "resource count preserved");
                        foreach (var resource in source.Resources.OfType<EmbeddedResource>())
                            Assert(resource.GetResourceData().SequenceEqual(target.Resources.OfType<EmbeddedResource>()
                                .Single(r => r.Name == resource.Name).GetResourceData()), "embedded resources preserved");

                        var flag = target.GetType("PCL.ModSecret").Fields.Single(f => f.Name == "RestrictMcDownload");
                        Assert(Equals(flag.Constant, !downloads), "restriction constant matches toggle");
                        // Manual update code must remain exactly equivalent with either toggle.
                        foreach (var method in new[] { "UpdateStart", "UpdateRestart", "UpdateCheckByButton", "get_HasUpdatePermission" })
                            Assert(Il(source.GetType("PCL.ModSecret").Methods.Single(m => m.Name == method)) ==
                                   Il(target.GetType("PCL.ModSecret").Methods.Single(m => m.Name == method)), "manual update method preserved: " + method);

                        var server = target.GetType("PCL.ModSecret").Methods.Single(m => m.Name == "ServerSub");
                        var originalServer = source.GetType("PCL.ModSecret").Methods.Single(m => m.Name == "ServerSub");
                        if (automaticUpdates)
                        {
                            Assert(Il(server) != Il(originalServer), "automatic update gate changed");
                            var gate = server.Body.Instructions.Single(i => Equals(i.Operand, "SystemSystemUpdate"));
                            var gateIndex = server.Body.Instructions.IndexOf(gate);
                            Assert(server.Body.Instructions.Skip(gateIndex - 6).Take(6).Any(i => i.OpCode == OpCodes.Brtrue &&
                                i.Operand is Instruction end && end.OpCode == OpCodes.Ldloc_S), "update announcements skipped");
                            var actionKeys = server.Body.Instructions.Where(i => Equals(i.Operand, "update") || Equals(i.Operand, "slientupdate")).Take(2).ToArray();
                            Assert(actionKeys.Length == 2, "both server update action names recognized");
                            foreach (var key in actionKeys)
                            {
                                var branch = server.Body.Instructions.Skip(server.Body.Instructions.IndexOf(key)).First(i => i.OpCode.FlowControl == FlowControl.Cond_Branch);
                                Assert(((Instruction)branch.Operand).OpCode == OpCodes.Leave, "server update action exits without updating");
                            }
                        }
                        else Assert(Il(server) == Il(originalServer), "automatic updates untouched when off");

                        var loader = target.GetType("PCL.PageDownloadInstall").Methods.Single(m => m.Name == "LoaderInit");
                        var modpack = target.GetType("PCL.ModModpack");
                        var dispatch = modpack.Methods.Single(m => m.Name == "ModpackInstall" && m.Parameters.Count == 3);
                        if (downloads)
                        {
                            Assert(loader.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "DlClientListLoader"), "real Minecraft loader restored");
                            Assert(!loader.Body.Instructions.Any(i => Equals(i.Operand, "关闭下载")), "disabled loader removed");
                            var cases = (Instruction[])dispatch.Body.Instructions.Single(i => i.OpCode == OpCodes.Switch).Operand;
                            foreach (var pair in new Dictionary<int, string> {
                                [0] = "InstallPackCurseForge", [1] = "InstallPackHMCL", [2] = "InstallPackMMC",
                                [3] = "InstallPackMCBBS", [4] = "InstallPackModrinth", [9] = "InstallPackLauncherPack", [5] = "InstallPackCompress" })
                            {
                                var call = dispatch.Body.Instructions.Skip(dispatch.Body.Instructions.IndexOf(cases[pair.Key])).First(i => i.OpCode == OpCodes.Call);
                                Assert(((MethodReference)call.Operand).Name == pair.Value, "modpack route " + pair.Key);
                            }
                            Assert(dispatch.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally && h.HandlerEnd?.OpCode == OpCodes.Ldloc), "archive disposal retained around restored routes");
                            var picker = modpack.Methods.Single(m => m.Name == "ModpackInstall" && m.Parameters.Count == 0);
                            Assert(picker.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ShowDialog"), "modpack picker restored");
                            Assert(picker.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RunInThread"), "modpack installation stays on worker thread");
                        }
                        else
                        {
                            Assert(Il(loader) == Il(source.GetType("PCL.PageDownloadInstall").Methods.Single(m => m.Name == "LoaderInit")), "download loader unchanged when off");
                            Assert(Il(dispatch) == Il(source.GetType("PCL.ModModpack").Methods.Single(m => m.Name == "ModpackInstall" && m.Parameters.Count == 3)), "modpack restriction unchanged when off");
                        }
                    }
                    // Ask the .NET Framework JIT to compile every modified method, without running PCL startup.
                    var runtime = Assembly.Load(patched);
                    Compile(runtime, "PCL.ModSecret", "ServerSub");
                    Compile(runtime, "PCL.PageDownloadInstall", "LoaderInit");
                    Compile(runtime, "PCL.ModModpack", "ModpackInstall");
                    Compile(runtime, "PCL.FormMain+_Closure$__25-0", "_Lambda$__0");
                    Console.WriteLine("PASS " + name + " (metadata, resources, routes, Framework JIT)");
                }
            }

            var invalid = (byte[])original.Clone();
            invalid[1024] ^= 1;
            Throws<NotSupportedException>(() => PatchEngine.Patch(invalid, new TweakOptions { DisableAutomaticUpdates = true }), "unknown binary rejected");

            var sandbox = Path.Combine(artifacts, "file-safety-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            var inputPath = Path.Combine(sandbox, "Plain Craft Launcher 2.exe");
            File.WriteAllBytes(inputPath, original);
            Assert(Launcher.Prepare(inputPath, new TweakOptions()) == inputPath, "all-off uses original path");
            var both = new TweakOptions { DisableAutomaticUpdates = true, DisableDownloadRestriction = true };
            var outputPath = Launcher.Prepare(inputPath, both);
            var timestamp = File.GetLastWriteTimeUtc(outputPath);
            Assert(Launcher.Prepare(inputPath, both) == outputPath && timestamp == File.GetLastWriteTimeUtc(outputPath), "matching generated copy reused");
            Launcher.Prepare(inputPath, new TweakOptions { DisableAutomaticUpdates = true });
            using (var generated = ModuleDefinition.ReadModule(outputPath))
                Assert(Equals(generated.GetType("PCL.ModSecret").Fields.Single(f => f.Name == "RestrictMcDownload").Constant, true), "turning a tweak off rebuilds from original");
            File.AppendAllText(outputPath, "external change");
            Throws<IOException>(() => Launcher.Prepare(inputPath, both), "externally changed output protected");
            Assert(PatchEngine.Hash(File.ReadAllBytes(inputPath)) == PatchEngine.SupportedHash, "sandbox original unchanged");
            Assert(PatchEngine.Hash(File.ReadAllBytes(originalPath)) == PatchEngine.SupportedHash, "user original unchanged");
            Console.WriteLine("PASS file safety, cache reuse, toggle reversal, unknown-build rejection");
            VerifyIl(original, artifacts);
            Console.WriteLine(assertions + " assertions passed.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static string Il(MethodDefinition method) => string.Join("\n", method.Body.Instructions.Select(i => i.ToString()));

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("FAILED: " + message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Assert(true, message); return; }
        throw new Exception("FAILED: " + message);
    }

    private static void Compile(Assembly assembly, string type, string method)
    {
        foreach (var member in assembly.GetType(type, true).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == method))
        {
            RuntimeHelpers.PrepareMethod(member.MethodHandle);
            Assert(true, "JIT compiled " + type + "." + method);
        }
    }

    private static void ReadDependencies(byte[] original)
    {
        using (var module = ModuleDefinition.ReadModule(new MemoryStream(original)))
        using (var reader = new ResourceReader(module.Resources.OfType<EmbeddedResource>().Single(r => r.Name == "PCL.Resources.resources").GetResourceStream()))
        {
            var values = reader.GetEnumerator();
            while (values.MoveNext())
                if (values.Value is byte[] bytes && bytes.Length > 2 && bytes[0] == 0x4d && bytes[1] == 0x5a)
                    Dependencies.Add((string)values.Key, bytes);
        }
    }

    private static Assembly ResolvePacked(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (Loaded.TryGetValue(name, out var assembly)) return assembly;
        if (!Dependencies.TryGetValue(name, out var bytes)) return null;
        assembly = Assembly.Load(bytes);
        Loaded.Add(name, assembly);
        return assembly;
    }

    private static void VerifyIl(byte[] original, string artifacts)
    {
        var verifier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\PEVerify.exe");
        if (!File.Exists(verifier))
        {
            Console.WriteLine("SKIP PEVerify (Windows SDK tool is not installed); Framework JIT checks passed.");
            return;
        }
        var folder = Path.Combine(artifacts, "verification");
        Directory.CreateDirectory(folder);
        foreach (var pair in Dependencies) File.WriteAllBytes(Path.Combine(folder, pair.Key + ".dll"), pair.Value);
        var before = Path.Combine(folder, "Original.exe");
        var after = Path.Combine(folder, "Tweaked.exe");
        File.WriteAllBytes(before, original);
        File.Copy(Path.Combine(artifacts, "updates-True_downloads-True.exe"), after, true);
        Func<string, string[]> verify = path =>
        {
            using (var process = Process.Start(new ProcessStartInfo(verifier, "\"" + path + "\" /nologo /hresult /md /il")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            }))
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                File.WriteAllText(path + ".verification.txt", output);
                return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Contains("Error:")).Select(line => line.Replace(path, "LAUNCHER")).OrderBy(line => line).ToArray();
            }
        };
        var baseline = verify(before);
        var patched = verify(after);
        Assert(baseline.SequenceEqual(patched), "PEVerify introduces no errors beyond the unmodified launcher's baseline");
        Console.WriteLine("PASS PEVerify: no added errors (" + baseline.Length + " pre-existing upstream error(s))");
    }
}
