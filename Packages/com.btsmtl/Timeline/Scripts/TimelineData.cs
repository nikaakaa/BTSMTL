using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Diagnostics;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [Serializable]
    public sealed partial class TimelineData
    {
        [SerializeField, HideInInspector]
        string m_AuthoringId;

        [SerializeField]
        string m_Name = "Timeline";

        [SerializeReference]
        List<Track> m_Tracks = new List<Track>();

        [SerializeField]
        List<TimelineSection> m_Sections = new List<TimelineSection>();

        [SerializeField]
        List<TimelineExternalBindingDeclaration> m_ExternalBindings = new List<TimelineExternalBindingDeclaration>();

        [SerializeField]
        bool m_Loop;

        [NonSerialized]
        UnityEngine.Object m_SerializedOwner;

        [NonSerialized]
        string m_SerializedPropertyPath;

        public string Name { get => string.IsNullOrEmpty(m_Name) ? "Timeline" : m_Name; set => m_Name = value; }
        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public List<Track> Tracks => m_Tracks;
        public IReadOnlyList<TimelineSection> Sections => m_Sections;
        public IReadOnlyList<TimelineExternalBindingDeclaration> ExternalBindings => m_ExternalBindings;
        public bool Loop { get => m_Loop; set => m_Loop = value; }
        public UnityEngine.Object SerializedOwner => m_SerializedOwner;
        public string SerializedPropertyPath => m_SerializedPropertyPath ?? string.Empty;

        public static TimelineData CreateDefault(string name)
        {
            return new TimelineData
            {
                m_AuthoringId = AuthoringIdentity.Create(),
                Name = string.IsNullOrEmpty(name) ? "Timeline" : name
            };
        }

        public TimelineData Clone()
        {
            TimelineData clone = ManagedReferenceCloneUtility.Clone(this);
            clone.m_SerializedOwner = null;
            clone.m_SerializedPropertyPath = string.Empty;
            return clone;
        }

#if UNITY_EDITOR
        public TimelineData CloneForAuthoring()
        {
            TimelineData clone = Clone();
            clone.RegenerateAuthoringIdentities();
            return clone;
        }

        public void ConfigureAuthoringIdentity(string authoringId)
        {
            if (!AuthoringIdentity.IsValid(authoringId))
                throw new ArgumentException("Timeline authoring identity is invalid.", nameof(authoringId));
            m_AuthoringId = authoringId;
        }

        public bool EnsureAuthoringIdentities()
        {
            bool changed = false;
            if (!AuthoringIdentity.IsValid(m_AuthoringId))
            {
                m_AuthoringId = AuthoringIdentity.Create();
                changed = true;
            }
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                if (track == null)
                    continue;
                changed |= track.EnsureAuthoringIdentity();
                if (track is ITimelineTrackOwnedAuthoringIdentity identityOwner)
                    changed |= identityOwner.EnsureOwnedAuthoringIdentities();
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    changed |= clip?.EnsureAuthoringIdentity() ?? false;
                }
                for (int markerIndex = 0; markerIndex < track.Markers.Count; markerIndex++)
                    changed |= track.Markers[markerIndex]?.EnsureAuthoringIdentity() ?? false;
            }
            for (int i = 0; i < m_Sections.Count; i++)
                changed |= m_Sections[i]?.EnsureAuthoringIdentity() ?? false;
            for (int i = 0; i < m_ExternalBindings.Count; i++)
                changed |= m_ExternalBindings[i]?.EnsureAuthoringIdentity() ?? false;
            return changed;
        }

        public void RegenerateAuthoringIdentities()
        {
            m_AuthoringId = AuthoringIdentity.Create();
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                if (track == null)
                    continue;
                track.RegenerateAuthoringIdentity();
                if (track is ITimelineTrackOwnedAuthoringIdentity identityOwner)
                    identityOwner.RegenerateOwnedAuthoringIdentities();
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    clip?.RegenerateAuthoringIdentity();
                    if (clip is ITimelineOwnedAuthoringIdentity owner)
                        owner.RegenerateOwnedAuthoringIdentity();
                }
                for (int markerIndex = 0; markerIndex < track.Markers.Count; markerIndex++)
                    track.Markers[markerIndex]?.RegenerateAuthoringIdentity();
            }
            for (int i = 0; i < m_Sections.Count; i++)
                m_Sections[i]?.RegenerateAuthoringIdentity();
            for (int i = 0; i < m_ExternalBindings.Count; i++)
                m_ExternalBindings[i]?.RegenerateAuthoringIdentity();
        }
