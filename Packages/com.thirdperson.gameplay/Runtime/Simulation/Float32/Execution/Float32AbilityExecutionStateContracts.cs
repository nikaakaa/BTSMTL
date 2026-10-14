using MotionWarpState = ThirdPersonSimulation.MotionWarpState<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32ActionInstanceReference>;
using System;
using System.Collections.Generic;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation
{
    internal interface IFloat32SkillExecutionState : IDisposable
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

    internal interface IFloat32InputRequestStatePort
    {
        SimulationTick Tick { get; }
        SimulationInputRequestState GetInputRequest(CanonicalTextValue requestId);
        void SetInputRequest(CanonicalTextValue requestId, SimulationInputRequestState state);
    }

    internal interface IFloat32ActionRuntimeStatePort
    {
        ulong NextActionEventSequence();
        IReadOnlyList<SimulationActionActivationRequestState> GetActionActivationRequests();
        void AddActivationRequest(SimulationActionActivationRequestState request);
        void RemoveActivationRequestAt(int index);
        IReadOnlyList<Float32ActionInstanceState> GetActionInstances();
        bool TryGetActionInstance(ulong instanceId, out Float32ActionInstanceState action);
        bool TryFindActionInstanceIndex(ulong instanceId, out int index);
        IReadOnlyList<int> GetActionInstanceIndices(CharacterSkillId skillId);
        void ReplaceActionInstanceAt(int index, Float32ActionInstanceState action);
        void AddActionInstance(Float32ActionInstanceState action);
    }

    internal interface IFloat32HandleAllocatorStatePort
    {
        ulong NextHandleAllocator();
        ulong CaptureHandleAllocator();
        void RestoreHandleAllocator(ulong value);
    }

    internal interface IFloat32EventSequenceStatePort
    {
        ulong NextEventSequence();
    }

    internal interface IFloat32GameplayEffectStatePort
    {
        int TickRate { get; }
        SimulationGameplayEffectState GetGameplayEffectState(Float32GameplayEffectExecutionScratch scratch);
        GameplayEffectStateAggregate GetGameplayEffectAggregate();
    }

    internal interface IFloat32EquipmentStatePort
    {
        EquipmentStateAggregate GetEquipmentState();
        void SetEquipmentState(EquipmentStateAggregate state);
    }
}
