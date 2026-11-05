using System;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using Photon.Deterministic;
using UnityEngine;
using FPGraphValues = BTSMTL.Graphs.GraphNumericValues<ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValue,
    Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP,
    Photon.Deterministic.FPQuaternion, ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValueAccess,
    ThirdPersonSimulation.QuantumNumerics.FPGraphNumerics>;

namespace ThirdPersonSimulation.QuantumNumerics
{
    // FP 图宿主数值方案：执行运算转调 FPOrchestrationNumerics，宿主值转换与状态布局随方案提供。
    public readonly struct FPGraphNumerics : IGraphNumerics<FPOrchestrationValue>,
        IGraphNumericAdapter<FP, FPVector2, FPVector3, FPQuaternion>
    {
        public OrchestrationValueKind Kind(in FPOrchestrationValue value) => default(FPOrchestrationNumerics).Kind(in value);
        public bool Boolean(in FPOrchestrationValue value) => default(FPOrchestrationNumerics).Boolean(in value);
        public ulong UInt64(in FPOrchestrationValue value) => default(FPOrchestrationNumerics).UInt64(in value);
        public ObjectHandle ObjectHandle(in FPOrchestrationValue value) => default(FPOrchestrationNumerics).ObjectHandle(in value);
        public OrchestrationIdentityValue Identity(in FPOrchestrationValue value) => default(FPOrchestrationNumerics).Identity(in value);
        public FPOrchestrationValue FromBoolean(bool value) => default(FPOrchestrationNumerics).FromBoolean(value);
        public FPOrchestrationValue FromInt32(int value) => default(FPOrchestrationNumerics).FromInt32(value);
        public FPOrchestrationValue FromUInt64(ulong value) => default(FPOrchestrationNumerics).FromUInt64(value);
        public FPOrchestrationValue FromObjectHandle(ObjectHandle value) => default(FPOrchestrationNumerics).FromObjectHandle(value);
        public FPOrchestrationValue FromTime(OrchestrationTime time) => default(FPOrchestrationNumerics).FromTime(time);

        public void Unary(OrchestrationValueOpcode code, in FPOrchestrationValue source,
            in OrchestrationIdentityOrdinalView identities, ref FPOrchestrationValue result) =>
            default(FPOrchestrationNumerics).Unary(code, in source, in identities, ref result);

        public void Compute(OrchestrationValueOpcode code, ReadOnlySpan<FPOrchestrationValue> inputs,
            ReadOnlySpan<int> arguments, in OrchestrationIdentityOrdinalView identities, ref FPOrchestrationValue result) =>
            default(FPOrchestrationNumerics).Compute(code, inputs, arguments, in identities, ref result);

        public void Assign(OrchestrationAssignmentOpcode code, in FPOrchestrationValue current,
            in FPOrchestrationValue value, ref FPOrchestrationValue result) =>
            default(FPOrchestrationNumerics).Assign(code, in current, in value, ref result);

        public FPOrchestrationValue MapContentIdentities(in FPOrchestrationValue value, ReadOnlySpan<int> mapping) =>
            default(FPOrchestrationNumerics).MapContentIdentities(in value, mapping);

        public FPOrchestrationValue FromIdentity(OrchestrationIdentityValue identity) =>
            default(FPOrchestrationValueAccess).FromIdentity(identity);

        public int EnumTypeContentId(in FPOrchestrationValue value) =>
            default(FPOrchestrationValueAccess).Enum(in value).TypeContentId;

        public Type ValueType(in FPOrchestrationValue value, OrchestrationIdentityTable identities) =>
            FPGraphValues.ValueType(in value, identities);

        public FPOrchestrationValue ToState(in GraphHostValue value, int enumTypeContentId,
            in OrchestrationIdentityValue identity) => FPGraphValues.ToState(in value, enumTypeContentId, in identity);

        public GraphHostValue FromState(in FPOrchestrationValue value, Type enumType, string identity) =>
            FPGraphValues.FromState(in value, enumType, identity);

        public OrchestrationExecutionStateLayout<FPOrchestrationValue> CreateStateLayout(StableHash contentIdentity,
            NumericProfileId numericProfile, TargetAbiVersion targetAbi, int blackboardMetadataCapacity,
            int framesCapacity, int operationsCapacity, int rosterCapacity, int savedOutputsCapacity, int valuesCapacity) =>
            FPGraphStateLayouts.Create(contentIdentity, numericProfile, targetAbi, blackboardMetadataCapacity,
                framesCapacity, operationsCapacity, rosterCapacity, savedOutputsCapacity, valuesCapacity);

        public IGraphRuntimeCore<TContext> CreateGraphRuntime<TContext>(GraphRuntimeSetup setup) where TContext : struct =>
            new HostedGraphRuntime<FPOrchestrationValue, FPGraphNumerics, TContext>(setup.Source, setup.Contract,
                (OrchestrationGraphContent<FPOrchestrationValue>)setup.Content, setup.Extensions);

        public FP FromSingle(float value) => QuantumScalarBoundary.ConvertExternal(value, nameof(value));
        public float ToSingle(FP value) => value.AsFloat;
        public FPVector2 FromUnityVector2(Vector2 value) => new FPVector2(FromSingle(value.x), FromSingle(value.y));
        public Vector2 ToUnityVector2(FPVector2 value) => new Vector2(value.X.AsFloat, value.Y.AsFloat);
        public FPVector3 FromUnityVector3(Vector3 value) =>
            new FPVector3(FromSingle(value.x), FromSingle(value.y), FromSingle(value.z));
        public Vector3 ToUnityVector3(FPVector3 value) => new Vector3(value.X.AsFloat, value.Y.AsFloat, value.Z.AsFloat);
        public FPQuaternion FromUnityQuaternion(Quaternion value) =>
            new FPQuaternion(FromSingle(value.x), FromSingle(value.y), FromSingle(value.z), FromSingle(value.w));
        public Quaternion ToUnityQuaternion(FPQuaternion value) =>
            new Quaternion(value.X.AsFloat, value.Y.AsFloat, value.Z.AsFloat, value.W.AsFloat);
    }

    static partial class FPGraphStateLayouts
    {
        [ValueStateLayoutFactory]
        internal static partial OrchestrationExecutionStateLayout<FPOrchestrationValue> Create(
            StableHash contentIdentity, NumericProfileId numericProfile, TargetAbiVersion targetAbi,
            int blackboardMetadataCapacity, int framesCapacity, int operationsCapacity,
            int rosterCapacity, int savedOutputsCapacity, int valuesCapacity);
    }
}
