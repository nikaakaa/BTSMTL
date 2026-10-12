using System;

namespace ThirdPersonSimulation
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class CanonicalContractAttribute : Attribute
    {
        public bool MaterializedRead { get; set; }
        public bool PreparedRead { get; set; } = true;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalUnionCaseAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalPayloadAttribute : Attribute
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

    public interface ICanonicalValueReader
    {
        int Remaining { get; }
        byte ReadByte();
        sbyte ReadSByte();
        bool ReadBoolean();
        short ReadInt16();
        ushort ReadUInt16();
        char ReadChar();
        int ReadInt32();
        uint ReadUInt32();
        long ReadInt64();
        ulong ReadUInt64();
        float ReadSingleBits();
        double ReadDoubleBits();
        string ReadString();
        CanonicalTextValue ReadText();
        byte[] ReadBytes();
        void SkipBytes();
        void SkipText();
    }

    public interface ICanonicalTextVisitor
    {
        void Visit(in CanonicalTextValue value);
    }

    public static class CanonicalContractProtocol
    {
        public const int Version = 1;
        public const string GeneratorIdentity = "canonical-csharp/1";
    }
}
