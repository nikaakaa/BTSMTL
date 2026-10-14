using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<Photon.Deterministic.FP, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.FPGameplay.FPActionInstanceReference>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal readonly struct FPAbilityExecutionSavepoint
    {
        internal FPAbilityExecutionSavepoint(
            FPCharacterRuntimeStateTransaction owner,
            ulong id,
            ulong previousId,
            int depth,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            ulong handleAllocator,
            ulong eventSequence,
            FPActionStatePageReference actionState = default,
            FPInputRequestPageReference inputRequests = default,
            ulong actionEventSequence = 0)
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
        }

        internal FPCharacterRuntimeStateTransaction Owner { get; }
        internal ulong Id { get; }
        internal ulong PreviousId { get; }
        public int Depth { get; }
        internal GameplayEffectStateAggregate GameplayEffectState { get; }
        internal EquipmentStateAggregate EquipmentState { get; }
        internal ulong HandleAllocator { get; }
        internal ulong EventSequence { get; }
        internal FPActionStatePageReference ActionState { get; }
        internal FPInputRequestPageReference InputRequests { get; }
        internal ulong ActionEventSequence { get; }
        internal bool CapturesActivation => ActionState.Page != null;
    }

    internal sealed class FPCharacterRuntimeStateTransaction : IFPAbilityExecutionSavepointPort, IFPControlRuntimeStatePort
    {
        FPCharacterRuntimeState m_BaseState;
        AbilityStateValuePageReference<AbilityStateValue> m_CharacterBlackboards;
        GeneratedCharacterBlackboardLayout<AbilityStateValue> m_CharacterBlackboardLayout;
        MotionWarpStatePageReference<MotionWarpState> m_MotionWarpStates;
        Dictionary<CharacterSkillId, FPAbilityRuntimeState> m_AbilityStates;
        Dictionary<CharacterSkillId, FPSkillExecutionState> m_SkillStates;
        SimulationTick m_Tick;
        int m_TickRate;
        ulong m_NextSavepointId;
        ulong m_CurrentSavepointId;
        readonly List<FPAbilityExecutionSavepoint> m_SavepointSnapshots =
            new List<FPAbilityExecutionSavepoint>(GameplayEffectRuntimeCapacityLayout.MaximumSavepointDepth);
        FPCharacterActionRuntimeState m_ActionState;
        FPCharacterInputRequestState m_InputRequestState;
        FPCharacterEventSequenceState m_EventSequenceState;
        FPCharacterHandleAllocatorState m_HandleAllocatorState;
        FPCharacterGameplayEffectRuntimeState m_GameplayEffectState;
        FPCharacterEquipmentRuntimeState m_EquipmentState;
        CharacterControlRuntimeStateTransaction m_ControlState;
        bool m_ControlStateBound;
        bool m_AbilitiesChanged;
        bool m_Disposed;

        public FPCharacterRuntimeStateTransaction()
        {
            m_Disposed = true;
        }

        internal void Initialize(IReadOnlyList<FPGameplayAbilityExecutionInstallation> installations,
            MotionWarpStatePageLayout motionWarpLayout, GeneratedCharacterBlackboardLayout<AbilityStateValue> characterBlackboards)
        {
            m_CharacterBlackboardLayout = characterBlackboards;
            m_AbilityStates = new Dictionary<CharacterSkillId, FPAbilityRuntimeState>(installations.Count);
            m_SkillStates = new Dictionary<CharacterSkillId, FPSkillExecutionState>(installations.Count);
            for (int index = 0; index < installations.Count; index++)
            {
                FPGameplayAbilityExecutionInstallation installation = installations[index];
                m_SkillStates.Add(installation.Identity.AbilityId,
                    new FPSkillExecutionState(this, installation.Identity, installation.Layout,
                        installation.Data, motionWarpLayout.Partition(installation.Identity.AbilityId),
                        characterBlackboards.Slots(installation.Identity.AbilityId)));
            }
        }

        public FPCharacterRuntimeStateTransaction Restart(
            FPCharacterRuntimeState baseState,
            SimulationTick tick,
            int tickRate,
            FPGameplayEffectRuntimeCatalog gameplayEffectCatalog,
            FPCharacterInputRequestState inputRequestState,
            FPCharacterActionRuntimeState actionState,
            CharacterControlRuntimeStateTransaction controlState,
            FPCharacterEventSequenceState eventSequenceState,
            FPCharacterHandleAllocatorState handleAllocatorState,
            FPCharacterGameplayEffectRuntimeState gameplayEffectState,
            FPCharacterEquipmentRuntimeState equipmentState,
            MotionWarpStatePageReference<MotionWarpState> motionWarpStates)
        {
            m_BaseState = baseState;
            m_CharacterBlackboards = baseState.CharacterBlackboards;
            m_MotionWarpStates = motionWarpStates;
            if (!tick.IsValid || tickRate <= 0)
                throw new ArgumentException("FP Character runtime transaction timing is incomplete.");
            if (m_SavepointSnapshots.Count != 0)
                throw new InvalidOperationException("FP Character runtime transaction reuse found active savepoints.");
            m_Tick = tick;
            m_TickRate = tickRate;
            m_AbilityStates.Clear();
            for (int i = 0; i < baseState.Abilities.Count; i++)
            {
                FPAbilityRuntimeState state = baseState.Abilities[i];
                state.Retain();
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

        internal IFPSkillExecutionState BindAbility(CharacterSkillId abilityId)
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
                throw new InvalidOperationException("FP Character Control state is already bound.");
            if (!m_BaseState.ControlState.IsValid)
                throw new InvalidOperationException("FP Character runtime has no Control state.");
            m_ControlStateBound = true;
            return m_ControlState.Restart(
                m_BaseState.ControlState,
                schema,
                m_Tick);
        }

        internal void AcceptAbility(IFPSkillExecutionState skillState)
        {
            RequireActive();
            if (!(skillState is FPSkillExecutionState ability) ||
                !ReferenceEquals(ability.BindingIdentity, this))
            {
                throw new InvalidOperationException("FP Ability transaction belongs to another Character runtime transaction.");
            }
            FPAbilityRuntimeState state = ability.TakeSnapshot();
            if (!m_AbilityStates[state.AbilityIdentity.AbilityId].SharesStateWith(state))
            {
                m_AbilityStates[state.AbilityIdentity.AbilityId].Release();
                m_AbilitiesChanged = true;
            }
            m_AbilityStates[state.AbilityIdentity.AbilityId] = state;
        }

        internal FPCharacterRuntimeState Commit()
        {
            RequireActive();
            if (m_SavepointSnapshots.Count != 0)
                throw new InvalidOperationException("Character runtime state transaction has active savepoints.");
            return Snapshot();
        }

        internal FPCharacterInputRequestState InputRequests => m_InputRequestState;
        internal IFPActionRuntimeStatePort ActionState => m_ActionState;
        internal IFPEventSequenceStatePort EventSequenceState => m_EventSequenceState;
        internal IFPHandleAllocatorStatePort HandleAllocatorState => m_HandleAllocatorState;
        internal IFPGameplayEffectStatePort GameplayEffectState => m_GameplayEffectState;
        internal IFPEquipmentStatePort EquipmentState => m_EquipmentState;

        public FPAbilityExecutionSavepoint CreateSavepoint()
        {
            RequireActive();
            GameplayEffectStateAggregate gameplayEffectState = m_GameplayEffectState.Capture();
            EquipmentStateAggregate equipmentState = m_EquipmentState.Capture();
            ulong id = checked(m_NextSavepointId + 1);
            var savepoint = new FPAbilityExecutionSavepoint(
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

        public FPAbilityExecutionSavepoint CreateActivationSavepoint()
        {
            FPAbilityExecutionSavepoint current = CreateSavepoint();
            FPActionStatePageReference actions = m_ActionState.Capture();
            FPInputRequestPageReference requests = m_InputRequestState.Capture();
            actions.Retain();
            requests.Retain();
            var savepoint = new FPAbilityExecutionSavepoint(this, current.Id, current.PreviousId,
                current.Depth, current.GameplayEffectState, current.EquipmentState, current.HandleAllocator,
                current.EventSequence, actions, requests, m_ActionState.ActionEventSequence);
            m_SavepointSnapshots[m_SavepointSnapshots.Count - 1] = savepoint;
            return savepoint;
        }

        public void Restore(FPAbilityExecutionSavepoint savepoint)
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
                m_InputRequestState.RestoreActivation(savepoint.InputRequests);
            }
            CompleteSavepoint(savepoint);
        }

        public void Release(FPAbilityExecutionSavepoint savepoint)
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
            foreach (KeyValuePair<CharacterSkillId, FPAbilityRuntimeState> ability in m_AbilityStates)
            {
                ability.Value.Release();
            }
            if (!ReferenceEquals(m_CharacterBlackboards.Set, m_BaseState.CharacterBlackboards.Set))
                m_CharacterBlackboards.Release();
            m_BaseState.ReleaseStatePages();
            m_Disposed = true;
        }

        FPCharacterRuntimeState Snapshot()
        {
            FPAbilityStateDirectoryReference abilities;
            if (m_AbilitiesChanged)
            {
                abilities = m_BaseState.AbilityDirectory.Page.Bank.Begin();
                FPAbilityRuntimeState[] source = m_BaseState.AbilityDirectory.Page.Values;
                for (int abilityIndex = 0; abilityIndex < source.Length; abilityIndex++)
                    abilities.Page.Values[abilityIndex] = m_AbilityStates[source[abilityIndex].AbilityIdentity.AbilityId];
            }
            else
            {
                abilities = m_BaseState.AbilityDirectory;
            }
            FPActionStatePageReference actions = m_ActionState.Capture();
            FPInputRequestPageReference inputRequests = m_InputRequestState.Capture();
            FPCharacterRuntimeStatePage page = m_BaseState.StatePages.Begin();
            page.Data = FPCharacterRuntimeStateData.FromValueStorage(
                CharacterBlackboards: m_CharacterBlackboards,
                Metadata: new CharacterRuntimeStateMetadata(m_BaseState.NumericProfile, m_BaseState.GameplayContentHash,
                    m_BaseState.StateSchemaHash, m_Tick.Value, m_EventSequenceState.EventSequence,
                    m_ActionState.ActionEventSequence, m_HandleAllocatorState.HandleAllocator),
                Abilities: abilities,
                ActionStatePage: actions,
                InputRequestPage: inputRequests,
                ControlState: m_ControlState.Capture(page.ControlStorage),
                GameplayEffectState: m_GameplayEffectState.Commit(),
                EquipmentState: m_EquipmentState.Capture(),
                TimelineSnapshots: m_BaseState.TimelineSnapshotPage,
                MotionWarpStates: m_MotionWarpStates);
            return new FPCharacterRuntimeState(page);
        }

        void RequireTopSavepoint(FPAbilityExecutionSavepoint savepoint)
        {
            if (!ReferenceEquals(savepoint.Owner, this) || savepoint.Id != m_CurrentSavepointId)
                throw new InvalidOperationException("FP Ability execution savepoint is stale or unbalanced.");
        }

        void CompleteSavepoint(FPAbilityExecutionSavepoint savepoint)
        {
            m_CurrentSavepointId = savepoint.PreviousId;
            int index = m_SavepointSnapshots.Count - 1;
            ReleaseSavepointStorage(m_SavepointSnapshots[index]);
            m_SavepointSnapshots.RemoveAt(index);
        }

        static void ReleaseSavepointStorage(FPAbilityExecutionSavepoint savepoint)
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
                throw new ObjectDisposedException(nameof(FPCharacterRuntimeStateTransaction));
        }
    }

    internal sealed class FPSkillExecutionState : IFPSkillExecutionState
    {
        readonly FPCharacterRuntimeStateTransaction m_BindingIdentity;
        readonly int[] m_CharacterSlots;
        readonly GameplayAbilityExecutionLayout m_Layout;
        readonly FPGameplayAbilityExecutionData m_Ability;
        FPAbilityRuntimeState m_CommittedState;
        AbilityStateValuePageReference<AbilityStateValue> m_StateValues;
        MotionWarpStatePageReference<MotionWarpState> m_MotionWarpStates;
        readonly int m_MotionWarpPartition;
        GameplayAbilityExecutionAggregate<AbilityStateValue> m_AbilityExecutionState;
        bool m_StateValuesChanged;
        bool m_Disposed;

        public FPSkillExecutionState(
            FPCharacterRuntimeStateTransaction bindingIdentity,
            GameplayAbilityExecutionIdentity identity,
            GameplayAbilityExecutionLayout layout,
            FPGameplayAbilityExecutionData ability,
            int motionWarpPartition, int[] characterSlots)
        {
            m_BindingIdentity = bindingIdentity ?? throw new ArgumentNullException(nameof(bindingIdentity));
            if (!identity.IsValid)
                throw new ArgumentException("FP Ability execution identity is incomplete.", nameof(identity));
            m_Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            m_Ability = ability ?? throw new ArgumentNullException(nameof(ability));
            if (m_Ability.AbilityId != identity.AbilityId)
                throw new ArgumentException("FP Ability execution data does not match its identity.", nameof(ability));
            m_MotionWarpPartition = motionWarpPartition;
            m_CharacterSlots = characterSlots;
            m_Disposed = true;
        }

        internal FPSkillExecutionState Restart(
            FPAbilityRuntimeState state,
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

        public void SetMotionWarpState(OperationHandle operation, in MotionWarpState value)
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

        internal FPAbilityRuntimeState TakeSnapshot()
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
                FPAbilityRuntimeState committed = m_CommittedState;
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
            FPAbilityRuntimeState snapshot = FPAbilityRuntimeState.Adopt(
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
                throw new ObjectDisposedException(nameof(FPSkillExecutionState));
        }
    }
}
