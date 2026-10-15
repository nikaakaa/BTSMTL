using System;
using Photon.Deterministic;

namespace ThirdPersonSimulation.QuantumNumerics
{
    public readonly struct QuantumScalarConversion
    {
        internal QuantumScalarConversion(string sourceIdentity, double sourceValue, FP value)
        {
            SourceIdentity = sourceIdentity;
            SourceValue = sourceValue;
            Value = value;
            AbsoluteError = Math.Abs(sourceValue - value.AsDouble);
        }

        public string SourceIdentity { get; }
        public double SourceValue { get; }
        public FP Value { get; }
        public double AbsoluteError { get; }
        public bool WasRounded => AbsoluteError > 0d;
    }

    public static class QuantumScalarBoundary
    {
        public static QuantumScalarConversion LowerAuthoring(double value, string sourceIdentity) =>
            new QuantumScalarConversion(sourceIdentity, value, ConvertExternal(value, sourceIdentity));

        public static FP ConvertExternal(double value, string sourceIdentity)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) ||
                value < FP.MinValue.AsDouble || value >= -FP.MinValue.AsDouble)
                throw new ArgumentOutOfRangeException(sourceIdentity, value, "作者或外部数值超出官方 FP 可表达范围。");
            return FP.FromRoundedDouble_UNSAFE(value);
        }
    }

    public readonly struct QuantumScalarContentLowering : IScalarContentLowering<FP>
    {
        public FP FromSingle(float value) => QuantumScalarBoundary.ConvertExternal(value, nameof(value));
    }
}
