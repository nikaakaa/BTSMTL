using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;

namespace ThirdPersonSimulation.Serialization
{
    public interface IMemoryPackSchema
    {
        StableHash SchemaHash { get; }
    }

    public enum MemoryPackContentPart
    {
        Full,
        Layout,
        Payload
    }

    public sealed class MemoryPackContentContext : IServiceProvider
    {
        readonly CanonicalTextReferences m_Texts;
        readonly char[] m_Characters;

        internal MemoryPackContentContext(MemoryPackContentPart part) => Part = part;

        public MemoryPackContentContext(CanonicalTextReferences texts,
            CanonicalDeclaredIdentityReader declaredIdentities, int maximumUtf16Characters)
        {
            m_Texts = texts;
            DeclaredIdentities = declaredIdentities;
            m_Characters = new char[maximumUtf16Characters];
            Options = new MemoryPackSerializerOptions { ServiceProvider = this };
        }

        public MemoryPackContentPart Part { get; set; }
        public CanonicalDeclaredIdentityReader DeclaredIdentities { get; set; }
        public bool IsPrepared => m_Texts != null;
        public object PreparedStorage { get; private set; }
        public MemoryPackSerializerOptions Options { get; }

        public void BeginRead(object storage)
        {
            Part = MemoryPackContentPart.Full;
            PreparedStorage = storage;
            m_Texts.Reset();
        }

        public void SelectPreparedStorage(object storage) => PreparedStorage = storage;

        public CanonicalTextValue ReadText(ref MemoryPackReader reader)
        {
            ReadOnlySpan<char> characters = ReadCharacters(ref reader, out bool isNull, out int byteCount);
            CanonicalTextValue value = isNull ? default : characters.IsEmpty ? CanonicalTextValue.Empty :
                m_Texts.CaptureCharacters(characters);
            reader.Advance(byteCount);
            return value;
        }

        public string ReadDeclaredText(ref MemoryPackReader reader)
        {
            ReadOnlySpan<char> characters = ReadCharacters(ref reader, out bool isNull, out int byteCount);
            string value = isNull ? null : DeclaredIdentities.ResolvePublished(characters);
            reader.Advance(byteCount);
            return value;
        }

        ReadOnlySpan<char> ReadCharacters(ref MemoryPackReader reader, out bool isNull, out int byteCount)
        {
            isNull = !reader.TryReadCollectionHeader(out int length);
            if (isNull)
            {
                byteCount = 0;
                return default;
            }
            if (length >= 0)
            {
                byteCount = checked(length * sizeof(char));
                ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref reader.GetSpanReference(byteCount), byteCount);
                return MemoryMarshal.Cast<byte, char>(bytes);
            }
            byteCount = ~length;
            reader.ReadUnmanaged(out int _);
            ReadOnlySpan<byte> utf8 = MemoryMarshal.CreateReadOnlySpan(ref reader.GetSpanReference(byteCount), byteCount);
            int characters = Encoding.UTF8.GetChars(utf8, m_Characters.AsSpan());
            return m_Characters.AsSpan(0, characters);
        }

        public object GetService(Type serviceType) => serviceType == typeof(MemoryPackContentContext) ? this : null;
    }

    public static partial class MemoryPackContent
    {
        static MemoryPackContent() => MemoryPackGeneratedFormatters.Register();

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
            MemoryPackSerializer.Serialize<T, TBufferWriter>(in destination, in value, context.Options);
        }

        public static FPGameplay.SimulationInput DeserializeInto(ReadOnlySpan<byte> bytes,
            FPGameplay.SimulationInputFrameStorage storage, MemoryPackContentContext context) =>
            DeserializeInto<FPGameplay.SimulationInput, FPGameplay.SimulationInputFrameStorage>(bytes, storage, context);

        public static SimulationInput DeserializeInto(ReadOnlySpan<byte> bytes,
            Float32SimulationInputFrameStorage storage, MemoryPackContentContext context) =>
            DeserializeInto<SimulationInput, Float32SimulationInputFrameStorage>(bytes, storage, context);

        public static T DeserializeInto<T, TStorage>(ReadOnlySpan<byte> bytes, TStorage storage,
            MemoryPackContentContext context) where TStorage : class
        {
            context.BeginRead(storage);
            T value = default;
            int consumed = MemoryPackSerializer.Deserialize(bytes, ref value, context.Options);
            if (consumed != bytes.Length)
                throw new System.IO.InvalidDataException("MemoryPack content contains trailing data.");
            return value;
        }

        public static byte[] Serialize<T>(T value, MemoryPackContentPart part = MemoryPackContentPart.Full)
        {
            var options = new MemoryPackSerializerOptions { ServiceProvider = new MemoryPackContentContext(part) };
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
