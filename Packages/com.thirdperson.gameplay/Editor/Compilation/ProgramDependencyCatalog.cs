using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct ProgramIdentityDependency : IEquatable<ProgramIdentityDependency>, IComparable<ProgramIdentityDependency>
    {
        public ProgramIdentityDependency(string identityDomainId, string identity)
        {
            IdentityDomainId = SimulationIdentity.Require(identityDomainId, nameof(identityDomainId));
            Identity = SimulationIdentity.Require(identity, nameof(identity));
        }

        public string IdentityDomainId { get; }
        public string Identity { get; }

        public bool Equals(ProgramIdentityDependency other) =>
            string.Equals(IdentityDomainId, other.IdentityDomainId, StringComparison.Ordinal) &&
            string.Equals(Identity, other.Identity, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ProgramIdentityDependency other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IdentityDomainId, Identity);

        public int CompareTo(ProgramIdentityDependency other)
        {
            int domain = string.CompareOrdinal(IdentityDomainId, other.IdentityDomainId);
            return domain != 0 ? domain : string.CompareOrdinal(Identity, other.Identity);
        }
    }

    public enum ProgramCatalogFieldKind : byte
    {
        Constant = 1,
        Identity = 2
    }

    public sealed partial class ProgramCatalogField
    {
        public ProgramCatalogField(string name, ProgramCatalogFieldKind kind, int constantIndex,
            ProgramIdentityDependency? dependency)
        {
            Name = name.Trim();
            Kind = kind;
            ConstantIndex = constantIndex;
            Dependency = kind == ProgramCatalogFieldKind.Identity ? dependency : null;
            RequireRecord();
        }

        public string Name { get; }
        public ProgramCatalogFieldKind Kind { get; }
        public int ConstantIndex { get; }
        public ProgramIdentityDependency? Dependency { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SimulationIdentity.Require(Name, nameof(Name));
            if (Kind == ProgramCatalogFieldKind.Constant && ConstantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(ConstantIndex));
            if (Kind == ProgramCatalogFieldKind.Identity && !Dependency.HasValue)
                throw new ArgumentException("Catalog identity field requires its declared identity domain and identity.");
        }
    }

    [ValueStorage]
    public sealed partial class ProgramCatalogEntry
    {
        readonly ReadOnlyCollection<ProgramCatalogField> m_Fields;

        public ProgramCatalogEntry(int index, ProgramIdentityDependency dependency, int revision,
            IEnumerable<ProgramCatalogField> fields)
        {
            Index = index;
            Dependency = dependency;
            Revision = revision;
            var copied = fields == null ? new List<ProgramCatalogField>() : new List<ProgramCatalogField>(fields);
            copied.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            m_Fields = copied.AsReadOnly();
            RequireRecord();
        }

        public int Index { get; }
        public ProgramIdentityDependency Dependency { get; }
        public int Revision { get; }
        public IReadOnlyList<ProgramCatalogField> Fields => m_Fields;

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0 || Revision < 0)
                throw new ArgumentOutOfRangeException();
            for (int i = 1; i < m_Fields.Count; i++)
                if (string.CompareOrdinal(m_Fields[i - 1].Name, m_Fields[i].Name) >= 0)
                    throw new ArgumentException($"Catalog entry '{Dependency.Identity}' fields must be sorted and unique.");
        }
    }
}
