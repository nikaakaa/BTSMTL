using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationGraphContentBlocks
    {
        public OrchestrationGraphContentBlocks(GraphExecutionIdentity identity,
            byte[] structure, byte[] parameters, byte[] sources)
        {
            Identity = identity;
            Structure = structure;
            Parameters = parameters;
            Sources = sources;
        }

        public GraphExecutionIdentity Identity { get; }
        public byte[] Structure { get; }
        public byte[] Parameters { get; }
        public byte[] Sources { get; }
    }
}
