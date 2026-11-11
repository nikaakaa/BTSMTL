using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BTSMTL.WebWorkbench;

sealed class PlayerCompilationInputs : IDisposable
{
    internal const string DataKey = "ScriptCompilationBuildProgram.Data.ScriptCompilationData";
    readonly string m_Project;
    readonly string m_Work;
    readonly Dictionary<string, string> m_PackagePaths;
    readonly Dictionary<string, string> m_Files = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> m_Directories = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string>[] m_OriginalSources;
    readonly JsonObject[] m_Csc;
    readonly JsonObject[] m_Ilpp;
    readonly CscResponses m_Responses;
    readonly string[][] m_CscArguments;
    readonly List<string>[] m_CscResponseArguments;
    readonly List<string>[] m_CscInputFiles;

    public PlayerCompilationInputs(string project, string work, JsonObject recipe,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sources)
    {
        m_Project = project;
        m_Work = work;
        Data = JsonNode.Parse(File.ReadAllBytes((string)recipe["compilerInputs"]!["path"]!))!.AsObject();
        JsonObject compilation = Data[DataKey]!.AsObject();
        m_PackagePaths = Data["BeeBuildProgramCommon.Data.ConfigurationData"]!["Packages"]!.AsArray()
            .ToDictionary(item => Path.GetFullPath("Packages/" + (string)item!["Name"]!, project).Replace('\\', '/') + "/",
                item => Path.GetFullPath((string)item!["ResolvedPath"]!, project).Replace('\\', '/') + "/",
                StringComparer.OrdinalIgnoreCase);
        JsonObject[] inputs = compilation["Assemblies"]!.AsArray().Select(item => item!.AsObject()).ToArray();
        m_OriginalSources = inputs.Select(input => input["SourceFiles"]!.AsArray()
            .Select(file => Canonical((string)file!)).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (JsonObject input in inputs)
            if (sources.TryGetValue((string)input["Name"]!, out IReadOnlyList<string>? current))
                input["SourceFiles"] = JsonSerializer.SerializeToNode(current);
        Assemblies = compilation["Assemblies"]!.Deserialize<UnityPlayerAssemblyInput[]>(WorkbenchArtifacts.JsonOptions)!;
        JsonObject dag = JsonNode.Parse(File.ReadAllBytes((string)recipe["compilerDag"]!["path"]!))!.AsObject();
        JsonObject[] nodes = dag["Nodes"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        Configuration = nodes.Single(node => ((string)node["Annotation"]!).StartsWith("ILPP-Configuration ", StringComparison.Ordinal));
        m_Ilpp = Assemblies.Select(assembly => nodes.Single(node =>
            (string?)node["DisplayName"] == "ILPostProcess " + assembly.Name)).ToArray();
        m_Csc = m_Ilpp.Select(ilpp => nodes.Single(node =>
            ((string)node["Annotation"]!).StartsWith("Csc ", StringComparison.Ordinal) &&
            node["Outputs"]!.AsArray().Any(output => Canonical((string)output!) == Canonical((string)ilpp["Inputs"]![0]!)))).ToArray();
        ImmutableAssemblies = recipe["assemblies"]!.AsArray().Where(assembly => !(bool)assembly!["replaceable"]!)
            .ToDictionary(assembly => (int)assembly!["compilerInputIndex"]!, assembly => new JsonObject
            {
                ["name"] = assembly!["name"]!.DeepClone(), ["reason"] = assembly["reason"]!.DeepClone(),
                ["compilerInputIndex"] = assembly["compilerInputIndex"]!.DeepClone(),
                ["csc"] = "sealed-raw-input", ["annotation"] = m_Csc[(int)assembly["compilerInputIndex"]!]["Annotation"]!.DeepClone()
            });

        foreach (JsonNode? item in recipe["compilationFiles"]!.AsArray())
        {
            string original = Canonical((string)item!["sourcePath"]!);
            string destination = Path.Combine(work, "inputs", DirectoryKey(Path.GetDirectoryName(original)!), Path.GetFileName(original));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy((string)item["path"]!, destination, true);
            m_Files[original] = destination;
        }
        foreach (KeyValuePair<int, JsonObject> assembly in ImmutableAssemblies)
            assembly.Value["inputs"] = new JsonArray(m_Csc[assembly.Key]["Outputs"]!.AsArray().Select(output =>
            {
                string original = (string)output!;
                string input = MapFile(original);
                JsonNode sealedFile = recipe["compilationFiles"]!.AsArray().Single(file =>
                    Canonical((string)file!["sourcePath"]!) == Canonical(original))!;
                return new JsonObject { ["sourcePath"] = original, ["sealedFile"] = sealedFile.DeepClone(), ["inputPath"] = input };
            }).ToArray<JsonNode>());
        foreach (JsonObject node in m_Csc.Where((_, index) => !ImmutableAssemblies.ContainsKey(index)).Concat(m_Ilpp))
        foreach (JsonNode? output in node["Outputs"]!.AsArray())
        {
            string original = Canonical((string)output!);
            string directory = Path.GetDirectoryName(original)!.Replace('\\', '/');
            string destination = Path.Combine(work, "outputs", DirectoryKey(directory));
            Directory.CreateDirectory(destination);
            m_Directories[directory] = destination;
            m_Files[original] = Path.Combine(destination, Path.GetFileName(original));
        }
        string roslyn = (string)recipe["compiler"]!["roslyn"]!["path"]!;
        m_Responses = new CscResponses(roslyn);
        m_CscResponseArguments = Assemblies.Select(_ => new List<string>()).ToArray();
        m_CscInputFiles = Assemblies.Select(_ => new List<string>()).ToArray();
        m_CscArguments = Enumerable.Range(0, Assemblies.Length).Select(index => RewriteCscArguments(index, roslyn, recipe)).ToArray();
        for (int index = 0; index < Assemblies.Length; index++)
            inputs[index]["Defines"] = JsonSerializer.SerializeToNode(
                m_Responses.Defines(m_CscArguments[index].Skip(2), m_Project, Path.GetDirectoryName(roslyn)!));
        Assemblies = compilation["Assemblies"]!.Deserialize<UnityPlayerAssemblyInput[]>(WorkbenchArtifacts.JsonOptions)!;
    }

    public void Dispose() => m_Responses.Dispose();

    public JsonObject Data { get; }
    public UnityPlayerAssemblyInput[] Assemblies { get; }
    public IReadOnlyDictionary<int, JsonObject> ImmutableAssemblies { get; }
    public IEnumerable<string> SourceFiles => Assemblies.Where((_, index) => !ImmutableAssemblies.ContainsKey(index))
        .SelectMany(assembly => assembly.SourceFiles)
        .Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase);
    public JsonObject Configuration { get; }
    public JsonObject Csc(int index) => m_Csc[index];
    public JsonObject Ilpp(int index) => m_Ilpp[index];
    public string Output(JsonObject node, string extension) => MapFile(node["Outputs"]!.AsArray()
        .Select(output => (string)output!).First(path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));

    public string[] CscArguments(int index) => m_CscArguments[index];
    public IEnumerable<string> CscKeyText(int index) => m_CscArguments[index].Concat(m_CscResponseArguments[index]).Select(Normalize);
    public IEnumerable<string> CscInputFiles(int index) =>
        Assemblies[index].SourceFiles.Select(Canonical).Concat(m_CscInputFiles[index]);
    public string[] Outputs(JsonObject node) => node["Outputs"]!.AsArray().Select(output => MapFile((string)output!)).ToArray();

    // 工作目录随请求变化，复用指纹只保留其余参数原文。
    string Normalize(string value) => value.Replace(m_Work, "<work>", StringComparison.OrdinalIgnoreCase);

    string[] RewriteCscArguments(int index, string roslyn, JsonObject recipe)
    {
        var arguments = Split((string)m_Csc[index]["Action"]!).Skip(1).Where(argument => argument != "/shared").ToList();
        arguments[1] = roslyn;
        var remaining = Assemblies[index].SourceFiles.Select(Canonical).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 2; i < arguments.Count; i++)
            if (arguments[i].StartsWith('@'))
                arguments[i] = "@" + RewriteCscResponse(arguments[i][1..], index, remaining);
            else if (!arguments[i].StartsWith('-') && !arguments[i].StartsWith('/') &&
                m_OriginalSources[index].Contains(Canonical(arguments[i])))
                if (remaining.Remove(Canonical(arguments[i])))
                    arguments[i] = Canonical(arguments[i]);
                else
                    arguments.RemoveAt(i--);
        arguments.AddRange(Assemblies[index].SourceFiles.Select(Canonical).Where(remaining.Remove));
        if (!ImmutableAssemblies.ContainsKey(index))
        {
            string original = Canonical((string)m_Ilpp[index]["Inputs"]![0]!);
            string snapshot = (string)recipe["compilationFiles"]!.AsArray().Single(file =>
                Canonical((string)file!["sourcePath"]!) == original)!["path"]!;
            using var reader = new PEReader(File.OpenRead(snapshot));
            DebugDirectoryEntry codeView = reader.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
            string pdbPath = reader.ReadCodeViewDebugDirectoryData(codeView).Path;
            arguments.Add("/pathmap:" + Path.GetDirectoryName(Output(m_Csc[index], ".pdb")) + "=" + Path.GetDirectoryName(pdbPath));
        }
        return arguments.ToArray();
    }

