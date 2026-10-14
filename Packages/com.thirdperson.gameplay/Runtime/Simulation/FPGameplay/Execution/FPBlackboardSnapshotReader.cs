using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    public readonly struct FPBlackboardValueSnapshot
    {
        internal FPBlackboardValueSnapshot(CharacterSkillId ability, ulong actionInstanceId, ulong skillGeneration,
            int stateSlot, string scopeIdentity, ProgramScopeKind scopeKind, int compiledOwnerIndex, ProgramBlackboardLifetime lifetime, BlackboardOwnerToken owner,
            bool active, bool usesDefault, AbilityStateValue value)
        {
            Ability = ability;
            ActionInstanceId = actionInstanceId;
            SkillGeneration = skillGeneration;
            StateSlot = stateSlot;
            ScopeIdentity = scopeIdentity;
            ScopeKind = scopeKind;
            CompiledOwnerIndex = compiledOwnerIndex;
            Lifetime = lifetime;
            Owner = owner;
            IsActive = active;
            UsesDefault = usesDefault;
            Value = value;
        }

        public CharacterSkillId Ability { get; }
        public ulong ActionInstanceId { get; }
        public ulong SkillGeneration { get; }
        public int StateSlot { get; }
        public string ScopeIdentity { get; }
        public ProgramScopeKind ScopeKind { get; }
        public int CompiledOwnerIndex { get; }
        public ProgramBlackboardLifetime Lifetime { get; }
        public BlackboardOwnerToken Owner { get; }
        public bool IsActive { get; }
        public bool UsesDefault { get; }
        public bool IsReadOnly => Lifetime == ProgramBlackboardLifetime.Config;
        public AbilityStateValue Value { get; }
    }

    internal static class FPBlackboardSnapshotReader
    {
        internal static bool TryBindWrite(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, ulong instance, int slot, AbilityStateValue value,
            out FPBlackboardWriteCommand command)
        {
            FPAbilityRuntimeState ability = default;
            for (int i = 0; i < character.Abilities.Count; i++)
                if (character.Abilities[i].AbilityIdentity.AbilityId == installation.Data.AbilityId)
                {
                    ability = character.Abilities[i];
                    break;
                }
            SimulationBlackboardSlotGroup group = installation.Services.BlackboardGroup(slot);
            GameplayAbilityExecutionFrame<AbilityStateValue> frame = instance == 0
                ? null : ability.AbilityExecutionState.Find(instance);
            command = default;
            if (group.Scope.Kind != ProgramScopeKind.Character && frame == null ||
                !TryOwner(installation, character, ability, frame, group, out BlackboardOwnerToken owner))
                return false;
            command = new FPBlackboardWriteCommand(installation.Identity, instance,
                frame?.Generation ?? 0, slot, owner, value);
            return true;
        }


        internal static void Append(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, List<FPBlackboardValueSnapshot> destination)
        {
            FPAbilityRuntimeState ability = default;
            for (int i = 0; i < character.Abilities.Count; i++)
                if (character.Abilities[i].AbilityIdentity.AbilityId == installation.Data.AbilityId)
                {
                    ability = character.Abilities[i];
                    break;
                }
            if (ability.StateValues.Generation == 0 || !ability.AbilityIdentity.Equals(installation.Identity))
                throw new InvalidOperationException("黑板快照状态与正式技能安装版本不匹配。");
            IReadOnlyList<ProgramScopeLayout> scopes = installation.Data.Scopes;
            for (int i = 0; i < scopes.Count; i++)
            {
                ProgramScopeLayout scope = scopes[i];
                IReadOnlyList<SimulationBlackboardSlotGroup> groups = installation.Services.BlackboardGroups(scope);
                if (scope.Kind == ProgramScopeKind.Character)
                    AppendScope(installation, character, ability, null, groups, destination);
                else
                {
                    var frames = ability.AbilityExecutionState.Frames;
                    if (frames.Count == 0)
                        AppendScope(installation, character, ability, null, groups, destination);
                    for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                        AppendScope(installation, character, ability, frames[frameIndex], groups, destination);
                }
            }
        }

        static void AppendScope(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, FPAbilityRuntimeState ability,
            GameplayAbilityExecutionFrame<AbilityStateValue> frame,
            IReadOnlyList<SimulationBlackboardSlotGroup> groups, List<FPBlackboardValueSnapshot> destination)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                SimulationBlackboardSlotGroup group = groups[i];
                bool active = TryOwner(installation, character, ability, frame, group, out BlackboardOwnerToken owner);
                BlackboardOwnerToken stored = Read(installation, character, ability, frame, group.OwnerToken).BlackboardOwnerToken;
                AbilityStateValue value;
                bool usesDefault = !active || stored != owner;
                if (!active || !TryReadGraphInput(installation, character, ability, frame, group.Value, out value))
                    value = active && stored == owner
                        ? Read(installation, character, ability, frame, group.Value)
                        : installation.DefaultStateValue(group.Value);
                else
                    usesDefault = false;
                destination.Add(new FPBlackboardValueSnapshot(installation.Data.AbilityId,
                    frame?.ActionInstanceId ?? 0, frame?.Generation ?? 0, group.Value,
                    group.Scope.Identity, group.Scope.Kind, group.CompiledOwnerIndex, group.LifetimeKind, active ? owner : stored, active, usesDefault, value));
            }
        }

        static bool TryReadGraphInput(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, FPAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame,
            int slot, out AbilityStateValue value)
        {
            ReadOnlySpan<OperationHandle> owners = installation.Services.GraphInputOwners(slot);
            for (int i = 0; i < owners.Length; i++)
            {
                if (!IsActiveOperation(installation, character, ability, frame, owners[i]))
                    continue;
                value = Read(installation, character, ability, frame, slot);
                return true;
            }
            value = default;
            return false;
        }

        static bool TryOwner(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, FPAbilityRuntimeState ability,
            GameplayAbilityExecutionFrame<AbilityStateValue> frame, SimulationBlackboardSlotGroup group,
            out BlackboardOwnerToken owner)
        {
            owner = default;
            ProgramScopeLayout scope = group.Scope;
            if (scope.Kind != ProgramScopeKind.Character && frame == null)
                return false;
            ulong generation;
            switch (scope.Kind)
            {
                case ProgramScopeKind.Character:
                    generation = 1;
                    break;
                case ProgramScopeKind.Graph:
                    if (group.LifetimeKind == ProgramBlackboardLifetime.Config)
                    {
                        generation = 1;
                        break;
                    }
                    if (!IsActiveOperation(installation, character, ability, frame, scope.OwnerOperation))
                        return false;
                    generation = ReadGeneration(installation, character, ability, frame, scope.OwnerOperation);
                    break;
                case ProgramScopeKind.State:
                    OperationHandle machine = installation.Layout.Topology.StateMachineOwner(scope.OwnerOperation);
                    string stateId = installation.Layout.Topology.OperationIdentity(scope.OwnerOperation);
                    int activeSlot = installation.Layout.FindOperationStateSlot(machine, ProgramStateSemantic.StateMachineActive);
                    int exitingSlot = installation.Layout.FindOperationStateSlot(machine, ProgramStateSemantic.StateMachineExiting);
                    if (!IsActiveOperation(installation, character, ability, frame, scope.OwnerOperation) ||
                        (Read(installation, character, ability, frame, activeSlot).Identity != stateId &&
                         Read(installation, character, ability, frame, exitingSlot).Identity != stateId))
                        return false;
                    generation = ReadGeneration(installation, character, ability, frame, scope.OwnerOperation);
                    break;
                case ProgramScopeKind.ActionInstance:
                    bool active = false;
                    ReadOnlySpan<FPActionInstanceState> actions = character.ActionInstances;
                    for (int i = 0; i < actions.Length; i++)
                        if (actions[i].InstanceId == frame.ActionInstanceId)
                        {
                            active = actions[i].IsActive;
                            break;
                        }
                    if (!active)
                        return false;
                    generation = frame.ActionInstanceId;
                    break;
                case ProgramScopeKind.Frame:
                    if (character.LastCompletedTick == 0)
                        return false;
                    generation = character.LastCompletedTick;
                    break;
                default:
                    throw new InvalidOperationException($"未知黑板作用域 {scope.Kind}。");
            }
            owner = new BlackboardOwnerToken(scope.Kind, group.CompiledOwnerIndex, generation);
            return true;
        }

        static bool IsActiveOperation(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, FPAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame, OperationHandle operation)
        {
            int slot = installation.Layout.FindOperationStateSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            var status = (OperationRunnableStatus)Read(installation, character, ability, frame, slot).Int32;
            return status == OperationRunnableStatus.Running || status == OperationRunnableStatus.Stopping;
        }

        static ulong ReadGeneration(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, FPAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame, OperationHandle operation)
        {
            int slot = installation.Layout.FindOperationStateSlot(operation, ProgramStateSemantic.RunnableActivationGeneration);
            return Read(installation, character, ability, frame, slot).UInt64;
        }

        static AbilityStateValue Read(FPGameplayAbilityExecutionInstallation installation,
            FPCharacterRuntimeState character, FPAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame, int slot)
        {
            int characterSlot = installation.CharacterBlackboardSlots[slot];
            AbilityStateValue value;
            if (characterSlot >= 0)
                return character.CharacterBlackboards.TryGetValue(characterSlot, out value) ? value : installation.DefaultStateValue(slot);
            bool stored = frame != null && installation.Layout.IsSkillExecutionStateSlot(slot)
                ? frame.TryGetValue(slot, out value)
                : ability.StateValues.TryGetValue(slot, out value);
            return stored ? value : installation.DefaultStateValue(slot);
        }
    }
}
