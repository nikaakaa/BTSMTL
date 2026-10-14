using System;

namespace ThirdPersonSimulation
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueOperationsAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class ValueUnionOmittedAttribute : Attribute
    {
    }

    [ValueUnionOmitted]
    public readonly struct CanonicalUnavailable
    {
    }

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
    }
}
