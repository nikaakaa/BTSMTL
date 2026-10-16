using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace ThirdPersonGameplay.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct GameplayOrchestrationField<TValue, TScalar>
        where TValue : unmanaged
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        public GameplayOrchestrationField(string id, OrchestrationExtensionFieldKind kind,
            OrchestrationExtensionFieldCardinality cardinality,
            TValue value, string text, long enumValue, ReadOnlyMemory<string> identities,
            GameplayAbilityCurve<TScalar> curve, string typeIdentity, string identityDomainId)
        {
            Id = id;
            Kind = kind;
            Cardinality = cardinality;
            Value = value;
            Text = text;
            EnumValue = enumValue;
            Identities = identities;
            Curve = curve;
            TypeIdentity = typeIdentity;
            IdentityDomainId = identityDomainId;
        }

        public string Id { get; }
        public OrchestrationExtensionFieldKind Kind { get; }
        public OrchestrationExtensionFieldCardinality Cardinality { get; }
        public TValue Value { get; }
        public string Text { get; }
        public long EnumValue { get; }
        public ReadOnlyMemory<string> Identities { get; }
        public GameplayAbilityCurve<TScalar> Curve { get; }
        public string TypeIdentity { get; }
        public string IdentityDomainId { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class GameplayOrchestrationPayload<TValue, TScalar>
        where TValue : unmanaged
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        public GameplayOrchestrationPayload(ReadOnlyMemory<GameplayOrchestrationField<TValue, TScalar>> fields)
        {
            Fields = fields;
        }

        public ReadOnlyMemory<GameplayOrchestrationField<TValue, TScalar>> Fields { get; }

        public GameplayOrchestrationContent<TValue, TScalar> Prepare(
            ReadOnlyMemory<OrchestrationExtensionContentField> fields, Func<TValue, OrchestrationValueKind> valueKind) =>
            new GameplayOrchestrationContent<TValue, TScalar>(this, fields, valueKind);
    }

    public sealed class GameplayOrchestrationContent<TValue, TScalar> : IOrchestrationExtensionContentReader<TValue>
        where TValue : unmanaged
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        readonly ReadOnlyMemory<GameplayOrchestrationField<TValue, TScalar>> m_Fields;
        readonly Dictionary<string, int> m_Indices;

        internal GameplayOrchestrationContent(GameplayOrchestrationPayload<TValue, TScalar> payload,
            ReadOnlyMemory<OrchestrationExtensionContentField> fields, Func<TValue, OrchestrationValueKind> valueKind)
        {
            m_Fields = payload.Fields;
            m_Indices = new Dictionary<string, int>(StringComparer.Ordinal);
            var declarations = new Dictionary<string, OrchestrationExtensionContentField>(StringComparer.Ordinal);
            foreach (ref readonly OrchestrationExtensionContentField declaration in fields.Span)
                declarations.Add(declaration.Id, declaration);
            for (int i = 0; i < m_Fields.Length; ++i)
            {
                ref readonly GameplayOrchestrationField<TValue, TScalar> field = ref m_Fields.Span[i];
                if (!declarations.TryGetValue(field.Id, out OrchestrationExtensionContentField declaration))
                    throw new InvalidOperationException($"Gameplay 扩展内容包含未声明字段 '{field.Id}'。");
                if (!m_Indices.TryAdd(field.Id, i))
                    throw new InvalidOperationException($"Gameplay 扩展内容重复声明字段 '{field.Id}'。");
                if (field.Kind != declaration.Kind)
                    throw new InvalidOperationException($"Gameplay 扩展字段 '{field.Id}' 的种类与合同不一致。");
                if (field.Cardinality != declaration.Cardinality)
                    throw new InvalidOperationException($"Gameplay 扩展字段 '{field.Id}' 的数量形式与合同不一致。");
                switch (declaration.Kind)
                {
                    case OrchestrationExtensionFieldKind.Value:
                        if (valueKind(field.Value) != declaration.ValueKind)
                            throw new InvalidOperationException($"Gameplay 扩展字段 '{field.Id}' 的数值类型与合同不一致。");
                        break;
                    case OrchestrationExtensionFieldKind.Enum:
                    case OrchestrationExtensionFieldKind.Data:
                        if (!string.Equals(field.TypeIdentity, declaration.TypeIdentity, StringComparison.Ordinal))
                            throw new InvalidOperationException($"Gameplay 扩展字段 '{field.Id}' 的类型身份与合同不一致。");
                        break;
                    case OrchestrationExtensionFieldKind.Identity:
                        if (!string.Equals(field.IdentityDomainId, declaration.IdentityDomainId, StringComparison.Ordinal))
                            throw new InvalidOperationException($"Gameplay 扩展字段 '{field.Id}' 的身份领域与合同不一致。");
                        break;
                }
            }
            foreach (ref readonly OrchestrationExtensionContentField declaration in fields.Span)
                if (declaration.Required && !m_Indices.ContainsKey(declaration.Id))
                    throw new InvalidOperationException($"Gameplay 扩展内容缺少必需字段 '{declaration.Id}'。");
        }

        public TValue ReadValue(in OrchestrationExtensionContentField field) => Field(in field).Value;
        public string ReadText(in OrchestrationExtensionContentField field) => Field(in field).Text;
        public TEnum ReadEnum<TEnum>(in OrchestrationExtensionContentField field) where TEnum : struct, Enum =>
            (TEnum)Enum.ToObject(typeof(TEnum), Field(in field).EnumValue);
        public string ReadIdentity(in OrchestrationExtensionContentField field) => Field(in field).Text;
        public ReadOnlySpan<string> ReadIdentities(in OrchestrationExtensionContentField field) => Field(in field).Identities.Span;
        public TData ReadData<TData>(in OrchestrationExtensionContentField field) => (TData)(object)Field(in field).Curve;

        ref readonly GameplayOrchestrationField<TValue, TScalar> Field(in OrchestrationExtensionContentField field) =>
            ref m_Fields.Span[m_Indices[field.Id]];
    }

    public sealed class GameplayOrchestrationPayloadWriter<TValue, TScalar> : IOrchestrationExtensionContentWriter<TValue>
        where TValue : unmanaged
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        readonly Dictionary<string, OrchestrationExtensionContentField> m_Declarations;
        readonly List<GameplayOrchestrationField<TValue, TScalar>> m_Fields = new List<GameplayOrchestrationField<TValue, TScalar>>();

        public GameplayOrchestrationPayloadWriter(ReadOnlyMemory<OrchestrationExtensionContentField> fields)
        {
            m_Declarations = new Dictionary<string, OrchestrationExtensionContentField>(StringComparer.Ordinal);
            foreach (ref readonly OrchestrationExtensionContentField field in fields.Span)
                m_Declarations.Add(field.Id, field);
        }

        public OrchestrationExtensionContentField GetField(string id) => m_Declarations[id];

        public void WriteValue(in OrchestrationExtensionContentField field, in TValue value) =>
            m_Fields.Add(new GameplayOrchestrationField<TValue, TScalar>(field.Id, OrchestrationExtensionFieldKind.Value,
                OrchestrationExtensionFieldCardinality.Single,
                value, null, 0, default, null, null, null));

        public void WriteText(in OrchestrationExtensionContentField field, string value) =>
            m_Fields.Add(new GameplayOrchestrationField<TValue, TScalar>(field.Id, OrchestrationExtensionFieldKind.Text,
                OrchestrationExtensionFieldCardinality.Single,
                default, value, 0, default, null, null, null));

        public void WriteEnum<TEnum>(in OrchestrationExtensionContentField field, TEnum value) where TEnum : struct, Enum =>
            WriteEnum(in field, typeof(TEnum).AssemblyQualifiedName, Convert.ToInt64(value));

        public void WriteEnum(in OrchestrationExtensionContentField field, string typeIdentity, long value) =>
            m_Fields.Add(new GameplayOrchestrationField<TValue, TScalar>(field.Id, OrchestrationExtensionFieldKind.Enum,
                OrchestrationExtensionFieldCardinality.Single,
                default, null, value, default, null, typeIdentity, null));

        public void WriteIdentity(in OrchestrationExtensionContentField field, string value) =>
            m_Fields.Add(new GameplayOrchestrationField<TValue, TScalar>(field.Id, OrchestrationExtensionFieldKind.Identity,
                OrchestrationExtensionFieldCardinality.Single,
                default, value, 0, default, null, null, field.IdentityDomainId));

        public void WriteIdentities(in OrchestrationExtensionContentField field, ReadOnlySpan<string> values) =>
            m_Fields.Add(new GameplayOrchestrationField<TValue, TScalar>(field.Id, OrchestrationExtensionFieldKind.Identity,
                OrchestrationExtensionFieldCardinality.Multiple,
                default, null, 0, values.ToArray(), null, null, field.IdentityDomainId));

        public void WriteData<TData>(in OrchestrationExtensionContentField field, TData value) =>
            m_Fields.Add(new GameplayOrchestrationField<TValue, TScalar>(field.Id, OrchestrationExtensionFieldKind.Data,
                OrchestrationExtensionFieldCardinality.Single,
                default, null, 0, default, (GameplayAbilityCurve<TScalar>)(object)value,
                GameplayOrchestrationPayloadContract.CurveTypeIdentity, null));

        public GameplayOrchestrationPayload<TValue, TScalar> Build() =>
            new GameplayOrchestrationPayload<TValue, TScalar>(m_Fields.ToArray());
    }

    public static class GameplayOrchestrationPayloadContract
    {
        public const string Identity = "thirdperson.gameplay.orchestration-payload/2";
        public const string CurveTypeIdentity = "thirdperson.gameplay.scalar-curve/1";

        public static readonly OrchestrationExtensionContentField ProviderOwner =
            OrchestrationExtensionContentField.Identity("ProviderOwner", "btsmtl.provider-owner");
        public static readonly OrchestrationExtensionContentField QueryAll =
            OrchestrationExtensionContentField.Identities("Query:All", "gameplay.tag");
        public static readonly OrchestrationExtensionContentField QueryAny =
            OrchestrationExtensionContentField.Identities("Query:Any", "gameplay.tag");
        public static readonly OrchestrationExtensionContentField QueryNone =
            OrchestrationExtensionContentField.Identities("Query:None", "gameplay.tag");
    }
}
