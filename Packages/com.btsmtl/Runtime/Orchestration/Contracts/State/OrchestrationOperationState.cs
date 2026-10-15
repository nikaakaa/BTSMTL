namespace BTSMTL.Orchestration
{
    public struct OrchestrationOperationState
    {
        public OrchestrationOperationStatus Status;
        public int ChildCursor;
        public uint StopCause;
        public int StopSourceOperationIndex;
        public int StopReplacementOperationIndex;
        public ulong Generation;
        public int ActiveStateIdentityId;
        public int PendingStateIdentityId;
        public int ExitingStateIdentityId;
        public int TransitionIdentityId;
        public ulong TimelineRuntimeHandle;
    }
}
