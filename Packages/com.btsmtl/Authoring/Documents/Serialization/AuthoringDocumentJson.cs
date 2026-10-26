using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringDocumentFormatException : FormatException
    {
        public AuthoringDocumentFormatException(string sourcePath, string elementPath, string reason)
            : base($"{sourcePath}:{elementPath}: {reason}")
        {
            SourcePath = sourcePath;
            ElementPath = elementPath;
        }

        public string SourcePath { get; }
        public string ElementPath { get; }
    }

    public static class AuthoringDocumentJson
    {
        public static string WriteRoot(AuthoringRootDocument root) => Write(new JObject
        {
            ["formatVersion"] = AuthoringRootDocument.FormatVersion,
            ["id"] = root.Id.Value,
            ["domain"] = root.DomainId.Value,
            ["kind"] = root.Kind,
            ["definitionVersion"] = root.DefinitionVersion,
            ["displayName"] = root.DisplayName,
            ["fields"] = WriteFields(root.Fields),
            ["files"] = new JArray(root.Files.ToArray().Select(file => new JObject
            {
                ["id"] = file.Id.Value,
                ["kind"] = file.Kind.ToString(),
                ["path"] = file.Path
            })),
            ["externalReferences"] = new JArray(root.ExternalReferences.ToArray().Select(WriteReference)),
            ["unityOutputs"] = new JArray(root.UnityOutputs.ToArray().Select(output => new JObject
            {
                ["documentId"] = output.DocumentId.Value,
                ["elementId"] = output.ElementId.Value,
                ["assetPath"] = output.AssetPath,
                ["guid"] = output.Guid,
                ["localFileId"] = output.LocalFileId.ToString(CultureInfo.InvariantCulture)
            }))
        });

        public static AuthoringRootDocument ReadRoot(string text, string sourcePath)
        {
            JObject root = ReadDocument(text, sourcePath);
            return new AuthoringRootDocument(Id(root, "id", sourcePath),
                new GraphAuthoringDomainId(Text(root, "domain", sourcePath)), Text(root, "kind", sourcePath),
                Text(root, "definitionVersion", sourcePath), Text(root, "displayName", sourcePath),
                ReadFields(Required(root, "fields", sourcePath), sourcePath),
                Array(root, "files", sourcePath).Select(file => new AuthoringDocumentFile(
                    Id(file, "id", sourcePath), Enumeration<AuthoringDocumentKind>(file, "kind", sourcePath),
                    Text(file, "path", sourcePath))).ToArray(),
                Array(root, "externalReferences", sourcePath).Select(value => ReadReference(value, sourcePath)).ToArray(),
                Array(root, "unityOutputs", sourcePath).Select(value => new AuthoringUnityOutput(
                    Id(value, "documentId", sourcePath), Id(value, "elementId", sourcePath),
                    Text(value, "assetPath", sourcePath), Text(value, "guid", sourcePath),
                    long.Parse(Text(value, "localFileId", sourcePath), CultureInfo.InvariantCulture))).ToArray());
        }

        public static string WriteGraph(AuthoringGraphDocument graph) => Write(new JObject
        {
            ["formatVersion"] = AuthoringRootDocument.FormatVersion,
            ["id"] = graph.Id.Value,
            ["role"] = graph.RoleId.Value,
            ["owner"] = WriteReference(graph.Owner),
            ["displayName"] = graph.DisplayName,
            ["definitionVersion"] = graph.DefinitionVersion,
            ["fields"] = WriteFields(graph.Fields),
            ["nodes"] = new JArray(graph.Nodes.ToArray().Select(node => new JObject
            {
                ["id"] = node.Id.Value,
                ["capability"] = node.CapabilityId.Value,
                ["displayName"] = node.DisplayName,
                ["fields"] = WriteFields(node.Fields),
                ["dynamicPorts"] = new JArray(node.DynamicPorts.ToArray().Select(port => new JObject
                {
                    ["id"] = port.Id.Value,
                    ["displayName"] = port.DisplayName,
                    ["valueType"] = port.ValueTypeId,
                    ["direction"] = port.Direction.ToString(),
                    ["capacity"] = port.Capacity.ToString(),
                    ["interfacePortId"] = port.InterfacePortId
                }))
            })),
            ["edges"] = new JArray(graph.Edges.ToArray().Select(edge => new JObject
            {
                ["id"] = edge.Id.Value,
                ["sourceNode"] = edge.SourceNodeId.Value,
                ["sourcePort"] = edge.SourcePortId.Value,
                ["targetNode"] = edge.TargetNodeId.Value,
                ["targetPort"] = edge.TargetPortId.Value,
                ["order"] = edge.Order,
                ["fields"] = WriteFields(edge.Fields)
            })),
            ["variables"] = new JArray(graph.Variables.ToArray().Select(variable => new JObject
            {
                ["id"] = variable.Id.Value,
                ["key"] = variable.Key,
                ["type"] = variable.TypeId,
                ["declaration"] = WriteFields(variable.Declaration),
                ["defaultValue"] = ValueToken(variable.DefaultValue)
            }))
        });

        public static AuthoringGraphDocument ReadGraph(string text, string sourcePath)
        {
            JObject graph = ReadDocument(text, sourcePath);
            return new AuthoringGraphDocument(Id(graph, "id", sourcePath),
                new GraphAuthoringDocumentRoleId(Text(graph, "role", sourcePath)),
                ReadReference(Required(graph, "owner", sourcePath), sourcePath),
                Text(graph, "displayName", sourcePath), Text(graph, "definitionVersion", sourcePath),
                ReadFields(Required(graph, "fields", sourcePath), sourcePath),
                Array(graph, "nodes", sourcePath).Select(node => new AuthoringNodeDocument(
                    Id(node, "id", sourcePath), new GraphAuthoringCapabilityId(Text(node, "capability", sourcePath)),
                    Text(node, "displayName", sourcePath), ReadFields(Required(node, "fields", sourcePath), sourcePath),
                    Array(node, "dynamicPorts", sourcePath).Select(port => new AuthoringPortDocument(
                        new GraphAuthoringPortId(Text(port, "id", sourcePath)), Text(port, "displayName", sourcePath),
                        Text(port, "valueType", sourcePath), Enumeration<GraphAuthoringPortDirection>(port, "direction", sourcePath),
                        Enumeration<GraphAuthoringPortCapacity>(port, "capacity", sourcePath),
                        Text(port, "interfacePortId", sourcePath))).ToArray())).ToArray(),
                Array(graph, "edges", sourcePath).Select(edge => new AuthoringEdgeDocument(
                    Id(edge, "id", sourcePath), Id(edge, "sourceNode", sourcePath),
                    new GraphAuthoringPortId(Text(edge, "sourcePort", sourcePath)), Id(edge, "targetNode", sourcePath),
                    new GraphAuthoringPortId(Text(edge, "targetPort", sourcePath)),
                    Required(edge, "order", sourcePath).Value<int>(),
                    ReadFields(Required(edge, "fields", sourcePath), sourcePath))).ToArray(),
                Array(graph, "variables", sourcePath).Select(variable => new AuthoringVariableDocument(
                    Id(variable, "id", sourcePath), Text(variable, "key", sourcePath), Text(variable, "type", sourcePath),
                    ReadFields(Required(variable, "declaration", sourcePath), sourcePath),
                    ReadValue(Required(variable, "defaultValue", sourcePath), sourcePath))).ToArray());
        }

        public static string WriteValue(AuthoringValue value) => Write(ValueToken(value));
        public static AuthoringValue ReadValue(string text, string sourcePath) => ReadValue(ReadObject(text), sourcePath);

        static JToken ValueToken(AuthoringValue value)
        {
            var token = new JObject { ["type"] = value.TypeId };
            switch (value)
            {
                case AuthoringNullValue:
                    token["kind"] = "null";
                    break;
                case AuthoringScalarValue scalar:
                    token["kind"] = "scalar";
                    token["valueKind"] = scalar.Kind.ToString();
                    token["value"] = scalar.Value;
                    break;
                case AuthoringReferenceValue reference:
                    token["kind"] = "reference";
                    token["target"] = WriteReference(reference.Target);
                    break;
                case AuthoringObjectValue composite:
                    token["kind"] = "object";
                    token["members"] = WriteFields(composite.Members);
                    break;
                case AuthoringSequenceValue sequence:
                    token["kind"] = "sequence";
                    token["items"] = new JArray(sequence.Items.ToArray().Select(ValueToken));
                    break;
                default:
                    throw new ArgumentException($"作者值类型 '{value.GetType()}' 没有正式文档表示。", nameof(value));
            }
            return token;
        }

        static AuthoringValue ReadValue(JToken token, string sourcePath)
        {
            string type = Text(token, "type", sourcePath);
            string kind = Text(token, "kind", sourcePath);
            return kind switch
            {
                "null" => new AuthoringNullValue(type),
                "scalar" => new AuthoringScalarValue(type,
                    Enumeration<GraphAuthoringFieldValueKind>(token, "valueKind", sourcePath),
                    Text(token, "value", sourcePath)),
                "reference" => new AuthoringReferenceValue(type, ReadReference(Required(token, "target", sourcePath), sourcePath)),
                "object" => new AuthoringObjectValue(type, ReadFields(Required(token, "members", sourcePath), sourcePath)),
                "sequence" => new AuthoringSequenceValue(type,
                    Array(token, "items", sourcePath).Select(item => ReadValue(item, sourcePath)).ToArray()),
                _ => throw new AuthoringDocumentFormatException(sourcePath, token.Path + ".kind", $"未知作者值种类 '{kind}'。")
            };
        }

        static JObject WriteFields(ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            var result = new JObject();
            foreach (AuthoringNamedValue field in fields.ToArray().OrderBy(value => value.FieldId.Value, StringComparer.Ordinal))
                result.Add(field.FieldId.Value, ValueToken(field.Value));
            return result;
        }

        static AuthoringNamedValue[] ReadFields(JToken token, string sourcePath)
        {
            if (token is not JObject fields)
                throw new AuthoringDocumentFormatException(sourcePath, token.Path, "字段集合必须是对象。");
            return fields.Properties().Select(field => new AuthoringNamedValue(
                new GraphAuthoringFieldId(field.Name), ReadValue(field.Value, sourcePath))).ToArray();
        }

        static JObject WriteReference(AuthoringContentReference reference) => new JObject
        {
            ["rootId"] = reference.RootId.Value,
            ["documentId"] = reference.DocumentId.Value,
            ["elementId"] = reference.ElementId.Value
        };

        static AuthoringContentReference ReadReference(JToken token, string sourcePath) => new AuthoringContentReference(
            Id(token, "rootId", sourcePath), Id(token, "documentId", sourcePath), Id(token, "elementId", sourcePath));

        static JObject ReadDocument(string text, string sourcePath)
        {
            JObject document = ReadObject(text);
            int version = Required(document, "formatVersion", sourcePath).Value<int>();
            if (version != AuthoringRootDocument.FormatVersion)
                throw new AuthoringDocumentFormatException(sourcePath, "formatVersion", $"不支持作者文档版本 '{version}'。");
            return document;
        }

        static JObject ReadObject(string text)
        {
            using var source = new StringReader(text);
            using var reader = new JsonTextReader(source) { DateParseHandling = DateParseHandling.None };
            var document = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            while (reader.Read()) { }
            return document;
        }

        static GraphAuthoringElementId Id(JToken token, string name, string sourcePath) =>
            new GraphAuthoringElementId(Text(token, name, sourcePath));

        static string Text(JToken token, string name, string sourcePath)
        {
            JToken value = Required(token, name, sourcePath);
            if (value.Type != JTokenType.String)
                throw new AuthoringDocumentFormatException(sourcePath, value.Path, "必须使用文本，数值原值不能经过 JSON Number 转换。");
            return value.Value<string>();
        }

        static T Enumeration<T>(JToken token, string name, string sourcePath) where T : struct, Enum
        {
            string value = Text(token, name, sourcePath);
            if (!Enum.TryParse(value, false, out T parsed) || !Enum.IsDefined(typeof(T), parsed))
                throw new AuthoringDocumentFormatException(sourcePath, token.Path + "." + name, $"未知 {typeof(T).Name} 值 '{value}'。");
            return parsed;
        }

        static JArray Array(JToken token, string name, string sourcePath)
        {
            JToken value = Required(token, name, sourcePath);
            return value as JArray ?? throw new AuthoringDocumentFormatException(sourcePath, value.Path, "必须使用数组。");
        }

        static JToken Required(JToken token, string name, string sourcePath) =>
            token[name] ?? throw new AuthoringDocumentFormatException(sourcePath, token.Path + "." + name, "缺少必需字段。");

        static string Write(JToken token)
        {
            using var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using var writer = new JsonTextWriter(output) { Formatting = Formatting.Indented, Indentation = 2 };
            token.WriteTo(writer);
            writer.Flush();
            return output + "\n";
        }
    }
}
