namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationValueAddress
    {
        public OrchestrationValueAddress(OrchestrationValueKind kind, int offsetBytes, int sizeBytes, int bitIndex = 0)
        {
            Kind = kind;
            OffsetBytes = offsetBytes;
            SizeBytes = sizeBytes;
            BitIndex = bitIndex;
        }

        public OrchestrationValueKind Kind { get; }
        public int OffsetBytes { get; }
        public int SizeBytes { get; }
        public int BitIndex { get; }
    }
}
