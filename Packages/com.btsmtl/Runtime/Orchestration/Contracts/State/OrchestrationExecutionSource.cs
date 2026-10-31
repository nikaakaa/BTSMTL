using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExecutionSource<TValue> where TValue : unmanaged
    {
        internal OrchestrationExecutionSource(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            int frameSlot, ulong tick, in OrchestrationStopContext stopContext)
        {
            Frames = frames;
            SharedFrames = frames;
            SharedFrameSlot = frameSlot;
            FrameSlot = frameSlot;
            Tick = tick;
            StopContext = stopContext;
        }

        public ReadOnlyOrchestrationExecutionFrames<TValue> Frames { get; }
        public ReadOnlyOrchestrationExecutionFrames<TValue> SharedFrames { get; }
        public int SharedFrameSlot { get; }
        public int FrameSlot { get; }
        public ulong Tick { get; }
        public OrchestrationStopContext StopContext { get; }
        public ref readonly OrchestrationExecutionFrameState Header => ref Frames.Header(FrameSlot);

        OrchestrationExecutionSource(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            ReadOnlyOrchestrationExecutionFrames<TValue> sharedFrames, int sharedFrameSlot, int frameSlot, ulong tick,
            OrchestrationStopContext stopContext)
        {
            Frames = frames;
            SharedFrames = sharedFrames;
            SharedFrameSlot = sharedFrameSlot;
            FrameSlot = frameSlot;
            Tick = tick;
            StopContext = stopContext;
        }

        public OrchestrationExecutionSource<TValue> WithSharedSource(in OrchestrationExecutionSource<TValue> source) =>
            new(Frames, source.SharedFrames, source.SharedFrameSlot, FrameSlot, Tick, StopContext);
    }
}
