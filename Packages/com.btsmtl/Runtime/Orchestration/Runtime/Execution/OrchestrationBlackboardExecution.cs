using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TDiagnostics : struct, IOrchestrationExecutionDiagnostics
        where TStorage : struct, IOrchestrationValueStorage<TValue>
    {
        public void ReadBlackboard(int operationIndex, int valueSlot, ref TValue destination)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            OrchestrationBlackboardOwnerToken expected = ResolveOwner(operationIndex, valueSlot, out _);
            int overrideIndex = default(TDiagnostics).FindOverride(ref this, operationIndex, valueSlot,
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
            int overrideIndex = default(TDiagnostics).FindOverride(ref this, operationIndex, valueSlot,
                OrchestrationValueOverrideKind.Blackboard, in expected);
            if (overrideIndex >= 0)
                current = m_Input.Overrides[overrideIndex].Value;
            OrchestrationTypedValueMath<TValue, TScalar, TVector2, TVector3,
                TYaw, TQuaternion, TMath, TValues>.Assign(assignment, in current, in value,
                    ref current);
            m_Blackboards.WriteValue(valueSlot, in current);
            ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(valueSlot);
            metadata.Revision = checked(metadata.Revision + 1);
            metadata.Owner = expected;
            default(TDiagnostics).Trace(ref this, OrchestrationTraceKind.Blackboard, operationIndex,
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
            return new OrchestrationValueOverrideAddress
            {
                Kind = OrchestrationValueOverrideKind.Blackboard,
                Slot = character ? m_Blackboards.CharacterSlot(valueSlot) : valueSlot,
                Owner = character ? default : OrchestrationExecutionOwnerToken.Create(
                    in ReadExecutionFrame(slot.ExecutionFrameOwnerOperationIndex)[0],
                    slot.OwnerOperationIndex, owner.Generation)
            };
        }

        public bool TryMutateBlackboard(int operationIndex, int valueSlot,
            in OrchestrationValueOverrideAddress address, in TValue value, OrchestrationValueMutation mutation)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            if (slot.Lifetime == OrchestrationBlackboardLifetime.Config)
                throw new InvalidOperationException($"Blackboard slot '{valueSlot}' is read-only configuration.");
            if (slot.ScopeKind != OrchestrationScopeKind.Character &&
                !address.Owner.MatchesFrame(in ReadExecutionFrame(slot.ExecutionFrameOwnerOperationIndex)[0]))
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
                    throw new InvalidOperationException($"Blackboard slot '{valueSlot}' declares unsupported scope '{slot.ScopeKind}'.");
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
            int depth = m_InputDepth++;
            TValues values = default;
            m_Workspace.Inputs[depth][0] = values.FromUInt64(currentOwnerGeneration);
            Span<TValue> outputs = m_Workspace.Outputs[depth].AsSpan(0, 4);
            try
            {
                var invocation = new OrchestrationExtensionArguments<TValue>(
                    OrchestrationExtensionInvocationKind.Evaluate, operationIndex, ReadGeneration(operationIndex), m_Input.Tick, 0,
                    StateAncestors(operationIndex),
                    ReadExecutionFrame(operationIndex),
                    m_Workspace.Inputs[depth].AsSpan(0, 1), outputs, in m_TimelineInvocation);
                RequireSynchronousValueResult(operationIndex, bindingIndex,
                    m_Bindings.InvokeReadOnly(bindingIndex, in m_Context, in invocation));
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
