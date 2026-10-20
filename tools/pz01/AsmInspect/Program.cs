using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Просмотр состава результата сборки для ПЗ-1 (аналог dumpbin /headers /dependents и ildasm /manifest):
//  - PE-заголовок: архитектура, подсистема, отладочная запись CodeView (ссылка на .pdb);
//  - нативные импорты (какие DLL загрузит ОС при запуске);
//  - для управляемой сборки: версия, ссылки на сборки, P/Invoke-модули, число типов и методов,
//    DebuggableAttribute -- выключены ли оптимизации JIT (так различаются Development и Release).
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("usage: AsmInspect <file.dll|file.exe> [...]");
            return 2;
        }
        foreach (string path in args) Inspect(path);
        return 0;
    }

    static void Inspect(string path)
    {
        Console.WriteLine($"== {Path.GetFileName(path)}  ({new FileInfo(path).Length / 1024} КБ)");
        using FileStream fs = File.OpenRead(path);
        using PEReader pe = new PEReader(fs);
        PEHeaders h = pe.PEHeaders;
        Console.WriteLine($"   PE: {h.CoffHeader.Machine}, {(h.IsDll ? "DLL" : "EXE")}, подсистема {h.PEHeader?.Subsystem}");

        foreach (DebugDirectoryEntry e in pe.ReadDebugDirectory())
        {
            if (e.Type == DebugDirectoryEntryType.CodeView)
                Console.WriteLine($"   отладочная запись CodeView: {pe.ReadCodeViewDebugDirectoryData(e).Path}");
        }

        List<string> imports = NativeImports(pe);
        if (imports.Count > 0)
            Console.WriteLine($"   импорт DLL ({imports.Count}): {string.Join(", ", imports)}");

        if (!pe.HasMetadata)
        {
            Console.WriteLine("   метаданных .NET нет -- нативный модуль");
            return;
        }

        MetadataReader md = pe.GetMetadataReader();
        AssemblyDefinition asm = md.GetAssemblyDefinition();
        Console.WriteLine($"   сборка .NET: {md.GetString(asm.Name)} {asm.Version}, runtime {md.MetadataVersion}");
        Console.WriteLine($"   типов: {md.TypeDefinitions.Count}, методов: {md.MethodDefinitions.Count}");
        Console.WriteLine("   ссылки на сборки: " + string.Join(", ",
            md.AssemblyReferences.Select(r => md.GetAssemblyReference(r)).Select(r => $"{md.GetString(r.Name)} {r.Version}")));
        int moduleRefs = md.GetTableRowCount(TableIndex.ModuleRef);
        if (moduleRefs > 0)
            Console.WriteLine("   P/Invoke-модули: " + string.Join(", ", Enumerable.Range(1, moduleRefs)
                .Select(i => md.GetString(md.GetModuleReference(MetadataTokens.ModuleReferenceHandle(i)).Name))));
        Console.WriteLine("   DebuggableAttribute: " + Debuggable(md));
    }

    // Флаги System.Diagnostics.DebuggableAttribute: что компилятор разрешил JIT
    static string Debuggable(MetadataReader md)
    {
        foreach (CustomAttributeHandle ch in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute ca = md.GetCustomAttribute(ch);
            if (ca.Constructor.Kind != HandleKind.MemberReference) continue;
            MemberReference ctor = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
            if (ctor.Parent.Kind != HandleKind.TypeReference) continue;
            if (md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name) != "DebuggableAttribute") continue;

            BlobReader blob = md.GetBlobReader(ca.Value);
            blob.ReadUInt16();   // пролог 0x0001
            int modes;
            if (blob.RemainingBytes >= 6) modes = blob.ReadInt32();                 // ctor(DebuggingModes)
            else modes = (blob.ReadBoolean() ? 1 : 0) | (blob.ReadBoolean() ? 256 : 0); // ctor(bool, bool)

            List<string> flags = new List<string>();
            if ((modes & 1) != 0) flags.Add("Default");
            if ((modes & 2) != 0) flags.Add("IgnoreSymbolStoreSequencePoints");
            if ((modes & 4) != 0) flags.Add("EnableEditAndContinue");
            if ((modes & 256) != 0) flags.Add("DisableOptimizations");
            string verdict = (modes & 256) != 0 ? "оптимизации JIT ВЫКЛЮЧЕНЫ (отладочная сборка)" : "оптимизации JIT разрешены";
            return $"0x{modes:X} [{string.Join(", ", flags)}] -> {verdict}";
        }
        return "нет -> оптимизации JIT разрешены";
    }

    // Таблица импорта PE: имена DLL из IMAGE_IMPORT_DESCRIPTOR
    static List<string> NativeImports(PEReader pe)
    {
        List<string> result = new List<string>();
        DirectoryEntry dir = pe.PEHeaders.PEHeader?.ImportTableDirectory ?? default;
        if (dir.RelativeVirtualAddress == 0) return result;

        PEMemoryBlock block = pe.GetSectionData(dir.RelativeVirtualAddress);
        BlobReader r = block.GetReader();
        while (r.RemainingBytes >= 20)
        {
            r.ReadInt32(); r.ReadInt32(); r.ReadInt32();   // OriginalFirstThunk, TimeDateStamp, ForwarderChain
            int nameRva = r.ReadInt32();
            r.ReadInt32();                                 // FirstThunk
            if (nameRva == 0) break;
            BlobReader nr = pe.GetSectionData(nameRva).GetReader();
            List<byte> bytes = new List<byte>();
            byte b;
            while (nr.RemainingBytes > 0 && (b = nr.ReadByte()) != 0) bytes.Add(b);
            string name = System.Text.Encoding.ASCII.GetString(bytes.ToArray());
            if (name.Length > 0) result.Add(name);
        }
        return result;
    }
}
