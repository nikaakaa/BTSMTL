using System.Collections.Generic;

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
