using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueUnion(typeof(OrchestrationValueCases<Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, Float32Quaternion>), Layout = LayoutKind.Explicit)]
    [ValueStorage]
    public readonly partial struct Float32OrchestrationValue { }

    [ValueUnionAccess(typeof(Float32OrchestrationValue))]
    public readonly partial struct Float32OrchestrationValueAccess :
        IOrchestrationValueAccess<Float32OrchestrationValue, Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, Float32Quaternion> { }
}
