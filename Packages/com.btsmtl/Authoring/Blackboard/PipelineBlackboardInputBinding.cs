using System;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Blackboard
{
    [Serializable]
    public sealed partial class PipelineBlackboardInputBinding
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_InputValueId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        PipelineBlackboardInputProjection m_Projection;

        [GraphAuthoringField(nameof(m_InputValueId), GraphAuthoringFieldValueKind.IdentityReference, ReadOnly = true)]
        public string InputValueId => m_InputValueId ?? string.Empty;
        public bool IsDefined => !string.IsNullOrWhiteSpace(m_InputValueId);
        [GraphAuthoringField(nameof(m_Projection), typeof(PipelineBlackboardInputProjection), ReadOnly = true)]
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
