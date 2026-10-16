using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineLogicSampleExtension
    {
        public static class InputSchema
        {
            public static readonly OrchestrationExtensionPort Weight = new("weight", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, 0);
            public static readonly OrchestrationExtensionPort PreviousSourceTime = new("previous-source-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, 1);
            public static readonly OrchestrationExtensionPort SourceTime = new("source-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, 2);
            public static readonly OrchestrationExtensionPort NormalizedTime = new("normalized-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, 3);
            public static readonly OrchestrationExtensionPort Cycle = new("cycle", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, 4);
            public static readonly OrchestrationExtensionPort PlaybackIdentity = new("playback-identity", OrchestrationValueKind.UInt64, OrchestrationExtensionPortDirection.Input, 5);
            public static readonly OrchestrationExtensionPort PlaybackSlot = new("playback-slot", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, 6);
            public static readonly OrchestrationExtensionPort AdvancePreviousTime = new("advance-previous-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, 7);
            public static readonly OrchestrationExtensionPort AdvancePreviousCycle = new("advance-previous-cycle", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, 8);
            public static readonly OrchestrationExtensionPort AdvanceTime = new("advance-time", OrchestrationValueKind.Scalar, OrchestrationExtensionPortDirection.Input, 9);
            public static readonly OrchestrationExtensionPort AdvanceCycle = new("advance-cycle", OrchestrationValueKind.Int32, OrchestrationExtensionPortDirection.Input, 10);
        }

        public static int InputCount => Contract.Ports.Length;

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
