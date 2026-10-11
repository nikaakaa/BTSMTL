namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedActionReplayIdentity
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

        public void Retain() { Request.Retain(); Source.Retain(); }
        public void Release() { Request.Release(); Source.Release(); }

        internal static FixedActionReplayIdentity Capture(in FixedActionInstanceState action) =>
            new(action.SkillId, action.ActionId, action.ActivationEntryId,
                action.SourceInputRequestId, action.InputSequence, action.Source);

        internal bool Matches(in FixedActionInstanceState action) =>
            action.IsActive && action.SkillId == Ability && action.ActionId == Action &&
            action.ActivationEntryId == Entry && action.SourceInputRequestId == Request &&
            action.InputSequence == Sequence && action.Source == Source;

        public void Write(CanonicalWriter writer)
        {
            writer.WriteText(Ability.Value);
            writer.WriteText(Action);
            writer.WriteText(Entry);
            writer.WriteText(Request);
            writer.WriteUInt64(Sequence);
            SimulationExecutionSourceCodec.Write(writer, Source);
        }

        public static FixedActionReplayIdentity Read(CanonicalReader reader, System.Func<CanonicalReader, string> identity) =>
            new(new CharacterSkillId(identity(reader)), identity(reader), identity(reader),
                reader.ReadText(), reader.ReadUInt64(), SimulationExecutionSourceCodec.Read(reader, identity));
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

    public readonly struct FixedBlackboardWriteRecord
    {
        internal FixedBlackboardWriteRecord(ActorId actor, ulong tick, CharacterSkillId ability,
            ulong actionInstanceId, ulong skillGeneration, int stateSlot, AbilityStateValue value,
            FixedActionReplayIdentity origin)
        {
            Actor = actor;
            Tick = tick;
            Ability = ability;
            ActionInstanceId = actionInstanceId;
            SkillGeneration = skillGeneration;
            StateSlot = stateSlot;
            Value = value;
            Origin = origin;
        }

        public ActorId Actor { get; }
        public ulong Tick { get; }
        public CharacterSkillId Ability { get; }
        public ulong ActionInstanceId { get; }
        public ulong SkillGeneration { get; }
        public int StateSlot { get; }
        public AbilityStateValue Value { get; }
        public FixedActionReplayIdentity Origin { get; }

        public void Retain()
        {
            Actor.Value.Retain();
            Value.Retain();
            Origin.Retain();
        }

        public void Release()
        {
            Actor.Value.Release();
            Value.Release();
            Origin.Release();
        }

        public static void Write(CanonicalWriter writer, ActorId actor, in FixedBlackboardWriteResult result,
            in FixedActionReplayIdentity origin)
        {
            writer.WriteText(actor.Value);
            writer.WriteUInt64(result.Tick);
            writer.WriteText(result.Command.Ability.AbilityId.Value);
            writer.WriteUInt64(result.Command.ActionInstanceId);
            writer.WriteUInt64(result.Command.SkillGeneration);
            writer.WriteInt32(result.Command.StateSlot);
            FixedCharacterRuntimeStateCodec.WriteValue(writer, result.Command.Value);
            writer.WriteBoolean(result.Command.ActionInstanceId != 0);
            if (result.Command.ActionInstanceId != 0)
                origin.Write(writer);
        }

        public static FixedBlackboardWriteRecord Read(CanonicalReader reader, System.Func<CanonicalReader, string> identity)
        {
            var actor = new ActorId(reader.ReadText());
            ulong tick = reader.ReadUInt64();
            var ability = new CharacterSkillId(identity(reader));
            ulong action = reader.ReadUInt64();
            ulong generation = reader.ReadUInt64();
            int slot = reader.ReadInt32();
            AbilityStateValue value = FixedCharacterRuntimeStateCodec.ReadValue(reader, identity);
            FixedActionReplayIdentity origin = reader.ReadBoolean() ? FixedActionReplayIdentity.Read(reader, identity) : default;
            return new FixedBlackboardWriteRecord(actor, tick, ability, action, generation, slot, value, origin);
        }
    }

    public readonly struct FixedBlackboardWriteCommand
    {
        public FixedBlackboardWriteCommand(GameplayAbilityExecutionIdentity ability, ulong actionInstanceId,
            ulong skillGeneration, int stateSlot, BlackboardOwnerToken owner, AbilityStateValue value)
        {
            Ability = ability;
            ActionInstanceId = actionInstanceId;
            SkillGeneration = skillGeneration;
            StateSlot = stateSlot;
            Owner = owner;
            Value = value;
        }

        public GameplayAbilityExecutionIdentity Ability { get; }
        public ulong ActionInstanceId { get; }
        public ulong SkillGeneration { get; }
        public int StateSlot { get; }
        public BlackboardOwnerToken Owner { get; }
        public AbilityStateValue Value { get; }
        public void Retain() => Value.Retain();
        public void Release() => Value.Release();
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
