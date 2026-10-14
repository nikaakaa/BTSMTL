using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<Photon.Deterministic.FP, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.FPGameplay.FPActionInstanceReference>;
using System;
using System.Collections.Generic;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.FPGameplay.SimulationActionTargetSnapshot>;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal interface IFPSkillExecutionState : IDisposable
    {
        AbilityStateValue Get(int slotIndex);
        AbilityStateValue Get(TypedStateAddress address);
        void Set(int slotIndex, AbilityStateValue value);
        void Set(TypedStateAddress address, AbilityStateValue value);
        void Reset(int slotIndex);
        GameplayAbilityExecutionAggregate<AbilityStateValue> GetAbilityExecutionState();
        void SetAbilityExecutionState(GameplayAbilityExecutionAggregate<AbilityStateValue> state);
        MotionWarpState GetMotionWarpState(OperationHandle operation);
        void SetMotionWarpState(OperationHandle operation, in MotionWarpState value);
    }

    internal interface IFPInputRequestStatePort
    {
        SimulationTick Tick { get; }
        SimulationInputRequestState GetInputRequest(CanonicalTextValue requestId);
        void SetInputRequest(CanonicalTextValue requestId, SimulationInputRequestState state);
    }

    internal interface IFPActionRuntimeStatePort
    {
        ulong NextActionEventSequence();
        IReadOnlyList<SimulationActionActivationRequestState> GetActionActivationRequests();
        void AddActivationRequest(SimulationActionActivationRequestState request);
        void RemoveActivationRequestAt(int index);
        IReadOnlyList<FPActionInstanceState> GetActionInstances();
        bool TryGetActionInstance(ulong instanceId, out FPActionInstanceState action);
        bool TryFindActionInstanceIndex(ulong instanceId, out int index);
        IReadOnlyList<int> GetActionInstanceIndices(CharacterSkillId skillId);
        void ReplaceActionInstanceAt(int index, FPActionInstanceState action);
        void AddActionInstance(FPActionInstanceState action);
    }

    internal interface IFPHandleAllocatorStatePort
    {
        ulong NextHandleAllocator();
        ulong CaptureHandleAllocator();
        void RestoreHandleAllocator(ulong value);
    }

    internal interface IFPEventSequenceStatePort
    {
        ulong NextEventSequence();
    }

    internal interface IFPGameplayEffectStatePort
    {
        int TickRate { get; }
        SimulationGameplayEffectState GetGameplayEffectState(FPGameplayEffectExecutionScratch scratch);
        GameplayEffectStateAggregate GetGameplayEffectAggregate();
    }

    internal interface IFPEquipmentStatePort
    {
        EquipmentStateAggregate GetEquipmentState();
        void SetEquipmentState(EquipmentStateAggregate state);
    }
}
