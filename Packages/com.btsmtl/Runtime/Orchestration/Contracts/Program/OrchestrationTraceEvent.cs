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
    }
}
