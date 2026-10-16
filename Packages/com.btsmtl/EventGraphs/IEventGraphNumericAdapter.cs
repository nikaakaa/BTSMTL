using UnityEngine;

namespace BTSMTL.EventGraphs
{
    public interface IEventGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
    {
        TScalar FromSingle(float value);
        float ToSingle(TScalar value);
        TVector2 FromUnityVector2(Vector2 value);
        Vector2 ToUnityVector2(TVector2 value);
        TVector3 FromUnityVector3(Vector3 value);
        Vector3 ToUnityVector3(TVector3 value);
        TQuaternion FromUnityQuaternion(Quaternion value);
        Quaternion ToUnityQuaternion(TQuaternion value);
    }
}
