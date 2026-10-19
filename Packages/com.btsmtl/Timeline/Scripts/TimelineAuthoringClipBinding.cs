#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public enum TimelineAuthoringSourceBoundary : byte { None, Start, End }

    [GraphAuthoringAnnotation]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class TimelineAuthoringPropertyAttribute : Attribute
    {
        public bool Trimmed { get; set; }
        public bool SourceMappingOnly { get; set; }
        public bool IsSourceAsset { get; set; }
        public TimelineAuthoringSourceBoundary SourceBoundary { get; set; }
        public TimelineBindingValueKind[] BindingValueKinds { get; set; } = Array.Empty<TimelineBindingValueKind>();
        public TimelineBindingAccess BindingAccess { get; set; }
        public TimelineBindingLifetime BindingLifetime { get; set; }
    }

    public readonly struct TimelineAuthoringProperty
    {
        readonly GraphAuthoringMember m_Member;
        readonly TimelineAuthoringPropertyAttribute m_Annotation;
        public TimelineAuthoringProperty(GraphAuthoringMember member)
        {
            m_Member = member;
            m_Annotation = member.Annotation<TimelineAuthoringPropertyAttribute>();
        }
        public string PropertyId => m_Member.Metadata.FieldId;
        public GraphAuthoringFieldValueKind Kind => m_Member.Metadata.ValueKind;
        public Type EnumType => m_Member.Metadata.EnumType;
        public bool Optional => m_Member.Metadata.Optional;
        public bool HasMinimum => m_Member.Metadata.HasMinimum;
        public double Minimum => m_Member.Metadata.Minimum;
        public bool HasMaximum => m_Member.Metadata.HasMaximum;
        public double Maximum => m_Member.Metadata.Maximum;
        public bool Finite => m_Member.Metadata.Finite;
        public bool Trimmed => m_Annotation?.Trimmed == true;
        public bool SourceMappingOnly => m_Annotation?.SourceMappingOnly == true;
        public bool IsSourceAsset => m_Annotation?.IsSourceAsset == true;
        public TimelineAuthoringSourceBoundary SourceBoundary => m_Annotation?.SourceBoundary ?? TimelineAuthoringSourceBoundary.None;
        public IReadOnlyList<TimelineBindingValueKind> BindingValueKinds =>
            m_Annotation?.BindingValueKinds ?? Array.Empty<TimelineBindingValueKind>();
        public TimelineBindingAccess BindingAccess => m_Annotation.BindingAccess;
        public TimelineBindingLifetime BindingLifetime => m_Annotation.BindingLifetime;
        public bool Supports(Clip clip) => Supports(clip, TimelineAuthoringClipBinding.SourceAsset(clip));
        public bool Supports(Clip clip, UnityEngine.Object source) =>
            !SourceMappingOnly || source is ITimelineAuthoringSourceAsset;
    }

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
        readonly Dictionary<string, object> m_Values = new(StringComparer.Ordinal);
        public object this[string propertyId]
        {
            get => m_Values[propertyId];
            set => m_Values[propertyId] = value;
        }
        public IReadOnlyDictionary<string, object> Values => m_Values;
        public T Get<T>(string propertyId) => (T)m_Values[propertyId];
        public void Set(string propertyId, object value) => m_Values[propertyId] = value;
    }

    public static class TimelineAuthoringClipBinding
    {
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

        public static TimelineAuthoringClipConfiguration Read(Clip clip)
        {
            var result = new TimelineAuthoringClipConfiguration();
            foreach (GraphAuthoringMember member in GraphAuthoringMemberBindings.Contract(clip).Fields)
                result.Set(member.Metadata.FieldId, member.Read(clip));
            return result;
        }

        public static TimelineAuthoringClipConfiguration Defaults(Clip clip)
        {
            var result = new TimelineAuthoringClipConfiguration();
            foreach (KeyValuePair<string, object> field in GraphAuthoringMemberBindings.ReadDefaults(clip.GetType()))
                result.Set(field.Key, field.Value);
            return result;
        }

        public static Type ValueType(Clip clip, TimelineAuthoringProperty property) =>
            GraphAuthoringMemberBindings.ValueType(clip.GetType(), property.PropertyId);

        public static void Configure(TimelineData timeline, Clip clip,
            TimelineAuthoringClipConfiguration configuration) =>
            GraphAuthoringMemberBindings.Apply(clip, configuration.Values);
    }
}
#endif
