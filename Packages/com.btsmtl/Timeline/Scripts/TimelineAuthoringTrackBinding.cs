#if UNITY_EDITOR
using System;
using Photon.Deterministic;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public sealed class TimelineAuthoringTrackFieldAttribute : Attribute
    {
        public TimelineAuthoringTrackFieldAttribute(
            string fieldId,
            string errorCode,
            string errorMessage)
        {
            FieldId = fieldId ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string FieldId { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
    }

    public interface ITimelineAuthoringTrackFieldSink
    {
        void ApplyAuthoringField(string fieldId, string value);
    }

    public readonly struct TimelineAuthoringTrackExport
    {
        public TimelineAuthoringTrackExport(TimelineExecutionDomain executionDomain, string animationChannelId, string animationSlotId)
        {
            ExecutionDomain = executionDomain;
            AnimationChannelId = animationChannelId ?? string.Empty;
            AnimationSlotId = animationSlotId ?? string.Empty;
        }

        public TimelineExecutionDomain ExecutionDomain { get; }
        public string AnimationChannelId { get; }
        public string AnimationSlotId { get; }
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
        public static IReadOnlyList<TimelineAuthoringTrackFieldAttribute> GetFields(string kind)
        {
            Type trackType = TimelineAuthoringTypeCatalog.RequireTrackType(kind);
            return trackType
                .GetCustomAttributes(typeof(TimelineAuthoringTrackFieldAttribute), true)
                .OfType<TimelineAuthoringTrackFieldAttribute>()
                .ToArray();
        }

        public static Clip CreateClip(
            TimelineData timeline,
            TimelineContractCatalog catalog,
            Track track,
            UnityEngine.AnimationClip animationAsset,
            FP startTime)
        {
            if (track is AnimationTrack animation)
                return timeline.AddClip(catalog, animationAsset, animation, startTime);
            return timeline.AddClip(catalog, track, startTime);
        }

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
            if (track is not ITimelineAuthoringTrackFieldSink sink)
            {
                if (GetFields(track.ContractKind).Count != 0)
                    throw new InvalidOperationException($"Track '{track.ContractKind}' does not provide an authoring field sink.");
                return;
            }
            foreach (TimelineAuthoringTrackFieldAttribute field in GetFields(track.ContractKind))
                sink.ApplyAuthoringField(
                    field.FieldId,
                    values.TryGetValue(field.FieldId, out string value) ? value : string.Empty);
        }

        public static TimelineAuthoringTrackExport Export(Track track, TimelineContractCatalog catalog)
        {
            if (track == null)
                throw new ArgumentNullException(nameof(track));
            TimelineTrackContract contract = catalog.RequireTrack(track.ContractKind);
            if (!track.HasExplicitExecutionDomain || !contract.SupportsExecutionDomain(track.ExecutionDomain))
                throw new InvalidOperationException($"Timeline track '{track.AuthoringId}' has a missing or unsupported execution domain '{track.ExecutionDomain}'.");
            return track is AnimationTrack animation
                ? new TimelineAuthoringTrackExport(
                    track.ExecutionDomain,
                    animation.AnimationChannelId.Value.ToString(),
                    animation.AnimationSlotId)
                : new TimelineAuthoringTrackExport(track.ExecutionDomain, string.Empty, string.Empty);
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
