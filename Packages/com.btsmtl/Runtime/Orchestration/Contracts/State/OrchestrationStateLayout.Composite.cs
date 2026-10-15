using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStateLayout, StructLayout(LayoutKind.Auto)]
    public readonly partial struct OrchestrationStateLayout<TValue, TPlayback>
        where TValue : unmanaged
        where TPlayback : unmanaged
    {
        public OrchestrationCharacterBlackboardStateLayout<TValue> Blackboard { get; }
        public OrchestrationExecutionStateLayout<TValue> Execution { get; }
        public OrchestrationTimelineStateLayout<TPlayback> Timeline { get; }
    }
}
