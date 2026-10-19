using System;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationStateSegmentRole : byte
    {
        Unclassified,
        Frames,
        Roster,
        Operations,
        SavedOutputs,
        CharacterValues,
        CharacterMetadata,
        PrivateValues,
        PrivateMetadata,
        TimelinePlaybacks,
        TimelineSequences,
        TimelineTreeExitBits,
        TimelineTreeExitFirstCycles,
        TimelineTreeExitRequests
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class OrchestrationStateSegmentAttribute : Attribute
    {
        public OrchestrationStateSegmentAttribute(OrchestrationStateSegmentRole role,
            bool perFrame = false)
        {
            Role = role;
            PerFrame = perFrame;
        }

        public OrchestrationStateSegmentRole Role { get; }
        public OrchestrationValueKind ValueKind { get; set; }
        public bool PerFrame { get; }
    }

    public readonly struct OrchestrationStateSegment
    {
        public OrchestrationStateSegment(int offsetBytes, int length, int elementSizeBytes,
            int alignment, OrchestrationStateSegmentRole role, OrchestrationValueKind valueKind,
            bool perFrame)
        {
            OffsetBytes = offsetBytes;
            Length = length;
            ElementSizeBytes = elementSizeBytes;
            Alignment = alignment;
            Role = role;
            ValueKind = valueKind;
            PerFrame = perFrame;
        }

        public int OffsetBytes { get; }
        public int Length { get; }
        public int ElementSizeBytes { get; }
        public int Alignment { get; }
        public OrchestrationStateSegmentRole Role { get; }
        public OrchestrationValueKind ValueKind { get; }
        public bool PerFrame { get; }
        public int SizeBytes => checked(Length * ElementSizeBytes);

        public OrchestrationStateSegment AtOffset(int offsetBytes) =>
            new OrchestrationStateSegment(offsetBytes, Length, ElementSizeBytes, Alignment,
                Role, ValueKind, PerFrame);
    }
}
