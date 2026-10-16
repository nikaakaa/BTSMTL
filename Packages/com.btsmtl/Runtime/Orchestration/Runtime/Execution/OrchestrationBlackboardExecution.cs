using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
    {
        public TValue ReadBlackboard(int operationIndex, int valueSlot)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            if (slot.IsGraphCallParameter && IsGraphScopeActive(slot.OwnerOperationIndex))
                return ReadValueSlot(valueSlot, in m_Blackboards.ReadValue(valueSlot));
            OrchestrationBlackboardOwnerToken expected = ResolveOwner(operationIndex, valueSlot, out _);
            return SameOwner(in m_Blackboards.ReadMetadata(valueSlot).Owner, in expected)
                ? ReadValueSlot(valueSlot, in m_Blackboards.ReadValue(valueSlot)) : m_Program.DefaultValue(valueSlot);
        }

        public void WriteBlackboard(int operationIndex, int valueSlot, in TValue value,
            OrchestrationAssignmentKind assignment = OrchestrationAssignmentKind.Set)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            if (slot.Lifetime == OrchestrationBlackboardLifetime.Config)
                throw new InvalidOperationException($"Blackboard slot '{valueSlot}' is read-only configuration.");
            OrchestrationBlackboardOwnerToken expected = ResolveOwner(operationIndex, valueSlot, out OrchestrationBlackboardSourceContext source);
            TValue current = SameOwner(in m_Blackboards.ReadMetadata(valueSlot).Owner, in expected)
                ? ReadValueSlot(valueSlot, in m_Blackboards.ReadValue(valueSlot)) : m_Program.DefaultValue(valueSlot);
            TValue converted = OrchestrationValueMath<TValue, TScalar, TVector2, TVector3,
                TQuaternion, TMath, TValues>.Convert(in value, slot.ValueKind);
            m_Blackboards.WriteValue(valueSlot) = OrchestrationValueMath<TValue, TScalar, TVector2, TVector3,
                TQuaternion, TMath, TValues>.Assign(in current, in converted, assignment);
            ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(valueSlot);
            metadata.Owner = expected;
            metadata.Lifetime = slot.Lifetime;
            metadata.WriteStamp = new OrchestrationBlackboardWriteStamp
            {
                SourceOperationIndex = operationIndex,
                LogicTick = m_Input.Tick,
                SourceContext = source,
                TimelineOperationIndex = m_TimelineInvocation.HasInvocation
                    ? m_Frames.Header(m_FrameSlot).ParentTimelineOperationIndex : -1,
                ClipOperationIndex = m_TimelineInvocation.HasInvocation
                    ? m_Frames.Header(m_FrameSlot).OwnerOperationIndex : -1,
                Cycle = m_TimelineInvocation.HasInvocation ? m_TimelineInvocation.Invocation.Token.Cycle : 0,
                CommandSequence = m_TimelineInvocation.HasInvocation ? m_TimelineInvocation.Invocation.Token.Sequence : 0
            };
            TraceOperation(OrchestrationTraceKind.Blackboard, operationIndex, valueSlot: valueSlot);
        }

        public void WriteInputProjection(int valueSlot, in TValue value)
        {
            ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(valueSlot);
            m_Blackboards.WriteValue(valueSlot) = value;
            m_Blackboards.WriteMetadata(valueSlot) = new OrchestrationBlackboardMetadata
            {
                Owner = new OrchestrationBlackboardOwnerToken
                {
                    ScopeKind = OrchestrationScopeKind.Character,
                    CompiledOwnerIndex = slot.CompiledOwnerIndex,
                    Generation = 1
                },
                Lifetime = slot.Lifetime,
                WriteStamp = new OrchestrationBlackboardWriteStamp
                {
                    SourceOperationIndex = m_Program.RootOperationIndex,
                    LogicTick = m_Input.Tick,
                    TimelineOperationIndex = -1,
                    ClipOperationIndex = -1
                }
            };
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
                        if (!IsGraphScopeActive(slot.OwnerOperationIndex))
                            throw new InvalidOperationException($"Operation '{operationIndex}' accesses Blackboard slot '{valueSlot}' outside its Graph scope.");
                        generation = slot.Lifetime == OrchestrationBlackboardLifetime.GraphInstance &&
                            !m_Program.Operation(slot.OwnerOperationIndex).IsRunnable
                            ? m_Blackboards.OwnerGeneration(valueSlot)
                            : ReadGeneration(slot.OwnerOperationIndex);
                    }
                    break;
                case OrchestrationScopeKind.State:
                    if (!IsCurrentStateExecution(slot.OwnerOperationIndex))
                        throw new InvalidOperationException($"Operation '{operationIndex}' accesses Blackboard slot '{valueSlot}' outside its State scope.");
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
