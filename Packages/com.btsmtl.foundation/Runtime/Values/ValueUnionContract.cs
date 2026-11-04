using System;

namespace ThirdPersonSimulation
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueOperationsAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class ValuePayloadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class ValuePresenceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValueLogicalStorageAttribute : Attribute
    {
        public ValueLogicalStorageAttribute(string countMember, string restoreMethod)
        {
            CountMember = countMember;
            RestoreMethod = restoreMethod;
        }

        public string CountMember { get; }
        public string RestoreMethod { get; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueDefaultAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValueValidationAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValueHashAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueTextRulesAttribute : Attribute
    {
        public bool Required { get; set; }
        public bool Trim { get; set; }
        public bool NullToEmpty { get; set; }
        public bool Declared { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueStorageAttribute : Attribute
    {
        public bool Construct { get; set; }
        public bool UseConstructor { get; set; }
        public bool RecordedState { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueStorageMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueStorageIgnoreAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ValueSerializationPurposeAttribute : Attribute
    {
        public ValueSerializationPurposeAttribute(string name) => Name = name;
        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
    public sealed class ValueStorageProjectionAttribute : Attribute
    {
        public ValueStorageProjectionAttribute(string purpose) => Purpose = purpose;
        public string Purpose { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ValueStorageSourceAttribute : Attribute
    {
        public ValueStorageSourceAttribute(string path) => Path = path;
        public string Path { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ValueStorageContextAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ValueRecordedStateConstructorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Constructor)]
    public sealed class ValueRecordedStateAttribute : Attribute
    {
        public ValueRecordedStateAttribute(string name = "") => Name = name;
        public string Name { get; }
        public bool ReadInputs { get; set; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ValueRecordedStateContextAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class ValueStateLayoutAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValueStateLayoutFactoryAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ValueVariantAttribute : Attribute
    {
        public ValueVariantAttribute(ushort discriminator) => Discriminator = discriminator;
        public ushort Discriminator { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValueUpdateAttribute : Attribute { }

    public readonly struct ValueConstruction { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueUnionCaseAttribute : Attribute
    {
        public ValueUnionCaseAttribute() { }
        public ValueUnionCaseAttribute(Type payload) => Payload = payload;
        public Type Payload { get; }
        public bool NormalizeZero { get; set; }
        public bool NormalizeNull { get; set; }
        public string DefaultMember { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Enum)]
    public sealed class ValueUnionTemplateAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class ValueUnionAttribute : Attribute
    {
        public ValueUnionAttribute(Type definition) => Definition = definition;
        public Type Definition { get; }
        public System.Runtime.InteropServices.LayoutKind Layout { get; set; } = System.Runtime.InteropServices.LayoutKind.Auto;
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class ValueUnionAccessAttribute : Attribute
    {
        public ValueUnionAccessAttribute(Type union) => Union = union;
        public Type Union { get; }
    }
}
