namespace BTSMTL.Orchestration
{
    public struct OrchestrationBlackboardSourceContext
    {
        public int SkillContentId;
#pragma warning disable CS0169
        readonly int m_AlignmentFill;
#pragma warning restore CS0169
        public ulong InstanceId;
        public ulong SkillExecutionGeneration;
        public ulong SegmentGeneration;
    }
}
