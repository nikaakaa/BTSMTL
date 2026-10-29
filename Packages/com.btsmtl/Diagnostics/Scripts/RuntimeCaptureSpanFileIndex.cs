using System;
using System.IO;
using System.Text;

namespace BTSMTL.Diagnostics
{
    readonly struct RuntimeCaptureSpanReference
    {
        internal RuntimeCaptureSpanReference(long startOrdinal, long endOrdinal, double endTime,
            RuntimeCaptureSpanState state, RuntimeTraceDomain domain, Guid branch, ulong epoch)
        {
            StartOrdinal = startOrdinal;
            EndOrdinal = endOrdinal;
            EndTime = endTime;
            State = state;
            Domain = domain;
            Branch = branch;
            Epoch = epoch;
        }

        internal readonly long StartOrdinal;
        internal readonly long EndOrdinal;
        internal readonly double EndTime;
        internal readonly RuntimeCaptureSpanState State;
        internal readonly RuntimeTraceDomain Domain;
        internal readonly Guid Branch;
        internal readonly ulong Epoch;
    }

    sealed class RuntimeCaptureSpanFileIndex : IRuntimeCaptureSpanStorage, IDisposable
    {
        const int CandidateHeaderSize = 60;
        const int SpanSize = 52;
        const int LatestRevisionOffset = 36;
        const int IntervalNodeSize = 24;
        const int IntervalEndOffset = 16;
        const ulong HashBasis = 14695981039346656037;
        const ulong HashPrime = 1099511628211;
        readonly RuntimeCaptureFileArchive m_Archive;
        readonly BinaryReader m_BucketReader;
        readonly BinaryWriter m_BucketWriter;
        readonly BinaryReader m_CandidateReader;
        readonly BinaryWriter m_CandidateWriter;
        readonly BinaryReader m_SpanReader;
        readonly BinaryWriter m_SpanWriter;
        readonly BinaryReader m_RevisionReader;
        readonly BinaryWriter m_RevisionWriter;
        readonly BinaryReader m_IntervalReader;
        readonly BinaryWriter m_IntervalWriter;
        readonly long[] m_IntervalRoots = new long[64];
        readonly IntervalQueryNode[] m_QueryNodes = new IntervalQueryNode[64];
        readonly byte[] m_Guid = new byte[16];
        readonly byte[] m_ZeroPage = new byte[65536];
        long m_BucketCount = 256;
        long m_CandidateCount;
        long m_SpanCount;
        long m_CurrentCandidate;
        RuntimeCaptureSpanSlot m_CurrentSlot;
        long m_QuerySpanCount;
        long m_QueryTreeCount;
        long m_QueryRootFirst;
        long m_QueryRootOffset;
        int m_QueryRootLevel;
        int m_QueryDepth;
        int m_QueryVisited;
        double m_QueryWindowStart;
        double m_QueryWindowEnd;
        internal long BytesWritten => m_BucketWriter.BaseStream.Length + m_CandidateWriter.BaseStream.Length +
            m_SpanWriter.BaseStream.Length + m_RevisionWriter.BaseStream.Length + m_IntervalWriter.BaseStream.Length;

        internal RuntimeCaptureSpanFileIndex(RuntimeCaptureFileArchive archive, string directory)
        {
            m_Archive = archive;
            Open(directory, "span-buckets.idx", out m_BucketReader, out m_BucketWriter);
            Open(directory, "span-candidates.idx", out m_CandidateReader, out m_CandidateWriter);
            Open(directory, "spans.idx", out m_SpanReader, out m_SpanWriter);
            Open(directory, "span-revisions.idx", out m_RevisionReader, out m_RevisionWriter);
            Open(directory, "span-intervals.idx", out m_IntervalReader, out m_IntervalWriter);
            ClearBuckets();
        }

        static void Open(string directory, string name, out BinaryReader reader, out BinaryWriter writer)
        {
            var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.Read, 65536);
            reader = new BinaryReader(stream, Encoding.UTF8, true);
            writer = new BinaryWriter(stream, Encoding.UTF8, false);
        }

        internal bool BeginWindow(double start, double end, long version, long request)
        {
            if (!CountAtVersion(version, request, out m_QuerySpanCount))
                return false;
            m_QueryTreeCount = m_SpanCount;
            m_QueryRootFirst = 0;
            m_QueryRootOffset = 0;
            m_QueryRootLevel = 0;
            for (long count = m_QueryTreeCount; (count >>= 1) != 0;)
                m_QueryRootLevel++;
            m_QueryDepth = 0;
            m_QueryVisited = 0;
            m_QueryWindowStart = start;
            m_QueryWindowEnd = end;
            return true;
        }

