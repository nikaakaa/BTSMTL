using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
namespace ThirdPersonSimulation.FPGameplay
{
    [ValueOperations]
    public readonly partial struct FPActionReplayIdentity
    {
        public FPActionReplayIdentity(CharacterSkillId ability, string action, string entry,
            CanonicalTextValue request, ulong sequence, SimulationExecutionSource source)
        {
            Ability = ability;
            Action = action;
            Entry = entry;
            Request = request;
            Sequence = sequence;
            Source = source;
        }

        public CharacterSkillId Ability { get; }
        public string Action { get; }
        public string Entry { get; }
        public CanonicalTextValue Request { get; }
        public ulong Sequence { get; }
        public SimulationExecutionSource Source { get; }

        internal static FPActionReplayIdentity Capture(in FPActionInstanceState action) =>
            new(action.SkillId, action.ActionId, action.ActivationEntryId,
                action.SourceInputRequestId, action.InputSequence, action.Source);

        internal bool Matches(in FPActionInstanceState action) =>
            action.IsActive && action.SkillId == Ability && action.ActionId == Action &&
            action.ActivationEntryId == Entry && action.SourceInputRequestId == Request &&
                action.InputSequence == Sequence && action.Source == Source;
    }

    public enum FPCharacterHistoryRecordKind : byte
    {
        Input,
        BlackboardWrite,
        PoseControlInput,
        RuntimeIngress,
        AnimationVariable,
        PortInput,
        AnimationPort,
        TimelineTimeEdit,
        InputDisplay
    }

    [ValueOperations]
    public readonly partial struct FPBlackboardWriteRecord
    {
        public FPBlackboardWriteRecord(ActorId actor, ulong tick, CharacterSkillId ability,
            ulong actionInstanceId, ulong skillGeneration, int stateSlot, AbilityStateValue value,
            FPActionReplayIdentity origin, FPPortInputMutation mutation)
        {
            Actor = actor;
            Tick = tick;
            Ability = ability;
            ActionInstanceId = actionInstanceId;
            SkillGeneration = skillGeneration;
            StateSlot = stateSlot;
            Value = value;
            Origin = origin;
            Mutation = mutation;
        }

        public ActorId Actor { get; }
        public ulong Tick { get; }
        public CharacterSkillId Ability { get; }
        public ulong ActionInstanceId { get; }
        public ulong SkillGeneration { get; }
        public int StateSlot { get; }
        public AbilityStateValue Value { get; }
        public FPActionReplayIdentity Origin { get; }
        public FPPortInputMutation Mutation { get; }

    }

    [ValueOperations]
    public readonly partial struct FPBlackboardWriteCommand
    {
        public FPBlackboardWriteCommand(GameplayAbilityExecutionIdentity ability, ulong actionInstanceId,
            ulong skillGeneration, int stateSlot, BlackboardOwnerToken owner, AbilityStateValue value,
            FPPortInputMutation mutation = FPPortInputMutation.Assign)
        {
            Ability = ability;
            ActionInstanceId = actionInstanceId;
            SkillGeneration = skillGeneration;
            StateSlot = stateSlot;
            Owner = owner;
            Value = value;
            Mutation = mutation;
        }

        public GameplayAbilityExecutionIdentity Ability { get; }
        public ulong ActionInstanceId { get; }
        public ulong SkillGeneration { get; }
        public int StateSlot { get; }
        public BlackboardOwnerToken Owner { get; }
        public AbilityStateValue Value { get; }
        public FPPortInputMutation Mutation { get; }
    }

    public enum FPBlackboardWriteStatus : byte
    {
        Applied = 1,
        InstanceEnded = 2,
        ScopeEnded = 3,
        ScopeChanged = 4
    }

    public readonly struct FPBlackboardWriteResult
    {
        internal FPBlackboardWriteResult(ulong sequence, FPBlackboardWriteCommand command, ulong tick,
            FPBlackboardWriteStatus status)
        {
            Sequence = sequence;
            Command = command;
            Tick = tick;
            Status = status;
        }

        public ulong Sequence { get; }
        public FPBlackboardWriteCommand Command { get; }
        public ulong Tick { get; }
        public FPBlackboardWriteStatus Status { get; }
        public void Release() => Command.Release();
    }
}
