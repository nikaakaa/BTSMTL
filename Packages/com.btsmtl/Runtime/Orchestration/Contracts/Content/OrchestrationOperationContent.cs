using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationOperationContent
    {
        public OrchestrationOperationContent(int operationIndex, string encodingContract, ReadOnlyMemory<byte> payload)
        {
            OperationIndex = operationIndex;
            EncodingContract = encodingContract;
            Payload = payload;
        }

        public int OperationIndex { get; }
        public string EncodingContract { get; }
        public ReadOnlyMemory<byte> Payload { get; }
    }
}