        internal bool TryGetNextWindowCandidate(long request, out long location)
        {
            location = -1;
            while (true)
            {
                if ((m_QueryVisited++ & 127) == 0 && !m_Archive.ContinueRead(request))
                    return false;
                if (m_QueryDepth == 0)
                {
                    if (m_QueryRootFirst >= m_QuerySpanCount)
                        return true;
                    while ((m_QueryTreeCount & (1L << m_QueryRootLevel)) == 0)
                        m_QueryRootLevel--;
                    long width = 1L << m_QueryRootLevel;
                    m_QueryNodes[m_QueryDepth++] = new IntervalQueryNode(
                        m_QueryRootOffset + (width * 2 - 2) * IntervalNodeSize,
                        m_QueryRootFirst, m_QueryRootLevel);
                    m_QueryRootOffset += (width * 2 - 1) * IntervalNodeSize;
                    m_QueryRootFirst += width;
                    m_QueryRootLevel--;
                }
                IntervalQueryNode node = m_QueryNodes[--m_QueryDepth];
                if (node.First >= m_QuerySpanCount)
                    continue;
                ReadIntervalRange(node.Offset, out double start, out double end);
                if (start > m_QueryWindowEnd || end < m_QueryWindowStart)
                    continue;
                if (node.Level == 0)
                {
                    location = node.First;
                    return true;
                }
                int level = node.Level - 1;
                long half = 1L << level;
                m_QueryNodes[m_QueryDepth++] = new IntervalQueryNode(
                    node.Offset - IntervalNodeSize, node.First + half, level);
                m_QueryNodes[m_QueryDepth++] = new IntervalQueryNode(
                    node.Offset - half * 2 * IntervalNodeSize, node.First, level);
            }
        }

        bool CountAtVersion(long version, long request, out long count)
        {
            long first = 0, end = m_SpanCount;
            while (first < end)
            {
                if (!m_Archive.IsCurrentRead(request))
                {
                    count = 0;
                    return false;
                }
                long middle = first + (end - first) / 2;
                m_SpanReader.BaseStream.Position = middle * SpanSize;
                if (m_SpanReader.ReadInt64() < version)
                    first = middle + 1;
                else
                    end = middle;
            }
            count = first;
            return true;
        }

        internal bool Read(long location, long version, long request, out RuntimeCaptureSpanReference result)
        {
            m_SpanReader.BaseStream.Position = location * SpanSize;
            long start = m_SpanReader.ReadInt64();
            RuntimeTraceDomain domain = (RuntimeTraceDomain)m_SpanReader.ReadInt32();
            Guid branch = ReadGuid(m_SpanReader);
            ulong epoch = m_SpanReader.ReadUInt64();
            long revision = m_SpanReader.ReadInt64();
            for (int visited = 0; ; visited++)
            {
                if (visited % 128 == 0 && !m_Archive.ContinueRead(request))
                {
                    result = default;
                    return false;
                }
                m_RevisionReader.BaseStream.Position = revision;
                long previous = m_RevisionReader.ReadInt64();
                long effectiveVersion = m_RevisionReader.ReadInt64();
                if (effectiveVersion <= version)
                    break;
                revision = previous;
            }
            long end = m_RevisionReader.ReadInt64();
            double endTime = m_RevisionReader.ReadDouble();
            RuntimeCaptureSpanState state = (RuntimeCaptureSpanState)m_RevisionReader.ReadInt32();
            result = new RuntimeCaptureSpanReference(start, end, endTime, state, domain, branch, epoch);
            return true;
        }

        internal void Append(in RuntimeTraceEvent value, int sourceGroup) =>
            RuntimeCaptureSpanTransitions.Append(this, in value, sourceGroup);

