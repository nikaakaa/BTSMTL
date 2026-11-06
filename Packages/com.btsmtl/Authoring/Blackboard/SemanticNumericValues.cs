using System;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public enum SemanticNumericPrecision : byte
    {
        Exact = 1,
        TargetRounded = 2
    }

    static class SemanticNumeric
    {
        internal static void RequirePrecision(SemanticNumericPrecision precision)
        {
            if (precision != SemanticNumericPrecision.Exact && precision != SemanticNumericPrecision.TargetRounded)
                throw new ArgumentOutOfRangeException(nameof(precision));
        }

        internal static double RequireFinite(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter);
            return value == 0d ? 0d : value;
        }
    }

    [ValueStorage(UseConstructor = true)]
    public partial struct SemanticNumberValue
    {
        public SemanticNumberValue(double value, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded)
        {
            Value = value;
            Precision = precision;
            RequireRecord();
        }

        public double Value { get; private set; }
        public SemanticNumericPrecision Precision { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            Value = SemanticNumeric.RequireFinite(Value, nameof(Value));
        }
    }

    [ValueStorage(UseConstructor = true)]
    public partial struct SemanticVector2Value
    {
        public SemanticVector2Value(double x, double y, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded)
        {
            X = x; Y = y; Precision = precision;
            RequireRecord();
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public SemanticNumericPrecision Precision { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
        }
    }

    [ValueStorage(UseConstructor = true)]
    public partial struct SemanticVector3Value
    {
        public SemanticVector3Value(double x, double y, double z, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded)
        {
            X = x; Y = y; Z = z; Precision = precision;
            RequireRecord();
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public SemanticNumericPrecision Precision { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
            Z = SemanticNumeric.RequireFinite(Z, nameof(Z));
        }
    }

    public partial struct SemanticQuaternionValue
    {
        public SemanticQuaternionValue(double x, double y, double z, double w)
        {
            X = x; Y = y; Z = z; W = w;
            RequireRecord();
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public double W { get; private set; }

        [ValueValidation]
        void RequireRecord()
        {
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
            Z = SemanticNumeric.RequireFinite(Z, nameof(Z));
            W = SemanticNumeric.RequireFinite(W, nameof(W));
        }
    }
}
