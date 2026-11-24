using System;

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
            destination = OrchestrationMemoryImageWriter.Section<TValue>(memory, new OrchestrationMemorySection(address.OffsetBytes, 1))[0];

        public void Write(Span<byte> memory, in OrchestrationValueAddress address, in TValue value) =>
            OrchestrationMemoryImageWriter.Section<TValue>(memory, new OrchestrationMemorySection(address.OffsetBytes, 1))[0] = value;
    }
}
