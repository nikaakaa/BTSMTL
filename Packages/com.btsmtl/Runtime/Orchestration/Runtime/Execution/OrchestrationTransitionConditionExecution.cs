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
        bool ReadTransitionCache(int conditionIndex, ref TValue result)
        {
            OrchestrationValueSchedule schedule = m_Program.ValueSchedule;
            ref readonly OrchestrationTransitionCondition condition = ref schedule.TransitionConditions.Span[conditionIndex];
            ref OrchestrationTransitionCacheState cache = ref m_Frames.TransitionCaches(m_FrameSlot)[condition.CacheIndex];
            int state = CurrentStateOperationIndex;
            int source = m_Program.Edge(condition.EdgeIndex).SourceOperationIndex;
            ref readonly OrchestrationExecutionFrameState frame = ref ReadExecutionFrame(state >= 0 ? state : source)[0];
            bool changed = !cache.Valid || cache.MachineGeneration != ReadGeneration(condition.MachineOperationIndex) ||
                cache.Context.StateOperationIndex != state ||
                cache.Context.StateGeneration != (state >= 0 ? ReadGeneration(state) : 0) ||
                !cache.Context.MatchesFrame(in frame);
            bool initialize = changed;
            Span<OrchestrationTransitionSnapshot<TValue>> snapshots = m_Frames.TransitionSnapshots(m_FrameSlot);
            for (int i = 0; i < condition.Sources.Count; i++)
                changed |= RefreshTransitionSource(in schedule.TransitionSources.Span[condition.Sources.Offset + i],
                    ref snapshots[condition.SnapshotOffset + i], initialize);
            if (changed)
            {
                cache.Valid = false;
                return false;
            }
            result = default(TValues).FromBoolean(cache.Passed);
            return true;
        }

        void WriteTransitionCache(int conditionIndex, in TValue result)
        {
            OrchestrationValueSchedule schedule = m_Program.ValueSchedule;
            ref readonly OrchestrationTransitionCondition condition = ref schedule.TransitionConditions.Span[conditionIndex];
            ref OrchestrationTransitionCacheState cache = ref m_Frames.TransitionCaches(m_FrameSlot)[condition.CacheIndex];
            int state = CurrentStateOperationIndex;
            int source = m_Program.Edge(condition.EdgeIndex).SourceOperationIndex;
            ref readonly OrchestrationExecutionFrameState frame = ref ReadExecutionFrame(state >= 0 ? state : source)[0];
            ref OrchestrationExecutionOwnerToken context = ref cache.Context;
            context.SkillContentId = frame.SkillContentId;
            context.ExecutionFrameOwnerOperationIndex = frame.OwnerOperationIndex;
            context.ActionInstanceId = frame.ActionInstanceId;
            context.ExecutionGeneration = frame.Generation;
            context.TimelineStart = frame.TimelineStart;
            context.ClipOrdinal = frame.ClipOrdinal;
            context.Cycle = frame.Cycle;
            context.Marker = frame.Marker;
            context.StateOperationIndex = state;
            context.StateGeneration = state >= 0 ? ReadGeneration(state) : 0;
            cache.MachineGeneration = ReadGeneration(condition.MachineOperationIndex);
            cache.Passed = default(TValues).Boolean(in result);
            cache.Valid = true;
        }

        bool RefreshTransitionSource(in OrchestrationTransitionSource source,
            ref OrchestrationTransitionSnapshot<TValue> snapshot, bool initialize)
        {
            TValues values = default;
            switch (source.Kind)
            {
                case OrchestrationTransitionSourceKind.Blackboard:
                {
                    OrchestrationBlackboardOwnerToken owner = ResolveOwner(m_CurrentOperationIndex, source.Index,
                        out _);
                    ulong revision = m_Blackboards.ReadMetadata(source.Index).Revision;
                    bool changed = initialize || snapshot.Revision != revision ||
                        snapshot.OwnerGeneration != owner.Generation;
                    ref readonly OrchestrationBlackboardSlot slot = ref m_Program.Blackboard(source.Index);
                    if (slot.IsGraphCallParameter)
                    {
                        bool active = IsGraphScopeActive(slot.OwnerOperationIndex);
                        if (!changed)
                            changed = values.Boolean(in snapshot.Value) != active;
                        if (changed)
                            snapshot.Value = values.FromBoolean(active);
                    }
                    if (changed)
                    {
                        snapshot.Revision = revision;
                        snapshot.OwnerGeneration = owner.Generation;
                    }
                    return changed;
                }
                case OrchestrationTransitionSourceKind.InputRequest:
                {
                    bool requested = HasRequest(m_Program.Payload<OrchestrationRequestPayload>(source.Index).IdentityId);
                    if (!initialize && values.Boolean(in snapshot.Value) == requested)
                        return false;
                    snapshot.Value = values.FromBoolean(requested);
                    return true;
                }
                case OrchestrationTransitionSourceKind.StateRootCompleted:
                {
                    bool completed = CurrentStateRootCompleted();
                    if (!initialize && values.Boolean(in snapshot.Value) == completed)
                        return false;
                    snapshot.Value = values.FromBoolean(completed);
                    return true;
                }
                case OrchestrationTransitionSourceKind.StateExitCause:
                {
                    int cause = CurrentStateExitCause();
                    if (!initialize && values.Int32(in snapshot.Value) == cause)
                        return false;
                    snapshot.Value = values.FromInt32(cause);
                    return true;
                }
                case OrchestrationTransitionSourceKind.Cycle:
                {
                    int cycle = ReadOperationState(source.Index).ChildCursor;
                    if (!initialize && values.Int32(in snapshot.Value) == cycle)
                        return false;
                    snapshot.Value = values.FromInt32(cycle);
                    return true;
                }
            }
            ref readonly TValue input = ref m_Input.HostInputs[source.Index];
            if (!initialize && SameTransitionValue(in input, in snapshot.Value))
                return false;
            snapshot.Value = input;
            return true;
        }

        static unsafe bool SameTransitionValue(in TValue left, in TValue right)
        {
            fixed (TValue* leftAddress = &left)
            fixed (TValue* rightAddress = &right)
                return new ReadOnlySpan<byte>(leftAddress, sizeof(TValue)).SequenceEqual(
                    new ReadOnlySpan<byte>(rightAddress, sizeof(TValue)));
        }
    }
}
