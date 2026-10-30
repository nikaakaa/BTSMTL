using System;

namespace BTSMTL.Orchestration
{
    internal struct OrchestrationStateLayoutImageHeader
    {
        public int FrameCapacity;
        public OrchestrationStateRegion<byte> TimelineRegion;
        public OrchestrationMemorySection Segments;
        public OrchestrationMemorySection Indices;
        public OrchestrationMemorySection CharacterAddresses;
        public OrchestrationMemorySection Programs;
    }

    internal readonly struct OrchestrationStateProgramRecord
    {
        internal OrchestrationStateProgramRecord(int skillContentId, OrchestrationMemorySection operationIndices,
            OrchestrationMemorySection values, OrchestrationMemorySection owners,
            OrchestrationMemorySection privateAddresses)
        {
            SkillContentId = skillContentId;
            OperationIndices = operationIndices;
            Values = values;
            Owners = owners;
            PrivateAddresses = privateAddresses;
        }

        internal int SkillContentId { get; }
        internal OrchestrationMemorySection OperationIndices { get; }
        internal OrchestrationMemorySection Values { get; }
        internal OrchestrationMemorySection Owners { get; }
        internal OrchestrationMemorySection PrivateAddresses { get; }
    }

    internal readonly struct OrchestrationStateOwnerRecord
    {
        internal OrchestrationStateOwnerRecord(int operationCount, int savedOutputCount,
            int blackboardCount, OrchestrationMemorySection valueKindCounts)
        {
            OperationCount = operationCount;
            SavedOutputCount = savedOutputCount;
            BlackboardCount = blackboardCount;
            ValueKindCounts = valueKindCounts;
        }

        internal int OperationCount { get; }
        internal int SavedOutputCount { get; }
        internal int BlackboardCount { get; }
        internal OrchestrationMemorySection ValueKindCounts { get; }
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
            Segments[Section<int>(Header.Indices)[(int)role * KindCount + (int)valueKind]];

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

        public int PayloadCount(in OrchestrationStateSegment segment, int program, int owner)
        {
            ref readonly OrchestrationStateOwnerRecord storage =
                ref Section<OrchestrationStateOwnerRecord>(Program(program).Owners)[owner];
            switch (segment.Role)
            {
                case OrchestrationStateSegmentRole.Operations: return storage.OperationCount;
                case OrchestrationStateSegmentRole.SavedOutputs: return storage.SavedOutputCount;
                case OrchestrationStateSegmentRole.PrivateMetadata: return storage.BlackboardCount;
                case OrchestrationStateSegmentRole.PrivateValues:
                    if (segment.ValueKind == 0)
                        return storage.BlackboardCount;
                    int count = Section<int>(storage.ValueKindCounts)[(int)segment.ValueKind];
                    return segment.ValueKind == OrchestrationValueKind.Boolean ? (count + 63) / 64 : count;
                default: throw new InvalidOperationException("区段不是执行帧载荷。");
            }
        }

        public OrchestrationStateRegion<byte> PayloadRegion(in OrchestrationStateSegment segment,
            int frame, int program, int owner) =>
            new OrchestrationStateRegion<byte>(segment.OffsetBytes + frame * (segment.SizeBytes / FrameCapacity),
                PayloadCount(in segment, program, owner) * segment.ElementSizeBytes);
    }
}
