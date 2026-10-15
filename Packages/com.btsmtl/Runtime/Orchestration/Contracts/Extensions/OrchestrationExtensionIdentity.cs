using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExtensionIdentity : IEquatable<OrchestrationExtensionIdentity>
    {
        public OrchestrationExtensionIdentity(string domainId, string operationId)
        {
            DomainId = domainId;
            OperationId = operationId;
        }

        public string DomainId { get; }
        public string OperationId { get; }

        public bool Equals(OrchestrationExtensionIdentity other) =>
            string.Equals(DomainId, other.DomainId, StringComparison.Ordinal) &&
            string.Equals(OperationId, other.OperationId, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is OrchestrationExtensionIdentity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(DomainId, OperationId);
    }
}
