using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationTimelineInitialBoundaryState : byte
    {
        Pending = 0,
        Applied = 1,
        Skipped = 2
    }

    [ValueStorage]
    [ValueOperations]
    public partial struct OrchestrationTimelineProgress<TTime, TPlaybackState, TControl, TStopContext> :
        IEquatable<OrchestrationTimelineProgress<TTime, TPlaybackState, TControl, TStopContext>>
        where TTime : unmanaged, IEquatable<TTime>
        where TPlaybackState : unmanaged, Enum
        where TControl : unmanaged, IEquatable<TControl>
        where TStopContext : unmanaged, IEquatable<TStopContext>
    {
        public TPlaybackState State;
        public TTime CursorTime;
        public int Cycle;
        public int TimeCarry;
        public TControl Control;
        public bool HasStopContext;
        public TStopContext StopContext;
        public OrchestrationTimelineInitialBoundaryState InitialBoundaryState;
    }
}
