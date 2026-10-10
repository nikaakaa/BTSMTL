using System;
using System.Collections.Generic;

namespace BTSMTL.Diagnostics.Editor
{
    public enum RuntimeDebugAttachmentState
    {
        Detached,
        Live,
        Frozen,
        CaptureHistory,
        Ended
    }

    public enum RuntimeDebugTargetMatch
    {
        Exact,
        SourceMissing
    }

    public enum RuntimeDebugTargetResolutionStatus
    {
        Attached,
        Ended,
        ExplicitHostUnregistered,
        ExplicitHostSourceMissing,
        SourceMissing,
        NoExactTarget,
        MultipleExactTargets,
        PinnedTargetNotAttached
    }

    public readonly struct RuntimeDebugTargetRequest : IEquatable<RuntimeDebugTargetRequest>
    {
        public RuntimeDebugTargetRequest(RuntimeSourceElementKey source)
        {
            Source = source;
        }

        public RuntimeSourceElementKey Source { get; }

        public bool Equals(RuntimeDebugTargetRequest other)
        {
            return Source.Equals(other.Source);
        }

        public override bool Equals(object obj) => obj is RuntimeDebugTargetRequest other && Equals(other);
        public override int GetHashCode() => Source.GetHashCode();
    }

    public readonly struct RuntimeDebugSceneSelection
    {
        public RuntimeDebugSceneSelection(int hostInstanceId)
        {
            HostInstanceId = hostInstanceId;
        }

        public int HostInstanceId { get; }
        public bool HasExplicitHost => HostInstanceId != 0;
    }

    public static class RuntimeDebugSceneSelectionRegistry
    {
        static Func<RuntimeDebugSceneSelection> s_Resolver;

        public static void Register(Func<RuntimeDebugSceneSelection> resolver)
        {
            s_Resolver = resolver;
        }

        public static RuntimeDebugSceneSelection Resolve()
        {
            return s_Resolver != null ? s_Resolver() : default;
        }
    }

    public readonly struct RuntimeDebugTargetCandidate
    {
        public RuntimeDebugTargetCandidate(RuntimeDebugTargetInfo target, RuntimeDebugTargetMatch match)
        {
            Target = target;
            Match = match;
        }

        public RuntimeDebugTargetInfo Target { get; }
        public RuntimeDebugTargetMatch Match { get; }
        public bool IsExact => Match == RuntimeDebugTargetMatch.Exact;
    }

    public readonly struct RuntimeDebugTargetResolution
    {
        readonly IReadOnlyList<RuntimeDebugTargetCandidate> m_Candidates;

        public RuntimeDebugTargetResolution(
            RuntimeDebugTargetResolutionStatus status,
            IReadOnlyList<RuntimeDebugTargetCandidate> candidates = null)
        {
            Status = status;
            m_Candidates = candidates ?? Array.Empty<RuntimeDebugTargetCandidate>();
        }

        public RuntimeDebugTargetResolutionStatus Status { get; }
        public IReadOnlyList<RuntimeDebugTargetCandidate> Candidates => m_Candidates ?? Array.Empty<RuntimeDebugTargetCandidate>();
        public bool CanReadSnapshot => Status == RuntimeDebugTargetResolutionStatus.Attached || Status == RuntimeDebugTargetResolutionStatus.Ended;

        public string Message => Status switch
        {
            RuntimeDebugTargetResolutionStatus.Attached => string.Empty,
            RuntimeDebugTargetResolutionStatus.Ended => "Target ended. Showing frozen history.",
            RuntimeDebugTargetResolutionStatus.ExplicitHostUnregistered => "The selected character host is not registered.",
            RuntimeDebugTargetResolutionStatus.ExplicitHostSourceMissing => "The selected character host does not contain this authoring source.",
            RuntimeDebugTargetResolutionStatus.SourceMissing => "The attached trace does not contain this authoring source.",
            RuntimeDebugTargetResolutionStatus.NoExactTarget => "No registered target exactly matches this authoring source.",
            RuntimeDebugTargetResolutionStatus.MultipleExactTargets => "Multiple registered targets match this authoring source. Choose a target.",
            RuntimeDebugTargetResolutionStatus.PinnedTargetNotAttached => "The pinned runtime target is not attached. Select that target or explicitly resume Follow.",
            _ => string.Empty
        };
    }
}

