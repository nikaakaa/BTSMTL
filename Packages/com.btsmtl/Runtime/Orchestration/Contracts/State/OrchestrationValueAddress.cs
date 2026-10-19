namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationValueAddress
    {
        public OrchestrationValueAddress(OrchestrationValueKind kind, int offsetBytes, int bitIndex = 0)
        {
            Kind = kind;
            OffsetBytes = offsetBytes;
            BitIndex = bitIndex;
        }

        public OrchestrationValueKind Kind { get; }
        public int OffsetBytes { get; }
        public int BitIndex { get; }
    }
}
