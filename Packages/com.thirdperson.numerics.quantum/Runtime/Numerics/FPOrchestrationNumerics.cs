using System;
using BTSMTL.Orchestration;
using Photon.Deterministic;
using FPValueMath = BTSMTL.Orchestration.OrchestrationTypedValueMath<ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValue,
    Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP,
    Photon.Deterministic.FPQuaternion, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath,
    ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValueAccess>;

namespace ThirdPersonSimulation.QuantumNumerics
{
    public readonly struct FPOrchestrationNumerics : IOrchestrationNumerics<FPOrchestrationValue>
    {
        // FP 与 OrchestrationTime 的 Raw 可直接互换的前提；精度不同时此处编译失败。
        internal const int MatchingPrecision = 1 / (FP.Precision == OrchestrationTime.Precision ? 1 : 0);

        public bool Boolean(in FPOrchestrationValue value) => default(FPOrchestrationValueAccess).Boolean(in value);

        public ulong UInt64(in FPOrchestrationValue value) => default(FPOrchestrationValueAccess).UInt64(in value);

        public OrchestrationIdentityValue Identity(in FPOrchestrationValue value) =>
            default(FPOrchestrationValueAccess).Identity(in value);

        public FPOrchestrationValue FromBoolean(bool value) => default(FPOrchestrationValueAccess).FromBoolean(value);

        public FPOrchestrationValue FromInt32(int value) => default(FPOrchestrationValueAccess).FromInt32(value);

        public FPOrchestrationValue FromUInt64(ulong value) => default(FPOrchestrationValueAccess).FromUInt64(value);

        public FPOrchestrationValue FromTime(OrchestrationTime time) =>
            default(FPOrchestrationValueAccess).FromScalar(FP.FromRaw(time.RawValue));

        public void Unary(OrchestrationValueOpcode code, in FPOrchestrationValue source,
            in OrchestrationIdentityOrdinalView identities, ref FPOrchestrationValue result) =>
            FPValueMath.Unary(code, in source, in identities, ref result);

        public void Compute(OrchestrationValueOpcode code, ReadOnlySpan<FPOrchestrationValue> inputs,
            ReadOnlySpan<int> arguments, in OrchestrationIdentityOrdinalView identities,
            ref FPOrchestrationValue result) =>
            FPValueMath.Compute(code, inputs, arguments, in identities, ref result);

        public void Assign(OrchestrationAssignmentOpcode code, in FPOrchestrationValue current,
            in FPOrchestrationValue value, ref FPOrchestrationValue result) =>
            FPValueMath.Assign(code, in current, in value, ref result);
    }
}
