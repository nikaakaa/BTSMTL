using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.FPGameplay
{
    internal readonly struct FPHandleAllocator
    {
        readonly FPAbilityExecutionFrame m_Frame;

        public FPHandleAllocator(FPAbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public ulong Next() => m_Frame.HandleAllocatorState.NextHandleAllocator();

        public ulong Capture() => m_Frame.HandleAllocatorState.CaptureHandleAllocator();

        public void Restore(ulong value) => m_Frame.HandleAllocatorState.RestoreHandleAllocator(value);
    }
}

