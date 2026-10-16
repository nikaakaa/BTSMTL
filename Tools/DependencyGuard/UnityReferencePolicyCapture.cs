#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Btsmtl.DependencyGuard
{
    public static class UnityReferencePolicyCapture
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static Dictionary<string, object?> Capture(string batchId, Assembly editorCore, object compilationContext)
        {
            Type api = editorCore.GetType("UnityEditor.Scripting.ScriptCompilation.EditorCompilationInterface", true)!;
            object editor = api.GetField("editorCompilation", Static)!.GetValue(null)!;
            object state = Field(editor, "_currentBeeScriptCompilationState")!;
            if (!ReferenceEquals(Property(state, "ActiveBuild"), compilationContext))
                throw new InvalidOperationException("政策原件必须来自本次 compilationStarted 的 ActiveBuild。");
            object settings = Property(state, "Settings")!;
            object provider = Property(editor, "PrecompiledAssemblyProvider")!;
            Type packages = editorCore.GetType("UnityEditor.Scripting.ScriptCompilation.AutoReferencedPackageAssemblies", true)!;
            Type tests = editorCore.GetType("UnityEditor.Scripting.ScriptCompilation.TestRunnerHelpers", true)!;
            Type codegen = editorCore.GetType("UnityEditor.Scripting.ScriptCompilation.UnityCodeGenHelpers", true)!;
            Type players = editorCore.GetType("UnityEditor.PlayerSettings", true)!;
            Type packageInfo = editorCore.GetType("UnityEditor.PackageManager.PackageInfo", true)!;
            return new Dictionary<string, object?>
            {
                ["Version"] = 2,
                ["BatchId"] = batchId,
                ["CapturePhase"] = "CompilationStarted",
                ["ProducerPath"] = editorCore.Location,
                ["ProducerModuleVersionId"] = editorCore.ManifestModule.ModuleVersionId.ToString("D"),
                ["ProviderType"] = provider.GetType().FullName,
                ["Settings"] = new Dictionary<string, object?>
                {
                    ["BuildingForEditor"] = Property(settings, "BuildingForEditor"),
                    ["BuildTarget"] = Convert.ToInt32(Property(settings, "BuildTarget")),
                    ["BuildTargetGroup"] = Convert.ToInt32(Property(settings, "BuildTargetGroup")),
                    ["CompilationOptions"] = Convert.ToInt32(Property(settings, "CompilationOptions")),
                    ["ProjectDirectory"] = Property(settings, "ProjectDirectory"),
                    ["ExtraGeneralDefines"] = Strings(Property(settings, "ExtraGeneralDefines"))
                },
                ["SelectedPackages"] = Items(packageInfo.GetMethod("GetAllRegisteredPackages", Static, null, Type.EmptyTypes, null)!.Invoke(null, null)!)
                    .Select(Package).ToArray(),
                ["ScriptAssemblies"] = Items(Property(state, "ScriptAssemblies")!).Select(Script).ToArray(),
                ["TargetAssemblies"] = ((IDictionary)Field(editor, "customTargetAssemblies")!).Values.Cast<object>().Select(Target).ToArray(),
                ["EditorPrecompiledAssemblies"] = PrecompiledDictionary(Field(provider, "m_EditorPrecompiledAssemblies")),
                ["UnityAssemblies"] = PrecompiledArray(Field(editor, "unityAssemblies")),
                ["AutoReferencedPackages"] = new Dictionary<string, object?>
                {
                    ["RuntimeAssemblyNames"] = Strings(packages.GetField("runtimeAssemblyNames", Static)!.GetValue(null)),
                    ["EditorAssemblyNames"] = Strings(packages.GetField("editorAssemblyNames", Static)!.GetValue(null)),
                    ["IgnoreAssemblies"] = Strings(packages.GetField("ignoreAssemblies", Static)!.GetValue(null))
                },
                ["TestRunner"] = new Dictionary<string, object?>
                {
                    ["NunitAssemblyName"] = tests.GetField("NunitAssemblyName", Static)!.GetRawConstantValue(),
                    ["EditorTestRunnerAssemblyName"] = tests.GetField("k_EditorTestRunnerAssemblyName", Static)!.GetRawConstantValue(),
                    ["EngineTestRunnerAssemblyName"] = tests.GetField("k_EngineTestRunnerAssemblyName", Static)!.GetRawConstantValue(),
                    ["PlayModeTestRunnerEnabled"] = players.GetProperty("playModeTestRunnerEnabled", Static)!.GetValue(null)
                },
                ["CodeGen"] = new Dictionary<string, object?>
                {
                    ["Prefix"] = codegen.GetField("k_CodeGenPrefix", Static)!.GetRawConstantValue(),
                    ["CompilerSuffix"] = codegen.GetField("k_CompilerSuffix", Static)!.GetRawConstantValue(),
                    ["CodeGenSuffix"] = codegen.GetField("k_CodeGenSuffix", Static)!.GetRawConstantValue()
                }
            };
        }

        static Dictionary<string, object?> Package(object package) => new Dictionary<string, object?>
        {
            ["Name"] = Property(package, "name"),
            ["Version"] = Property(package, "version"),
            ["PackageId"] = Property(package, "packageId"),
            ["Source"] = Property(package, "source")!.ToString(),
            ["AssetPath"] = Property(package, "assetPath"),
            ["ResolvedPath"] = Property(package, "resolvedPath")
        };

        static Dictionary<string, object?> Script(object script)
        {
            object options = Property(script, "CompilerOptions")!;
            return new Dictionary<string, object?>
            {
                ["Filename"] = Property(script, "Filename"),
                ["AsmDefPath"] = Property(script, "AsmDefPath"),
                ["Flags"] = Convert.ToInt32(Property(script, "Flags")),
                ["References"] = Strings(Property(script, "References")),
                ["ScriptAssemblyReferences"] = Items(Property(script, "ScriptAssemblyReferences")!).Select(item => (string)Property(item, "Filename")!).ToArray(),
                ["ResponseFiles"] = Strings(Property(options, "ResponseFiles")),
                ["AdditionalCompilerArguments"] = Strings(Property(options, "AdditionalCompilerArguments"))
            };
        }

        static Dictionary<string, object?> Target(object target) => new Dictionary<string, object?>
        {
            ["Filename"] = Property(target, "Filename"),
            ["AsmDefPath"] = Property(target, "AsmDefPath"),
            ["Flags"] = Convert.ToInt32(Property(target, "Flags")),
            ["ExplicitPrecompiledReferences"] = Strings(Property(target, "ExplicitPrecompiledReferences")),
            ["References"] = Items(Property(target, "References")!).Select(item => (string)Property(item, "Filename")!).ToArray()
        };

        static Dictionary<string, object?> Precompiled(object assembly, string? key) => new Dictionary<string, object?>
        {
            ["Key"] = key,
            ["Path"] = Field(assembly, "Path"),
            ["Flags"] = Convert.ToInt32(Field(assembly, "Flags")),
            ["Redirected"] = Field(assembly, "Redirected")
        };

        static object[]? PrecompiledDictionary(object? dictionary)
        {
            if (dictionary is null) return null;
            var rows = new List<object>();
            IDictionaryEnumerator entries = ((IDictionary)dictionary).GetEnumerator();
            while (entries.MoveNext()) rows.Add(Precompiled(entries.Value!, (string)entries.Key));
            return rows.ToArray();
        }
        static object[]? PrecompiledArray(object? assemblies) => assemblies is null ? null :
            Items(assemblies).Select(item => (object)Precompiled(item, null)).ToArray();
        static IEnumerable<object> Items(object values) => ((IEnumerable)values).Cast<object>();
        static string[]? Strings(object? values) => values is null ? null : ((IEnumerable)values).Cast<string>().ToArray();
        static object? Field(object value, string name) => value.GetType().GetField(name, Instance)!.GetValue(value);
        static object? Property(object value, string name) => value.GetType().GetProperty(name, Instance)!.GetValue(value);
    }
}
