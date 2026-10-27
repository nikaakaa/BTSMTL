using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Timeline
{
    public static class TreeTimelineDefinition
    {
        public static TimelineTrackContract Track { get; } = new(
            TimelineContractKinds.TreeTrack,
            TimelineTrackOverlapPolicy.Reject,
            TimelineContentKind.Discrete,
            TimelineExecutionDomain.Logic,
            TimelineOutputKind.LogicFact,
            TimelineExecutionDomainMask.Logic | TimelineExecutionDomainMask.Presentation,
            null,
            TimelineContractKinds.TreeClip);

        public static TimelineClipDefinition Clip { get; } = new(
            TimelineContractKinds.TreeClip,
            TimelineContractKinds.TreeTrack,
            TimelineClipExecutionPhase.DecisionAndCommit,
            TimelineContentKind.Discrete,
            true,
            true,
            TimelineExecutionDomain.Logic,
            TimelineOutputKind.LogicFact,
            TimelineExecutionDomainMask.Logic | TimelineExecutionDomainMask.Presentation);

        public const TimelineClipExitSource DefaultExitSource = TimelineClipExitSource.TreeDecision;

        public static TimelineTreeExecutionPhase DefaultExecutionPhase =>
            Clip.DefaultExecutionPhase == TimelineClipExecutionPhase.Decision
                ? TimelineTreeExecutionPhase.Decision : TimelineTreeExecutionPhase.Commit;

        public static TimelineClipExecutionPhase ExecutionPhase(TimelineTreeExecutionPhase phase) =>
            phase == TimelineTreeExecutionPhase.Decision
                ? TimelineClipExecutionPhase.Decision : TimelineClipExecutionPhase.Commit;

        public static TimelineClipExitSource ConfiguredExitSource(TimelineExecutionDomain domain, TimelineClipExitSource source) =>
            domain == TimelineExecutionDomain.Logic ? TimelineClipExitSource.TreeDecision : source;

        public static OrchestrationTime TerminalTime(string identity, OrchestrationTime startTime,
            OrchestrationTime terminalTime, OrchestrationTime? declaredEnd)
        {
            if (!declaredEnd.HasValue)
                return OrchestrationTime.Max(startTime + OrchestrationTime.FromRaw(1), terminalTime);
            if (declaredEnd.Value <= startTime)
                throw new InvalidOperationException($"TreeClip '{identity}' 的结束声明必须晚于起点 {startTime.AsSingle}。");
            return declaredEnd.Value;
        }

        public static void RequireExitSource(TimelineExecutionDomain domain, TimelineClipExitSource source)
        {
            if (!Enum.IsDefined(typeof(TimelineClipExitSource), source))
                throw new ArgumentOutOfRangeException(nameof(source), source, "TimelineTree exit source is invalid.");
            if (domain == TimelineExecutionDomain.Logic && source != TimelineClipExitSource.TreeDecision)
                throw new InvalidOperationException("Logic TreeClip必须由节点图决定退出。");
        }

        public static void RequireExecutionPhase(TimelineTreeExecutionPhase phase)
        {
            if (!Enum.IsDefined(typeof(TimelineTreeExecutionPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "TimelineTree execution phase is invalid.");
        }

        public static void Validate(string identity, TimelineExecutionDomain domain, TimelineClipExitSource exitSource,
            TimelineTreeExecutionPhase executionPhase, List<string> errors)
        {
            switch (domain)
            {
                case TimelineExecutionDomain.Logic:
                    if (exitSource != TimelineClipExitSource.TreeDecision)
                        errors?.Add($"Timeline Logic TreeClip '{identity}' must use TreeDecision exit.");
                    break;
                case TimelineExecutionDomain.Presentation:
                    if (exitSource != TimelineClipExitSource.FrameBoundary && exitSource != TimelineClipExitSource.TreeDecision)
                        errors?.Add($"Timeline Presentation TreeClip '{identity}' has an invalid exit source.");
                    if (executionPhase != TimelineTreeExecutionPhase.Commit)
                        errors?.Add($"Timeline Presentation TreeClip '{identity}' must use Commit phase.");
                    break;
                default:
                    errors?.Add($"Timeline TreeClip '{identity}' has an invalid execution domain.");
                    break;
            }
        }
    }
}
