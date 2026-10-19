using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ThirdPersonSimulation;

namespace Btsmtl.BlackboardData.Generation;

internal sealed class BlackboardSchema : IDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly UTF8Encoding Utf8 = new(false);
    readonly JsonDocument m_Input;
    readonly string m_Namespace;
    readonly JsonElement[] m_Targets;
    readonly ModuleFactory[] m_ModuleFactories;
    readonly List<Block> m_Blocks = new();

    internal BlackboardSchema(string inputPath, string manifestPath)
    {
        m_Input = JsonDocument.Parse(File.ReadAllText(inputPath, Utf8));
        JsonElement root = m_Input.RootElement;
        if (root.GetProperty("protocol").GetInt32() != GeneratedBlackboardProtocol.Version)
            throw new InvalidDataException("黑板声明输入协议不匹配。");
        m_Namespace = root.GetProperty("namespace").GetString()!;
        m_Targets = root.GetProperty("numericTargets").EnumerateArray().ToArray();
        m_ModuleFactories = m_Targets.Select(target =>
        {
            string name = target.GetProperty("name").GetString()!;
            if (!target.TryGetProperty("moduleFactory", out JsonElement factory))
                throw new InvalidDataException($"数值目标 '{name}' 缺少 moduleFactory 声明；请通过当前项目入口重新导出。");
            return new ModuleFactory(name, factory.GetProperty("namespace").GetString()!,
                factory.GetProperty("typeName").GetString()!, factory.GetProperty("methodName").GetString()!,
                target.GetProperty("valueType").GetString()!);
        }).ToArray();
        var assignments = ReadBlockAssignments(manifestPath);
        foreach (JsonElement owner in root.GetProperty("owners").EnumerateArray()
            .OrderBy(value => value.GetProperty("identity").GetString(), StringComparer.Ordinal))
        {
            string ownerId = owner.GetProperty("identity").GetString()!;
            var declarations = owner.GetProperty("fields").EnumerateArray()
                .OrderBy(value => value.GetProperty("id").GetString(), StringComparer.Ordinal).ToArray();
            foreach (var scope in declarations.GroupBy(value => value.GetProperty("scope").GetString()!, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var unassigned = scope.ToDictionary(field => field.GetProperty("id").GetString()!, StringComparer.Ordinal);
                var blocks = new List<List<JsonElement>>();
                if (assignments.TryGetValue((ownerId, scope.Key), out string[][]? previous))
                {
                    foreach (string[] assigned in previous)
                    {
                        var fields = new List<JsonElement>(GeneratedBlackboardProtocol.MaximumFieldsPerBlock);
                        foreach (string id in assigned)
                            if (unassigned.Remove(id, out JsonElement field))
                                fields.Add(field);
                        blocks.Add(fields);
                    }
                }
                int available = 0;
                foreach (JsonElement field in unassigned.Values.OrderBy(value => value.GetProperty("id").GetString(), StringComparer.Ordinal))
                {
                    while (available < blocks.Count && blocks[available].Count == GeneratedBlackboardProtocol.MaximumFieldsPerBlock)
                        available++;
                    if (available == blocks.Count)
                        blocks.Add(new List<JsonElement>(GeneratedBlackboardProtocol.MaximumFieldsPerBlock));
                    blocks[available].Add(field);
                }
                foreach (List<JsonElement> fields in blocks.Where(block => block.Count != 0))
                {
                    JsonElement[] blockFields = fields.OrderBy(field => field.GetProperty("id").GetString(), StringComparer.Ordinal).ToArray();
                    string signature = string.Join("\n", blockFields.Select(field => string.Join("|",
                        field.GetProperty("id").GetString(), field.GetProperty("key").GetString(),
                        field.GetProperty("type").GetString(), field.GetProperty("scope").GetString(),
                        field.GetProperty("lifetime").GetString(), JsonSerializer.Serialize(field.GetProperty("binding")))));
                    string identity = Hash($"{GeneratedBlackboardProtocol.Version}|{ownerId}|{scope.Key}|{signature}");
                    m_Blocks.Add(new Block(ownerId, identity, "Blackboard_" + identity[..24], blockFields));
                }
            }
        }
    }

    internal IEnumerable<string> TargetNames => m_Targets.Select(target => target.GetProperty("name").GetString()!);

    static Dictionary<(string Owner, string Scope), string[][]> ReadBlockAssignments(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return new();
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath, Utf8));
        if (manifest.RootElement.GetProperty("protocol").GetInt32() != GeneratedBlackboardProtocol.Version)
            throw new InvalidDataException("黑板生成清单协议不匹配，无法读取原字段分块归属。");
        return manifest.RootElement.GetProperty("declarations").EnumerateArray()
            .GroupBy(field => (Owner: field.GetProperty("owner").GetString()!, Scope: field.GetProperty("scope").GetString()!))
            .ToDictionary(scope => scope.Key, scope => scope
                .GroupBy(field => field.GetProperty("defaultsFile").GetString()!, StringComparer.Ordinal)
                .Select(block => block.Select(field => field.GetProperty("id").GetString()!).ToArray()).ToArray());
    }

    internal void Write(string output, string[] precompiledReferences)
    {
        foreach (JsonElement numeric in m_Targets)
        {
            string target = numeric.GetProperty("name").GetString()!;
            string directory = Path.Combine(output, target);
            Directory.CreateDirectory(Path.Combine(directory, "Schema"));
            Directory.CreateDirectory(Path.Combine(directory, "Defaults"));
            var definitions = new XElement("module", new XAttribute("name", m_Namespace + "." + target));
            AddValueTypes(definitions, numeric);
            foreach (Block block in m_Blocks)
            {
                var bean = new XElement("bean", new XAttribute("name", block.Name));
                Tag(bean, "owner", Literal(block.Owner));
                Tag(bean, "identity", Literal(TargetIdentity(numeric, block.Identity)));
                Tag(bean, "domain", target);
                Tag(bean, "valueType", numeric.GetProperty("valueType").GetString()!);
                Tag(bean, "dataFile", Literal(block.Name.ToLowerInvariant() + ".bytes"));
                var defaults = new Dictionary<string, JsonElement>();
                for (int index = 0; index < block.Fields.Length; index++)
                {
                    JsonElement field = block.Fields[index];
                    string kind = field.GetProperty("type").GetString()!;
                    string name = "Value_" + Hash(field.GetProperty("id").GetString()!)[..20];
                    var variable = new XElement("var", new XAttribute("name", name), new XAttribute("type", SchemaType(kind)));
                    Tag(variable, "id", Literal(field.GetProperty("id").GetString()!));
                    Tag(variable, "key", Literal(field.GetProperty("key").GetString()!));
                    Tag(variable, "kind", ValueKind(kind));
                    Tag(variable, "scope", field.GetProperty("scope").GetString()!);
                    Tag(variable, "lifetime", field.GetProperty("lifetime").GetString()!);
                    Tag(variable, "index", index.ToString(CultureInfo.InvariantCulture));
                    bean.Add(variable);
                    defaults.Add(name, field.GetProperty("loweredDefaults").GetProperty(target));
                }
                definitions.Add(bean);
                definitions.Add(new XElement("table", new XAttribute("name", block.Name + "Defaults"),
                    new XAttribute("value", block.Name), new XAttribute("mode", "one"),
                    new XAttribute("input", block.Name + ".json"),
                    new XAttribute("output", block.Name.ToLowerInvariant())));
                File.WriteAllText(Path.Combine(directory, "Defaults", block.Name + ".json"),
                    JsonSerializer.Serialize(defaults, JsonOptions), Utf8);
            }
            new XDocument(new XElement("root", definitions)).Save(Path.Combine(directory, "Schema", "blackboard.xml"));
            File.WriteAllText(Path.Combine(directory, "luban.conf"), JsonSerializer.Serialize(new
            {
                groups = new[] { new { names = new[] { "runtime" }, @default = true } },
                schemaFiles = new[] { new { fileName = "Schema/blackboard.xml", type = "" } },
                dataDir = "Defaults",
                targets = new[] { new { name = "runtime", manager = "BlackboardModule", groups = new[] { "runtime" }, topModule = "" } }
            }, JsonOptions), Utf8);
        }
        File.WriteAllText(Path.Combine(output, m_Namespace + ".asmdef"), JsonSerializer.Serialize(new
        {
            name = m_Namespace,
            references = new[] { "ThirdPerson.Shared.Contracts", "ThirdPerson.Luban", "BTSMTL.Orchestration.Contracts",
                "ThirdPersonSimulation.BlackboardConfiguration" },
            overrideReferences = true,
            precompiledReferences,
            noEngineReferences = true
        }, JsonOptions), Utf8);
    }

    internal void WriteModuleFactories(string output)
    {
        foreach (ModuleFactory factory in m_ModuleFactories)
        {
            string source = string.Join("\n", new[]
            {
                $"namespace {factory.Namespace}",
                "{",
                $"    public static partial class {factory.TypeName}",
                "    {",
                $"        static partial void {factory.MethodName}(ref global::ThirdPersonSimulation.GeneratedBlackboardModule<global::{factory.ValueType}> module)",
                "        {",
                $"            module = new global::{m_Namespace}.{factory.Target}.BlackboardModule();",
                "        }",
                "    }",
                "}"
            }) + "\n";
            File.WriteAllText(Path.Combine(output, factory.Target, "CSharp", "BlackboardModule.Factory.g.cs"), source, Utf8);
        }
    }

    internal void WriteManifest(string output)
    {
        using JsonDocument dependency = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "luban.lock.json"), Utf8));
        string[] sources = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] defaults = sources.Where(path => path.EndsWith(".bytes", StringComparison.Ordinal)).ToArray();
        File.WriteAllText(Path.Combine(output, "blackboard.manifest.json"), JsonSerializer.Serialize(new
        {
            protocol = GeneratedBlackboardProtocol.Version,
            @namespace = m_Namespace,
            numericTargets = m_Targets,
            inputIdentity = Hash(JsonSerializer.Serialize(m_Input.RootElement)),
            structureIdentity = Hash(m_Namespace + "|" + string.Join("|", m_Blocks.Select(block =>
                string.Join("|", m_Targets.Select(target => TargetIdentity(target, block.Identity)))))),
            defaultsIdentity = Hash(string.Join("|", defaults.Select(path =>
                Path.GetRelativePath(output, path).Replace('\\', '/') + "|" + Hash(File.ReadAllBytes(path))))),
            tool = dependency.RootElement,
            templateIdentity = Hash(string.Join("|", Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Templates"), "*.sbn", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal).Select(path => Hash(File.ReadAllBytes(path))))),
            inputs = m_Input.RootElement.GetProperty("inputs"),
            csharp = "generated",
            cpp = "schema-reserved",
            owners = m_Blocks.Select(block => new { identity = block.Owner, contract = block.Identity, type = block.Name,
                contracts = TargetContracts(block.Identity) }),
            declarations = m_Blocks.SelectMany(block => block.Fields.Select((field, index) => new
            {
                owner = block.Owner, id = field.GetProperty("id").GetString(), index, source = field,
                type = field.GetProperty("type").GetString(), scope = field.GetProperty("scope").GetString(),
                lifetime = field.GetProperty("lifetime").GetString(),
                contracts = TargetContracts(block.Identity),
                defaultsFile = block.Name.ToLowerInvariant() + ".bytes"
            })),
            files = sources.Select(path => new { path = Path.GetRelativePath(output, path).Replace('\\', '/'),
                hash = Hash(File.ReadAllBytes(path)) }),
            defaults = defaults
                .Select(path => new { path = Path.GetRelativePath(output, path).Replace('\\', '/'), hash = Hash(File.ReadAllBytes(path)) })
        }, JsonOptions), Utf8);
    }

    public void Dispose() => m_Input.Dispose();

    Dictionary<string, string> TargetContracts(string identity) => m_Targets.ToDictionary(
        target => target.GetProperty("name").GetString()!, target => TargetIdentity(target, identity), StringComparer.Ordinal);

    static string TargetIdentity(JsonElement target, string identity) => Hash(
        $"{target.GetProperty("name").GetString()}|{target.GetProperty("profileId").GetString()}|{target.GetProperty("targetAbi").GetRawText()}|{identity}");

    static void AddValueTypes(XElement module, JsonElement target)
    {
        string numberType = target.GetProperty("rawType").GetString()!;
        AddMapped(module, "Number", target.GetProperty("scalarType").GetString()!,
            target.GetProperty("numberFactory").GetString()!, ("Raw", numberType));
        AddMapped(module, "Vector2", target.GetProperty("vector2Type").GetString()!,
            target.GetProperty("vector2Factory").GetString()!, ("X", numberType), ("Y", numberType));
        AddMapped(module, "Vector3", target.GetProperty("vector3Type").GetString()!,
            target.GetProperty("vector3Factory").GetString()!, ("X", numberType), ("Y", numberType), ("Z", numberType));
        AddMapped(module, "UInt64", "System.UInt64", "ThirdPersonSimulation.Configuration.BlackboardDefaultValues.UInt64", ("Bits", "long"));
        AddMapped(module, "Text", "System.String", "ThirdPersonSimulation.Configuration.BlackboardDefaultValues.Text", ("IsNull", "bool"), ("CodeUnits", "array,int"));
    }

    static void AddMapped(XElement module, string name, string csharpType, string constructor,
        params (string Name, string Type)[] members)
    {
        var bean = new XElement("bean", new XAttribute("name", name), new XAttribute("valueType", "1"));
        foreach (var member in members)
            bean.Add(new XElement("var", new XAttribute("name", member.Name), new XAttribute("type", member.Type)));
        Tag(bean, "mappedType", csharpType);
        Tag(bean, "constructor", constructor);
        bean.Add(new XElement("mapper", new XAttribute("target", "runtime"), new XAttribute("codeTarget", "cs-bin"),
            new XElement("option", new XAttribute("name", "type"), new XAttribute("value", csharpType))));
        module.Add(bean);
    }

    static string SchemaType(string kind) => kind switch
    {
        "Bool" => "bool", "Int32" => "int", "UInt64" => "UInt64", "Number" => "Number",
        "Text" => "Text", "Vector2" => "Vector2", "Vector3" => "Vector3",
        _ => throw new InvalidDataException($"黑板不支持类型 '{kind}'。")
    };

    static string ValueKind(string kind) => kind switch
    {
        "Bool" => "Boolean", "Number" => "Scalar", "Text" => "Identity",
        _ => kind
    };

    static void Tag(XElement element, string name, string value)
    {
        var escaped = new StringBuilder();
        foreach (char character in value)
        {
            if ("\\#()[]{}".IndexOf(character) >= 0)
                escaped.Append('\\');
            escaped.Append(character);
        }
        string? previous = (string?)element.Attribute("tags");
        element.SetAttributeValue("tags", (previous == null ? "" : previous + "#") + "btsmtl." + name + "=" + escaped);
    }

    static string Literal(string value) => JsonSerializer.Serialize(value);
    static string Hash(string value) => Hash(Utf8.GetBytes(value));
    static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    sealed record ModuleFactory(string Target, string Namespace, string TypeName, string MethodName, string ValueType);
    sealed record Block(string Owner, string Identity, string Name, JsonElement[] Fields);
}
