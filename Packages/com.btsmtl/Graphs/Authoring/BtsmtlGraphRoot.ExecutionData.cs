using System;
using System.IO;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs
{
    public partial class BtsmtlGraphRoot
    {
        [SerializeField, HideInInspector] ScriptableObject m_ExecutionContentSource;
        [SerializeField, HideInInspector] string m_CompilationInputIdentity = string.Empty;
        [NonSerialized] GraphExecutionIdentity m_PreparedIdentity;

        public GraphExecutionIdentity ExecutionIdentity => m_PreparedIdentity;

        public OrchestrationGraphContent<TValue> RequireExecutionData<TValue>(
            IOrchestrationGraphContentEncoding<TValue> encoding, string numericTarget) where TValue : unmanaged
        {
            if (!m_ExecutionContentSource)
                throw new InvalidOperationException($"图 '{AuthoringId}' 尚未发布执行数据，请显式编译该图。");
            var source = (IOrchestrationGraphContentSource)m_ExecutionContentSource;
            GraphExecutionIdentity expected = source.Identity;
            if (expected.Source.GraphId != AuthoringId)
                throw new InvalidOperationException($"图 '{AuthoringId}' 的发布身份不匹配。");
#if UNITY_EDITOR
            expected.Source.RequireMatches(new BtsmtlGraphFingerprint().CreateIdentity(this, AuthoringId,
                encoding.EncodingContract, numericTarget));
#endif
            OrchestrationGraphContent<TValue> data = source.Load(encoding);
            m_PreparedIdentity = expected;
            return data;
        }

        public void WriteExecutionData<TValue>(Stream destination, Stream expectedIdentity,
            IOrchestrationGraphContentEncoding<TValue> encoding, string numericTarget) where TValue : unmanaged
        {
            OrchestrationGraphContent<TValue> content = RequireExecutionData(encoding, numericTarget);
            byte[] bytes = encoding.Write(content);
            byte[] identity = encoding.WriteIdentity(content.Identity);
            destination.Write(bytes, 0, bytes.Length);
            expectedIdentity.Write(identity, 0, identity.Length);
        }

#if UNITY_EDITOR
        public string CompilationInputIdentity => m_CompilationInputIdentity;

        public void SetExecutionData<TSource>(TSource source, string compilationInputIdentity)
            where TSource : ScriptableObject, IOrchestrationGraphContentSource
        {
            m_ExecutionContentSource = source;
            m_CompilationInputIdentity = compilationInputIdentity;
            m_PreparedIdentity = source.Identity;
        }
#endif
    }
}
