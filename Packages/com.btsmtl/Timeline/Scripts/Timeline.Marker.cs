using System;
using BTSMTL.Diagnostics;
using BTSMTL.Orchestration;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [Serializable]
    public sealed class TimelineMarker
    {
        [SerializeField, HideInInspector]
        string m_AuthoringId;

        [SerializeField]
        long m_TimeFPRawValue;

        [SerializeField]
        bool m_ForceTriggerOnInitialSeek;

#if UNITY_EDITOR
        [SerializeField]
        ScriptableObject m_Graph;
#endif

        Track m_Track;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public OrchestrationTime Time => OrchestrationTime.FromRaw(m_TimeFPRawValue);
        public bool ForceTriggerOnInitialSeek => m_ForceTriggerOnInitialSeek;
#if UNITY_EDITOR
        public ScriptableObject Graph => m_Graph;
#endif
        public Track Track => m_Track;
        public TimelineExecutionDomain ExecutionDomain =>
            m_Track != null ? m_Track.ExecutionDomain : throw new InvalidOperationException("Timeline Marker has no owning Track.");

        internal void BindTrack(Track track) => m_Track = track;

        public void Init(Track track)
        {
            BindTrack(track);
            if (m_TimeFPRawValue < 0)
                throw new InvalidOperationException($"Timeline Marker '{AuthoringId}' time is invalid.");
        }

#if UNITY_EDITOR
        public void Configure(OrchestrationTime time, ScriptableObject graph, bool forceTriggerOnInitialSeek)
        {
            if (time < OrchestrationTime.Zero)
                throw new ArgumentOutOfRangeException(nameof(time));
            if (graph is not ITimelineTreeGraphAsset markerGraph || !markerGraph.IsTimelineTrigger)
                throw new ArgumentException("Timeline Marker需要正式的Timeline触发图资产。", nameof(graph));
            m_TimeFPRawValue = time.RawValue;
            m_Graph = graph;
            m_ForceTriggerOnInitialSeek = forceTriggerOnInitialSeek;
        }

        public void ConfigureAuthoringIdentity(string authoringId)
        {
            if (!AuthoringIdentity.IsValid(authoringId))
                throw new ArgumentException("Timeline Marker authoring identity is invalid.", nameof(authoringId));
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
    }
}
