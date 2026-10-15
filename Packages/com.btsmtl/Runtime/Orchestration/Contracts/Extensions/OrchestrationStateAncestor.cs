namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationStateAncestor
    {
        public OrchestrationStateAncestor(int stateOperationIndex, ulong generation)
        {
            StateOperationIndex = stateOperationIndex;
            Generation = generation;
        }

        public int StateOperationIndex { get; }
        public ulong Generation { get; }
    }
}
