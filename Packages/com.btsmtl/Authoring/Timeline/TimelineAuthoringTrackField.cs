#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Timeline
{
    [GraphAuthoringAnnotation]
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class TimelineAuthoringTrackFieldAttribute : Attribute
    {
        public TimelineAuthoringTrackFieldAttribute(string errorCode, string errorMessage)
        {
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string ErrorCode { get; }
        public string ErrorMessage { get; }
    }

    public readonly struct TimelineAuthoringTrackField
    {
        readonly GraphAuthoringMember m_Member;
        readonly TimelineAuthoringTrackFieldAttribute m_Annotation;

        public TimelineAuthoringTrackField(GraphAuthoringMember member)
        {
            m_Member = member;
            m_Annotation = member.Annotation<TimelineAuthoringTrackFieldAttribute>();
        }

        public string FieldId => m_Member.Metadata.FieldId;
        public GraphAuthoringFieldValueKind ValueKind => m_Member.Metadata.ValueKind;
        public IReadOnlyList<string> AllowedValues => m_Member.AllowedValues;
        public string ErrorCode => m_Annotation.ErrorCode;
        public string ErrorMessage => m_Annotation.ErrorMessage;

        public static IReadOnlyList<TimelineAuthoringTrackIssue> Validate(
            IReadOnlyList<TimelineAuthoringTrackField> fields, IReadOnlyDictionary<string, string> values) =>
            fields.Where(field => !values.TryGetValue(field.FieldId, out string value) || !IsIdentity(value))
                .Select(field => new TimelineAuthoringTrackIssue(field.FieldId, field.ErrorCode, field.ErrorMessage))
                .ToArray();

        static bool IsIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) ||
                value.StartsWith("@", StringComparison.Ordinal))
                return false;
            if (!value.StartsWith("local:", StringComparison.Ordinal))
                return true;
            string local = value.Substring("local:".Length);
            return local.Length > 0 && local.All(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.');
        }
    }

    public readonly struct TimelineAuthoringTrackIssue
    {
        public TimelineAuthoringTrackIssue(string fieldId, string errorCode, string errorMessage)
        {
            FieldId = fieldId ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string FieldId { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
    }
}
#endif
