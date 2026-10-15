using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage]
    [ValueOperations(Resources = false)]
    public partial struct OrchestrationTimelineControl<TTime> :
        IEquatable<OrchestrationTimelineControl<TTime>>
        where TTime : unmanaged, IEquatable<TTime>
    {
        public TTime Rate;
        public bool Paused;
    }

    [ValueStorage]
    [ValueOperations(Resources = false)]
    public partial struct OrchestrationTimelineStopContext : IEquatable<OrchestrationTimelineStopContext>
    {
        public uint Cause;
        public ulong Tick;
        public int SourceIdentityId;
    }

    [ValueStorage]
    [ValueOperations(Resources = false)]
    public partial struct OrchestrationTimelineStart<TTime> :
        IEquatable<OrchestrationTimelineStart<TTime>>
        where TTime : unmanaged, IEquatable<TTime>
    {
        public int ContentIdentityId;
        public int ContentRevisionId;
        public OrchestrationTimelinePlaybackMode Mode;
        public ulong PlaybackIdentity;
        public int SourceIdentityId;
        public ulong SourceGeneration;
        public ulong OwnerInstanceId;
        public ulong InputSequence;
        public ulong StartTick;
        public TTime StartTime;
    }

    [ValueStorage]
    [ValueOperations(Resources = false)]
    public partial struct OrchestrationTimelinePlaybackState<TTime> :
        IEquatable<OrchestrationTimelinePlaybackState<TTime>>
        where TTime : unmanaged, IEquatable<TTime>
    {
        public OrchestrationTimelineProgress<TTime, OrchestrationTimelinePlaybackStatus,
            OrchestrationTimelineControl<TTime>, OrchestrationTimelineStopContext> Progress;
        public OrchestrationTimelineStart<TTime> Start;
        public ulong InvocationSequence;
        public int TreeExitRequestCount;
        public int TreeExitCount;
    }
}
