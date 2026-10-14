using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;

namespace ThirdPersonSimulation.Serialization
{
    public enum MemoryPackContentPart
    {
        Full,
        Layout,
        Payload
    }

    public sealed class MemoryPackContentContext : IServiceProvider
    {
        readonly CanonicalTextReferences m_Texts;
        readonly CanonicalDeclaredIdentityReader m_DeclaredIdentities;
        readonly char[] m_Characters;

        internal MemoryPackContentContext(MemoryPackContentPart part) => Part = part;

        public MemoryPackContentContext(CanonicalTextReferences texts,
            CanonicalDeclaredIdentityReader declaredIdentities, int maximumUtf16Characters)
        {
            m_Texts = texts;
            m_DeclaredIdentities = declaredIdentities;
            m_Characters = new char[maximumUtf16Characters];
            Options = new MemoryPackSerializerOptions { ServiceProvider = this };
        }

        public MemoryPackContentPart Part { get; set; }
        public bool IsPrepared => m_Texts != null;
        public object PreparedStorage { get; private set; }
        internal MemoryPackSerializerOptions Options { get; }

        internal void BeginRead(object storage)
        {
            Part = MemoryPackContentPart.Full;
            PreparedStorage = storage;
            m_Texts.Reset();
        }

        public CanonicalTextValue ReadText(ref MemoryPackReader reader)
        {
            if (!reader.TryReadCollectionHeader(out int length))
                return default;
            if (length == 0)
                return CanonicalTextValue.Empty;
            if (length > 0)
            {
                int byteCount = checked(length * sizeof(char));
                ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref reader.GetSpanReference(byteCount), byteCount);
                CanonicalTextValue value = m_Texts.CaptureCharacters(MemoryMarshal.Cast<byte, char>(bytes));
                reader.Advance(byteCount);
                return value;
            }
            int utf8Bytes = ~length;
            reader.ReadUnmanaged(out int _);
            ReadOnlySpan<byte> utf8 = MemoryMarshal.CreateReadOnlySpan(ref reader.GetSpanReference(utf8Bytes), utf8Bytes);
            int characters = Encoding.UTF8.GetChars(utf8, m_Characters.AsSpan());
            CanonicalTextValue captured = m_Texts.CaptureCharacters(m_Characters.AsSpan(0, characters));
            reader.Advance(utf8Bytes);
            return captured;
        }

        public string ReadDeclaredText(ref MemoryPackReader reader)
        {
            CanonicalTextValue value = ReadText(ref reader);
            return m_DeclaredIdentities.ResolvePublished(in value);
        }

        public object GetService(Type serviceType) => serviceType == typeof(MemoryPackContentContext) ? this : null;
    }

    public static partial class MemoryPackContent
    {
        static MemoryPackContent() => MemoryPackGeneratedFormatters.Register();

        public static void SerializeInto<T, TBufferWriter>(in T value, TBufferWriter destination,
            MemoryPackContentContext context) where TBufferWriter : class, IBufferWriter<byte>
        {
            context.Part = MemoryPackContentPart.Full;
            MemoryPackSerializer.Serialize<T, TBufferWriter>(in destination, in value, context.Options);
        }

        public static Fixed.SimulationInput DeserializeInto(ReadOnlySpan<byte> bytes,
            Fixed.SimulationInputFrameStorage storage, MemoryPackContentContext context) =>
            DeserializeInto<Fixed.SimulationInput, Fixed.SimulationInputFrameStorage>(bytes, storage, context);

        public static SimulationInput DeserializeInto(ReadOnlySpan<byte> bytes,
            Float32SimulationInputFrameStorage storage, MemoryPackContentContext context) =>
            DeserializeInto<SimulationInput, Float32SimulationInputFrameStorage>(bytes, storage, context);

        static T DeserializeInto<T, TStorage>(ReadOnlySpan<byte> bytes, TStorage storage,
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
