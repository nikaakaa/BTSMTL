using System;

namespace ThirdPersonSimulation
{
    public interface IProgramNumericValueAccess<TValue, TScalar>
        where TValue : struct
        where TScalar : struct
    {
        ProgramStateValueKind Kind(in TValue value);
        bool Boolean(in TValue value);
        int Int32(in TValue value);
        ulong UInt64(in TValue value);
        TScalar Scalar(in TValue value);
        CanonicalTextValue Identity(in TValue value);
    }

    public static class ProgramValueCalculation<TValue, TScalar, TMath, TValues>
        where TValue : struct
        where TScalar : struct
        where TMath : struct, IScalarMath<TScalar>
        where TValues : struct, IProgramNumericValueAccess<TValue, TScalar>
    {
        public static bool ConditionResult(ReadOnlySpan<TValue> inputs) =>
            inputs.Length > 0 && ToBoolean(in inputs[0]);

        public static bool And(ReadOnlySpan<TValue> inputs) =>
            inputs.Length >= 2 && ToBoolean(in inputs[0]) && ToBoolean(in inputs[1]);

        public static bool Or(ReadOnlySpan<TValue> inputs) =>
            inputs.Length > 0 && (ToBoolean(in inputs[0]) || inputs.Length > 1 && ToBoolean(in inputs[1]));

        public static bool Not(ReadOnlySpan<TValue> inputs) =>
            inputs.Length == 0 || !ToBoolean(in inputs[0]);

        public static bool StopsAfterFirstInput(SimulationOperationCode code, in TValue value) =>
            code == SimulationOperationCode.And && !ToBoolean(in value) ||
            code == SimulationOperationCode.Or && ToBoolean(in value);

        public static bool CompareScalars(int comparison, ReadOnlySpan<TValue> inputs)
        {
            TMath math = default;
            TScalar left = ToScalar(in inputs[0]);
            TScalar right = ToScalar(in inputs[1]);
            return comparison switch
            {
                0 => math.Equal(left, right),
                1 => !math.Equal(left, right),
                2 => math.GreaterThan(right, left),
                3 => math.LessThanOrEqual(left, right),
                4 => math.LessThanOrEqual(right, left),
                5 => math.GreaterThan(left, right),
                _ => false
            };
        }

        public static TScalar ToScalar(in TValue value)
        {
            TMath math = default;
            TValues values = default;
            ProgramStateValueKind kind = values.Kind(in value);
            return kind switch
            {
                ProgramStateValueKind.Int32 => math.FromInt64(values.Int32(in value)),
                ProgramStateValueKind.UInt64 when values.UInt64(in value) <= long.MaxValue => math.FromInt64((long)values.UInt64(in value)),
                ProgramStateValueKind.Scalar => values.Scalar(in value),
                ProgramStateValueKind.Boolean => values.Boolean(in value) ? math.One : math.Zero,
                _ => throw new InvalidOperationException($"State value '{kind}' is not numeric.")
            };
        }

        public static bool ToBoolean(in TValue value)
        {
            TMath math = default;
            TValues values = default;
            return values.Kind(in value) switch
            {
                ProgramStateValueKind.Boolean => values.Boolean(in value),
                ProgramStateValueKind.Int32 => values.Int32(in value) != 0,
                ProgramStateValueKind.UInt64 => values.UInt64(in value) != 0,
                ProgramStateValueKind.Scalar => !math.IsZero(values.Scalar(in value)),
                ProgramStateValueKind.Identity => !CanonicalTextValue.IsNullOrEmpty(values.Identity(in value)),
                _ => false
            };
        }
    }
}
