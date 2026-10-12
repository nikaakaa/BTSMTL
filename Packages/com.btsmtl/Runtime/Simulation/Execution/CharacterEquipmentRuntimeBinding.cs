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

    [CanonicalContract]
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

        [CanonicalValidation]
        void RequireFinite()
        {
            if (double.IsNaN(X) || double.IsInfinity(X) ||
                double.IsNaN(Y) || double.IsInfinity(Y) ||
                double.IsNaN(Z) || double.IsInfinity(Z))
                throw new ArgumentException("Equipment runtime state value is invalid.");
        }

        public bool Equals(EquipmentRuntimeStateValue other) =>
            Kind == other.Kind &&
            Boolean == other.Boolean &&
            Int32 == other.Int32 &&
            UInt64 == other.UInt64 &&
            X == other.X &&
            Y == other.Y &&
            Z == other.Z &&
            Identity.Equals(other.Identity);
    }

    public sealed class CharacterEquipmentRuntimeBinding
    {
        public const int ContractSemanticVersion = 1;
        public const int CatalogFormatVersion = 3;
        public const string CodecIdentity = "character-equipment-runtime-binding/v3";

        readonly byte[] m_CatalogBytes;

        public CharacterEquipmentRuntimeBinding(
            string sourceIdentity,
            StableHash contentRevision,
            int semanticVersion,
            byte[] catalogBytes)
        {
            SourceIdentity = RequireIdentity(sourceIdentity, nameof(sourceIdentity));
            if (!contentRevision.IsValid)
                throw new ArgumentException("Character Equipment content revision is required.", nameof(contentRevision));
            if (semanticVersion != ContractSemanticVersion)
                throw new ArgumentOutOfRangeException(nameof(semanticVersion), "Character Equipment semantic version is unsupported.");
            if (catalogBytes == null || catalogBytes.Length == 0)
                throw new ArgumentException("Character Equipment catalog bytes are required.", nameof(catalogBytes));
            ContentRevision = contentRevision;
            SemanticVersion = semanticVersion;
            m_CatalogBytes = (byte[])catalogBytes.Clone();
            BindingHash = StableHash.Compute(
                CodecIdentity,
                SourceIdentity,
                ContentRevision.ToString(),
                SemanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SimulationCanonicalPayloadHash.Compute(m_CatalogBytes).ToString());
        }

        public string SourceIdentity { get; }
        public StableHash ContentRevision { get; }
        public int SemanticVersion { get; }
        public StableHash BindingHash { get; }
        public byte[] CatalogBytes => (byte[])m_CatalogBytes.Clone();

        static string RequireIdentity(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Identity is required.", parameter);
            return value.Trim();
        }
    }

    public static class CharacterEquipmentRuntimeBindingCodec
    {
        public static byte[] Write(CharacterEquipmentRuntimeBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            using var writer = new CanonicalWriter();
            writer.WriteText(CharacterEquipmentRuntimeBinding.CodecIdentity);
            writer.WriteText(binding.SourceIdentity);
            writer.WriteText(binding.ContentRevision.ToString());
            writer.WriteInt32(binding.SemanticVersion);
            writer.WriteBytes(binding.CatalogBytes);
            return writer.ToArray();
        }

        public static CharacterEquipmentRuntimeBinding Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes ?? throw new ArgumentNullException(nameof(bytes)));
            if (!string.Equals(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), CharacterEquipmentRuntimeBinding.CodecIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("Character Equipment runtime binding codec identity is unsupported.");
            var binding = new CharacterEquipmentRuntimeBinding(
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                new StableHash(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                reader.ReadInt32(),
                reader.ReadBytes());
            reader.RequireComplete();
            return binding;
        }
    }
}
