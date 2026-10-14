namespace ThirdPersonSimulation.Fixed
{
    [ValueOperations]
    public readonly partial struct FixedActionReplayIdentity
    {
        public FixedActionReplayIdentity(CharacterSkillId ability, string action, string entry,
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

        internal static FixedActionReplayIdentity Capture(in FixedActionInstanceState action) =>
            new(action.SkillId, action.ActionId, action.ActivationEntryId,
                action.SourceInputRequestId, action.InputSequence, action.Source);

        internal bool Matches(in FixedActionInstanceState action) =>
            action.IsActive && action.SkillId == Ability && action.ActionId == Action &&
            action.ActivationEntryId == Entry && action.SourceInputRequestId == Request &&
                action.InputSequence == Sequence && action.Source == Source;
    }

    public enum FixedCharacterHistoryRecordKind : byte
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
    public readonly partial struct FixedBlackboardWriteRecord
    {
        public FixedBlackboardWriteRecord(ActorId actor, ulong tick, CharacterSkillId ability,
            ulong actionInstanceId, ulong skillGeneration, int stateSlot, AbilityStateValue value,
            FixedActionReplayIdentity origin, FixedPortInputMutation mutation)
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
        public FixedActionReplayIdentity Origin { get; }
        public FixedPortInputMutation Mutation { get; }

    }

    [ValueOperations]
    public readonly partial struct FixedBlackboardWriteCommand
    {
        public FixedBlackboardWriteCommand(GameplayAbilityExecutionIdentity ability, ulong actionInstanceId,
            ulong skillGeneration, int stateSlot, BlackboardOwnerToken owner, AbilityStateValue value,
            FixedPortInputMutation mutation = FixedPortInputMutation.Assign)
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
        public FixedPortInputMutation Mutation { get; }
    }

    public enum FixedBlackboardWriteStatus : byte
    {
        Applied = 1,
        InstanceEnded = 2,
        ScopeEnded = 3,
        ScopeChanged = 4
    }

    public readonly struct FixedBlackboardWriteResult
    {
        internal FixedBlackboardWriteResult(ulong sequence, FixedBlackboardWriteCommand command, ulong tick,
            FixedBlackboardWriteStatus status)
        {
            Sequence = sequence;
            Command = command;
            Tick = tick;
            Status = status;
        }

        public ulong Sequence { get; }
        public FixedBlackboardWriteCommand Command { get; }
        public ulong Tick { get; }
        public FixedBlackboardWriteStatus Status { get; }
        public void Release() => Command.Release();
    }
}
