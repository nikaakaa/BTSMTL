using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage]
    [ValueOperations(Resources = false)]
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
        public bool InitialBoundaryPending;
    }
}
