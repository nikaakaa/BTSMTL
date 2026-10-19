using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStateLayout, StructLayout(LayoutKind.Auto)]
    public readonly partial struct OrchestrationTimelineStateLayout<TPlayback>
        where TPlayback : unmanaged
    {
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.TimelinePlaybacks)]
        public OrchestrationStateRegion<TPlayback> Playbacks { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.TimelineSequences)]
        public OrchestrationStateRegion<OrchestrationTimelineSequenceState> Sequences { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.TimelineTreeExitBits)]
        public OrchestrationStateRegion<ulong> TreeExitBits { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.TimelineTreeExitFirstCycles)]
        public OrchestrationStateRegion<long> TreeExitFirstCycles { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.TimelineTreeExitRequests)]
        public OrchestrationStateRegion<OrchestrationTimelineClipOccurrence> TreeExitRequests { get; }
    }
}
