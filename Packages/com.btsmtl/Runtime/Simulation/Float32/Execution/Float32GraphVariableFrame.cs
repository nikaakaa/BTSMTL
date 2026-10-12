using System;
using BTSMTL.EventGraphs;

namespace ThirdPersonSimulation
{
    internal sealed class Float32GraphVariableBuffer
    {
        internal sealed class Page
        {
            readonly AbilityStateValue[] m_Values;
            ulong m_Version;
            int m_References;

            internal Page(int count) => m_Values = new AbilityStateValue[count];
            internal bool IsAvailable => m_References == 0;
            internal ulong Version => m_Version;
            internal bool IsCurrent(ulong version) => m_References != 0 && version == m_Version;
            internal void Acquire()
            {
                m_Version = checked(m_Version + 1);
                m_References = 1;
            }
            internal void Retain() => m_References = checked(m_References + 1);
            internal void Write(int index, in AbilityStateValue value)
            {
                value.Retain();
                m_Values[index] = value;
            }
            internal AbilityStateValue Read(int index) => m_Values[index];
            internal void Release()
            {
                m_References--;
                if (m_References != 0)
                    return;
                for (int i = 0; i < m_Values.Length; i++)
                {
                    m_Values[i].Release();
                    m_Values[i] = default;
                }
            }
        }

        readonly Page[] m_Pages;
        Page m_Published;
        Page m_Writing;

        internal Float32GraphVariableBuffer(int count)
        {
            m_Pages = new[] { new Page(count), new Page(count) };
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

        internal void Write(int index, in AbilityStateValue value) => m_Writing.Write(index, in value);
        internal void Invalidate()
        {
            m_Writing?.Release();
            m_Writing = null;
            m_Published?.Release();
            m_Published = null;
        }

        internal Float32GraphVariableFrame Publish(EventGraphInvocationIdentity invocation, ulong resetGeneration)
        {
            m_Published?.Release();
            m_Published = m_Writing;
            m_Writing = null;
            return new Float32GraphVariableFrame(invocation, resetGeneration, m_Published, m_Published.Version);
        }
    }

    public readonly struct Float32GraphVariableFrame
    {
        readonly Float32GraphVariableBuffer.Page m_Page;
        readonly ulong m_Version;

        internal Float32GraphVariableFrame(EventGraphInvocationIdentity invocation, ulong resetGeneration,
            Float32GraphVariableBuffer.Page page, ulong version)
        {
            Invocation = invocation;
            ResetGeneration = resetGeneration;
            m_Page = page;
            m_Version = version;
        }

        public EventGraphInvocationIdentity Invocation { get; }
        public ulong ResetGeneration { get; }
        public bool IsValid => m_Page != null && m_Page.IsCurrent(m_Version);
        public void Retain()
        {
            m_Page?.Retain();
            Invocation.Retain();
        }
        public void Release()
        {
            Invocation.Release();
            m_Page?.Release();
        }
        public AbilityStateValue Read(int index) => m_Page.Read(index);
    }
}
