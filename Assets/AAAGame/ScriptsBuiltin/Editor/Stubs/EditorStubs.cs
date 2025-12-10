// Minimal editor-only stubs to keep custom tools compiling without HybridCLR/Obfuz.
// These provide just enough API surface to satisfy references; they perform no actions.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HybridCLR.Editor.Commands
{
    public static class Compile
    {
        public static void CompileTargetDll(bool generateAotDll = false, BuildTarget target = BuildTarget.NoTarget) { }
    }

    public static class CompileDllCommand
    {
        public static void CompileDll(BuildTarget target) { }
    }

    public static class StripAOTDllCommand
    {
        public static void Run(BuildTarget target) { }
        public static void GenerateStripedAOTDlls(BuildTarget target) { }
        public static void GenerateStripedAOTDlls() { }
    }

    public static class Il2CppDefGeneratorCommand
    {
        public static void Run() { }
        public static void GenerateIl2CppDef(BuildTarget target) { }
        public static void GenerateIl2CppDef() { }
    }

    public static class LinkGeneratorCommand
    {
        public static void Run() { }
        public static void GenerateLinkXml(BuildTarget target) { }
    }

    public static class AOTReferenceGeneratorCommand
    {
        public static void Run() { }
        public static void GenerateAOTGenericReference(BuildTarget target) { }
    }

    public static class MethodBridgeGeneratorCommand
    {
        public static void Run() { }
        public static void GenerateMethodBridgeAndReversePInvokeWrapper(BuildTarget target) { }
    }
}

namespace HybridCLR.Editor
{
    public static class SettingsUtil
    {
        // Used to iterate hot update assemblies; return empty list to disable.
        public static IReadOnlyList<string> HotUpdateAssemblyNamesIncludePreserved => Array.Empty<string>();

        // Paths used by extension tool; return existing safe defaults.
        public static string GetHotUpdateDllsOutputDirByTarget(BuildTarget target) => Application.dataPath;
        public static string GetAssembliesPostIl2CppStripDir(BuildTarget target) => Application.dataPath;
        public static string LocalIl2CppDir => string.Empty;
        public static IReadOnlyList<string> AOTAssemblyNames => Array.Empty<string>();
    }

    namespace Settings
    {
        public class HybridCLRSettings
        {
            public static HybridCLRSettings Instance { get; } = new HybridCLRSettings();
            public bool enable;
            public string[] patchAOTAssemblies { get; set; } = Array.Empty<string>();
            public static void Save() { }
        }
    }
}

namespace HybridCLR.Editor.Installer
{
    public class InstallerController
    {
        public bool HasInstalledHybridCLR() => false;
    }
}

namespace HybridCLR.Editor.AOT
{
    public static class AOTAssemblyMetadataStripper
    {
        public static byte[] Strip(byte[] dllBytes) => dllBytes;
    }
}

namespace Obfuz.Settings
{
    public class ObfuzSettings
    {
        public static ObfuzSettings Instance { get; } = new ObfuzSettings();
        public BuildPipelineSettings buildPipelineSettings { get; } = new BuildPipelineSettings();
        public AssemblySettings assemblySettings { get; } = new AssemblySettings();
        public static void Save() { }
    }

    public class BuildPipelineSettings
    {
        public bool enable;
    }

    public class AssemblySettings
    {
        public IReadOnlyList<string> GetObfuscationRelativeAssemblyNames() => Array.Empty<string>();
    }
}

namespace Obfuz.Unity
{
    public static class LinkXmlProcess
    {
        public static void GenerateAdditionalLinkXmlFile(BuildTarget target) { }
    }
}

namespace Obfuz4HybridCLR
{
    public static class PrebuildCommandExt
    {
        public static string GetObfuscatedHotUpdateAssemblyOutputPath(BuildTarget target) => Application.dataPath;
    }
}

namespace Obfuz
{
    public static class ObfuscateUtil
    {
        public static void ObfuscateHotUpdateAssemblies(BuildTarget target, string outputDir) { }
    }

    public static class ObfuzMenu
    {
        public static void GenerateEncryptionVM() { }
        public static void SaveSecretFile() { }
    }
}

// Some code references ObfuzMenu without namespace; provide a global alias.
public static class ObfuzMenu
{
    public static void GenerateEncryptionVM() { }
    public static void SaveSecretFile() { }
}

namespace SevenZip
{
    // Placeholder namespace to satisfy using SevenZip
}
#endif
