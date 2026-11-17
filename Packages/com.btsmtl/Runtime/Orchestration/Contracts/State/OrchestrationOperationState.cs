namespace BTSMTL.Orchestration
{
    public struct OrchestrationOperationState
    {
        public OrchestrationOperationStatus Status;
        public int ChildCursor;
        public ulong Generation;
    }

    public struct OrchestrationStateMachineState
    {
        public int ActiveStateIdentityId;
        public int ExitingStateIdentityId;
    }

    [System.Flags]
    public enum OrchestrationOperationStateFields : byte
    {
        None = 0,
        Core = 1,
        StopContext = 2,
        StateMachine = 4,
        Timeline = 8
    }

    public static class OrchestrationOperationStateRequirements
    {
        public static OrchestrationOperationStateFields For(OrchestrationOperationCode code, bool isRunnable)
        {
            OrchestrationOperationStateFields fields = isRunnable
                ? OrchestrationOperationStateFields.Core | OrchestrationOperationStateFields.StopContext
                : OrchestrationOperationStateFields.None;
            return fields | (code switch
            {
                OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.Loop or
                    OrchestrationOperationCode.Parallel or OrchestrationOperationCode.Cycle =>
                    OrchestrationOperationStateFields.Core,
                OrchestrationOperationCode.Sequence or OrchestrationOperationCode.Selector or
                    OrchestrationOperationCode.State =>
                    OrchestrationOperationStateFields.Core | OrchestrationOperationStateFields.StopContext,
                OrchestrationOperationCode.StateMachine => OrchestrationOperationStateFields.Core |
                    OrchestrationOperationStateFields.StopContext | OrchestrationOperationStateFields.StateMachine,
                OrchestrationOperationCode.Timeline => OrchestrationOperationStateFields.Core |
                    OrchestrationOperationStateFields.StopContext | OrchestrationOperationStateFields.Timeline,
                _ => OrchestrationOperationStateFields.None
            });
        }
    }

    public readonly struct OrchestrationOperationStateIndices
    {
        public const ushort NoIndex = ushort.MaxValue;

        public OrchestrationOperationStateIndices(ushort stopContext, ushort stateMachine, ushort timeline)
        {
            StopContext = stopContext;
            StateMachine = stateMachine;
            Timeline = timeline;
        }

        public ushort StopContext { get; }
        public ushort StateMachine { get; }
        public ushort Timeline { get; }
    }

    public readonly struct OrchestrationOperationStateStorage
    {
        public OrchestrationOperationStateStorage(int stopContexts, int stateMachines, int timelines)
        {
            StopContexts = stopContexts;
            StateMachines = stateMachines;
            Timelines = timelines;
        }

        public int StopContexts { get; }
        public int StateMachines { get; }
        public int Timelines { get; }
    }
}
