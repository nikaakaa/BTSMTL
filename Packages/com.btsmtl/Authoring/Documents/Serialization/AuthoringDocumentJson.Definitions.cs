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

        public static string WriteObjectDefinition(AuthoringObjectDefinition definition) => Write(new JObject
        {
            ["authoringType"] = definition.TypeId,
            ["members"] = new JObject(definition.Members.OrderBy(member => member.Key, StringComparer.Ordinal)
                .Select(member => new JProperty(member.Key, member.Value.Value))),
            ["fields"] = new JArray(definition.Fields.OrderBy(field => field.Id.Value, StringComparer.Ordinal)
                .Select(field => JToken.Parse(WriteFieldDefinition(field))))
        });

        public static AuthoringObjectDefinition ReadObjectDefinition(string text, string sourcePath)
        {
            JObject definition = ReadObject(text);
            JToken declaredMembers = Required(definition, "members", sourcePath);
            if (declaredMembers is not JObject members)
                throw new AuthoringDocumentFormatException(sourcePath, declaredMembers.Path, "成员绑定必须使用 JSON Object。");
            var result = new AuthoringObjectDefinition(Text(definition, "authoringType", sourcePath), Array(definition, "fields", sourcePath)
                .Select(field => ReadFieldDefinition(field, sourcePath)).ToArray(),
                members.Properties().ToDictionary(member => member.Name,
                    member =>
                    {
                        member.AddAnnotation(ReadMember.Instance);
                        return new GraphAuthoringFieldId(Text(member.Value, sourcePath));
                    }, StringComparer.Ordinal));
            return ReceiveMembers(definition, result, sourcePath);
        }

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

        public static AuthoringFieldDefinition ReadFieldDefinition(string text, string sourcePath) =>
            ReadFieldDefinition(ReadObject(text), sourcePath);

        static AuthoringFieldDefinition ReadFieldDefinition(JToken token, string sourcePath)
        {
            if (token is not JObject field)
                throw new AuthoringDocumentFormatException(sourcePath, token.Path, "字段描述必须使用 JSON Object。");
            JToken constraint = Required(field, "constraint", sourcePath);
            JToken visibility = Required(field, "visibility", sourcePath);
            JToken compilation = Required(field, "compilation", sourcePath);
            JToken unit = Required(compilation, "unit", sourcePath);
            AuthoringValueDefinition value = ReadDefinition(Required(field, "valueDefinition", sourcePath), sourcePath);
            if (Text(field, "valueType", sourcePath) != value.TypeId)
                throw new AuthoringDocumentFormatException(sourcePath, field.Path + ".valueType", "字段类型与原值定义不一致。");
            var result = new AuthoringFieldDefinition(new GraphAuthoringFieldId(Text(field, "id", sourcePath)), Text(field, "displayName", sourcePath),
                Enumeration<GraphAuthoringFieldValueKind>(field, "valueKind", sourcePath),
                value,
                ReadValue(Required(field, "defaultValue", sourcePath), sourcePath),
                Boolean(field, "visible", sourcePath), Boolean(field, "writable", sourcePath),
                Boolean(field, "optional", sourcePath), Text(field, "pickerKind", sourcePath),
                new GraphAuthoringFieldConstraint(Required(constraint, "Minimum", sourcePath).Value<double?>(),
                    Required(constraint, "Maximum", sourcePath).Value<double?>(),
                    Boolean(constraint, "Finite", sourcePath), Boolean(constraint, "NonEmpty", sourcePath),
                    Array(constraint, "AllowedValues", sourcePath).Select(item => Text(item, sourcePath)).ToArray()),
                visibility.Type == JTokenType.Null ? null : new GraphAuthoringFieldVisibilityCondition(
                    new GraphAuthoringFieldId(Text(visibility, "fieldId", sourcePath)), Text(visibility, "equals", sourcePath)),
                new GraphAuthoringFieldCompilation(Enumeration<GraphAuthoringFieldCompilationImpact>(compilation, "impact", sourcePath),
                    unit.Type == JTokenType.Null ? null : Text(unit, sourcePath)),
                Enumeration<GraphAuthoringDocumentMember>(field, "documentMember", sourcePath));
            return ReceiveMembers(field, result, sourcePath);
        }

        public static string WriteNodeDefinition(AuthoringNodeDefinition node) => Write(new JObject
        {
            ["id"] = node.Id.Value, ["domain"] = node.Domain.Value,
            ["roles"] = new JArray(node.Roles.Select(role => role.Value).OrderBy(role => role, StringComparer.Ordinal)),
            ["displayName"] = node.DisplayName, ["category"] = node.Category,
            ["color"] = new JArray(node.Color.Red, node.Color.Green, node.Color.Blue, node.Color.Alpha),
            ["authoringType"] = node.AuthoringType, ["iconName"] = node.IconName,
            ["presentationKind"] = node.PresentationKind.ToString(),
            ["systemOwned"] = node.SystemOwned, ["anchorId"] = node.AnchorId,
            ["stateKind"] = node.StateKind.HasValue ? node.StateKind.Value.ToString() : JValue.CreateNull(),
            ["dynamicPortPolicy"] = node.PortShape.DynamicPortPolicy.ToString(),
            ["dynamicPortSource"] = node.DynamicPortSource.ToString(),
            ["fields"] = new JArray(node.Fields.Values.OrderBy(field => field.Id.Value, StringComparer.Ordinal)
                .Select(field => JToken.Parse(WriteFieldDefinition(field)))),
            ["ports"] = new JArray(node.PortShape.FixedPorts.OrderBy(port => port.Order)
                .ThenBy(port => port.PortId.Value, StringComparer.Ordinal).Select(DefinitionPort)),
            ["portVariants"] = new JArray(node.PortShape.PortVariants.Select(variant => new JObject
            {
                ["id"] = variant.VariantId, ["when"] = new JObject
                {
                    ["fieldId"] = variant.When.FieldId.Value, ["valueKind"] = variant.When.ValueKind.ToString(),
                    ["equals"] = variant.When.ExpectedValue
                },
                ["ports"] = new JArray(variant.Ports.OrderBy(port => port.Order).Select(DefinitionPort))
            })),
            ["childSurfaces"] = new JArray(node.ChildSurfaces.Select(child => new JObject
            {
                ["command"] = child.CommandId.Value, ["role"] = child.DocumentRoleId.Value, ["displayName"] = child.DisplayName
            })),
            ["commands"] = new JArray(node.Commands.Select(command => new JObject
            {
                ["id"] = command.CommandId.Value, ["displayName"] = command.DisplayName,
                ["destructive"] = command.Destructive, ["presentationKind"] = command.PresentationKind.ToString()
            }))
        });

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
                Required(node, "stateKind", sourcePath).Type == JTokenType.Null ? null : Enumeration<AuthoringStateKind>(node, "stateKind", sourcePath),
                Text(node, "displayName", sourcePath), Text(node, "category", sourcePath),
                ReadDefinitionColor(node, sourcePath), Text(node, "authoringType", sourcePath), Text(node, "iconName", sourcePath),
                Enumeration<GraphAuthoringNodePresentationKind>(node, "presentationKind", sourcePath),
                Array(node, "childSurfaces", sourcePath).Select(child => new GraphAuthoringChildSurfaceDescriptor(
                    new GraphAuthoringCommandId(Text(child, "command", sourcePath)),
                    new GraphAuthoringDocumentRoleId(Text(child, "role", sourcePath)), Text(child, "displayName", sourcePath))).ToArray(),
                Array(node, "commands", sourcePath).Select(command => new GraphAuthoringCommandDescriptor(
                    new GraphAuthoringCommandId(Text(command, "id", sourcePath)), Text(command, "displayName", sourcePath),
                    Required(command, "destructive", sourcePath).Value<bool>(),
                    Enumeration<GraphAuthoringCommandPresentationKind>(command, "presentationKind", sourcePath))).ToArray());
        }

        static GraphAuthoringColor ReadDefinitionColor(JToken node, string sourcePath)
        {
            JArray color = Array(node, "color", sourcePath);
            if (color.Count != 4)
                throw new AuthoringDocumentFormatException(sourcePath, color.Path, "作者颜色必须包含四个通道。");
            return new(color[0].Value<float>(), color[1].Value<float>(), color[2].Value<float>(), color[3].Value<float>());
        }

        static JObject DefinitionPort(GraphAuthoringPortDescriptor port) => new JObject
        {
            ["id"] = port.PortId.Value, ["displayName"] = port.DisplayName, ["valueType"] = port.ValueTypeId,
            ["direction"] = port.Direction.ToString(), ["capacity"] = port.Capacity.ToString(),
            ["required"] = port.Required, ["order"] = port.Order, ["interfacePortId"] = port.InterfacePortId,
            ["valueTypeFieldId"] = port.ValueTypeFieldId.IsValid ? port.ValueTypeFieldId.Value : JValue.CreateNull()
        };

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
