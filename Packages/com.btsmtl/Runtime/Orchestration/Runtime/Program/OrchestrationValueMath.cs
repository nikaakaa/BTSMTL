using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public static class OrchestrationValueMath<TValue, TScalar, TVector2, TVector3,
        TQuaternion, TMath, TValues>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
    {
        public static bool ToBoolean(in TValue value, in OrchestrationIdentityOrdinalView identities)
        {
            TValues values = default;
            TMath math = default;
            return values.Kind(in value) switch
            {
                OrchestrationValueKind.Boolean => values.Boolean(in value),
                OrchestrationValueKind.Int32 => values.Int32(in value) != 0,
                OrchestrationValueKind.UInt64 => values.UInt64(in value) != 0,
                OrchestrationValueKind.Scalar => !math.IsZero(values.Scalar(in value)),
                OrchestrationValueKind.Identity => !identities.IsEmpty(values.Identity(in value)),
                _ => false
            };
        }

        public static TScalar ToScalar(in TValue value)
        {
            TValues values = default;
            TMath math = default;
            return values.Kind(in value) switch
            {
                OrchestrationValueKind.Boolean => values.Boolean(in value) ? math.One : math.Zero,
                OrchestrationValueKind.Int32 => math.FromInt32(values.Int32(in value)),
                OrchestrationValueKind.UInt64 when values.UInt64(in value) <= long.MaxValue =>
                    math.FromInt64((long)values.UInt64(in value)),
                OrchestrationValueKind.Scalar => values.Scalar(in value),
                _ => throw new InvalidOperationException($"Value kind '{values.Kind(in value)}' is not numeric.")
            };
        }

        public static bool Compare(OrchestrationComparisonKind comparison, ReadOnlySpan<TValue> inputs)
        {
            TValues values = default;
            if (values.Kind(in inputs[0]) == OrchestrationValueKind.Int32 &&
                values.Kind(in inputs[1]) == OrchestrationValueKind.Int32)
            {
                int left = values.Int32(in inputs[0]);
                int right = values.Int32(in inputs[1]);
                return comparison switch
                {
                    OrchestrationComparisonKind.Equal => left == right,
                    OrchestrationComparisonKind.NotEqual => left != right,
                    OrchestrationComparisonKind.LessThan => left < right,
                    OrchestrationComparisonKind.LessThanOrEqual => left <= right,
                    OrchestrationComparisonKind.GreaterThanOrEqual => left >= right,
                    OrchestrationComparisonKind.GreaterThan => left > right,
                    _ => throw new ArgumentOutOfRangeException(nameof(comparison))
                };
            }
            TMath math = default;
            TScalar scalarLeft = ToScalar(in inputs[0]);
            TScalar scalarRight = ToScalar(in inputs[1]);
            return comparison switch
            {
                OrchestrationComparisonKind.Equal => math.Equal(scalarLeft, scalarRight),
                OrchestrationComparisonKind.NotEqual => !math.Equal(scalarLeft, scalarRight),
                OrchestrationComparisonKind.LessThan => math.GreaterThan(scalarRight, scalarLeft),
                OrchestrationComparisonKind.LessThanOrEqual => math.LessThanOrEqual(scalarLeft, scalarRight),
                OrchestrationComparisonKind.GreaterThanOrEqual => math.LessThanOrEqual(scalarRight, scalarLeft),
                OrchestrationComparisonKind.GreaterThan => math.GreaterThan(scalarLeft, scalarRight),
                _ => throw new ArgumentOutOfRangeException(nameof(comparison))
            };
        }

        public static TValue Convert(in TValue value, OrchestrationValueKind expected)
        {
            TValues values = default;
            if (values.Kind(in value) == expected)
                return value;
            if (expected == OrchestrationValueKind.Scalar)
                return values.FromScalar(ToScalar(in value));
            if (expected == OrchestrationValueKind.Int32 && values.Kind(in value) == OrchestrationValueKind.Scalar)
                return values.FromInt32(default(TMath).TruncateToInt32(values.Scalar(in value)));
            throw new InvalidOperationException($"Value kind '{values.Kind(in value)}' cannot convert to '{expected}'.");
        }

        public static TValue Calculate(OrchestrationCalculationKind kind, ReadOnlySpan<TValue> inputs,
            in OrchestrationIdentityOrdinalView identities)
        {
            TValues values = default;
            TMath math = default;
            switch (kind)
            {
                case OrchestrationCalculationKind.ScalarMultiply:
                    return values.FromScalar(math.Multiply(values.Scalar(in inputs[0]), values.Scalar(in inputs[1])));
                case OrchestrationCalculationKind.ScalarDivide:
                    return values.FromScalar(math.Divide(values.Scalar(in inputs[0]), values.Scalar(in inputs[1])));
                case OrchestrationCalculationKind.IdentityEqual:
                    return values.FromBoolean(identities.Equal(values.Identity(in inputs[0]), values.Identity(in inputs[1])));
                case OrchestrationCalculationKind.ScalarAbs:
                    return values.FromScalar(math.Abs(values.Scalar(in inputs[0])));
                case OrchestrationCalculationKind.ScalarClamp:
                    return values.FromScalar(math.ClampGraph(values.Scalar(in inputs[0]),
                        values.Scalar(in inputs[1]), values.Scalar(in inputs[2])));
                case OrchestrationCalculationKind.ScalarMoveTowards:
                    return values.FromScalar(math.MoveTowardsGraph(values.Scalar(in inputs[0]),
                        values.Scalar(in inputs[1]), values.Scalar(in inputs[2])));
                case OrchestrationCalculationKind.Vector3Planar:
                {
                    TVector3 value = values.Vector3(in inputs[0]);
                    return values.FromVector2(math.Vector2(math.X3(value), math.Z3(value)));
                }
                case OrchestrationCalculationKind.Vector3Y:
                    return values.FromScalar(math.Y3(values.Vector3(in inputs[0])));
                case OrchestrationCalculationKind.Vector2Magnitude:
                    return values.FromScalar(math.Magnitude2(values.Vector2(in inputs[0])));
                case OrchestrationCalculationKind.Vector2Normalize:
                {
                    TVector2 value = values.Vector2(in inputs[0]);
                    TScalar minimumSquaredMagnitude = values.Scalar(in inputs[1]);
                    TScalar magnitude = math.Magnitude2(value);
                    bool aboveMinimum = math.LessThanOrEqual(minimumSquaredMagnitude, math.Zero) ||
                        math.GreaterThan(math.SqrMagnitude2(value), minimumSquaredMagnitude);
                    return values.FromVector2(aboveMinimum && math.GreaterThan(magnitude, math.NormalizationMinimumMagnitude)
                        ? math.MultiplyVector2(value, math.Divide(math.One, magnitude))
                        : math.ZeroVector2);
                }
                case OrchestrationCalculationKind.Vector2Subtract:
                    return values.FromVector2(math.SubtractVector2(values.Vector2(in inputs[0]), values.Vector2(in inputs[1])));
                case OrchestrationCalculationKind.Vector2SignedAngle:
                    return values.FromScalar(math.SignedAngle2(values.Vector2(in inputs[0]), values.Vector2(in inputs[1])));
                case OrchestrationCalculationKind.QuaternionForward:
                    return values.FromVector3(math.QuaternionForward(values.Quaternion(in inputs[0])));
                case OrchestrationCalculationKind.QuaternionAngleAxis:
                    return values.FromQuaternion(math.QuaternionAngleAxis(values.Scalar(in inputs[0]), values.Vector3(in inputs[1])));
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static TValue Assign(in TValue current, in TValue value, OrchestrationAssignmentKind operation)
        {
            if (operation == OrchestrationAssignmentKind.Set)
                return value;
            TValues values = default;
            TMath math = default;
            switch (values.Kind(in value))
            {
                case OrchestrationValueKind.Int32:
                {
                    int left = values.Int32(in current);
                    int right = values.Int32(in value);
                    return values.FromInt32(operation switch
                    {
                        OrchestrationAssignmentKind.Add => left + right,
                        OrchestrationAssignmentKind.Subtract => left - right,
                        OrchestrationAssignmentKind.Multiply => left * right,
                        OrchestrationAssignmentKind.Divide => left / right,
                        _ => throw new ArgumentOutOfRangeException(nameof(operation))
                    });
                }
                case OrchestrationValueKind.Scalar:
                    return values.FromScalar(AssignScalar(values.Scalar(in current), values.Scalar(in value), operation));
                case OrchestrationValueKind.Vector3:
                {
                    TVector3 left = values.Vector3(in current);
                    TVector3 right = values.Vector3(in value);
                    if (operation == OrchestrationAssignmentKind.Add)
                        return values.FromVector3(math.AddVector3(left, right));
                    if (operation == OrchestrationAssignmentKind.Subtract)
                        return values.FromVector3(math.SubtractVector3(left, right));
                    return values.FromVector3(math.Vector3(
                        AssignScalar(math.X3(left), math.X3(right), operation),
                        AssignScalar(math.Y3(left), math.Y3(right), operation),
                        AssignScalar(math.Z3(left), math.Z3(right), operation)));
                }
                default:
                    return value;
            }
        }

        static TScalar AssignScalar(TScalar left, TScalar right, OrchestrationAssignmentKind operation)
        {
            TMath math = default;
            return operation switch
            {
                OrchestrationAssignmentKind.Add => math.Add(left, right),
                OrchestrationAssignmentKind.Subtract => math.Subtract(left, right),
                OrchestrationAssignmentKind.Multiply => math.Multiply(left, right),
                OrchestrationAssignmentKind.Divide => math.Divide(left, right),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
    }
}
