using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using ThirdPersonGameplay.Orchestration;

namespace ThirdPersonSimulation
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class TimelineContentMotion : TimelineContentParameters
    {
        public const string Kind = "motion-curve.clip";
        public readonly struct MotionCurveSample
        {
            public static readonly OrchestrationExtensionDefinition<MotionCurveSample> Definition = new(
                new OrchestrationExtensionIdentity(GameplayMotionExtensionContracts.DomainId,
                    GameplayMotionExtensionContracts.MotionCurveSampleOperationId), 2,
                TimelineLogicSampleExtension.Contract);
        }

        public TimelineContentMotion(string resourceId, string curveId, SimulationMotionContributionSpace space,
            SimulationMotionChannel channel, SimulationMotionBlendMode blendMode, int priority, bool consumeLowerChannels,
            TimelineContentCurve x, TimelineContentCurve y, TimelineContentCurve z, TimelineContentCurve yaw)
        {
            ResourceId = resourceId;
            CurveId = curveId;
            Space = space;
            Channel = channel;
            BlendMode = blendMode;
            Priority = priority;
            ConsumeLowerChannels = consumeLowerChannels;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        [ValueStorageIgnore]
        public override string ContractKind => Kind;
        public string ResourceId { get; }
        public string CurveId { get; }
        public SimulationMotionContributionSpace Space { get; }
        public SimulationMotionChannel Channel { get; }
        public SimulationMotionBlendMode BlendMode { get; }
        public int Priority { get; }
        public bool ConsumeLowerChannels { get; }
        public TimelineContentCurve X { get; }
        public TimelineContentCurve Y { get; }
        public TimelineContentCurve Z { get; }
        public TimelineContentCurve Yaw { get; }
    }
}
