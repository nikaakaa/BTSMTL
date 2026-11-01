using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;
using Float32GraphValues = BTSMTL.Graphs.GraphNumericValues<BTSMTL.Orchestration.Float32OrchestrationValue,
    ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3,
    ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32Quaternion, BTSMTL.Orchestration.Float32OrchestrationValueAccess,
    BTSMTL.Graphs.Float32GraphNumerics>;

namespace BTSMTL.Graphs
{
    // 内置 Float32 数值方案：执行运算转调 Float32OrchestrationNumerics，宿主值转换与状态布局随方案提供。
    public readonly struct Float32GraphNumerics : IGraphNumerics<Float32OrchestrationValue>,
        IGraphNumericAdapter<Float32Scalar, Float32Vector2, Float32Vector3, Float32Quaternion>
    {
        public OrchestrationValueKind Kind(in Float32OrchestrationValue value) => default(Float32OrchestrationNumerics).Kind(in value);
        public bool Boolean(in Float32OrchestrationValue value) => default(Float32OrchestrationNumerics).Boolean(in value);
        public ulong UInt64(in Float32OrchestrationValue value) => default(Float32OrchestrationNumerics).UInt64(in value);
        public ObjectHandle ObjectHandle(in Float32OrchestrationValue value) => default(Float32OrchestrationNumerics).ObjectHandle(in value);
        public OrchestrationIdentityValue Identity(in Float32OrchestrationValue value) => default(Float32OrchestrationNumerics).Identity(in value);
        public Float32OrchestrationValue FromBoolean(bool value) => default(Float32OrchestrationNumerics).FromBoolean(value);
        public Float32OrchestrationValue FromInt32(int value) => default(Float32OrchestrationNumerics).FromInt32(value);
        public Float32OrchestrationValue FromUInt64(ulong value) => default(Float32OrchestrationNumerics).FromUInt64(value);
        public Float32OrchestrationValue FromObjectHandle(ObjectHandle value) => default(Float32OrchestrationNumerics).FromObjectHandle(value);
        public Float32OrchestrationValue FromTime(OrchestrationTime time) => default(Float32OrchestrationNumerics).FromTime(time);

        public void Unary(OrchestrationValueOpcode code, in Float32OrchestrationValue source,
            in OrchestrationIdentityOrdinalView identities, ref Float32OrchestrationValue result) =>
            default(Float32OrchestrationNumerics).Unary(code, in source, in identities, ref result);

        public void Compute(OrchestrationValueOpcode code, ReadOnlySpan<Float32OrchestrationValue> inputs,
            ReadOnlySpan<int> arguments, in OrchestrationIdentityOrdinalView identities,
            ref Float32OrchestrationValue result) =>
            default(Float32OrchestrationNumerics).Compute(code, inputs, arguments, in identities, ref result);

        public void Assign(OrchestrationAssignmentOpcode code, in Float32OrchestrationValue current,
            in Float32OrchestrationValue value, ref Float32OrchestrationValue result) =>
            default(Float32OrchestrationNumerics).Assign(code, in current, in value, ref result);

        public Float32OrchestrationValue MapContentIdentities(in Float32OrchestrationValue value, ReadOnlySpan<int> mapping) =>
            default(Float32OrchestrationNumerics).MapContentIdentities(in value, mapping);

        public Float32OrchestrationValue FromIdentity(OrchestrationIdentityValue identity) =>
            default(Float32OrchestrationValueAccess).FromIdentity(identity);

        public int EnumTypeContentId(in Float32OrchestrationValue value) =>
            default(Float32OrchestrationValueAccess).Enum(in value).TypeContentId;

        public Type ValueType(in Float32OrchestrationValue value, OrchestrationIdentityTable identities) =>
            Float32GraphValues.ValueType(in value, identities);

        public Float32OrchestrationValue ToState(in GraphHostValue value, int enumTypeContentId,
            in OrchestrationIdentityValue identity) => Float32GraphValues.ToState(in value, enumTypeContentId, in identity);

        public GraphHostValue FromState(in Float32OrchestrationValue value, Type enumType, string identity) =>
            Float32GraphValues.FromState(in value, enumType, identity);

        public OrchestrationExecutionStateLayout<Float32OrchestrationValue> CreateStateLayout(StableHash contentIdentity,
            NumericProfileId numericProfile, TargetAbiVersion targetAbi, int blackboardMetadataCapacity,
            int framesCapacity, int operationsCapacity, int rosterCapacity, int savedOutputsCapacity, int valuesCapacity) =>
            Float32GraphStateLayouts.Create(contentIdentity, numericProfile, targetAbi, blackboardMetadataCapacity,
                framesCapacity, operationsCapacity, rosterCapacity, savedOutputsCapacity, valuesCapacity);

        public IGraphRuntimeCore<TContext> CreateGraphRuntime<TContext>(GraphRuntimeSetup setup) where TContext : struct =>
            new HostedGraphRuntime<Float32OrchestrationValue, Float32GraphNumerics, TContext>(setup.Source, setup.Contract,
                setup.RequireEncoding<Float32OrchestrationValue>(), setup.Extensions);

        public Float32Scalar FromSingle(float value) => Float32Scalar.FromSingle(value);
        public float ToSingle(Float32Scalar value) => value.ToSingle();
        public Float32Vector2 FromUnityVector2(Vector2 value) => new Float32Vector2(
            Float32Scalar.FromSingle(value.x), Float32Scalar.FromSingle(value.y));
        public Vector2 ToUnityVector2(Float32Vector2 value) => new Vector2(value.X.ToSingle(), value.Y.ToSingle());
        public Float32Vector3 FromUnityVector3(Vector3 value) => new Float32Vector3(
            Float32Scalar.FromSingle(value.x), Float32Scalar.FromSingle(value.y), Float32Scalar.FromSingle(value.z));
        public Vector3 ToUnityVector3(Float32Vector3 value) => new Vector3(value.X.ToSingle(), value.Y.ToSingle(), value.Z.ToSingle());
        public Float32Quaternion FromUnityQuaternion(Quaternion value) => new Float32Quaternion(
            Float32Scalar.FromSingle(value.x), Float32Scalar.FromSingle(value.y),
            Float32Scalar.FromSingle(value.z), Float32Scalar.FromSingle(value.w));
        public Quaternion ToUnityQuaternion(Float32Quaternion value) => new Quaternion(
            value.X.ToSingle(), value.Y.ToSingle(), value.Z.ToSingle(), value.W.ToSingle());
    }

    static partial class Float32GraphStateLayouts
    {
        [ValueStateLayoutFactory]
        internal static partial OrchestrationExecutionStateLayout<Float32OrchestrationValue> Create(
            StableHash contentIdentity, NumericProfileId numericProfile, TargetAbiVersion targetAbi,
            int blackboardMetadataCapacity, int framesCapacity, int operationsCapacity,
            int rosterCapacity, int savedOutputsCapacity, int valuesCapacity);
    }
}
