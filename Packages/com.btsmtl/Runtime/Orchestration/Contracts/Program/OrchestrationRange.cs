namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationRange
    {
        public OrchestrationRange(int offset, int count)
        {
            Offset = offset;
            Count = count;
        }

        public int Offset { get; }
        public int Count { get; }
    }
}
