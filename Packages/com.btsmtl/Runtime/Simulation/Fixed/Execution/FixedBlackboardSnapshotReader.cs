using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedBlackboardValueSnapshot
    {
        internal FixedBlackboardValueSnapshot(CharacterSkillId ability, ulong actionInstanceId, ulong skillGeneration,
            int stateSlot, string scopeIdentity, ProgramScopeKind scopeKind, int compiledOwnerIndex, ProgramBlackboardLifetime lifetime, BlackboardOwnerToken owner,
            bool active, AbilityStateValue value)
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
        public bool IsReadOnly => Lifetime == ProgramBlackboardLifetime.Config;
        public AbilityStateValue Value { get; }
    }

    internal static class FixedBlackboardSnapshotReader
    {
        internal static bool TryBindWrite(FixedGameplayAbilityExecutionInstallation installation,
            FixedCharacterRuntimeState character, ulong instance, int slot, AbilityStateValue value,
            out FixedBlackboardWriteCommand command)
        {
            FixedAbilityRuntimeState ability = default;
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
            command = new FixedBlackboardWriteCommand(installation.Identity, instance,
                frame?.Generation ?? 0, slot, owner, value);
            return true;
        }

        internal static bool IsFrameWindowActive(FixedGameplayAbilityExecutionInstallation installation,
            FixedCharacterRuntimeState character, in GameplayAbilitySourceContext source, string windowTag)
        {
            FixedAbilityRuntimeState ability = default;
            for (int i = 0; i < character.Abilities.Count; i++)
                if (character.Abilities[i].AbilityIdentity.AbilityId == installation.Data.AbilityId)
                {
                    ability = character.Abilities[i];
                    break;
                }
            var frames = ability.AbilityExecutionState.Frames;
            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                var frame = frames[frameIndex];
                if (frame.ActionInstanceId != source.InstanceId || frame.SkillId != source.SkillId ||
                    frame.Generation != source.SkillExecutionGeneration)
                    continue;
                for (int scopeIndex = 0; scopeIndex < installation.Data.Scopes.Count; scopeIndex++)
                {
                    ProgramScopeLayout scope = installation.Data.Scopes[scopeIndex];
                    if (scope.Kind != ProgramScopeKind.Frame)
                        continue;
                    var groups = installation.Services.BlackboardGroups(scope);
                    for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                    {
                        SimulationBlackboardSlotGroup group = groups[groupIndex];
                        if (!installation.Layout.TryGetCatalogIdentity(group.Declaration, ProgramCatalogFieldId.ActionWindowTag, out string windowIdentity) ||
                            !installation.Services.GameplayEffectCatalog.IsTagOrParent(windowIdentity, windowTag))
                            continue;
                        var owner = new BlackboardOwnerToken(ProgramScopeKind.Frame, group.CompiledOwnerIndex, character.LastCompletedTick);
                        if (Read(installation, ability, frame, group.OwnerToken).BlackboardOwnerToken == owner &&
                            Read(installation, ability, frame, group.WriteStamp).BlackboardWriteStamp.SourceContext.Equals(source) &&
                            Read(installation, ability, frame, group.Value).Boolean)
                            return true;
                    }
                }
            }
            return false;
        }


        internal static void Append(FixedGameplayAbilityExecutionInstallation installation,
            FixedCharacterRuntimeState character, List<FixedBlackboardValueSnapshot> destination)
        {
            FixedAbilityRuntimeState ability = default;
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
                    for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                        AppendScope(installation, character, ability, frames[frameIndex], groups, destination);
                }
            }
        }

        static void AppendScope(FixedGameplayAbilityExecutionInstallation installation,
            FixedCharacterRuntimeState character, FixedAbilityRuntimeState ability,
            GameplayAbilityExecutionFrame<AbilityStateValue> frame,
            IReadOnlyList<SimulationBlackboardSlotGroup> groups, List<FixedBlackboardValueSnapshot> destination)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                SimulationBlackboardSlotGroup group = groups[i];
                bool active = TryOwner(installation, character, ability, frame, group, out BlackboardOwnerToken owner);
                BlackboardOwnerToken stored = Read(installation, ability, frame, group.OwnerToken).BlackboardOwnerToken;
                AbilityStateValue value;
                if (!active || !TryReadGraphInput(installation, ability, frame, group.Value, out value))
                    value = active && stored == owner
                        ? Read(installation, ability, frame, group.Value)
                        : installation.Data.DefaultStateValue(group.Value);
                destination.Add(new FixedBlackboardValueSnapshot(installation.Data.AbilityId,
                    frame?.ActionInstanceId ?? 0, frame?.Generation ?? 0, group.Value,
                    group.Scope.Identity, group.Scope.Kind, group.CompiledOwnerIndex, group.LifetimeKind, active ? owner : stored, active, value));
            }
        }

        static bool TryReadGraphInput(FixedGameplayAbilityExecutionInstallation installation,
            FixedAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame,
            int slot, out AbilityStateValue value)
        {
            ReadOnlySpan<OperationHandle> owners = installation.Services.GraphInputOwners(slot);
            for (int i = 0; i < owners.Length; i++)
            {
                if (!IsActiveOperation(installation, ability, frame, owners[i]))
                    continue;
                value = Read(installation, ability, frame, slot);
                return true;
            }
            value = default;
            return false;
        }

        static bool TryOwner(FixedGameplayAbilityExecutionInstallation installation,
            FixedCharacterRuntimeState character, FixedAbilityRuntimeState ability,
            GameplayAbilityExecutionFrame<AbilityStateValue> frame, SimulationBlackboardSlotGroup group,
            out BlackboardOwnerToken owner)
        {
            owner = default;
            ProgramScopeLayout scope = group.Scope;
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
                    if (!IsActiveOperation(installation, ability, frame, scope.OwnerOperation))
                        return false;
                    generation = ReadGeneration(installation, ability, frame, scope.OwnerOperation);
                    break;
                case ProgramScopeKind.State:
                    OperationHandle machine = installation.Layout.Topology.StateMachineOwner(scope.OwnerOperation);
                    string stateId = installation.Layout.Topology.OperationIdentity(scope.OwnerOperation);
                    int activeSlot = installation.Layout.FindOperationStateSlot(machine, ProgramStateSemantic.StateMachineActive);
                    int exitingSlot = installation.Layout.FindOperationStateSlot(machine, ProgramStateSemantic.StateMachineExiting);
                    if (!IsActiveOperation(installation, ability, frame, scope.OwnerOperation) ||
                        (Read(installation, ability, frame, activeSlot).Identity != stateId &&
                         Read(installation, ability, frame, exitingSlot).Identity != stateId))
                        return false;
                    generation = ReadGeneration(installation, ability, frame, scope.OwnerOperation);
                    break;
                case ProgramScopeKind.ActionInstance:
                    bool active = false;
                    ReadOnlySpan<FixedActionInstanceState> actions = character.ActionInstances;
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

        static bool IsActiveOperation(FixedGameplayAbilityExecutionInstallation installation,
            FixedAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame, OperationHandle operation)
        {
            int slot = installation.Layout.FindOperationStateSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            var status = (OperationRunnableStatus)Read(installation, ability, frame, slot).Int32;
            return status == OperationRunnableStatus.Running || status == OperationRunnableStatus.Stopping;
        }

        static ulong ReadGeneration(FixedGameplayAbilityExecutionInstallation installation,
            FixedAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame, OperationHandle operation)
        {
            int slot = installation.Layout.FindOperationStateSlot(operation, ProgramStateSemantic.RunnableActivationGeneration);
            return Read(installation, ability, frame, slot).UInt64;
        }

        static AbilityStateValue Read(FixedGameplayAbilityExecutionInstallation installation,
            FixedAbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame, int slot)
        {
            AbilityStateValue value;
            bool stored = frame != null && installation.Layout.IsSkillExecutionStateSlot(slot)
                ? frame.TryGetValue(slot, out value)
                : ability.StateValues.TryGetValue(slot, out value);
            return stored ? value : installation.Data.DefaultStateValue(slot);
        }
    }
}
