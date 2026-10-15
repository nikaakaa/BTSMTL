using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStateLayout, StructLayout(LayoutKind.Auto)]
    public readonly partial struct OrchestrationExecutionStateLayout<TValue>
        where TValue : unmanaged
    {
        public OrchestrationStateRegion<OrchestrationBlackboardMetadata> BlackboardMetadata { get; }
        public OrchestrationStateRegion<OrchestrationExecutionFrameState> Frames { get; }
        public OrchestrationStateRegion<OrchestrationOperationState> Operations { get; }
        public OrchestrationStateRegion<OrchestrationExecutionRosterState> Roster { get; }
        public OrchestrationStateRegion<TValue> Values { get; }
    }
}
