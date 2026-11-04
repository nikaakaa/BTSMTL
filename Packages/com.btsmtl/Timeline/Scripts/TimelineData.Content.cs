using System;
using System.Collections.Generic;
using BTSMTL.Diagnostics;

namespace BTSMTL.Timeline
{
    public sealed partial class TimelineData
    {
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField(nameof(m_Name), BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.String,
            DocumentMember = BTSMTL.Authoring.Graph.GraphAuthoringDocumentMember.DisplayName)]
#endif
        public string Name { get => string.IsNullOrEmpty(m_Name) ? "Timeline" : m_Name; set => m_Name = value; }
        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public List<Track> Tracks => m_Tracks;
        public IReadOnlyList<TimelineExternalBindingDeclaration> ExternalBindings => m_ExternalBindings;
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField(nameof(m_Loop), BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Boolean,
            DocumentMember = BTSMTL.Authoring.Graph.GraphAuthoringDocumentMember.Loop)]
#endif
        public bool Loop { get => m_Loop; set => m_Loop = value; }

        [field: NonSerialized]
        internal string ContentFingerprint { get; private set; }

        public void BindContentFingerprint(string fingerprint) => ContentFingerprint = fingerprint;

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
    }

    public abstract partial class Clip
    {
        [field: NonSerialized]
        protected TimelineContentParameters ContentResourceParameters { get; private set; }
        [field: NonSerialized]
        protected TimelineContentDependency ContentResourceDependency { get; private set; }

        public void BindContentResource(TimelineContentParameters parameters, TimelineContentDependency dependency)
        {
            ContentResourceParameters = parameters;
            ContentResourceDependency = dependency;
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
}
