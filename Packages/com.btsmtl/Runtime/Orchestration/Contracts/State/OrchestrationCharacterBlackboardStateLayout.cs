using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStateLayout, StructLayout(LayoutKind.Auto)]
    public readonly partial struct OrchestrationCharacterBlackboardStateLayout<TValue>
        where TValue : unmanaged
    {
        public OrchestrationStateRegion<OrchestrationBlackboardMetadata> Metadata { get; }
        public OrchestrationStateRegion<TValue> Values { get; }
    }
}
