using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExecutionSource<TValue> where TValue : unmanaged
    {
        internal OrchestrationExecutionSource(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            int frameSlot, ulong tick, in OrchestrationStopContext stopContext)
        {
            Frames = frames;
            FrameSlot = frameSlot;
            Tick = tick;
            StopContext = stopContext;
        }

        public ReadOnlyOrchestrationExecutionFrames<TValue> Frames { get; }
        public int FrameSlot { get; }
        public ulong Tick { get; }
        public OrchestrationStopContext StopContext { get; }
        public ref readonly OrchestrationExecutionFrameState Header => ref Frames.Header(FrameSlot);
    }
}
