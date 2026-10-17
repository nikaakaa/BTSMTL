#if UNITY_EDITOR
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ThirdPersonSimulation
{
    public readonly struct RecordedFrameSlice
    {
        public RecordedFrameSlice(long offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public long Offset { get; }
        public int Length { get; }
    }

    public sealed class RecordedFrameArchive
    {
        const int BlockSize = 4096;
        readonly object m_Gate = new();
        readonly byte[][] m_Pages;
        readonly long[] m_PageOffsets;
        readonly int[] m_PageLengths;
        readonly Queue<int> m_Available = new();
        readonly Queue<int> m_Pending = new();
        readonly List<long> m_StoredOffsets = new();
        readonly byte[] m_ReadBuffer;
        readonly byte[] m_ReadHeader = new byte[8];
        readonly byte[] m_EncodedPage;
        readonly FileStream m_Writer;
        readonly FileStream m_Reader;
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

        public RecordedFrameArchive(string directory, int pageCapacity, int pageCount)
        {
            m_Directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(m_Directory);
            m_Path = Path.Combine(m_Directory, "frames.bin");
            m_PageCapacity = pageCapacity;
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
            m_EncodedPage = new byte[checked(pageCapacity + 8 * ((pageCapacity + BlockSize - 1) / BlockSize) + 8)];
            m_Writer = new FileStream(m_Path, FileMode.CreateNew, FileAccess.Write,
                FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            m_Reader = new FileStream(m_Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, FileOptions.RandomAccess);
            m_Worker = new Thread(WritePages) { IsBackground = true, Name = "Recorded Frame Archive" };
            m_Worker.Start();
        }

        public void Retain() => m_References++;

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
                long storedOffset;
                lock (m_Gate)
                    storedOffset = m_StoredOffsets[checked((int)(pageOffset / m_PageCapacity))];
                m_Reader.Position = storedOffset;
                ReadBytes(m_ReadHeader, 0, 8);
                int pageLength = BinaryPrimitives.ReadInt32LittleEndian(m_ReadHeader.AsSpan(0, 4));
                int blocks = BinaryPrimitives.ReadInt32LittleEndian(m_ReadHeader.AsSpan(4, 4));
                Array.Clear(m_ReadBuffer, 0, pageLength);
                for (int i = 0; i < blocks; i++)
                {
                    ReadBytes(m_ReadHeader, 0, 8);
                    int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(m_ReadHeader.AsSpan(0, 4));
                    int blockLength = BinaryPrimitives.ReadInt32LittleEndian(m_ReadHeader.AsSpan(4, 4));
                    ReadBytes(m_ReadBuffer, blockOffset, blockLength);
                }
                m_ReadPageOffset = pageOffset;
            }
            return m_ReadBuffer.AsSpan(checked((int)(slice.Offset - pageOffset)), length);
        }

        void ReadBytes(byte[] buffer, int offset, int length)
        {
            int read = 0;
            while (read < length)
            {
                int count = m_Reader.Read(buffer, offset + read, length - read);
                if (count == 0)
                    throw new EndOfStreamException("录制帧归档载荷尚未完整写入。");
                read += count;
            }
        }

        int EncodePage(int page)
        {
            int length = m_PageLengths[page];
            int written = 8;
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
            BinaryPrimitives.WriteInt32LittleEndian(m_EncodedPage.AsSpan(0, 4), length);
            BinaryPrimitives.WriteInt32LittleEndian(m_EncodedPage.AsSpan(4, 4), blocks);
            return written;
        }

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
                    lock (m_Gate)
                    {
                        m_StoredOffsets.Add(offset);
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
            m_Failure?.Throw();
            File.Delete(m_Path);
            Directory.Delete(m_Directory);
        }
    }
}
#endif
