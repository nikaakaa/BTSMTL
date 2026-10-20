using System;
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
        const int KindCount = (int)OrchestrationValueKind.SpatialIdentity + 1;
        readonly OrchestrationStateSegment[] m_Segments;
        readonly int[] m_Indices;
        readonly OrchestrationValueAddress[] m_CharacterAddresses;
        readonly FrameValueAddress[][] m_PrivateAddresses;

        readonly struct FrameValueAddress
        {
            internal FrameValueAddress(OrchestrationValueAddress address, int stride)
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
            m_Indices = new int[Enum.GetValues(typeof(OrchestrationStateSegmentRole)).Length * KindCount];
            Array.Fill(m_Indices, -1);
            for (int i = 0; i < m_Segments.Length; i++)
            {
                OrchestrationStateSegment segment = m_Segments[i];
                int start = (int)segment.Role * KindCount;
                if ((segment.Role == OrchestrationStateSegmentRole.CharacterValues ||
                    segment.Role == OrchestrationStateSegmentRole.PrivateValues) && segment.ValueKind == 0)
                    for (int kind = 1; kind < KindCount; kind++)
                        m_Indices[start + kind] = i;
                m_Indices[start + (int)segment.ValueKind] = i;
            }
            m_CharacterAddresses = new OrchestrationValueAddress[characterTypes.Length];
            for (int slot = 0; slot < m_CharacterAddresses.Length; slot++)
            {
                OrchestrationValueSlotType type = characterTypes.Span[slot];
                OrchestrationStateSegment segment = Segment(OrchestrationStateSegmentRole.CharacterValues, type.ValueKind);
                m_CharacterAddresses[slot] = Address(in segment, type.ValueKind,
                    segment.ValueKind == 0 ? slot : type.TypeIndex);
            }
            m_PrivateAddresses = new FrameValueAddress[programs.Length][];
            if (frameCapacity == 0)
                return;
            for (int program = 0; program < programs.Length; program++)
            {
                OrchestrationPreparedProgramStorage<TValue> prepared = programs.Span[program];
                var addresses = new FrameValueAddress[prepared.Storage.Values.Length];
                for (int slot = 0; slot < addresses.Length; slot++)
                {
                    OrchestrationValueStorageSlot storage = prepared.Storage.Values.Span[slot];
                    if (storage.ValueIndex < 0)
                        continue;
                    OrchestrationValueKind kind = prepared.ValueTypes.Span[slot].ValueKind;
                    OrchestrationStateSegment segment = Segment(OrchestrationStateSegmentRole.PrivateValues, kind);
                    addresses[slot] = new FrameValueAddress(Address(in segment, kind,
                        segment.ValueKind == 0 ? storage.ValueIndex : storage.TypeIndex), segment.SizeBytes / frameCapacity);
                }
                m_PrivateAddresses[program] = addresses;
            }
        }

        public OrchestrationStateLayout Layout { get; }
        public int FrameCapacity { get; }
        public ReadOnlyMemory<OrchestrationStateSegment> Segments => m_Segments;
        public ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> Programs { get; }
        public ReadOnlyMemory<OrchestrationValueSlotType> CharacterTypes { get; }
        public OrchestrationStateRegion<byte> TimelineRegion { get; }
        public OrchestrationStateLayout TimelineLayout { get; }

        public int ProgramIndex(int skillContentId)
        {
            ReadOnlySpan<OrchestrationPreparedProgramStorage<TValue>> programs = Programs.Span;
            int index = 0;
            while (programs[index].SkillContentId != skillContentId)
                index++;
            return index;
        }

        public OrchestrationStateSegment Segment(OrchestrationStateSegmentRole role,
            OrchestrationValueKind valueKind = 0) => m_Segments[m_Indices[(int)role * KindCount + (int)valueKind]];

        public OrchestrationValueAddress CharacterAddress(int slot) => m_CharacterAddresses[slot];
        public OrchestrationValueAddress PrivateAddress(int program, int frame, int valueSlot) =>
            m_PrivateAddresses[program][valueSlot].AtFrame(frame);

        public int PayloadCount(in OrchestrationStateSegment segment, int program, int owner)
        {
            OrchestrationPreparedProgramStorage<TValue> prepared = Programs.Span[program];
            OrchestrationExecutionOwnerStorage storage = prepared.Storage.Owners.Span[owner];
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
                default: throw new InvalidOperationException($"区段'{segment.Role}'不是执行帧载荷。");
            }
        }

        public OrchestrationStateRegion<byte> PayloadRegion(in OrchestrationStateSegment segment,
            int frame, int program, int owner) =>
            new OrchestrationStateRegion<byte>(segment.OffsetBytes + frame * (segment.SizeBytes / FrameCapacity),
                PayloadCount(in segment, program, owner) * segment.ElementSizeBytes);

        public OrchestrationPreparedStateLayout<TValue> PrepareSource(int frameCapacity)
        {
            var segments = new System.Collections.Generic.List<OrchestrationStateSegment>();
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

        static OrchestrationValueAddress Address(in OrchestrationStateSegment segment,
            OrchestrationValueKind kind, int index) =>
            kind == OrchestrationValueKind.Boolean && segment.ValueKind != 0
                ? new OrchestrationValueAddress(kind, segment.OffsetBytes + index / 64 * segment.ElementSizeBytes,
                    segment.ElementSizeBytes, index % 64)
                : new OrchestrationValueAddress(kind, segment.OffsetBytes + index * segment.ElementSizeBytes,
                    segment.ElementSizeBytes);
    }
}
