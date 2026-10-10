using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.Fixed
{
    internal readonly struct FixedHandleAllocator
    {
        readonly FixedAbilityExecutionFrame m_Frame;

        public FixedHandleAllocator(FixedAbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public ulong Next() => m_Frame.HandleAllocatorState.NextHandleAllocator();

        public ulong Capture() => m_Frame.HandleAllocatorState.CaptureHandleAllocator();

        public void Restore(ulong value) => m_Frame.HandleAllocatorState.RestoreHandleAllocator(value);
    }
}

