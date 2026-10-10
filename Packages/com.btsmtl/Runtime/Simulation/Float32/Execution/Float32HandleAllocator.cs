using System;

namespace ThirdPersonSimulation
{
    internal readonly struct Float32HandleAllocator
    {
        readonly Float32AbilityExecutionFrame m_Frame;

        public Float32HandleAllocator(Float32AbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public ulong Next() => m_Frame.HandleAllocatorState.NextHandleAllocator();

        public ulong Capture() => m_Frame.HandleAllocatorState.CaptureHandleAllocator();

        public void Restore(ulong value) => m_Frame.HandleAllocatorState.RestoreHandleAllocator(value);
    }
}

