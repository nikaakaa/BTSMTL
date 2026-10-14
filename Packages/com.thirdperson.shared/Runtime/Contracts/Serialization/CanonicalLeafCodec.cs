using System;

namespace ThirdPersonSimulation
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class CanonicalExternalLeafAttribute : Attribute
    {
        public CanonicalExternalLeafAttribute(Type valueType, Type codecType)
        {
            ValueType = valueType;
            CodecType = codecType;
        }

        public Type ValueType { get; }
        public Type CodecType { get; }
    }

    public interface ICanonicalLeafCodec<TValue> where TValue : struct
    {
        string SchemaIdentity { get; }
        int EncodedSize { get; }
        void Write<TWriter>(TWriter writer, in TValue value) where TWriter : class, ICanonicalValueWriter;
        TValue Read<TReader>(TReader reader) where TReader : class, ICanonicalValueReader;
    }

    public readonly struct CanonicalRecordLeafCodec<TValue> : ICanonicalLeafCodec<TValue>
        where TValue : unmanaged, ICanonicalRecord<TValue>
    {
        public string SchemaIdentity => default(TValue).CanonicalSchemaIdentity;
        public int EncodedSize => default(TValue).CanonicalRecordMinimumSize;

        public void Write<TWriter>(TWriter writer, in TValue value) where TWriter : class, ICanonicalValueWriter =>
            value.WriteCanonical(writer);

        public TValue Read<TReader>(TReader reader) where TReader : class, ICanonicalValueReader =>
            default(TValue).ReadCanonical(reader);
    }
}
