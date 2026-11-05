using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
    public sealed class AuthoringSchemaReference
    {
        public AuthoringSchemaReference(string kind, JValue value, string domain)
        { Kind = kind; Value = value; Domain = domain; }
        public string Kind { get; }
        public JValue Value { get; }
        public string Domain { get; }
    }

    public sealed class AuthoringSchemaReferences
    {
        readonly SkillSchemaModel m_Model;

        public AuthoringSchemaReferences(string workspace)
        {
            string root = Path.Combine(Path.GetFullPath(workspace), "Skills", "Schema", "skill.xml");
            string enums = Path.Combine(Path.GetDirectoryName(root), "Generated", "runtime-enums.xml");
            var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var schemas = new Dictionary<string, XDocument>(StringComparer.Ordinal);
            Load(root);
            var errors = new List<SkillSchemaError>();
            m_Model = SkillSchemaModel.Load(schemas, errors);
            if (errors.Count != 0)
                throw new InvalidDataException(string.Join("\n", errors.Select(error =>
                    error.File + error.JsonPath + ": " + error.Message)));
            Inputs = inputs;

            void Load(string path)
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                inputs.Add(path, text);
                XDocument document = XDocument.Parse(text, LoadOptions.SetLineInfo);
                schemas.Add(path, document);
                foreach (XElement import in document.Root.Elements("import"))
                {
                    string imported = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), import.Attribute("file").Value));
                    if (!string.Equals(imported, enums, StringComparison.OrdinalIgnoreCase))
                        Load(imported);
                }
            }
        }

        public IReadOnlyDictionary<string, string> Inputs { get; }

        public string FieldKind(string type, IReadOnlyList<string> path)
        {
            SkillSchemaVar field = Field(type, path);
            return field == null ? null : Kind(field);
        }

        public string FieldDomain(string type, IReadOnlyList<string> path) => Field(type, path)?.Tags["resource"];

        SkillSchemaVar Field(string type, IReadOnlyList<string> path)
        {
            SkillSchemaBean bean = m_Model.FindBean(type, "Btsmtl.Skill");
            for (int index = 0; bean != null && index < path.Count; index++)
            {
                SkillSchemaVar field = bean.AncestorsAndSelf().SelectMany(owner => owner.Vars)
                    .SingleOrDefault(member => member.Name == path[index]);
                if (field == null || index == path.Count - 1)
                    return field;
                bean = m_Model.FindBean(field.Type.Element, field.Bean.Module);
            }
            return null;
        }

        public IReadOnlyList<AuthoringSchemaReference> Graph(JObject graph) =>
            Read(graph, RequireBean("Graph", "Btsmtl.Skill")).ToArray();

        SkillSchemaBean RequireBean(string name, string module) => m_Model.FindBean(name, module) ??
            throw new InvalidDataException($"作者类型 '{module}.{name}' 不在正式 Schema 中。");

        IEnumerable<AuthoringSchemaReference> Read(JToken value, SkillSchemaBean type)
        {
            if (value.Type == JTokenType.Null)
                yield break;
            if (value is JArray array)
            {
                foreach (JToken item in array)
                    foreach (AuthoringSchemaReference reference in Read(item, type))
                        yield return reference;
                yield break;
            }
            var data = (JObject)value;
            if (data["$type"] is JValue polymorphic)
                type = RequireBean((string)polymorphic, type.Module);
            foreach (SkillSchemaVar field in type.AncestorsAndSelf().SelectMany(bean => bean.Vars)
                         .Where(field => field.Group == "data"))
            {
                if (!data.TryGetValue(field.Name, StringComparison.Ordinal, out JToken stored))
                    continue;
                string kind = Kind(field);
                if (kind != null)
                {
                    if (stored is JValue scalar && scalar.Type is JTokenType.String or JTokenType.Null)
                        yield return new AuthoringSchemaReference(kind, scalar, field.Tags["resource"]);
                    else if (stored is JArray values)
                        foreach (JValue item in values.OfType<JValue>().Where(item => item.Type == JTokenType.String))
                            yield return new AuthoringSchemaReference(kind, item, field.Tags["resource"]);
                }
                SkillSchemaBean nested = m_Model.FindBean(field.Type.Element, field.Bean.Module);
                if (nested != null)
                    foreach (AuthoringSchemaReference reference in Read(stored, nested))
                        yield return reference;
            }
        }

        static string Kind(SkillSchemaVar field) => field.Tags["reference"] ?? field.Tags["asset"] ??
            field.Tags["resource"] ?? (field.Type.Reference == "Btsmtl.Skill.Graphs" ? "graph" : null);
    }
}
