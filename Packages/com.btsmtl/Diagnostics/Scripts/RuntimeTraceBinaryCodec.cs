using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MemoryPack;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Serialization;

namespace BTSMTL.Diagnostics
{
    sealed class RuntimeTraceBinaryCodec : IDisposable, IServiceProvider, IBufferWriter<byte>
    {
        internal const string Protocol = "btsmtl.runtime-trace.memorypack/13";
        const uint Magic = 0x43525442;
        static readonly string s_FormatIdentity;
        static readonly int s_StringSlots;
        readonly Dictionary<string, int> m_StringIds;
        readonly List<string> m_Strings;
        readonly BinaryWriter m_Symbols;
        readonly bool m_InlineText;
        readonly MemoryPackWriterOptionalState m_WriteState;
        readonly MemoryPackReaderOptionalState m_ReadState;
        byte[] m_Buffer = new byte[65536];
        int m_Length;

        static RuntimeTraceBinaryCodec()
        {
            MemoryPackGeneratedFormatters.Register();
            MemoryPackReader reader = default;
            s_FormatIdentity = StableHash.Compute(Protocol,
                SchemaHash(reader.GetFormatter<RuntimeTraceEvent>()),
                SchemaHash(reader.GetFormatter<RuntimeCaptureFileArchive.Page>()),
                SchemaHash(reader.GetFormatter<RuntimeCaptureSpanSource>()),
                SchemaHash(reader.GetFormatter<RuntimeCaptureSpanKey>())).Value;
            s_StringSlots = StringSlots(typeof(RuntimeTraceEvent));
        }

        static string SchemaHash(object formatter) => ((IMemoryPackSchema)formatter).SchemaHash.Value;

        internal RuntimeTraceBinaryCodec(Stream symbols, int maximumEventsPerPage, bool inlineText = false)
        {
            m_Symbols = new BinaryWriter(symbols, Encoding.UTF8, true);
            int capacity = checked(maximumEventsPerPage * s_StringSlots);
            m_StringIds = new Dictionary<string, int>(capacity, StringComparer.Ordinal);
            m_Strings = new List<string>(capacity);
            m_InlineText = inlineText;
            var options = new MemoryPackSerializerOptions { ServiceProvider = this, StringEncoding = StringEncoding.Utf16 };
            m_WriteState = MemoryPackWriterOptionalStatePool.Rent(options);
            m_ReadState = MemoryPackReaderOptionalStatePool.Rent(options);
        }

        internal int SymbolCount => m_Strings.Count;
        internal ReadOnlySpan<byte> Encoded => m_Buffer.AsSpan(0, m_Length);

        static int StringSlots(Type type)
        {
            if (type == typeof(string)) return 1;
            if (type.IsPrimitive || type.IsEnum) return 0;
            int count = 0;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                count += StringSlots(field.FieldType);
            return count;
        }

        internal static void WriteFormat(BinaryWriter writer)
        {
            writer.BaseStream.Position = 0;
            writer.Write(Magic);
            writer.Write(Protocol);
            writer.Write(s_FormatIdentity);
        }

        internal static void ReadFormat(BinaryReader reader)
        {
            if (reader.ReadUInt32() != Magic || reader.ReadString() != Protocol || reader.ReadString() != s_FormatIdentity)
                throw new InvalidDataException("Trace 文件不符合当前生成格式 " + Protocol + "，原文件保持不变。");
        }

        internal void Reset()
        {
            ClearSymbols();
            m_Length = 0;
        }

        internal void WriteSourceMetadata(BinaryWriter writer, in RuntimeCaptureSpanSource source) => Write(writer, in source);

        internal RuntimeCaptureSpanSource ReadSourceMetadata(BinaryReader reader) => Read<RuntimeCaptureSpanSource>(reader);

        internal void WriteClockMetadata(BinaryWriter writer, (RuntimeTraceDomain Domain, Guid Branch, ulong Epoch) key,
            double time)
        {
            var value = (key, time);
            Write(writer, in value);
        }

        internal void ReadClockMetadata(BinaryReader reader, RuntimeCaptureSpanAccumulator facts)
        {
            var value = Read<((RuntimeTraceDomain Domain, Guid Branch, ulong Epoch) Key, double Time)>(reader);
            facts.ImportClock(value.Key.Domain, value.Key.Branch, value.Key.Epoch, value.Time);
        }

        internal long BeginPage()
        {
            ClearSymbols();
            m_Symbols.BaseStream.Position = m_Symbols.BaseStream.Length;
            return m_Symbols.BaseStream.Position;
        }

