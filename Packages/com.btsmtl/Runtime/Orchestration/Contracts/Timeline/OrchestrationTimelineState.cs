using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage]
    [ValueOperations(Resources = false)]
    public partial struct OrchestrationTimelineSequenceState : IEquatable<OrchestrationTimelineSequenceState>
    {
        public ulong NextPlaybackIdentity;
    }

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

        public OrchestrationTimelineStart<TTarget> WithStartTime<TTarget>(TTarget startTime)
            where TTarget : unmanaged, IEquatable<TTarget> => new()
        {
            ContentIdentityId = ContentIdentityId,
            ContentRevisionId = ContentRevisionId,
            Mode = Mode,
            PlaybackIdentity = PlaybackIdentity,
            SourceIdentityId = SourceIdentityId,
            SourceGeneration = SourceGeneration,
            OwnerInstanceId = OwnerInstanceId,
            InputSequence = InputSequence,
            StartTick = StartTick,
            StartTime = startTime
        };
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
        public ulong LastConsumedTick;
        public TTime LastConsumedClockTime;
        public ulong InvocationSequence;
        public int TreeExitRequestCount;
        public int TreeExitCount;
        // 当前分段与待执行的分段跳转目标（排序后分段下标 + 1，0 为无）；跳转由下一次推进执行。
        public int Section;
        public int PendingSectionJump;
        // 已执行的跳转次数与最近一次跳转的离开时刻、目标起点，供表现与诊断分段遍历。
        public int SectionJumpCount;
        public ulong SectionJumpTick;
        public TTime SectionJumpFrom;
        public TTime SectionJumpTo;
    }
}
