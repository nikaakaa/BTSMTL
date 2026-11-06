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

        public static TimelineAuthoringReferenceValue Reference(Clip clip)
        {
            if (IsDocumentSource(clip))
                return default;
            if (clip is ITimelineAuthoringResourceClip resource)
            {
                TimelineAuthoringReferenceValue resolution = resource.ResourceResolution;
                if (resolution.Asset || resolution.IsMissing)
                    return resolution;
            }
            UnityEngine.Object asset = SourceAsset(clip);
            if (asset)
                return new TimelineAuthoringReferenceValue(asset);
            if (clip is ITimelineAuthoringClipSource source)
                return new TimelineAuthoringReferenceValue(null, source.SourceMissingErrorCode, source.SourceMissingError);
            if (clip is ITimelineTreeClipAuthoringSource)
                return new TimelineAuthoringReferenceValue(null, "timeline_tree_source_unsupported", "TreeClip不是正式资产节点图引用。");
            return new TimelineAuthoringReferenceValue(null);
        }

        public static UnityEngine.Object SourceAsset(Clip clip)
        {
            if (IsDocumentSource(clip))
                return null;
            if (clip is ITimelineAuthoringClipSource source)
                return source.SourceAsset;
            return GraphAuthoringMemberBindings.Contract(clip).Fields
                .Where(field => field.Annotation<TimelineAuthoringPropertyAttribute>()?.IsSourceAsset == true)
                .Select(field => (UnityEngine.Object)field.Read(clip))
                .SingleOrDefault();
        }

        public static OrchestrationTime DefaultEndTime(Clip clip, OrchestrationTime startTime,
            UnityEngine.Object referenceObject) =>
            clip is ITimelineAuthoringBoundClipSource || clip is ITimelineAuthoringClipSource && referenceObject
                ? startTime + OrchestrationTime.FromSeconds(SourceDuration(clip, referenceObject))
                : startTime + (clip is SignalClip ? TimelineTimeGrid.Position(1, TimelineUtility.FrameRate) : OrchestrationTime.FromSeconds(0.05d));

        public static float SourceDuration(Clip clip, UnityEngine.Object referenceObject) =>
            clip is ITimelineAuthoringBoundClipSource source ? source.TotalSourceDuration :
                ((ITimelineAuthoringClipSource)clip).GetSourceDuration(referenceObject);

        public static bool HasResourceReference(Type clipType) => GraphAuthoringMemberBindings.Contract(clipType).Fields.Any(field =>
            field.Annotation<TimelineAuthoringPropertyAttribute>()?.IsSourceAsset == true &&
            field.Annotation<GraphAuthoringResourceAttribute>() != null);

        static GraphAuthoringMember ResourceMember(Clip clip) => GraphAuthoringMemberBindings.Contract(clip).Fields.Single(field =>
            field.Annotation<TimelineAuthoringPropertyAttribute>()?.IsSourceAsset == true &&
            field.Annotation<GraphAuthoringResourceAttribute>() != null);

        static bool IsDocumentSource(Clip clip) => GraphAuthoringMemberBindings.Contract(clip).Fields.Any(field =>
            field.Annotation<TimelineAuthoringPropertyAttribute>()?.IsSourceAsset == true &&
            field.Annotation<GraphAuthoringResourceAttribute>()?.Source == GraphAuthoringReferenceSource.AuthoringDocument);

        public static string ResourceReference(Clip clip) => (string)ResourceMember(clip).Read(clip);

        public static string ResourceReference(Clip clip, TimelineAuthoringClipConfiguration configuration) =>
            configuration.Get<string>(ResourceMember(clip).Metadata.FieldId);

        public static void PrepareSource(TimelineData timeline, Clip clip, TimelineAuthoringClipConfiguration configuration)
        {
            if (IsDocumentSource(clip))
                timeline.AuthoringDocument.Prepare(timeline, clip, configuration);
        }

        public static void PrepareResource(Clip clip)
        {
            if (IsDocumentSource(clip))
            {
                clip.Timeline.AuthoringDocument.Prepare(clip.Timeline, clip, Read(clip));
                return;
            }
            if (clip is ITimelineAuthoringResourceClip resource)
            {
                resource.BindResourceResolution(default);
                if (!string.IsNullOrEmpty(resource.ResourceReference))
                    resource.BindResourceResolution(((ITimelineAuthoringResourceDocument)clip.Timeline.AuthoringDocument)
                        .ResolveResource(clip.Timeline, ResourceDomain(clip), resource.ResourceReference));
            }
        }

        public static string ResourceDomain(Clip clip) => GraphAuthoringMemberBindings.Contract(clip).Fields
            .Single(field => field.Annotation<TimelineAuthoringPropertyAttribute>() is { IsSourceAsset: true })
            .Annotation<GraphAuthoringResourceAttribute>().Domain;

        public static void PrepareResources(TimelineData timeline)
        {
            foreach (Track track in timeline.Tracks)
                foreach (Clip clip in track.Clips)
                    PrepareResource(clip);
        }

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
            TimelineAuthoringClipConfiguration configuration)
        {
            PrepareSource(timeline, clip, configuration);
            configuration.Candidate.Apply(clip);
        }
    }
}
#endif
