using System;
using BTSMTL.Orchestration;

namespace BTSMTL.Graphs
{
    [Serializable]
    public sealed class BtsmtlGraphEntryDeclaration
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_EntryId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_NodeId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        OrchestrationExecutionContract m_Contract;

        public BtsmtlGraphEntryDeclaration(string entryId, string nodeId, OrchestrationExecutionContract contract)
        {
            m_EntryId = entryId;
            m_NodeId = nodeId;
            m_Contract = contract;
        }

        public string EntryId => m_EntryId;
        public string NodeId => m_NodeId;
        public OrchestrationExecutionContract Contract => m_Contract;
    }
}
