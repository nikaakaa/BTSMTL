using System;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationExtensionFieldKind : byte
    {
        Value = 1,
        Text = 2,
        Enum = 3,
        Identity = 4,
        Data = 5
    }

    public enum OrchestrationExtensionFieldCardinality : byte
    {
        Single = 1,
        Multiple = 2
    }

    public readonly struct OrchestrationExtensionContentField : IEquatable<OrchestrationExtensionContentField>
    {
        public OrchestrationExtensionContentField(string id, OrchestrationExtensionFieldKind kind,
            OrchestrationValueKind valueKind, string typeIdentity, string identityDomainId,
            OrchestrationExtensionFieldCardinality cardinality, bool required)
        {
            Id = id;
            Kind = kind;
            ValueKind = valueKind;
            TypeIdentity = typeIdentity;
            IdentityDomainId = identityDomainId;
            Cardinality = cardinality;
            Required = required;
        }

        public string Id { get; }
        public OrchestrationExtensionFieldKind Kind { get; }
        public OrchestrationValueKind ValueKind { get; }
        public string TypeIdentity { get; }
        public string IdentityDomainId { get; }
        public OrchestrationExtensionFieldCardinality Cardinality { get; }
        public bool Required { get; }

        public static OrchestrationExtensionContentField Value(string id, OrchestrationValueKind valueKind,
            bool required = true) => new OrchestrationExtensionContentField(id,
                OrchestrationExtensionFieldKind.Value, valueKind, string.Empty, string.Empty,
                OrchestrationExtensionFieldCardinality.Single, required);

        public static OrchestrationExtensionContentField Text(string id, bool required = true) =>
            new OrchestrationExtensionContentField(id, OrchestrationExtensionFieldKind.Text,
                default, string.Empty, string.Empty, OrchestrationExtensionFieldCardinality.Single, required);

        public static OrchestrationExtensionContentField Enum<TEnum>(string id, bool required = true)
            where TEnum : struct, System.Enum => new OrchestrationExtensionContentField(id,
                OrchestrationExtensionFieldKind.Enum, OrchestrationValueKind.Enum,
                typeof(TEnum).AssemblyQualifiedName, string.Empty,
                OrchestrationExtensionFieldCardinality.Single, required);

        public static OrchestrationExtensionContentField Identity(string id, string domainId,
            bool required = true) => new OrchestrationExtensionContentField(id,
                OrchestrationExtensionFieldKind.Identity, OrchestrationValueKind.Identity,
                string.Empty, domainId, OrchestrationExtensionFieldCardinality.Single, required);

        public static OrchestrationExtensionContentField Identities(string id, string domainId,
            bool required = true) => new OrchestrationExtensionContentField(id,
                OrchestrationExtensionFieldKind.Identity, OrchestrationValueKind.Identity,
                string.Empty, domainId, OrchestrationExtensionFieldCardinality.Multiple, required);

        public static OrchestrationExtensionContentField Data(string id, string typeIdentity,
            bool required = true) => new OrchestrationExtensionContentField(id,
                OrchestrationExtensionFieldKind.Data, default, typeIdentity, string.Empty,
                OrchestrationExtensionFieldCardinality.Single, required);

        public bool Equals(OrchestrationExtensionContentField other) =>
            string.Equals(Id, other.Id, StringComparison.Ordinal) && Kind == other.Kind &&
            ValueKind == other.ValueKind &&
            string.Equals(TypeIdentity, other.TypeIdentity, StringComparison.Ordinal) &&
            string.Equals(IdentityDomainId, other.IdentityDomainId, StringComparison.Ordinal) &&
            Cardinality == other.Cardinality && Required == other.Required;
    }

    public interface IOrchestrationExtensionContentReader<TValue> where TValue : struct
    {
        TValue ReadValue(in OrchestrationExtensionContentField field);
        string ReadText(in OrchestrationExtensionContentField field);
        TEnum ReadEnum<TEnum>(in OrchestrationExtensionContentField field) where TEnum : struct, Enum;
        string ReadIdentity(in OrchestrationExtensionContentField field);
        ReadOnlySpan<string> ReadIdentities(in OrchestrationExtensionContentField field);
        TData ReadData<TData>(in OrchestrationExtensionContentField field);
    }

    public interface IOrchestrationExtensionContentWriter<TValue> where TValue : struct
    {
        void WriteValue(in OrchestrationExtensionContentField field, in TValue value);
        void WriteText(in OrchestrationExtensionContentField field, string value);
        void WriteEnum<TEnum>(in OrchestrationExtensionContentField field, TEnum value) where TEnum : struct, Enum;
        void WriteIdentity(in OrchestrationExtensionContentField field, string value);
        void WriteIdentities(in OrchestrationExtensionContentField field, ReadOnlySpan<string> values);
        void WriteData<TData>(in OrchestrationExtensionContentField field, TData value);
    }
}
