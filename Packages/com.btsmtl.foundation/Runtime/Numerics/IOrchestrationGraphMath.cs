namespace ThirdPersonSimulation
{
    public interface IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion> :
        IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
    {
        TScalar NormalizationMinimumMagnitude { get; }
        int TruncateToInt32(TScalar value);
        TScalar ClampGraph(TScalar value, TScalar minimum, TScalar maximum);
        TScalar MoveTowardsGraph(TScalar current, TScalar target, TScalar maximumDelta);
        TVector2 MultiplyVector2(TVector2 value, TScalar scale);
        TVector2 SubtractVector2(TVector2 left, TVector2 right);
        TScalar SignedAngle2(TVector2 from, TVector2 to);
        TVector3 QuaternionForward(TQuaternion value);
        TQuaternion QuaternionAngleAxis(TScalar angleDegrees, TVector3 axis);
    }
}
