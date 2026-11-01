using System;
using ThirdPersonSimulation;
using Float32ValueMath = BTSMTL.Orchestration.OrchestrationTypedValueMath<BTSMTL.Orchestration.Float32OrchestrationValue,
    ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3,
    ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32Quaternion, ThirdPersonSimulation.Float32SimulationMath,
    BTSMTL.Orchestration.Float32OrchestrationValueAccess>;

namespace BTSMTL.Orchestration
{
    // 数值方案：执行器只经此按编排值运算，标量、向量等底层类型只在方案内部出现。
    // 具体方案转调 OrchestrationTypedValueMath，指令算法只有一份。
    public interface IOrchestrationNumerics<TValue> where TValue : unmanaged
    {
        OrchestrationValueKind Kind(in TValue value);
        bool Boolean(in TValue value);
        ulong UInt64(in TValue value);
        ObjectHandle ObjectHandle(in TValue value);
        OrchestrationIdentityValue Identity(in TValue value);
        TValue FromBoolean(bool value);
        TValue FromInt32(int value);
        TValue FromUInt64(ulong value);
        TValue FromObjectHandle(ObjectHandle value);
        TValue FromTime(OrchestrationTime time);

        void Unary(OrchestrationValueOpcode code, in TValue source,
            in OrchestrationIdentityOrdinalView identities, ref TValue result);

        void Compute(OrchestrationValueOpcode code, ReadOnlySpan<TValue> inputs,
            ReadOnlySpan<int> arguments, in OrchestrationIdentityOrdinalView identities, ref TValue result);

        void Assign(OrchestrationAssignmentOpcode code, in TValue current, in TValue value, ref TValue result);

        TValue MapContentIdentities(in TValue value, ReadOnlySpan<int> mapping);
    }

    public readonly struct Float32OrchestrationNumerics : IOrchestrationNumerics<Float32OrchestrationValue>
    {
        public OrchestrationValueKind Kind(in Float32OrchestrationValue value) =>
            default(Float32OrchestrationValueAccess).Kind(in value);

        public bool Boolean(in Float32OrchestrationValue value) =>
            default(Float32OrchestrationValueAccess).Boolean(in value);

        public ulong UInt64(in Float32OrchestrationValue value) =>
            default(Float32OrchestrationValueAccess).UInt64(in value);

        public ObjectHandle ObjectHandle(in Float32OrchestrationValue value) =>
            default(Float32OrchestrationValueAccess).ObjectHandle(in value);

        public OrchestrationIdentityValue Identity(in Float32OrchestrationValue value) =>
            default(Float32OrchestrationValueAccess).Identity(in value);

        public Float32OrchestrationValue FromBoolean(bool value) =>
            default(Float32OrchestrationValueAccess).FromBoolean(value);

        public Float32OrchestrationValue FromInt32(int value) =>
            default(Float32OrchestrationValueAccess).FromInt32(value);

        public Float32OrchestrationValue FromUInt64(ulong value) =>
            default(Float32OrchestrationValueAccess).FromUInt64(value);

        public Float32OrchestrationValue FromObjectHandle(ObjectHandle value) =>
            default(Float32OrchestrationValueAccess).FromObjectHandle(value);

        public Float32OrchestrationValue FromTime(OrchestrationTime time) =>
            default(Float32OrchestrationValueAccess).FromScalar(Float32Scalar.FromSingle(time.AsSingle));

        public void Unary(OrchestrationValueOpcode code, in Float32OrchestrationValue source,
            in OrchestrationIdentityOrdinalView identities, ref Float32OrchestrationValue result) =>
            Float32ValueMath.Unary(code, in source, in identities, ref result);

        public void Compute(OrchestrationValueOpcode code, ReadOnlySpan<Float32OrchestrationValue> inputs,
            ReadOnlySpan<int> arguments, in OrchestrationIdentityOrdinalView identities,
            ref Float32OrchestrationValue result) =>
            Float32ValueMath.Compute(code, inputs, arguments, in identities, ref result);

        public void Assign(OrchestrationAssignmentOpcode code, in Float32OrchestrationValue current,
            in Float32OrchestrationValue value, ref Float32OrchestrationValue result) =>
            Float32ValueMath.Assign(code, in current, in value, ref result);

        public Float32OrchestrationValue MapContentIdentities(in Float32OrchestrationValue value, ReadOnlySpan<int> mapping) =>
            Float32ValueMath.MapContentIdentities(in value, mapping);
    }
}
