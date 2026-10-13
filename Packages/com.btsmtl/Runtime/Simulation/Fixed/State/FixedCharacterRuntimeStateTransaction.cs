using MotionWarpState = ThirdPersonSimulation.MotionWarpState<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedYaw, ThirdPersonSimulation.Fixed.FixedActionInstanceReference, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedScalar>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedVector3>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedYaw>>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal readonly struct FixedAbilityExecutionSavepoint
    {
        internal FixedAbilityExecutionSavepoint(
            FixedCharacterRuntimeStateTransaction owner,
            ulong id,
            ulong previousId,
            int depth,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            ulong handleAllocator,
            ulong eventSequence,
            FixedActionStatePageReference actionState = default,
            FixedInputRequestPageReference inputRequests = default,
            ulong actionEventSequence = 0,
            int changedRequestCount = 0)
        {
            Owner = owner;
            Id = id;
            PreviousId = previousId;
            Depth = depth;
            GameplayEffectState = gameplayEffectState;
            EquipmentState = equipmentState;
            HandleAllocator = handleAllocator;
            EventSequence = eventSequence;
            ActionState = actionState;
            InputRequests = inputRequests;
            ActionEventSequence = actionEventSequence;
            ChangedRequestCount = changedRequestCount;
        }

        internal FixedCharacterRuntimeStateTransaction Owner { get; }
        internal ulong Id { get; }
        internal ulong PreviousId { get; }
        public int Depth { get; }
        internal GameplayEffectStateAggregate GameplayEffectState { get; }
        internal EquipmentStateAggregate EquipmentState { get; }
        internal ulong HandleAllocator { get; }
        internal ulong EventSequence { get; }
        internal FixedActionStatePageReference ActionState { get; }
        internal FixedInputRequestPageReference InputRequests { get; }
        internal ulong ActionEventSequence { get; }
        internal int ChangedRequestCount { get; }
        internal bool CapturesActivation => ActionState.Page != null;
    }

    internal sealed class FixedCharacterRuntimeStateTransaction : IFixedAbilityExecutionSavepointPort, IFixedControlRuntimeStatePort
    {
        FixedCharacterRuntimeState m_BaseState;
        AbilityStateValuePageReference<AbilityStateValue> m_CharacterBlackboards;
        GeneratedCharacterBlackboardLayout<AbilityStateValue> m_CharacterBlackboardLayout;
        MotionWarpStatePageReference<MotionWarpState> m_MotionWarpStates;
        Dictionary<CharacterSkillId, FixedAbilityRuntimeState> m_AbilityStates;
        Dictionary<CharacterSkillId, FixedSkillExecutionState> m_SkillStates;
        SimulationTick m_Tick;
        int m_TickRate;
        ulong m_NextSavepointId;
        ulong m_CurrentSavepointId;
        readonly List<FixedAbilityExecutionSavepoint> m_SavepointSnapshots =
            new List<FixedAbilityExecutionSavepoint>(GameplayEffectRuntimeCapacityLayout.MaximumSavepointDepth);
        FixedCharacterActionRuntimeState m_ActionState;
        FixedCharacterInputRequestState m_InputRequestState;
        FixedCharacterEventSequenceState m_EventSequenceState;
        FixedCharacterHandleAllocatorState m_HandleAllocatorState;
        FixedCharacterGameplayEffectRuntimeState m_GameplayEffectState;
        FixedCharacterEquipmentRuntimeState m_EquipmentState;
        CharacterControlRuntimeStateTransaction m_ControlState;
        bool m_ControlStateBound;
        bool m_AbilitiesChanged;
        bool m_Disposed;

        public FixedCharacterRuntimeStateTransaction()
        {
            m_Disposed = true;
        }

        internal void Initialize(IReadOnlyList<FixedGameplayAbilityExecutionInstallation> installations,
            MotionWarpStatePageLayout motionWarpLayout, GeneratedCharacterBlackboardLayout<AbilityStateValue> characterBlackboards)
        {
            m_CharacterBlackboardLayout = characterBlackboards;
            m_AbilityStates = new Dictionary<CharacterSkillId, FixedAbilityRuntimeState>(installations.Count);
            m_SkillStates = new Dictionary<CharacterSkillId, FixedSkillExecutionState>(installations.Count);
            for (int index = 0; index < installations.Count; index++)
            {
                FixedGameplayAbilityExecutionInstallation installation = installations[index];
                m_SkillStates.Add(installation.Identity.AbilityId,
                    new FixedSkillExecutionState(this, installation.Identity, installation.Layout,
                        installation.Data, motionWarpLayout.Partition(installation.Identity.AbilityId),
                        characterBlackboards.Slots(installation.Identity.AbilityId)));
            }
        }

        public FixedCharacterRuntimeStateTransaction Restart(
            FixedCharacterRuntimeState baseState,
            SimulationTick tick,
            int tickRate,
            FixedGameplayEffectRuntimeCatalog gameplayEffectCatalog,
            FixedCharacterInputRequestState inputRequestState,
            FixedCharacterActionRuntimeState actionState,
            CharacterControlRuntimeStateTransaction controlState,
            FixedCharacterEventSequenceState eventSequenceState,
            FixedCharacterHandleAllocatorState handleAllocatorState,
            FixedCharacterGameplayEffectRuntimeState gameplayEffectState,
            FixedCharacterEquipmentRuntimeState equipmentState,
            MotionWarpStatePageReference<MotionWarpState> motionWarpStates)
        {
            m_BaseState = baseState;
            m_CharacterBlackboards = baseState.CharacterBlackboards;
            m_MotionWarpStates = motionWarpStates;
            if (!tick.IsValid || tickRate <= 0)
                throw new ArgumentException("Fixed Character runtime transaction timing is incomplete.");
            if (m_SavepointSnapshots.Count != 0)
                throw new InvalidOperationException("Fixed Character runtime transaction reuse found active savepoints.");
            m_Tick = tick;
            m_TickRate = tickRate;
            m_AbilityStates.Clear();
            for (int i = 0; i < baseState.Abilities.Count; i++)
            {
                FixedAbilityRuntimeState state = baseState.Abilities[i];
                m_AbilityStates.Add(state.AbilityIdentity.AbilityId, state);
            }
            m_ActionState = actionState.Restart(
                baseState.ActionStatePage,
                baseState.ActionEventSequence);
            m_InputRequestState = inputRequestState.Restart(m_Tick, baseState.InputRequestPage);
            m_EventSequenceState = eventSequenceState.Restart(baseState.EventSequence);
            m_HandleAllocatorState = handleAllocatorState.Restart(baseState.HandleAllocator);
            m_GameplayEffectState = gameplayEffectState.Restart(
                m_TickRate,
                gameplayEffectCatalog,
                baseState.GameplayEffectState);
            m_EquipmentState = equipmentState.Restart(baseState.EquipmentState);
            m_ControlState = controlState;
            m_ControlStateBound = false;
            m_AbilitiesChanged = false;
            baseState.RetainStatePages();
            m_Disposed = false;
            return this;
        }

        internal AbilityStateValue ReadCharacterBlackboard(int slot)
        {
            if (m_CharacterBlackboards.TryGetValue(slot, out AbilityStateValue value))
                return value;
            if (m_CharacterBlackboardLayout.Binding.Contains(slot))
                return m_CharacterBlackboardLayout.Binding.DefaultValue(slot);
            if (m_CharacterBlackboardLayout.Kinds[slot] == ProgramStateValueKind.BlackboardOwnerToken)
                return AbilityStateValue.FromBlackboardOwnerToken(default);
            if (m_CharacterBlackboardLayout.Kinds[slot] == ProgramStateValueKind.Int32)
                return AbilityStateValue.FromInt32((int)m_CharacterBlackboardLayout.Lifetimes[slot]);
            return AbilityStateValue.FromBlackboardWriteStamp(default);
        }

        internal void WriteCharacterBlackboard(int slot, AbilityStateValue value)
        {
            if (ReadCharacterBlackboard(slot).Equals(value))
                return;
            if (ReferenceEquals(m_CharacterBlackboards.Set, m_BaseState.CharacterBlackboards.Set))
                m_CharacterBlackboards = m_CharacterBlackboards.Set.Bank.BeginWrite(m_CharacterBlackboards);
            m_CharacterBlackboards.Set.Set(slot, value);
        }

        internal IFixedSkillExecutionState BindAbility(CharacterSkillId abilityId)
        {
            RequireActive();
            return m_SkillStates[abilityId].Restart(m_AbilityStates[abilityId], m_MotionWarpStates);
        }

        public CharacterControlRuntimeStateTransaction BindControl(CharacterControlStateSchema schema)
        {
            RequireActive();
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));
            if (m_ControlStateBound)
                throw new InvalidOperationException("Fixed Character Control state is already bound.");
            if (!m_BaseState.ControlState.IsValid)
                throw new InvalidOperationException("Fixed Character runtime has no Control state.");
            m_ControlStateBound = true;
            return m_ControlState.Restart(
                m_BaseState.ControlState,
                schema,
                m_Tick);
        }

        internal void AcceptAbility(IFixedSkillExecutionState skillState)
        {
            RequireActive();
            if (!(skillState is FixedSkillExecutionState ability) ||
                !ReferenceEquals(ability.BindingIdentity, this))
            {
                throw new InvalidOperationException("Fixed Ability transaction belongs to another Character runtime transaction.");
            }
            FixedAbilityRuntimeState state = ability.TakeSnapshot();
            if (!m_AbilityStates[state.AbilityIdentity.AbilityId].SharesStateWith(state))
            {
                m_AbilityStates[state.AbilityIdentity.AbilityId].StateValues.Release();
                m_AbilityStates[state.AbilityIdentity.AbilityId].AbilityExecutionState.ReleaseStateStorage();
                m_AbilitiesChanged = true;
            }
            m_AbilityStates[state.AbilityIdentity.AbilityId] = state;
        }

        internal FixedCharacterRuntimeState Commit()
        {
            RequireActive();
            if (m_SavepointSnapshots.Count != 0)
                throw new InvalidOperationException("Character runtime state transaction has active savepoints.");
            return Snapshot();
        }

        internal FixedCharacterInputRequestState InputRequests => m_InputRequestState;
        internal IFixedActionRuntimeStatePort ActionState => m_ActionState;
        internal IFixedEventSequenceStatePort EventSequenceState => m_EventSequenceState;
        internal IFixedHandleAllocatorStatePort HandleAllocatorState => m_HandleAllocatorState;
        internal IFixedGameplayEffectStatePort GameplayEffectState => m_GameplayEffectState;
        internal IFixedEquipmentStatePort EquipmentState => m_EquipmentState;

        public FixedAbilityExecutionSavepoint CreateSavepoint()
        {
            RequireActive();
            GameplayEffectStateAggregate gameplayEffectState = m_GameplayEffectState.Capture();
            EquipmentStateAggregate equipmentState = m_EquipmentState.Capture();
            ulong id = checked(m_NextSavepointId + 1);
            var savepoint = new FixedAbilityExecutionSavepoint(
                this,
                id,
                m_CurrentSavepointId,
                m_SavepointSnapshots.Count + 1,
                gameplayEffectState,
                equipmentState,
                m_HandleAllocatorState.HandleAllocator,
                m_EventSequenceState.EventSequence);
            m_NextSavepointId = id;
            m_CurrentSavepointId = id;
            if (gameplayEffectState.IsValid)
                gameplayEffectState.RetainSnapshots();
            equipmentState?.Retain();
            m_SavepointSnapshots.Add(savepoint);
            return savepoint;
        }

        public FixedAbilityExecutionSavepoint CreateActivationSavepoint()
        {
            FixedAbilityExecutionSavepoint current = CreateSavepoint();
            FixedActionStatePageReference actions = m_ActionState.Capture();
            FixedInputRequestPageReference requests = m_InputRequestState.CaptureActivation(out int changedRequestCount);
            actions.Retain();
            requests.Retain();
            var savepoint = new FixedAbilityExecutionSavepoint(this, current.Id, current.PreviousId,
                current.Depth, current.GameplayEffectState, current.EquipmentState, current.HandleAllocator,
                current.EventSequence, actions, requests, m_ActionState.ActionEventSequence, changedRequestCount);
            m_SavepointSnapshots[m_SavepointSnapshots.Count - 1] = savepoint;
            return savepoint;
        }

        public void Restore(FixedAbilityExecutionSavepoint savepoint)
        {
            RequireActive();
            RequireTopSavepoint(savepoint);
            if (savepoint.GameplayEffectState.IsValid)
                m_GameplayEffectState.Restore(savepoint.GameplayEffectState);
            m_EquipmentState.Restore(savepoint.EquipmentState);
            m_HandleAllocatorState.RestoreHandleAllocator(savepoint.HandleAllocator);
            m_EventSequenceState.Restore(savepoint.EventSequence);
            if (savepoint.CapturesActivation)
            {
                m_ActionState.RestoreActivation(savepoint.ActionState, savepoint.ActionEventSequence);
                m_InputRequestState.RestoreActivation(savepoint.InputRequests, savepoint.ChangedRequestCount);
            }
            CompleteSavepoint(savepoint);
        }

        public void Release(FixedAbilityExecutionSavepoint savepoint)
        {
            RequireActive();
            RequireTopSavepoint(savepoint);
            CompleteSavepoint(savepoint);
        }

        public int SavepointDepth => m_SavepointSnapshots.Count;

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_CurrentSavepointId = 0;
            for (int index = m_SavepointSnapshots.Count - 1; index >= 0; index--)
                ReleaseSavepointStorage(m_SavepointSnapshots[index]);
            m_SavepointSnapshots.Clear();
            m_ControlState?.Dispose();
            m_InputRequestState.Dispose();
            m_ActionState.Dispose();
            m_EventSequenceState.Dispose();
            m_HandleAllocatorState.Dispose();
            m_GameplayEffectState.Dispose();
            m_EquipmentState.Dispose();
            foreach (KeyValuePair<CharacterSkillId, FixedAbilityRuntimeState> ability in m_AbilityStates)
            {
                ability.Value.StateValues.Release();
                ability.Value.AbilityExecutionState.ReleaseStateStorage();
            }
            m_BaseState.AbilityDirectory.Release();
            if (!ReferenceEquals(m_CharacterBlackboards.Set, m_BaseState.CharacterBlackboards.Set))
                m_CharacterBlackboards.Release();
            m_BaseState.CharacterBlackboards.Release();
            m_BaseState.InputRequestPage.Release();
            m_BaseState.ActionStatePage.Release();
            m_BaseState.TimelineSnapshotPage.Release();
            m_BaseState.GameplayEffectState.ReleaseSnapshots();
            m_BaseState.EquipmentState?.Release();
            m_BaseState.ReleaseStateStorage();
            m_Disposed = true;
        }

        FixedCharacterRuntimeState Snapshot()
        {
            FixedAbilityStateDirectoryReference abilities;
            if (m_AbilitiesChanged)
            {
                abilities = m_BaseState.AbilityDirectory.Page.Bank.Begin();
                FixedAbilityRuntimeState[] source = m_BaseState.AbilityDirectory.Page.Values;
                for (int abilityIndex = 0; abilityIndex < source.Length; abilityIndex++)
                    abilities.Page.Values[abilityIndex] = m_AbilityStates[source[abilityIndex].AbilityIdentity.AbilityId];
            }
            else
            {
                abilities = m_BaseState.AbilityDirectory;
            }
            FixedActionStatePageReference actions = m_ActionState.Capture();
            FixedInputRequestPageReference inputRequests = m_InputRequestState.Capture();
            FixedCharacterRuntimeStatePage page = m_BaseState.StatePages.Begin();
            page.Data.CharacterBlackboards = m_CharacterBlackboards;
            return FixedCharacterRuntimeState.AdoptSnapshot(
                page,
                new CharacterRuntimeStateMetadata(m_BaseState.NumericProfile, m_BaseState.GameplayContentHash,
                    m_BaseState.StateSchemaHash, m_Tick.Value, m_EventSequenceState.EventSequence,
                    m_ActionState.ActionEventSequence, m_HandleAllocatorState.HandleAllocator),
                abilities,
                actions,
                inputRequests,
                m_ControlState.Capture(page.ControlStorage),
                m_GameplayEffectState.Commit(),
                m_EquipmentState.Capture(),
                m_BaseState.TimelineSnapshotPage,
                m_MotionWarpStates);
        }

        void RequireTopSavepoint(FixedAbilityExecutionSavepoint savepoint)
        {
            if (!ReferenceEquals(savepoint.Owner, this) || savepoint.Id != m_CurrentSavepointId)
                throw new InvalidOperationException("Fixed Ability execution savepoint is stale or unbalanced.");
        }

        void CompleteSavepoint(FixedAbilityExecutionSavepoint savepoint)
        {
            m_CurrentSavepointId = savepoint.PreviousId;
            int index = m_SavepointSnapshots.Count - 1;
            ReleaseSavepointStorage(m_SavepointSnapshots[index]);
            m_SavepointSnapshots.RemoveAt(index);
        }

        static void ReleaseSavepointStorage(FixedAbilityExecutionSavepoint savepoint)
        {
            if (savepoint.GameplayEffectState.IsValid)
                savepoint.GameplayEffectState.ReleaseSnapshots();
            savepoint.EquipmentState?.Release();
            if (savepoint.CapturesActivation)
            {
                savepoint.ActionState.Release();
                savepoint.InputRequests.Release();
            }
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FixedCharacterRuntimeStateTransaction));
        }
    }

    internal sealed class FixedSkillExecutionState : IFixedSkillExecutionState
    {
        readonly FixedCharacterRuntimeStateTransaction m_BindingIdentity;
        readonly int[] m_CharacterSlots;
        readonly GameplayAbilityExecutionLayout m_Layout;
        readonly FixedGameplayAbilityExecutionData m_Ability;
        FixedAbilityRuntimeState m_CommittedState;
        AbilityStateValuePageReference<AbilityStateValue> m_StateValues;
        MotionWarpStatePageReference<MotionWarpState> m_MotionWarpStates;
        readonly int m_MotionWarpPartition;
        GameplayAbilityExecutionAggregate<AbilityStateValue> m_AbilityExecutionState;
        bool m_StateValuesChanged;
        bool m_Disposed;

        public FixedSkillExecutionState(
            FixedCharacterRuntimeStateTransaction bindingIdentity,
            GameplayAbilityExecutionIdentity identity,
            GameplayAbilityExecutionLayout layout,
            FixedGameplayAbilityExecutionData ability,
            int motionWarpPartition, int[] characterSlots)
        {
            m_BindingIdentity = bindingIdentity ?? throw new ArgumentNullException(nameof(bindingIdentity));
            if (!identity.IsValid)
                throw new ArgumentException("Fixed Ability execution identity is incomplete.", nameof(identity));
            m_Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            m_Ability = ability ?? throw new ArgumentNullException(nameof(ability));
            if (m_Ability.AbilityId != identity.AbilityId)
                throw new ArgumentException("Fixed Ability execution data does not match its identity.", nameof(ability));
            m_MotionWarpPartition = motionWarpPartition;
            m_CharacterSlots = characterSlots;
            m_Disposed = true;
        }

        internal FixedSkillExecutionState Restart(
            FixedAbilityRuntimeState state,
            MotionWarpStatePageReference<MotionWarpState> motionWarpStates)
        {
            m_CommittedState = state;
            m_MotionWarpStates = motionWarpStates;
            BindCommittedCollections();
            m_AbilityExecutionState = state.AbilityExecutionState;
            m_StateValuesChanged = false;
            m_Disposed = false;
            return this;
        }

        internal object BindingIdentity => m_BindingIdentity;

        public AbilityStateValue Get(int slotIndex) => Get(m_Layout.Address(slotIndex));

        public AbilityStateValue Get(TypedStateAddress address)
        {
            RequireActive();
            int character = m_CharacterSlots[address.SlotIndex];
            if (character >= 0)
                return m_BindingIdentity.ReadCharacterBlackboard(character);
            if (!m_StateValues.Set.TryGetValue(address.SlotIndex, out AbilityStateValue value))
                value = DefaultValue(address.SlotIndex);
            if (value.Kind != address.ValueKind)
                throw new InvalidOperationException($"State slot '{address.SlotIndex}' expects '{address.ValueKind}', received '{value.Kind}'.");
            return value;
        }

        public void Set(int slotIndex, AbilityStateValue value) => Set(m_Layout.Address(slotIndex), value);

        public void Set(TypedStateAddress address, AbilityStateValue value)
        {
            RequireActive();
            if (value.Kind != address.ValueKind)
                throw new InvalidOperationException($"State slot '{address.SlotIndex}' expects '{address.ValueKind}', received '{value.Kind}'.");
            int character = m_CharacterSlots[address.SlotIndex];
            if (character >= 0)
            {
                m_BindingIdentity.WriteCharacterBlackboard(character, value);
                return;
            }
            AbilityStateValue current = m_StateValues.Set.TryGetValue(address.SlotIndex, out AbilityStateValue existing)
                ? existing
                : DefaultValue(address.SlotIndex);
            if (current.Equals(value))
                return;
            if (!m_StateValuesChanged)
                m_StateValues = m_StateValues.Set.Bank.BeginWrite(m_StateValues);
            m_StateValuesChanged = true;
            m_StateValues.Set.Set(address.SlotIndex, value);
        }

        public void Reset(int slotIndex)
        {
            RequireActive();
            Set(slotIndex, DefaultValue(slotIndex));
        }

        public GameplayAbilityExecutionAggregate<AbilityStateValue> GetAbilityExecutionState()
        {
            RequireActive();
            return m_AbilityExecutionState;
        }

        public void SetAbilityExecutionState(GameplayAbilityExecutionAggregate<AbilityStateValue> state)
        {
            RequireActive();
            m_AbilityExecutionState = state ?? throw new ArgumentNullException(nameof(state));
        }

        public MotionWarpState GetMotionWarpState(OperationHandle operation)
        {
            RequireActive();
            int slot = m_MotionWarpStates.Page.Layout.Slot(m_MotionWarpPartition, operation);
            return m_MotionWarpStates.Page.At(slot);
        }

        public void SetMotionWarpState(OperationHandle operation, MotionWarpState value)
        {
            RequireActive();
            int slot = m_MotionWarpStates.Page.Layout.Slot(m_MotionWarpPartition, operation);
            MotionWarpState stored = value.Active ? value : default;
            m_MotionWarpStates.Page.Set(slot, in stored);
        }

        public void Dispose()
        {
            if (m_StateValuesChanged)
                m_StateValues.Release();
            m_StateValuesChanged = false;
            m_Disposed = true;
        }

        internal FixedAbilityRuntimeState TakeSnapshot()
        {
            RequireActive();
            IReadOnlyList<int> operations = m_MotionWarpStates.Page.Layout.Operations(m_MotionWarpPartition);
            int offset = m_MotionWarpStates.Page.Layout.Offset(m_MotionWarpPartition);
            for (int index = 0; index < operations.Count; index++)
            {
                ref readonly MotionWarpState warp = ref m_MotionWarpStates.Page.At(offset + index);
                if (warp.Active && m_AbilityExecutionState.Find(warp.ActionInstance.InstanceId) == null)
                {
                    MotionWarpState cleared = default;
                    m_MotionWarpStates.Page.Set(offset + index, in cleared);
                }
            }
            bool abilityExecutionStateChanged = !m_AbilityExecutionState.Equals(m_CommittedState.AbilityExecutionState);
            if (!m_StateValuesChanged && !abilityExecutionStateChanged)
            {
                FixedAbilityRuntimeState committed = m_CommittedState;
                Clear();
                return committed;
            }
            AbilityStateValuePageReference<AbilityStateValue> stateValues = m_StateValuesChanged
                ? m_StateValues
                : m_CommittedState.StateValues;
            if (!m_StateValuesChanged)
                stateValues.Retain();
            GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState = abilityExecutionStateChanged
                ? m_AbilityExecutionState
                : m_CommittedState.AbilityExecutionState;
            FixedAbilityRuntimeState snapshot = FixedAbilityRuntimeState.Adopt(
                stateValues,
                abilityExecutionState);
            abilityExecutionState.RetainStateStorage();
            m_CommittedState = snapshot;
            Clear();
            return snapshot;
        }

        void BindCommittedCollections()
        {
            m_StateValues = m_CommittedState.StateValues;
        }

        AbilityStateValue DefaultValue(int slot) => m_StateValues.Set.Bank.Blackboards.Contains(slot)
            ? m_StateValues.Set.Bank.Blackboards.DefaultValue(slot) : m_Ability.DefaultStateValue(slot);

        void Clear()
        {
            m_StateValues = default;
            m_StateValuesChanged = false;
            m_MotionWarpStates = default;
            m_AbilityExecutionState = null;
            m_Disposed = true;
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FixedSkillExecutionState));
        }
    }
}
