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

    [ValueOperations]
    public partial struct OrchestrationTimelineTreeExitRange :
        IEquatable<OrchestrationTimelineTreeExitRange>
    {
        public int ClipOrdinal;
        public int FirstCycle;
        public int LastCycle;
    }
}
