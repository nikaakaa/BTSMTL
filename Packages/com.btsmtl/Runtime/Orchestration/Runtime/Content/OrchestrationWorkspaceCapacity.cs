using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationWorkspaceCapacity
    {
        public OrchestrationWorkspaceCapacity(ReadOnlyMemory<int> inputCounts,
            ReadOnlyMemory<int> outputCounts, int stateDepth, int graphCallDepth,
            int entryDepth, int stateAncestorCount)
        {
            InputCounts = inputCounts;
            OutputCounts = outputCounts;
            StateDepth = stateDepth;
            GraphCallDepth = graphCallDepth;
            EntryDepth = entryDepth;
            StateAncestorCount = stateAncestorCount;
        }

        public ReadOnlyMemory<int> InputCounts { get; }
        public ReadOnlyMemory<int> OutputCounts { get; }
        public int StateDepth { get; }
        public int GraphCallDepth { get; }
        public int EntryDepth { get; }
        public int StateAncestorCount { get; }
    }
}
