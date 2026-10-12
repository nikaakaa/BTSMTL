using System;

namespace ThirdPersonSimulation
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class CanonicalContractAttribute : Attribute
    {
        public bool MaterializedRead { get; set; }
        public bool PublishedRead { get; set; }
        public bool PreparedRead { get; set; } = true;
        public Type PreparedStorage { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class CanonicalBorrowedSequenceAttribute : Attribute
    {
        public CanonicalBorrowedSequenceAttribute(Type storage) => Storage = storage;
        public Type Storage { get; }
        public bool HoldStorage { get; set; } = true;
        public bool RetainElementsOnRead { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalDeclaredTextAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalTextRulesAttribute : Attribute
    {
        public bool Required { get; set; }
        public bool Trim { get; set; }
        public bool NullToEmpty { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalPublishedAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CanonicalPresenceAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalRequiredAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CanonicalUnionCaseAttribute : Attribute
    {
        public bool NormalizeZero { get; set; }
        public bool NormalizeNull { get; set; }
        public string DefaultMember { get; set; }
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

    public readonly struct CanonicalUnavailable
    {
    }

    public readonly struct CanonicalConstruction
    {
    }

    public interface ICanonicalValueReader
    {
        int Remaining { get; }
        CanonicalMatchWriter MatchWriter { get; }
        CanonicalPublishedValues PublishedValues { get; }
        void RequireText(in CanonicalTextValue value);
        StableHash ReadHash();
        ArraySegment<byte> ReadRawBytesSegment(int length);
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
        string ReadDeclaredString();
        CanonicalTextValue ReadText();
        byte[] ReadBytes();
        void SkipBytes();
        void SkipText();
    }

    public interface ICanonicalRecord<T> where T : struct
    {
        string CanonicalSchemaIdentity { get; }
        int CanonicalRecordMinimumSize { get; }
        int CanonicalRecordMaximumTextReferences { get; }
        void WriteCanonical<TWriter>(TWriter writer) where TWriter : class, ICanonicalValueWriter;
        T ReadCanonical<TReader>(TReader reader) where TReader : class, ICanonicalValueReader;
        void SkipCanonical<TReader>(TReader reader) where TReader : class, ICanonicalValueReader;
        int MeasureCanonical();
        void RetainCanonical();
        void ReleaseCanonical();
        void VisitCanonicalTexts<TVisitor>(TVisitor visitor) where TVisitor : class, ICanonicalTextVisitor;
    }

    public interface ICanonicalTextVisitor
    {
        void Visit(in CanonicalTextValue value);
    }

    public sealed class CanonicalPublishedValues
    {
        readonly System.Collections.Generic.Dictionary<Type, object> m_Values =
            new System.Collections.Generic.Dictionary<Type, object>();

        public void Add<T>(T value) => m_Values.Add(typeof(T), value);
        public T Require<T>() => (T)m_Values[typeof(T)];
    }

    public static class CanonicalContractProtocol
    {
        public const int Version = 1;
        public const string GeneratorIdentity = "canonical-csharp/1";

        public static string ComposeSchema(string template, params string[] arguments)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(template + "|" + string.Join("|", arguments));
            using var hash = System.Security.Cryptography.SHA256.Create();
            byte[] digest = hash.ComputeHash(bytes);
            return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
