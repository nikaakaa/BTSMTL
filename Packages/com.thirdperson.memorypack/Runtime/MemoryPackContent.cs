using System;
using System.Buffers;
using MemoryPack;

namespace ThirdPersonSimulation.Serialization
{
    public interface IMemoryPackSchema
    {
        StableHash SchemaHash { get; }
    }

    public interface IMemoryPackPreparedFormatter<TValue, TStorage>
    {
        TValue ReadInto(ref MemoryPackReader reader, TStorage storage);
    }

    public enum MemoryPackContentPart
    {
        Full,
        Layout,
        Payload
    }

    public sealed class MemoryPackContentContext : IServiceProvider
    {
        internal MemoryPackContentContext(MemoryPackContentPart part)
        {
            Part = part;
            Options = new MemoryPackSerializerOptions { ServiceProvider = this };
        }

        public MemoryPackContentPart Part { get; set; }
        public MemoryPackSerializerOptions Options { get; }

        public void BeginRead() => Part = MemoryPackContentPart.Full;

        public object GetService(Type serviceType) => serviceType == typeof(MemoryPackContentContext) ? this : null;
    }

    public static partial class MemoryPackContent
    {
        static MemoryPackContent()
        {
            MemoryPackGeneratedFormatters.Register();
            MemoryPackFormatterProvider.Register(new TimelineContentParametersFormatter());
        }

        public static void Initialize() { }

        public static StableHash GetSchemaHash<T>()
        {
            MemoryPackReader reader = default;
            return ((IMemoryPackSchema)reader.GetFormatter<T>()).SchemaHash;
        }

        public static StableHash GetSnapshotSchemaHash(IValueSnapshotCopy value)
        {
            MemoryPackReader reader = default;
            return ((IMemoryPackSchema)reader.GetFormatter(value.GetType())).SchemaHash;
        }

        public static void SerializeInto<T, TBufferWriter>(in T value, TBufferWriter destination,
            MemoryPackContentContext context) where TBufferWriter : class, IBufferWriter<byte>
        {
            context.Part = MemoryPackContentPart.Full;
            using var state = MemoryPackWriterOptionalStatePool.Rent(context.Options);
            var writer = new MemoryPackWriter<TBufferWriter>(ref destination, state);
            MemoryPackSerializer.Serialize(ref writer, in value);
        }

        public static T DeserializeInto<T, TStorage>(ReadOnlySpan<byte> bytes, TStorage storage,
            MemoryPackContentContext context)
        {
            context.BeginRead();
            using var state = MemoryPackReaderOptionalStatePool.Rent(context.Options);
            var reader = new MemoryPackReader(bytes, state);
            try
            {
                T value = ((IMemoryPackPreparedFormatter<T, TStorage>)reader.GetFormatter<T>())
                    .ReadInto(ref reader, storage);
                if (reader.Remaining != 0)
                    throw new System.IO.InvalidDataException("MemoryPack content contains trailing data.");
                return value;
            }
            finally { reader.Dispose(); }
        }

        public static byte[] Serialize<T>(T value, MemoryPackContentPart part = MemoryPackContentPart.Full)
            => Serialize(value, new MemoryPackContentContext(part));

        internal static byte[] Serialize<T>(T value, IServiceProvider services)
        {
            var options = new MemoryPackSerializerOptions { ServiceProvider = services };
            using var state = MemoryPackWriterOptionalStatePool.Rent(options);
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new MemoryPackWriter<ArrayBufferWriter<byte>>(ref bytes, state);
            writer.WriteValue(value);
            writer.Flush();
            return bytes.WrittenSpan.ToArray();
        }

        public static T Deserialize<T>(ReadOnlySpan<byte> bytes)
        {
            T value = default;
            Deserialize(bytes, ref value, MemoryPackContentPart.Full);
            return value;
        }

        public static T DeserializeParts<T>(ReadOnlySpan<byte> layout, ReadOnlySpan<byte> payload)
        {
            T value = default;
            Deserialize(layout, ref value, MemoryPackContentPart.Layout);
            Deserialize(payload, ref value, MemoryPackContentPart.Payload);
            return value;
        }

        static void Deserialize<T>(ReadOnlySpan<byte> bytes, ref T value, MemoryPackContentPart part)
        {
            var options = new MemoryPackSerializerOptions { ServiceProvider = new MemoryPackContentContext(part) };
            using var state = MemoryPackReaderOptionalStatePool.Rent(options);
            var reader = new MemoryPackReader(bytes, state);
            try
            {
                reader.ReadValue(ref value);
                if (reader.Remaining != 0)
                    throw new System.IO.InvalidDataException("MemoryPack content contains trailing data.");
            }
            finally { reader.Dispose(); }
        }
    }
}
