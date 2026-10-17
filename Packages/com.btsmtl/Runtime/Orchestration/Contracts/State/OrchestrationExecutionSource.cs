using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExecutionSource<TValue> where TValue : unmanaged
    {
        internal OrchestrationExecutionSource(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            ReadOnlySpan<TValue> characterValues, ReadOnlySpan<OrchestrationBlackboardMetadata> characterBlackboards,
            int frameSlot, ulong tick, in OrchestrationStopContext stopContext)
        {
            Frames = frames;
            CharacterValues = characterValues;
            CharacterBlackboards = characterBlackboards;
            FrameSlot = frameSlot;
            Tick = tick;
            StopContext = stopContext;
        }

        public ReadOnlyOrchestrationExecutionFrames<TValue> Frames { get; }
        public ReadOnlySpan<TValue> CharacterValues { get; }
        public ReadOnlySpan<OrchestrationBlackboardMetadata> CharacterBlackboards { get; }
        public int FrameSlot { get; }
        public ulong Tick { get; }
        public OrchestrationStopContext StopContext { get; }
        public ref readonly OrchestrationExecutionFrameState Header => ref Frames.Header(FrameSlot);
    }
}
