using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueOperations]
    public partial struct OrchestrationTimelineClipOccurrence :
        IEquatable<OrchestrationTimelineClipOccurrence>
    {
        public int ClipOrdinal;
        public int Cycle;
    }

    public readonly struct OrchestrationTimelineTreeCycleWindow
    {
        public OrchestrationTimelineTreeCycleWindow(int stateIndex, int wordOffset, int cycleCapacity)
        {
            StateIndex = stateIndex;
            WordOffset = wordOffset;
            CycleCapacity = cycleCapacity;
        }

        public int StateIndex { get; }
        public int WordOffset { get; }
        public int CycleCapacity { get; }
        public int WordCount => (int)(((long)CycleCapacity + 31) / 32);
    }
}
