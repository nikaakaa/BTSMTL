using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStateLayout, StructLayout(LayoutKind.Auto)]
    public readonly partial struct OrchestrationCharacterBlackboardStateLayout<TValue>
        where TValue : unmanaged
    {
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.CharacterMetadata)]
        public OrchestrationStateRegion<OrchestrationBlackboardMetadata> Metadata { get; }
        [OrchestrationStateSegment(OrchestrationStateSegmentRole.CharacterValues)]
        public OrchestrationStateRegion<TValue> Values { get; }
    }
}
