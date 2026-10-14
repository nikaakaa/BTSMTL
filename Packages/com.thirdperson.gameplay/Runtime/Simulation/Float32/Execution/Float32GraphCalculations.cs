using System;

namespace ThirdPersonSimulation
{
    public static class Float32GraphCalculations
    {
        public static AbilityStateValue Evaluate(GraphCalculationKind kind, Float32ValueInputLease inputs)
        {
            switch (kind)
            {
                case GraphCalculationKind.FloatMultiply:
                    return AbilityStateValue.FromScalar(inputs[0].Scalar * inputs[1].Scalar);
                case GraphCalculationKind.FloatDivide:
                    return AbilityStateValue.FromScalar(inputs[0].Scalar / inputs[1].Scalar);
                case GraphCalculationKind.TextEqual:
                    return AbilityStateValue.FromBoolean(inputs[0].Identity.Equals(inputs[1].Identity));
                case GraphCalculationKind.FloatAbs:
                    return Scalar(Math.Abs(inputs[0].Scalar.ToSingle()));
                case GraphCalculationKind.FloatClamp:
                    return Scalar(Clamp(inputs[0].Scalar.ToSingle(), inputs[1].Scalar.ToSingle(), inputs[2].Scalar.ToSingle()));
                case GraphCalculationKind.FloatMoveTowards:
                {
                    float current = inputs[0].Scalar.ToSingle();
                    float target = inputs[1].Scalar.ToSingle();
                    float delta = inputs[2].Scalar.ToSingle();
                    return Scalar(Math.Abs(target - current) <= delta ? target : current + Sign(target - current) * delta);
                }
                case GraphCalculationKind.Vector3Planar:
                    return AbilityStateValue.FromVector2(new Float32Vector2(inputs[0].Vector3.X, inputs[0].Vector3.Z));
                case GraphCalculationKind.Vector3Y:
                    return AbilityStateValue.FromScalar(inputs[0].Vector3.Y);
                case GraphCalculationKind.Vector2Magnitude:
                    return AbilityStateValue.FromScalar(inputs[0].Vector2.Magnitude);
                case GraphCalculationKind.Vector2Normalize:
                {
                    Float32Vector2 value = inputs[0].Vector2;
                    float minimum = inputs[1].Scalar.ToSingle();
                    float magnitude = value.Magnitude.ToSingle();
                    return AbilityStateValue.FromVector2(
                        (minimum <= 0f || value.SqrMagnitude.ToSingle() > minimum) && magnitude > 0.00001f
                            ? value * Float32Scalar.FromSingle(1f / magnitude) : Float32Vector2.Zero);
                }
                case GraphCalculationKind.Vector2Subtract:
                    return AbilityStateValue.FromVector2(inputs[0].Vector2 - inputs[1].Vector2);
                case GraphCalculationKind.Vector2SignedAngle:
                    return Scalar(SignedAngle(inputs[0].Vector2, inputs[1].Vector2));
                case GraphCalculationKind.QuaternionForward:
                {
                    Float32Quaternion value = inputs[0].Quaternion;
                    float x = value.X.ToSingle(), y = value.Y.ToSingle(), z = value.Z.ToSingle(), w = value.W.ToSingle();
                    return AbilityStateValue.FromVector3(new Float32Vector3(
                        Float32Scalar.FromSingle(2f * (x * z + w * y)),
                        Float32Scalar.FromSingle(2f * (y * z - w * x)),
                        Float32Scalar.FromSingle(1f - 2f * (x * x + y * y))));
                }
                case GraphCalculationKind.QuaternionAngleAxis:
                    return AbilityStateValue.FromQuaternion(AngleAxis(inputs[0].Scalar.ToSingle(), inputs[1].Vector3));
                default:
                    throw new InvalidOperationException($"图计算 '{kind}' 没有 Float32 实现。");
            }
        }

        public static Float32Quaternion AngleAxis(float angle, Float32Vector3 axis)
        {
            float magnitude = axis.Magnitude.ToSingle();
            if (magnitude == 0f)
                return Float32Quaternion.Identity;
            float halfRadians = angle * (MathF.PI / 180f) * 0.5f;
            float scale = MathF.Sin(halfRadians) / magnitude;
            return new Float32Quaternion(Float32Scalar.FromSingle(axis.X.ToSingle() * scale),
                Float32Scalar.FromSingle(axis.Y.ToSingle() * scale), Float32Scalar.FromSingle(axis.Z.ToSingle() * scale),
                Float32Scalar.FromSingle(MathF.Cos(halfRadians)));
        }

        public static float SignedAngle(Float32Vector2 from, Float32Vector2 to)
        {
            float denominator = MathF.Sqrt(from.SqrMagnitude.ToSingle() * to.SqrMagnitude.ToSingle());
            if (denominator < 1e-15f)
                return 0f;
            float cosine = (from.X.ToSingle() * to.X.ToSingle() + from.Y.ToSingle() * to.Y.ToSingle()) / denominator;
            float angle = MathF.Acos(Clamp(cosine, -1f, 1f)) * (180f / MathF.PI);
            return angle * Sign(from.X.ToSingle() * to.Y.ToSingle() - from.Y.ToSingle() * to.X.ToSingle());
        }

        public static AbilityStateValue Assign(AbilityStateValue current, AbilityStateValue value, int operation)
        {
            if (operation == 0)
                return value;
            switch (value.Kind)
            {
                case ProgramStateValueKind.Int32:
                    int left = current.Int32, right = value.Int32;
                    return AbilityStateValue.FromInt32(operation switch {
                        1 => left + right, 2 => left - right, 3 => left * right, 4 => left / right,
                        _ => throw new InvalidOperationException($"赋值操作 '{operation}' 不受支持。") });
                case ProgramStateValueKind.Scalar:
                    return AbilityStateValue.FromScalar(AssignScalar(current.Scalar, value.Scalar, operation));
                case ProgramStateValueKind.Vector3:
                    if (operation == 1) return AbilityStateValue.FromVector3(current.Vector3 + value.Vector3);
                    if (operation == 2) return AbilityStateValue.FromVector3(current.Vector3 - value.Vector3);
                    return AbilityStateValue.FromVector3(new Float32Vector3(
                        AssignScalar(current.Vector3.X, value.Vector3.X, operation),
                        AssignScalar(current.Vector3.Y, value.Vector3.Y, operation),
                        AssignScalar(current.Vector3.Z, value.Vector3.Z, operation)));
                default:
                    return value;
            }
        }

        static Float32Scalar AssignScalar(Float32Scalar left, Float32Scalar right, int operation) => operation switch
        {
            1 => left + right, 2 => left - right, 3 => left * right, 4 => left / right,
            _ => throw new InvalidOperationException($"赋值操作 '{operation}' 不受支持。")
        };

        static AbilityStateValue Scalar(float value) => AbilityStateValue.FromScalar(Float32Scalar.FromSingle(value));
        static float Sign(float value) => value >= 0f ? 1f : -1f;
        static float Clamp(float value, float minimum, float maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
