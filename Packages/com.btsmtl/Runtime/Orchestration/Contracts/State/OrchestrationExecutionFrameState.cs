namespace BTSMTL.Orchestration
{
    public struct OrchestrationExecutionFrameState
    {
        public int SkillContentId;
        public int EntryOperationIndex;
        public int OwnerOperationIndex;
        public int ParentFrameIndex;
        public int ParentTimelineOperationIndex;
        public ulong ActionInstanceId;
        public ulong Generation;
        public OrchestrationTimelineStart<OrchestrationTime> TimelineStart;
        public int ClipOrdinal;
        public int Cycle;
        public bool Marker;
    }

    public struct OrchestrationExecutionOwnerToken
    {
        public int SkillContentId;
        public int ExecutionFrameOwnerOperationIndex;
        public ulong ActionInstanceId;
        public ulong ExecutionGeneration;
        public OrchestrationTimelineStart<OrchestrationTime> TimelineStart;
        public int ClipOrdinal;
        public int Cycle;
        public bool Marker;
        public int InvocationOperationIndex;
        public ulong InvocationGeneration;
        public int GenerationOwnerOperationIndex;
        public int StateOperationIndex;
        public ulong StateGeneration;

        public static OrchestrationExecutionOwnerToken Create(in OrchestrationExecutionFrameState frame,
            int invocationOperationIndex, ulong invocationGeneration, int stateOperationIndex = -1,
            ulong stateGeneration = 0, int generationOwnerOperationIndex = -1) => new OrchestrationExecutionOwnerToken
        {
            SkillContentId = frame.SkillContentId,
            ExecutionFrameOwnerOperationIndex = frame.OwnerOperationIndex,
            ActionInstanceId = frame.ActionInstanceId,
            ExecutionGeneration = frame.Generation,
            TimelineStart = frame.TimelineStart,
            ClipOrdinal = frame.ClipOrdinal,
            Cycle = frame.Cycle,
            Marker = frame.Marker,
            InvocationOperationIndex = invocationOperationIndex,
            InvocationGeneration = invocationGeneration,
            GenerationOwnerOperationIndex = generationOwnerOperationIndex,
            StateOperationIndex = stateOperationIndex,
            StateGeneration = stateGeneration
        };

        public bool MatchesFrame(in OrchestrationExecutionFrameState frame) =>
            SkillContentId == frame.SkillContentId &&
            ExecutionFrameOwnerOperationIndex == frame.OwnerOperationIndex &&
            ActionInstanceId == frame.ActionInstanceId && ExecutionGeneration == frame.Generation &&
            TimelineStart.Equals(frame.TimelineStart) && ClipOrdinal == frame.ClipOrdinal &&
            Cycle == frame.Cycle && Marker == frame.Marker;

        public bool Equals(in OrchestrationExecutionOwnerToken other) =>
            SkillContentId == other.SkillContentId &&
            ExecutionFrameOwnerOperationIndex == other.ExecutionFrameOwnerOperationIndex &&
            ActionInstanceId == other.ActionInstanceId && ExecutionGeneration == other.ExecutionGeneration &&
            TimelineStart.Equals(other.TimelineStart) && ClipOrdinal == other.ClipOrdinal &&
            Cycle == other.Cycle && Marker == other.Marker &&
            InvocationOperationIndex == other.InvocationOperationIndex &&
            InvocationGeneration == other.InvocationGeneration &&
            GenerationOwnerOperationIndex == other.GenerationOwnerOperationIndex &&
            StateOperationIndex == other.StateOperationIndex && StateGeneration == other.StateGeneration;
    }
}
