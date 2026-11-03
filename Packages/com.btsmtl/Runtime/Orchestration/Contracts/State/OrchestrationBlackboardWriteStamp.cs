namespace BTSMTL.Orchestration
{
    public struct OrchestrationBlackboardWriteStamp
    {
        public ulong LogicTick;
        public ulong CommandSequence;
        public OrchestrationBlackboardSourceContext SourceContext;
        public int SourceOperationIndex;
        public int TimelineOperationIndex;
        public int ClipOperationIndex;
        public int Cycle;
    }
}
