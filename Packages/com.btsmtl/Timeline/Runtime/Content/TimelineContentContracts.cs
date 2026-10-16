using System;

namespace BTSMTL.Timeline
{
    public enum TimelineContentKind : byte
    {
        Discrete = 1,
        Sampled = 2
    }

    public enum TimelineTrackOverlapPolicy : byte
    {
        Reject = 1,
        Parallel = 2,
        Blend = 3
    }

    public enum TimelineClipExecutionPhase : byte
    {
        Decision = 1,
        Commit = 2,
        DecisionAndCommit = 3
    }

    public enum TimelineExecutionDomain : byte
    {
        Logic = 1,
        Presentation = 2
    }

    [Flags]
    public enum TimelineExecutionDomainMask : byte
    {
        None = 0,
        Logic = 1 << 0,
        Presentation = 1 << 1
    }

    public enum TimelineOutputKind : byte
    {
        LogicFact = 1,
        PresentationEvent = 2
    }

    public static class TimelineExecutionDomains
    {
        public static bool IsValid(TimelineExecutionDomain domain) =>
            domain == TimelineExecutionDomain.Logic || domain == TimelineExecutionDomain.Presentation;

        public static TimelineExecutionDomain Require(TimelineExecutionDomain domain) =>
            IsValid(domain) ? domain : throw new ArgumentOutOfRangeException(nameof(domain));

        public static TimelineExecutionDomainMask ToMask(TimelineExecutionDomain domain)
        {
            return Require(domain) switch
            {
                TimelineExecutionDomain.Logic => TimelineExecutionDomainMask.Logic,
                TimelineExecutionDomain.Presentation => TimelineExecutionDomainMask.Presentation,
                _ => throw new ArgumentOutOfRangeException(nameof(domain))
            };
        }

        public static TimelineOutputKind ToOutputKind(TimelineExecutionDomain domain)
        {
            return Require(domain) switch
            {
                TimelineExecutionDomain.Logic => TimelineOutputKind.LogicFact,
                TimelineExecutionDomain.Presentation => TimelineOutputKind.PresentationEvent,
                _ => throw new ArgumentOutOfRangeException(nameof(domain))
            };
        }

        public static bool HasProjection(
            TimelineExecutionDomain contentDomain,
            TimelineExecutionDomain projectionDomain)
        {
            if (projectionDomain != TimelineExecutionDomain.Logic &&
                projectionDomain != TimelineExecutionDomain.Presentation)
            {
                throw new ArgumentOutOfRangeException(nameof(projectionDomain));
            }
            return Require(contentDomain) == projectionDomain;
        }
    }

    public readonly struct TimelineClipExecutionPolicy
    {
        public TimelineClipExecutionPolicy(
            TimelineExecutionDomain domain,
            TimelineOutputKind outputKind)
        {
            if (!TimelineExecutionDomains.IsValid(domain) ||
                (outputKind != TimelineOutputKind.LogicFact && outputKind != TimelineOutputKind.PresentationEvent))
            {
                throw new ArgumentOutOfRangeException(nameof(domain));
            }
            Domain = domain;
            OutputKind = outputKind;
        }

        public static TimelineClipExecutionPolicy FromDomain(TimelineExecutionDomain domain)
        {
            TimelineExecutionDomain declared = TimelineExecutionDomains.Require(domain);
            return new TimelineClipExecutionPolicy(
                declared,
                TimelineExecutionDomains.ToOutputKind(declared));
        }

        public TimelineExecutionDomain Domain { get; }
        public TimelineOutputKind OutputKind { get; }
        public bool IsLogic => Domain == TimelineExecutionDomain.Logic;
        public bool IsPresentation => Domain == TimelineExecutionDomain.Presentation;
    }

    public interface ITimelineClipExecutionPhaseSource
    {
        TimelineClipExecutionPhase TimelineExecutionPhase { get; }
    }

    public enum TimelineClipExitSource : byte
    {
        FrameBoundary = 0,
        TreeDecision = 1
    }

    public interface ITimelineClipExitSource
    {
        TimelineClipExitSource ClipExitSource { get; }
    }

    public static class TimelineContractKinds
    {
        public const string TreeTrack = "tree.track";
        public const string TreeClip = "tree.clip";
        public const string ScenePresentationParameterTrack = "scene-presentation-parameter.track";
        public const string ScenePresentationParameterCurveClip = "scene-presentation-parameter.curve";
    }

    public readonly struct TimelineGraphBinding
    {
        public TimelineGraphBinding(string graphId, string revision)
        {
            GraphId = graphId;
            Revision = revision;
        }

        public string GraphId { get; }
        public string Revision { get; }
    }
}
