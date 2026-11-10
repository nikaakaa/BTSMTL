using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationPreparedProgramStorage<TValue> where TValue : unmanaged
    {
        public OrchestrationPreparedProgramStorage(int skillContentId,
            OrchestrationProgramStorageLayout storage,
            ReadOnlyMemory<OrchestrationValueSlotType> valueTypes)
        {
            SkillContentId = skillContentId;
            Storage = storage;
            ValueTypes = valueTypes;
        }

        public int SkillContentId { get; }
        public OrchestrationProgramStorageLayout Storage { get; }
        public ReadOnlyMemory<OrchestrationValueSlotType> ValueTypes { get; }
    }

    public sealed class OrchestrationPreparedStateLayout<TValue> where TValue : unmanaged
    {
        const int KindCount = OrchestrationStateLayoutView.KindCount;
        readonly OrchestrationStateSegment[] m_Segments;
        readonly byte[] m_Image;

        public OrchestrationPreparedStateLayout(OrchestrationStateLayout layout,
            ReadOnlyMemory<OrchestrationStateSegment> segments, int frameCapacity,
            ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> programs,
            ReadOnlyMemory<OrchestrationValueSlotType> characterTypes,
            OrchestrationStateRegion<byte> timelineRegion, OrchestrationStateLayout timelineLayout)
        {
            Layout = layout;
            FrameCapacity = frameCapacity;
            Programs = programs;
            CharacterTypes = characterTypes;
            TimelineRegion = timelineRegion;
            TimelineLayout = timelineLayout;
            m_Segments = segments.ToArray();
            var indices = new OrchestrationStateSegmentIndex[
                Enum.GetValues(typeof(OrchestrationStateSegmentRole)).Length * KindCount];
            Array.Fill(indices, new OrchestrationStateSegmentIndex(-1, -1));
            var payloadSegments = new List<OrchestrationStateSegment>();
            for (int i = 0; i < m_Segments.Length; i++)
            {
                OrchestrationStateSegment segment = m_Segments[i];
                int payloadIndex = -1;
                if (segment.PerFrame && segment.Role != OrchestrationStateSegmentRole.Frames)
                {
                    payloadIndex = payloadSegments.Count;
                    payloadSegments.Add(segment);
                }
                var index = new OrchestrationStateSegmentIndex(i, payloadIndex);
                int start = (int)segment.Role * KindCount;
                if ((segment.Role == OrchestrationStateSegmentRole.CharacterValues ||
                    segment.Role == OrchestrationStateSegmentRole.PrivateValues) && segment.ValueKind == 0)
                    for (int kind = 1; kind < KindCount; kind++)
                        indices[start + kind] = index;
                indices[start + (int)segment.ValueKind] = index;
            }
            var framePayloads = new OrchestrationFramePayloadSegment[payloadSegments.Count];
            for (int i = 0; i < framePayloads.Length; i++)
            {
                OrchestrationStateSegment segment = payloadSegments[i];
                framePayloads[i] = new OrchestrationFramePayloadSegment(segment.OffsetBytes,
                    frameCapacity == 0 ? 0 : segment.SizeBytes / frameCapacity);
            }
            var characterAddresses = new OrchestrationValueAddress[characterTypes.Length];
            for (int slot = 0; slot < characterAddresses.Length; slot++)
            {
                OrchestrationValueSlotType type = characterTypes.Span[slot];
                OrchestrationStateSegment segment = Segment(indices, OrchestrationStateSegmentRole.CharacterValues, type.ValueKind);
                characterAddresses[slot] = Address(in segment, type.ValueKind,
                    segment.ValueKind == 0 ? slot : type.TypeIndex);
            }
            var writer = new OrchestrationMemoryImageWriter(
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationStateLayoutImageHeader>());
            var header = new OrchestrationStateLayoutImageHeader
            {
                FrameCapacity = frameCapacity,
                TimelineRegion = timelineRegion
            };
            header.Segments = writer.Add<OrchestrationStateSegment>(m_Segments);
            header.Indices = writer.Add<OrchestrationStateSegmentIndex>(indices);
            header.FramePayloads = writer.Add<OrchestrationFramePayloadSegment>(framePayloads);
            header.CharacterAddresses = writer.Add<OrchestrationValueAddress>(characterAddresses);
            var records = new OrchestrationStateProgramRecord[programs.Length];
            for (int program = 0; program < programs.Length; program++)
            {
                OrchestrationPreparedProgramStorage<TValue> prepared = programs.Span[program];
                OrchestrationProgramStorageLayout storage = prepared.Storage;
                var payloadSizes = new int[checked(storage.Owners.Length * payloadSegments.Count)];
                for (int owner = 0; owner < storage.Owners.Length; owner++)
                {
                    ref readonly OrchestrationExecutionOwnerStorage source = ref storage.Owners.Span[owner];
                    if (source.ValueKindCounts.IsEmpty)
                        continue;
                    int start = owner * payloadSegments.Count;
                    for (int i = 0; i < payloadSegments.Count; i++)
                    {
                        OrchestrationStateSegment segment = payloadSegments[i];
                        payloadSizes[start + i] = checked(PayloadElementCount(in segment, in source) *
                            segment.ElementSizeBytes);
                    }
                }
                var addresses = new OrchestrationFrameValueAddress[frameCapacity == 0 ? 0 : storage.Values.Length];
                for (int slot = 0; slot < addresses.Length; slot++)
                {
                    OrchestrationValueStorageSlot value = storage.Values.Span[slot];
                    if (value.ValueIndex < 0)
                        continue;
                    OrchestrationValueKind kind = prepared.ValueTypes.Span[slot].ValueKind;
                    OrchestrationStateSegment segment = Segment(indices, OrchestrationStateSegmentRole.PrivateValues, kind);
                    addresses[slot] = new OrchestrationFrameValueAddress(Address(in segment, kind,
                        segment.ValueKind == 0 ? value.ValueIndex : value.TypeIndex), segment.SizeBytes / frameCapacity);
                }
                records[program] = new OrchestrationStateProgramRecord(prepared.SkillContentId,
                    writer.Add(storage.OperationIndices.Span), writer.Add(storage.Values.Span),
                    writer.Add<int>(payloadSizes),
                    writer.Add<OrchestrationFrameValueAddress>(addresses));
            }
            header.Programs = writer.Add<OrchestrationStateProgramRecord>(records);
            m_Image = writer.Finish(in header);
        }

        public OrchestrationStateLayout Layout { get; }
        public int FrameCapacity { get; }
        public ReadOnlyMemory<OrchestrationStateSegment> Segments => m_Segments;
        public ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> Programs { get; }
        public ReadOnlyMemory<OrchestrationValueSlotType> CharacterTypes { get; }
        public OrchestrationStateRegion<byte> TimelineRegion { get; }
        public OrchestrationStateLayout TimelineLayout { get; }
        public ReadOnlyMemory<byte> Image => m_Image;
        public OrchestrationStateLayoutView View => new OrchestrationStateLayoutView(m_Image);

        public OrchestrationPreparedStateLayout<TValue> PrepareSource(int frameCapacity)
        {
            var segments = new List<OrchestrationStateSegment>();
            int offset = 0;
            int alignment = 1;
            foreach (OrchestrationStateSegment segment in m_Segments)
            {
                if (segment.Role >= OrchestrationStateSegmentRole.TimelinePlaybacks)
                    continue;
                int length = segment.PerFrame ? segment.Length / FrameCapacity * frameCapacity : segment.Length;
                offset = checked((offset + segment.Alignment - 1) / segment.Alignment * segment.Alignment);
                segments.Add(new OrchestrationStateSegment(offset, length, segment.ElementSizeBytes,
                    segment.Alignment, segment.Role, segment.ValueKind, segment.PerFrame));
                offset = checked(offset + length * segment.ElementSizeBytes);
                alignment = Math.Max(alignment, segment.Alignment);
            }
            int size = checked((offset + alignment - 1) / alignment * alignment);
            var layout = new OrchestrationStateLayout(size, alignment,
                StableHash.Compute(Layout.Identity.ToString(), "source", frameCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return new OrchestrationPreparedStateLayout<TValue>(layout, segments.ToArray(), frameCapacity,
                Programs, CharacterTypes, new OrchestrationStateRegion<byte>(size, 0), default);
        }

        OrchestrationStateSegment Segment(OrchestrationStateSegmentIndex[] indices,
            OrchestrationStateSegmentRole role, OrchestrationValueKind valueKind) =>
            m_Segments[indices[(int)role * KindCount + (int)valueKind].Segment];

        static int PayloadElementCount(in OrchestrationStateSegment segment,
            in OrchestrationExecutionOwnerStorage storage)
        {
            switch (segment.Role)
            {
                case OrchestrationStateSegmentRole.Operations: return storage.OperationCount;
                case OrchestrationStateSegmentRole.SavedOutputs: return storage.SavedOutputCount;
                case OrchestrationStateSegmentRole.PrivateMetadata: return storage.BlackboardCount;
                case OrchestrationStateSegmentRole.PrivateValues:
                    if (segment.ValueKind == 0)
                        return storage.BlackboardCount;
                    int count = storage.ValueKindCounts.Span[(int)segment.ValueKind];
                    return segment.ValueKind == OrchestrationValueKind.Boolean ? (count + 63) / 64 : count;
                default: throw new InvalidOperationException("区段不是执行帧载荷。");
            }
        }

        static OrchestrationValueAddress Address(in OrchestrationStateSegment segment,
            OrchestrationValueKind kind, int index) =>
            kind == OrchestrationValueKind.Boolean && segment.ValueKind != 0
                ? new OrchestrationValueAddress(kind, segment.OffsetBytes + index / 64 * segment.ElementSizeBytes,
                    segment.ElementSizeBytes, index % 64)
                : new OrchestrationValueAddress(kind, segment.OffsetBytes + index * segment.ElementSizeBytes,
                    segment.ElementSizeBytes);
    }
}
