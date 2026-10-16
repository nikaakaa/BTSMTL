using System;

namespace ThirdPersonSimulation
{
    public sealed class EquipmentProgramParameterValue
    {
        public EquipmentProgramParameterValue(
            EquipmentParameterValueKind kind,
            bool boolean,
            int int32,
            double x,
            double y,
            double z,
            string identity)
        {
            if (!Enum.IsDefined(typeof(EquipmentParameterValueKind), kind) ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(z) || double.IsInfinity(z))
            {
                throw new ArgumentException("Equipment parameter value is invalid.");
            }
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            X = x;
            Y = y;
            Z = z;
            Identity = identity ?? string.Empty;
        }

        public EquipmentParameterValueKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public string Identity { get; }
    }

    public enum EquipmentLocalStateValueKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt64 = 3,
        Scalar = 4,
        Vector2 = 5,
        Vector3 = 6,
        Yaw = 7,
        Identity = 8
    }

    public readonly struct EquipmentProgramLocalStateDefault
    {
        public EquipmentProgramLocalStateDefault(EquipmentLocalStateValueKind kind,
            bool boolean, int int32, ulong uint64, double x, double y, double z, string identity)
        {
            if (!Enum.IsDefined(typeof(EquipmentLocalStateValueKind), kind))
                throw new ArgumentException("Equipment local state default kind is invalid.");
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(z) || double.IsInfinity(z))
                throw new ArgumentException("Equipment local state default is invalid.");
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            X = x;
            Y = y;
            Z = z;
            Identity = identity ?? string.Empty;
        }

        public EquipmentLocalStateValueKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public string Identity { get; }
    }
}
