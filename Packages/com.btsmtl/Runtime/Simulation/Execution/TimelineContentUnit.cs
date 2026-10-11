using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineContentBindingUse
    {
        public TimelineContentBindingUse(
            string bindingId,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime,
            string targetBindingId = null)
        {
            BindingId = string.IsNullOrWhiteSpace(bindingId)
                ? throw new ArgumentException("Timeline binding identity is required.", nameof(bindingId))
                : bindingId.Trim();
            ValueKind = valueKind;
            Access = access;
            Lifetime = lifetime;
            TargetBindingId = targetBindingId?.Trim() ?? string.Empty;
        }

        public string BindingId { get; }
        public TimelineBindingValueKind ValueKind { get; }
        public TimelineBindingAccess Access { get; }
        public TimelineBindingLifetime Lifetime { get; }
        public string TargetBindingId { get; }
    }

    public readonly struct TimelineContentDependency
    {
        public TimelineContentDependency(string identity, string kind, string sourcePath, string contentHash)
        {
            Identity = Require(identity, nameof(identity));
            Kind = Require(kind, nameof(kind));
            SourcePath = sourcePath ?? string.Empty;
            ContentHash = Require(contentHash, nameof(contentHash));
        }

        public string Identity { get; }
        public string Kind { get; }
        public string SourcePath { get; }
        public string ContentHash { get; }

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline content dependency identity is required.", name)
                : value.Trim();
        }
    }

    public interface ITimelineContentClosureSource
    {
        void CollectContentClosure(TimelineContentClosureBuilder builder);
    }

    public readonly struct TimelineContentCurveKey
    {
        public TimelineContentCurveKey(
            float time,
            float value,
            float inTangent,
            float outTangent,
            float inWeight,
            float outWeight,
            int weightedMode)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
            InWeight = inWeight;
            OutWeight = outWeight;
            WeightedMode = weightedMode;
        }

        public float Time { get; }
        public float Value { get; }
        public float InTangent { get; }
        public float OutTangent { get; }
        public float InWeight { get; }
        public float OutWeight { get; }
        public int WeightedMode { get; }
    }

    public readonly struct TimelineContentCurve
    {
        public TimelineContentCurve(
            string channelId,
            string displayName,
            TimelineCurveTimeDomain timeDomain,
            TimelineCurveValueDomain valueDomain,
            int preWrapMode,
            int postWrapMode,
            IReadOnlyList<TimelineContentCurveKey> keys)
        {
            ChannelId = string.IsNullOrWhiteSpace(channelId)
                ? throw new ArgumentException("Timeline curve channel identity is required.", nameof(channelId))
                : channelId.Trim();
            DisplayName = displayName ?? string.Empty;
            TimeDomain = timeDomain;
            ValueDomain = valueDomain;
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            Keys = new ReadOnlyCollection<TimelineContentCurveKey>(
                new List<TimelineContentCurveKey>(keys ?? Array.Empty<TimelineContentCurveKey>()));
        }

        public string ChannelId { get; }
        public string DisplayName { get; }
        public TimelineCurveTimeDomain TimeDomain { get; }
        public TimelineCurveValueDomain ValueDomain { get; }
        public int PreWrapMode { get; }
        public int PostWrapMode { get; }
        public IReadOnlyList<TimelineContentCurveKey> Keys { get; }
    }

    public readonly struct TimelineContentMarker
    {
        public TimelineContentMarker(
            string markerId,
            string trackAuthoringId,
            TimelineClipExecutionPolicy executionPolicy,
            FixedScalar time,
            bool forceTriggerOnInitialSeek,
            string graphId,
            string graphRevision,
            bool trackMuted)
        {
            MarkerId = Require(markerId, nameof(markerId));
            TrackAuthoringId = Require(trackAuthoringId, nameof(trackAuthoringId));
            ExecutionPolicy = executionPolicy;
            TrackMuted = trackMuted;
            ForceTriggerOnInitialSeek = forceTriggerOnInitialSeek;
            GraphId = Require(graphId, nameof(graphId));
            GraphRevision = Require(graphRevision, nameof(graphRevision));
            Time = time < FixedScalar.Zero ? throw new ArgumentOutOfRangeException(nameof(time)) : time;
        }

        public string MarkerId { get; }
        public bool ForceTriggerOnInitialSeek { get; }
        public bool TrackMuted { get; }
        public string TrackAuthoringId { get; }
        public TimelineClipExecutionPolicy ExecutionPolicy { get; }
        public FixedScalar Time { get; }
        public string GraphId { get; }
        public string GraphRevision { get; }

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline content identity is required.", name)
                : value.Trim();
        }
    }

    public sealed class TimelineContentClosureBuilder
    {
        readonly List<TimelineContentDependency> m_Dependencies = new List<TimelineContentDependency>();
        readonly List<string> m_Errors = new List<string>();

        public IReadOnlyList<TimelineContentDependency> Dependencies => m_Dependencies;
        public IReadOnlyList<string> Errors => m_Errors;

        public void AddDependency(string identity, string kind, string sourcePath, string contentHash)
        {
            TimelineContentDependency dependency = new TimelineContentDependency(identity, kind, sourcePath, contentHash);
            for (int i = 0; i < m_Dependencies.Count; i++)
            {
                if (string.Equals(m_Dependencies[i].Identity, dependency.Identity, StringComparison.Ordinal))
                {
                    if (!string.Equals(m_Dependencies[i].ContentHash, dependency.ContentHash, StringComparison.Ordinal))
                        AddError("timeline_dependency_version_conflict", dependency.Identity, "The same dependency identity resolved to different content hashes.");
                    return;
                }
            }
            m_Dependencies.Add(dependency);
        }

        public void AddError(string code, string sourcePath, string message)
        {
            m_Errors.Add($"{code}:{sourcePath}:{message}");
        }
    }

    public readonly struct TimelineContentClip
    {
        public TimelineContentClip(
            string authoringId,
            string contractKind,
            string trackAuthoringId,
            TimelineClipExecutionPolicy executionPolicy,
            TimelineClipTimeRange timeRange,
            TimelineClipExecutionPhase executionPhase,
            TimelineClipExitSource exitSource,
            TimelineCapability capabilities,
            bool supportsStop,
            bool trackMuted,
            TimelineContentParameters parameters,
            IReadOnlyList<TimelineContentBindingUse> bindings,
            IReadOnlyList<TimelineContentCurve> curves = null,
            TimelineGraphBinding graphBinding = default)
        {
            AuthoringId = Require(authoringId, nameof(authoringId));
            ContractKind = Require(contractKind, nameof(contractKind));
            TrackAuthoringId = Require(trackAuthoringId, nameof(trackAuthoringId));
            ExecutionPolicy = executionPolicy;
            TimeRange = timeRange;
            ExecutionPhase = executionPhase;
            ExitSource = exitSource;
            Capabilities = capabilities;
            SupportsStop = supportsStop;
            TrackMuted = trackMuted;
            Bindings = new ReadOnlyCollection<TimelineContentBindingUse>(new List<TimelineContentBindingUse>(bindings ?? Array.Empty<TimelineContentBindingUse>()));
            Curves = new ReadOnlyCollection<TimelineContentCurve>(new List<TimelineContentCurve>(curves ?? Array.Empty<TimelineContentCurve>()));
            GraphBinding = graphBinding;
            Parameters = parameters;
        }

        public string AuthoringId { get; }
        public string ContractKind { get; }
        public string TrackAuthoringId { get; }
        public TimelineClipExecutionPolicy ExecutionPolicy { get; }
        public TimelineClipTimeRange TimeRange { get; }
        public FixedScalar StartTime => TimeRange.Start;
        public FixedScalar EndTime => TimeRange.End;
        public FixedScalar DurationTime => EndTime - StartTime;
        public FixedScalar EaseInTime => TimeRange.EffectiveEaseIn;
        public FixedScalar EaseOutTime => TimeRange.EffectiveEaseOut;
        public FixedScalar ClipInTime => TimeRange.ClipIn;
        public TimelineContentParameters Parameters { get; }
        public TimelineClipExecutionPhase ExecutionPhase { get; }
        public TimelineTreeExecutionPhase TreeExecutionPhase => ExecutionPhase == TimelineClipExecutionPhase.Decision
            ? TimelineTreeExecutionPhase.Decision : TimelineTreeExecutionPhase.Commit;
        public TimelineClipExitSource ExitSource { get; }
        public TimelineCapability Capabilities { get; }
        public bool SupportsStop { get; }
        public bool TrackMuted { get; }
        public IReadOnlyList<TimelineContentBindingUse> Bindings { get; }
        public IReadOnlyList<TimelineContentCurve> Curves { get; }
        public TimelineGraphBinding GraphBinding { get; }

        public TimelineContentCurve RequireCurve(string channelId)
        {
            for (int i = 0; i < Curves.Count; i++)
                if (Curves[i].ChannelId == channelId)
                    return Curves[i];
            throw new InvalidOperationException($"Timeline Clip '{AuthoringId}' has no curve '{channelId}'.");
        }

        TimelineContentClip(TimelineContentClip source, in TimelineClipTimeRange range)
        {
            AuthoringId = source.AuthoringId;
            ContractKind = source.ContractKind;
            TrackAuthoringId = source.TrackAuthoringId;
            ExecutionPolicy = source.ExecutionPolicy;
            TimeRange = range;
            ExecutionPhase = source.ExecutionPhase;
            ExitSource = source.ExitSource;
            Capabilities = source.Capabilities;
            SupportsStop = source.SupportsStop;
            TrackMuted = source.TrackMuted;
            Bindings = source.Bindings;
            Curves = source.Curves;
            GraphBinding = source.GraphBinding;
            Parameters = source.Parameters;
        }

        internal TimelineContentClip WithTimeRange(in TimelineClipTimeRange range) => new TimelineContentClip(this, in range);

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline content identity is required.", name)
                : value.Trim();
        }
    }

    public readonly struct TimelineContentSection
    {
        public TimelineContentSection(string authoringId, FixedScalar time, string nextSectionId, string branchId)
        {
            AuthoringId = Require(authoringId, nameof(authoringId));
            if (time < FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(time));
            Time = time;
            NextSectionId = nextSectionId?.Trim() ?? string.Empty;
            BranchId = branchId?.Trim() ?? string.Empty;
        }

        public string AuthoringId { get; }
        public FixedScalar Time { get; }
        public string NextSectionId { get; }
        public string BranchId { get; }

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline section identity is required.", name)
                : value.Trim();
        }
    }

    public readonly struct TimelineContentTrack
    {
        public TimelineContentTrack(
            string authoringId,
            string contractKind,
            TimelineClipExecutionPolicy executionPolicy,
            int order,
            bool muted,
            IReadOnlyList<string> clipAuthoringIds,
            string name = "")
        {
            AuthoringId = Require(authoringId, nameof(authoringId));
            ContractKind = Require(contractKind, nameof(contractKind));
            ExecutionPolicy = executionPolicy;
            Order = order;
            Muted = muted;
            Name = name;
            ClipAuthoringIds = new ReadOnlyCollection<string>(
                new List<string>(clipAuthoringIds ?? Array.Empty<string>()));
        }

        public string AuthoringId { get; }
        public string ContractKind { get; }
        public TimelineClipExecutionPolicy ExecutionPolicy { get; }
        public int Order { get; }
        public bool Muted { get; }
        public string Name { get; }
        public IReadOnlyList<string> ClipAuthoringIds { get; }

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline track identity is required.", name)
                : value.Trim();
        }
    }

    public sealed class TimelineContentUnit
    {
        TimelineContentClip[] m_TimeClips;
        int[] m_TimeClipSlots;
        public CanonicalTextValue Revision { get; private set; }
        public CanonicalTextValue SourceRevision { get; private set; }
        public TimelineContentUnit(
            string identity,
            string authoringId,
            string contentHash,
            string rootFingerprint,
            string name,
            FixedScalar duration,
            bool loop,
            IReadOnlyList<TimelineContentTrack> tracks,
            IReadOnlyList<TimelineContentClip> clips,
            IReadOnlyList<TimelineContentMarker> markers,
            IReadOnlyList<TimelineContentSection> sections,
            IReadOnlyList<TimelineBindingDeclaration> bindings,
            IReadOnlyList<TimelineContentDependency> dependencies)
        {
            Identity = identity ?? string.Empty;
            AuthoringId = authoringId;
            Revision = contentHash;
            SourceRevision = rootFingerprint;
            Name = name ?? string.Empty;
            Duration = duration;
            Loop = loop;
            Tracks = new ReadOnlyCollection<TimelineContentTrack>(new List<TimelineContentTrack>(tracks ?? Array.Empty<TimelineContentTrack>()));
            Clips = new ReadOnlyCollection<TimelineContentClip>(new List<TimelineContentClip>(clips ?? Array.Empty<TimelineContentClip>()));
            Markers = new ReadOnlyCollection<TimelineContentMarker>(new List<TimelineContentMarker>(markers ?? Array.Empty<TimelineContentMarker>()));
            Sections = new ReadOnlyCollection<TimelineContentSection>(new List<TimelineContentSection>(sections ?? Array.Empty<TimelineContentSection>()));
            Bindings = new ReadOnlyCollection<TimelineBindingDeclaration>(new List<TimelineBindingDeclaration>(bindings ?? Array.Empty<TimelineBindingDeclaration>()));
            Dependencies = new ReadOnlyCollection<TimelineContentDependency>(new List<TimelineContentDependency>(dependencies ?? Array.Empty<TimelineContentDependency>()));
        }

        public string Identity { get; }
        public string AuthoringId { get; }
        public string ContentHash => Revision.ToString();
        public string RootFingerprint => SourceRevision.ToString();
        public string Name { get; }
        public FixedScalar Duration { get; private set; }
        public bool Loop { get; }
        public IReadOnlyList<TimelineContentTrack> Tracks { get; }
        public IReadOnlyList<TimelineContentClip> Clips { get; private set; }
        public IReadOnlyList<TimelineContentMarker> Markers { get; }
        public IReadOnlyList<TimelineContentSection> Sections { get; }
        public IReadOnlyList<TimelineBindingDeclaration> Bindings { get; }
        public IReadOnlyList<TimelineContentDependency> Dependencies { get; }

        public TimelineContentUnit CreateTimeRangeCandidate(ReadOnlySpan<int> authoringSlots)
        {
            var candidate = new TimelineContentUnit(Identity, AuthoringId, ContentHash, RootFingerprint, Name, Duration, Loop,
                Tracks, Clips, Markers, Sections, Bindings, Dependencies);
            candidate.m_TimeClips = new TimelineContentClip[Clips.Count];
            candidate.m_TimeClipSlots = authoringSlots.ToArray();
            for (int i = 0; i < Clips.Count; i++)
                candidate.m_TimeClips[i] = Clips[i];
            candidate.Clips = candidate.m_TimeClips;
            return candidate;
        }

        public void UpdateTimeRangeCandidate(ReadOnlySpan<TimelineClipTimeRange> ranges, FixedScalar duration,
            CanonicalTextValue sourceRevision, CanonicalTextValue revision)
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                int slot = m_TimeClipSlots[i];
                m_TimeClips[slot] = m_TimeClips[slot].WithTimeRange(in ranges[i]);
            }
            Duration = duration;
            SourceRevision = sourceRevision;
            Revision = revision;
        }
        public bool IsValid => !string.IsNullOrEmpty(Identity) &&
            !string.IsNullOrEmpty(AuthoringId) &&
            !Revision.IsEmpty &&
            !SourceRevision.IsEmpty &&
            Duration >= FixedScalar.Zero;
    }

    public sealed class TimelineContentDiscoveryResult
    {
        public TimelineContentDiscoveryResult(TimelineContentUnit content, IReadOnlyList<string> errors)
        {
            Content = content;
            Errors = new ReadOnlyCollection<string>(new List<string>(errors ?? Array.Empty<string>()));
        }

        public TimelineContentUnit Content { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Content != null && Content.IsValid && Errors.Count == 0;
    }

}
