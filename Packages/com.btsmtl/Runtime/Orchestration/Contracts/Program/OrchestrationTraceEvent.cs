namespace BTSMTL.Orchestration
{
    public enum OrchestrationTraceKind : byte
    {
        Enter = 1,
        Running = 2,
        Complete = 3,
        StopRequested = 4,
        Stopped = 5,
        ForceStopped = 6,
        Waiting = 7,
        LoopIterationEnter = 8,
        LoopIterationComplete = 9,
        Edge = 10,
        Blackboard = 11
    }

    public struct OrchestrationTraceEvent<TValue> where TValue : unmanaged
    {
        public OrchestrationTraceKind Kind;
        public int OperationIndex;
        public int EdgeIndex;
        public ulong Generation;
        public int StateFrameIndex;
        public int ContextIndex;
        public OrchestrationOperationResult Result;
        public OrchestrationWaitReason WaitReason;
        public uint StopCause;
        public bool Selected;
        public bool Passed;
        public int ValueSlot;
        public TValue Value;
        public OrchestrationBlackboardMetadata Blackboard;
    }

    public struct OrchestrationStateTraceFrame
    {
        public int ParentIndex;
        public int StateOperationIndex;
        public ulong Generation;
        public int ContextIndex;
    }

    // 当前执行或关闭入口的 scope，不追溯节点首次激活时的 hook。
    public struct OrchestrationTraceContext
    {
        public int SourceIndex;
        public int InvocationSourceIndex;
        public int ParentInvocationSourceIndex;
        public int InvocationOperationIndex;
        public int ParentInvocationOperationIndex;
        public int ExecutionFrameOwnerOperationIndex;
        public ulong ActionInstanceId;
        public ulong SkillExecutionGeneration;
        public ulong GraphInvocationGeneration;
        public ulong ParentInvocationGeneration;
        public OrchestrationTimelineStart<OrchestrationTime> TimelineStart;
        public int ClipOrdinal;
        public int Cycle;
        public bool Marker;
    }

    public readonly struct OrchestrationTraceBatch
    {
        public OrchestrationTraceBatch(OrchestrationRange events, OrchestrationRange stateFrames,
            OrchestrationRange contexts)
        {
            Events = events;
            StateFrames = stateFrames;
            Contexts = contexts;
        }

        public OrchestrationRange Events { get; }
        public OrchestrationRange StateFrames { get; }
        public OrchestrationRange Contexts { get; }
    }
}
