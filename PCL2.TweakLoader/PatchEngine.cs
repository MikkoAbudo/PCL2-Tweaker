using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Resources;
using System.Windows.Forms;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PclTweaks
{
    public sealed class TweakOptions
    {
        public bool DisableAutomaticUpdates { get; set; }
        public bool DisableDownloadRestriction { get; set; }
        public bool Any => DisableAutomaticUpdates || DisableDownloadRestriction;
    }

    public static class PatchEngine
    {
        // Deliberately pinned: a version number alone cannot identify compiler-folded code.
        public const string SupportedHash = "6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386";
        public const string SupportedVersion = "2.99.0.0";
        public const int PatchRevision = 1;
        private const string RestrictionMessage = "为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……";

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static byte[] ReadOriginal(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (Hash(bytes) != SupportedHash)
                throw new NotSupportedException("不支持此启动器构建。请选择与说明文档中的 SHA-256 匹配的原始 Plain Craft Launcher 2.exe（2.99.0.0）。未修改任何文件。");
            return bytes;
        }

        public static byte[] Patch(byte[] original, TweakOptions options)
        {
            if (Hash(original) != SupportedHash)
                throw new NotSupportedException("启动器文件哈希不匹配，已拒绝修改未知构建。");
            if (!options.Any) return (byte[])original.Clone();

            using (var resolver = new PackedAssemblyResolver())
            using (var input = new MemoryStream(original, false))
            using (var module = ModuleDefinition.ReadModule(input, new ReaderParameters { InMemory = true, AssemblyResolver = resolver }))
            {
                resolver.Attach(module);
                if (options.DisableAutomaticUpdates) DisableAutomaticUpdates(module);
                if (options.DisableDownloadRestriction) RestoreDownloads(module);
                using (var output = new MemoryStream())
                {
                    module.Write(output);
                    return output.ToArray();
                }
            }
        }

        private static TypeDefinition Type(ModuleDefinition module, string name) => module.GetType(name)
            ?? throw new InvalidDataException("未找到预期的启动器类型：" + name);

        private static MethodDefinition Method(TypeDefinition type, string name, int parameters) =>
            type.Methods.Single(m => m.Name == name && m.Parameters.Count == parameters);

        private static Instruction At(MethodDefinition method, int offset) =>
            method.Body.Instructions.Single(i => i.Offset == offset);

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException("启动器代码与补丁预期不符：" + message);
        }

        private static void DisableAutomaticUpdates(ModuleDefinition module)
        {
            var server = Method(Type(module, "PCL.ModSecret"), "ServerSub", 1);
            var announcement = At(server, 0x054f);
            var nextAnnouncement = At(server, 0x09c6);
            Expect(announcement.OpCode == OpCodes.Ldloc_S &&
                   ((VariableDefinition)announcement.Operand).Index == 19 &&
                   (string)At(server, 0x055a).Operand == "SystemSystemUpdate", "update announcement gate");
            var il = server.Body.GetILProcessor();
            // Skip update announcements, including their prompts; keep unrelated announcements.
            il.InsertBefore(announcement, Instruction.Create(OpCodes.Ldloc, server.Body.Variables[19]));
            il.InsertBefore(announcement, Instruction.Create(OpCodes.Brtrue, nextAnnouncement));

            // Both server actions, "update" and "slientupdate" (upstream spelling), land here.
            // LEAVE exits the existing action try/catch without entering any update code.
            var action = At(server, 0x08fc);
            Expect(action.OpCode == OpCodes.Ldloc_S &&
                   At(server, 0x093d).Operand is MethodReference update && update.Name == "UpdateStart",
                   "automatic update action");
            action.OpCode = OpCodes.Leave;
            action.Operand = At(server, 0x09a2);
            ClearRange(server, 0x08fe, 0x0944);
            ExpandBranches(server);
        }

        private static void RestoreDownloads(ModuleDefinition module)
        {
            var secret = Type(module, "PCL.ModSecret");
            var flag = secret.Fields.Single(f => f.Name == "RestrictMcDownload");
            Expect(flag.IsLiteral && Equals(flag.Constant, true), "RestrictMcDownload constant");
            flag.Constant = false;

            var loader = Method(Type(module, "PCL.PageDownloadInstall"), "LoaderInit", 0);
            var start = At(loader, 0x001a);
            Expect((string)start.Operand == "关闭下载", "disabled Minecraft loader");
            var realLoader = Type(module, "PCL.ModDownload").Fields.Single(f => f.Name == "DlClientListLoader");
            // Restore the normal loader argument; the compiler eliminated the original Else.
            foreach (var instruction in loader.Body.Instructions.Where(i => i.Offset >= 0x001a && i.Offset < 0x004a))
            {
                instruction.OpCode = OpCodes.Nop;
                instruction.Operand = null;
            }
            start.OpCode = OpCodes.Ldsfld;
            start.Operand = realLoader;
            ExpandBranches(loader);

            var modpack = Type(module, "PCL.ModModpack");
            RestoreModpackDispatch(modpack);
            RestoreModpackPicker(module, modpack);

            // Remove the compiler-folded restriction-specific catch in file drag-and-drop.
            var drop = Type(module, "PCL.FormMain").NestedTypes.Single(t => t.Name == "_Closure$__25-0")
                .Methods.Single(m => m.Name == "_Lambda$__0");
            Expect((string)At(drop, 0x0295).Operand == RestrictionMessage, "drag-and-drop restriction catch");
            At(drop, 0x028e).OpCode = OpCodes.Br;
            At(drop, 0x028e).Operand = At(drop, 0x02ba);
            ClearRange(drop, 0x0290, 0x02ba);
            ExpandBranches(drop);
        }

        private static void RestoreModpackDispatch(TypeDefinition modpack)
        {
            var method = Method(modpack, "ModpackInstall", 3);
            var restriction = At(method, 0x02da);
            Expect((string)restriction.Operand == RestrictionMessage && method.Body.Variables.Count == 10,
                "modpack dispatcher");
            var body = method.Body;
            var il = body.GetILProcessor();
            var finallyStart = At(method, 0x02e5);
            var returnValue = new VariableDefinition(method.ReturnType);
            body.Variables.Add(returnValue);
            var returnLoad = Instruction.Create(OpCodes.Ldloc, returnValue);
            var outerFinally = body.ExceptionHandlers.Single(h => h.HandlerType == ExceptionHandlerType.Finally && h.HandlerStart == finallyStart);
            Expect(outerFinally.HandlerEnd == null, "modpack outer finally boundary");
            outerFinally.HandlerEnd = returnLoad;

            var dispatch = new List<Instruction>();
            // Existing locals: archive = 1, base folder = 2, detected pack type = 4.
            Func<string, Instruction> addCase = name =>
            {
                var target = modpack.Methods.Single(m => m.Name == name);
                var entry = Instruction.Create(OpCodes.Ldarg_0);
                dispatch.Add(entry);
                dispatch.Add(Instruction.Create(OpCodes.Ldloc, body.Variables[1]));
                if (target.Parameters.Count >= 3) dispatch.Add(Instruction.Create(OpCodes.Ldloc, body.Variables[2]));
                if (target.Parameters.Count >= 4) dispatch.Add(Instruction.Create(OpCodes.Ldarg_1));
                if (target.Parameters.Count >= 5) dispatch.Add(Instruction.Create(OpCodes.Ldarg_2));
                dispatch.Add(Instruction.Create(OpCodes.Call, target));
                dispatch.Add(Instruction.Create(OpCodes.Stloc, returnValue));
                dispatch.Add(Instruction.Create(OpCodes.Leave, returnLoad));
                return entry;
            };
            var fallback = addCase("InstallPackCompress");
            var targets = Enumerable.Repeat(fallback, 10).ToArray();
            targets[0] = addCase("InstallPackCurseForge");
            targets[1] = addCase("InstallPackHMCL");
            targets[2] = addCase("InstallPackMMC");
            targets[3] = addCase("InstallPackMCBBS");
            targets[4] = addCase("InstallPackModrinth");
            targets[9] = addCase("InstallPackLauncherPack");

            restriction.OpCode = OpCodes.Ldloc;
            restriction.Operand = body.Variables[4];
            At(method, 0x02df).OpCode = OpCodes.Switch;
            At(method, 0x02df).Operand = targets;
            At(method, 0x02e4).OpCode = OpCodes.Br;
            At(method, 0x02e4).Operand = fallback;
            foreach (var instruction in dispatch) il.InsertBefore(finallyStart, instruction);
            il.Append(returnLoad);
            il.Append(Instruction.Create(OpCodes.Ret));
            ExpandBranches(method);
        }

        private static void RestoreModpackPicker(ModuleDefinition module, TypeDefinition modpack)
        {
            var method = Method(modpack, "ModpackInstall", 0);
            Expect(method.Body.Instructions.Any(i => Equals(i.Operand, RestrictionMessage)), "modpack file picker");
            var closure = modpack.NestedTypes.Single(t => t.Name == "_Closure$__0-0");
            var file = closure.Fields.Single(f => f.Name == "$VB$Local_File");
            var work = closure.Methods.Single(m => m.Name == "_Lambda$__0");
            var run = Method(Type(module, "PCL.ModBase"), "RunInThread", 1);

            method.Body = new MethodBody(method) { InitLocals = true };
            var state = new VariableDefinition(closure);
            var dialog = new VariableDefinition(module.ImportReference(typeof(OpenFileDialog)));
            var selected = new VariableDefinition(module.TypeSystem.String);
            method.Body.Variables.Add(state);
            method.Body.Variables.Add(dialog);
            method.Body.Variables.Add(selected);
            var il = method.Body.GetILProcessor();
            var finish = Instruction.Create(OpCodes.Ret);
            var afterDispose = Instruction.Create(OpCodes.Ldloc, selected);
            var useFile = Instruction.Create(OpCodes.Ldloc, dialog);
            il.Emit(OpCodes.Newobj, closure.Methods.Single(m => m.IsConstructor && !m.IsStatic));
            il.Emit(OpCodes.Stloc, state);
            il.Emit(OpCodes.Newobj, module.ImportReference(typeof(OpenFileDialog).GetConstructor(System.Type.EmptyTypes)));
            il.Emit(OpCodes.Stloc, dialog);
            var tryStart = Instruction.Create(OpCodes.Ldloc, dialog);
            il.Append(tryStart);
            il.Emit(OpCodes.Ldstr, "选择整合包压缩文件");
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(FileDialog).GetProperty("Title").GetSetMethod()));
            il.Emit(OpCodes.Ldloc, dialog);
            il.Emit(OpCodes.Ldstr, "整合包 (*.zip;*.mrpack;*.rar)|*.zip;*.mrpack;*.rar|所有文件 (*.*)|*.*");
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(FileDialog).GetProperty("Filter").GetSetMethod()));
            il.Emit(OpCodes.Ldloc, dialog);
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(CommonDialog).GetMethod("ShowDialog", System.Type.EmptyTypes)));
            il.Emit(OpCodes.Ldc_I4, (int)DialogResult.OK);
            il.Emit(OpCodes.Beq, useFile);
            il.Emit(OpCodes.Leave, afterDispose);
            il.Append(useFile);
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(FileDialog).GetProperty("FileName").GetGetMethod()));
            il.Emit(OpCodes.Stloc, selected);
            il.Emit(OpCodes.Leave, afterDispose);
            var finallyStart = Instruction.Create(OpCodes.Ldloc, dialog);
            il.Append(finallyStart);
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(IDisposable).GetMethod("Dispose")));
            il.Emit(OpCodes.Endfinally);
            il.Append(afterDispose);
            il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("IsNullOrEmpty")));
            il.Emit(OpCodes.Brtrue, finish);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Ldloc, selected);
            il.Emit(OpCodes.Stfld, file);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Ldftn, work);
            il.Emit(OpCodes.Newobj, module.ImportReference(typeof(Action).GetConstructor(new[] { typeof(object), typeof(IntPtr) })));
            il.Emit(OpCodes.Call, run);
            il.Append(finish);
            method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = tryStart, TryEnd = finallyStart, HandlerStart = finallyStart, HandlerEnd = afterDispose
            });
        }

        private static void ExpandBranches(MethodDefinition method)
        {
            // Cecil writes the requested opcode as-is; growth must not overflow short branches.
            var longBranches = new Dictionary<Code, OpCode>
            {
                [Code.Br_S] = OpCodes.Br, [Code.Brfalse_S] = OpCodes.Brfalse, [Code.Brtrue_S] = OpCodes.Brtrue,
                [Code.Beq_S] = OpCodes.Beq, [Code.Bge_S] = OpCodes.Bge, [Code.Bge_Un_S] = OpCodes.Bge_Un,
                [Code.Bgt_S] = OpCodes.Bgt, [Code.Bgt_Un_S] = OpCodes.Bgt_Un, [Code.Ble_S] = OpCodes.Ble,
                [Code.Ble_Un_S] = OpCodes.Ble_Un, [Code.Blt_S] = OpCodes.Blt, [Code.Blt_Un_S] = OpCodes.Blt_Un,
                [Code.Bne_Un_S] = OpCodes.Bne_Un, [Code.Leave_S] = OpCodes.Leave
            };
            foreach (var instruction in method.Body.Instructions)
                if (longBranches.TryGetValue(instruction.OpCode.Code, out var opcode)) instruction.OpCode = opcode;
        }

        private static void ClearRange(MethodDefinition method, int start, int end)
        {
            // Preserve branch/exception-boundary instruction identities while clearing dead code.
            foreach (var instruction in method.Body.Instructions.Where(i => i.Offset >= start && i.Offset < end))
            {
                instruction.OpCode = OpCodes.Nop;
                instruction.Operand = null;
            }
        }

        private sealed class PackedAssemblyResolver : DefaultAssemblyResolver
        {
            private readonly Dictionary<string, byte[]> packed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            private readonly Dictionary<string, AssemblyDefinition> loaded = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
            private ModuleDefinition launcher;

            public void Attach(ModuleDefinition module)
            {
                launcher = module;
                var resource = module.Resources.OfType<EmbeddedResource>().Single(r => r.Name == "PCL.Resources.resources");
                using (var reader = new ResourceReader(resource.GetResourceStream()))
                {
                    var values = reader.GetEnumerator();
                    while (values.MoveNext())
                    {
                        var key = values.Key as string;
                        if (key != null && module.AssemblyReferences.Any(a => a.Name == key) && values.Value is byte[] bytes)
                            packed.Add(key, bytes);
                    }
                }
            }

            public override AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

            public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                if (name.Name == launcher.Assembly.Name.Name) return launcher.Assembly;
                if (loaded.TryGetValue(name.Name, out var assembly)) return assembly;
                if (packed.TryGetValue(name.Name, out var bytes))
                {
                    assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, false),
                        new ReaderParameters { InMemory = true, AssemblyResolver = this });
                    loaded.Add(name.Name, assembly);
                    RegisterAssembly(assembly);
                    return assembly;
                }
                return base.Resolve(name, parameters);
            }
        }
    }
}
