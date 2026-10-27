using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Documents
{
    public static partial class AuthoringDocumentJson
    {
        public static string WriteValueDefinition(AuthoringValueDefinition definition) => Write(DefinitionToken(definition));
        public static AuthoringValueDefinition ReadValueDefinition(string text, string sourcePath) =>
            ReadDefinition(ReadObject(text), sourcePath);

        static JObject DefinitionToken(AuthoringValueDefinition definition) => new JObject
        {
            ["type"] = definition.TypeId,
            ["kind"] = definition.Kind.ToString(),
            ["scalarType"] = definition.ScalarType.ToString(),
            ["allowsNull"] = definition.AllowsNull,
            ["enumValues"] = new JArray(definition.EnumValues),
            ["flags"] = definition.Flags,
            ["members"] = new JArray(definition.Members.Select(member => new JObject
            {
                ["id"] = member.Id, ["definition"] = DefinitionToken(member.Definition)
            })),
            ["element"] = definition.Element == null ? JValue.CreateNull() : DefinitionToken(definition.Element)
        };

        static AuthoringValueDefinition ReadDefinition(JToken value, string sourcePath) => new(
            Text(value, "type", sourcePath), Enumeration<AuthoringValueDefinitionKind>(value, "kind", sourcePath),
            Enumeration<TypeCode>(value, "scalarType", sourcePath), Required(value, "allowsNull", sourcePath).Value<bool>(),
            Array(value, "enumValues", sourcePath).Select(item => item.Value<string>()).ToArray(),
            Required(value, "flags", sourcePath).Value<bool>(), Array(value, "members", sourcePath)
                .Select(member => new AuthoringValueMemberDefinition(Text(member, "id", sourcePath),
                    ReadDefinition(Required(member, "definition", sourcePath), sourcePath))).ToArray(),
            Required(value, "element", sourcePath).Type == JTokenType.Null ? null : ReadDefinition(value["element"], sourcePath));
    }
}
