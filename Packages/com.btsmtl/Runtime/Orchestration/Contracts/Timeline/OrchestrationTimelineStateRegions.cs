namespace BTSMTL.Orchestration
{
    // Timeline 推进只读的区段位置，纯值，可进入 Burst；由区段布局声明导出。
    public readonly struct OrchestrationTimelineStateRegions<TPlayback> where TPlayback : unmanaged
    {
        public OrchestrationTimelineStateRegions(OrchestrationStateRegion<TPlayback> playbacks,
            OrchestrationStateRegion<OrchestrationTimelineSequenceState> sequences,
            OrchestrationStateRegion<ulong> treeExitBits, OrchestrationStateRegion<long> treeExitFirstCycles,
            OrchestrationStateRegion<OrchestrationTimelineClipOccurrence> treeExitRequests)
        {
            Playbacks = playbacks;
            Sequences = sequences;
            TreeExitBits = treeExitBits;
            TreeExitFirstCycles = treeExitFirstCycles;
            TreeExitRequests = treeExitRequests;
        }

        public OrchestrationStateRegion<TPlayback> Playbacks { get; }
        public OrchestrationStateRegion<OrchestrationTimelineSequenceState> Sequences { get; }
        public OrchestrationStateRegion<ulong> TreeExitBits { get; }
        public OrchestrationStateRegion<long> TreeExitFirstCycles { get; }
        public OrchestrationStateRegion<OrchestrationTimelineClipOccurrence> TreeExitRequests { get; }
    }
}
