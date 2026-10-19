using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStateLayout, StructLayout(LayoutKind.Auto)]
    public readonly partial struct OrchestrationExecutionStateLayout<TValue>
        where TValue : unmanaged
    {
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.PrivateMetadata, perFrame: true)]
        public OrchestrationStateRegion<OrchestrationBlackboardMetadata> BlackboardMetadata { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.Frames, perFrame: true)]
        public OrchestrationStateRegion<OrchestrationExecutionFrameState> Frames { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.Operations, perFrame: true)]
        public OrchestrationStateRegion<OrchestrationOperationState> Operations { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.Roster)]
        public OrchestrationStateRegion<OrchestrationExecutionRosterState> Roster { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.SavedOutputs, perFrame: true)]
        public OrchestrationStateRegion<TValue> SavedOutputs { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.PrivateValues, perFrame: true)]
        public OrchestrationStateRegion<TValue> Values { get; }
    }
}
