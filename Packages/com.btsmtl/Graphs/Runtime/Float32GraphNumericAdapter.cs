using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs
{
    public readonly struct Float32GraphNumericAdapter :
        IGraphNumericAdapter<Float32Scalar, Float32Vector2, Float32Vector3, Float32Quaternion>
    {
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
}
