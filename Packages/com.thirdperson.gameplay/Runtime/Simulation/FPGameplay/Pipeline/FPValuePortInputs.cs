using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public enum FPPortInputMutation : byte { Assign, Override, Release, Trigger }

    [ValueOperations]
    public readonly partial struct FPValuePortInputRecord
    {
        public FPValuePortInputRecord(ActorId actor, CharacterSkillId ability, int operation, string port,
            ProgramValuePortDirection direction, AbilityStateValue value, FPPortInputMutation mutation, ulong tick = 0)
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
        public FPPortInputMutation Mutation { get; }
        public ulong Tick { get; }

        public void Apply(FPValuePortInputs inputs)
        {
            if (Mutation == FPPortInputMutation.Trigger) inputs.Pulse(Operation, Port, Direction);
            else inputs.Mutate(Operation, Port, Direction, Value, Mutation);
        }
    }

    [ValueSnapshot]
    public sealed partial class FPValuePortInputs : IProgramValuePortInputs<AbilityStateValue>, IProgramValuePortEdits<AbilityStateValue>
    {
        readonly Dictionary<(int Operation, string Port, ProgramValuePortDirection Direction), int> m_Indices = new();
        readonly Dictionary<(int Operation, int Port, ProgramValuePortDirection Direction), int> m_CompiledIndices = new();
        readonly FPGameplayAbilityExecutionData m_Data;
        readonly FPValuePortInputBank m_Bank;
        readonly int m_FirstSlot;
        readonly AbilityStateValue[] m_PreparedValues;
        readonly bool[] m_ConsumedTriggers;
        internal ActorId Actor { get; }
        internal int[] CharacterSlots { get; }
        internal IReadOnlyList<(ulong Sequence, FPBlackboardWriteCommand Command)> BlackboardCommands { get; set; }
        readonly int[] m_DefaultOutputs;
        readonly List<(int Index, OperationHandle Operation, ProgramValuePortDirection Direction, ProgramControlFlowEdge[] Edges)> m_Triggers = new();
        [ValueSnapshot] internal FPBlackboardInputOverrides BlackboardOverrides { get; }

        public bool IsBlackboardLocked(ulong action, ulong generation, int slot) =>
            m_Bank.IsBlackboardLocked(this, action, generation, slot);

        internal bool HasPendingBlackboardInput(FPBlackboardWriteCommand command) =>
            m_Bank.HasPendingBlackboardInput(this, command);

        internal void CommitBlackboardInput(FPBlackboardWriteCommand command, ulong sequence, ulong tick) =>
            m_Bank.CommitBlackboardInput(this, command, sequence, tick);

        internal GameplayAbilityExecutionIdentity BlackboardIdentity => new(m_Data.AbilityId, m_Data.ContentHash,
            m_Data.StateSchemaHash, m_Data.OperationSetVersion, m_Data.NumericProfile);

        internal FPValuePortInputs(ActorId actor, FPGameplayAbilityExecutionInstallation installation,
            FPValuePortInputBank bank, bool prepareBlackboardOverrides)
        {
            FPGameplayAbilityExecutionData data = installation.Data;
            m_Data = data;
            m_FirstSlot = bank.Count;
            Actor = actor;
            CharacterSlots = installation.CharacterBlackboardSlots;
            BlackboardOverrides = new FPBlackboardInputOverrides(data, prepareBlackboardOverrides);
            m_Bank = bank;
            m_DefaultOutputs = new int[data.Operations.Count];
            Array.Fill(m_DefaultOutputs, -1);
            foreach (ProgramExecutionSource source in data.ExecutionSources)
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
                SimulationOperation node = data.Operations[source.TargetIndex];
                OperationValuePortContract contract = GameplayAbilityValuePortContracts.Require(
                    node.Code, node.Handle, data.GraphCallFrames, node.Integer0);
                IReadOnlyList<OperationValuePortDefinition> ports = source.ValuePortDirection == ProgramValuePortDirection.Input
                    ? contract.Inputs : contract.Outputs;
                m_CompiledIndices.Add((source.TargetIndex, Find(ports, source.CompiledPortId).Order, source.ValuePortDirection), index);
                if (source.ValuePortDirection == ProgramValuePortDirection.Output && m_DefaultOutputs[source.TargetIndex] == -1)
                    m_DefaultOutputs[source.TargetIndex] = index;
            }
            m_PreparedValues = new AbilityStateValue[bank.Count - m_FirstSlot];
            m_ConsumedTriggers = new bool[m_Triggers.Count];
        }

        internal void PrepareEvaluation()
        {
            m_Bank.CaptureValues(m_FirstSlot, m_PreparedValues);
            Array.Clear(m_ConsumedTriggers, 0, m_ConsumedTriggers.Length);
        }

        internal void AdoptEvaluation()
        {
            for (int i = 0; i < m_ConsumedTriggers.Length; i++)
                if (m_ConsumedTriggers[i])
                    m_Bank.Release(m_Triggers[i].Index);
        }

        internal void DiscardEvaluation()
        {
            for (int i = 0; i < m_PreparedValues.Length; i++)
            {
                ref AbilityStateValue value = ref m_PreparedValues[i];
                if (value.Kind != default)
                    value.Release();
            }
            Array.Clear(m_PreparedValues, 0, m_PreparedValues.Length);
        }

        void DeclareTrigger(ProgramExecutionSource source, ProgramValuePortDirection direction)
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

        internal void ConsumeTriggers(FPAbilityOperationControlRuntime control)
        {
            for (int i = 0; i < m_Triggers.Count; i++)
            {
                var trigger = m_Triggers[i];
                AbilityStateValue pending = m_PreparedValues[trigger.Index - m_FirstSlot];
                if (pending.Kind == default)
                    continue;
                m_PreparedValues[trigger.Index - m_FirstSlot] = default;
                m_ConsumedTriggers[i] = true;
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

        public bool TryRead(OperationHandle operation, int portIndex, bool input, out AbilityStateValue value)
        {
            int index;
            ProgramValuePortDirection direction = input ? ProgramValuePortDirection.Input : ProgramValuePortDirection.Output;
            bool declared = !input && portIndex == -1
                ? (index = m_DefaultOutputs[operation.Value]) != -1
                : m_CompiledIndices.TryGetValue((operation.Value, portIndex, direction), out index);
            value = declared ? m_PreparedValues[index - m_FirstSlot] : default;
            return value.Kind != default;
        }

        public bool IsLocked(int operation, string port, ProgramValuePortDirection direction) =>
            m_Bank.IsLocked(m_Indices[(operation, port, direction)]);

        public bool TryReadInput(OperationHandle operation, int portIndex, bool input, out AbilityStateValue value)
        {
            ProgramValuePortDirection direction = input ? ProgramValuePortDirection.Input : ProgramValuePortDirection.Output;
            if (m_CompiledIndices.TryGetValue((operation.Value, portIndex, direction), out int index))
                return m_Bank.TryRead(index, out value);
            value = default;
            return false;
        }

        public bool IsLocked(OperationHandle operation, int portIndex, bool input) =>
            m_Bank.IsLocked(CompiledIndex(operation, portIndex, input));

        public void Release(OperationHandle operation, int portIndex, bool input) =>
            m_Bank.Release(CompiledIndex(operation, portIndex, input));

        public void Set(OperationHandle operation, int portIndex, bool input, AbilityStateValue value, bool locked)
        {
            SimulationOperation node = m_Data.Operations[operation.Value];
            OperationValuePortContract contract = GameplayAbilityValuePortContracts.Require(
                node.Code, node.Handle, m_Data.GraphCallFrames, node.Integer0);
            OperationValuePortDefinition definition = input ? contract.Inputs[portIndex] : contract.Outputs[portIndex];
            SetValue(node, definition, input ? ProgramValuePortDirection.Input : ProgramValuePortDirection.Output,
                CompiledIndex(operation, portIndex, input), value, locked);
        }

        int CompiledIndex(OperationHandle operation, int portIndex, bool input) =>
            m_CompiledIndices[(operation.Value, portIndex, input ? ProgramValuePortDirection.Input : ProgramValuePortDirection.Output)];

        public void Mutate(int operation, string port, ProgramValuePortDirection direction, AbilityStateValue value,
            FPPortInputMutation mutation)
        {
            int index = m_Indices[(operation, port, direction)];
            if (mutation == FPPortInputMutation.Release)
            {
                m_Bank.Release(index);
                return;
            }
            SimulationOperation node = m_Data.Operations[operation];
            var contract = GameplayAbilityValuePortContracts.Require(node.Code, node.Handle, m_Data.GraphCallFrames, node.Integer0);
            OperationValuePortDefinition definition = direction == ProgramValuePortDirection.Input
                ? Find(contract.Inputs, port) : Find(contract.Outputs, port);
            SetValue(node, definition, direction, index, value, mutation == FPPortInputMutation.Override);
        }

        void SetValue(SimulationOperation node, OperationValuePortDefinition definition,
            ProgramValuePortDirection direction, int index, AbilityStateValue value, bool locked)
        {
            SemanticValueKind kind = GameplayAbilityValuePortContracts.FromState(value.Kind);
            if (direction == ProgramValuePortDirection.Input)
                GameplayAbilityExecutionValueResolver.RequireInputKind(node, definition, kind, m_Data.References, m_Data.StateSlots);
            else if (GameplayAbilityExecutionValueResolver.ResolveOutputKind(node, definition, m_Data.Constants, m_Data.References, m_Data.StateSlots) != kind)
                throw new InvalidOperationException("端口输入值与正式端口类型不同。");
            m_Bank.Set(index, value, locked);
        }

        static OperationValuePortDefinition Find(IReadOnlyList<OperationValuePortDefinition> ports, string identity)
        {
            for (int i = 0; i < ports.Count; i++)
                if (ports[i].Identity == identity)
                    return ports[i];
            throw new InvalidOperationException("端口不在当前正式程序中。");
        }
    }

    [ValueSnapshot]
    public sealed partial class FPBlackboardInputOverrides
    {
        [ValueOperations, ValueStorage]
        public readonly partial struct Entry
        {
            internal Entry(FPBlackboardWriteCommand command, ulong sequence) { Command = command; Sequence = sequence; }
            internal readonly FPBlackboardWriteCommand Command;
            internal readonly ulong Sequence;
        }

        [ValueSnapshot] readonly Entry[] m_Entries;
        [ValueSnapshot] int m_Count;

        internal FPBlackboardInputOverrides(FPGameplayAbilityExecutionData data, bool prepare)
        {
            IsPrepared = prepare;
            int declarations = 0;
            foreach (ProgramStateSlot slot in data.StateSlots)
                if (slot.Semantic == ProgramStateSemantic.BlackboardValue) declarations++;
            m_Entries = prepare ? new Entry[checked(declarations * Math.Max(1, data.ExecutionLayout.ActionInstanceCapacity))]
                : Array.Empty<Entry>();
        }

        internal int Capacity => m_Entries.Length;
        internal bool IsPrepared { get; }
        internal int Count => m_Count;
        internal ref readonly Entry At(int index) => ref m_Entries[index];

        internal bool Contains(ulong action, ulong generation, int slot) => Find(action, generation, slot) >= 0;

        int Find(ulong action, ulong generation, int slot)
        {
            for (int index = 0; index < m_Count; index++)
                if (m_Entries[index].Command.ActionInstanceId == action && m_Entries[index].Command.SkillGeneration == generation &&
                    m_Entries[index].Command.StateSlot == slot) return index;
            return -1;
        }

        internal void Commit(FPBlackboardWriteCommand command, ulong sequence, ulong tick)
        {
            if (command.Mutation == FPPortInputMutation.Assign) return;
            int index = Find(command.ActionInstanceId, command.SkillGeneration, command.StateSlot);
            if (command.Mutation == FPPortInputMutation.Release)
            {
                if (index >= 0) Remove(index);
                return;
            }
            BlackboardOwnerToken owner = command.Owner.ScopeKind == ProgramScopeKind.Frame
                ? new BlackboardOwnerToken(ProgramScopeKind.Frame, command.Owner.CompiledOwnerIndex, tick) : command.Owner;
            var stored = new FPBlackboardWriteCommand(command.Ability, command.ActionInstanceId, command.SkillGeneration,
                command.StateSlot, owner, command.Value, FPPortInputMutation.Override);
            stored.Retain();
            if (index < 0)
            {
                if (m_Count == m_Entries.Length)
                    throw new InvalidOperationException("黑板覆盖输入超过已声明的实例容量。");
                index = m_Count++;
            }
            else m_Entries[index].Command.Release();
            m_Entries[index] = new Entry(stored, sequence);
        }

        internal void Expire(FPBlackboardWriteCommand command)
        {
            int index = Find(command.ActionInstanceId, command.SkillGeneration, command.StateSlot);
            if (index >= 0) Remove(index);
        }

        void Remove(int index)
        {
            m_Entries[index].Command.Release();
            m_Count--;
            Array.Copy(m_Entries, index + 1, m_Entries, index, m_Count - index);
            m_Entries[m_Count] = default;
        }

        internal void BeginTick(ulong tick)
        {
            for (int index = m_Count - 1; index >= 0; index--)
                if (m_Entries[index].Command.Owner.ScopeKind == ProgramScopeKind.Frame &&
                    m_Entries[index].Command.Owner.Generation != tick) Remove(index);
        }

        internal void Clear()
        {
            for (int index = m_Count - 1; index >= 0; index--) Remove(index);
        }

    }

    [ValueSnapshot]
    public sealed partial class FPValuePortInputBank
    {
        public enum InputState : byte { None, PendingOnce, Once, Locked }
        readonly Dictionary<(ActorId Actor, CharacterSkillId Ability), FPValuePortInputs> m_Abilities = new();
        [ValueSnapshot] readonly List<FPValuePortInputs> m_AbilityOrder = new();
        readonly Dictionary<(ActorId Actor, int Slot), (FPValuePortInputs Inputs, int Slot)> m_CharacterOwners = new();
        [ValueSnapshot] AbilityStateValue[] m_Values;
        [ValueSnapshot] InputState[] m_States;
        int m_Count;

        internal int Add() => m_Count++;
        internal int Count => m_Count;
        public FPValuePortInputs Declare(ActorId actor, FPGameplayAbilityExecutionInstallation installation, bool prepareBlackboardOverrides)
        {
            FPGameplayAbilityExecutionData data = installation.Data;
            var inputs = new FPValuePortInputs(actor, installation, this, prepareBlackboardOverrides);
            m_Abilities.Add((actor, data.AbilityId), inputs);
            m_AbilityOrder.Add(inputs);
            for (int slot = 0; slot < data.StateSlots.Count; slot++)
                if (data.StateSlots[slot].Semantic == ProgramStateSemantic.BlackboardValue && inputs.CharacterSlots[slot] >= 0)
                    m_CharacterOwners.TryAdd((actor, inputs.CharacterSlots[slot]), (inputs, slot));
            return inputs;
        }

        (FPValuePortInputs Inputs, int Slot) BlackboardOwner(FPValuePortInputs inputs, int slot) =>
            inputs.CharacterSlots[slot] >= 0 ? m_CharacterOwners[(inputs.Actor, inputs.CharacterSlots[slot])] : (inputs, slot);

        internal bool IsBlackboardLocked(FPValuePortInputs inputs, ulong action, ulong generation, int slot)
        {
            var owner = BlackboardOwner(inputs, slot);
            return owner.Inputs.BlackboardOverrides.Contains(action, generation, owner.Slot);
        }

        internal void CommitBlackboardInput(FPValuePortInputs inputs, FPBlackboardWriteCommand command, ulong sequence, ulong tick)
        {
            var owner = BlackboardOwner(inputs, command.StateSlot);
            var adopted = new FPBlackboardWriteCommand(owner.Inputs.BlackboardIdentity, command.ActionInstanceId,
                command.SkillGeneration, owner.Slot, command.Owner, command.Value, command.Mutation);
            owner.Inputs.BlackboardOverrides.Commit(adopted, sequence, tick);
        }

        internal bool HasPendingBlackboardInput(FPValuePortInputs inputs, FPBlackboardWriteCommand command)
        {
            var owner = BlackboardOwner(inputs, command.StateSlot);
            for (int ability = 0; ability < m_AbilityOrder.Count; ability++)
            {
                FPValuePortInputs candidate = m_AbilityOrder[ability];
                if (candidate.Actor != inputs.Actor) continue;
                IReadOnlyList<(ulong Sequence, FPBlackboardWriteCommand Command)> pending = candidate.BlackboardCommands;
                for (int index = 0; index < pending.Count; index++)
                {
                    FPBlackboardWriteCommand queued = pending[index].Command;
                    var target = BlackboardOwner(candidate, queued.StateSlot);
                    if (target.Inputs == owner.Inputs && target.Slot == owner.Slot && queued.ActionInstanceId == command.ActionInstanceId &&
                        queued.SkillGeneration == command.SkillGeneration) return true;
                }
            }
            return false;
        }
        public FPValuePortInputs Require(ActorId actor, CharacterSkillId ability) => m_Abilities[(actor, ability)];
        public void Prepare() { m_Values = new AbilityStateValue[m_Count]; m_States = new InputState[m_Count]; }
        internal bool TryRead(int index, out AbilityStateValue value) { value = m_Values[index]; return m_States[index] != InputState.None; }
        internal void CaptureValues(int firstSlot, AbilityStateValue[] destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                int index = firstSlot + i;
                if (m_States[index] == InputState.None)
                    continue;
                ref AbilityStateValue value = ref m_Values[index];
                value.Retain();
                destination[i] = value;
            }
        }
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
        public void BeginTick(ulong tick)
        {
            for (int index = 0; index < m_AbilityOrder.Count; index++) m_AbilityOrder[index].BlackboardOverrides.BeginTick(tick);
            for (int i = 0; i < m_Count; i++)
                if (m_States[i] == InputState.Once) Release(i);
                else if (m_States[i] == InputState.PendingOnce) m_States[i] = InputState.Once;
        }
        public void Clear()
        {
            for (int i = 0; i < m_Count; i++) Release(i);
            for (int index = 0; index < m_AbilityOrder.Count; index++) m_AbilityOrder[index].BlackboardOverrides.Clear();
        }
    }
}
