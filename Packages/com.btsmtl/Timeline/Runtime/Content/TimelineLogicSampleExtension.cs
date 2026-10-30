using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineLogicSampleExtension
    {
        // 推进路径只读这组纯值序号，端口声明由同一序号构造。
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

        public static class InputSchema
        {
            public static readonly OrchestrationExtensionPort Weight = new("weight", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, InputIndex.Weight);
            public static readonly OrchestrationExtensionPort PreviousSourceTime = new("previous-source-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, InputIndex.PreviousSourceTime);
            public static readonly OrchestrationExtensionPort SourceTime = new("source-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, InputIndex.SourceTime);
            public static readonly OrchestrationExtensionPort NormalizedTime = new("normalized-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, InputIndex.NormalizedTime);
            public static readonly OrchestrationExtensionPort Cycle = new("cycle", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, InputIndex.Cycle);
            public static readonly OrchestrationExtensionPort PlaybackIdentity = new("playback-identity", OrchestrationValueKind.UInt64, OrchestrationExtensionPortDirection.Input, InputIndex.PlaybackIdentity);
            public static readonly OrchestrationExtensionPort PlaybackSlot = new("playback-slot", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, InputIndex.PlaybackSlot);
            public static readonly OrchestrationExtensionPort AdvancePreviousTime = new("advance-previous-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, InputIndex.AdvancePreviousTime);
            public static readonly OrchestrationExtensionPort AdvancePreviousCycle = new("advance-previous-cycle", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, InputIndex.AdvancePreviousCycle);
            public static readonly OrchestrationExtensionPort AdvanceTime = new("advance-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, InputIndex.AdvanceTime);
            public static readonly OrchestrationExtensionPort AdvanceCycle = new("advance-cycle", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, InputIndex.AdvanceCycle);
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
                InputSchema.Weight,
                InputSchema.PreviousSourceTime,
                InputSchema.SourceTime,
                InputSchema.NormalizedTime,
                InputSchema.Cycle,
                InputSchema.PlaybackIdentity,
                InputSchema.PlaybackSlot,
                InputSchema.AdvancePreviousTime,
                InputSchema.AdvancePreviousCycle,
                InputSchema.AdvanceTime,
                InputSchema.AdvanceCycle
            }, System.ReadOnlyMemory<OrchestrationExtensionContentField>.Empty);

        public OrchestrationExtensionReference CreateReference(int contentIndex, string sourcePath) =>
            new OrchestrationExtensionDefinition(Identity, SemanticVersion, Contract).CreateReference(contentIndex, sourcePath);
    }

    public static class TimelineLogicSamplePorts<TOperation> where TOperation : struct
    {
        public static readonly OrchestrationExtensionInputPort<TOperation> Weight = Input(TimelineLogicSampleExtension.InputSchema.Weight);
        public static readonly OrchestrationExtensionInputPort<TOperation> PreviousSourceTime = Input(TimelineLogicSampleExtension.InputSchema.PreviousSourceTime);
        public static readonly OrchestrationExtensionInputPort<TOperation> SourceTime = Input(TimelineLogicSampleExtension.InputSchema.SourceTime);
        public static readonly OrchestrationExtensionInputPort<TOperation> NormalizedTime = Input(TimelineLogicSampleExtension.InputSchema.NormalizedTime);
        public static readonly OrchestrationExtensionInputPort<TOperation> Cycle = Input(TimelineLogicSampleExtension.InputSchema.Cycle);
        public static readonly OrchestrationExtensionInputPort<TOperation> PlaybackIdentity = Input(TimelineLogicSampleExtension.InputSchema.PlaybackIdentity);
        public static readonly OrchestrationExtensionInputPort<TOperation> PlaybackSlot = Input(TimelineLogicSampleExtension.InputSchema.PlaybackSlot);
        public static readonly OrchestrationExtensionInputPort<TOperation> AdvancePreviousTime = Input(TimelineLogicSampleExtension.InputSchema.AdvancePreviousTime);
        public static readonly OrchestrationExtensionInputPort<TOperation> AdvancePreviousCycle = Input(TimelineLogicSampleExtension.InputSchema.AdvancePreviousCycle);
        public static readonly OrchestrationExtensionInputPort<TOperation> AdvanceTime = Input(TimelineLogicSampleExtension.InputSchema.AdvanceTime);
        public static readonly OrchestrationExtensionInputPort<TOperation> AdvanceCycle = Input(TimelineLogicSampleExtension.InputSchema.AdvanceCycle);

        static OrchestrationExtensionInputPort<TOperation> Input(in OrchestrationExtensionPort declaration) =>
            new(declaration.Id, declaration.ValueKind, declaration.Index);
    }

    public interface ITimelineLogicSampleExtension<TScalar> : IOrchestrationExtension
        where TScalar : unmanaged
    {
        int PrepareSample(in TimelineContentClip clip, int clipIndex,
            ITimelineRuntimeSourceTimeMapping<TScalar> sourceTimeMapping);
    }
}