        bool IRuntimeCaptureSpanStorage.TryGetCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot,
            out RuntimeCaptureSpanToken token)
        {
            m_CurrentSlot = slot;
            m_CurrentCandidate = FindCandidate(in key);
            if (m_CurrentCandidate >= 0)
            {
                m_CandidateReader.BaseStream.Position = SlotOffset();
                long location = m_CandidateReader.ReadInt64();
                ulong position = m_CandidateReader.ReadUInt64();
                int cycle = m_CandidateReader.ReadInt32();
                token = new RuntimeCaptureSpanToken(location, position, cycle);
                return location >= 0;
            }
            token = default;
            return false;
        }

        long IRuntimeCaptureSpanStorage.Create(in RuntimeTraceEvent value, int sourceGroup, RuntimeCaptureSpanState state)
        {
            long location = m_SpanCount++;
            long revision = WriteRevision(-1, m_Archive.WrittenEventCount, value.TimeSeconds, state);
            long interval = AppendInterval((float)value.TimeSeconds, state);
            m_SpanWriter.BaseStream.Position = location * SpanSize;
            m_SpanWriter.Write(m_Archive.WrittenEventCount);
            m_SpanWriter.Write((int)(value.Domain == RuntimeTraceDomain.Lifecycle ? RuntimeTraceDomain.Logic : value.Domain));
            WriteGuid(m_SpanWriter, value.ExecutionBranchId);
            m_SpanWriter.Write(value.RuntimeEpoch);
            m_SpanWriter.Write(revision);
            m_SpanWriter.Write(interval);
            return location;
        }

        void IRuntimeCaptureSpanStorage.SetEnd(long location, in RuntimeTraceEvent value, RuntimeCaptureSpanState state)
        {
            long previous = LatestRevision(location, out long interval);
            SetLatestRevision(location, WriteRevision(previous, m_Archive.WrittenEventCount, value.TimeSeconds, state));
            if (state == RuntimeCaptureSpanState.Completed)
                SetIntervalEnd(interval, (float)value.TimeSeconds);
            WriteCurrent(location, value.Position, value.GetCycle());
        }

        void IRuntimeCaptureSpanStorage.Complete(long location, in RuntimeTraceEvent cause)
        {
            long previous = LatestRevision(location, out long interval);
            m_RevisionReader.BaseStream.Position = previous + 16;
            long endOrdinal = m_RevisionReader.ReadInt64();
            double endTime = m_RevisionReader.ReadDouble();
            SetLatestRevision(location, WriteRevision(previous, endOrdinal, endTime, RuntimeCaptureSpanState.Completed));
            // Earlier Open revisions can extend past their last event through the branch clock.
            SetIntervalEnd(interval, (float)Math.Max(endTime, cause.TimeSeconds));
        }

        void IRuntimeCaptureSpanStorage.SetCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot,
            in RuntimeCaptureSpanToken current)
        {
            if (m_CurrentCandidate < 0)
                m_CurrentCandidate = CreateCandidate(in key);
            m_CurrentSlot = slot;
            WriteCurrent(current.Location, current.EndPosition, current.EndCycle);
        }

        void IRuntimeCaptureSpanStorage.RemoveCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot)
        {
            m_CurrentSlot = slot;
            m_CandidateWriter.BaseStream.Position = SlotOffset();
            m_CandidateWriter.Write(-1L);
        }

        long SlotOffset() => m_CurrentCandidate + (m_CurrentSlot == RuntimeCaptureSpanSlot.ClipSample ? 20 : 40);

        void WriteCurrent(long location, ulong position, int cycle)
        {
            m_CandidateWriter.BaseStream.Position = SlotOffset();
            m_CandidateWriter.Write(location);
            m_CandidateWriter.Write(position);
            m_CandidateWriter.Write(cycle);
        }

        long LatestRevision(long location, out long interval)
        {
            m_SpanReader.BaseStream.Position = location * SpanSize + LatestRevisionOffset;
            long revision = m_SpanReader.ReadInt64();
            interval = m_SpanReader.ReadInt64();
            return revision;
        }

        void SetLatestRevision(long location, long revision)
        {
            m_SpanWriter.BaseStream.Position = location * SpanSize + LatestRevisionOffset;
            m_SpanWriter.Write(revision);
        }

        long WriteRevision(long previous, long endOrdinal, double endTime, RuntimeCaptureSpanState state)
        {
            long offset = m_RevisionWriter.BaseStream.Length;
            m_RevisionWriter.BaseStream.Position = offset;
            m_RevisionWriter.Write(previous);
            m_RevisionWriter.Write(m_Archive.WrittenEventCount + 1);
            m_RevisionWriter.Write(endOrdinal);
            m_RevisionWriter.Write(endTime);
            m_RevisionWriter.Write((int)state);
            return offset;
        }

        long AppendInterval(double start, RuntimeCaptureSpanState state)
        {
            double end = state == RuntimeCaptureSpanState.Open ? double.PositiveInfinity : start;
            long leaf = AppendIntervalNode(start, end);
            long root = leaf;
            int level = 0;
            for (long count = m_SpanCount; (count & 1) == 0; count >>= 1)
            {
                long left = m_IntervalRoots[level];
                ReadIntervalRange(left, out double leftStart, out double leftEnd);
                ReadIntervalRange(root, out double rightStart, out double rightEnd);
                long parent = AppendIntervalNode(Math.Min(leftStart, rightStart), Math.Max(leftEnd, rightEnd));
                m_IntervalWriter.BaseStream.Position = left;
                m_IntervalWriter.Write(parent);
                m_IntervalWriter.BaseStream.Position = root;
                m_IntervalWriter.Write(parent);
                root = parent;
                level++;
            }
            m_IntervalRoots[level] = root;
            return leaf;
        }

        long AppendIntervalNode(double start, double end)
        {
            long offset = m_IntervalWriter.BaseStream.Length;
            m_IntervalWriter.BaseStream.Position = offset;
            m_IntervalWriter.Write(-1L);
            m_IntervalWriter.Write(start);
            m_IntervalWriter.Write(end);
            return offset;
        }

        void ReadIntervalRange(long offset, out double start, out double end)
        {
            m_IntervalReader.BaseStream.Position = offset + sizeof(long);
            start = m_IntervalReader.ReadDouble();
            end = m_IntervalReader.ReadDouble();
        }

        double ReadIntervalEnd(long offset)
        {
            m_IntervalReader.BaseStream.Position = offset + IntervalEndOffset;
            return m_IntervalReader.ReadDouble();
        }

        void SetIntervalEnd(long offset, double end)
        {
            m_IntervalReader.BaseStream.Position = offset;
            long parent = m_IntervalReader.ReadInt64();
            m_IntervalReader.ReadDouble();
            double previousEnd = m_IntervalReader.ReadDouble();
            if (previousEnd != double.PositiveInfinity)
                end = Math.Max(previousEnd, end);
            if (previousEnd == end)
                return;
            m_IntervalWriter.BaseStream.Position = offset + IntervalEndOffset;
            m_IntervalWriter.Write(end);
            // Nodes are appended in postorder; each completed subtree has two equal-sized children.
            for (int level = 1; parent >= 0; level++)
            {
                m_IntervalReader.BaseStream.Position = parent;
                long next = m_IntervalReader.ReadInt64();
                m_IntervalReader.ReadDouble();
                previousEnd = m_IntervalReader.ReadDouble();
                end = Math.Max(ReadIntervalEnd(parent - (1L << level) * IntervalNodeSize),
                    ReadIntervalEnd(parent - IntervalNodeSize));
                if (previousEnd == end)
                    break;
                m_IntervalWriter.BaseStream.Position = parent + IntervalEndOffset;
                m_IntervalWriter.Write(end);
                parent = next;
            }
        }

        readonly struct IntervalQueryNode
        {
            internal IntervalQueryNode(long offset, long first, int level)
            {
                Offset = offset;
                First = first;
                Level = level;
            }

            internal readonly long Offset;
            internal readonly long First;
            internal readonly int Level;
        }

        long ReadBucket(ulong hash)
        {
            m_BucketReader.BaseStream.Position = (long)(hash & (ulong)(m_BucketCount - 1)) * sizeof(long);
            return m_BucketReader.ReadInt64() - 1;
        }

        void WriteBucket(ulong hash, long entry)
        {
            m_BucketWriter.BaseStream.Position = (long)(hash & (ulong)(m_BucketCount - 1)) * sizeof(long);
            m_BucketWriter.Write(entry + 1);
        }

        void ClearBuckets()
        {
            m_BucketWriter.BaseStream.SetLength(m_BucketCount * sizeof(long));
            m_BucketWriter.BaseStream.Position = 0;
            long remaining = m_BucketWriter.BaseStream.Length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(remaining, m_ZeroPage.Length);
                m_BucketWriter.Write(m_ZeroPage, 0, count);
                remaining -= count;
            }
        }

        void GrowBuckets()
        {
            m_BucketCount *= 2;
            ClearBuckets();
            long end = m_CandidateReader.BaseStream.Length;
            for (long offset = 0; offset < end;)
            {
                m_CandidateReader.BaseStream.Position = offset + 8;
                ulong hash = m_CandidateReader.ReadUInt64();
                int size = m_CandidateReader.ReadInt32();
                long next = ReadBucket(hash);
                m_CandidateWriter.BaseStream.Position = offset;
                m_CandidateWriter.Write(next);
                WriteBucket(hash, offset);
                offset += size;
            }
        }

        long FindCandidate(in RuntimeCaptureSpanKey key)
        {
            ulong hash = Hash(in key);
            long offset = ReadBucket(hash);
            while (offset >= 0)
            {
                m_CandidateReader.BaseStream.Position = offset;
                long next = m_CandidateReader.ReadInt64();
                ulong storedHash = m_CandidateReader.ReadUInt64();
                if (storedHash == hash)
                {
                    m_CandidateReader.BaseStream.Position = offset + CandidateHeaderSize;
                    if (Matches(in key))
                        return offset;
                }
                offset = next;
            }
            return -1;
        }

        long CreateCandidate(in RuntimeCaptureSpanKey key)
        {
            if (m_CandidateCount * 4 >= m_BucketCount * 3)
                GrowBuckets();
            ulong hash = Hash(in key);
            long next = ReadBucket(hash);
            long offset = m_CandidateWriter.BaseStream.Length;
            m_CandidateWriter.BaseStream.Position = offset;
            m_CandidateWriter.Write(next);
            m_CandidateWriter.Write(hash);
            m_CandidateWriter.Write(0);
            m_CandidateWriter.Write(-1L);
            m_CandidateWriter.Write(0UL);
            m_CandidateWriter.Write(0);
            m_CandidateWriter.Write(-1L);
            m_CandidateWriter.Write(0UL);
            m_CandidateWriter.Write(0);
            WriteKey(in key);
            int size = (int)(m_CandidateWriter.BaseStream.Position - offset);
            m_CandidateWriter.BaseStream.Position = offset + 16;
            m_CandidateWriter.Write(size);
            WriteBucket(hash, offset);
            m_CandidateCount++;
            return offset;
        }

        void WriteKey(in RuntimeCaptureSpanKey key)
        {
            var writer = m_CandidateWriter;
            writer.Write((int)key.Domain);
            WriteGuid(writer, key.ExecutionBranchId);
            writer.Write(key.RuntimeEpoch);
            writer.Write(key.Source.Value);
            writer.Write((int)key.Source.Kind);
            writer.Write((int)key.Instance.Kind);
            WriteGuid(writer, key.Instance.CharacterRuntimeId);
            WriteGuid(writer, key.Instance.GraphRuntimeId);
            WriteString(key.Instance.StateId);
            writer.Write(key.Instance.ActivationGeneration);
            writer.Write(key.Instance.TimelinePlaybackId);
            writer.Write(key.Instance.TreeClipCycle);
            writer.Write(key.Instance.ActionInstanceId);
            WriteString(key.Instance.CallSiteId);
            writer.Write(key.Instance.InvocationGeneration);
            writer.Write(key.Instance.SourceOperationIndex);
            writer.Write(key.Instance.TreeClipOperationIndex);
            WriteString(key.ContentRevision.RuntimeId);
            WriteString(key.ContentRevision.SourceRevision);
            WriteString(key.ContentRevision.ContentHash);
        }

        bool Matches(in RuntimeCaptureSpanKey key)
        {
            var reader = m_CandidateReader;
            bool match = reader.ReadInt32() == (int)key.Domain;
            match &= ReadGuid(reader) == key.ExecutionBranchId;
            match &= reader.ReadUInt64() == key.RuntimeEpoch;
            match &= reader.ReadInt32() == key.Source.Value;
            match &= reader.ReadInt32() == (int)key.Source.Kind;
            match &= reader.ReadInt32() == (int)key.Instance.Kind;
            match &= ReadGuid(reader) == key.Instance.CharacterRuntimeId;
            match &= ReadGuid(reader) == key.Instance.GraphRuntimeId;
            match &= MatchString(key.Instance.StateId);
            match &= reader.ReadUInt64() == key.Instance.ActivationGeneration;
            match &= reader.ReadUInt64() == key.Instance.TimelinePlaybackId;
            match &= reader.ReadInt32() == key.Instance.TreeClipCycle;
            match &= reader.ReadUInt64() == key.Instance.ActionInstanceId;
            match &= MatchString(key.Instance.CallSiteId);
            match &= reader.ReadUInt64() == key.Instance.InvocationGeneration;
            match &= reader.ReadInt32() == key.Instance.SourceOperationIndex;
            match &= reader.ReadInt32() == key.Instance.TreeClipOperationIndex;
            match &= MatchString(key.ContentRevision.RuntimeId);
            match &= MatchString(key.ContentRevision.SourceRevision);
            match &= MatchString(key.ContentRevision.ContentHash);
            return match;
        }

        void WriteString(string value)
        {
            int length = value == null ? -1 : value.Length;
            m_CandidateWriter.Write(length);
            ReadOnlySpan<char> characters = value.AsSpan();
            for (int i = 0; i < length; i++)
                m_CandidateWriter.Write((ushort)characters[i]);
        }

        bool MatchString(string value)
        {
            int length = m_CandidateReader.ReadInt32();
            int expected = value == null ? -1 : value.Length;
            if (length != expected)
            {
                if (length > 0)
                    m_CandidateReader.BaseStream.Position += length * sizeof(char);
                return false;
            }
            bool match = true;
            ReadOnlySpan<char> characters = value.AsSpan();
            for (int i = 0; i < length; i++)
                match &= m_CandidateReader.ReadUInt16() == characters[i];
            return match;
        }

        void WriteGuid(BinaryWriter writer, Guid value)
        {
            value.TryWriteBytes(m_Guid);
            writer.Write(m_Guid);
        }

        Guid ReadGuid(BinaryReader reader)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(m_Guid, reader.ReadUInt64());
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(m_Guid.AsSpan(8), reader.ReadUInt64());
            return new Guid(m_Guid);
        }

        ulong Hash(in RuntimeCaptureSpanKey key)
        {
            ulong hash = HashBasis;
            AddHash(ref hash, (ulong)key.Domain);
            AddHash(ref hash, key.ExecutionBranchId);
            AddHash(ref hash, key.RuntimeEpoch);
            AddHash(ref hash, (ulong)key.Source.Value);
            AddHash(ref hash, (ulong)key.Source.Kind);
            AddHash(ref hash, (ulong)key.Instance.Kind);
            AddHash(ref hash, key.Instance.CharacterRuntimeId);
            AddHash(ref hash, key.Instance.GraphRuntimeId);
            AddHash(ref hash, key.Instance.StateId);
            AddHash(ref hash, key.Instance.ActivationGeneration);
            AddHash(ref hash, key.Instance.TimelinePlaybackId);
            AddHash(ref hash, (ulong)key.Instance.TreeClipCycle);
            AddHash(ref hash, key.Instance.ActionInstanceId);
            AddHash(ref hash, key.Instance.CallSiteId);
            AddHash(ref hash, key.Instance.InvocationGeneration);
            AddHash(ref hash, (ulong)key.Instance.SourceOperationIndex);
            AddHash(ref hash, (ulong)key.Instance.TreeClipOperationIndex);
            AddHash(ref hash, key.ContentRevision.RuntimeId);
            AddHash(ref hash, key.ContentRevision.SourceRevision);
            AddHash(ref hash, key.ContentRevision.ContentHash);
            return hash;
        }

        static void AddHash(ref ulong hash, ulong value)
        {
            for (int i = 0; i < sizeof(ulong); i++)
            {
                hash = unchecked((hash ^ (byte)value) * HashPrime);
                value >>= 8;
            }
        }

        void AddHash(ref ulong hash, Guid value)
        {
            value.TryWriteBytes(m_Guid);
            for (int i = 0; i < m_Guid.Length; i++)
                hash = unchecked((hash ^ m_Guid[i]) * HashPrime);
        }

        static void AddHash(ref ulong hash, string value)
        {
            AddHash(ref hash, value == null ? ulong.MaxValue : (ulong)value.Length);
            if (value != null)
            {
                ReadOnlySpan<char> characters = value.AsSpan();
                for (int i = 0; i < value.Length; i++)
                    AddHash(ref hash, characters[i]);
            }
        }

        internal void Reset()
        {
            m_CandidateWriter.BaseStream.SetLength(0);
            m_SpanWriter.BaseStream.SetLength(0);
            m_RevisionWriter.BaseStream.SetLength(0);
            m_IntervalWriter.BaseStream.SetLength(0);
            m_CandidateCount = m_SpanCount = m_CurrentCandidate = 0;
            m_CurrentSlot = default;
            m_QueryDepth = 0;
            Array.Clear(m_IntervalRoots, 0, m_IntervalRoots.Length);
            ClearBuckets();
        }

        public void Dispose()
        {
            m_BucketReader.Dispose();
            m_BucketWriter.Dispose();
            m_CandidateReader.Dispose();
            m_CandidateWriter.Dispose();
            m_SpanReader.Dispose();
            m_SpanWriter.Dispose();
            m_RevisionReader.Dispose();
            m_RevisionWriter.Dispose();
            m_IntervalReader.Dispose();
            m_IntervalWriter.Dispose();
        }
    }
}
