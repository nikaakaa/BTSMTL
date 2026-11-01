using System;
using System.IO;
using Page = BTSMTL.Diagnostics.RuntimeCaptureFileArchive.Page;

namespace BTSMTL.Diagnostics
{
    public sealed class RuntimeCaptureFileReader : IDisposable
    {
        public const string Protocol = RuntimeTraceBinaryCodec.Protocol;
        readonly BinaryReader m_Events;
        readonly BinaryReader m_EventIndex;
        readonly BinaryReader m_Symbols;
        readonly RuntimeTraceBinaryCodec m_Codec;
        readonly Page[] m_Pages;

        public RuntimeCaptureFileReader(string directory)
        {
            using (var format = new BinaryReader(File.OpenRead(Path.Combine(directory, "events.bin"))))
                RuntimeTraceBinaryCodec.ReadFormat(format);
            m_Events = new BinaryReader(File.OpenRead(Path.Combine(directory, "events.bin")));
            m_EventIndex = new BinaryReader(File.OpenRead(Path.Combine(directory, "events.idx")));
            m_Symbols = new BinaryReader(File.OpenRead(Path.Combine(directory, "strings.bin")));
            m_Codec = new RuntimeTraceBinaryCodec(Stream.Null, 1024);
            using var pages = new BinaryReader(File.OpenRead(Path.Combine(directory, "pages.idx")));
            using var offsets = File.OpenRead(Path.Combine(directory, "page-offsets.idx"));
            m_Pages = new Page[offsets.Length / sizeof(long)];
            for (int i = 0; i < m_Pages.Length; i++)
                m_Pages[i] = m_Codec.Read<Page>(pages);
        }

        public long Count => m_EventIndex.BaseStream.Length / RuntimeCaptureFileArchive.EventIndexSize;

        public RuntimeTraceEvent[] Read(long offset, int limit)
        {
            int count = checked((int)Math.Min(limit, Count - offset));
            var result = new RuntimeTraceEvent[count];
            int written = 0;
            for (int page = 0; page < m_Pages.Length && written < count; page++)
            {
                long end = m_Pages[page].First + m_Pages[page].Count;
                if (offset >= end)
                    continue;
                m_Codec.BeginReadSymbols(m_Symbols, m_Pages[page].SymbolOffset);
                m_Codec.ReadSymbols(m_Symbols, m_Pages[page].SymbolCount);
                m_EventIndex.BaseStream.Position = offset * RuntimeCaptureFileArchive.EventIndexSize + sizeof(ulong);
                m_Events.BaseStream.Position = m_EventIndex.ReadInt64();
                while (offset < end && written < count)
                {
                    result[written++] = m_Codec.Read(m_Events);
                    offset++;
                }
            }
            return result;
        }

        public void Dispose()
        {
            m_Codec.Dispose();
            m_Symbols.Dispose();
            m_EventIndex.Dispose();
            m_Events.Dispose();
        }
    }
}
