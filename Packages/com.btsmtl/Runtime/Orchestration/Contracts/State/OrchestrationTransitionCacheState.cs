namespace BTSMTL.Orchestration
{
    public struct OrchestrationTransitionCacheState
    {
        public OrchestrationExecutionOwnerToken Context;
        public ulong MachineGeneration;
        public bool Valid;
        public bool Passed;
    }

    public struct OrchestrationTransitionSnapshot<TValue> where TValue : unmanaged
    {
        public TValue Value;
        public ulong Revision;
        public ulong OwnerGeneration;
    }
}
