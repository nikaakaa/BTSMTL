using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Diagnostics;
using BTSMTL.Authoring.Graph;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [Serializable]
    public sealed partial class TimelineData
    {
        [NonSerialized]
        UnityEngine.Object m_SerializedOwner;

        [NonSerialized]
        string m_SerializedPropertyPath;

#if UNITY_EDITOR
        [GraphAuthoringField(nameof(m_Name), GraphAuthoringFieldValueKind.String,
            DocumentMember = GraphAuthoringDocumentMember.DisplayName)]
#endif
        public string Name { get => string.IsNullOrEmpty(m_Name) ? "Timeline" : m_Name; set => m_Name = value; }
        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public List<Track> Tracks => m_Tracks;
        public IReadOnlyList<TimelineExternalBindingDeclaration> ExternalBindings => m_ExternalBindings;
#if UNITY_EDITOR
        [GraphAuthoringField(nameof(m_Loop), GraphAuthoringFieldValueKind.Boolean,
            DocumentMember = GraphAuthoringDocumentMember.Loop)]
#endif
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

        public bool ValidateContent(TimelineContractCatalog catalog, List<string> errors)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            bool valid = ValidateAuthoringIdentities(errors);
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

