#if UNITY_EDITOR
using System;
using BTSMTL.Orchestration;
using BTSMTL.Authoring.Graph;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [GraphAuthoringAnnotation]
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class TimelineAuthoringTrackFieldAttribute : Attribute
    {
        public TimelineAuthoringTrackFieldAttribute(
            string errorCode,
            string errorMessage)
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

    public static class TimelineAuthoringTrackBinding
    {
        public static IReadOnlyList<TimelineAuthoringTrackField> GetFields(string kind) =>
            GraphAuthoringMemberBindings.Contract(TimelineAuthoringTypeCatalog.RequireTrackType(kind)).Fields
                .Where(field => field.Annotation<TimelineAuthoringTrackFieldAttribute>() != null)
                .Select(field => new TimelineAuthoringTrackField(field)).ToArray();

        public static IReadOnlyList<TimelineAuthoringTrackIssue> Validate(
            string kind,
            IReadOnlyDictionary<string, string> values)
        {
            return GetFields(kind)
                .Where(field => !values.TryGetValue(field.FieldId, out string value) || !IsIdentity(value))
                .Select(field => new TimelineAuthoringTrackIssue(
                    field.FieldId,
                    field.ErrorCode,
                    field.ErrorMessage))
                .ToArray();
        }

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
}
#endif
