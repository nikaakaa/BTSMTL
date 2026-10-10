using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public enum EquipmentChangeFailure : byte
    {
        None = 0,
        CapabilityDisabled = 1,
        UnknownSlot = 2,
        UnknownEquipment = 3,
        SlotMismatch = 4,
        RequiredSlotCannotBeEmpty = 5,
        PendingChangeExists = 6,
        PendingChangeMissing = 7,
        ChangeIdentityMismatch = 8,
        StaleRevision = 9,
        ActionConflict = 10,
        ContributionRejected = 11,
        CommandBatchCapacityExceeded = 12
    }

    public readonly struct EquipmentChangeRequest
    {
        public EquipmentChangeRequest(
            EquipmentSlotId slotId,
            EquipmentId equipmentId,
            ulong expectedRevision,
            ulong sourceActionInstanceId)
        {
            if (!slotId.IsValid || expectedRevision == 0)
                throw new ArgumentException("Equipment change request is incomplete.");
            SlotId = slotId;
            EquipmentId = equipmentId;
            ExpectedRevision = expectedRevision;
            SourceActionInstanceId = sourceActionInstanceId;
        }

        public EquipmentSlotId SlotId { get; }
        public EquipmentId EquipmentId { get; }
        public ulong ExpectedRevision { get; }
        public ulong SourceActionInstanceId { get; }
        public bool IsValid => SlotId.IsValid && ExpectedRevision != 0;
    }

    public readonly struct EquipmentChangeOutcome
    {
        public EquipmentChangeOutcome(bool succeeded, EquipmentChangeFailure failure, EquipmentChangeId changeId,
            GameplayEffectRemoveResult effectRemoval = default, GameplayEffectExecutionFailure contributionFailure = default)
        {
            if (succeeded == (failure != EquipmentChangeFailure.None))
                throw new ArgumentException("Equipment change outcome is inconsistent.");
            Succeeded = succeeded;
            Failure = failure;
            ChangeId = changeId;
            EffectRemoval = effectRemoval;
            ContributionFailure = contributionFailure;

        }

        public bool Succeeded { get; }
        public EquipmentChangeFailure Failure { get; }
        public EquipmentChangeId ChangeId { get; }
        public GameplayEffectRemoveResult EffectRemoval { get; }
        public GameplayEffectExecutionFailure ContributionFailure { get; }

    }

    internal interface IEquipmentMutationScope : IDisposable
    {
        void Complete();
    }

    internal interface IEquipmentRuntimePort
    {
        ActorId ActorId { get; }
        ulong Tick { get; }
        EquipmentProgramLayout Layout { get; }
        EquipmentStateAggregate ReadState();
        void WriteState(EquipmentStateAggregate state);
        EquipmentChangeId AllocateChangeId();
        bool HasActiveActionConflict(EquipmentSlotState slot, ulong sourceActionInstanceId);
        bool IsActionActive(ulong actionInstanceId);
        void ResetLocalState(EquipmentFeatureId featureId, EquipmentLocalStateId stateId);
        EquipmentPassiveHandles AcquirePassiveHandles(EquipmentFeatureId featureId);
        void ReleasePassiveHandles(in EquipmentPassiveHandles handles);
        GameplayEffectApplyResult SetTags(GameplayTagSourceIdentityValue sourceId, IReadOnlyList<string> tags);
        void RemoveTags(GameplayTagSourceIdentityValue sourceId);
        GameplayEffectApplyResult ApplyPassiveEffect(string effectId);
        GameplayEffectRemoveResult RemovePassiveEffect(ulong handle);
        void EmitContributionFailure(OperationHandle source, GameplayEffectExecutionFailure failure);
        GameplayCommandBatchAdmission ReceiveGameplayCommands(OperationHandle source, int commands);
        IEquipmentMutationScope BeginMutation();
        GameplayEffectApplyResult CommitEffectOutputs(OperationHandle source);
        void CancelEffectOutputs();
        void EmitLifecycle(
            OperationHandle source,
            EquipmentSlotState before,
            EquipmentSlotState after,
            PendingEquipmentChangeState state,
            EquipmentChangeId changeId);
    }

    internal sealed class EquipmentRuntimeControl
    {
        readonly IEquipmentRuntimePort m_Port;

        public EquipmentRuntimeControl(IEquipmentRuntimePort port)
        {
            m_Port = port ?? throw new ArgumentNullException(nameof(port));
        }

        public EquipmentProgramLayout Layout => m_Port.Layout;

        public void PrepareEvaluation(OperationHandle source)
        {
            if (!Layout.CapabilityEnabled)
                return;
            EquipmentStateAggregate state = m_Port.ReadState();
            bool requiresMutation = false;
            for (int i = 0; i < state.Slots.Count; i++)
                requiresMutation |= state.Slots[i].IsEquipped && !state.Slots[i].ContributionsInstalled;

            if (requiresMutation)
            {
                int commands = 0;
                for (int i = 0; i < state.Slots.Count; i++)
                {
                    EquipmentSlotState slot = state.Slots[i];
                    if (slot.IsEquipped && !slot.ContributionsInstalled)
                        commands = checked(commands + 1 + Layout.RequireFeature(slot.FeatureId).PassiveEffects.Count);
                }
                if (!m_Port.ReceiveGameplayCommands(source, commands).Accepted)
                    return;
                GameplayEffectApplyResult installed = default;
                using (IEquipmentMutationScope mutation = m_Port.BeginMutation())
                {
                    bool succeeded = true;
                    for (int i = 0; i < state.Slots.Count; i++)
                    {
                        EquipmentSlotState slot = state.Slots[i];
                        if (!slot.IsEquipped || slot.ContributionsInstalled)
                            continue;
                        installed = Install(slot, out EquipmentSlotState incoming, out EquipmentPassiveHandles handles);
                        if (!installed.Succeeded)
                        {
                            m_Port.ReleasePassiveHandles(handles);
                            succeeded = false;
                            break;
                        }
                        state = m_Port.ReadState().WithSlot(incoming);
                        m_Port.WriteState(state);
                        m_Port.ReleasePassiveHandles(handles);
                    }
                    if (succeeded)
                    {
                        installed = m_Port.CommitEffectOutputs(source);
                        if (installed.Succeeded)
                            mutation.Complete();
                    }
                }
                if (!installed.Succeeded)
                {
                    m_Port.EmitContributionFailure(source, installed.Failure);
                    return;
                }
            }

            PendingEquipmentChange pending = state.PendingChange;
            if (!pending.IsPending || pending.SourceActionInstanceId == 0 || m_Port.IsActionActive(pending.SourceActionInstanceId))
                return;
            EquipmentSlotState pendingSlot = state.RequireSlot(pending.SlotId);
            m_Port.WriteState(state.ResolvePending(PendingEquipmentChangeState.Cancelled, m_Port.Tick));
            m_Port.EmitLifecycle(source, pendingSlot, pendingSlot, PendingEquipmentChangeState.Cancelled, pending.ChangeId);
        }

        public void CancelOrphanedPending(OperationHandle source)
        {
            EquipmentStateAggregate aggregate = m_Port.ReadState();
            PendingEquipmentChange pending = aggregate.PendingChange;
            if (!pending.IsPending || pending.SourceActionInstanceId == 0 || m_Port.IsActionActive(pending.SourceActionInstanceId))
                return;
            EquipmentSlotState slot = aggregate.RequireSlot(pending.SlotId);
            m_Port.WriteState(aggregate.ResolvePending(PendingEquipmentChangeState.Cancelled, m_Port.Tick));
            m_Port.EmitLifecycle(source, slot, slot, PendingEquipmentChangeState.Cancelled, pending.ChangeId);
        }

        public EquipmentChangeOutcome Validate(EquipmentChangeRequest request)
        {
            if (!Layout.CapabilityEnabled)
                return Fail(EquipmentChangeFailure.CapabilityDisabled);
            EquipmentProgramSlot definition;
            try { definition = Layout.RequireSlot(request.SlotId); }
            catch (InvalidOperationException) { return Fail(EquipmentChangeFailure.UnknownSlot); }
            EquipmentSlotState current = m_Port.ReadState().RequireSlot(request.SlotId);
            if (current.Revision != request.ExpectedRevision)
                return Fail(EquipmentChangeFailure.StaleRevision);
            if (!request.EquipmentId.IsValid)
                return definition.Requirement == EquipmentSlotRequirement.Required
                    ? Fail(EquipmentChangeFailure.RequiredSlotCannotBeEmpty)
                    : Success(default);
            EquipmentProgramItem item;
            try { item = Layout.RequireItem(request.EquipmentId); }
            catch (InvalidOperationException) { return Fail(EquipmentChangeFailure.UnknownEquipment); }
            return item.SlotId == request.SlotId
                ? Success(default)
                : Fail(EquipmentChangeFailure.SlotMismatch);
        }

        public EquipmentChangeOutcome Begin(EquipmentChangeRequest request)
        {
            EquipmentChangeOutcome validation = Validate(request);
            if (!validation.Succeeded)
                return validation;
            EquipmentStateAggregate aggregate = m_Port.ReadState();
            if (aggregate.PendingChange.IsPending)
                return Fail(EquipmentChangeFailure.PendingChangeExists);
            EquipmentSlotState current = aggregate.RequireSlot(request.SlotId);
            EquipmentChangeId changeId = m_Port.AllocateChangeId();
            var pending = new PendingEquipmentChange(
                changeId,
                request.SlotId,
                current.EquipmentId,
                request.EquipmentId,
                request.SourceActionInstanceId,
                m_Port.Tick,
                0,
                PendingEquipmentChangeState.Pending);
            m_Port.WriteState(aggregate.WithPending(pending));
            return Success(changeId);
        }

        public EquipmentChangeOutcome Commit(
            OperationHandle source,
            EquipmentChangeId changeId)
        {
            EquipmentStateAggregate aggregate = m_Port.ReadState();
            PendingEquipmentChange pending = aggregate.PendingChange;
            if (!pending.IsPending)
                return Fail(EquipmentChangeFailure.PendingChangeMissing);
            if (pending.ChangeId != changeId)
                return Fail(EquipmentChangeFailure.ChangeIdentityMismatch);
            EquipmentSlotState outgoing = aggregate.RequireSlot(pending.SlotId);
            if (outgoing.EquipmentId != pending.FromEquipmentId)
                return Fail(EquipmentChangeFailure.StaleRevision);
            if (m_Port.HasActiveActionConflict(outgoing, pending.SourceActionInstanceId))
                return Fail(EquipmentChangeFailure.ActionConflict);

            int commands = outgoing.PassiveEffectHandles.Count;
            if (pending.ToEquipmentId.IsValid)
            {
                EquipmentProgramItem item = Layout.RequireItem(pending.ToEquipmentId);
                commands = checked(commands + 1 + Layout.RequireFeature(item.FeatureId).PassiveEffects.Count);
            }
            GameplayCommandBatchAdmission commandBatch = m_Port.ReceiveGameplayCommands(source, commands);
            if (!commandBatch.Accepted)
                return new EquipmentChangeOutcome(false, EquipmentChangeFailure.CommandBatchCapacityExceeded,
                    changeId);

            GameplayEffectRemoveResult removal = default;
            GameplayEffectApplyResult installed = default;
            using (IEquipmentMutationScope mutation = m_Port.BeginMutation())
            {
                removal = Remove(outgoing);
                if (outgoing.PassiveEffectHandles.Count == 0 || removal.Succeeded)
                {
                    ResetFeatureState(outgoing.FeatureId);
                    ulong nextRevision = checked(outgoing.Revision + 1);
                    ulong nextGeneration = checked(outgoing.Generation + 1);
                    EquipmentSlotState incoming = CreateSlotState(outgoing.SlotId, pending.ToEquipmentId, nextRevision, nextGeneration);
                    ResetFeatureState(incoming.FeatureId);
                    EquipmentPassiveHandles handles = default;
                    if (incoming.IsEquipped)
                        installed = Install(incoming, out incoming, out handles);
                    else
                        installed = new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
                    if (installed.Succeeded)
                    {
                        aggregate = m_Port.ReadState().WithSlotAndResolvedPending(incoming, PendingEquipmentChangeState.Committed, m_Port.Tick);
                        m_Port.WriteState(aggregate);
                        m_Port.ReleasePassiveHandles(handles);
                        installed = m_Port.CommitEffectOutputs(source);
                        if (installed.Succeeded)
                        {
                            m_Port.EmitLifecycle(source, outgoing, incoming, PendingEquipmentChangeState.Committed, changeId);
                            mutation.Complete();
                            return Success(changeId);
                        }
                    }
                    else
                        m_Port.ReleasePassiveHandles(handles);
                }
            }
            GameplayEffectExecutionFailure failure = removal.Failure.IsValid ? removal.Failure : installed.Failure;
            if (failure.IsValid)
                m_Port.EmitContributionFailure(source, failure);
            return new EquipmentChangeOutcome(false, EquipmentChangeFailure.ContributionRejected, changeId, removal, failure);
        }

        public EquipmentChangeOutcome Cancel(OperationHandle source, EquipmentChangeId changeId)
        {
            EquipmentStateAggregate aggregate = m_Port.ReadState();
            PendingEquipmentChange pending = aggregate.PendingChange;
            if (!pending.IsPending)
                return Fail(EquipmentChangeFailure.PendingChangeMissing);
            if (pending.ChangeId != changeId)
                return Fail(EquipmentChangeFailure.ChangeIdentityMismatch);
            EquipmentSlotState slot = aggregate.RequireSlot(pending.SlotId);
            m_Port.WriteState(aggregate.ResolvePending(PendingEquipmentChangeState.Cancelled, m_Port.Tick));
            m_Port.EmitLifecycle(source, slot, slot, PendingEquipmentChangeState.Cancelled, changeId);
            return Success(changeId);
        }

        public EquipmentSlotState RequireCurrent(EquipmentSlotId slotId) =>
            m_Port.ReadState().RequireSlot(slotId);

        EquipmentSlotState CreateSlotState(
            EquipmentSlotId slotId,
            EquipmentId equipmentId,
            ulong revision,
            ulong generation)
        {
            if (!equipmentId.IsValid)
                return new EquipmentSlotState(slotId, default, default, default, default, revision, generation, false, GameplayTagSourceIdentityValue.Empty, default);
            EquipmentProgramItem item = Layout.RequireItem(equipmentId);
            EquipmentProgramFeature feature = Layout.RequireFeature(item.FeatureId);
            return new EquipmentSlotState(
                slotId,
                item.EquipmentId,
                item.FeatureId,
                feature.Revision,
                item.VisualBindingId,
                revision,
                generation,
                false,
                GameplayTagSourceIdentityValue.Empty,
                default);
        }

        GameplayEffectApplyResult Install(EquipmentSlotState slot, out EquipmentSlotState installed, out EquipmentPassiveHandles handles)
        {
            installed = slot;
            handles = default;
            EquipmentProgramFeature feature = Layout.RequireFeature(slot.FeatureId);
            GameplayTagSourceIdentityValue source = EquipmentTagSourceIdentity.Create(m_Port.ActorId, slot.SlotId, slot.Revision);
            GameplayEffectApplyResult result = m_Port.SetTags(source, feature.GrantedTags);
            if (!result.Succeeded)
                return result;
            handles = m_Port.AcquirePassiveHandles(slot.FeatureId);
            for (int i = 0; i < handles.Count; i++)
            {
                result = m_Port.ApplyPassiveEffect(feature.PassiveEffects[i]);
                if (!result.Succeeded)
                    return result;
                handles[i] = result.Handle;
            }
            installed = new EquipmentSlotState(slot.SlotId, slot.EquipmentId, slot.FeatureId,
                slot.FeatureRevision, slot.VisualBindingId, slot.Revision, slot.Generation, true, source, handles);
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
        }

        GameplayEffectRemoveResult Remove(EquipmentSlotState slot)
        {
            if (!slot.IsEquipped || !slot.ContributionsInstalled)
                return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.NoMatch, default);
            m_Port.RemoveTags(slot.TagSource);
            GameplayEffectRemoveResult result = new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.NoMatch, default);
            for (int i = 0; i < slot.PassiveEffectHandles.Count; i++)
            {
                result = m_Port.RemovePassiveEffect(slot.PassiveEffectHandles[i]);
                if (!result.Succeeded)
                    return result;
            }
            return result;
        }

        void ResetFeatureState(EquipmentFeatureId featureId)
        {
            if (!featureId.IsValid)
                return;
            IReadOnlyList<EquipmentProgramLocalState> states = Layout.LocalStates;
            for (int i = 0; i < states.Count; i++)
                if (states[i].FeatureId == featureId)
                    m_Port.ResetLocalState(states[i].FeatureId, states[i].StateId);
        }

        static EquipmentChangeOutcome Success(EquipmentChangeId changeId) =>
            new EquipmentChangeOutcome(true, EquipmentChangeFailure.None, changeId);

        static EquipmentChangeOutcome Fail(EquipmentChangeFailure failure) =>
            new EquipmentChangeOutcome(false, failure, default);
    }
}
