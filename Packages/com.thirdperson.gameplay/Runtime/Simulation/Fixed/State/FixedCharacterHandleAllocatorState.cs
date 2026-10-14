using System;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedCharacterHandleAllocatorState : IFixedHandleAllocatorStatePort
    {
        ulong m_HandleAllocator;
        bool m_Disposed;

        public FixedCharacterHandleAllocatorState()
        {

        }

        public FixedCharacterHandleAllocatorState Restart(ulong handleAllocator)
        {
            m_HandleAllocator = handleAllocator;
            m_Disposed = false;
            return this;
        }

        public ulong NextHandleAllocator()
        {
            RequireActive();
            m_HandleAllocator = checked(m_HandleAllocator + 1UL);
            if (m_HandleAllocator == 0)
                throw new OverflowException("Simulation handle allocator overflowed.");
            return m_HandleAllocator;
        }

        public ulong CaptureHandleAllocator()
        {
            RequireActive();
            return m_HandleAllocator;
        }

        public void RestoreHandleAllocator(ulong value)
        {
            RequireActive();
            m_HandleAllocator = value;
        }

        internal ulong HandleAllocator
        {
            get
            {
                RequireActive();
                return m_HandleAllocator;
            }
        }

        internal void Dispose()
        {
            m_Disposed = true;
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FixedCharacterHandleAllocatorState));
        }
    }
}
