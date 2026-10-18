using System;
using BTSMTL.Orchestration;
namespace BTSMTL.Graphs
{
    internal sealed class GraphVariableBuffer
    {
        internal sealed class Page
        {
            readonly GraphHostValue[] m_Values;
            readonly OrchestrationIdentityTable m_Identities;
            ulong m_Version;
            int m_References;

            internal Page(int count, OrchestrationIdentityTable identities)
            {
                m_Values = new GraphHostValue[count];
                m_Identities = identities;
            }
            internal string SourceText(int sourceIdentityId) => m_Identities.Resolve(sourceIdentityId);
            internal bool IsAvailable => m_References == 0;
            internal ulong Version => m_Version;
            internal bool IsCurrent(ulong version) => m_References != 0 && version == m_Version;
            internal void Acquire()
            {
                m_Version = checked(m_Version + 1);
                m_References = 1;
            }
            internal void Retain() => m_References = checked(m_References + 1);
            internal void Write(int index, in GraphHostValue value)
            {
                m_Values[index] = value;
            }
            internal GraphHostValue Read(int index) => m_Values[index];
            internal void Release()
            {
                m_References--;
                if (m_References != 0)
                    return;
                for (int i = 0; i < m_Values.Length; i++)
                {
                    m_Values[i] = default;
                }
            }
        }

        readonly Page[] m_Pages;
        Page m_Published;
        Page m_Writing;

        internal GraphVariableBuffer(int count, OrchestrationIdentityTable identities)
        {
            m_Pages = new[] { new Page(count, identities), new Page(count, identities) };
        }

        internal void BeginWrite()
        {
            for (int i = 0; i < m_Pages.Length; i++)
            {
                if (!m_Pages[i].IsAvailable)
                    continue;
                m_Writing = m_Pages[i];
                m_Writing.Acquire();
                return;
            }
            throw new InvalidOperationException("Event graph frame ownership exceeds the prepared old/new publication boundary.");
        }

        internal void Write(int index, in GraphHostValue value) => m_Writing.Write(index, in value);
        internal void Invalidate()
        {
            m_Writing?.Release();
            m_Writing = null;
            m_Published?.Release();
            m_Published = null;
        }

        internal GraphHostValueFrame Publish(GraphInvocationIdentity invocation, ulong resetGeneration)
        {
            m_Published?.Release();
            m_Published = m_Writing;
            m_Writing = null;
            return new GraphHostValueFrame(invocation, resetGeneration, m_Published, m_Published.Version);
        }
    }

    internal readonly struct GraphHostValueFrame
    {
        readonly GraphVariableBuffer.Page m_Page;
        readonly ulong m_Version;

        internal GraphHostValueFrame(GraphInvocationIdentity invocation, ulong resetGeneration,
            GraphVariableBuffer.Page page, ulong version)
        {
            Invocation = invocation;
            ResetGeneration = resetGeneration;
            m_Page = page;
            m_Version = version;
        }

        public GraphInvocationIdentity Invocation { get; }
        public ulong ResetGeneration { get; }
        public bool IsValid => m_Page != null && m_Page.IsCurrent(m_Version);
        internal string SourceText => Invocation.SourceIdentityId == 0 ? string.Empty : m_Page.SourceText(Invocation.SourceIdentityId);
        public void Retain() => m_Page?.Retain();
        public void Release() => m_Page?.Release();
        public GraphHostValue Read(int index) => m_Page.Read(index);
        public override string ToString() => $"{SourceText}:{Invocation.Sequence}";
    }
}
