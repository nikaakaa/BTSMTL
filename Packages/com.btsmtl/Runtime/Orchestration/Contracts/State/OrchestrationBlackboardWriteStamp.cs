namespace BTSMTL.Orchestration
{
    public struct OrchestrationBlackboardWriteStamp
    {
        public int SourceOperationIndex;
        public ulong LogicTick;
        public OrchestrationBlackboardSourceContext SourceContext;
        public int TimelineOperationIndex;
        public int ClipOperationIndex;
        public int Cycle;
        public ulong CommandSequence;
    }
}
