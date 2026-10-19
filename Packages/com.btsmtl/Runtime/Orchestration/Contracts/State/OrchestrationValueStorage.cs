using System;
using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public interface IOrchestrationValueStorage<TValue> where TValue : unmanaged
    {
        void Read(ReadOnlySpan<byte> memory, in OrchestrationValueAddress address, ref TValue destination);
        void Write(Span<byte> memory, in OrchestrationValueAddress address, in TValue value);
    }

    public readonly struct OrchestrationArrayValueStorage<TValue> : IOrchestrationValueStorage<TValue>
        where TValue : unmanaged
    {
        public void Read(ReadOnlySpan<byte> memory, in OrchestrationValueAddress address, ref TValue destination) =>
            destination = MemoryMarshal.Read<TValue>(memory.Slice(address.OffsetBytes));

        public void Write(Span<byte> memory, in OrchestrationValueAddress address, in TValue value) =>
            MemoryMarshal.Cast<byte, TValue>(memory.Slice(address.OffsetBytes))[0] = value;
    }
}
