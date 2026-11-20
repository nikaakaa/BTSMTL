using System;
using System.Runtime.CompilerServices;

namespace BTSMTL.Orchestration
{
    internal struct OrchestrationStateLayoutImageHeader
    {
        public int FrameCapacity;
        public int FramePayloadCount;
        public OrchestrationStateRegion<byte> TimelineRegion;
        public OrchestrationMemorySection Segments;
        public OrchestrationMemorySection Indices;
        public OrchestrationMemorySection FramePayloads;
        public OrchestrationMemorySection FramePools;
        public OrchestrationMemorySection FrameSlots;
        public OrchestrationMemorySection CharacterAddresses;
        public OrchestrationMemorySection Programs;
    }

    internal readonly struct OrchestrationStateSegmentIndex
    {
        internal OrchestrationStateSegmentIndex(int segment, int framePayload)
        {
            Segment = segment;
            FramePayload = framePayload;
        }

        internal int Segment { get; }
        internal int FramePayload { get; }
    }

    internal readonly struct OrchestrationStateProgramRecord
    {
        internal OrchestrationStateProgramRecord(int skillContentId, OrchestrationMemorySection operationIndices,
            OrchestrationMemorySection operationStateIndices,
            OrchestrationMemorySection values, OrchestrationMemorySection payloadSizes,
            OrchestrationMemorySection privateAddresses, OrchestrationMemorySection ownerPools,
            OrchestrationMemorySection runnableOperationOffsets, OrchestrationMemorySection runnableOperations,
            OrchestrationMemorySection defaultValueOffsets, OrchestrationMemorySection defaultValues)
        {
            SkillContentId = skillContentId;
            OperationIndices = operationIndices;
            OperationStateIndices = operationStateIndices;
            Values = values;
            PayloadSizes = payloadSizes;
            PrivateAddresses = privateAddresses;
            OwnerPools = ownerPools;
            RunnableOperationOffsets = runnableOperationOffsets;
            RunnableOperations = runnableOperations;
            DefaultValueOffsets = defaultValueOffsets;
            DefaultValues = defaultValues;
        }

        internal int SkillContentId { get; }
        internal OrchestrationMemorySection OperationIndices { get; }
        internal OrchestrationMemorySection OperationStateIndices { get; }
        internal OrchestrationMemorySection Values { get; }
        internal OrchestrationMemorySection PayloadSizes { get; }
        internal OrchestrationMemorySection PrivateAddresses { get; }
        internal OrchestrationMemorySection OwnerPools { get; }
        internal OrchestrationMemorySection RunnableOperationOffsets { get; }
        internal OrchestrationMemorySection RunnableOperations { get; }
        internal OrchestrationMemorySection DefaultValueOffsets { get; }
        internal OrchestrationMemorySection DefaultValues { get; }
    }

