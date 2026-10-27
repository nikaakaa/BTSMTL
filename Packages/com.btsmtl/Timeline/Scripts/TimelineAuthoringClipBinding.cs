#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public interface ITimelineAuthoringClipSource
    {
        UnityEngine.Object SourceAsset { get; }
        Type SourceAssetType { get; }
        float GetSourceDuration(UnityEngine.Object source);
        string SourceMissingErrorCode { get; }
        string SourceMissingError { get; }
    }

    public sealed class TimelineAuthoringClipConfiguration
    {
        internal readonly GraphAuthoringCandidate Candidate;
        internal TimelineAuthoringClipConfiguration(Clip clip, bool fromDefaults = false) =>
            Candidate = GraphAuthoringMemberBindings.CreateCandidate(clip, fromDefaults);
        public object this[string propertyId]
        {
            get => Candidate.Contract[propertyId].Evaluate(Candidate);
            set => Candidate.Contract[propertyId].WriteCandidate(Candidate, value);
        }
        public T Get<T>(string propertyId) => (T)this[propertyId];
        public void Set(string propertyId, object value) => this[propertyId] = value;
    }

    public static class TimelineAuthoringClipBinding
    {
        public static bool Supports(this TimelineAuthoringProperty property, Clip clip) =>
            property.Supports(clip, SourceAsset(clip));

        public static bool Supports(this TimelineAuthoringProperty property, Clip clip, UnityEngine.Object source) =>
            !property.SourceMappingOnly || source is ITimelineAuthoringSourceAsset;

        public static UnityEngine.Object SourceAsset(Clip clip) =>
            GraphAuthoringMemberBindings.Contract(clip).Fields
                .Where(field => field.Annotation<TimelineAuthoringPropertyAttribute>()?.IsSourceAsset == true)
                .Select(field => (UnityEngine.Object)field.Read(clip))
                .SingleOrDefault();

        public static OrchestrationTime DefaultEndTime(Clip clip, OrchestrationTime startTime,
            UnityEngine.Object referenceObject) =>
            clip is ITimelineAuthoringClipSource source && referenceObject
                ? startTime + OrchestrationTime.FromSeconds(source.GetSourceDuration(referenceObject))
                : startTime + (clip is SignalClip ? TimelineTimeGrid.Position(1, TimelineUtility.FrameRate) : OrchestrationTime.FromSeconds(0.05d));

        public static IReadOnlyList<TimelineAuthoringProperty> Properties(Clip clip) =>
            GraphAuthoringMemberBindings.Contract(clip).Fields
                .Where(field => field.Metadata.AuthoringVisible)
                .OrderBy(field => field.Metadata.FieldId, StringComparer.Ordinal)
                .Select(field => new TimelineAuthoringProperty(field)).ToArray();

        public static TimelineAuthoringClipConfiguration Read(Clip clip) => new(clip);

        public static TimelineAuthoringClipConfiguration Defaults(Clip clip) => new(clip, true);

        public static Type ValueType(Clip clip, TimelineAuthoringProperty property) =>
            GraphAuthoringMemberBindings.ValueType(clip.GetType(), property.PropertyId);

        public static void Configure(TimelineData timeline, Clip clip,
            TimelineAuthoringClipConfiguration configuration) =>
            configuration.Candidate.Apply(clip);
    }
}
#endif
