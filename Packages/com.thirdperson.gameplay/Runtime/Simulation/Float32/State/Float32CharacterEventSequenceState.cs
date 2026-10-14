using System;

namespace ThirdPersonSimulation
{
    internal sealed class Float32CharacterEventSequenceState : IFloat32EventSequenceStatePort
    {
        ulong m_EventSequence;
        bool m_Disposed;

        public Float32CharacterEventSequenceState()
        {

        }

        public Float32CharacterEventSequenceState Restart(ulong eventSequence)
        {
            m_EventSequence = eventSequence;
            m_Disposed = false;
            return this;
        }

        public ulong NextEventSequence()
        {
            RequireActive();
            m_EventSequence = checked(m_EventSequence + 1UL);
            if (m_EventSequence == 0)
                throw new OverflowException("Simulation event sequence overflowed.");
            return m_EventSequence;
        }

        internal ulong EventSequence
        {
            get
            {
                RequireActive();
                return m_EventSequence;
            }
        }

        internal void Restore(ulong eventSequence)
        {
            RequireActive();
            m_EventSequence = eventSequence;
        }

        internal void Dispose()
        {
            m_Disposed = true;
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32CharacterEventSequenceState));
        }
    }
}