    internal readonly struct OrchestrationFramePayloadSegment
    {
        internal OrchestrationFramePayloadSegment(int offsetBytes, int strideBytes)
        {
            OffsetBytes = offsetBytes;
            StrideBytes = strideBytes;
        }

        internal int OffsetBytes { get; }
        internal int StrideBytes { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OrchestrationStateRegion<byte> AtFrame(int frame, int sizeBytes) =>
            new OrchestrationStateRegion<byte>(OffsetBytes + frame * StrideBytes, sizeBytes);
    }

    internal ref struct OrchestrationFramePayloadRanges
    {
        readonly ReadOnlySpan<OrchestrationFramePayloadSegment> m_Segments;
        readonly ReadOnlySpan<int> m_Sizes;
        readonly int m_FrameIndex;
        int m_NextSegment;

        internal OrchestrationFramePayloadRanges(ReadOnlySpan<OrchestrationFramePayloadSegment> segments,
            ReadOnlySpan<int> sizes, int frameIndex)
        {
            m_Segments = segments;
            m_Sizes = sizes;
            m_FrameIndex = frameIndex;
            m_NextSegment = 0;
        }

        internal bool MoveNext(out OrchestrationStateRegion<byte> range)
        {
            while (m_NextSegment < m_Sizes.Length)
            {
                int segment = m_NextSegment++;
                int size = m_Sizes[segment];
                if (size > 0)
                {
                    range = m_Segments[segment].AtFrame(m_FrameIndex, size);
                    return true;
                }
            }
            range = default;
            return false;
        }

        internal readonly void CopyTo(in OrchestrationFramePayloadRanges destination,
            ReadOnlySpan<byte> sourceMemory, Span<byte> destinationMemory)
        {
            for (int segment = 0; segment < m_Sizes.Length; segment++)
            {
                int size = m_Sizes[segment];
                if (size == 0)
                    continue;
                int sourceOffset = m_Segments[segment].AtFrame(m_FrameIndex, size).OffsetBytes;
                int destinationOffset = destination.m_Segments[segment].AtFrame(destination.m_FrameIndex, size).OffsetBytes;
                sourceMemory.Slice(sourceOffset, size).CopyTo(destinationMemory.Slice(destinationOffset));
            }
        }
    }

    internal readonly struct OrchestrationFrameValueAddress
    {
        internal OrchestrationFrameValueAddress(OrchestrationValueAddress address, int payloadIndex)
        {
            Address = address;
            PayloadIndex = payloadIndex;
        }

        internal OrchestrationValueAddress Address { get; }
        internal int PayloadIndex { get; }
    }

    internal readonly struct OrchestrationFramePoolRange
    {
        internal OrchestrationFramePoolRange(int first, int count)
        {
            First = first;
            Count = count;
        }

        internal int First { get; }
        internal int Count { get; }
    }

    internal readonly struct OrchestrationFrameSlot
    {
        internal OrchestrationFrameSlot(int pool, int index)
        {
            Pool = pool;
            Index = index;
        }

        internal int Pool { get; }
        internal int Index { get; }
    }

    // 推进只经此视图读取状态布局；视图只持有布局镜像字节。
    public readonly ref struct OrchestrationStateLayoutView
    {
        internal readonly ref struct BlackboardFrame
        {
            readonly ReadOnlySpan<OrchestrationFramePayloadSegment> m_Payloads;
            readonly int m_FrameIndex;

            internal BlackboardFrame(ReadOnlySpan<OrchestrationFramePayloadSegment> payloads, int frameIndex,
                int metadataPayloadIndex)
            {
                m_Payloads = payloads;
                m_FrameIndex = frameIndex;
                MetadataOffset = payloads[metadataPayloadIndex].AtFrame(frameIndex, 0).OffsetBytes;
            }

            internal int MetadataOffset { get; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal OrchestrationValueAddress Address(in OrchestrationFrameValueAddress value) =>
                ResolveAddress(in value, in m_Payloads[value.PayloadIndex], m_FrameIndex);
        }

        internal const int KindCount = (int)OrchestrationValueKind.SpatialIdentity + 1;
        readonly ReadOnlySpan<byte> m_Memory;

        public OrchestrationStateLayoutView(ReadOnlySpan<byte> image) => m_Memory = image;

        ref readonly OrchestrationStateLayoutImageHeader Header =>
            ref OrchestrationMemoryImageWriter.Header<OrchestrationStateLayoutImageHeader>(m_Memory);

        ReadOnlySpan<T> Section<T>(OrchestrationMemorySection section) where T : unmanaged =>
            OrchestrationMemoryImageWriter.Section<T>(m_Memory, section);

        ref readonly OrchestrationStateProgramRecord Program(int program) =>
            ref Section<OrchestrationStateProgramRecord>(Header.Programs)[program];

        public int FrameCapacity => Header.FrameCapacity;
        public OrchestrationStateRegion<byte> TimelineRegion => Header.TimelineRegion;
        public ReadOnlySpan<OrchestrationStateSegment> Segments =>
            Section<OrchestrationStateSegment>(Header.Segments);
        internal OrchestrationFramePayloadRanges PayloadRanges(int frame, ReadOnlySpan<int> sizes)
        {
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            ref readonly OrchestrationFrameSlot slot = ref Section<OrchestrationFrameSlot>(header.FrameSlots)[frame];
            ReadOnlySpan<OrchestrationFramePayloadSegment> segments = Section<OrchestrationFramePayloadSegment>(
                header.FramePayloads).Slice(slot.Pool * header.FramePayloadCount, header.FramePayloadCount);
            return new OrchestrationFramePayloadRanges(segments, sizes, slot.Index);
        }

        internal OrchestrationFramePoolRange FramePool(int program, int owner) =>
            Section<OrchestrationFramePoolRange>(Header.FramePools)[Section<int>(Program(program).OwnerPools)[owner]];

        internal ReadOnlySpan<int> RunnableOperations(int program, int owner)
        {
            ref readonly OrchestrationStateProgramRecord record = ref Program(program);
            return OwnerSlots(owner, record.RunnableOperationOffsets, record.RunnableOperations);
        }

        internal ReadOnlySpan<int> DefaultValueSlots(int program, int owner)
        {
            ref readonly OrchestrationStateProgramRecord record = ref Program(program);
            return OwnerSlots(owner, record.DefaultValueOffsets, record.DefaultValues);
        }

        ReadOnlySpan<int> OwnerSlots(int owner, OrchestrationMemorySection offsetsSection,
            OrchestrationMemorySection slotsSection)
        {
            ReadOnlySpan<int> offsets = Section<int>(offsetsSection);
            int start = offsets[owner];
            return Section<int>(slotsSection).Slice(start, offsets[owner + 1] - start);
        }

        public int ProgramIndex(int skillContentId) =>
            FindProgramIndex(Section<OrchestrationStateProgramRecord>(Header.Programs), skillContentId);

        internal int SkillContentId(int programIndex) => Program(programIndex).SkillContentId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int FindProgramIndex(ReadOnlySpan<OrchestrationStateProgramRecord> programs, int skillContentId)
        {
            int index = 0;
            while (programs[index].SkillContentId != skillContentId)
                index++;
            return index;
        }

        public OrchestrationStateSegment Segment(OrchestrationStateSegmentRole role,
            OrchestrationValueKind valueKind = 0)
        {
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            return Section<OrchestrationStateSegment>(header.Segments)[Index(in header, role, valueKind).Segment];
        }

        OrchestrationStateSegmentIndex Index(in OrchestrationStateLayoutImageHeader header,
            OrchestrationStateSegmentRole role, OrchestrationValueKind valueKind) =>
            Section<OrchestrationStateSegmentIndex>(header.Indices)[(int)role * KindCount + (int)valueKind];

        public OrchestrationValueAddress CharacterAddress(int slot) =>
            Section<OrchestrationValueAddress>(Header.CharacterAddresses)[slot];

        public OrchestrationValueAddress PrivateAddress(int program, int frame, int valueSlot) =>
            ResolveAddress(Section<OrchestrationFrameValueAddress>(Program(program).PrivateAddresses)[valueSlot], frame);

        internal OrchestrationValueAddress ResolveAddress(in OrchestrationFrameValueAddress value, int frame)
        {
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            OrchestrationFrameSlot slot = Section<OrchestrationFrameSlot>(header.FrameSlots)[frame];
            return ResolveAddress(in value, in Section<OrchestrationFramePayloadSegment>(header.FramePayloads)[
                slot.Pool * header.FramePayloadCount + value.PayloadIndex], slot.Index);
        }

        internal BlackboardFrame BindBlackboardFrame(int frame)
        {
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            OrchestrationFrameSlot slot = Section<OrchestrationFrameSlot>(header.FrameSlots)[frame];
            ReadOnlySpan<OrchestrationFramePayloadSegment> payloads =
                Section<OrchestrationFramePayloadSegment>(header.FramePayloads)
                    .Slice(slot.Pool * header.FramePayloadCount, header.FramePayloadCount);
            return new BlackboardFrame(payloads, slot.Index,
                Index(in header, OrchestrationStateSegmentRole.PrivateMetadata, 0).FramePayload);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static OrchestrationValueAddress ResolveAddress(in OrchestrationFrameValueAddress value,
            in OrchestrationFramePayloadSegment payload, int frameIndex)
        {
            OrchestrationValueAddress address = value.Address;
            return new OrchestrationValueAddress(address.Kind,
                payload.OffsetBytes + frameIndex * payload.StrideBytes + address.OffsetBytes,
                address.SizeBytes, address.BitIndex);
        }

        internal ReadOnlySpan<OrchestrationFrameValueAddress> PrivateAddresses(int program) =>
            Section<OrchestrationFrameValueAddress>(Program(program).PrivateAddresses);

        public OrchestrationProgramStorageView ProgramStorage(int program)
        {
            ref readonly OrchestrationStateProgramRecord record = ref Program(program);
            return new OrchestrationProgramStorageView(Section<ushort>(record.OperationIndices),
                Section<OrchestrationValueStorageSlot>(record.Values));
        }

        public ReadOnlySpan<OrchestrationOperationStateIndices> OperationStateIndices(int program) =>
            Section<OrchestrationOperationStateIndices>(Program(program).OperationStateIndices);

        internal ReadOnlySpan<int> PayloadSizes(int program, int owner)
        {
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            OrchestrationMemorySection sizes =
                Section<OrchestrationStateProgramRecord>(header.Programs)[program].PayloadSizes;
            return PayloadSizes(sizes, owner, header.FramePayloadCount);
        }

        internal ReadOnlySpan<int> PayloadSizesForSkill(int skillContentId, int owner)
        {
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            ReadOnlySpan<OrchestrationStateProgramRecord> programs =
                Section<OrchestrationStateProgramRecord>(header.Programs);
            int program = FindProgramIndex(programs, skillContentId);
            return PayloadSizes(programs[program].PayloadSizes, owner, header.FramePayloadCount);
        }

        ReadOnlySpan<int> PayloadSizes(OrchestrationMemorySection sizes, int owner, int count) =>
            Section<int>(sizes).Slice(owner * count, count);

        internal OrchestrationStateRegion<byte> PayloadRegion(int frame, int program, int owner,
            OrchestrationStateSegmentRole role, OrchestrationValueKind valueKind = 0)
        {
            OrchestrationFramePayloadSegment payload = ResolvePayload(frame, role, valueKind,
                out int index, out int frameIndex);
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            OrchestrationMemorySection sizes =
                Section<OrchestrationStateProgramRecord>(header.Programs)[program].PayloadSizes;
            return payload.AtFrame(frameIndex, PayloadSizes(sizes, owner, header.FramePayloadCount)[index]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int PayloadOffset(int frame, OrchestrationStateSegmentRole role,
            OrchestrationValueKind valueKind = 0)
        {
            OrchestrationFramePayloadSegment payload = ResolvePayload(frame, role, valueKind,
                out _, out int frameIndex);
            return payload.AtFrame(frameIndex, 0).OffsetBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        OrchestrationFramePayloadSegment ResolvePayload(int frame, OrchestrationStateSegmentRole role,
            OrchestrationValueKind valueKind, out int index, out int frameIndex)
        {
            ref readonly OrchestrationStateLayoutImageHeader header = ref Header;
            index = Index(in header, role, valueKind).FramePayload;
            OrchestrationFrameSlot slot = Section<OrchestrationFrameSlot>(header.FrameSlots)[frame];
            frameIndex = slot.Index;
            return Section<OrchestrationFramePayloadSegment>(header.FramePayloads)[slot.Pool * header.FramePayloadCount + index];
        }
    }
}