    string RewriteCscResponse(string original, int index, HashSet<string> remaining)
    {
        string snapshot = MapFile(original);
        var arguments = new List<string>();
        foreach (string argument in m_Responses.Read(snapshot))
        {
            if (argument.StartsWith('@'))
            {
                arguments.Add("@" + RewriteCscResponse(argument[1..], index, remaining));
                continue;
            }
            if (argument.StartsWith('-') || argument.StartsWith('/'))
            {
                int colon = argument.IndexOf(':');
                string option = colon < 0 ? argument[1..] : argument[1..colon];
                string value = colon < 0 ? "" : argument[(colon + 1)..];
                switch (option.ToLowerInvariant())
                {
                    case "r":
                    case "reference":
                        int alias = value.IndexOf('=');
                        string reference = MapFile(alias < 0 ? value : value[(alias + 1)..]);
                        m_CscInputFiles[index].Add(reference);
                        arguments.Add(argument[..(colon + 1)] + (alias < 0 ? reference : value[..(alias + 1)] + reference));
                        break;
                    case "out":
                    case "refout":
                    case "pdb":
                    case "doc":
                        arguments.Add(argument[..(colon + 1)] + MapFile(value));
                        break;
                    case "analyzer":
                    case "additionalfile":
                    case "analyzerconfig":
                    case "ruleset":
                    case "appconfig":
                    case "sourcelink":
                    case "win32res":
                    case "win32icon":
                    case "win32manifest":
                    case "keyfile":
                        string input = MapFile(value);
                        m_CscInputFiles[index].Add(input);
                        arguments.Add(argument[..(colon + 1)] + input);
                        break;
                    case "generatedfilesout":
                        string generated = Path.Combine(m_Work, "generated", Assemblies[index].Name);
                        Directory.CreateDirectory(generated);
                        arguments.Add(argument[..(colon + 1)] + generated);
                        break;
                    default:
                        arguments.Add(argument);
                        break;
                }
            }
            else if (!m_OriginalSources[index].Contains(Canonical(argument)))
                arguments.Add(argument);
            else if (remaining.Remove(Canonical(argument)))
                arguments.Add(Canonical(argument));
        }
        m_CscResponseArguments[index].AddRange(arguments);
        string result = Path.Combine(m_Work, "responses", Assemblies[index].Name + "-" + DirectoryKey(Canonical(original)) + "-" + Path.GetFileName(original));
        Directory.CreateDirectory(Path.GetDirectoryName(result)!);
        File.WriteAllText(result, string.Join("\n", arguments.Select(Quote)), new UTF8Encoding(false));
        return result;
    }

