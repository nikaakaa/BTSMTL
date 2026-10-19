using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStateLayout, StructLayout(LayoutKind.Auto)]
    public readonly partial struct OrchestrationTimelineStateLayout<TPlayback>
        where TPlayback : unmanaged
    {
        public OrchestrationStateRegion<TPlayback> Playbacks { get; }
        public OrchestrationStateRegion<OrchestrationTimelineSequenceState> Sequences { get; }
        public OrchestrationStateRegion<ulong> TreeExitBits { get; }
        public OrchestrationStateRegion<long> TreeExitFirstCycles { get; }
        public OrchestrationStateRegion<OrchestrationTimelineClipOccurrence> TreeExitRequests { get; }
    }
}
