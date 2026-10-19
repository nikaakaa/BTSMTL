using System.Runtime.InteropServices;
using BTSMTL.Orchestration;
using Photon.Deterministic;

namespace ThirdPersonSimulation.QuantumNumerics
{
    [ValueUnion(typeof(OrchestrationValueCases<FP, FPVector2, FPVector3, FP, FPQuaternion>), Layout = LayoutKind.Explicit)]
    [ValueStorage]
    public readonly partial struct FPOrchestrationValue { }

    [ValueUnionAccess(typeof(FPOrchestrationValue))]
    public readonly partial struct FPOrchestrationValueAccess :
        IOrchestrationValueAccess<FPOrchestrationValue, FP, FPVector2, FPVector3, FP, FPQuaternion> { }
}