    public (string[] Text, List<string> Files) IlppKeyInputs(JsonObject node)
    {
        var files = new List<string>();
        return (IlppArgumentList(node, files).Skip(1).Select(Normalize).ToArray(), files);
    }

    string[] IlppArgumentList(JsonObject node, List<string> files)
    {
        string[] arguments = Split(File.ReadAllText(MapFile((string)node["FrontendResponseFiles"]![0]!), Encoding.UTF8));
        if (arguments[1] == "c")
        {
            for (int i = 2; i < arguments.Length; i++)
                if (arguments[i] != "-h")
                    files.Add(arguments[i] = MapFile(arguments[i]));
        }
        else
        {
            files.Add(arguments[2] = MapFile(arguments[2]));
            arguments[3] = m_Directories[Canonical(arguments[3])];
            int references = Array.IndexOf(arguments, "-r");
            for (int i = references < 0 ? arguments.Length : references + 1; i < arguments.Length; i++)
                files.Add(arguments[i] = MapFile(arguments[i]));
        }
        return arguments;
    }

    public string[] IlppArguments(JsonObject node, string pipeName)
    {
        string response = (string)node["FrontendResponseFiles"]![0]!;
        string[] arguments = IlppArgumentList(node, new List<string>());
        arguments[0] = pipeName;
        string rewritten = Path.Combine(m_Work, "responses", "ilpp-" + DirectoryKey(Canonical(response)) + ".rsp");
        Directory.CreateDirectory(Path.GetDirectoryName(rewritten)!);
        File.WriteAllText(rewritten, string.Join("\n", arguments.Select(Quote)), new UTF8Encoding(false));
        return new[] { "@" + rewritten };
    }

