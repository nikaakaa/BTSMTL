using System;

namespace ThirdPersonSimulation
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class CanonicalContractAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalUnionCaseAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class CanonicalUnionTemplateAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class CanonicalUnionAttribute : Attribute
    {
        public CanonicalUnionAttribute(Type definition) => Definition = definition;
        public Type Definition { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CanonicalValidationAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class CanonicalSchemaConstantAttribute : Attribute
    {
    }

    public readonly struct CanonicalConstruction
    {
    }

    public static class CanonicalContractProtocol
    {
        public const int Version = 1;
        public const string GeneratorIdentity = "canonical-csharp/1";
    }
}
