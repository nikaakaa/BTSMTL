using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ThirdPersonGameplay.Configuration;
using ThirdPersonSimulation;

namespace Btsmtl.GameplayConfig.Generation;

internal static class Program
{
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("用法：Btsmtl.GameplayConfig.Generator <配置源目录> <配置程序集目录> <数据输出目录> <锁定 Luban.dll>");
            return 2;
        }
        string input = Path.GetFullPath(args[0]);
        string code = Path.GetFullPath(args[1]);
        string data = Path.GetFullPath(args[2]);
        string pendingRoot = Path.Combine(Path.GetTempPath(), "BtsmtlGameplayConfig");
        string pending = Path.Combine(pendingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        using JsonDocument source = JsonDocument.Parse(File.ReadAllText(Path.Combine(input, "source.json"), Utf8));
        var schema = new Schema(Path.Combine(input, "Schema", "gameplay.xml"));
        try
        {
            RunLuban(args[3], input, pending);
            string reader = Path.Combine(pending, "Code", "Reader", "Generated");
            string contracts = Path.Combine(pending, "Code", "Generated");
            Directory.CreateDirectory(contracts);
            foreach (string name in schema.Enums)
            {
                string relative = schema.Namespace.Replace('.', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + name + ".cs";
                string origin = Path.Combine(reader, relative);
                string destination = Path.Combine(contracts, "Enums", name + ".cs");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(origin, destination);
            }
            schema.WriteContracts(contracts);
            schema.WriteReader(reader, contracts);
            schema.WriteClosure(reader);
            var records = CollectRecordSources(input, pending, schema);
            string contentIdentity = Hash(string.Join("|", records.Select(record => record.Table + "|" + record.RecordId + "|" + record.ContentIdentity)));
            var dataFiles = Directory.GetFiles(Path.Combine(pending, "Data"), "*.bytes").OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new GameplayConfigDataFile(Path.GetFileNameWithoutExtension(path), Hash(File.ReadAllBytes(path)))).ToArray();
            var identity = new GameplayConfigMetadata(source.RootElement.GetProperty("protocol").GetInt32(),
                source.RootElement.GetProperty("sourceId").GetString()!, schema.Identity, contentIdentity,
                records.Select(record => new GameplayConfigRecordSource(record.Table, record.RecordId, record.Workbook,
                    record.Sheet, record.Row, record.ContentIdentity)).ToArray(), dataFiles);
            using (var writer = new CanonicalWriter())
            {
                writer.WriteHash(GameplayConfigMetadata.CanonicalSchemaHash);
                GameplayConfigMetadata.CanonicalWrite(writer, identity);
                File.WriteAllBytes(Path.Combine(pending, "Data", GameplayConfigProtocol.MetadataFile + ".bytes"), writer.ToArray());
            }
            File.WriteAllText(Path.Combine(pending, "Data", "gameplay-config.manifest.json"), JsonSerializer.Serialize(new
            {
                protocol = source.RootElement.GetProperty("protocol").GetInt32(),
                sourceId = source.RootElement.GetProperty("sourceId").GetString(),
                schemaIdentity = schema.Identity,
                contentIdentity,
                tool = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "luban.lock.json"), Utf8)).RootElement,
                records,
                files = Directory.GetFiles(Path.Combine(pending, "Data"), "*.bytes").OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => new { path = Path.GetFileName(path), hash = Hash(File.ReadAllBytes(path)) }),
            }, JsonOptions), Utf8);
            Publish(Path.Combine(pending, "Code"), code, "gameplay-config.code-manifest.json");
            Publish(Path.Combine(pending, "Data"), data, "gameplay-config.data-manifest.json");
            Console.WriteLine($"Gameplay 配置已生成：{schema.Tables.Length} 张表，{records.Count} 条记录，合同 {schema.Identity}。尚未发布 GE 或采用新实例。");
        }
        finally
        {
            string resolved = Path.GetFullPath(pending);
            if (!resolved.StartsWith(Path.GetFullPath(pendingRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Gameplay 生成临时目录不属于本次写集。");
            Directory.Delete(resolved, true);
        }
        return 0;
    }

    static void RunLuban(string entry, string input, string pending)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (string argument in new[] { Path.GetFullPath(entry), "--conf", Path.Combine(input, "luban.conf"),
            "-t", "runtime", "-c", "cs-bin", "-c", "schema-json", "-d", "bin", "-d", "json", "--strict", "--locale", "zh",
            "--customTemplateDir", Path.Combine(AppContext.BaseDirectory, "Templates"),
            "-x", "cs-bin.outputCodeDir=" + Path.Combine(pending, "Code", "Reader", "Generated"),
            "-x", "schema-json.outputCodeDir=" + Path.Combine(pending, "Schema"),
            "-x", "bin.outputDataDir=" + Path.Combine(pending, "Data"),
            "-x", "json.outputDataDir=" + Path.Combine(pending, "Json") })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Luban Gameplay 生成失败，退出码 {process.ExitCode}。");
    }

    static List<RecordSource> CollectRecordSources(string input, string pending, Schema schema)
    {
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(pending, "Schema", "schema.json"), Utf8));
        var result = new List<RecordSource>();
        foreach (JsonElement table in metadata.RootElement.GetProperty("tables").EnumerateArray())
        {
            string tableName = table.GetProperty("name").GetString()!;
            string beanName = schema.Tables.Single(value => value.Attribute("name")!.Value == tableName).Attribute("value")!.Value;
            var locations = new Dictionary<string, (string Workbook, string Sheet, int Row)>(StringComparer.Ordinal);
            foreach (JsonElement source in table.GetProperty("inputFiles").EnumerateArray())
            {
                string[] parts = source.GetString()!.Split('@');
                string workbook = parts[^1];
                string? selectedSheet = parts.Length == 2 ? parts[0] : null;
                ReadLocations(Path.Combine(input, "Tables", workbook), workbook, selectedSheet, locations);
            }
            using JsonDocument rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(pending, "Json",
                table.GetProperty("outputDataFile").GetString() + ".json"), Utf8));
            foreach (JsonElement row in rows.RootElement.EnumerateArray())
            {
                string id = row.GetProperty("Id").GetString()!;
                var location = locations[id];
                result.Add(new RecordSource(tableName, id, "Tables/" + location.Workbook, location.Sheet, location.Row,
                    Hash(JsonSerializer.Serialize(schema.GameplayContent(beanName, row)))));
            }
        }
        return result.OrderBy(record => record.Table, StringComparer.Ordinal).ThenBy(record => record.RecordId, StringComparer.Ordinal).ToList();
    }

    static void ReadLocations(string path, string workbook, string? selectedSheet,
        Dictionary<string, (string Workbook, string Sheet, int Row)> result)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        using Stream workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
        using Stream relationsStream = archive.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        XDocument book = XDocument.Load(workbookStream);
        var relations = XDocument.Load(relationsStream).Root!.Elements()
            .ToDictionary(element => element.Attribute("Id")!.Value, element => element.Attribute("Target")!.Value);
        var strings = new List<string>();
        ZipArchiveEntry? shared = archive.GetEntry("xl/sharedStrings.xml");
        if (shared != null)
        {
            using Stream stream = shared.Open();
            strings.AddRange(XDocument.Load(stream).Root!.Elements(Spreadsheet + "si")
                .Select(element => string.Concat(element.Descendants(Spreadsheet + "t").Select(text => text.Value))));
        }
        foreach (XElement sheet in book.Descendants(Spreadsheet + "sheet"))
        {
            string sheetName = sheet.Attribute("name")!.Value;
            if (selectedSheet != null && selectedSheet != sheetName)
                continue;
            string target = relations[sheet.Attribute(Relationships + "id")!.Value];
            using Stream stream = archive.GetEntry(target.StartsWith('/') ? target[1..] : "xl/" + target)!.Open();
            string? column = null;
            foreach (XElement row in XDocument.Load(stream).Descendants(Spreadsheet + "row"))
            {
                var cells = row.Elements(Spreadsheet + "c").ToArray();
                string Cell(XElement cell) => cell.Attribute("t")?.Value switch
                {
                    "s" => strings[int.Parse(cell.Element(Spreadsheet + "v")!.Value)],
                    "inlineStr" => string.Concat(cell.Descendants(Spreadsheet + "t").Select(text => text.Value)),
                    _ => cell.Element(Spreadsheet + "v")?.Value ?? string.Empty,
                };
                if (cells.Any(cell => Cell(cell) == "##var"))
                {
                    XElement? key = cells.SingleOrDefault(cell => Cell(cell) == "Id");
                    if (key != null)
                        column = new string(key.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray());
                    continue;
                }
                if (cells.Any(cell => Cell(cell).StartsWith("##", StringComparison.Ordinal)))
                    continue;
                XElement? identity = cells.SingleOrDefault(cell => new string(cell.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray()) == column);
                if (identity != null && Cell(identity).Length != 0)
                    result.Add(Cell(identity), (workbook, sheetName, int.Parse(row.Attribute("r")!.Value)));
            }
        }
    }

    static void Publish(string pending, string output, string manifestName)
    {
        Directory.CreateDirectory(output);
        string[] files = Directory.GetFiles(pending, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(pending, path).Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string manifestPath = Path.Combine(output, manifestName);
        using JsonDocument? previous = File.Exists(manifestPath) ? JsonDocument.Parse(File.ReadAllText(manifestPath, Utf8)) : null;
        foreach (string relative in files)
        {
            string destination = Path.Combine(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            byte[] bytes = File.ReadAllBytes(Path.Combine(pending, relative));
            if (!File.Exists(destination) || !bytes.AsSpan().SequenceEqual(File.ReadAllBytes(destination)))
                File.WriteAllBytes(destination, bytes);
        }
        if (previous != null)
        {
            foreach (JsonElement file in previous.RootElement.GetProperty("files").EnumerateArray())
            {
                string relative = file.GetString()!;
                if (files.Contains(relative, StringComparer.Ordinal))
                    continue;
                string retired = Path.GetFullPath(Path.Combine(output, relative));
                if (!retired.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Gameplay 输出清单包含写集之外的路径。");
                File.Delete(retired);
                File.Delete(retired + ".meta");
            }
        }
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { files }, JsonOptions), Utf8);
    }

    static string Hash(string value) => Hash(Utf8.GetBytes(value));
    static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    static string Literal(string value) => JsonSerializer.Serialize(value);

    sealed record RecordSource(string Table, string RecordId, string Workbook, string Sheet, int Row, string ContentIdentity);

    sealed class Schema
    {
        readonly Dictionary<string, XElement> m_Beans;
        readonly HashSet<string> m_Enums;
        internal readonly XElement[] Tables;
        internal readonly string Namespace;
        internal readonly string Identity;
        internal IEnumerable<string> Enums => m_Enums.OrderBy(value => value, StringComparer.Ordinal);

        internal Schema(string path)
        {
            XElement module = XDocument.Load(path).Root!.Element("module")!;
            Namespace = module.Attribute("name")!.Value;
            m_Beans = module.Elements("bean").ToDictionary(bean => bean.Attribute("name")!.Value, StringComparer.Ordinal);
            m_Enums = module.Elements("enum").Select(element => element.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
            Tables = module.Elements("table").ToArray();
            Identity = Hash(module.ToString(SaveOptions.DisableFormatting));
        }

        static string Kind(XElement field) => field.Attribute("type")!.Value.Split('#')[0];
        static string? Tag(XElement field, string name) => field.Attribute("tags")?.Value.Split('#')
            .Where(tag => tag.StartsWith(name + "=", StringComparison.Ordinal)).Select(tag => tag[(name.Length + 1)..]).SingleOrDefault();
        static bool EditorOnly(XElement field) => Tag(field, "btsmtl.editorOnly") == "1";
        static string Name(XElement field) => field.Attribute("name")!.Value;
        string Type(XElement field) => Tag(field, "btsmtl.csharpType") ?? Type(Kind(field));
        string Type(string kind) => kind.StartsWith("array,", StringComparison.Ordinal) ? "IReadOnlyList<" + Type(kind[6..]) + ">" :
            m_Beans.ContainsKey(kind) ? "Published" + kind : kind;
        string Projection(string kind, string value) => kind.StartsWith("array,", StringComparison.Ordinal)
            ? m_Beans.ContainsKey(kind[6..]) ? value + ".Select(item => " + Projection(kind[6..], "item") + ").ToArray()" : value + ".ToArray()"
            : m_Beans.ContainsKey(kind) ? "Publish(" + value + ")" : value;

        internal object GameplayContent(string beanName, JsonElement row)
        {
            var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            if (row.TryGetProperty("$type", out JsonElement concrete))
            {
                beanName = concrete.GetString()!.Split('.').Last();
                result.Add("$type", beanName);
            }
            foreach (XElement field in m_Beans[beanName].Elements("var").Where(field => !EditorOnly(field)))
            {
                string kind = Kind(field);
                JsonElement value = row.GetProperty(Name(field));
                result.Add(Name(field), m_Beans.ContainsKey(kind) ? GameplayContent(kind, value) : value);
            }
            return result;
        }

        internal void WriteContracts(string directory)
        {
            var code = new StringBuilder("using System;\nusing System.Collections.Generic;\nusing ThirdPersonSimulation;\nnamespace " + Namespace + " {\n");
            foreach (var pair in m_Beans)
            {
                XElement[] children = m_Beans.Values.Where(bean => bean.Attribute("parent")?.Value == pair.Key).ToArray();
                if (children.Length != 0)
                {
                    code.AppendLine("[CanonicalUnionTemplate]");
                    code.Append("public readonly struct ").Append(pair.Key).AppendLine("Value {");
                    foreach (XElement child in children)
                        code.Append("[CanonicalUnionCase] public readonly Published").Append(Name(child)).Append(' ').Append(Name(child)).AppendLine(";");
                    code.AppendLine("}");
                    code.AppendLine("[CanonicalContract(MaterializedRead = true, PreparedRead = false)]");
                    code.Append("[CanonicalUnion(typeof(").Append(pair.Key).AppendLine("Value))]");
                    code.Append("public readonly partial struct Published").Append(pair.Key).AppendLine(" {} ");
                    continue;
                }
                code.AppendLine("[CanonicalContract(MaterializedRead = true, PreparedRead = false)]");
                code.Append("public sealed partial class Published").Append(pair.Key).AppendLine(" {");
                XElement[] fields = pair.Value.Elements("var").Where(field => !EditorOnly(field)).ToArray();
                code.Append("public Published").Append(pair.Key).Append('(')
                    .Append(string.Join(", ", fields.Select(field => Type(field) + " value_" + Name(field)))).AppendLine(") {");
                foreach (XElement field in fields)
                    code.Append(Name(field)).Append(" = value_").Append(Name(field)).AppendLine(";");
                code.AppendLine("}");
                foreach (XElement field in fields)
                    code.Append("public ").Append(Type(field)).Append(' ').Append(Name(field)).AppendLine(" { get; }");
                code.AppendLine("}");
            }
            code.AppendLine("[CanonicalContract(MaterializedRead = true, PreparedRead = false)] public sealed partial class PublishedGameplayConfiguration {");
            code.Append("public PublishedGameplayConfiguration(").Append(string.Join(", ", Tables.Select(table =>
                "IReadOnlyList<Published" + table.Attribute("value")!.Value + "> value_" + Name(table)))).AppendLine(") {");
            foreach (XElement table in Tables)
                code.Append(Name(table)).Append(" = value_").Append(Name(table)).AppendLine(";");
            code.AppendLine("}");
            foreach (XElement table in Tables)
                code.Append("public IReadOnlyList<Published").Append(table.Attribute("value")!.Value).Append("> ").Append(Name(table)).AppendLine(" { get; }");
            code.AppendLine("}");
            code.AppendLine("public enum GameplayConfigRecordKind {");
            foreach (XElement table in Tables)
                code.Append(Name(table)).AppendLine(",");
            code.AppendLine("} }");
            code.Append("namespace ").Append(Namespace).Append(" { public static class GameplayConfigGeneratedContract { public const string SchemaIdentity = ")
                .Append(Literal(Identity)).AppendLine("; } }");
            File.WriteAllText(Path.Combine(directory, "GameplayConfiguration.Contracts.g.cs"), code.ToString(), Utf8);
        }

        internal void WriteReader(string directory, string contractsDirectory)
        {
            var code = new StringBuilder("using System;\nusing System.Collections.Generic;\nusing System.Linq;\nnamespace " + Namespace + " {\npublic static class GameplayConfigProjection {\n");
            foreach (var pair in m_Beans)
            {
                code.Append("public static Published").Append(pair.Key).Append(" Publish(").Append(pair.Key).AppendLine(" value) {");
                XElement[] children = m_Beans.Values.Where(bean => bean.Attribute("parent")?.Value == pair.Key).ToArray();
                if (children.Length != 0)
                {
                    code.AppendLine("return value switch {");
                    foreach (XElement child in children)
                        code.Append(Name(child)).Append(" child => Published").Append(pair.Key).Append(".From").Append(Name(child)).AppendLine("(Publish(child)),");
                    code.AppendLine("_ => throw new InvalidOperationException(\"配置组件不属于正式 schema。\") }; }");
                    continue;
                }
                code.Append("return new Published").Append(pair.Key).Append('(').Append(string.Join(", ", pair.Value.Elements("var")
                    .Where(field => !EditorOnly(field)).Select(field => Projection(Kind(field), "value." + Name(field))))).AppendLine("); }");
            }
            code.AppendLine("public static string DisplayName(GameplayTables tables, GameplayConfigRecordKind kind, string id) => kind switch {");
            foreach (XElement table in Tables)
            {
                string bean = table.Attribute("value")!.Value;
                string field = m_Beans[bean].Elements("var").Any(value => Name(value) == "DisplayName") ? "DisplayName" : "Id";
                code.Append("GameplayConfigRecordKind.").Append(Name(table)).Append(" => tables.").Append(Name(table))
                    .Append(".Get(id).").Append(field).AppendLine(",");
            }
            code.AppendLine("_ => throw new ArgumentOutOfRangeException(nameof(kind)) };");
            code.Append("public static PublishedGameplayConfiguration Publish(GameplayTables tables) => new PublishedGameplayConfiguration(")
                .Append(string.Join(", ", Tables.Select(table => "tables." + Name(table) + ".DataList.Select(Publish).ToArray()"))).AppendLine("); }");
            code.AppendLine("}");
            File.WriteAllText(Path.Combine(directory, "GameplayConfiguration.Projection.g.cs"), code.ToString(), Utf8);
            code = new StringBuilder("using System;\nusing System.Collections.Generic;\nusing System.Linq;\nnamespace " + Namespace + " { public static class GameplayConfigFields {\n");
            foreach (XElement table in Tables)
            {
                code.Append("static readonly IReadOnlyList<GameplayConfigFieldDescription> s_").Append(Name(table))
                    .AppendLine(" = Array.AsReadOnly(new GameplayConfigFieldDescription[] {");
                foreach (Field field in Fields(table.Attribute("value")!.Value, string.Empty))
                    code.Append("new GameplayConfigFieldDescription(").Append(Literal(field.Id)).Append(", ").Append(Literal(field.Contract))
                        .Append(", ").Append(Literal(field.Path)).Append(", ").Append(Literal(field.Type)).Append(", GameplayConfigValueKind.").Append(field.ValueKind).AppendLine("),");
                code.AppendLine("});");
            }
            code.AppendLine("public static IReadOnlyList<GameplayConfigFieldDescription> Describe(GameplayConfigRecordKind kind) => kind switch {");
            foreach (XElement table in Tables)
                code.Append("GameplayConfigRecordKind.").Append(Name(table)).Append(" => s_").Append(Name(table)).AppendLine(",");
            code.AppendLine("_ => throw new ArgumentOutOfRangeException(nameof(kind)) }; ");
            code.AppendLine("public static GameplayConfigRecordKind RecordKind(string table) => table switch {");
            foreach (XElement table in Tables)
                code.Append(Literal(Name(table))).Append(" => GameplayConfigRecordKind.").Append(Name(table)).AppendLine(",");
            code.AppendLine("_ => throw new InvalidOperationException(\"配置表不属于已编译 schema：\" + table) }; ");
            code.AppendLine("public static GameplayConfigValue Read(PublishedGameplayConfiguration tables, GameplayConfigRecordKind kind, string recordId, string fieldId) { switch (kind) {");
            foreach (XElement table in Tables)
            {
                code.Append("case GameplayConfigRecordKind.").Append(Name(table)).AppendLine(": {");
                code.Append("var value = tables.").Append(Name(table)).AppendLine(".Single(item => item.Id == recordId); switch (fieldId) {");
                foreach (Field field in Fields(table.Attribute("value")!.Value, string.Empty).Where(field => field.ValueKind != "Unsupported"))
                    code.Append("case ").Append(Literal(field.Id)).Append(": return GameplayConfigValue.From").Append(field.ValueKind).Append('(')
                        .Append(m_Enums.Contains(field.Type) ? "(int)" : string.Empty).Append("value.").Append(field.Path).AppendLine(");");
                code.AppendLine("default: throw new InvalidOperationException(\"记录字段不存在或不支持基础读取：\" + recordId + \"/\" + fieldId); } }");
            }
            code.AppendLine("default: throw new ArgumentOutOfRangeException(nameof(kind)); } } } }");
            File.WriteAllText(Path.Combine(contractsDirectory, "GameplayConfiguration.Fields.g.cs"), code.ToString(), Utf8);
        }

        internal void WriteClosure(string directory)
        {
            var code = new StringBuilder("using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing ThirdPersonSimulation;\nnamespace " + Namespace + " {\npublic sealed class GameplayConfigClosure {\nreadonly GameplayConfigurationData m_Data;\n");
            foreach (XElement table in Tables)
                code.Append("readonly SortedDictionary<string, ").Append(table.Attribute("value")!.Value).Append("> m_").Append(Name(table))
                    .Append(" = new SortedDictionary<string, ").Append(table.Attribute("value")!.Value).AppendLine(">(StringComparer.Ordinal);");
            code.AppendLine("public GameplayConfigClosure(GameplayConfigurationData data) => m_Data = data;");
            foreach (XElement table in Tables)
            {
                string type = table.Attribute("value")!.Value;
                code.Append("public void Include(").Append(type).AppendLine(" value) {");
                code.Append("if (!m_").Append(Name(table)).AppendLine(".TryAdd(value.Id, value)) return;");
                if (NeedsReferences(type))
                    code.AppendLine("Visit(value);");
                code.AppendLine("}");
            }
            code.AppendLine("public void Include(GameplayConfigRecordKind kind, string id) { switch (kind) {");
            foreach (XElement table in Tables)
                code.Append("case GameplayConfigRecordKind.").Append(Name(table)).Append(": Include(m_Data.Tables.")
                    .Append(Name(table)).AppendLine(".Get(id)); break;");
            code.AppendLine("default: throw new ArgumentOutOfRangeException(nameof(kind)); } }");
            foreach (var pair in m_Beans.Where(pair => NeedsReferences(pair.Key)))
            {
                code.Append("void Visit(").Append(pair.Key).AppendLine(" value) {");
                XElement[] children = m_Beans.Values.Where(bean => bean.Attribute("parent")?.Value == pair.Key).ToArray();
                if (children.Length != 0)
                {
                    code.AppendLine("switch (value) {");
                    foreach (XElement child in children.Where(child => NeedsReferences(Name(child))))
                        code.Append("case ").Append(Name(child)).AppendLine(" child: Visit(child); break;");
                    code.AppendLine("}");
                }
                foreach (XElement field in pair.Value.Elements("var"))
                {
                    string kind = Kind(field);
                    bool reference = field.Attribute("type")!.Value.Split('#').Any(tag => tag.StartsWith("ref=", StringComparison.Ordinal));
                    if (reference)
                    {
                        if (kind.StartsWith("array,", StringComparison.Ordinal))
                            code.Append("foreach (var child in value.").Append(Name(field)).AppendLine("_Ref) Include(child);");
                        else
                            code.Append("Include(value.").Append(Name(field)).AppendLine("_Ref);");
                    }
                    else if (m_Beans.ContainsKey(kind) && NeedsReferences(kind))
                        code.Append("Visit(value.").Append(Name(field)).AppendLine(");");
                }
                code.AppendLine("}");
            }
            code.AppendLine("public PublishedGameplayConfiguration Publish() => new PublishedGameplayConfiguration(");
            code.Append(string.Join(",\n", Tables.Select(table => "m_" + Name(table) + ".Values.Select(GameplayConfigProjection.Publish).ToArray()"))).AppendLine(");");
            code.AppendLine("public StableHash ContentIdentity() { using var writer = new CanonicalWriter(); writer.WriteText(m_Data.Metadata.SourceId); writer.WriteHash(PublishedGameplayConfiguration.CanonicalSchemaHash); PublishedGameplayConfiguration.CanonicalWrite(writer, Publish()); return SimulationCanonicalPayloadHash.Compute(writer.ToArray()); }");
            code.AppendLine("} }");
            File.WriteAllText(Path.Combine(directory, "GameplayConfiguration.Closure.g.cs"), code.ToString(), Utf8);
        }

        bool NeedsReferences(string name) => m_Beans[name].Elements("var").Any(field =>
            field.Attribute("type")!.Value.Split('#').Any(tag => tag.StartsWith("ref=", StringComparison.Ordinal)) ||
            m_Beans.ContainsKey(Kind(field)) && NeedsReferences(Kind(field))) ||
            m_Beans.Values.Where(bean => bean.Attribute("parent")?.Value == name).Any(bean => NeedsReferences(Name(bean)));

        IEnumerable<Field> Fields(string beanName, string prefix, string? recordType = null)
        {
            recordType ??= beanName;
            foreach (XElement field in m_Beans[beanName].Elements("var"))
            {
                if (EditorOnly(field))
                    continue;
                string path = prefix + Name(field);
                string kind = Kind(field);
                if (m_Beans.TryGetValue(kind, out XElement? bean) && bean.HasElements)
                {
                    foreach (Field nested in Fields(kind, path + ".", recordType))
                        yield return nested;
                    continue;
                }
                string actualType = Tag(field, "btsmtl.csharpType") ?? kind;
                string valueKind = actualType switch
                {
                    "bool" => "Boolean", "int" => "Int32", "uint" or "ulong" => "UInt64",
                    "float" => "Number", "string" => "Text", _ => m_Enums.Contains(kind) ? "Int32" : "Unsupported",
                };
                string id = Hash(Namespace + "/" + recordType + "/" + path);
                yield return new Field(path, actualType, valueKind, id, Hash(id + "|" + actualType + "|" + valueKind));
            }
        }

        sealed record Field(string Path, string Type, string ValueKind, string Id, string Contract);
    }
}
