using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using MemoryPack;

namespace ThirdPersonSimulation
{
    public sealed class RecordedFrameArchive : IBufferWriter<byte>
    {
        internal const string FormatIdentity = "recorded-frame-page/memorypack-byte-collection/1|sparse-zero-4096/1";
        const int BlockSize = 4096;
        readonly object m_Gate = new();
        readonly byte[][] m_Pages;
        readonly long[] m_PageOffsets;
        readonly int[] m_PageLengths;
        readonly Queue<int> m_Available;
        readonly Queue<int> m_Pending;
        readonly byte[] m_ReadBuffer;
        readonly byte[] m_ReadHeader = new byte[8];
        readonly byte[] m_EncodedPage;
        readonly MemoryPackWriterOptionalState m_WriteState;
        readonly MemoryPackReaderOptionalState m_ReadState;
        readonly long m_IndexDataOffset;
        int m_EncodedOffset;
        readonly FileStream m_Writer;
        readonly FileStream m_Reader;
        readonly FileStream m_IndexReader;
        readonly BinaryWriter m_Index;
        readonly bool m_KeepFiles;
        readonly Thread m_Worker;
        readonly string m_Directory;
        readonly string m_Path;
        readonly int m_PageCapacity;
        int m_Writing = -1;
        long m_NextPageOffset;
        long m_ReadPageOffset = -1;
        int m_References = 1;
        bool m_Stopping;
        ExceptionDispatchInfo m_Failure;

