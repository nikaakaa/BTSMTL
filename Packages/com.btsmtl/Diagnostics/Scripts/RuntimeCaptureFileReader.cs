using System;
using System.IO;

namespace BTSMTL.Diagnostics
{
    public sealed class RuntimeCaptureFileReader : IDisposable
    {
        public const string Protocol = "btsmtl.runtime-trace/12";
        readonly BinaryReader m_Events;
        readonly BinaryReader m_Symbols;
        readonly RuntimeTraceBinaryCodec m_Codec;
        readonly Page[] m_Pages;

        public RuntimeCaptureFileReader(string directory)
        {
            m_Events = new BinaryReader(File.OpenRead(Path.Combine(directory, "events.bin")));
            m_Symbols = new BinaryReader(File.OpenRead(Path.Combine(directory, "strings.bin")));
            m_Codec = new RuntimeTraceBinaryCodec(Stream.Null, 1024);
            using var pages = new BinaryReader(File.OpenRead(Path.Combine(directory, "pages.idx")));
            m_Pages = new Page[pages.BaseStream.Length / 60];
            for (int i = 0; i < m_Pages.Length; i++)
            {
                long first = pages.ReadInt64();
                long symbols = pages.ReadInt64();
                pages.BaseStream.Position += 32;
                int symbolCount = pages.ReadInt32();
                pages.BaseStream.Position += 8;
                m_Pages[i] = new Page(first, symbols, symbolCount);
            }
        }

        public long Count => m_Events.BaseStream.Length / RuntimeTraceBinaryCodec.EventSize;

        public RuntimeTraceEvent[] Read(long offset, int limit)
        {
            int count = checked((int)Math.Min(limit, Count - offset));
            var result = new RuntimeTraceEvent[count];
            int written = 0;
            for (int page = 0; page < m_Pages.Length && written < count; page++)
            {
                long end = page + 1 == m_Pages.Length ? Count : m_Pages[page + 1].First;
                if (offset >= end)
                    continue;
                m_Codec.BeginReadSymbols(m_Symbols, m_Pages[page].Symbols);
                m_Codec.ReadSymbols(m_Symbols, m_Pages[page].SymbolCount);
                m_Events.BaseStream.Position = offset * RuntimeTraceBinaryCodec.EventSize;
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
            m_Events.Dispose();
        }

        readonly struct Page
        {
            internal Page(long first, long symbols, int symbolCount)
            {
                First = first;
                Symbols = symbols;
                SymbolCount = symbolCount;
            }
            internal long First { get; }
            internal long Symbols { get; }
            internal int SymbolCount { get; }
        }
    }
}
