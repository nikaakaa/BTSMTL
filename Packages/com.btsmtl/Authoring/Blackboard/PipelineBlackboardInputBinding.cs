using System;
using UnityEngine;

namespace BTSMTL.Authoring.Blackboard
{
    [Serializable]
    public sealed class PipelineBlackboardInputBinding
    {
        [SerializeField]
        string m_InputValueId;
        [SerializeField] PipelineBlackboardInputProjection m_Projection;

        public string InputValueId => m_InputValueId ?? string.Empty;
        public bool IsDefined => !string.IsNullOrWhiteSpace(m_InputValueId);
        public PipelineBlackboardInputProjection Projection => m_Projection;

        public PipelineBlackboardInputBinding(string inputValueId, PipelineBlackboardInputProjection projection = PipelineBlackboardInputProjection.Direct)
        {
            m_InputValueId = string.IsNullOrWhiteSpace(inputValueId)
                ? throw new ArgumentException("Blackboard Input Binding requires a stable InputValueId.", nameof(inputValueId))
                : inputValueId.Trim();
            m_Projection = projection;
        }
    }
}
