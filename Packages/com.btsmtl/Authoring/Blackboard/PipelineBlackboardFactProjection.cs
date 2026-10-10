using System;
using UnityEngine;

namespace BTSMTL.Authoring.Blackboard
{
    [Serializable]
    public sealed class PipelineBlackboardFactProjection
    {
        [SerializeField]
        PipelineBlackboardFactProjectionKind m_Kind;

        [SerializeField]
        string m_ActionWindowType;

        [SerializeField]
        string m_ActionWindowTag;

        [SerializeField]
        string m_ActionWindowId;

        [SerializeField]
        ulong m_ActionWindowDigest;

        public PipelineBlackboardFactProjectionKind Kind => m_Kind;
        public string ActionWindowType => m_ActionWindowType ?? string.Empty;
        public string ActionWindowTag => m_ActionWindowTag ?? string.Empty;
        public string ActionWindowId => m_ActionWindowId ?? string.Empty;
        public ulong ActionWindowDigest => m_ActionWindowDigest;
        public bool IsDefined =>
            !string.IsNullOrWhiteSpace(m_ActionWindowType) ||
            !string.IsNullOrWhiteSpace(m_ActionWindowTag) ||
            !string.IsNullOrWhiteSpace(m_ActionWindowId) ||
            m_ActionWindowDigest != 0;

        public PipelineBlackboardFactProjection(
            PipelineBlackboardFactProjectionKind kind,
            string actionWindowType,
            string actionWindowId,
            ulong actionWindowDigest,
            string actionWindowTag)
        {
            m_Kind = kind;
            m_ActionWindowType = actionWindowType ?? string.Empty;
            m_ActionWindowId = actionWindowId ?? string.Empty;
            m_ActionWindowDigest = actionWindowDigest;
            m_ActionWindowTag = actionWindowTag;
        }
    }
}
