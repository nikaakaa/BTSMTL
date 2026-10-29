using System;
using BTSMTL.Authoring.Graph;
using BTSMTL.Diagnostics;
using BTSMTL.Orchestration;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [Serializable]
    public sealed partial class TimelineMarker
    {
        public const string ContractKind = "marker";

        Track m_Track;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
#if UNITY_EDITOR
        [GraphAuthoringField("time", GraphAuthoringFieldValueKind.Integer, HasMinimum = true, Minimum = 0,
            DocumentMember = GraphAuthoringDocumentMember.Time)]
#endif
        public OrchestrationTime Time => OrchestrationTime.FromRaw(m_TimeFPRawValue);
#if UNITY_EDITOR
        [GraphAuthoringField(nameof(m_ForceTriggerOnInitialSeek), GraphAuthoringFieldValueKind.Boolean)]
#endif
        public bool ForceTriggerOnInitialSeek => m_ForceTriggerOnInitialSeek;
#if UNITY_EDITOR
        [GraphAuthoringField(nameof(m_Graph), GraphAuthoringFieldValueKind.IdentityReference)]
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
        [GraphAuthoringConfigure]
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
