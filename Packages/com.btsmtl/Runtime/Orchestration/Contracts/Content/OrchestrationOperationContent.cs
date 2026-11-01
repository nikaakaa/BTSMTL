using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationOperationContentBinding
    {
        public OrchestrationOperationContentBinding(int operationIndex, string encodingContract)
        {
            OperationIndex = operationIndex;
            EncodingContract = encodingContract;
        }

        public int OperationIndex { get; }
        public string EncodingContract { get; }
    }
}
