namespace BTSMTL.Orchestration
{
    public struct OrchestrationBlackboardSourceContext
    {
        internal const int InputValueCount = 1;
        internal const int OutputValueCount = 4;

        public int SkillContentId;
#pragma warning disable CS0169
        readonly int m_AlignmentFill;
#pragma warning restore CS0169
        public ulong InstanceId;
        public ulong SkillExecutionGeneration;
        public ulong SegmentGeneration;
    }
}