        internal void BeginReadSymbols(BinaryReader reader, long offset)
        {
            ClearSymbols();
            reader.BaseStream.Position = offset;
        }

        internal void ReadSymbols(BinaryReader reader, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                if (length < 0 || (length & 1) != 0)
                    throw new InvalidDataException("预览事件文件的文本长度无效。");
                m_Strings.Add(string.Create(length / 2, reader, static (characters, source) =>
                {
                    for (int character = 0; character < characters.Length; character++)
                        characters[character] = (char)source.ReadUInt16();
                }));
            }
        }

        internal void Encode<T>(in T value)
        {
            m_Length = 0;
            RuntimeTraceBinaryCodec destination = this;
            var writer = new MemoryPackWriter<RuntimeTraceBinaryCodec>(ref destination, m_WriteState);
            writer.WriteValueWithFormatter(writer.GetFormatter<T>(), in value);
            writer.Flush();
        }

        internal void Write<T>(BinaryWriter writer, in T value)
        {
            Encode(in value);
            writer.Write(m_Length);
            writer.Write(m_Buffer, 0, m_Length);
        }

        internal RuntimeTraceEvent Read(BinaryReader reader) => Read<RuntimeTraceEvent>(reader);

        internal T Read<T>(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            EnsureCapacity(length);
            for (int offset = 0; offset < length;)
            {
                int read = reader.Read(m_Buffer, offset, length - offset);
                if (read == 0) throw new EndOfStreamException("Trace 记录尚未完整写入。");
                offset += read;
            }
            var packed = new MemoryPackReader(m_Buffer.AsSpan(0, length), m_ReadState);
            try
            {
                T value = default;
                packed.ReadValueWithFormatter(packed.GetFormatter<T>(), ref value);
                if (packed.Remaining != 0)
                    throw new InvalidDataException("Trace 记录包含当前格式之外的字节。");
                return value;
            }
            finally { packed.Dispose(); }
        }

        internal void WriteString<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, string value)
            where TBufferWriter : class, IBufferWriter<byte>
        {
            if (m_InlineText)
            {
                writer.WriteString(value);
                return;
            }
            int id = -1;
            if (value != null && !m_StringIds.TryGetValue(value, out id))
            {
                id = m_Strings.Count;
                m_Strings.Add(value);
                m_StringIds.Add(value, id);
                m_Symbols.Write(checked(value.Length * sizeof(char)));
                for (int i = 0; i < value.Length; i++)
                    m_Symbols.Write((ushort)value[i]);
            }
            writer.WriteValue(id);
        }

        internal string ReadString(ref MemoryPackReader reader) =>
            m_InlineText ? reader.ReadString() : ReadSymbol(reader.ReadValue<int>());

        string ReadSymbol(int id) => id == -1 ? null : m_Strings[id];

        void ClearSymbols()
        {
            m_StringIds.Clear();
            m_Strings.Clear();
        }

        void EnsureCapacity(int capacity)
        {
            if (capacity > m_Buffer.Length)
                Array.Resize(ref m_Buffer, Math.Max(capacity, checked(m_Buffer.Length * 2)));
        }

        public void Advance(int count) => m_Length += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(checked(m_Length + Math.Max(sizeHint, 1)));
            return m_Buffer.AsMemory(m_Length);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(checked(m_Length + Math.Max(sizeHint, 1)));
            return m_Buffer.AsSpan(m_Length);
        }

        public object GetService(Type serviceType) => serviceType == typeof(RuntimeTraceBinaryCodec) ? this : null;

        public void Dispose()
        {
            ClearSymbols();
            ((IDisposable)m_WriteState).Dispose();
            ((IDisposable)m_ReadState).Dispose();
            m_Symbols.Dispose();
        }
    }
}

namespace ThirdPersonSimulation.Serialization
{
    internal static partial class MemoryPackGeneratedFormatters
    {
        const string StringFormat = "btsmtl.trace.page-symbol-utf16/1;span-key-inline-utf16/1";

        static void WriteString<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, string value)
            where TBufferWriter : class, IBufferWriter<byte> =>
            ((BTSMTL.Diagnostics.RuntimeTraceBinaryCodec)writer.Options.ServiceProvider).WriteString(ref writer, value);

        static string ReadString(ref MemoryPackReader reader) =>
            ((BTSMTL.Diagnostics.RuntimeTraceBinaryCodec)reader.Options.ServiceProvider).ReadString(ref reader);
    }
}
