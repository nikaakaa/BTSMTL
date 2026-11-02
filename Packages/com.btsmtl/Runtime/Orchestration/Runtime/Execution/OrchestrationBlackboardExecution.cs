using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        public void ReadBlackboard(int operationIndex, int valueSlot, ref TValue destination)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            OrchestrationBlackboardOwnerToken expected = ResolveOwner(operationIndex, valueSlot, out _);
            int overrideIndex = m_Host.FindOverride(ref this, operationIndex, valueSlot,
                OrchestrationValueOverrideKind.Blackboard, in expected);
            if (overrideIndex >= 0)
                destination = m_Input.Overrides[overrideIndex].Value;
            else if (slot.IsGraphCallParameter && IsGraphScopeActive(slot.OwnerOperationIndex) ||
                SameOwner(in m_Blackboards.ReadMetadata(valueSlot).Owner, in expected))
                m_Blackboards.ReadValue(valueSlot, ref destination);
            else
                destination = m_Program.DefaultValue(valueSlot);
        }

        public void WriteBlackboard(int operationIndex, int valueSlot, in TValue value,
            OrchestrationAssignmentOpcode assignment = OrchestrationAssignmentOpcode.Set)
        {
            m_CurrentOperationIndex = operationIndex;
            OrchestrationBlackboardOwnerToken expected = ResolveOwner(operationIndex, valueSlot,
                out OrchestrationBlackboardSourceContext source);
            WriteResolvedBlackboard(operationIndex, valueSlot, in value, assignment, in expected, in source);
        }

        void WriteResolvedBlackboard(int operationIndex, int valueSlot, in TValue value,
            OrchestrationAssignmentOpcode assignment, in OrchestrationBlackboardOwnerToken expected,
            in OrchestrationBlackboardSourceContext source)
        {
            TValue current = default;
            if (SameOwner(in m_Blackboards.ReadMetadata(valueSlot).Owner, in expected))
                m_Blackboards.ReadValue(valueSlot, ref current);
            else
                current = m_Program.DefaultValue(valueSlot);
            int overrideIndex = m_Host.FindOverride(ref this, operationIndex, valueSlot,
                OrchestrationValueOverrideKind.Blackboard, in expected);
            if (overrideIndex >= 0)
                current = m_Input.Overrides[overrideIndex].Value;
            default(TNumerics).Assign(assignment, in current, in value, ref current);
            m_Blackboards.WriteValue(valueSlot, in current);
            ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(valueSlot);
            metadata.Revision = checked(metadata.Revision + 1);
            metadata.Owner = expected;
            m_Host.Trace(ref this, OrchestrationTraceKind.Blackboard, operationIndex,
                -1, default, 0, default, valueSlot, false, false, in source);
        }

        public OrchestrationValueOverrideAddress ReadBlackboardOverrideAddress(int operationIndex, int valueSlot)
        {
            OrchestrationBlackboardOwnerToken owner = ResolveOwner(operationIndex, valueSlot, out _);
            return BlackboardOverrideAddress(valueSlot, in owner);
        }

        OrchestrationValueOverrideAddress BlackboardOverrideAddress(int valueSlot,
            in OrchestrationBlackboardOwnerToken owner)
        {
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            bool character = slot.ScopeKind == OrchestrationScopeKind.Character;
            bool action = slot.ScopeKind == OrchestrationScopeKind.ActionInstance;
            ReadOnlySpan<OrchestrationExecutionFrameState> frame = character ? default : m_Blackboards.ReadBlackboardFrame(valueSlot);
            return new OrchestrationValueOverrideAddress
            {
                Kind = OrchestrationValueOverrideKind.Blackboard,
                Slot = character ? m_Blackboards.CharacterSlot(valueSlot) :
                    action ? m_Program.ExecutionStorage.Values[valueSlot].ValueIndex : valueSlot,
                Owner = character ? default : OrchestrationExecutionOwnerToken.Create(
                    in frame[0], action ? frame[0].OwnerOperationIndex : slot.OwnerOperationIndex, owner.Generation)
            };
        }

        public bool TryMutateBlackboard(int operationIndex, int valueSlot,
            in OrchestrationValueOverrideAddress address, in TValue value, OrchestrationValueMutation mutation)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            if (slot.Lifetime == OrchestrationBlackboardLifetime.Config)
                Fail(OrchestrationExecutionErrorCode.BlackboardReadOnly, operationIndex, valueSlot);
            if (slot.ScopeKind != OrchestrationScopeKind.Character &&
                !address.Owner.MatchesFrame(in m_Blackboards.ReadBlackboardFrame(valueSlot)[0]))
                return false;
            if (slot.ScopeKind == OrchestrationScopeKind.State && !IsActive(slot.OwnerOperationIndex) ||
                slot.ScopeKind == OrchestrationScopeKind.Graph &&
                slot.Lifetime != OrchestrationBlackboardLifetime.GraphInstance &&
                !IsGraphScopeActive(slot.OwnerOperationIndex))
                return false;
            OrchestrationBlackboardOwnerToken owner = ResolveOwner(operationIndex, valueSlot,
                out OrchestrationBlackboardSourceContext source);
            OrchestrationValueOverrideAddress actual = BlackboardOverrideAddress(valueSlot, in owner);
            if (!actual.Equals(in address))
                return false;
            if (mutation == OrchestrationValueMutation.Lock)
            {
                OrchestrationValueOverride<TValue>.Set(m_Input.Overrides, in address, in value,
                    OrchestrationValueOverrideMode.Locked);
            }
            else
            {
                OrchestrationValueOverride<TValue>.Release(m_Input.Overrides, in address);
                if (mutation == OrchestrationValueMutation.Set)
                    WriteResolvedBlackboard(operationIndex, valueSlot, in value,
                        OrchestrationAssignmentOpcode.Set, in owner, in source);
            }
            return true;
        }

        OrchestrationBlackboardOwnerToken ResolveOwner(int operationIndex, int valueSlot,
            out OrchestrationBlackboardSourceContext source)
        {
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            source = default;
            ulong generation;
            switch (slot.ScopeKind)
            {
                case OrchestrationScopeKind.Character:
                    generation = 1;
                    break;
                case OrchestrationScopeKind.Graph:
                    if (slot.Lifetime == OrchestrationBlackboardLifetime.GraphInstance && slot.OwnerOperationIndex < 0)
                        generation = m_Blackboards.OwnerGeneration(valueSlot);
                    else if (slot.Lifetime == OrchestrationBlackboardLifetime.Config)
                        generation = 1;
                    else
                    {
                        generation = slot.Lifetime == OrchestrationBlackboardLifetime.GraphInstance &&
                            !m_Program.Operation(slot.OwnerOperationIndex).IsRunnable
                            ? m_Blackboards.OwnerGeneration(valueSlot)
                            : ReadGeneration(slot.OwnerOperationIndex);
                    }
                    break;
                case OrchestrationScopeKind.State:
                    generation = ReadGeneration(slot.OwnerOperationIndex);
                    break;
                case OrchestrationScopeKind.ActionInstance:
                    source = ReadBlackboardFacts(operationIndex, m_Blackboards.ReadMetadata(valueSlot).Owner.Generation);
                    generation = source.InstanceId;
                    break;
                case OrchestrationScopeKind.Frame:
                    generation = m_Blackboards.FrameTick(valueSlot, m_Input.Tick);
                    break;
                default:
                    Fail(OrchestrationExecutionErrorCode.BlackboardScope, operationIndex, valueSlot, (int)slot.ScopeKind);
                    generation = 0;
                    break;
            }
            return new OrchestrationBlackboardOwnerToken
            {
                ScopeKind = slot.ScopeKind,
                CompiledOwnerIndex = slot.CompiledOwnerIndex,
                Generation = generation
            };
        }

        OrchestrationBlackboardSourceContext ReadBlackboardFacts(int operationIndex, ulong currentOwnerGeneration)
        {
            int bindingIndex = m_Program.Operation(operationIndex).BlackboardFactsBindingIndex;
            int depth = EnterInputRow(operationIndex);
            TNumerics values = default;
            Span<TValue> inputs = BorrowInputs(operationIndex, depth, 1);
            inputs[0] = values.FromUInt64(currentOwnerGeneration);
            Span<TValue> outputs = BorrowOutputs(operationIndex, depth, 4);
            try
            {
                var invocation = new OrchestrationExtensionArguments<TValue>(
                    OrchestrationExtensionInvocationKind.Evaluate, operationIndex, ReadGeneration(operationIndex), m_Input.Tick, in CurrentStopContext(),
                    StateAncestors(operationIndex),
                    ReadExecutionFrame(operationIndex),
                    inputs, outputs, in m_TimelineInvocation);
                RequireSynchronousValueResult(operationIndex, bindingIndex,
                    m_Host.InvokeReadOnly(bindingIndex, in invocation));
                return new OrchestrationBlackboardSourceContext
                {
                    InstanceId = values.UInt64(in outputs[0]),
                    SkillContentId = values.Identity(in outputs[1]).Index,
                    SkillExecutionGeneration = values.UInt64(in outputs[2]),
                    SegmentGeneration = values.UInt64(in outputs[3])
                };
            }
            finally
            {
                ReleaseInputs();
            }
        }

        static bool SameOwner(in OrchestrationBlackboardOwnerToken left,
            in OrchestrationBlackboardOwnerToken right) => left.ScopeKind == right.ScopeKind &&
            left.CompiledOwnerIndex == right.CompiledOwnerIndex && left.Generation == right.Generation;
    }
}
