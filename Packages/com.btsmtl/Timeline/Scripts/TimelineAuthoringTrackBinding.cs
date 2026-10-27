#if UNITY_EDITOR
using System;
using BTSMTL.Authoring.Graph;
using System.Collections.Generic;
using System.Linq;

namespace BTSMTL.Timeline
{
    public static class TimelineAuthoringTrackBinding
    {
        public static IReadOnlyList<TimelineAuthoringTrackField> GetFields(string kind) =>
            GraphAuthoringMemberBindings.Contract(TimelineAuthoringTypeCatalog.RequireTrackType(kind)).Fields
                .Where(field => field.Annotation<TimelineAuthoringTrackFieldAttribute>() != null)
                .Select(field => new TimelineAuthoringTrackField(field)).ToArray();

        public static IReadOnlyList<TimelineAuthoringTrackIssue> Validate(
            string kind,
            IReadOnlyDictionary<string, string> values) => TimelineAuthoringTrackField.Validate(GetFields(kind), values);

        public static void Apply(Track track, IReadOnlyDictionary<string, string> values)
        {
            GraphAuthoringCandidate candidate = GraphAuthoringMemberBindings.CreateCandidate(track);
            foreach (KeyValuePair<string, string> value in values)
                candidate.Contract[value.Key].WriteCandidate(candidate, value.Value);
            candidate.Apply(track);
        }

        public static TimelineExecutionDomain RequireExecutionDomain(Track track, TimelineContractCatalog catalog)
        {
            TimelineTrackContract contract = catalog.RequireTrack(track.ContractKind);
            if (!track.HasExplicitExecutionDomain || !contract.SupportsExecutionDomain(track.ExecutionDomain))
                throw new InvalidOperationException($"Timeline track '{track.AuthoringId}' has a missing or unsupported execution domain '{track.ExecutionDomain}'.");
            return track.ExecutionDomain;
        }

    }
}
#endif
