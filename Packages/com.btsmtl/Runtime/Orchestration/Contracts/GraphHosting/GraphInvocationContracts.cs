using System;

namespace BTSMTL.Graphs
{
    public readonly struct GraphInvocationIdentity : IEquatable<GraphInvocationIdentity>
    {
        public GraphInvocationIdentity(int sourceIdentityId, ulong sequence)
        {
            SourceIdentityId = sourceIdentityId > 0
                ? sourceIdentityId
                : throw new ArgumentOutOfRangeException(nameof(sourceIdentityId));
            if (sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
        }

        public int SourceIdentityId { get; }
        public ulong Sequence { get; }
        public bool IsValid => SourceIdentityId > 0 && Sequence != 0;

        public bool Equals(GraphInvocationIdentity other) =>
            SourceIdentityId == other.SourceIdentityId && Sequence == other.Sequence;
        public override bool Equals(object obj) => obj is GraphInvocationIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SourceIdentityId, Sequence);
        public override string ToString() => $"{SourceIdentityId}:{Sequence}";
    }

    public readonly struct GraphInvocationContext
    {
        public GraphInvocationContext(
            GraphInvocationIdentity identity,
            string eventId,
            float deltaSeconds)
        {
            if (!identity.IsValid)
                throw new ArgumentException("Event graph invocation identity is invalid.", nameof(identity));
            EventId = string.IsNullOrWhiteSpace(eventId)
                ? throw new ArgumentException("Event graph event identity is missing.", nameof(eventId))
                : eventId.Trim();
            if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            Identity = identity;
            DeltaSeconds = deltaSeconds;
        }

        public GraphInvocationIdentity Identity { get; }
        public string EventId { get; }
        public float DeltaSeconds { get; }
    }

    public sealed class GraphExecutionFailure
    {
        public GraphExecutionFailure(
            string graphId,
            string eventId,
            string nodeId,
            string message,
            Exception exception,
            string exceptionType = "")
        {
            GraphId = graphId ?? string.Empty;
            EventId = eventId ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
            Message = string.IsNullOrWhiteSpace(message)
                ? exception?.Message ?? "Event graph execution failed."
                : message.Trim();
            ExceptionType = exception?.GetType().FullName ?? exceptionType;
        }

        public string GraphId { get; }
        public string EventId { get; }
        public string NodeId { get; }
        public string Message { get; }
        public string ExceptionType { get; }

        public override string ToString() =>
            $"GraphExecutionFailure graph={GraphId} event={EventId} node={NodeId} message={Message}";
    }

}
