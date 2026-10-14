using MotionWarpState = ThirdPersonSimulation.MotionWarpState<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32ActionInstanceReference>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal readonly struct Float32AbilityExecutionSavepoint
    {
        internal Float32AbilityExecutionSavepoint(
            Float32CharacterRuntimeStateTransaction owner,
            ulong id,
            ulong previousId,
            int depth,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            ulong handleAllocator,
            ulong eventSequence,
            Float32ActionStatePageReference actionState = default,
            Float32InputRequestPageReference inputRequests = default,
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

        internal Float32CharacterRuntimeStateTransaction Owner { get; }
        internal ulong Id { get; }
        internal ulong PreviousId { get; }
        public int Depth { get; }
        internal GameplayEffectStateAggregate GameplayEffectState { get; }
        internal EquipmentStateAggregate EquipmentState { get; }
        internal ulong HandleAllocator { get; }
        internal ulong EventSequence { get; }
        internal Float32ActionStatePageReference ActionState { get; }
        internal Float32InputRequestPageReference InputRequests { get; }
        internal ulong ActionEventSequence { get; }
        internal int ChangedRequestCount { get; }
        internal bool CapturesActivation => ActionState.Page != null;
    }

    internal sealed class Float32CharacterRuntimeStateTransaction : IFloat32AbilityExecutionSavepointPort, IFloat32ControlRuntimeStatePort
    {
        Float32CharacterRuntimeState m_BaseState;
        AbilityStateValuePageReference<AbilityStateValue> m_CharacterBlackboards;
        GeneratedCharacterBlackboardLayout<AbilityStateValue> m_CharacterBlackboardLayout;
        MotionWarpStatePageReference<MotionWarpState> m_MotionWarpStates;
        Dictionary<CharacterSkillId, Float32AbilityRuntimeState> m_AbilityStates;
        Dictionary<CharacterSkillId, Float32SkillExecutionState> m_SkillStates;
        SimulationTick m_Tick;
        int m_TickRate;
        ulong m_NextSavepointId;
        ulong m_CurrentSavepointId;
        readonly List<Float32AbilityExecutionSavepoint> m_SavepointSnapshots =
            new List<Float32AbilityExecutionSavepoint>(GameplayEffectRuntimeCapacityLayout.MaximumSavepointDepth);
        Float32CharacterActionRuntimeState m_ActionState;
        Float32CharacterInputRequestState m_InputRequestState;
        Float32CharacterEventSequenceState m_EventSequenceState;
        Float32CharacterHandleAllocatorState m_HandleAllocatorState;
        Float32CharacterGameplayEffectRuntimeState m_GameplayEffectState;
        Float32CharacterEquipmentRuntimeState m_EquipmentState;
        CharacterControlRuntimeStateTransaction m_ControlState;
        bool m_ControlStateBound;
        bool m_Disposed;

        public Float32CharacterRuntimeStateTransaction()
        {
            m_Disposed = true;
        }

        internal void Initialize(IReadOnlyList<Float32GameplayAbilityExecutionInstallation> installations,
            MotionWarpStatePageLayout motionWarpLayout, GeneratedCharacterBlackboardLayout<AbilityStateValue> characterBlackboards)
        {
            m_CharacterBlackboardLayout = characterBlackboards;
            m_AbilityStates = new Dictionary<CharacterSkillId, Float32AbilityRuntimeState>(installations.Count);
            m_SkillStates = new Dictionary<CharacterSkillId, Float32SkillExecutionState>(installations.Count);
            for (int index = 0; index < installations.Count; index++)
            {
                Float32GameplayAbilityExecutionInstallation installation = installations[index];
                m_SkillStates.Add(installation.Identity.AbilityId,
                    new Float32SkillExecutionState(this, installation.Identity, installation.Layout,
                        installation.Data, motionWarpLayout.Partition(installation.Identity.AbilityId),
                        characterBlackboards.Slots(installation.Identity.AbilityId)));
            }
        }

        public Float32CharacterRuntimeStateTransaction Restart(
            Float32CharacterRuntimeState baseState,
            SimulationTick tick,
            int tickRate,
            Float32GameplayEffectRuntimeCatalog gameplayEffectCatalog,
            Float32CharacterInputRequestState inputRequestState,
            Float32CharacterActionRuntimeState actionState,
            CharacterControlRuntimeStateTransaction controlState,
            Float32CharacterEventSequenceState eventSequenceState,
            Float32CharacterHandleAllocatorState handleAllocatorState,
            Float32CharacterGameplayEffectRuntimeState gameplayEffectState,
            Float32CharacterEquipmentRuntimeState equipmentState,
            MotionWarpStatePageReference<MotionWarpState> motionWarpStates)
        {
            m_BaseState = baseState ?? throw new ArgumentNullException(nameof(baseState));
            m_CharacterBlackboards = baseState.CharacterBlackboards;
            m_MotionWarpStates = motionWarpStates;
            if (!tick.IsValid || tickRate <= 0)
                throw new ArgumentException("Float32 Character runtime transaction timing is incomplete.");
            if (m_SavepointSnapshots.Count != 0)
                throw new InvalidOperationException("Float32 Character runtime transaction reuse found active savepoints.");
            m_Tick = tick;
            m_TickRate = tickRate;
            m_AbilityStates.Clear();
            for (int i = 0; i < baseState.Abilities.Count; i++)
            {
                Float32AbilityRuntimeState state = baseState.Abilities[i];
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

        internal IFloat32SkillExecutionState BindAbility(
            GameplayAbilityExecutionIdentity identity,
            GameplayAbilityExecutionLayout layout,
            Float32GameplayAbilityExecutionData ability)
        {
            RequireActive();
            return m_SkillStates[identity.AbilityId].Restart(m_AbilityStates[identity.AbilityId], m_MotionWarpStates);
        }

        public CharacterControlRuntimeStateTransaction BindControl(CharacterControlStateSchema schema)
        {
            RequireActive();
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));
            if (m_ControlStateBound)
                throw new InvalidOperationException("Float32 Character Control state is already bound.");
            if (!m_BaseState.ControlState.IsValid)
                throw new InvalidOperationException("Float32 Character runtime has no Control state.");
            m_ControlStateBound = true;
            return m_ControlState.Restart(
                m_BaseState.ControlState,
                schema,
                m_Tick);
        }

        internal void AcceptAbility(IFloat32SkillExecutionState skillState)
        {
            RequireActive();
            if (!(skillState is Float32SkillExecutionState ability) ||
                !ReferenceEquals(ability.BindingIdentity, this))
            {
                throw new InvalidOperationException("Float32 Ability transaction belongs to another Character runtime transaction.");
            }
            Float32AbilityRuntimeState state = ability.TakeSnapshot();
            if (!m_AbilityStates[state.AbilityIdentity.AbilityId].SharesStateWith(state))
            {
                m_AbilityStates[state.AbilityIdentity.AbilityId].Release();
            }
            m_AbilityStates[state.AbilityIdentity.AbilityId] = state;
        }

        internal Float32CharacterRuntimeState Commit()
        {
            RequireActive();
            if (m_SavepointSnapshots.Count != 0)
                throw new InvalidOperationException("Character runtime state transaction has active savepoints.");
            return Snapshot();
        }

        internal Float32CharacterInputRequestState InputRequests => m_InputRequestState;
        internal IFloat32ActionRuntimeStatePort ActionState => m_ActionState;
        internal IFloat32EventSequenceStatePort EventSequenceState => m_EventSequenceState;
        internal IFloat32HandleAllocatorStatePort HandleAllocatorState => m_HandleAllocatorState;
        internal IFloat32GameplayEffectStatePort GameplayEffectState => m_GameplayEffectState;
        internal IFloat32EquipmentStatePort EquipmentState => m_EquipmentState;

        public Float32AbilityExecutionSavepoint CreateSavepoint()
        {
            RequireActive();
            GameplayEffectStateAggregate gameplayEffectState = m_GameplayEffectState.Capture();
            EquipmentStateAggregate equipmentState = m_EquipmentState.Capture();
            ulong id = checked(m_NextSavepointId + 1);
            var savepoint = new Float32AbilityExecutionSavepoint(
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

        public Float32AbilityExecutionSavepoint CreateActivationSavepoint()
        {
            Float32AbilityExecutionSavepoint current = CreateSavepoint();
            Float32ActionStatePageReference actions = m_ActionState.Capture();
            Float32InputRequestPageReference requests = m_InputRequestState.CaptureActivation(out int changedRequestCount);
            actions.Retain();
            requests.Retain();
            var savepoint = new Float32AbilityExecutionSavepoint(this, current.Id, current.PreviousId,
                current.Depth, current.GameplayEffectState, current.EquipmentState, current.HandleAllocator,
                current.EventSequence, actions, requests, m_ActionState.ActionEventSequence, changedRequestCount);
            m_SavepointSnapshots[m_SavepointSnapshots.Count - 1] = savepoint;
            return savepoint;
        }

        public void Restore(Float32AbilityExecutionSavepoint savepoint)
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

        public void Release(Float32AbilityExecutionSavepoint savepoint)
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
            for (int i = m_SavepointSnapshots.Count - 1; i >= 0; i--)
                ReleaseSavepointStorage(m_SavepointSnapshots[i]);
            m_SavepointSnapshots.Clear();
            m_ControlState?.Dispose();
            m_InputRequestState.Dispose();
            m_ActionState.Dispose();
            m_EventSequenceState.Dispose();
            m_HandleAllocatorState.Dispose();
            m_GameplayEffectState.Dispose();
            m_EquipmentState.Dispose();
            foreach (KeyValuePair<CharacterSkillId, Float32SkillExecutionState> skill in m_SkillStates)
                skill.Value.Dispose();
            foreach (KeyValuePair<CharacterSkillId, Float32AbilityRuntimeState> ability in m_AbilityStates)
            {
                ability.Value.Release();
            }
            if (!ReferenceEquals(m_CharacterBlackboards.Set, m_BaseState.CharacterBlackboards.Set))
                m_CharacterBlackboards.Release();
            m_BaseState.ReleaseStatePages();
            m_Disposed = true;
        }

        Float32CharacterRuntimeState Snapshot()
        {
            Float32CharacterRuntimeStatePage page = m_BaseState.StatePages.Begin(m_BaseState.Page);
            Float32AbilityRuntimeState[] source = m_BaseState.AbilitiesArray;
            for (int index = 0; index < source.Length; index++)
                page.Abilities[index] = m_AbilityStates[source[index].AbilityIdentity.AbilityId];
            page.TimelineValues.AddRange(m_BaseState.Page.TimelineValues);
            Float32ActionStatePageReference actions = m_ActionState.Capture();
            Float32InputRequestPageReference inputRequests = m_InputRequestState.Capture();
            CharacterControlRuntimeState control = m_ControlStateBound
                ? m_ControlState.Capture(page.ControlStorage)
                : m_BaseState.ControlState.IsValid ? page.ControlStorage.CopyFrom(m_BaseState.ControlState) : default;
            page.Data = Float32CharacterRuntimeStateData.FromValueStorage(
                CharacterBlackboards: m_CharacterBlackboards,
                Metadata: new CharacterRuntimeStateMetadata(m_BaseState.NumericProfile, m_BaseState.GameplayContentHash,
                    m_BaseState.StateSchemaHash, m_Tick.Value, m_EventSequenceState.EventSequence,
                    m_ActionState.ActionEventSequence, m_HandleAllocatorState.HandleAllocator),
                ActionStatePage: actions,
                InputRequestPage: inputRequests,
                ControlState: control,
                GameplayEffectState: m_GameplayEffectState.Commit(),
                EquipmentState: m_EquipmentState.Capture(),
                MotionWarpStates: m_MotionWarpStates);
            return page.State;
        }

        void RequireTopSavepoint(Float32AbilityExecutionSavepoint savepoint)
        {
            if (!ReferenceEquals(savepoint.Owner, this) || savepoint.Id != m_CurrentSavepointId)
                throw new InvalidOperationException("Float32 Ability execution savepoint is stale or unbalanced.");
        }

        void CompleteSavepoint(Float32AbilityExecutionSavepoint savepoint)
        {
            m_CurrentSavepointId = savepoint.PreviousId;
            int index = m_SavepointSnapshots.Count - 1;
            ReleaseSavepointStorage(m_SavepointSnapshots[index]);
            m_SavepointSnapshots.RemoveAt(index);
        }

        static void ReleaseSavepointStorage(Float32AbilityExecutionSavepoint savepoint)
        {
            if (savepoint.GameplayEffectState.IsValid)
                savepoint.GameplayEffectState.ReleaseSnapshots();
            savepoint.EquipmentState?.Release();
            if (savepoint.CapturesActivation)
            {
                savepoint.InputRequests.Release();
                savepoint.ActionState.Release();
            }
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32CharacterRuntimeStateTransaction));
        }
    }

    internal sealed class Float32SkillExecutionState : IFloat32SkillExecutionState
    {
        readonly Float32CharacterRuntimeStateTransaction m_BindingIdentity;
        readonly int[] m_CharacterSlots;
        readonly GameplayAbilityExecutionLayout m_Layout;
        readonly Float32GameplayAbilityExecutionData m_Ability;
        Float32AbilityRuntimeState m_CommittedState;
        AbilityStateValuePageReference<AbilityStateValue> m_StateValues;
        MotionWarpStatePageReference<MotionWarpState> m_MotionWarpStates;
        readonly int m_MotionWarpPartition;
        GameplayAbilityExecutionAggregate<AbilityStateValue> m_AbilityExecutionState;
        bool m_StateValuesChanged;
        bool m_Disposed;

        public Float32SkillExecutionState(
            Float32CharacterRuntimeStateTransaction bindingIdentity,
            GameplayAbilityExecutionIdentity identity,
            GameplayAbilityExecutionLayout layout,
            Float32GameplayAbilityExecutionData ability,
            int motionWarpPartition, int[] characterSlots)
        {
            m_BindingIdentity = bindingIdentity ?? throw new ArgumentNullException(nameof(bindingIdentity));
            if (!identity.IsValid)
                throw new ArgumentException("Float32 Ability execution identity is incomplete.", nameof(identity));
            m_Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            m_Ability = ability ?? throw new ArgumentNullException(nameof(ability));
            if (m_Ability.AbilityId != identity.AbilityId)
                throw new ArgumentException("Float32 Ability execution data does not match its identity.", nameof(ability));
            m_MotionWarpPartition = motionWarpPartition;
            m_CharacterSlots = characterSlots;
            m_Disposed = true;
        }

        internal Float32SkillExecutionState Restart(
            Float32AbilityRuntimeState state,
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

        internal Float32AbilityRuntimeState TakeSnapshot()
        {
            RequireActive();
            IReadOnlyList<int> operations = m_MotionWarpStates.Page.Layout.Operations(m_MotionWarpPartition);
            int offset = m_MotionWarpStates.Page.Layout.Offset(m_MotionWarpPartition);
            for (int index = 0; index < operations.Count; index++)
            {
                ref readonly MotionWarpState warp = ref m_MotionWarpStates.Page.At(offset + index);
                if (warp.Active && m_AbilityExecutionState.Find(warp.ActionInstance.InstanceId) == null)
                    m_MotionWarpStates.Page.Set(offset + index, default);
            }
            bool abilityExecutionStateChanged = !m_AbilityExecutionState.Equals(m_CommittedState.AbilityExecutionState);
            if (!m_StateValuesChanged && !abilityExecutionStateChanged)
            {
                Float32AbilityRuntimeState committed = m_CommittedState;
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
            Float32AbilityRuntimeState snapshot = Float32AbilityRuntimeState.Adopt(
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

        void Clear()
        {
            m_StateValues = default;
            m_StateValuesChanged = false;
            m_MotionWarpStates = default;
            m_AbilityExecutionState = null;
            m_Disposed = true;
        }

        AbilityStateValue DefaultValue(int slotIndex)
        {
            if (m_StateValues.Set.Bank.Blackboards.Contains(slotIndex))
                return m_StateValues.Set.Bank.Blackboards.DefaultValue(slotIndex);
            ProgramStateSlot slot = m_Layout.StateSlots[slotIndex];
            return slot.DefaultConstantIndex >= 0
                ? AbilityStateValue.FromConstant(m_Ability.Constants[slot.DefaultConstantIndex], slot.ValueKind)
                : AbilityStateValue.Default(slot.ValueKind);
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32SkillExecutionState));
        }
    }
}
