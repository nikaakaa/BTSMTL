using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExecutionSource<TValue> where TValue : unmanaged
    {
        internal OrchestrationExecutionSource(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            int frameSlot, ulong tick, in OrchestrationStopContext stopContext)
        {
            Frames = frames;
            CharacterFrames = frames;
            FrameSlot = frameSlot;
            Tick = tick;
            StopContext = stopContext;
        }

        public ReadOnlyOrchestrationExecutionFrames<TValue> Frames { get; }
        public ReadOnlyOrchestrationExecutionFrames<TValue> CharacterFrames { get; }
        public int FrameSlot { get; }
        public ulong Tick { get; }
        public OrchestrationStopContext StopContext { get; }
        public ref readonly OrchestrationExecutionFrameState Header => ref Frames.Header(FrameSlot);

        OrchestrationExecutionSource(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            ReadOnlyOrchestrationExecutionFrames<TValue> characterFrames, int frameSlot, ulong tick,
            OrchestrationStopContext stopContext)
        {
            Frames = frames;
            CharacterFrames = characterFrames;
            FrameSlot = frameSlot;
            Tick = tick;
            StopContext = stopContext;
        }

        public OrchestrationExecutionSource<TValue> WithCharacterSource(in OrchestrationExecutionSource<TValue> source) =>
            new(Frames, source.CharacterFrames, FrameSlot, Tick, StopContext);
    }
}
