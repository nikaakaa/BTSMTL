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
        [NonSerialized] ITimelineAuthoringDocument m_AuthoringDocument;

        public ITimelineAuthoringDocument AuthoringDocument => m_AuthoringDocument;

        public void BindAuthoringDocument(ITimelineAuthoringDocument document)
        {
            m_AuthoringDocument = document;
            foreach (Track track in m_Tracks)
                foreach (Clip clip in track.Clips)
                    if (clip is ITimelineTreeClipAuthoringSource tree && tree.AssetTreeId.Length != 0)
                        ((ITimelineTreeGraphAsset)document.ResolveGraph(tree.AssetTreeId)).BindTimelineAuthoringContext(this, clip);
        }

#endif
        public UnityEngine.Object SerializedOwner => m_SerializedOwner;
        public string SerializedPropertyPath => m_SerializedPropertyPath ?? string.Empty;

        public TimelineData Clone()
        {
            TimelineData clone = ManagedReferenceCloneUtility.Clone(this);
            clone.m_SerializedOwner = null;
            clone.m_SerializedPropertyPath = string.Empty;
#if UNITY_EDITOR
            clone.m_AuthoringDocument = m_AuthoringDocument;
#endif
            return clone;
        }

#if UNITY_EDITOR
        public TimelineData CloneForAuthoring()
        {
            TimelineData clone = Clone();
            clone.RegenerateAuthoringIdentities();
            return clone;
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

        public void BindSerializedOwner(UnityEngine.Object owner, string propertyPath)
        {
#if UNITY_EDITOR
            if (!ReferenceEquals(m_SerializedOwner, owner))
                m_AuthoringDocument = null;
#endif
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

    public interface ITimelineNestedSerializedOwner
    {
        void BindNestedSerializedOwner(UnityEngine.Object owner, string propertyPath);
    }
}

