using System;

namespace BTSMTL.Orchestration
{
    internal struct OrchestrationStateLayoutImageHeader
    {
        public int FrameCapacity;
        public OrchestrationStateRegion<byte> TimelineRegion;
        public OrchestrationMemorySection Segments;
        public OrchestrationMemorySection Indices;
        public OrchestrationMemorySection FramePayloads;
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
            OrchestrationMemorySection values, OrchestrationMemorySection payloadSizes,
            OrchestrationMemorySection privateAddresses)
        {
            SkillContentId = skillContentId;
            OperationIndices = operationIndices;
            Values = values;
            PayloadSizes = payloadSizes;
            PrivateAddresses = privateAddresses;
        }

        internal int SkillContentId { get; }
        internal OrchestrationMemorySection OperationIndices { get; }
        internal OrchestrationMemorySection Values { get; }
        internal OrchestrationMemorySection PayloadSizes { get; }
        internal OrchestrationMemorySection PrivateAddresses { get; }
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
        internal OrchestrationStateRegion<byte> AtFrame(int frame, int sizeBytes) =>
            new OrchestrationStateRegion<byte>(OffsetBytes + frame * StrideBytes, sizeBytes);
    }

    internal readonly struct OrchestrationFrameValueAddress
    {
        internal OrchestrationFrameValueAddress(OrchestrationValueAddress address, int stride)
        {
            Address = address;
            Stride = stride;
        }

        internal OrchestrationValueAddress Address { get; }
        internal int Stride { get; }
        internal OrchestrationValueAddress AtFrame(int frame) =>
            new OrchestrationValueAddress(Address.Kind,
                Address.OffsetBytes + frame * Stride, Address.SizeBytes, Address.BitIndex);
    }

    // 推进只经此视图读取状态布局；视图只持有布局镜像字节。
    public readonly ref struct OrchestrationStateLayoutView
    {
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
        internal ReadOnlySpan<OrchestrationFramePayloadSegment> FramePayloads =>
            Section<OrchestrationFramePayloadSegment>(Header.FramePayloads);

        public int ProgramIndex(int skillContentId)
        {
            ReadOnlySpan<OrchestrationStateProgramRecord> programs =
                Section<OrchestrationStateProgramRecord>(Header.Programs);
            int index = 0;
            while (programs[index].SkillContentId != skillContentId)
                index++;
            return index;
        }

        public OrchestrationStateSegment Segment(OrchestrationStateSegmentRole role,
            OrchestrationValueKind valueKind = 0) =>
            Segments[Index(role, valueKind).Segment];

        OrchestrationStateSegmentIndex Index(OrchestrationStateSegmentRole role, OrchestrationValueKind valueKind) =>
            Section<OrchestrationStateSegmentIndex>(Header.Indices)[(int)role * KindCount + (int)valueKind];

        public OrchestrationValueAddress CharacterAddress(int slot) =>
            Section<OrchestrationValueAddress>(Header.CharacterAddresses)[slot];

        public OrchestrationValueAddress PrivateAddress(int program, int frame, int valueSlot) =>
            Section<OrchestrationFrameValueAddress>(Program(program).PrivateAddresses)[valueSlot].AtFrame(frame);

        public OrchestrationProgramStorageView ProgramStorage(int program)
        {
            ref readonly OrchestrationStateProgramRecord record = ref Program(program);
            return new OrchestrationProgramStorageView(Section<ushort>(record.OperationIndices),
                Section<OrchestrationValueStorageSlot>(record.Values));
        }

        internal ReadOnlySpan<int> PayloadSizes(int program, int owner)
        {
            int count = Header.FramePayloads.Count;
            return Section<int>(Program(program).PayloadSizes).Slice(owner * count, count);
        }

        internal OrchestrationStateRegion<byte> PayloadRegion(int frame, int program, int owner,
            OrchestrationStateSegmentRole role, OrchestrationValueKind valueKind = 0)
        {
            int index = Index(role, valueKind).FramePayload;
            return FramePayloads[index].AtFrame(frame, PayloadSizes(program, owner)[index]);
        }
    }
}
