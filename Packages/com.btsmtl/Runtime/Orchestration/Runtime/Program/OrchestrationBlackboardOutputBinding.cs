namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationBlackboardOutputBinding
    {
        public OrchestrationBlackboardOutputBinding(int sourceOutputPortIndex, int valueSlot)
        {
            SourceOutputPortIndex = sourceOutputPortIndex;
            ValueSlot = valueSlot;
        }

        public int SourceOutputPortIndex { get; }
        public int ValueSlot { get; }
    }
}
