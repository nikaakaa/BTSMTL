using System;

namespace ThirdPersonSimulation
{
    public enum TimelineBindingValueKind : byte
    {
        Target = 1,
        Scalar = 2,
        Boolean = 3
    }

    public enum TimelineBindingAccess : byte
    {
        Input = 1,
        Read = 2,
        Write = 3
    }

    public enum TimelineBindingLifetime : byte
    {
        Call = 1,
        Tick = 2
    }

    public readonly struct TimelineBindingDeclaration : IEquatable<TimelineBindingDeclaration>
    {
        public TimelineBindingDeclaration(
            string bindingId,
            string domain,
            string parameterId,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime)
        {
            BindingId = SimulationIdentity.Require(bindingId, nameof(bindingId));
            Domain = SimulationIdentity.Require(domain, nameof(domain));
            if (!Enum.IsDefined(typeof(TimelineBindingValueKind), valueKind) ||
                !Enum.IsDefined(typeof(TimelineBindingAccess), access) ||
                !Enum.IsDefined(typeof(TimelineBindingLifetime), lifetime))
                throw new ArgumentException("Timeline binding declaration type is invalid.");
            ParameterId = parameterId?.Trim() ?? string.Empty;
            if (valueKind == TimelineBindingValueKind.Target && ParameterId.Length != 0 ||
                valueKind != TimelineBindingValueKind.Target && ParameterId.Length == 0)
                throw new ArgumentException("Timeline binding declaration parameter identity is invalid.");
            if (access == TimelineBindingAccess.Input && lifetime != TimelineBindingLifetime.Call ||
                access != TimelineBindingAccess.Input && lifetime != TimelineBindingLifetime.Tick)
                throw new ArgumentException("Timeline binding declaration lifetime is invalid.");
            ValueKind = valueKind;
            Access = access;
            Lifetime = lifetime;
        }

        public string BindingId { get; }
        public string Domain { get; }
        public string ParameterId { get; }
        public TimelineBindingValueKind ValueKind { get; }
        public TimelineBindingAccess Access { get; }
        public TimelineBindingLifetime Lifetime { get; }

        public bool Equals(TimelineBindingDeclaration other) =>
            string.Equals(BindingId, other.BindingId, StringComparison.Ordinal) &&
            string.Equals(Domain, other.Domain, StringComparison.Ordinal) &&
            string.Equals(ParameterId, other.ParameterId, StringComparison.Ordinal) &&
            ValueKind == other.ValueKind &&
            Access == other.Access &&
            Lifetime == other.Lifetime;

        public override bool Equals(object obj) => obj is TimelineBindingDeclaration other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(BindingId, Domain, ParameterId, (int)ValueKind, (int)Access, (int)Lifetime);
    }
}
