using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BTSMTL.Diagnostics;
using ThirdPersonSimulation;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BTSMTL.Timeline
{
#if UNITY_EDITOR
    public interface ITimelineTreeClipAuthoringSource
    {
        ScriptableObject AssetTree { get; }
    }

    public interface ITimelineTreeGraphAsset
    {
        string AuthoringId { get; }
        bool IsTimelineTree { get; }
        bool IsTimelineTrigger { get; }
        void BindTimelineAuthoringContext(TimelineData timeline, Clip clip);
        bool TryGetDeclaredEndTime(out OrchestrationTime endTime);
        void ValidateTimelineBinding(TimelineData timeline, Clip clip, List<string> errors, bool requireComplete);
        Func<ScriptableObject, string> CreateTimelineContentFingerprint();
        void CollectTimelineContentClosure(TimelineContentClosureBuilder builder, string sourcePath,
            TimelineExecutionDomain domain, Func<ScriptableObject, string> fingerprint);
    }
#endif

#if UNITY_EDITOR
    public interface ITimelineTerminalTimeAlignedClip
    {
        bool AlignTerminalTime(OrchestrationTime terminalTime);
    }
#endif

    public delegate void TimelineClipContractValidator(Clip clip, List<string> errors);
    public delegate void TimelineContentContractValidator(TimelineData timeline, List<string> errors);

    [Serializable]
    public sealed class TimelineExternalBindingDeclaration
    {
        [SerializeField, HideInInspector]
        string m_AuthoringId;

        [SerializeField]
        string m_BindingId;

        [SerializeField]
        string m_DisplayName;

        [SerializeField]
        string m_Domain;

        [SerializeField]
        string m_ParameterId;

        [SerializeField]
        TimelineBindingValueKind m_ValueKind;

        [SerializeField]
        TimelineBindingAccess m_Access;

        [SerializeField]
        TimelineBindingLifetime m_Lifetime;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public string BindingId => m_BindingId ?? string.Empty;
        public string DisplayName => m_DisplayName ?? string.Empty;
        public string Domain => m_Domain ?? string.Empty;
        public string ParameterId => m_ParameterId ?? string.Empty;
        public TimelineBindingValueKind ValueKind => m_ValueKind;
        public TimelineBindingAccess Access => m_Access;
        public TimelineBindingLifetime Lifetime => m_Lifetime;

        public void Configure(
            string bindingId,
            string displayName,
            string domain,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime,
            string parameterId = null)
        {
            m_BindingId = Normalize(bindingId);
            m_DisplayName = Normalize(displayName);
            m_Domain = Normalize(domain);
            m_ParameterId = Normalize(parameterId);
            m_ValueKind = valueKind;
            m_Access = access;
            m_Lifetime = lifetime;
        }

        public bool Validate(out string error)
        {
            if (!AuthoringIdentity.IsValid(AuthoringId))
            {
                error = "authoring identity is invalid";
                return false;
            }
            if (string.IsNullOrEmpty(BindingId) || string.IsNullOrEmpty(DisplayName) || string.IsNullOrEmpty(Domain))
            {
                error = "binding identity, display name and domain are required";
                return false;
            }
            if (!Enum.IsDefined(typeof(TimelineBindingValueKind), ValueKind) ||
                !Enum.IsDefined(typeof(TimelineBindingAccess), Access) ||
                !Enum.IsDefined(typeof(TimelineBindingLifetime), Lifetime))
            {
                error = "binding type, access or lifetime is invalid";
                return false;
            }
            if (ValueKind != TimelineBindingValueKind.Target && string.IsNullOrEmpty(ParameterId))
            {
                error = "scalar and boolean bindings require a parameter identity";
                return false;
            }
            if (ValueKind == TimelineBindingValueKind.Target && !string.IsNullOrEmpty(ParameterId))
            {
                error = "target bindings cannot contain a parameter identity";
                return false;
            }
            if (Access == TimelineBindingAccess.Input && Lifetime != TimelineBindingLifetime.Call)
            {
                error = "input bindings must be captured for the call";
                return false;
            }
            if (Access != TimelineBindingAccess.Input && Lifetime != TimelineBindingLifetime.Tick)
            {
                error = "read and write bindings must use the current tick";
                return false;
            }
            error = string.Empty;
            return true;
        }

#if UNITY_EDITOR
        public void ConfigureAuthoringIdentity(string authoringId)
        {
            if (!AuthoringIdentity.IsValid(authoringId))
                throw new ArgumentException("Timeline binding authoring identity is invalid.", nameof(authoringId));
            m_AuthoringId = authoringId;
        }

        public bool EnsureAuthoringIdentity()
        {
            if (AuthoringIdentity.IsValid(m_AuthoringId))
                return false;
            m_AuthoringId = AuthoringIdentity.Create();
            return true;
        }

        public void RegenerateAuthoringIdentity()
        {
            m_AuthoringId = AuthoringIdentity.Create();
        }
#endif

        static string Normalize(string value) => value?.Trim() ?? string.Empty;
    }

    [Serializable]
    public sealed class TimelineExternalBindingUse
    {
        [SerializeField]
        string m_BindingId;

        [SerializeField]
        string m_TargetBindingId;

        [SerializeField]
        TimelineBindingValueKind m_ValueKind;

        [SerializeField]
        TimelineBindingAccess m_Access;

        [SerializeField]
        TimelineBindingLifetime m_Lifetime;

        public TimelineExternalBindingUse()
        {
        }

        public TimelineExternalBindingUse(
            string bindingId,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime,
            string targetBindingId = null)
        {
            Configure(bindingId, valueKind, access, lifetime, targetBindingId);
        }

        public string BindingId => m_BindingId ?? string.Empty;
        public string TargetBindingId => m_TargetBindingId ?? string.Empty;
        public TimelineBindingValueKind ValueKind => m_ValueKind;
        public TimelineBindingAccess Access => m_Access;
        public TimelineBindingLifetime Lifetime => m_Lifetime;

        public void Configure(
            string bindingId,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime,
            string targetBindingId = null)
        {
            m_BindingId = bindingId?.Trim() ?? string.Empty;
            m_TargetBindingId = targetBindingId?.Trim() ?? string.Empty;
            m_ValueKind = valueKind;
            m_Access = access;
            m_Lifetime = lifetime;
        }

        public bool Matches(TimelineBindingDeclaration declaration)
        {
            return string.Equals(BindingId, declaration.BindingId, StringComparison.Ordinal) &&
                   ValueKind == declaration.ValueKind &&
                   Access == declaration.Access &&
                   Lifetime == declaration.Lifetime;
        }
    }

    public interface ITimelineExternalBindingUseSource
    {
        IReadOnlyList<TimelineExternalBindingUse> ExternalBindingUses { get; }
    }

    public sealed class TimelineClipContract
    {
        readonly ReadOnlyCollection<TimelineBindingRequirement> m_Bindings;

        public TimelineClipContract(
            string kind,
            string trackKind,
            TimelineClipExecutionPhase executionPhase,
            TimelineContentKind contentKind,
            bool supportsStop,
            bool requiresPositiveDuration,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            params TimelineBindingRequirement[] bindings)
            : this(
                kind,
                trackKind,
                executionPhase,
                contentKind,
                supportsStop,
                requiresPositiveDuration,
                executionDomain,
                outputKind,
                TimelineExecutionDomains.ToMask(executionDomain),
                null,
                bindings)
        {
        }

        public TimelineClipContract(
            string kind,
            string trackKind,
            TimelineClipExecutionPhase executionPhase,
            TimelineContentKind contentKind,
            bool supportsStop,
            bool requiresPositiveDuration,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            TimelineExecutionDomainMask allowedExecutionDomains,
            params TimelineBindingRequirement[] bindings)
            : this(
                kind,
                trackKind,
                executionPhase,
                contentKind,
                supportsStop,
                requiresPositiveDuration,
                executionDomain,
                outputKind,
                allowedExecutionDomains,
                null,
                bindings)
        {
        }

        public TimelineClipContract(
            string kind,
            string trackKind,
            TimelineClipExecutionPhase executionPhase,
            TimelineContentKind contentKind,
            bool supportsStop,
            bool requiresPositiveDuration,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            TimelineClipContractValidator validator,
            params TimelineBindingRequirement[] bindings)
            : this(
                kind,
                trackKind,
                executionPhase,
                contentKind,
                supportsStop,
                requiresPositiveDuration,
                executionDomain,
                outputKind,
                TimelineExecutionDomains.ToMask(executionDomain),
                validator,
                bindings)
        {
        }

        public TimelineClipContract(
            string kind,
            string trackKind,
            TimelineClipExecutionPhase executionPhase,
            TimelineContentKind contentKind,
            bool supportsStop,
            bool requiresPositiveDuration,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            TimelineExecutionDomainMask allowedExecutionDomains,
            TimelineClipContractValidator validator,
            params TimelineBindingRequirement[] bindings)
        {
            Kind = Require(kind, nameof(kind));
            TrackKind = Require(trackKind, nameof(trackKind));
            SupportedExecutionPhases = executionPhase;
            DefaultExecutionPhase = executionPhase == TimelineClipExecutionPhase.DecisionAndCommit
                ? TimelineClipExecutionPhase.Commit
                : executionPhase;
            ContentKind = contentKind;
            SupportsStop = supportsStop;
            RequiresPositiveDuration = requiresPositiveDuration;
            ExecutionPolicy = new TimelineClipExecutionPolicy(executionDomain, outputKind);
            AllowedExecutionDomains = allowedExecutionDomains;
            if (AllowedExecutionDomains == TimelineExecutionDomainMask.None ||
                (AllowedExecutionDomains & TimelineExecutionDomains.ToMask(executionDomain)) == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(allowedExecutionDomains));
            }
            Validator = validator;
            m_Bindings = Array.AsReadOnly(bindings == null
                ? Array.Empty<TimelineBindingRequirement>()
                : (TimelineBindingRequirement[])bindings.Clone());
        }

        public string Kind { get; }
        public string TrackKind { get; }
        public TimelineClipExecutionPhase SupportedExecutionPhases { get; }
        public TimelineClipExecutionPhase DefaultExecutionPhase { get; }
        public TimelineContentKind ContentKind { get; }
        public bool SupportsStop { get; }
        public bool RequiresPositiveDuration { get; }
        public TimelineClipExecutionPolicy ExecutionPolicy { get; }
        public TimelineExecutionDomainMask AllowedExecutionDomains { get; }
        public TimelineClipContractValidator Validator { get; }
        public IReadOnlyList<TimelineBindingRequirement> Bindings => m_Bindings;

        public bool SupportsExecutionPhase(TimelineClipExecutionPhase phase) =>
            (SupportedExecutionPhases & phase) == phase;

        public bool SupportsExecutionDomain(TimelineExecutionDomain domain) =>
            TimelineExecutionDomains.IsValid(domain) &&
            (AllowedExecutionDomains & TimelineExecutionDomains.ToMask(domain)) != 0;

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline Clip contract identity is required.", name)
                : value.Trim();
        }
    }

    public interface ITimelineContractProvider
    {
        IReadOnlyList<TimelineTrackContract> Tracks { get; }
        IReadOnlyList<TimelineClipContract> Clips { get; }
        IReadOnlyList<ITimelineContentParameterCodec> ParameterCodecs { get; }
        TimelineContentContractValidator ContentValidator { get; }
    }

    public sealed class TimelineContractProvider : ITimelineContractProvider
    {
        readonly ReadOnlyCollection<TimelineTrackContract> m_Tracks;
        readonly ReadOnlyCollection<TimelineClipContract> m_Clips;
        readonly ReadOnlyCollection<ITimelineContentParameterCodec> m_ParameterCodecs;

        public TimelineContractProvider(
            IEnumerable<TimelineTrackContract> tracks,
            IEnumerable<TimelineClipContract> clips,
            IEnumerable<ITimelineContentParameterCodec> parameterCodecs,
            TimelineContentContractValidator contentValidator = null)
        {
            m_Tracks = new ReadOnlyCollection<TimelineTrackContract>(
                new List<TimelineTrackContract>(tracks ?? throw new ArgumentNullException(nameof(tracks))));
            m_Clips = new ReadOnlyCollection<TimelineClipContract>(
                new List<TimelineClipContract>(clips ?? throw new ArgumentNullException(nameof(clips))));
            m_ParameterCodecs = new ReadOnlyCollection<ITimelineContentParameterCodec>(
                new List<ITimelineContentParameterCodec>(parameterCodecs));
            ContentValidator = contentValidator;
        }

        public IReadOnlyList<TimelineTrackContract> Tracks => m_Tracks;
        public IReadOnlyList<TimelineClipContract> Clips => m_Clips;
        public IReadOnlyList<ITimelineContentParameterCodec> ParameterCodecs => m_ParameterCodecs;
        public TimelineContentContractValidator ContentValidator { get; }
    }

    internal sealed class TimelineContractCatalogData
    {
        readonly ReadOnlyCollection<TimelineTrackContract> m_Tracks;
        readonly ReadOnlyCollection<TimelineClipContract> m_Clips;
        readonly Dictionary<string, TimelineTrackContract> m_TracksByKind;
        readonly Dictionary<string, TimelineClipContract> m_ClipsByKind;
        readonly List<TimelineContentContractValidator> m_ContentValidators;

        public TimelineContractCatalogData(IEnumerable<ITimelineContractProvider> providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            var tracks = new List<TimelineTrackContract>();
            var clips = new List<TimelineClipContract>();
            var parameterCodecs = new List<ITimelineContentParameterCodec>();
            m_ContentValidators = new List<TimelineContentContractValidator>();
            foreach (ITimelineContractProvider provider in providers)
            {
                if (provider == null)
                    throw new InvalidOperationException("Timeline contract composition contains a missing provider.");
                tracks.AddRange(provider.Tracks ?? throw new InvalidOperationException("Timeline contract provider has no Track contracts."));
                clips.AddRange(provider.Clips ?? throw new InvalidOperationException("Timeline contract provider has no Clip contracts."));
                parameterCodecs.AddRange(provider.ParameterCodecs);
                if (provider.ContentValidator != null)
                    m_ContentValidators.Add(provider.ContentValidator);
            }
            m_TracksByKind = BuildTrackIndex(tracks);
            m_ClipsByKind = BuildClipIndex(clips);
            ParameterCodecs = new TimelineContentParameterCodecs(parameterCodecs);
            for (int i = 0; i < clips.Count; i++)
            {
                if (!m_TracksByKind.ContainsKey(clips[i].TrackKind))
                    throw new InvalidOperationException($"Timeline Clip contract '{clips[i].Kind}' references missing Track contract '{clips[i].TrackKind}'.");
                ParameterCodecs.Require(clips[i].Kind);
            }
            m_Tracks = new ReadOnlyCollection<TimelineTrackContract>(tracks);
            m_Clips = new ReadOnlyCollection<TimelineClipContract>(clips);
        }

        public IReadOnlyList<TimelineTrackContract> Tracks => m_Tracks;
        public IReadOnlyList<TimelineClipContract> Clips => m_Clips;
        public TimelineContentParameterCodecs ParameterCodecs { get; }

        public bool TryGetTrack(string kind, out TimelineTrackContract contract) =>
            m_TracksByKind.TryGetValue(kind ?? string.Empty, out contract);

        public bool TryGetClip(string kind, out TimelineClipContract contract) =>
            m_ClipsByKind.TryGetValue(kind ?? string.Empty, out contract);

        public void ValidateContent(TimelineData timeline, List<string> errors)
        {
            for (int i = 0; i < m_ContentValidators.Count; i++)
                m_ContentValidators[i](timeline, errors);
        }

        static Dictionary<string, TimelineTrackContract> BuildTrackIndex(IReadOnlyList<TimelineTrackContract> contracts)
        {
            var result = new Dictionary<string, TimelineTrackContract>(StringComparer.Ordinal);
            for (int i = 0; i < contracts.Count; i++)
            {
                string kind = contracts[i].Kind;
                if (!result.TryAdd(kind, contracts[i]))
                    throw new InvalidOperationException($"Timeline Track contract '{kind}' is registered more than once.");
            }
            return result;
        }

        static Dictionary<string, TimelineClipContract> BuildClipIndex(IReadOnlyList<TimelineClipContract> contracts)
        {
            var result = new Dictionary<string, TimelineClipContract>(StringComparer.Ordinal);
            for (int i = 0; i < contracts.Count; i++)
            {
                string kind = contracts[i].Kind;
                if (!result.TryAdd(kind, contracts[i]))
                    throw new InvalidOperationException($"Timeline Clip contract '{kind}' is registered more than once.");
            }
            return result;
        }
    }

    public sealed class TimelineContractCatalog
    {
        readonly TimelineContractCatalogData m_Data;

        public TimelineContractCatalog(IEnumerable<ITimelineContractProvider> providers)
        {
            m_Data = new TimelineContractCatalogData(providers);
        }

        public IReadOnlyList<TimelineTrackContract> Tracks => m_Data.Tracks;
        public IReadOnlyList<TimelineClipContract> Clips => m_Data.Clips;
        public TimelineContentParameterCodecs ParameterCodecs => m_Data.ParameterCodecs;

        public bool TryGetTrack(string kind, out TimelineTrackContract contract) =>
            m_Data.TryGetTrack(kind, out contract);

        public bool TryGetClip(string kind, out TimelineClipContract contract) =>
            m_Data.TryGetClip(kind, out contract);

        public TimelineTrackContract RequireTrack(string kind)
        {
            return TryGetTrack(kind, out TimelineTrackContract contract)
                ? contract
                : throw new InvalidOperationException($"Unknown Timeline Track contract kind '{kind}'.");
        }

        public TimelineClipContract RequireClip(string kind)
        {
            return TryGetClip(kind, out TimelineClipContract contract)
                ? contract
                : throw new InvalidOperationException($"Unknown Timeline Clip contract kind '{kind}'.");
        }

        public void RequireClipPlacement(Track track, Clip clip)
        {
            if (track == null)
                throw new ArgumentNullException(nameof(track));
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            TimelineTrackContract trackContract = RequireTrack(track.ContractKind);
            TimelineClipContract clipContract = RequireClip(clip.ContractKind);
            if (!trackContract.AllowsClip(clipContract.Kind) ||
                !string.Equals(trackContract.Kind, clipContract.TrackKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Timeline Clip '{clip.ContractKind}' cannot be added to Track '{track.ContractKind}'.");
            }
            TimelineExecutionDomain domain = track.ExecutionDomain;
            if (!track.HasExplicitExecutionDomain || !trackContract.SupportsExecutionDomain(domain) ||
                !clipContract.SupportsExecutionDomain(domain))
                throw new InvalidOperationException(
                    $"Timeline Clip '{clip.AuthoringId}' cannot use execution domain '{domain}' on Track '{track.AuthoringId}'.");
        }

        public void Validate(TimelineData timeline, List<string> errors)
        {
            if (timeline == null)
            {
                errors?.Add("Timeline is missing.");
                return;
            }

            if (timeline.Tracks == null)
            {
                errors?.Add($"Timeline '{timeline.Name}' has no track list.");
                return;
            }

            for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
            {
                Track track = timeline.Tracks[trackIndex];
                if (track == null)
                {
                    errors?.Add($"Timeline '{timeline.Name}' track #{trackIndex} is missing.");
                    continue;
                }
                if (track.Clips == null)
                {
                    errors?.Add($"Timeline '{timeline.Name}' track '{track.AuthoringId}' has no clip list.");
                    continue;
                }
                if (!TryGetTrack(track.ContractKind, out TimelineTrackContract trackContract))
                {
                    string clipReferences = string.Join(", ", track.Clips.Where(clip => clip != null)
                        .Select(clip => $"'{clip.AuthoringId}' [{clip.ContractKind}]"));
                    errors?.Add($"Timeline '{timeline.Name}' track '{track.AuthoringId}' has unknown contract kind '{track.ContractKind}'; clips: {clipReferences}.");
                    continue;
                }
                if (!track.HasExplicitExecutionDomain || !trackContract.SupportsExecutionDomain(track.ExecutionDomain))
                {
                    errors?.Add($"Timeline '{timeline.Name}' track '{track.AuthoringId}' uses unsupported execution domain '{track.ExecutionDomain}'.");
                }
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    if (clip == null)
                    {
                        errors?.Add($"Timeline '{timeline.Name}' track #{trackIndex} clip #{clipIndex} is missing.");
                        continue;
                    }
                    if (!TryGetClip(clip.ContractKind, out TimelineClipContract clipContract))
                    {
                        errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' has unknown contract kind '{clip.ContractKind}'.");
                        continue;
                    }
                    if (!trackContract.AllowsClip(clipContract.Kind) || !string.Equals(clipContract.TrackKind, trackContract.Kind, StringComparison.Ordinal))
                        errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' is not allowed on track '{track.AuthoringId}'.");
                    TimelineExecutionDomain executionDomain = track.ExecutionDomain;
                    if (!trackContract.SupportsExecutionDomain(executionDomain) ||
                        !clipContract.SupportsExecutionDomain(executionDomain))
                    {
                        errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' uses unsupported execution domain '{executionDomain}'.");
                    }
                    if (clip.StartTime < OrchestrationTime.Zero || clip.EndTime < clip.StartTime || clipContract.RequiresPositiveDuration && clip.DurationTime <= OrchestrationTime.Zero)
                        errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' has an invalid frame range.");
                    TimelineClipExecutionPhase executionPhase = clipContract.DefaultExecutionPhase;
                    if (clip is ITimelineClipExecutionPhaseSource phaseSource)
                        executionPhase = phaseSource.TimelineExecutionPhase;
                    if (!clipContract.SupportsExecutionPhase(executionPhase))
                        errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' uses unsupported execution phase '{executionPhase}'.");
                    clipContract.Validator?.Invoke(clip, errors);
                    if (trackContract.OverlapPolicy == TimelineTrackOverlapPolicy.Reject)
                        ValidateRejectedOverlap(track, clipIndex, clip, errors);
                    ValidateBindingUses(timeline, clip, errors);
                }
            }

            ValidateBindings(timeline, errors);
            m_Data.ValidateContent(timeline, errors);
        }

        static void ValidateRejectedOverlap(Track track, int clipIndex, Clip clip, List<string> errors)
        {
            for (int otherIndex = clipIndex + 1; otherIndex < track.Clips.Count; otherIndex++)
            {
                Clip other = track.Clips[otherIndex];
                if (other == null || clip.EndTime <= other.StartTime || other.EndTime <= clip.StartTime)
                    continue;
                errors?.Add($"Timeline track '{track.AuthoringId}' has forbidden overlap between clips '{clip.AuthoringId}' and '{other.AuthoringId}'.");
            }
        }

        static void ValidateBindings(TimelineData timeline, List<string> errors)
        {
            if (timeline.ExternalBindings == null)
            {
                errors?.Add($"Timeline '{timeline.Name}' has no external binding list.");
                return;
            }
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < timeline.ExternalBindings.Count; i++)
            {
                TimelineExternalBindingDeclaration binding = timeline.ExternalBindings[i];
                if (binding == null)
                {
                    errors?.Add($"Timeline '{timeline.Name}' external binding #{i} is missing.");
                    continue;
                }
                if (!binding.Validate(out string error))
                    errors?.Add($"Timeline '{timeline.Name}' external binding #{i} is invalid: {error}");
                if (!identities.Add(binding.BindingId))
                    errors?.Add($"Timeline '{timeline.Name}' external binding identity '{binding.BindingId}' is duplicated.");
            }
        }

        static void ValidateBindingUses(TimelineData timeline, Clip clip, List<string> errors)
        {
            if (clip is not ITimelineExternalBindingUseSource source)
                return;
            IReadOnlyList<TimelineExternalBindingUse> uses = source.ExternalBindingUses;
            if (uses == null)
            {
                errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' has no external binding use list.");
                return;
            }
            for (int i = 0; i < uses.Count; i++)
            {
                TimelineExternalBindingUse use = uses[i];
                if (use == null || !timeline.TryGetExternalBinding(use.BindingId, out TimelineExternalBindingDeclaration declaration))
                {
                    errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' references missing external binding '{use?.BindingId}'.");
                    continue;
                }
                if (use.Access == TimelineBindingAccess.Input)
                {
                    if (!string.IsNullOrEmpty(use.TargetBindingId))
                        errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' input binding '{use.BindingId}' cannot target another binding.");
                }
                else if (string.IsNullOrEmpty(use.TargetBindingId))
                {
                    errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' binding '{use.BindingId}' must declare its target binding.");
                }
                else if (!timeline.TryGetExternalBinding(use.TargetBindingId, out TimelineExternalBindingDeclaration target) ||
                         target.ValueKind != TimelineBindingValueKind.Target ||
                         target.Access != TimelineBindingAccess.Input ||
                         target.Lifetime != TimelineBindingLifetime.Call)
                {
                    errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' binding '{use.BindingId}' targets invalid call target '{use.TargetBindingId}'.");
                }
                var signature = new TimelineBindingDeclaration(
                    declaration.BindingId,
                    declaration.Domain,
                    declaration.ParameterId,
                    declaration.ValueKind,
                    declaration.Access,
                    declaration.Lifetime);
                if (!use.Matches(signature))
                    errors?.Add($"Timeline '{timeline.Name}' clip '{clip.AuthoringId}' has an external binding use mismatch for '{use.BindingId}'.");
            }
        }

    }

#if UNITY_EDITOR
    public static class TimelineAuthoringTypeCatalog
    {
        static readonly IReadOnlyDictionary<string, Type> s_TrackTypes = BuildTrackTypes();

        public static Type RequireTrackType(string kind)
        {
            return s_TrackTypes.TryGetValue(kind ?? string.Empty, out Type type)
                ? type
                : throw new InvalidOperationException($"Unknown Timeline Track authoring kind '{kind}'.");
        }

        static IReadOnlyDictionary<string, Type> BuildTrackTypes()
        {
            var result = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Track>()
                         .Where(value => value != null && !value.IsAbstract && !value.ContainsGenericParameters))
            {
                if (Activator.CreateInstance(type) is not Track track || string.IsNullOrWhiteSpace(track.ContractKind))
                    continue;
                if (!result.TryAdd(track.ContractKind, type))
                    throw new InvalidOperationException($"Timeline Track kind '{track.ContractKind}' has multiple formal authoring types.");
            }
            return result;
        }
    }
#endif
}
