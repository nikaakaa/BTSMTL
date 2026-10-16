using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    public abstract class TimelineContentParameters
    {
        [ValueStorageIgnore]
        public abstract string ContractKind { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class TimelineContentTree : TimelineContentParameters
    {
        public TimelineContentTree(bool hasDeclaredEndTime) => HasDeclaredEndTime = hasDeclaredEndTime;
        [ValueStorageIgnore]
        public override string ContractKind => TimelineContractKinds.TreeClip;
        public bool HasDeclaredEndTime { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class TimelineContentSceneParameter : TimelineContentParameters
    {
        public TimelineContentSceneParameter(string targetBindingId, string parameterBindingId,
            TimelineBindingValueKind valueKind)
        {
            TargetBindingId = targetBindingId;
            ParameterBindingId = parameterBindingId;
            ValueKind = valueKind;
        }

        public string TargetBindingId { get; }
        [ValueStorageIgnore]
        public override string ContractKind => TimelineContractKinds.ScenePresentationParameterCurveClip;
        public string ParameterBindingId { get; }
        public TimelineBindingValueKind ValueKind { get; }
    }
}