#endif

        public bool ValidateAuthoringIdentities(List<string> errors)
        {
            bool valid = true;
            if (!AuthoringIdentity.IsValid(m_AuthoringId))
            {
                errors?.Add($"Timeline '{Name}' has an invalid authoring identity.");
                valid = false;
            }
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var sectionNames = new HashSet<string>(StringComparer.Ordinal);
            if (m_Tracks == null)
            {
                errors?.Add($"Timeline '{Name}' has no track list.");
                valid = false;
            }
            else
            {
                for (int i = 0; i < m_Tracks.Count; i++)
                {
                    Track track = m_Tracks[i];
                    if (track == null || !AuthoringIdentity.IsValid(track.AuthoringId) || !identities.Add(track.AuthoringId))
                    {
                        errors?.Add($"Timeline '{Name}' track #{i} has a missing or duplicate authoring identity.");
                        valid = false;
                        continue;
                    }
                    if (track.Clips == null)
                    {
                        errors?.Add($"Timeline '{Name}' track '{track.AuthoringId}' has no clip list.");
                        valid = false;
                        continue;
                    }
                    for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                    {
                        Clip clip = track.Clips[clipIndex];
                        if (clip == null || !AuthoringIdentity.IsValid(clip.AuthoringId) || !identities.Add(clip.AuthoringId))
                        {
                            errors?.Add($"Timeline '{Name}' clip #{i}:{clipIndex} has a missing or duplicate authoring identity.");
                            valid = false;
                        }
                    }
                    for (int markerIndex = 0; markerIndex < track.Markers.Count; markerIndex++)
                    {
                        TimelineMarker marker = track.Markers[markerIndex];
                        if (marker == null || !AuthoringIdentity.IsValid(marker.AuthoringId) ||
                            !identities.Add(marker.AuthoringId))
                        {
                            errors?.Add($"Timeline '{Name}' track '{track.AuthoringId}' marker #{markerIndex} has a missing or duplicate authoring identity.");
                            valid = false;
                        }
                    }
                }
            }
            if (m_Sections == null)
            {
                errors?.Add($"Timeline '{Name}' has no section list.");
                valid = false;
            }
            else for (int i = 0; i < m_Sections.Count; i++)
            {
                TimelineSection section = m_Sections[i];
                if (section == null || !section.RequireValid() || !identities.Add(section.AuthoringId) ||
                    !sectionNames.Add(section.Name))
                {
                    errors?.Add($"Timeline '{Name}' section #{i} is invalid or duplicated.");
                    valid = false;
                }
                else if (!string.IsNullOrEmpty(section.NextSectionId) &&
                         !m_Sections.Any(candidate => candidate != null && candidate.AuthoringId == section.NextSectionId))
                {
                    errors?.Add($"Timeline '{Name}' section '{section.Name}' references unknown next Section '{section.NextSectionId}'.");
                    valid = false;
                }
            }
            if (m_ExternalBindings == null)
            {
                errors?.Add($"Timeline '{Name}' has no external binding list.");
                valid = false;
            }
            else for (int i = 0; i < m_ExternalBindings.Count; i++)
            {
                TimelineExternalBindingDeclaration binding = m_ExternalBindings[i];
                if (binding == null || !AuthoringIdentity.IsValid(binding.AuthoringId) || !identities.Add(binding.AuthoringId))
                {
                    errors?.Add($"Timeline '{Name}' external binding #{i} has a missing or duplicate authoring identity.");
                    valid = false;
                }
            }
            return valid;
        }

        public bool TryGetSectionRange(string sectionId, OrchestrationTime terminalTime,
            out OrchestrationTime start, out OrchestrationTime end)
        {
            start = end = OrchestrationTime.Zero;
            if (string.IsNullOrEmpty(sectionId))
                return false;
            TimelineSection owner = null;
            for (int i = 0; i < m_Sections.Count; i++)
                if (m_Sections[i] != null && m_Sections[i].AuthoringId == sectionId)
                    owner = m_Sections[i];
            if (owner == null)
                return false;
            start = owner.Time;
            end = OrchestrationTime.Max(terminalTime, start);
            for (int i = 0; i < m_Sections.Count; i++)
                if (m_Sections[i] != null && m_Sections[i].Time > start && m_Sections[i].Time < end)
                    end = m_Sections[i].Time;
            return true;
        }

        public bool ValidateSectionOwnership(List<string> errors)
        {
            OrchestrationTime terminal = OrchestrationTime.Zero;
            foreach (Track track in m_Tracks)
                if (track != null)
                {
                    foreach (Clip clip in track.Clips)
                        if (clip != null)
                            terminal = OrchestrationTime.Max(terminal, clip.EndTime);
                    foreach (TimelineMarker marker in track.Markers)
                        if (marker != null)
                            terminal = OrchestrationTime.Max(terminal, marker.Time);
                }
            bool valid = true;
            foreach (Track track in m_Tracks)
            {
                if (track == null || string.IsNullOrEmpty(track.SectionId))
                    continue;
                if (!TryGetSectionRange(track.SectionId, terminal, out OrchestrationTime start, out OrchestrationTime end))
                {
                    errors?.Add($"Timeline '{Name}' 轨道 '{track.Name}' 指向不存在的分段 '{track.SectionId}'。");
                    valid = false;
                    continue;
                }
                foreach (Clip clip in track.Clips)
                    if (clip != null && (clip.StartTime < start || clip.EndTime > end))
                    {
                        errors?.Add($"Timeline '{Name}' 轨道 '{track.Name}' 的片段 '{clip.AuthoringId}' " +
                            $"({clip.StartTime.AsSingle:0.###}–{clip.EndTime.AsSingle:0.###}s) 超出所属分段 " +
                            $"{start.AsSingle:0.###}–{end.AsSingle:0.###}s。");
                        valid = false;
                    }
                foreach (TimelineMarker marker in track.Markers)
                    if (marker != null && (marker.Time < start || marker.Time > end))
                    {
                        errors?.Add($"Timeline '{Name}' 轨道 '{track.Name}' 的触发点 '{marker.AuthoringId}' 超出所属分段。");
                        valid = false;
                    }
            }
            return valid;
        }

        public bool ValidateContent(TimelineContractCatalog catalog, List<string> errors)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            bool valid = ValidateAuthoringIdentities(errors);
            valid &= ValidateSectionOwnership(errors);
            int count = errors?.Count ?? 0;
            catalog.Validate(this, errors);
            return valid && (errors == null || errors.Count == count);
        }

        public bool TryGetExternalBinding(string bindingId, out TimelineExternalBindingDeclaration binding)
        {
            for (int i = 0; i < (m_ExternalBindings?.Count ?? 0); i++)
            {
                TimelineExternalBindingDeclaration candidate = m_ExternalBindings[i];
                if (candidate != null && string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal))
                {
                    binding = candidate;
                    return true;
                }
            }
            binding = null;
            return false;
        }

        public void BindSerializedOwner(UnityEngine.Object owner, string propertyPath)
        {
            m_SerializedOwner = owner;
            m_SerializedPropertyPath = propertyPath ?? string.Empty;
#if UNITY_EDITOR
            UpdateSerializedTimeline();
#endif
            for (int trackIndex = 0; trackIndex < m_Tracks.Count; trackIndex++)
            {
                Track track = m_Tracks[trackIndex];
                if (track == null)
                    continue;
                track.BindTimeline(this);
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    clip.BindTrack(track);
                    if (clip is ITimelineNestedSerializedOwner nestedOwner)
                    {
                        nestedOwner.BindNestedSerializedOwner(
                            owner,
                            GetSerializedPropertyPath(
                                $"m_Tracks.Array.data[{trackIndex}].m_Clips.Array.data[{clipIndex}]"));
                    }
                }
                for (int markerIndex = 0; markerIndex < track.Markers.Count; markerIndex++)
                    track.Markers[markerIndex].BindTrack(track);
            }
        }

        public bool TryGetSerializedPropertyPath(object element, out string propertyPath)
        {
            for (int trackIndex = 0; trackIndex < m_Tracks.Count; trackIndex++)
            {
                Track track = m_Tracks[trackIndex];
                if (ReferenceEquals(track, element))
                {
                    propertyPath = GetSerializedPropertyPath($"m_Tracks.Array.data[{trackIndex}]");
                    return true;
                }
                if (track == null)
                    continue;
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                    if (ReferenceEquals(track.Clips[clipIndex], element))
                    {
                        propertyPath = GetSerializedPropertyPath(
                            $"m_Tracks.Array.data[{trackIndex}].m_Clips.Array.data[{clipIndex}]");
                        return true;
                    }
                for (int markerIndex = 0; markerIndex < track.Markers.Count; markerIndex++)
                    if (ReferenceEquals(track.Markers[markerIndex], element))
                    {
                        propertyPath = GetSerializedPropertyPath(
                            $"m_Tracks.Array.data[{trackIndex}].m_Markers.Array.data[{markerIndex}]");
                        return true;
                    }
            }
            for (int sectionIndex = 0; sectionIndex < m_Sections.Count; sectionIndex++)
                if (ReferenceEquals(m_Sections[sectionIndex], element))
                {
                    propertyPath = GetSerializedPropertyPath($"m_Sections.Array.data[{sectionIndex}]");
                    return true;
                }
            propertyPath = string.Empty;
            return false;
        }

        public string GetSerializedPropertyPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return SerializedPropertyPath;
            return string.IsNullOrEmpty(SerializedPropertyPath) ? relativePath : $"{SerializedPropertyPath}.{relativePath}";
        }

    }

    [Serializable]
    public sealed class TimelineSection
    {
        [SerializeField, HideInInspector]
        string m_AuthoringId;

        [SerializeField]
        string m_Name;

        [SerializeField]
        long m_TimeFPRawValue;

        [SerializeField]
        string m_NextSectionId;

        [SerializeField]
        string m_BranchId;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public string Name => m_Name ?? string.Empty;
        public OrchestrationTime Time => OrchestrationTime.FromRaw(m_TimeFPRawValue);
        public string NextSectionId => m_NextSectionId ?? string.Empty;
        public string BranchId => m_BranchId?.Trim() ?? string.Empty;

#if UNITY_EDITOR
        public void ConfigureAuthoringIdentity(string authoringId)
        {
            if (!AuthoringIdentity.IsValid(authoringId))
                throw new ArgumentException("Timeline section authoring identity is invalid.", nameof(authoringId));
            m_AuthoringId = authoringId;
        }

        public void Configure(string name, OrchestrationTime time)
        {
            string value = name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(value) || time < OrchestrationTime.Zero)
                throw new ArgumentException("Timeline Section requires a name and non-negative time.");
            m_Name = value;
            m_TimeFPRawValue = time.RawValue;
        }

        public void SetNextSection(string authoringId)
        {
            m_NextSectionId = authoringId?.Trim() ?? string.Empty;
        }

        public void ConfigureBranch(string branchId)
        {
            m_BranchId = branchId?.Trim() ?? string.Empty;
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

        public bool RequireValid() =>
            AuthoringIdentity.IsValid(AuthoringId) &&
            !string.IsNullOrWhiteSpace(Name) &&
            string.Equals(Name, Name.Trim(), StringComparison.Ordinal) &&
            m_TimeFPRawValue >= 0;
    }

    public interface ITimelineOwnedAuthoringIdentity
    {
#if UNITY_EDITOR
        void RegenerateOwnedAuthoringIdentity();
#endif
    }

    public interface ITimelineTrackOwnedAuthoringIdentity
    {
#if UNITY_EDITOR
        bool EnsureOwnedAuthoringIdentities();
        void RegenerateOwnedAuthoringIdentities();
#endif
    }

    public interface ITimelineNestedSerializedOwner
    {
        void BindNestedSerializedOwner(UnityEngine.Object owner, string propertyPath);
    }
}