    public string MapFile(string path)
    {
        string canonical = Canonical(path);
        return m_Files.TryGetValue(canonical, out string? mapped) ? mapped
            : throw new FileNotFoundException($"原 Player 配方未封存必需编译输入 '{path}'。");
    }

    string Canonical(string path)
    {
        string absolute = Path.GetFullPath(path, m_Project).Replace('\\', '/');
        foreach (KeyValuePair<string, string> package in m_PackagePaths)
            if (absolute.StartsWith(package.Key, StringComparison.OrdinalIgnoreCase))
                return package.Value + absolute[package.Key.Length..];
        return absolute;
    }

    static string DirectoryKey(string directory) =>
        PlayerCodeUpdates.Sha256(Encoding.UTF8.GetBytes(directory.ToLowerInvariant()))[..16];

    internal static IEnumerable<KeyValuePair<string, string>> Environment(JsonObject node) =>
        node["Env"]!.AsArray().Select(item => new KeyValuePair<string, string>((string)item!["Key"]!, (string)item["Value"]!));

    internal static string[] Split(string text)
    {
        IntPtr arguments = CommandLineToArgvW("command " + text.Replace('\r', ' ').Replace('\n', ' '), out int count);
        try
        {
            var result = new string[count - 1];
            for (int i = 1; i < count; i++)
                result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, i * IntPtr.Size))!;
            return result;
        }
        finally { LocalFree(arguments); }
    }

    internal static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char value in argument)
        {
            if (value == '\\')
                slashes++;
            else
            {
                result.Append('\\', value == '"' ? slashes * 2 + 1 : slashes).Append(value);
                slashes = 0;
            }
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    sealed class CscResponses : IDisposable
    {
        readonly AssemblyLoadContext m_Context = new("unity-player-csc-responses", isCollectible: true);
        readonly Func<IEnumerable<string>, IEnumerable<string>> m_Lines;
        readonly Func<string, string> m_Unquote;
        readonly object m_Parser;
        readonly MethodInfo m_Parse;
        readonly PropertyInfo m_ParseOptions;
        readonly PropertyInfo m_PreprocessorSymbols;

        public CscResponses(string roslyn)
        {
            Assembly common = m_Context.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(roslyn)!, "Microsoft.CodeAnalysis.dll"));
            Type parser = common.GetType("Microsoft.CodeAnalysis.CommandLineParser", true)!;
            m_Lines = parser.GetMethod("ParseResponseLines", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Func<IEnumerable<string>, IEnumerable<string>>>();
            m_Unquote = parser.GetMethod("RemoveQuotesAndSlashes", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null)!.CreateDelegate<Func<string, string>>();
            Assembly csharp = m_Context.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(roslyn)!, "Microsoft.CodeAnalysis.CSharp.dll"));
            Type csharpParser = csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCommandLineParser", true)!;
            m_Parser = csharpParser.GetProperty("Default", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            m_Parse = csharpParser.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
            m_ParseOptions = csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCommandLineArguments", true)!
                .GetProperty("ParseOptions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
            m_PreprocessorSymbols = common.GetType("Microsoft.CodeAnalysis.ParseOptions", true)!.GetProperty("PreprocessorSymbolNames")!;
        }

        public IEnumerable<string> Read(string path) => m_Lines(File.ReadLines(path, Encoding.UTF8)).Select(m_Unquote);
        public string[] Defines(IEnumerable<string> arguments, string project, string sdkDirectory)
        {
            object parsed = m_Parse.Invoke(m_Parser, new object?[] { arguments, project, sdkDirectory, null })!;
            object options = m_ParseOptions.GetValue(parsed)!;
            return ((IEnumerable<string>)m_PreprocessorSymbols.GetValue(options)!).ToArray();
        }
        public void Dispose() => m_Context.Unload();
    }
}
