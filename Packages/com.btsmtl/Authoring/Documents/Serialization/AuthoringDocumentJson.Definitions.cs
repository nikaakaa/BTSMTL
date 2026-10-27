using System;
using System.Linq;
using BTSMTL.Authoring.Graph;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Documents
{
    public static partial class AuthoringDocumentJson
    {
        public static string WriteValueDefinition(AuthoringValueDefinition definition) => Write(DefinitionToken(definition));
        public static AuthoringValueDefinition ReadValueDefinition(string text, string sourcePath) =>
            ReadDefinition(ReadObject(text), sourcePath);

        public static string WriteFieldDefinition(AuthoringFieldDefinition field) => Write(new JObject
        {
            ["id"] = field.Id.Value, ["displayName"] = field.DisplayName,
            ["valueType"] = field.ValueDefinition.TypeId, ["valueKind"] = field.ValueKind.ToString(),
            ["visible"] = field.Visible, ["writable"] = field.Writable, ["optional"] = field.Optional,
            ["pickerKind"] = field.PickerKind, ["constraint"] = new JObject
            {
                ["Minimum"] = field.Constraint.Minimum, ["Maximum"] = field.Constraint.Maximum,
                ["Finite"] = field.Constraint.Finite, ["NonEmpty"] = field.Constraint.NonEmpty,
                ["AllowedValues"] = new JArray(field.Constraint.AllowedValues)
            },
            ["visibility"] = field.Visibility == null ? JValue.CreateNull() : new JObject
            {
                ["fieldId"] = field.Visibility.ControllerFieldId.Value, ["equals"] = field.Visibility.ExpectedValue
            },
            ["compilation"] = new JObject { ["impact"] = field.Compilation.Impact.ToString(), ["unit"] = field.Compilation.Unit },
            ["defaultValue"] = ValueToken(field.DefaultValue), ["valueDefinition"] = DefinitionToken(field.ValueDefinition)
        });

        public static AuthoringFieldDefinition ReadFieldDefinition(string text, string sourcePath)
        {
            JObject field = ReadObject(text);
            JToken constraint = Required(field, "constraint", sourcePath);
            JToken visibility = Required(field, "visibility", sourcePath);
            JToken compilation = Required(field, "compilation", sourcePath);
            return new(new GraphAuthoringFieldId(Text(field, "id", sourcePath)), Text(field, "displayName", sourcePath),
                Enumeration<GraphAuthoringFieldValueKind>(field, "valueKind", sourcePath),
                ReadDefinition(Required(field, "valueDefinition", sourcePath), sourcePath),
                ReadValue(Required(field, "defaultValue", sourcePath), sourcePath),
                Required(field, "visible", sourcePath).Value<bool>(), Required(field, "writable", sourcePath).Value<bool>(),
                Required(field, "optional", sourcePath).Value<bool>(), Text(field, "pickerKind", sourcePath),
                new GraphAuthoringFieldConstraint(constraint["Minimum"].Value<double?>(), constraint["Maximum"].Value<double?>(),
                    constraint["Finite"].Value<bool>(), constraint["NonEmpty"].Value<bool>(),
                    Array(constraint, "AllowedValues", sourcePath).Select(value => value.Value<string>()).ToArray()),
                visibility.Type == JTokenType.Null ? null : new GraphAuthoringFieldVisibilityCondition(
                    new GraphAuthoringFieldId(Text(visibility, "fieldId", sourcePath)), Text(visibility, "equals", sourcePath)),
                new GraphAuthoringFieldCompilation(Enumeration<GraphAuthoringFieldCompilationImpact>(compilation, "impact", sourcePath),
                    Text(compilation, "unit", sourcePath)));
        }

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
