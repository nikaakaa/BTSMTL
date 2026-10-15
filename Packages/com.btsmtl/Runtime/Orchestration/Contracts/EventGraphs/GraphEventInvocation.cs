using System;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    public readonly struct EventGraphInvocationIdentity : IEquatable<EventGraphInvocationIdentity>
    {
        public EventGraphInvocationIdentity(CanonicalTextValue sourceId, ulong sequence)
        {
            SourceId = CanonicalTextValue.IsNullOrWhiteSpace(sourceId)
                ? throw new ArgumentException("Event graph invocation source identity is missing.", nameof(sourceId))
                : sourceId.Trim();
            if (sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
        }

        public CanonicalTextValue SourceId { get; }
        public void Retain() => SourceId.Retain();
        public void Release() => SourceId.Release();
        public ulong Sequence { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrWhiteSpace(SourceId) && Sequence != 0;

        public bool Equals(EventGraphInvocationIdentity other) =>
            SourceId.Equals(other.SourceId) && Sequence == other.Sequence;
        public override bool Equals(object obj) => obj is EventGraphInvocationIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SourceId, Sequence);
        public override string ToString() => $"{SourceId}:{Sequence}";
    }

    public readonly struct EventGraphInvocationContext
    {
        public EventGraphInvocationContext(
            EventGraphInvocationIdentity identity,
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

        public EventGraphInvocationIdentity Identity { get; }
        public string EventId { get; }
        public float DeltaSeconds { get; }
    }

    public sealed class EventGraphExecutionFailure
    {
        public EventGraphExecutionFailure(
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
            $"EventGraphFailure graph={GraphId} event={EventId} node={NodeId} message={Message}";
    }

}
