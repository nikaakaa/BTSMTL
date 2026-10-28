using System.Text.Json;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.WebWorkbench;

public static class AuthoringPortProjection
{
    public static AuthoringProjectedNodePorts Project(JsonElement definition, AuthoringNodeDocument node)
    {
        try
        {
            return new(node.Id.Value, ProjectComplete(definition, node)
                .Select(port => new AuthoringProjectedPort(port.PortId.Value, port.DisplayName, port.ValueTypeId,
                    port.Direction.ToString(), port.Capacity.ToString(), port.Required, port.Order,
                    port.InterfacePortId)).ToArray(), null);
        }
        catch (GraphAuthoringPortShapeException error)
        {
            return new(node.Id.Value, Array.Empty<AuthoringProjectedPort>(), new(error.Code, error.Message));
        }
    }

    public static IReadOnlyList<GraphAuthoringDynamicPortProjection> ProjectComplete(JsonElement definition,
        AuthoringNodeDocument node)
    {
        var shape = new GraphAuthoringPortShapeDefinition(node.CapabilityId,
            definition.GetProperty("ports").EnumerateArray().Select(ReadPort).ToArray(),
            definition.GetProperty("portVariants").EnumerateArray().Select(variant =>
            {
                JsonElement when = variant.GetProperty("when");
                return new GraphAuthoringPortVariantDescriptor(variant.GetProperty("id").GetString()!,
                    new GraphAuthoringPortVariantCondition(new(when.GetProperty("fieldId").GetString()!),
                        Enum.Parse<GraphAuthoringFieldValueKind>(when.GetProperty("valueKind").GetString()!),
                        when.GetProperty("equals").GetString()!),
                    variant.GetProperty("ports").EnumerateArray().Select(ReadPort).ToArray());
            }).ToArray(),
            Enum.Parse<GraphAuthoringDynamicPortPolicy>(definition.GetProperty("dynamicPortPolicy").GetString()!));
        var declarations = definition.GetProperty("fields").EnumerateArray().ToDictionary(
            field => new GraphAuthoringFieldId(field.GetProperty("id").GetString()!));
        var fields = declarations.ToDictionary(field => field.Key,
            field => AuthoringDocumentJson.ReadValue(field.Value.GetProperty("defaultValue").GetRawText(),
                node.CapabilityId.Value));
        foreach (AuthoringNamedValue field in node.Fields.Span)
            fields[field.FieldId] = field.Value;
        var discriminators = shape.PortVariants.Select(variant => variant.When.FieldId).ToHashSet();
        discriminators.UnionWith(shape.FixedPorts.Concat(shape.PortVariants.SelectMany(variant => variant.Ports))
            .Select(port => port.ValueTypeFieldId).Where(field => field.IsValid));
        GraphAuthoringTypedPropertyValue[] properties = fields.Where(field => discriminators.Contains(field.Key) &&
                field.Value is AuthoringScalarValue)
            .Select(field => new GraphAuthoringTypedPropertyValue(field.Key,
                Enum.Parse<GraphAuthoringFieldValueKind>(declarations[field.Key].GetProperty("valueKind").GetString()!),
                ((AuthoringScalarValue)field.Value).Value)).ToArray();
        GraphAuthoringDynamicPortProjection[] authored = node.DynamicPorts.ToArray().Select((port, index) =>
            new GraphAuthoringDynamicPortProjection(port.Id, port.DisplayName, port.ValueTypeId,
                port.Direction, port.Capacity, false, index, port.InterfacePortId)).ToArray();
        return GraphAuthoringNodePortShapeProjector.ProjectComplete(shape, properties, authored);
    }

    static GraphAuthoringPortDescriptor ReadPort(JsonElement port) => new(
        new(port.GetProperty("id").GetString()!), port.GetProperty("displayName").GetString()!,
        port.GetProperty("valueType").GetString()!,
        Enum.Parse<GraphAuthoringPortDirection>(port.GetProperty("direction").GetString()!),
        Enum.Parse<GraphAuthoringPortCapacity>(port.GetProperty("capacity").GetString()!),
        port.GetProperty("required").GetBoolean(), port.GetProperty("order").GetInt32(),
        port.GetProperty("interfacePortId").GetString()!,
        port.GetProperty("valueTypeFieldId").GetString() is string field ? new(field) : default);
}
