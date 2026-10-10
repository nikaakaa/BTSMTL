using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public enum FixedPortInputMutation : byte { Assign, Override, Release, Trigger }

    public readonly struct FixedValuePortInputRecord
    {
        public FixedValuePortInputRecord(ActorId actor, CharacterSkillId ability, int operation, string port,
            ProgramValuePortDirection direction, AbilityStateValue value, FixedPortInputMutation mutation, ulong tick = 0)
        {
            Actor = actor; Ability = ability; Operation = operation; Port = port;
            Direction = direction; Value = value; Mutation = mutation; Tick = tick;
        }

        public ActorId Actor { get; }
        public CharacterSkillId Ability { get; }
        public int Operation { get; }
        public string Port { get; }
        public ProgramValuePortDirection Direction { get; }
        public AbilityStateValue Value { get; }
        public FixedPortInputMutation Mutation { get; }
        public ulong Tick { get; }
        public void Retain() { Actor.Value.Retain(); Value.Retain(); }
        public void Release() { Actor.Value.Release(); Value.Release(); }

        public void Write(CanonicalWriter writer, ulong tick)
        {
            writer.WriteText(Actor.Value); writer.WriteText(Ability.Value); writer.WriteInt32(Operation);
            writer.WriteText(Port); writer.WriteByte((byte)Direction); writer.WriteByte((byte)Mutation);
            writer.WriteUInt64(tick);
            if (Mutation is FixedPortInputMutation.Assign or FixedPortInputMutation.Override)
                FixedCharacterRuntimeStateCodec.WriteValue(writer, Value);
        }

        public static FixedValuePortInputRecord Read(CanonicalReader reader, Func<CanonicalReader, string> identity)
        {
            var actor = new ActorId(reader.ReadText());
            var ability = new CharacterSkillId(identity(reader));
            int operation = reader.ReadInt32();
            string port = identity(reader);
            var direction = (ProgramValuePortDirection)reader.ReadByte();
            var mutation = (FixedPortInputMutation)reader.ReadByte();
            ulong tick = reader.ReadUInt64();
            AbilityStateValue value = mutation is FixedPortInputMutation.Assign or FixedPortInputMutation.Override
                ? FixedCharacterRuntimeStateCodec.ReadValue(reader, identity) : default;
            return new FixedValuePortInputRecord(actor, ability, operation, port, direction, value, mutation, tick);
        }

        public void Apply(FixedValuePortInputs inputs)
        {
            if (Mutation == FixedPortInputMutation.Trigger) inputs.Pulse(Operation, Port, Direction);
            else inputs.Mutate(Operation, Port, Direction, Value, Mutation);
        }
    }

    public sealed class FixedValuePortInputs
    {
        readonly Dictionary<(int Operation, string Port, ProgramValuePortDirection Direction), int> m_Indices = new();
        readonly FixedGameplayAbilityExecutionData m_Data;
        readonly FixedValuePortInputBank m_Bank;
        readonly int[] m_DefaultOutputs;
        readonly List<(int Index, OperationHandle Operation, ProgramValuePortDirection Direction, ProgramControlFlowEdge[] Edges)> m_Triggers = new();

        internal FixedValuePortInputs(FixedGameplayAbilityExecutionData data, FixedValuePortInputBank bank)
        {
            m_Data = data;
            m_Bank = bank;
            m_DefaultOutputs = new int[data.Operations.Count];
            Array.Fill(m_DefaultOutputs, -1);
            foreach (ProgramSourceMapEntry source in data.SourceMap)
            {
                if (source.TargetKind != ProgramSourceTargetKind.OperationPort)
                    continue;
                if (source.ValuePortDirection == ProgramValuePortDirection.None)
                {
                    DeclareTrigger(source, ProgramValuePortDirection.Input);
                    DeclareTrigger(source, ProgramValuePortDirection.Output);
                    continue;
                }
                var key = (source.TargetIndex, source.CompiledPortId, source.ValuePortDirection);
                if (m_Indices.ContainsKey(key))
                    continue;
                int index = bank.Add();
                m_Indices.Add(key, index);
                if (source.ValuePortDirection == ProgramValuePortDirection.Output && m_DefaultOutputs[source.TargetIndex] == -1)
                    m_DefaultOutputs[source.TargetIndex] = index;
            }
        }

        void DeclareTrigger(ProgramSourceMapEntry source, ProgramValuePortDirection direction)
        {
            var key = (source.TargetIndex, source.PortId, direction);
            if (m_Indices.ContainsKey(key))
                return;
            int index = m_Bank.Add();
            m_Indices.Add(key, index);
            var handle = new OperationHandle(source.TargetIndex);
            ProgramControlFlowEdge[] edges = direction == ProgramValuePortDirection.Output
                ? m_Data.ControlFlow.Where(edge => edge.Source.Equals(handle) && edge.SourcePort == source.PortId).ToArray()
                : Array.Empty<ProgramControlFlowEdge>();
            m_Triggers.Add((index, handle, direction, edges));
        }

        public void Pulse(int operation, string port, ProgramValuePortDirection direction)
        {
            int index = m_Indices[(operation, port, direction)];
            m_Bank.TryRead(index, out AbilityStateValue current);
            m_Bank.Set(index, AbilityStateValue.FromInt32(current.Int32 + 1), true);
        }

        internal void ConsumeTriggers(FixedAbilityOperationControlRuntime control)
        {
            for (int i = 0; i < m_Triggers.Count; i++)
            {
                var trigger = m_Triggers[i];
                if (!m_Bank.TryRead(trigger.Index, out AbilityStateValue pending))
                    continue;
                m_Bank.Release(trigger.Index);
                for (int pulse = 0; pulse < pending.Int32; pulse++)
                    if (trigger.Direction == ProgramValuePortDirection.Input)
                        control.Tick(trigger.Operation);
                    else
                        for (int edge = 0; edge < trigger.Edges.Length; edge++)
                            control.TickEdge(trigger.Edges[edge]);
            }
        }

        public bool TryRead(int operation, string port, ProgramValuePortDirection direction, out AbilityStateValue value)
        {
            int index;
            bool declared = direction == ProgramValuePortDirection.Output && port.Length == 0
                ? (index = m_DefaultOutputs[operation]) != -1 : m_Indices.TryGetValue((operation, port, direction), out index);
            if (declared)
                return m_Bank.TryRead(index, out value);
            value = default;
            return false;
        }

        public bool IsLocked(int operation, string port, ProgramValuePortDirection direction) =>
            m_Bank.IsLocked(m_Indices[(operation, port, direction)]);

        public void Mutate(int operation, string port, ProgramValuePortDirection direction, AbilityStateValue value,
            FixedPortInputMutation mutation)
        {
            int index = m_Indices[(operation, port, direction)];
            if (mutation == FixedPortInputMutation.Release)
            {
                m_Bank.Release(index);
                return;
            }
            SimulationOperation node = m_Data.Operations[operation];
            var contract = GameplayAbilityValuePortContracts.Require(node.Code, node.Handle, m_Data.GraphCallFrames);
            OperationValuePortDefinition definition = direction == ProgramValuePortDirection.Input
                ? Find(contract.Inputs, port) : Find(contract.Outputs, port);
            SemanticValueKind kind = GameplayAbilityValuePortContracts.FromState(value.Kind);
            if (direction == ProgramValuePortDirection.Input)
                GameplayAbilityExecutionValueResolver.RequireInputKind(node, definition, kind, m_Data.References, m_Data.StateSlots);
            else if (GameplayAbilityExecutionValueResolver.ResolveOutputKind(node, definition, m_Data.Constants, m_Data.References, m_Data.StateSlots) != kind)
                throw new InvalidOperationException("端口输入值与正式端口类型不同。");
            m_Bank.Set(index, value, mutation == FixedPortInputMutation.Override);
        }

        static OperationValuePortDefinition Find(IReadOnlyList<OperationValuePortDefinition> ports, string identity)
        {
            for (int i = 0; i < ports.Count; i++)
                if (ports[i].Identity == identity)
                    return ports[i];
            throw new InvalidOperationException("端口不在当前正式程序中。");
        }
    }

    public sealed class FixedValuePortInputBank
    {
        enum InputState : byte { None, PendingOnce, Once, Locked }
        readonly Dictionary<(ActorId Actor, CharacterSkillId Ability), FixedValuePortInputs> m_Abilities = new();
        AbilityStateValue[] m_Values;
        InputState[] m_States;
        int m_Count;

        internal int Add() => m_Count++;
        public int TextCapacity => m_Count;
        public FixedValuePortInputs Declare(ActorId actor, FixedGameplayAbilityExecutionData data)
        {
            var inputs = new FixedValuePortInputs(data, this);
            m_Abilities.Add((actor, data.AbilityId), inputs);
            return inputs;
        }
        public FixedValuePortInputs Require(ActorId actor, CharacterSkillId ability) => m_Abilities[(actor, ability)];
        public void Prepare() { m_Values = new AbilityStateValue[m_Count]; m_States = new InputState[m_Count]; }
        internal bool TryRead(int index, out AbilityStateValue value) { value = m_Values[index]; return m_States[index] != InputState.None; }
        internal bool IsLocked(int index) => m_States[index] == InputState.Locked;
        internal void Set(int index, AbilityStateValue value, bool locked)
            => Store(index, value, locked ? InputState.Locked : InputState.PendingOnce);
        void Store(int index, AbilityStateValue value, InputState state)
        {
            value.Retain();
            m_Values[index].Release();
            m_Values[index] = value;
            m_States[index] = state;
        }
        internal void Release(int index) { m_Values[index].Release(); m_Values[index] = default; m_States[index] = InputState.None; }
        public void BeginTick()
        {
            for (int i = 0; i < m_Count; i++)
                if (m_States[i] == InputState.Once) Release(i);
                else if (m_States[i] == InputState.PendingOnce) m_States[i] = InputState.Once;
        }
        public void Clear() { for (int i = 0; i < m_Count; i++) Release(i); }
        public void Write(CanonicalWriter writer)
        {
            int count = 0;
            for (int i = 0; i < m_Count; i++) if (m_States[i] != InputState.None) count++;
            writer.WriteInt32(count);
            for (int i = 0; i < m_Count; i++)
                if (m_States[i] != InputState.None)
                {
                    writer.WriteInt32(i);
                    writer.WriteByte((byte)m_States[i]);
                    FixedCharacterRuntimeStateCodec.WriteValue(writer, m_Values[i]);
                }
        }
        public void Read(CanonicalReader reader)
        {
            Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int index = reader.ReadInt32();
                InputState state = (InputState)reader.ReadByte();
                Store(index, FixedCharacterRuntimeStateCodec.ReadValue(reader, null), state);
            }
        }
    }
}
