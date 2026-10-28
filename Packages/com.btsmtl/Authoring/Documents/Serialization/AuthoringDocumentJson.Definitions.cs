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
            ["documentMember"] = field.DocumentMember.ToString(),
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
                    compilation["unit"].Type == JTokenType.Null ? null : Text(compilation, "unit", sourcePath)),
                Enumeration<GraphAuthoringDocumentMember>(field, "documentMember", sourcePath));
        }

        public static AuthoringNodeDefinition ReadNodeDefinition(string text, string sourcePath)
        {
            JObject node = ReadObject(text);
            var id = new GraphAuthoringCapabilityId(Text(node, "id", sourcePath));
            return new(id, new GraphAuthoringDomainId(Text(node, "domain", sourcePath)),
                Array(node, "roles", sourcePath).Select(role => new GraphAuthoringDocumentRoleId(role.Value<string>())).ToArray(),
                Array(node, "fields", sourcePath).Select(field => ReadFieldDefinition(field.ToString(), sourcePath)).ToArray(),
                new GraphAuthoringPortShapeDefinition(id, Array(node, "ports", sourcePath).Select(port => ReadDefinitionPort(port, sourcePath)).ToArray(),
                    Array(node, "portVariants", sourcePath).Select(variant => new GraphAuthoringPortVariantDescriptor(
                        Text(variant, "id", sourcePath), new GraphAuthoringPortVariantCondition(
                            new GraphAuthoringFieldId(Text(variant["when"], "fieldId", sourcePath)),
                            Enumeration<GraphAuthoringFieldValueKind>(variant["when"], "valueKind", sourcePath), Text(variant["when"], "equals", sourcePath)),
                        Array(variant, "ports", sourcePath).Select(port => ReadDefinitionPort(port, sourcePath)).ToArray())).ToArray(),
                    Enumeration<GraphAuthoringDynamicPortPolicy>(node, "dynamicPortPolicy", sourcePath)),
                Enumeration<GraphAuthoringDynamicPortSource>(node, "dynamicPortSource", sourcePath),
                Required(node, "systemOwned", sourcePath).Value<bool>(), Text(node, "anchorId", sourcePath),
                Required(node, "stateKind", sourcePath).Type == JTokenType.Null ? null : Enumeration<AuthoringStateKind>(node, "stateKind", sourcePath));
        }

        static GraphAuthoringPortDescriptor ReadDefinitionPort(JToken port, string sourcePath) => new(
            new GraphAuthoringPortId(Text(port, "id", sourcePath)), Text(port, "displayName", sourcePath),
            Text(port, "valueType", sourcePath), Enumeration<GraphAuthoringPortDirection>(port, "direction", sourcePath),
            Enumeration<GraphAuthoringPortCapacity>(port, "capacity", sourcePath), Required(port, "required", sourcePath).Value<bool>(),
            Required(port, "order", sourcePath).Value<int>(), Text(port, "interfacePortId", sourcePath),
            Required(port, "valueTypeFieldId", sourcePath).Type == JTokenType.Null ? default :
                new GraphAuthoringFieldId(Text(port, "valueTypeFieldId", sourcePath)));

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
