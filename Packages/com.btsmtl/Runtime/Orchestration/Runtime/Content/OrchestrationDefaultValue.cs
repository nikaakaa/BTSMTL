namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationDefaultValue<TValue> where TValue : unmanaged
    {
        public OrchestrationDefaultValue(int valueSlot, TValue value)
        {
            ValueSlot = valueSlot;
            Value = value;
        }

        public int ValueSlot { get; }
        public TValue Value { get; }
    }
}
