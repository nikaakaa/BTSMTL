namespace BTSMTL.Orchestration
{
    public struct OrchestrationOperationState
    {
        public OrchestrationOperationStatus Status;
        public int ChildCursor;
        public OrchestrationStopContext StopContext;
        public ulong Generation;
        public int ActiveStateIdentityId;
        public int ExitingStateIdentityId;
        public ulong TimelineRuntimeHandle;
    }
}
