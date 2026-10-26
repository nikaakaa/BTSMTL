using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public static class OrchestrationTypedValueMath<TValue, TScalar, TVector2, TVector3,
        TYaw, TQuaternion, TMath, TValues>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
    {
        public static void Unary(OrchestrationValueOpcode code, in TValue source,
            in OrchestrationIdentityOrdinalView identities, ref TValue result)
        {
            TValues values = default;
            TMath math = default;
            switch (code)
            {
                case OrchestrationValueOpcode.Copy: result = source; return;
                case OrchestrationValueOpcode.Int32ToBoolean: result = values.FromBoolean(values.Int32(in source) != 0); return;
                case OrchestrationValueOpcode.UInt64ToBoolean: result = values.FromBoolean(values.UInt64(in source) != 0); return;
                case OrchestrationValueOpcode.ScalarToBoolean: result = values.FromBoolean(!math.IsZero(values.Scalar(in source))); return;
                case OrchestrationValueOpcode.IdentityToBoolean: result = values.FromBoolean(!identities.IsEmpty(values.Identity(in source))); return;
                case OrchestrationValueOpcode.BooleanToScalar: result = values.FromScalar(values.Boolean(in source) ? math.One : math.Zero); return;
                case OrchestrationValueOpcode.Int32ToScalar: result = values.FromScalar(math.FromInt32(values.Int32(in source))); return;
                case OrchestrationValueOpcode.UInt64ToScalar:
                    result = values.FromScalar(math.FromInt64(checked((long)values.UInt64(in source))));
                    return;
                case OrchestrationValueOpcode.ScalarToInt32: result = values.FromInt32(math.TruncateToInt32(values.Scalar(in source))); return;
                default: throw new ArgumentOutOfRangeException(nameof(code));
            }
        }

        public static void Compute(OrchestrationValueOpcode code, ReadOnlySpan<TValue> inputs,
            ReadOnlySpan<int> arguments, in OrchestrationIdentityOrdinalView identities, ref TValue result)
        {
            TValues values = default;
            TMath math = default;
            switch (code)
            {
                case OrchestrationValueOpcode.BooleanNot: result = values.FromBoolean(!values.Boolean(in inputs[arguments[0]])); return;
                case OrchestrationValueOpcode.BooleanAnd: result = values.FromBoolean(values.Boolean(in inputs[arguments[0]]) && values.Boolean(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.BooleanOr: result = values.FromBoolean(values.Boolean(in inputs[arguments[0]]) || values.Boolean(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.Int32Equal: result = values.FromBoolean(values.Int32(in inputs[arguments[0]]) == values.Int32(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.Int32NotEqual: result = values.FromBoolean(values.Int32(in inputs[arguments[0]]) != values.Int32(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.Int32LessThan: result = values.FromBoolean(values.Int32(in inputs[arguments[0]]) < values.Int32(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.Int32LessThanOrEqual: result = values.FromBoolean(values.Int32(in inputs[arguments[0]]) <= values.Int32(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.Int32GreaterThanOrEqual: result = values.FromBoolean(values.Int32(in inputs[arguments[0]]) >= values.Int32(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.Int32GreaterThan: result = values.FromBoolean(values.Int32(in inputs[arguments[0]]) > values.Int32(in inputs[arguments[1]])); return;
                case OrchestrationValueOpcode.ScalarEqual: result = values.FromBoolean(math.Equal(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.ScalarNotEqual: result = values.FromBoolean(!math.Equal(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.ScalarLessThan: result = values.FromBoolean(math.GreaterThan(values.Scalar(in inputs[arguments[1]]), values.Scalar(in inputs[arguments[0]]))); return;
                case OrchestrationValueOpcode.ScalarLessThanOrEqual: result = values.FromBoolean(math.LessThanOrEqual(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.ScalarGreaterThanOrEqual: result = values.FromBoolean(math.LessThanOrEqual(values.Scalar(in inputs[arguments[1]]), values.Scalar(in inputs[arguments[0]]))); return;
                case OrchestrationValueOpcode.ScalarGreaterThan: result = values.FromBoolean(math.GreaterThan(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.ScalarMultiply: result = values.FromScalar(math.Multiply(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.ScalarDivide: result = values.FromScalar(math.Divide(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.ScalarSubtract: result = values.FromScalar(math.Subtract(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.IdentityEqual: result = values.FromBoolean(identities.Equal(values.Identity(in inputs[arguments[0]]), values.Identity(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.ScalarAbs: result = values.FromScalar(math.Abs(values.Scalar(in inputs[arguments[0]]))); return;
                case OrchestrationValueOpcode.ScalarClamp: result = values.FromScalar(math.ClampGraph(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]), values.Scalar(in inputs[arguments[2]]))); return;
                case OrchestrationValueOpcode.ScalarMoveTowards: result = values.FromScalar(math.MoveTowardsGraph(values.Scalar(in inputs[arguments[0]]), values.Scalar(in inputs[arguments[1]]), values.Scalar(in inputs[arguments[2]]))); return;
                case OrchestrationValueOpcode.Vector3Planar:
                {
                    TVector3 value = values.Vector3(in inputs[arguments[0]]);
                    result = values.FromVector2(math.Vector2(math.X3(value), math.Z3(value)));
                    return;
                }
                case OrchestrationValueOpcode.Vector3Y: result = values.FromScalar(math.Y3(values.Vector3(in inputs[arguments[0]]))); return;
                case OrchestrationValueOpcode.Vector2Magnitude: result = values.FromScalar(math.Magnitude2(values.Vector2(in inputs[arguments[0]]))); return;
                case OrchestrationValueOpcode.Vector2Normalize:
                {
                    TVector2 value = values.Vector2(in inputs[arguments[0]]);
                    TScalar minimumSquaredMagnitude = values.Scalar(in inputs[arguments[1]]);
                    TScalar magnitude = math.Magnitude2(value);
                    bool aboveMinimum = math.LessThanOrEqual(minimumSquaredMagnitude, math.Zero) ||
                        math.GreaterThan(math.SqrMagnitude2(value), minimumSquaredMagnitude);
                    result = values.FromVector2(aboveMinimum && math.GreaterThan(magnitude, math.NormalizationMinimumMagnitude)
                        ? math.MultiplyVector2(value, math.Divide(math.One, magnitude)) : math.ZeroVector2);
                    return;
                }
                case OrchestrationValueOpcode.Vector2Subtract: result = values.FromVector2(math.SubtractVector2(values.Vector2(in inputs[arguments[0]]), values.Vector2(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.Vector2SignedAngle: result = values.FromScalar(math.SignedAngle2(values.Vector2(in inputs[arguments[0]]), values.Vector2(in inputs[arguments[1]]))); return;
                case OrchestrationValueOpcode.Vector2Dot:
                {
                    TVector2 a = values.Vector2(in inputs[arguments[0]]);
                    TVector2 b = values.Vector2(in inputs[arguments[1]]);
                    result = values.FromScalar(math.Add(math.Multiply(math.X2(a), math.X2(b)), math.Multiply(math.Y2(a), math.Y2(b))));
                    return;
                }
                case OrchestrationValueOpcode.Vector2Cross:
                {
                    TVector2 a = values.Vector2(in inputs[arguments[0]]);
                    TVector2 b = values.Vector2(in inputs[arguments[1]]);
                    result = values.FromScalar(math.Subtract(math.Multiply(math.X2(a), math.Y2(b)), math.Multiply(math.Y2(a), math.X2(b))));
                    return;
                }
                case OrchestrationValueOpcode.QuaternionForward: result = values.FromVector3(math.QuaternionForward(values.Quaternion(in inputs[arguments[0]]))); return;
                case OrchestrationValueOpcode.QuaternionAngleAxis: result = values.FromQuaternion(math.QuaternionAngleAxis(values.Scalar(in inputs[arguments[0]]), values.Vector3(in inputs[arguments[1]]))); return;
                default: throw new ArgumentOutOfRangeException(nameof(code));
            }
        }

        public static void Assign(OrchestrationAssignmentOpcode code, in TValue current, in TValue value, ref TValue result)
        {
            TValues values = default;
            TMath math = default;
            switch (code)
            {
                case OrchestrationAssignmentOpcode.Set: result = value; return;
                case OrchestrationAssignmentOpcode.Int32Add: result = values.FromInt32(unchecked(values.Int32(in current) + values.Int32(in value))); return;
                case OrchestrationAssignmentOpcode.Int32Subtract: result = values.FromInt32(unchecked(values.Int32(in current) - values.Int32(in value))); return;
                case OrchestrationAssignmentOpcode.Int32Multiply: result = values.FromInt32(unchecked(values.Int32(in current) * values.Int32(in value))); return;
                case OrchestrationAssignmentOpcode.Int32Divide: result = values.FromInt32(values.Int32(in current) / values.Int32(in value)); return;
                case OrchestrationAssignmentOpcode.ScalarAdd: result = values.FromScalar(math.Add(values.Scalar(in current), values.Scalar(in value))); return;
                case OrchestrationAssignmentOpcode.ScalarSubtract: result = values.FromScalar(math.Subtract(values.Scalar(in current), values.Scalar(in value))); return;
                case OrchestrationAssignmentOpcode.ScalarMultiply: result = values.FromScalar(math.Multiply(values.Scalar(in current), values.Scalar(in value))); return;
                case OrchestrationAssignmentOpcode.ScalarDivide: result = values.FromScalar(math.Divide(values.Scalar(in current), values.Scalar(in value))); return;
                case OrchestrationAssignmentOpcode.Vector3Add: result = values.FromVector3(math.AddVector3(values.Vector3(in current), values.Vector3(in value))); return;
                case OrchestrationAssignmentOpcode.Vector3Subtract: result = values.FromVector3(math.SubtractVector3(values.Vector3(in current), values.Vector3(in value))); return;
                case OrchestrationAssignmentOpcode.Vector3Multiply:
                {
                    TVector3 left = values.Vector3(in current);
                    TVector3 right = values.Vector3(in value);
                    result = values.FromVector3(math.Vector3(math.Multiply(math.X3(left), math.X3(right)),
                        math.Multiply(math.Y3(left), math.Y3(right)), math.Multiply(math.Z3(left), math.Z3(right))));
                    return;
                }
                case OrchestrationAssignmentOpcode.Vector3Divide:
                {
                    TVector3 left = values.Vector3(in current);
                    TVector3 right = values.Vector3(in value);
                    result = values.FromVector3(math.Vector3(math.Divide(math.X3(left), math.X3(right)),
                        math.Divide(math.Y3(left), math.Y3(right)), math.Divide(math.Z3(left), math.Z3(right))));
                    return;
                }
                default: throw new ArgumentOutOfRangeException(nameof(code));
            }
        }
    }
}
