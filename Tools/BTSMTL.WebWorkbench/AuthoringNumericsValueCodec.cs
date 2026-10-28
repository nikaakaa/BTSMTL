using System.Numerics;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.WebWorkbench;

public sealed class AuthoringNumericsValueCodec : AuthoringValueCodec
{
    readonly IReadOnlyDictionary<Type, AuthoringValueDefinition> m_Numerics;

    public AuthoringNumericsValueCodec(AuthoringValueDefinition vector2, AuthoringValueDefinition vector3,
        AuthoringValueDefinition quaternion)
    {
        m_Numerics = new Dictionary<Type, AuthoringValueDefinition>
        {
            [typeof(Vector2)] = vector2,
            [typeof(Vector3)] = vector3,
            [typeof(Quaternion)] = quaternion
        };
    }

    public override AuthoringValueDefinition Describe(Type type) =>
        m_Numerics.TryGetValue(type, out AuthoringValueDefinition? definition) ? definition : base.Describe(type);

    public override AuthoringValue Capture(Type type, object value)
    {
        if (type == typeof(Vector2))
        {
            var vector = (Vector2)value;
            return Components(type, ("x", vector.X), ("y", vector.Y));
        }
        if (type == typeof(Vector3))
        {
            var vector = (Vector3)value;
            return Components(type, ("x", vector.X), ("y", vector.Y), ("z", vector.Z));
        }
        if (type == typeof(Quaternion))
        {
            var rotation = (Quaternion)value;
            return Components(type, ("x", rotation.X), ("y", rotation.Y), ("z", rotation.Z), ("w", rotation.W));
        }
        return base.Capture(type, value);
    }

    public override object Restore(Type type, AuthoringValue value)
    {
        if (!m_Numerics.TryGetValue(type, out AuthoringValueDefinition? definition))
            return base.Restore(type, value);
        if (value is not AuthoringObjectValue composite || value.TypeId != definition.TypeId)
            throw new InvalidOperationException($"作者数值 '{value.TypeId}' 不属于正式 '{definition.TypeId}' 复合值。");
        var fields = composite.Members.ToArray().ToDictionary(member => member.FieldId.Value,
            member => member.Value, StringComparer.Ordinal);
        if (!fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(definition.Members.Select(member => member.Id)))
            throw new InvalidOperationException($"作者数值 '{value.TypeId}' 的分量与正式定义不一致。");
        float Component(string name) => (float)base.Restore(typeof(float), fields[name]);
        if (type == typeof(Vector2)) return new Vector2(Component("x"), Component("y"));
        if (type == typeof(Vector3)) return new Vector3(Component("x"), Component("y"), Component("z"));
        return new Quaternion(Component("x"), Component("y"), Component("z"), Component("w"));
    }

    AuthoringObjectValue Components(Type type, params (string Name, float Value)[] components) =>
        new(m_Numerics[type].TypeId, components.Select(component =>
            Member(component.Name, typeof(float), component.Value)).ToArray());
}
