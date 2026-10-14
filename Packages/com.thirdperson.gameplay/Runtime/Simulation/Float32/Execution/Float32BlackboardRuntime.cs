using System;

namespace ThirdPersonSimulation
{
    internal sealed class Float32BlackboardRuntime : Float32OperationModule, IFloat32BlackboardPort
    {
        readonly Float32StatePort m_State;
        readonly Float32AbilityExecutionFrame m_Frame;
        readonly IFloat32ActionContextReader m_Actions;
        readonly Float32TraceSink m_Trace;

        public Float32BlackboardRuntime(
            Float32GameplayAbilityExecutionAccess access,
            Float32StatePort state,
            Float32AbilityExecutionFrame frame,
            IFloat32ActionContextReader actions,
            Float32TraceSink trace)
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
            ref readonly SimulationBlackboardSlotGroup group = ref Access.Services.BlackboardGroup(valueSlot);
            var owner = new BlackboardOwnerToken(ProgramScopeKind.Character, group.CompiledOwnerIndex, 1);
            if (m_State.Get(group.OwnerToken).BlackboardOwnerToken != owner)
                MaterializeGroup(in group, owner);
            m_State.Set(group.Value, value);
            m_State.Set(group.WriteStamp, AbilityStateValue.FromBlackboardWriteStamp(new BlackboardWriteStamp(
                m_Layout.RootOperation,
                m_Frame.Tick.Value,
                default,
                OperationHandle.Invalid,
                OperationHandle.Invalid,
                0)));
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
            BlackboardOwnerToken expected = ResolveBlackboardOwnerToken(cursor, operation, in group, out Float32ActionInstanceState action);
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

        BlackboardOwnerToken ResolveBlackboardOwnerToken<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            in SimulationBlackboardSlotGroup group,
            out Float32ActionInstanceState action)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            action = group.Scope.Kind == ProgramScopeKind.ActionInstance
                ? ResolveBlackboardActionContext(operation) : default;
            return BlackboardScopeOwnerCalculation.Resolve(cursor, operation.Handle, in group,
                Access.Services, m_Frame.Tick.Value, action.IsActive, action.InstanceId);
        }

        Float32ActionInstanceState ResolveBlackboardActionContext(SimulationOperation operation)
        {
            if (m_Actions.TryGetCurrentSkillExecution(out Float32ActionInstanceState skillAction))
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
                int slot = m_Actions.FindActive(contextId, out Float32ActionInstanceState explicitAction);
                return slot >= 0 ? explicitAction : default;
            }

            return m_Actions.FindOnlyActive();
        }

        BlackboardWriteStamp BuildBlackboardWriteStamp(SimulationOperation operation, in Float32ActionInstanceState action) =>
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
