using System;

namespace ThirdPersonSimulation
{
    public sealed class EquipmentRuntimeParameterValue
    {
        public EquipmentRuntimeParameterValue(
            EquipmentParameterValueKind kind,
            bool boolean,
            int int32,
            ulong uint64,
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
                throw new ArgumentException("Equipment runtime parameter value is invalid.");
            }
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            X = x;
            Y = y;
            Z = z;
            Identity = identity ?? string.Empty;
        }

        public EquipmentParameterValueKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public string Identity { get; }
    }

    public enum EquipmentRuntimeStateValueKind : byte
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

    [ValueOperations]
    public readonly partial struct EquipmentRuntimeStateValue : IEquatable<EquipmentRuntimeStateValue>
    {
        public EquipmentRuntimeStateValue(
            EquipmentRuntimeStateValueKind kind,
            bool boolean,
            int int32,
            ulong uint64,
            double x,
            double y,
            double z,
            CanonicalTextValue identity)
        {
            if (!IsValidKind(kind))
                throw new ArgumentException("Equipment runtime state value kind is invalid.");
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            X = x;
            Y = y;
            Z = z;
            Identity = identity.IsNull ? CanonicalTextValue.Empty : identity;
            RequireFinite();
        }

        public EquipmentRuntimeStateValueKind Kind { get; }
        internal static bool IsValidKind(EquipmentRuntimeStateValueKind kind) =>
            kind is EquipmentRuntimeStateValueKind.Boolean or EquipmentRuntimeStateValueKind.Int32 or
                EquipmentRuntimeStateValueKind.UInt64 or EquipmentRuntimeStateValueKind.Scalar or
                EquipmentRuntimeStateValueKind.Vector2 or EquipmentRuntimeStateValueKind.Vector3 or
                EquipmentRuntimeStateValueKind.Yaw or EquipmentRuntimeStateValueKind.Identity;
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public CanonicalTextValue Identity { get; }

        [ValueValidation]
        void RequireFinite()
        {
            if (double.IsNaN(X) || double.IsInfinity(X) ||
                double.IsNaN(Y) || double.IsInfinity(Y) ||
                double.IsNaN(Z) || double.IsInfinity(Z))
                throw new ArgumentException("Equipment runtime state value is invalid.");
        }

    }

    public sealed class CharacterEquipmentRuntimeBinding
    {
        public const int ContractSemanticVersion = 1;
        public const string BindingIdentity = "character-equipment-runtime-binding/v3";

        public CharacterEquipmentRuntimeBinding(
            string sourceIdentity,
            StableHash contentRevision,
            int semanticVersion,
            EquipmentProgramLayout layout)
        {
            SourceIdentity = RequireIdentity(sourceIdentity, nameof(sourceIdentity));
            if (!contentRevision.IsValid)
                throw new ArgumentException("Character Equipment content revision is required.", nameof(contentRevision));
            if (semanticVersion != ContractSemanticVersion)
                throw new ArgumentOutOfRangeException(nameof(semanticVersion), "Character Equipment semantic version is unsupported.");
            ContentRevision = contentRevision;
            SemanticVersion = semanticVersion;
            Layout = layout;
            BindingHash = StableHash.Compute(
                BindingIdentity,
                SourceIdentity,
                ContentRevision.ToString(),
                SemanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public string SourceIdentity { get; }
        public StableHash ContentRevision { get; }
        public int SemanticVersion { get; }
        public StableHash BindingHash { get; }
        public EquipmentProgramLayout Layout { get; }

        static string RequireIdentity(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Identity is required.", parameter);
            return value.Trim();
        }
    }

}
