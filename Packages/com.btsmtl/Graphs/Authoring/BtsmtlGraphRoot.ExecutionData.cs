using System;
using System.IO;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs
{
    public partial class BtsmtlGraphRoot
    {
        [SerializeField, HideInInspector] byte[] m_ExecutionData = Array.Empty<byte>();
        [SerializeField, HideInInspector] byte[] m_ExecutionIdentity = Array.Empty<byte>();
        [SerializeField, HideInInspector] string m_CompilationInputIdentity = string.Empty;
        [NonSerialized] GraphExecutionIdentity m_PreparedIdentity;

        public GraphExecutionIdentity ExecutionIdentity => m_PreparedIdentity;

        public OrchestrationGraphContent<TValue> RequireExecutionData<TValue>(
            IOrchestrationGraphContentEncoding<TValue> encoding, string numericTarget) where TValue : unmanaged
        {
            if (m_ExecutionData.Length == 0 || m_ExecutionIdentity.Length == 0)
                throw new InvalidOperationException($"图 '{AuthoringId}' 尚未发布执行数据，请显式编译该图。");
#if UNITY_EDITOR
            GraphExecutionIdentity expected = new BtsmtlGraphFingerprint().CreateIdentity(this, AuthoringId,
                encoding.EncodingContract, numericTarget);
#else
            GraphExecutionIdentity expected = encoding.ReadIdentity(m_ExecutionIdentity);
            if (expected.GraphId != AuthoringId)
                throw new InvalidOperationException($"图 '{AuthoringId}' 的发布身份不匹配。");
#endif
            OrchestrationGraphContent<TValue> data = encoding.Read(m_ExecutionData, expected);
            m_PreparedIdentity = expected;
            return data;
        }

        public void WriteExecutionData<TValue>(Stream destination, Stream expectedIdentity,
            IOrchestrationGraphContentEncoding<TValue> encoding, string numericTarget) where TValue : unmanaged
        {
            RequireExecutionData(encoding, numericTarget);
            destination.Write(m_ExecutionData, 0, m_ExecutionData.Length);
            expectedIdentity.Write(m_ExecutionIdentity, 0, m_ExecutionIdentity.Length);
        }

#if UNITY_EDITOR
        public string CompilationInputIdentity => m_CompilationInputIdentity;

        public void SetExecutionData<TValue>(byte[] bytes, GraphExecutionIdentity identity,
            IOrchestrationGraphContentEncoding<TValue> encoding, string compilationInputIdentity) where TValue : unmanaged
        {
            m_ExecutionData = bytes;
            m_ExecutionIdentity = encoding.WriteIdentity(identity);
            m_CompilationInputIdentity = compilationInputIdentity;
            m_PreparedIdentity = identity;
        }
#endif
    }
}