        public RecordedFrameArchive(string directory, int pageCapacity, int pageCount, bool keepFiles = false)
        {
            m_Directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(m_Directory);
            m_Path = Path.Combine(m_Directory, "frames.memorypack");
            m_PageCapacity = pageCapacity;
            m_KeepFiles = keepFiles;
            m_Available = new Queue<int>(pageCount);
            m_Pending = new Queue<int>(pageCount);
            m_Pages = new byte[pageCount][];
            m_PageOffsets = new long[pageCount];
            m_PageLengths = new int[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                m_Pages[i] = new byte[pageCapacity];
                m_PageOffsets[i] = -1;
                m_Available.Enqueue(i);
            }
            m_ReadBuffer = new byte[pageCapacity];
            m_EncodedPage = new byte[checked(pageCapacity + 8 * ((pageCapacity + BlockSize - 1) / BlockSize) + 12)];
            m_WriteState = MemoryPackWriterOptionalStatePool.Rent();
            m_ReadState = MemoryPackReaderOptionalStatePool.Rent();
            m_Writer = new FileStream(m_Path, FileMode.CreateNew, FileAccess.Write,
                FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            m_Reader = new FileStream(m_Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, FileOptions.RandomAccess);
            string indexPath = Path.Combine(m_Directory, "pages.idx");
            m_Index = new BinaryWriter(new FileStream(indexPath,
                FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            m_Index.Write(2);
            m_Index.Write(pageCapacity);
            m_Index.Write(FormatIdentity);
            m_Index.Flush();
            m_IndexDataOffset = m_Index.BaseStream.Position;
            m_IndexReader = new FileStream(indexPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 64 * 1024, FileOptions.RandomAccess);
            m_Worker = new Thread(WritePages) { IsBackground = true, Name = "Recorded Frame Archive" };
            m_Worker.Start();
        }

        public void Retain() => m_References++;

        // 生产者据此在没有空闲内存页时让出本次更新，而不是在 Reserve 中等待后台写盘。
        public bool HasFreePage
        {
            get
            {
                lock (m_Gate)
                    return m_Available.Count != 0;
            }
        }

        public int PendingPageCount
        {
            get
            {
                lock (m_Gate)
                    return m_Pending.Count;
            }
        }

        public long ReservedBytes => m_NextPageOffset;

        public RecordedFrameSlice Reserve(int maximumBytes)
        {
            if (maximumBytes > m_PageCapacity)
                throw new InvalidOperationException("录制状态超出归档页的准备容量。");
            m_Failure?.Throw();
            if (m_Writing < 0 || m_PageLengths[m_Writing] + maximumBytes > m_PageCapacity)
            {
                SealCurrentPage();
                lock (m_Gate)
                {
                    while (m_Available.Count == 0 && m_Failure == null)
                        Monitor.Wait(m_Gate);
                    m_Failure?.Throw();
                    m_Writing = m_Available.Dequeue();
                }
                m_PageOffsets[m_Writing] = m_NextPageOffset;
                m_NextPageOffset = checked(m_NextPageOffset + m_PageCapacity);
                m_PageLengths[m_Writing] = 0;
            }
            return new RecordedFrameSlice(m_PageOffsets[m_Writing] + m_PageLengths[m_Writing], maximumBytes);
        }

        public void Write(in RecordedFrameSlice slice, int offset, ReadOnlySpan<byte> bytes)
        {
            int destination = checked((int)(slice.Offset - m_PageOffsets[m_Writing]) + offset);
            bytes.CopyTo(m_Pages[m_Writing].AsSpan(destination, bytes.Length));
            m_PageLengths[m_Writing] = destination + bytes.Length;
        }

        public RecordedFrameSlice Append(ReadOnlySpan<byte> bytes)
        {
            RecordedFrameSlice slice = Reserve(bytes.Length);
            Write(in slice, 0, bytes);
            return slice;
        }

        public ReadOnlySpan<byte> Read(in RecordedFrameSlice slice, int length)
        {
            if (length == 0)
                return ReadOnlySpan<byte>.Empty;
            m_Failure?.Throw();
            long pageOffset = slice.Offset / m_PageCapacity * m_PageCapacity;
            for (int i = 0; i < m_Pages.Length; i++)
                if (m_PageOffsets[i] == pageOffset)
                    return m_Pages[i].AsSpan(checked((int)(slice.Offset - pageOffset)), length);
            if (m_ReadPageOffset != pageOffset)
            {
                m_IndexReader.Position = m_IndexDataOffset + pageOffset / m_PageCapacity * sizeof(long);
                ReadBytes(m_IndexReader, m_ReadHeader, 0, sizeof(long));
                long storedOffset = BinaryPrimitives.ReadInt64LittleEndian(m_ReadHeader);
                m_Reader.Position = storedOffset;
                ReadPage(m_Reader, m_ReadBuffer, m_ReadHeader, m_ReadState);
                m_ReadPageOffset = pageOffset;
            }
            return m_ReadBuffer.AsSpan(checked((int)(slice.Offset - pageOffset)), length);
        }

        internal static int ReadPage(FileStream reader, byte[] buffer, byte[] header, MemoryPackReaderOptionalState state)
        {
            ReadBytes(reader, header, 0, sizeof(int));
            var format = new MemoryPackReader(header.AsSpan(0, sizeof(int)), state);
            int encodedBytes;
            try
            {
                if (!format.TryReadCollectionHeader(out encodedBytes))
                    throw new InvalidDataException("录制帧页不能为空。");
            }
            finally { format.Dispose(); }
            long start = reader.Position;
            ReadBytes(reader, header, 0, 8);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
            int blocks = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
            Array.Clear(buffer, 0, length);
            for (int i = 0; i < blocks; i++)
            {
                ReadBytes(reader, header, 0, 8);
                int offset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
                int count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
                ReadBytes(reader, buffer, offset, count);
            }
            if (reader.Position - start != encodedBytes)
                throw new InvalidDataException("录制帧页长度与正式编码不符。");
            return length;
        }

        static void ReadBytes(FileStream reader, byte[] buffer, int offset, int length)
        {
            int read = 0;
            while (read < length)
            {
                int count = reader.Read(buffer, offset + read, length - read);
                if (count == 0)
                    throw new EndOfStreamException("录制帧归档载荷尚未完整写入。");
                read += count;
            }
        }

        int EncodePage(int page)
        {
            int length = m_PageLengths[page];
            int written = 12;
            int blocks = 0;
            for (int offset = 0; offset < length; offset += BlockSize)
            {
                int size = Math.Min(BlockSize, length - offset);
                ReadOnlySpan<byte> bytes = m_Pages[page].AsSpan(offset, size);
                if (IsZero(bytes))
                    continue;
                BinaryPrimitives.WriteInt32LittleEndian(m_EncodedPage.AsSpan(written, 4), offset);
                BinaryPrimitives.WriteInt32LittleEndian(m_EncodedPage.AsSpan(written + 4, 4), size);
                bytes.CopyTo(m_EncodedPage.AsSpan(written + 8, size));
                written += 8 + size;
                blocks++;
            }
            BinaryPrimitives.WriteInt32LittleEndian(m_EncodedPage.AsSpan(4, 4), length);
            BinaryPrimitives.WriteInt32LittleEndian(m_EncodedPage.AsSpan(8, 4), blocks);
            ReadOnlyMemory<byte> data = m_EncodedPage.AsMemory(4, written - 4);
            m_EncodedOffset = 0;
            RecordedFrameArchive destination = this;
            var writer = new MemoryPackWriter<RecordedFrameArchive>(ref destination, m_WriteState);
            writer.WriteValueWithFormatter(writer.GetFormatter<ReadOnlyMemory<byte>>(), in data);
            writer.Flush();
            return m_EncodedOffset;
        }

        void IBufferWriter<byte>.Advance(int count) => m_EncodedOffset += count;
        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint) => m_EncodedPage.AsMemory(m_EncodedOffset);
        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => m_EncodedPage.AsSpan(m_EncodedOffset);

        static bool IsZero(ReadOnlySpan<byte> bytes)
        {
            ReadOnlySpan<ulong> words = MemoryMarshal.Cast<byte, ulong>(bytes);
            for (int i = 0; i < words.Length; i++)
                if (words[i] != 0)
                    return false;
            for (int i = words.Length * sizeof(ulong); i < bytes.Length; i++)
                if (bytes[i] != 0)
                    return false;
            return true;
        }

        void SealCurrentPage()
        {
            if (m_Writing < 0)
                return;
            lock (m_Gate)
            {
                m_Pending.Enqueue(m_Writing);
                m_Writing = -1;
                Monitor.PulseAll(m_Gate);
            }
        }

        void WritePages()
        {
            try
            {
                while (true)
                {
                    int page;
                    lock (m_Gate)
                    {
                        while (m_Pending.Count == 0 && !m_Stopping)
                            Monitor.Wait(m_Gate);
                        if (m_Pending.Count == 0)
                            return;
                        page = m_Pending.Dequeue();
                    }
                    int length = EncodePage(page);
                    long offset = m_Writer.Position;
                    m_Writer.Write(m_EncodedPage, 0, length);
                    m_Writer.Flush();
                    m_Index.Write(offset);
                    m_Index.Flush();
                    lock (m_Gate)
                    {
                        m_Available.Enqueue(page);
                        Monitor.PulseAll(m_Gate);
                    }
                }
            }
            catch (Exception exception)
            {
                lock (m_Gate)
                {
                    m_Failure = ExceptionDispatchInfo.Capture(exception);
                    Monitor.PulseAll(m_Gate);
                }
            }
        }

        public void Release()
        {
            if (--m_References != 0)
                return;
            SealCurrentPage();
            lock (m_Gate)
            {
                m_Stopping = true;
                Monitor.PulseAll(m_Gate);
            }
            m_Worker.Join();
            m_Reader.Dispose();
            m_Writer.Dispose();
            m_IndexReader.Dispose();
            m_Index.Dispose();
            m_WriteState.Dispose();
            m_ReadState.Dispose();
            m_Failure?.Throw();
            if (!m_KeepFiles)
            {
                File.Delete(m_Path);
                File.Delete(Path.Combine(m_Directory, "pages.idx"));
                Directory.Delete(m_Directory);
            }
        }
    }

    public sealed class RecordedFrameArchiveReader : IDisposable
    {
        readonly FileStream m_Frames;
        readonly long[] m_Offsets;
        readonly byte[] m_Buffer;
        readonly byte[] m_Header = new byte[8];
        readonly MemoryPackReaderOptionalState m_ReadState;
        int m_CurrentPage = -1;
        int m_Length;

        public RecordedFrameArchiveReader(string directory)
        {
            using var index = new BinaryReader(File.OpenRead(Path.Combine(directory, "pages.idx")));
            if (index.ReadInt32() != 2)
                throw new InvalidDataException("录制帧页索引版本不受支持。");
            PageCapacity = index.ReadInt32();
            if (index.ReadString() != RecordedFrameArchive.FormatIdentity)
                throw new InvalidDataException("录制帧页编码需要重新录制，原文件未修改。");
            m_Offsets = new long[(index.BaseStream.Length - index.BaseStream.Position) / sizeof(long)];
            for (int i = 0; i < m_Offsets.Length; i++)
                m_Offsets[i] = index.ReadInt64();
            m_Buffer = new byte[PageCapacity];
            m_Frames = File.OpenRead(Path.Combine(directory, "frames.memorypack"));
            m_ReadState = MemoryPackReaderOptionalStatePool.Rent();
        }

        public int PageCapacity { get; }
        public int PageCount => m_Offsets.Length;

        public ReadOnlySpan<byte> ReadPage(int page)
        {
            if (m_CurrentPage != page)
            {
                m_Frames.Position = m_Offsets[page];
                m_Length = RecordedFrameArchive.ReadPage(m_Frames, m_Buffer, m_Header, m_ReadState);
                m_CurrentPage = page;
            }
            return m_Buffer.AsSpan(0, m_Length);
        }

        public ReadOnlySpan<byte> Read(RecordedFrameSlice slice)
        {
            int page = checked((int)(slice.Offset / PageCapacity));
            return ReadPage(page).Slice(checked((int)(slice.Offset % PageCapacity)), slice.Length);
        }

        public void Dispose()
        {
            m_Frames.Dispose();
            m_ReadState.Dispose();
        }
    }
}
