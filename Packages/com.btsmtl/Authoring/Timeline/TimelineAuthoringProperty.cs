#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Graph;
using ThirdPersonSimulation;

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
    }
}
#endif
