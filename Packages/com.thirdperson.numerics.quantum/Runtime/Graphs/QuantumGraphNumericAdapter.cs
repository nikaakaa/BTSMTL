using BTSMTL.Graphs;
using Photon.Deterministic;
using UnityEngine;

namespace ThirdPersonSimulation.QuantumNumerics
{
    public readonly struct QuantumGraphNumericAdapter :
        IGraphNumericAdapter<FP, FPVector2, FPVector3, FPQuaternion>
    {
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
}
