using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineLogicSampleExtension
    {
        public static class Ports
        {
            public static readonly OrchestrationExtensionInputPort Weight = new("weight", OrchestrationValueKind.Scalar, 0);
            public static readonly OrchestrationExtensionInputPort PreviousSourceTime = new("previous-source-time", OrchestrationValueKind.Scalar, 1);
            public static readonly OrchestrationExtensionInputPort SourceTime = new("source-time", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionInputPort NormalizedTime = new("normalized-time", OrchestrationValueKind.Scalar, 3);
            public static readonly OrchestrationExtensionInputPort Cycle = new("cycle", OrchestrationValueKind.Int32, 4);
            public static readonly OrchestrationExtensionInputPort PlaybackIdentity = new("playback-identity", OrchestrationValueKind.UInt64, 5);
            public static readonly OrchestrationExtensionInputPort PlaybackSlot = new("playback-slot", OrchestrationValueKind.Int32, 6);
            public static readonly OrchestrationExtensionInputPort AdvancePreviousTime = new("advance-previous-time", OrchestrationValueKind.Scalar, 7);
            public static readonly OrchestrationExtensionInputPort AdvancePreviousCycle = new("advance-previous-cycle", OrchestrationValueKind.Int32, 8);
            public static readonly OrchestrationExtensionInputPort AdvanceTime = new("advance-time", OrchestrationValueKind.Scalar, 9);
            public static readonly OrchestrationExtensionInputPort AdvanceCycle = new("advance-cycle", OrchestrationValueKind.Int32, 10);
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
            new[]
            {
                Ports.Weight.Declaration,
                Ports.PreviousSourceTime.Declaration,
                Ports.SourceTime.Declaration,
                Ports.NormalizedTime.Declaration,
                Ports.Cycle.Declaration,
                Ports.PlaybackIdentity.Declaration,
                Ports.PlaybackSlot.Declaration,
                Ports.AdvancePreviousTime.Declaration,
                Ports.AdvancePreviousCycle.Declaration,
                Ports.AdvanceTime.Declaration,
                Ports.AdvanceCycle.Declaration
            }, System.ReadOnlyMemory<OrchestrationExtensionContentField>.Empty);

        public OrchestrationExtensionReference CreateReference(int contentIndex, string sourcePath) =>
            new OrchestrationExtensionDefinition(Identity, SemanticVersion, Contract).CreateReference(contentIndex, sourcePath);
    }

    public interface ITimelineLogicSampleExtension<TScalar> : IOrchestrationExtension
        where TScalar : unmanaged
    {
        int PrepareSample(in TimelineContentClip clip, int clipIndex,
            ITimelineRuntimeSourceTimeMapping<TScalar> sourceTimeMapping);
    }
}
