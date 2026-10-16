using System;
using Photon.Deterministic;

namespace ThirdPersonSimulation
{

    public enum AbilityTreeClipHook : byte
    {
        OnEnable = 0,
        OnDisable = 1,
        OnDestroy = 2,
        Root = 3
    }

    public readonly partial struct AbilityTreeClipInvocation
    {
        public AbilityTreeClipInvocation(
            CanonicalTextValue clipAuthoringId,
            CanonicalTextValue treeGraphId,
            CanonicalTextValue treeGraphRevision,
            CanonicalTextValue nodeAuthoringId,
            AbilityTreeClipHook hook,
            FP time,
            FP previousTime,
            FP windowStartTime,
            int cycle,
            ulong playbackGeneration,
            ulong branchRevision,
            ulong actionInstanceId,
            ulong timelineRuntimeHandle,
            FP bodyStartFraction,
            FP bodyEndFraction)
        {
            ClipAuthoringId = SimulationIdentity.Require(clipAuthoringId, nameof(clipAuthoringId));
            TreeGraphId = SimulationIdentity.Require(treeGraphId, nameof(treeGraphId));
            TreeGraphRevision = SimulationIdentity.Require(treeGraphRevision, nameof(treeGraphRevision));
            NodeAuthoringId = SimulationIdentity.Require(nodeAuthoringId, nameof(nodeAuthoringId));
            Time = time;
            PreviousTime = previousTime;
            WindowStartTime = windowStartTime;
            Hook = hook;
            Cycle = cycle;
            PlaybackGeneration = playbackGeneration;
            BranchRevision = branchRevision;
            ActionInstanceId = actionInstanceId;
            TimelineRuntimeHandle = timelineRuntimeHandle;
            BodyStartFraction = bodyStartFraction;
            BodyEndFraction = bodyEndFraction;
            RequireRecord();
        }

        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue ClipAuthoringId { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue TreeGraphId { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue TreeGraphRevision { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue NodeAuthoringId { get; }
        public AbilityTreeClipHook Hook { get; }
        public FP Time { get; }
        public FP PreviousTime { get; }
        public FP WindowStartTime { get; }
        public int Cycle { get; }
        public ulong PlaybackGeneration { get; }
        public ulong BranchRevision { get; }
        public ulong ActionInstanceId { get; }
        public ulong TimelineRuntimeHandle { get; }
        public FP BodyStartFraction { get; }
        public FP BodyEndFraction { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(ClipAuthoringId) &&
            !CanonicalTextValue.IsNullOrEmpty(TreeGraphId) &&
            !CanonicalTextValue.IsNullOrEmpty(TreeGraphRevision) &&
            !CanonicalTextValue.IsNullOrEmpty(NodeAuthoringId) &&
            PlaybackGeneration != 0 &&
            BranchRevision != 0 &&
            ActionInstanceId != 0 &&
            TimelineRuntimeHandle != 0;

        [ValueValidation]
        void RequireRecord()
        {
            if ((byte)Hook > (byte)AbilityTreeClipHook.Root)
                throw new ArgumentOutOfRangeException("Hook");
            if (Time < FP._0)
                throw new ArgumentOutOfRangeException("Time");
            if (Cycle < 0)
                throw new ArgumentOutOfRangeException("Cycle");
            if (PlaybackGeneration == 0)
                throw new ArgumentOutOfRangeException("PlaybackGeneration");
            if (BranchRevision == 0)
                throw new ArgumentOutOfRangeException("BranchRevision");
            if (ActionInstanceId == 0)
                throw new ArgumentOutOfRangeException("ActionInstanceId");
            if (TimelineRuntimeHandle == 0)
                throw new ArgumentOutOfRangeException("TimelineRuntimeHandle");
        }
    }





}
