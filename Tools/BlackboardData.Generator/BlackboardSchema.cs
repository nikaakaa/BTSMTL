using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

namespace Btsmtl.BlackboardData.Generation;

internal sealed class BlackboardSchema : IDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly UTF8Encoding Utf8 = new(false);
    readonly JsonDocument m_Input;
    readonly string m_Namespace;
    readonly List<Block> m_Blocks = new();

    internal BlackboardSchema(string inputPath)
    {
        m_Input = JsonDocument.Parse(File.ReadAllText(inputPath, Utf8));
        JsonElement root = m_Input.RootElement;
        if (root.GetProperty("protocol").GetInt32() != GeneratedBlackboardProtocol.Version)
            throw new InvalidDataException("黑板声明输入协议不匹配。");
        m_Namespace = root.GetProperty("namespace").GetString()!;
        foreach (JsonElement owner in root.GetProperty("owners").EnumerateArray()
            .OrderBy(value => value.GetProperty("identity").GetString(), StringComparer.Ordinal))
        {
            string ownerId = owner.GetProperty("identity").GetString()!;
            var declarations = owner.GetProperty("fields").EnumerateArray()
                .OrderBy(value => value.GetProperty("id").GetString(), StringComparer.Ordinal).ToArray();
            foreach (var scope in declarations.GroupBy(value => value.GetProperty("scope").GetString()!, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                JsonElement[] fields = scope.ToArray();
                for (int offset = 0; offset < fields.Length; offset += GeneratedBlackboardProtocol.MaximumFieldsPerBlock)
                {
                    JsonElement[] blockFields = fields.Skip(offset).Take(GeneratedBlackboardProtocol.MaximumFieldsPerBlock).ToArray();
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

    internal void Write(string output)
    {
        foreach (string target in new[] { "Fixed", "Float32" })
        {
            string directory = Path.Combine(output, target);
            Directory.CreateDirectory(Path.Combine(directory, "Schema"));
            Directory.CreateDirectory(Path.Combine(directory, "Defaults"));
            var definitions = new XElement("module", new XAttribute("name", m_Namespace + "." + target));
            AddValueTypes(definitions, target);
            foreach (Block block in m_Blocks)
            {
                var bean = new XElement("bean", new XAttribute("name", block.Name));
                Tag(bean, "owner", Literal(block.Owner));
                Tag(bean, "identity", Literal(TargetIdentity(target, block.Identity)));
                Tag(bean, "domain", target);
                Tag(bean, "dataFile", Literal(block.Name.ToLowerInvariant() + ".bytes"));
                var defaults = new Dictionary<string, object?>();
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
                    defaults.Add(name, Default(field.GetProperty("default"), kind, target));
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
            references = new[] { "ThirdPerson.Shared.Contracts", "ThirdPerson.Luban", "ThirdPersonSimulation.Core",
                "ThirdPersonSimulation.Fixed", "ThirdPersonSimulation.Float32", "ThirdPersonSimulation.BlackboardConfiguration" },
            noEngineReferences = true
        }, JsonOptions), Utf8);
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
            inputIdentity = Hash(JsonSerializer.Serialize(m_Input.RootElement)),
            structureIdentity = Hash(m_Namespace + "|" + string.Join("|", m_Blocks.Select(block =>
                TargetIdentity("Fixed", block.Identity) + "|" + TargetIdentity("Float32", block.Identity)))),
            defaultsIdentity = Hash(string.Join("|", defaults.Select(path =>
                Path.GetRelativePath(output, path).Replace('\\', '/') + "|" + Hash(File.ReadAllBytes(path))))),
            tool = dependency.RootElement,
            templateIdentity = Hash(string.Join("|", Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Templates"), "*.sbn", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal).Select(path => Hash(File.ReadAllBytes(path))))),
            inputs = m_Input.RootElement.GetProperty("inputs"),
            csharp = "generated",
            cpp = "schema-reserved",
            owners = m_Blocks.Select(block => new { identity = block.Owner, contract = block.Identity, type = block.Name,
                fixedContract = TargetIdentity("Fixed", block.Identity), float32Contract = TargetIdentity("Float32", block.Identity) }),
            declarations = m_Blocks.SelectMany(block => block.Fields.Select((field, index) => new
            {
                owner = block.Owner, id = field.GetProperty("id").GetString(), index, source = field,
                type = field.GetProperty("type").GetString(), scope = field.GetProperty("scope").GetString(),
                lifetime = field.GetProperty("lifetime").GetString(),
                fixedContract = TargetIdentity("Fixed", block.Identity), float32Contract = TargetIdentity("Float32", block.Identity),
                defaultsFile = block.Name.ToLowerInvariant() + ".bytes"
            })),
            files = sources.Select(path => new { path = Path.GetRelativePath(output, path).Replace('\\', '/'),
                hash = Hash(File.ReadAllBytes(path)) }),
            defaults = defaults
                .Select(path => new { path = Path.GetRelativePath(output, path).Replace('\\', '/'), hash = Hash(File.ReadAllBytes(path)) })
        }, JsonOptions), Utf8);
    }

    public void Dispose() => m_Input.Dispose();

    static string TargetIdentity(string target, string identity)
    {
        SimulationNumericProfile profile = target == "Fixed" ? FixedSimulationNumericProfile.Value : Float32SimulationNumericProfile.Value;
        return Hash($"{target}|{profile.Id.Value}|{profile.AbiVersion.Value}|{identity}");
    }

    static void AddValueTypes(XElement module, string target)
    {
        string numberType = target == "Fixed" ? "long" : "int";
        string numericNamespace = target == "Fixed" ? "ThirdPersonSimulation.Fixed" : "ThirdPersonSimulation";
        AddMapped(module, "Number", numericNamespace + "." + target + "Scalar", target + "Number", ("Raw", numberType));
        AddMapped(module, "Vector2", numericNamespace + "." + target + "Vector2", target + "Vector2", ("X", numberType), ("Y", numberType));
        AddMapped(module, "Vector3", numericNamespace + "." + target + "Vector3", target + "Vector3", ("X", numberType), ("Y", numberType), ("Z", numberType));
        AddMapped(module, "UInt64", "System.UInt64", "UInt64", ("Bits", "long"));
        AddMapped(module, "Text", "ThirdPersonSimulation.CanonicalTextValue", "Text", ("IsNull", "bool"), ("CodeUnits", "array,int"));
    }

    static void AddMapped(XElement module, string name, string csharpType, string constructor,
        params (string Name, string Type)[] members)
    {
        var bean = new XElement("bean", new XAttribute("name", name), new XAttribute("valueType", "1"));
        foreach (var member in members)
            bean.Add(new XElement("var", new XAttribute("name", member.Name), new XAttribute("type", member.Type)));
        Tag(bean, "mappedType", csharpType);
        Tag(bean, "constructor", "ThirdPersonSimulation.Configuration.BlackboardDefaultValues." + constructor);
        bean.Add(new XElement("mapper", new XAttribute("target", "runtime"), new XAttribute("codeTarget", "cs-bin"),
            new XElement("option", new XAttribute("name", "type"), new XAttribute("value", csharpType))));
        module.Add(bean);
    }

    static object? Default(JsonElement value, string kind, string target) => kind switch
    {
        "Bool" => value.GetBoolean(),
        "Int32" => value.GetInt32(),
        "UInt64" => new { Bits = unchecked((long)value.GetUInt64()) },
        "Number" => new { Raw = Number(value.GetDouble(), target) },
        "Vector2" => Vector(value, target, 2),
        "Vector3" => Vector(value, target, 3),
        "Text" => Text(value),
        _ => throw new InvalidDataException($"黑板不支持类型 '{kind}'。")
    };

    static object Text(JsonElement value)
    {
        return new { IsNull = value.GetProperty("isNull").GetBoolean(),
            CodeUnits = value.GetProperty("codeUnits").EnumerateArray().Select(character => character.GetInt32()).ToArray() };
    }

    static Dictionary<string, long> Vector(JsonElement value, string target, int dimension)
    {
        string[] names = { "X", "Y", "Z" };
        var result = new Dictionary<string, long>();
        int index = 0;
        foreach (JsonElement component in value.EnumerateArray())
            result.Add(names[index++], Number(component.GetDouble(), target));
        if (index != dimension)
            throw new InvalidDataException($"Vector{dimension} 默认值分量数不匹配。");
        return result;
    }

    static long Number(double value, string target) => target == "Fixed"
        ? FixedScalar.FromDouble(value).Raw : unchecked((int)Float32Scalar.FromDouble(value).Bits);

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
    sealed record Block(string Owner, string Identity, string Name, JsonElement[] Fields);
}
