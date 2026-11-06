using System;
using BTSMTL.Diagnostics;

namespace BTSMTL.Timeline
{
    [Serializable]
    public sealed partial class TimelineData
    {
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [NonSerialized] ITimelineAuthoringDocument m_AuthoringDocument;

        public ITimelineAuthoringDocument AuthoringDocument => m_AuthoringDocument;

        public void BindAuthoringDocument(ITimelineAuthoringDocument document)
        {
            m_AuthoringDocument = document;
#if UNITY_EDITOR
            foreach (Track track in m_Tracks)
                foreach (Clip clip in track.Clips)
                    if (clip is ITimelineTreeClipAuthoringSource tree && tree.AssetTreeId.Length != 0)
                        ((ITimelineTreeGraphAsset)document.ResolveGraph(tree.AssetTreeId)).BindTimelineAuthoringContext(this, clip);
#endif
        }
#endif

        public TimelineData Clone()
        {
            TimelineData clone = ManagedReferenceCloneUtility.Clone(this);
#if UNITY_EDITOR || BTSMTL_AUTHORING
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
            foreach (Track track in m_Tracks)
            {
                changed |= track.EnsureAuthoringIdentity();
                if (track is ITimelineTrackOwnedAuthoringIdentity identityOwner)
                    changed |= identityOwner.EnsureOwnedAuthoringIdentities();
                foreach (Clip clip in track.Clips)
                    changed |= clip.EnsureAuthoringIdentity();
                foreach (TimelineMarker marker in track.Markers)
                    changed |= marker.EnsureAuthoringIdentity();
            }
            foreach (TimelineExternalBindingDeclaration binding in m_ExternalBindings)
                changed |= binding.EnsureAuthoringIdentity();
            return changed;
        }

        public void RegenerateAuthoringIdentities()
        {
            m_AuthoringId = AuthoringIdentity.Create();
            foreach (Track track in m_Tracks)
            {
                track.RegenerateAuthoringIdentity();
                if (track is ITimelineTrackOwnedAuthoringIdentity identityOwner)
                    identityOwner.RegenerateOwnedAuthoringIdentities();
                foreach (Clip clip in track.Clips)
                {
                    clip.RegenerateAuthoringIdentity();
                    if (clip is ITimelineOwnedAuthoringIdentity owner)
                        owner.RegenerateOwnedAuthoringIdentity();
                }
                foreach (TimelineMarker marker in track.Markers)
                    marker.RegenerateAuthoringIdentity();
            }
            foreach (TimelineExternalBindingDeclaration binding in m_ExternalBindings)
                binding.RegenerateAuthoringIdentity();
        }
#endif
    }
}

