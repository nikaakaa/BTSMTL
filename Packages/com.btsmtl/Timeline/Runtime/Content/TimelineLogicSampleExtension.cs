using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineLogicSampleExtension
    {
        // 推进路径只读这组纯值序号，合同端口直接用同一序号声明。
        public static class InputIndex
        {
            public const int Weight = 0;
            public const int PreviousSourceTime = 1;
            public const int SourceTime = 2;
            public const int NormalizedTime = 3;
            public const int Cycle = 4;
            public const int PlaybackIdentity = 5;
            public const int PlaybackSlot = 6;
            public const int AdvancePreviousTime = 7;
            public const int AdvancePreviousCycle = 8;
            public const int AdvanceTime = 9;
            public const int AdvanceCycle = 10;
        }

        public const int InputCount = InputIndex.AdvanceCycle + 1;

        public TimelineLogicSampleExtension(OrchestrationExtensionIdentity identity, uint semanticVersion)
        {
            Identity = identity;
            SemanticVersion = semanticVersion;
        }

        public OrchestrationExtensionIdentity Identity { get; }
        public uint SemanticVersion { get; }

        public static OrchestrationExtensionContract Contract { get; } = new(
            OrchestrationExtensionKind.Action,
            OrchestrationExtensionAccess.CandidateWrite,
            OrchestrationExtensionInvocationKind.Execute,
            OrchestrationExtensionCompletion.Immediate,
            new[]
            {
                Input("weight", OrchestrationValueKind.Scalar, InputIndex.Weight),
                Input("previous-source-time", OrchestrationValueKind.Scalar, InputIndex.PreviousSourceTime),
                Input("source-time", OrchestrationValueKind.Scalar, InputIndex.SourceTime),
                Input("normalized-time", OrchestrationValueKind.Scalar, InputIndex.NormalizedTime),
                Input("cycle", OrchestrationValueKind.Int32, InputIndex.Cycle),
                Input("playback-identity", OrchestrationValueKind.UInt64, InputIndex.PlaybackIdentity),
                Input("playback-slot", OrchestrationValueKind.Int32, InputIndex.PlaybackSlot),
                Input("advance-previous-time", OrchestrationValueKind.Scalar, InputIndex.AdvancePreviousTime),
                Input("advance-previous-cycle", OrchestrationValueKind.Int32, InputIndex.AdvancePreviousCycle),
                Input("advance-time", OrchestrationValueKind.Scalar, InputIndex.AdvanceTime),
                Input("advance-cycle", OrchestrationValueKind.Int32, InputIndex.AdvanceCycle)
            }, System.ReadOnlyMemory<OrchestrationExtensionContentField>.Empty);

        public OrchestrationExtensionReference CreateReference(int contentIndex, string sourcePath) =>
            new OrchestrationExtensionDefinition(Identity, SemanticVersion, Contract).CreateReference(contentIndex, sourcePath);

        static OrchestrationExtensionPort Input(string id, OrchestrationValueKind valueKind, int index) =>
            new(id, valueKind, OrchestrationExtensionPortDirection.Input, index);
    }

    public interface ITimelineLogicSampleExtension<TScalar> : IOrchestrationExtension
        where TScalar : unmanaged
    {
        int PrepareSample(in TimelineContentClip clip, int clipIndex,
            ITimelineRuntimeSourceTimeMapping<TScalar> sourceTimeMapping);
    }
}
