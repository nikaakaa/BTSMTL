using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedBlackboardRuntime : FixedOperationModule, IFixedBlackboardPort
    {
        readonly FixedStatePort m_State;
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly IFixedActionContextReader m_Actions;
        readonly FixedTraceSink m_Trace;

        public FixedBlackboardRuntime(
            FixedGameplayAbilityExecutionAccess access,
            FixedStatePort state,
            FixedAbilityExecutionFrame frame,
            IFixedActionContextReader actions,
            FixedTraceSink trace)
            : base(access)
        {
            m_State = state;
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            m_Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public void WriteGraphCallParameter(int valueSlot, AbilityStateValue value)
        {
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(valueSlot);
            if (value.Kind != m_Ability.StateSlots[valueSlot].ValueKind)
                throw new InvalidOperationException($"Graph call parameter state '{valueSlot}' expects '{m_Ability.StateSlots[valueSlot].ValueKind}', received '{value.Kind}'.");
            m_State.Set(group.Value, value);
        }

        public AbilityStateValue ReadGraphCallParameter(int valueSlot)
        {
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(valueSlot);
            return m_State.Get(group.Value);
        }

        public void ResetGraphCallParameter(int valueSlot)
        {
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(valueSlot);
            m_State.Reset(group.Value);
        }

        public void WriteInputProjection(int valueSlot, AbilityStateValue value)
        {
#if UNITY_EDITOR
            FixedCharacterEvaluationOutput output = m_Frame.TraceOutput;
            long projectionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(valueSlot);
            var owner = new BlackboardOwnerToken(ProgramScopeKind.Character, group.CompiledOwnerIndex, 1);
            if (m_State.Get(group.OwnerToken).BlackboardOwnerToken != owner)
                MaterializeGroup(in group, owner);
#if UNITY_EDITOR
            long projectionFinished = System.Diagnostics.Stopwatch.GetTimestamp();
            output.EvaluationInputProjectionOwnerElapsedStopwatchTicks += projectionFinished - projectionStarted;
            projectionStarted = projectionFinished;
#endif
            m_State.Set(group.Value, value);
#if UNITY_EDITOR
            projectionFinished = System.Diagnostics.Stopwatch.GetTimestamp();
            output.EvaluationInputProjectionValueElapsedStopwatchTicks += projectionFinished - projectionStarted;
            projectionStarted = projectionFinished;
#endif
            m_State.Set(group.WriteStamp, AbilityStateValue.FromBlackboardWriteStamp(new BlackboardWriteStamp(
                m_Layout.RootOperation,
                m_Frame.Tick.Value,
                default,
                OperationHandle.Invalid,
                OperationHandle.Invalid,
                0)));
#if UNITY_EDITOR
            output.EvaluationInputProjectionStampElapsedStopwatchTicks +=
                System.Diagnostics.Stopwatch.GetTimestamp() - projectionStarted;
#endif
        }

        public AbilityStateValue Read<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            int valueSlot)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(valueSlot);
            if (BlackboardScopeOwnerCalculation.IsActiveGraphCallInput(cursor, m_Ability.GraphCallFrames, valueSlot))
                return m_State.Get(group.Value);
            BlackboardOwnerToken expected = ResolveBlackboardOwnerToken(cursor, operation, in group, out _);
            if (m_State.Get(group.OwnerToken).BlackboardOwnerToken != expected)
                return DefaultValue(in group);
            return m_State.Get(valueSlot);
        }

        public void Write<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            int valueSlot,
            AbilityStateValue value)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(valueSlot);
            ProgramBlackboardLifetime lifetime = group.LifetimeKind;
            if (lifetime == ProgramBlackboardLifetime.Config)
                throw new InvalidOperationException($"Blackboard config '{m_Ability.StateSlots[valueSlot].OwnerIdentity}' is read-only.");
            BlackboardOwnerToken expected = ResolveBlackboardOwnerToken(cursor, operation, in group, out FixedActionInstanceState action);
            StoreValue(in group, value, expected, BuildBlackboardWriteStamp(operation, in action));
            if (m_Trace.CaptureBlackboard && !cursor.IsPredictiveEvaluation)
                m_Trace.AddBlackboard(operation, valueSlot, value, expected, group.LifetimeKind);
        }

        void StoreValue(in SimulationBlackboardSlotGroup group, AbilityStateValue value,
            BlackboardOwnerToken expected, BlackboardWriteStamp stamp)
        {
            BlackboardOwnerToken current = m_State.Get(group.OwnerToken).BlackboardOwnerToken;
            if (current != expected)
            {
                if (group.Scope.Kind == ProgramScopeKind.ActionInstance && current.IsValid && m_Actions.ContainsInstance(current.Generation))
                {
                    throw new InvalidOperationException(
                        $"ActionInstance Blackboard scope '{group.Scope.Identity}' cannot bind active instances '{current.Generation}' and '{expected.Generation}' to one state address.");
                }
                MaterializeGroup(in group, expected);
            }
            m_State.Set(group.Value, value);
            m_State.Set(group.WriteStamp, AbilityStateValue.FromBlackboardWriteStamp(stamp));
        }

        internal FixedBlackboardWriteStatus ApplyCommand<TTarget>(OperationControlCursor<TTarget> cursor,
            FixedBlackboardWriteCommand command, ulong sequence, FixedActionInstanceState action)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(command.StateSlot);
            ProgramScopeLayout scope = group.Scope;
            ulong generation;
            switch (scope.Kind)
            {
                case ProgramScopeKind.Character:
                    generation = 1;
                    break;
                case ProgramScopeKind.Graph:
                case ProgramScopeKind.State:
                    if (group.LifetimeKind == ProgramBlackboardLifetime.Config)
                    {
                        generation = 1;
                        break;
                    }
                    if (!cursor.IsActive(scope.OwnerOperation))
                        return FixedBlackboardWriteStatus.ScopeEnded;
                    if (scope.Kind == ProgramScopeKind.State)
                    {
                        OperationHandle machine = m_Frame.Layout.Topology.StateMachineOwner(scope.OwnerOperation);
                        string state = m_Frame.Layout.Topology.OperationIdentity(scope.OwnerOperation);
                        int active = m_Frame.Layout.FindOperationStateSlot(machine, ProgramStateSemantic.StateMachineActive);
                        int exiting = m_Frame.Layout.FindOperationStateSlot(machine, ProgramStateSemantic.StateMachineExiting);
                        if (m_Frame.ReadState(active).Identity != state && m_Frame.ReadState(exiting).Identity != state)
                            return FixedBlackboardWriteStatus.ScopeEnded;
                    }
                    generation = cursor.ReadGeneration(scope.OwnerOperation);
                    break;
                case ProgramScopeKind.ActionInstance:
                    generation = action.InstanceId;
                    break;
                case ProgramScopeKind.Frame:
                    generation = m_Frame.Tick.Value;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Blackboard scope '{scope.Kind}'.");
            }
            if (scope.Kind != ProgramScopeKind.Frame && generation != command.Owner.Generation)
                return FixedBlackboardWriteStatus.ScopeChanged;
            var owner = new BlackboardOwnerToken(scope.Kind, group.CompiledOwnerIndex, generation);
            if (command.Mutation == FixedPortInputMutation.Release)
                return FixedBlackboardWriteStatus.Applied;
            var stamp = new BlackboardWriteStamp(OperationHandle.Invalid, m_Frame.Tick.Value,
                action.IsActive ? action.SourceContext : default, OperationHandle.Invalid, OperationHandle.Invalid, 0, sequence);
            SimulationExecutionSource source = SimulationExecutionSource.FromBlackboardCommand(group.Declaration.Identity);
            StoreValue(in group, command.Value, owner, stamp);
            m_Trace.AddBlackboardCommand(source, sequence, command.StateSlot, command.Value, owner, group.LifetimeKind);
            return FixedBlackboardWriteStatus.Applied;
        }

        BlackboardOwnerToken ResolveBlackboardOwnerToken<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            in SimulationBlackboardSlotGroup group,
            out FixedActionInstanceState action)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            action = group.Scope.Kind == ProgramScopeKind.ActionInstance
                ? ResolveBlackboardActionContext(operation) : default;
            return BlackboardScopeOwnerCalculation.Resolve(cursor, operation.Handle, in group,
                Access.Services, m_Frame.Tick.Value, action.IsActive, action.InstanceId);
        }

        FixedActionInstanceState ResolveBlackboardActionContext(SimulationOperation operation)
        {
            if (m_Actions.TryGetCurrentSkillExecution(out FixedActionInstanceState skillAction))
            {
                string skillContext = GetStringConstant(operation, OperationNamedConstant.FactContext, string.Empty);
                if (!CanonicalTextValue.IsNullOrEmpty(skillContext) &&
                    !CanonicalTextValue.Equals(skillContext, skillAction.ContextId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Skill operation '{SourcePath(operation)}' Action Context does not match its Action instance.");
                return skillAction;
            }

            CanonicalTextValue contextId = GetStringConstant(operation, OperationNamedConstant.FactContext, string.Empty);
            if (!CanonicalTextValue.IsNullOrEmpty(contextId))
            {
                int slot = m_Actions.FindActive(contextId, out FixedActionInstanceState explicitAction);
                return slot >= 0 ? explicitAction : default;
            }

            return m_Actions.FindOnlyActive();
        }

        BlackboardWriteStamp BuildBlackboardWriteStamp(SimulationOperation operation, in FixedActionInstanceState action) =>
            new BlackboardWriteStamp(
                operation.Handle,
                m_Frame.Tick.Value,
                action.IsActive ? action.SourceContext : default,
                OperationHandle.Invalid,
                OperationHandle.Invalid,
                0);

        AbilityStateValue DefaultValue(in SimulationBlackboardSlotGroup group)
        {
            return Access.Services.Blackboards.DefaultValue(group.Value);
        }

        void MaterializeGroup(in SimulationBlackboardSlotGroup group, BlackboardOwnerToken ownerToken)
        {
            m_State.Set(group.Value, DefaultValue(in group));
            m_State.Set(group.OwnerToken, AbilityStateValue.FromBlackboardOwnerToken(ownerToken));
            m_State.Set(group.WriteStamp, AbilityStateValue.FromBlackboardWriteStamp(default));
        }
    }
}
