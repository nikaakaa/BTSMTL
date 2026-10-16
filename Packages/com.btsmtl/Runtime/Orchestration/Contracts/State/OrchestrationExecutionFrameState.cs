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
}
