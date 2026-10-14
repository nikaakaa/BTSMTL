using System.Collections.Generic;
using Photon.Deterministic;
using ThirdPersonSimulation;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<Photon.Deterministic.FP, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.FPGameplay.FPActionInstanceReference>;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.FPGameplay.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed partial class FPCharacterRuntimeStatePage
    {
        SimulationActorBinding m_PreparedReadOwner;

        internal void BindPreparedReadOwner(SimulationActorBinding owner) => m_PreparedReadOwner = owner;

        internal AbilityStateValuePageReference<AbilityStateValue> PrepareReadCharacterBlackboards() =>
            m_PreparedReadOwner.CharacterBlackboardPages.BeginPreparedRead();

        internal FPAbilityStateDirectoryReference PrepareReadAbilities() => Data.Abilities.BeginPreparedRead();

        internal FPActionStatePageReference PrepareReadActions() =>
            m_PreparedReadOwner.ActionStatePages.BeginPreparedRead();

        internal FPInputRequestPageReference PrepareReadInputRequests() =>
            m_PreparedReadOwner.InputRequestPages.BeginPreparedRead();

        internal MotionWarpStatePageReference<MotionWarpState> PrepareReadMotionWarp()
        {
            MotionWarpStatePageReference<MotionWarpState> value = m_PreparedReadOwner.MotionWarpPages.BeginEmpty();
            value.Retain();
            return value;
        }

        internal void RestorePreparedMotionWarp(in MotionWarpStatePageReference<MotionWarpState> state,
            int slot, in MotionWarpState value) => state.Page.Set(slot, in value);

        internal void RestorePreparedControlValue(int index, in CharacterControlStateValue value) =>
            ControlStorage.SetValue(index, value);

        internal CharacterControlRuntimeState CompleteReadControlState(CharacterControlStateSchema schema,
            ulong lastCompletedTick) => new CharacterControlRuntimeState(schema, lastCompletedTick, ControlStorage);

        internal EquipmentStateAggregate PrepareReadEquipment()
        {
            EquipmentStateAggregate value = m_PreparedReadOwner.EquipmentState.Storage.Begin();
            value.Retain();
            return value;
        }

        internal EquipmentPassiveHandles PrepareReadPassiveEffectHandles(int count) =>
            m_PreparedReadOwner.EquipmentState.Storage.AcquirePassiveHandles(count);

        internal void RestorePreparedEquipmentSlot(EquipmentStateAggregate state, int index,
            in EquipmentSlotState value) => state.SetSlot(index, value);

        internal void RestorePreparedEquipmentLocalState(EquipmentStateAggregate state, int index,
            in EquipmentLocalStateValue value) => state.RestoreLocalState(index, in value);

        internal void RestorePreparedEquipmentChanges(EquipmentStateAggregate state,
            in PendingEquipmentChange pending, in PendingEquipmentChange resolved) =>
            state.RestoreChanges(pending, resolved);

        internal SimulationGameplayEffectState PrepareReadGameplayEffectOwner() =>
            m_PreparedReadOwner.GameplayEffectState.PrepareReadState();
    }

    internal sealed partial class FPActionStatePageBank
    {
        internal FPActionStatePageReference BeginPreparedRead()
        {
            FPActionStatePageReference value = Begin();
            value.Retain();
            return value;
        }
    }

    internal readonly partial struct FPActionStatePageReference
    {
        internal FPActionStatePageReference BeginPreparedRead() => Page.Bank.BeginPreparedRead();

        internal void AddPreparedActivationRequest(in SimulationActionActivationRequestState value)
        {
            value.Retain();
            value.TargetSnapshot.Retain();
            Page.Requests[Page.RequestCount++] = value;
        }

        internal void AddPreparedAction(in FPActionInstanceState value)
        {
            value.Retain();
            Page.Actions[Page.ActionCount++] = value;
        }
    }

    internal sealed partial class FPInputRequestPageBank
    {
        internal FPInputRequestPageReference BeginPreparedRead()
        {
            FPInputRequestPageReference value = Begin(0);
            value.Retain();
            return value;
        }
    }

    internal readonly partial struct FPInputRequestPageReference
    {
        internal FPInputRequestPageReference BeginPreparedRead() => Page.Bank.BeginPreparedRead();

        internal void AddPreparedInput(in KeyValuePair<string, SimulationInputRequestState> value)
        {
            value.Value.Retain();
            Page.Values[Page.Count++] = value;
        }

        internal void AddPreparedPending(in SimulationInputRequest value)
        {
            value.Retain();
            Page.PendingRequests[Page.PendingCount++] = value;
        }

        internal void CompleteRead(ulong lastAcceptedSequence) => Page.LastAcceptedSequence = lastAcceptedSequence;
    }
}
