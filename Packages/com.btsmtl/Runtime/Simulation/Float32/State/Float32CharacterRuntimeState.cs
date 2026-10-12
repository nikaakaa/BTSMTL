using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation
{
    public readonly struct Float32AbilityRuntimeState
    {
        Float32AbilityRuntimeState(
            AbilityStateValuePageReference<AbilityStateValue> stateValues,
            GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState)
        {
            StateValues = stateValues;
            AbilityExecutionState = abilityExecutionState;
        }

        internal AbilityStateValuePageReference<AbilityStateValue> StateValues { get; }
        internal GameplayAbilityExecutionAggregate<AbilityStateValue> AbilityExecutionState { get; }
        public GameplayAbilityExecutionIdentity AbilityIdentity => StateValues.Set.Bank.Identity;

        internal bool SharesStateWith(Float32AbilityRuntimeState other) =>
            ReferenceEquals(StateValues.Set, other.StateValues.Set) &&
            StateValues.Generation == other.StateValues.Generation &&
            ReferenceEquals(AbilityExecutionState, other.AbilityExecutionState);

        internal static Float32AbilityRuntimeState Adopt(
            AbilityStateValuePageReference<AbilityStateValue> stateValues,
            GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState) =>
            new Float32AbilityRuntimeState(stateValues, abilityExecutionState);
    }

    public sealed class Float32CharacterRuntimeState : ICommittedCharacterControlState
    {
        readonly Float32CharacterRuntimeStatePage m_Page;

        internal Float32CharacterRuntimeState(Float32CharacterRuntimeStatePage page) => m_Page = page;

        internal Float32CharacterRuntimeStatePage Page => m_Page;
        internal SimulationCharacterStatePayload CanonicalPayload => m_Page.CanonicalPayload;
        internal void DiscardDecodedStorage() => m_Page.Clear();

        internal void SetCanonicalPayload(SimulationCharacterStatePayload payload)
        {
            m_Page.ClearCanonicalPayload();
            payload.Retain();
            m_Page.CanonicalPayload = payload;
        }
        internal Float32CharacterRuntimeStatePageBank StatePages => m_Page.Bank;
        public SimulationNumericProfile NumericProfile => m_Page.Data.NumericProfile;
        public GameplayContentHash GameplayContentHash => m_Page.Data.GameplayContentHash;
        public StableHash StateSchemaHash => m_Page.Data.StateSchemaHash;
        public ulong LastCompletedTick => m_Page.Data.LastCompletedTick;
        public IReadOnlyList<Float32AbilityRuntimeState> Abilities => m_Page.Abilities;
        internal Float32AbilityRuntimeState[] AbilitiesArray => m_Page.Abilities;
        internal MotionWarpStatePageReference<Float32MotionWarpState> MotionWarpStates => m_Page.Data.MotionWarpStates;
        internal AbilityStateValuePageReference<AbilityStateValue> CharacterBlackboards => m_Page.Data.CharacterBlackboards;
        internal Float32ActionStatePageReference ActionStatePage => m_Page.Data.ActionStatePage;
        internal ReadOnlySpan<SimulationActionActivationRequestState> ActionActivationRequests => ActionStatePage.Requests;
        internal ReadOnlySpan<Float32ActionInstanceState> ActionInstances => ActionStatePage.Actions;
        internal Float32InputRequestPageReference InputRequestPage => m_Page.Data.InputRequestPage;
        internal ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>> InputRequests => InputRequestPage.Values;
        internal ulong EventSequence => m_Page.Data.EventSequence;
        internal ulong ActionEventSequence => m_Page.Data.ActionEventSequence;
        internal ulong HandleAllocator => m_Page.Data.HandleAllocator;
        public CharacterControlRuntimeState ControlState => m_Page.Data.ControlState;
        internal GameplayEffectStateAggregate GameplayEffectState => m_Page.Data.GameplayEffectState;
        internal EquipmentStateAggregate EquipmentState => m_Page.Data.EquipmentState;
        public IReadOnlyList<AbilityTimelineRuntimeSnapshot> TimelineSnapshots => m_Page.TimelineView;

        static readonly Comparison<Float32AbilityRuntimeState> s_CompareAbilities =
            (left, right) => left.AbilityIdentity.AbilityId.CompareTo(right.AbilityIdentity.AbilityId);

        public bool TryGetEquipmentState(out EquipmentStateAggregate equipment)
        {
            equipment = EquipmentState;
            return equipment != null;
        }

        public bool IsAbilityActive(CharacterSkillId abilityId)
        {
            ReadOnlySpan<Float32ActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].IsActive && actions[i].SkillId == abilityId)
                    return true;
            return false;
        }

        internal void RetainStatePages()
        {
            m_Page.Retainers++;
            MotionWarpStates.Retain();
            CharacterBlackboards.Retain();
            ActionStatePage.Retain();
            InputRequestPage.Retain();
            GameplayEffectState.RetainSnapshots();
            EquipmentState?.Retain();
            EquipmentState?.Storage.CompleteDecodeAdoption(EquipmentState);
            for (int index = 0; index < m_Page.Abilities.Length; index++)
            {
                m_Page.Abilities[index].StateValues.Retain();
                m_Page.Abilities[index].AbilityExecutionState.RetainStateStorage();
            }
            for (int index = 0; index < m_Page.TimelineValues.Count; index++)
                m_Page.TimelineValues[index].RetainStateStorage();
        }

        internal void ReleaseStatePages()
        {
            CharacterBlackboards.Release();
            for (int index = 0; index < m_Page.TimelineValues.Count; index++)
                m_Page.TimelineValues[index].ReleaseStateStorage();
            for (int index = 0; index < m_Page.Abilities.Length; index++)
            {
                m_Page.Abilities[index].AbilityExecutionState.ReleaseStateStorage();
                m_Page.Abilities[index].StateValues.Release();
            }
            GameplayEffectState.ReleaseSnapshots();
            EquipmentState?.Release();
            InputRequestPage.Release();
            ActionStatePage.Release();
            MotionWarpStates.Release();
            if (--m_Page.Retainers == 0)
                m_Page.Clear();
        }

        internal Float32CharacterRuntimeState WithTimelineSnapshot(AbilityTimelineRuntimeSnapshot snapshot)
        {
            if (!snapshot.IsValid)
                throw new ArgumentException("Float32 Character Timeline snapshot is invalid.", nameof(snapshot));
            Float32CharacterRuntimeStatePage page = ClonePage();
            List<AbilityTimelineRuntimeSnapshot> snapshots = page.TimelineValues;
            for (int index = 0; index < snapshots.Count; index++)
            {
                if (snapshots[index].RuntimeHandle != snapshot.RuntimeHandle)
                    continue;
                snapshots[index] = snapshot;
                return page.State;
            }
            int first = 0;
            int last = snapshots.Count;
            while (first < last)
            {
                int middle = first + ((last - first) >> 1);
                if (snapshots[middle].RuntimeHandle < snapshot.RuntimeHandle)
                    first = middle + 1;
                else
                    last = middle;
            }
            snapshots.Insert(first, snapshot);
            return page.State;
        }

        internal Float32CharacterRuntimeState WithoutTimelineSnapshot(ulong runtimeHandle)
        {
            if (runtimeHandle == 0)
                throw new ArgumentOutOfRangeException(nameof(runtimeHandle));
            int removedIndex = -1;
            for (int index = 0; index < m_Page.TimelineValues.Count; index++)
                if (m_Page.TimelineValues[index].RuntimeHandle == runtimeHandle)
                {
                    removedIndex = index;
                    break;
                }
            if (removedIndex < 0)
                return this;
            Float32CharacterRuntimeStatePage page = ClonePage();
            page.TimelineValues.RemoveAt(removedIndex);
            return page.State;
        }

        internal Float32CharacterRuntimeState WithoutUnownedTerminalTimelines()
        {
            int firstRemoved = -1;
            for (int index = 0; index < m_Page.TimelineValues.Count; index++)
                if (!RetainTimelineSnapshot(m_Page.TimelineValues[index]))
                {
                    firstRemoved = index;
                    break;
                }
            if (firstRemoved < 0)
                return this;
            Float32CharacterRuntimeStatePage page = ClonePage();
            List<AbilityTimelineRuntimeSnapshot> snapshots = page.TimelineValues;
            for (int index = snapshots.Count - 1; index >= firstRemoved; index--)
                if (!RetainTimelineSnapshot(snapshots[index]))
                    snapshots.RemoveAt(index);
            return page.State;
        }

        bool RetainTimelineSnapshot(in AbilityTimelineRuntimeSnapshot snapshot)
        {
            if (snapshot.State != BTSMTL.Timeline.TimelineRuntimePlaybackState.Completed && snapshot.State != BTSMTL.Timeline.TimelineRuntimePlaybackState.Stopped)
                return true;
            ReadOnlySpan<Float32ActionInstanceState> actions = ActionInstances;
            for (int index = 0; index < actions.Length; index++)
            {
                Float32ActionInstanceState action = actions[index];
                if (action.IsActive && action.InstanceId == snapshot.ActionContext.InstanceId &&
                    action.SkillExecutionGeneration == snapshot.ActionContext.SkillExecutionGeneration)
                    return true;
            }
            return false;
        }

        Float32CharacterRuntimeStatePage ClonePage()
        {
            Float32CharacterRuntimeStatePage page = StatePages.Begin(m_Page);
            page.Data = m_Page.Data;
            page.Data.ControlState = ControlState.IsValid ? page.ControlStorage.CopyFrom(ControlState) : default;
            Array.Copy(m_Page.Abilities, page.Abilities, page.Abilities.Length);
            page.TimelineValues.AddRange(m_Page.TimelineValues);
            return page;
        }

        internal static Float32CharacterRuntimeState AdoptPrepared(
            Float32CharacterRuntimeStatePage page,
            SimulationNumericProfile numericProfile,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            ulong lastCompletedTick,
            Float32ActionStatePageReference actions,
            Float32InputRequestPageReference inputRequests,
            ulong eventSequence,
            ulong actionEventSequence,
            ulong handleAllocator,
            CharacterControlRuntimeState controlState,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            MotionWarpStatePageReference<Float32MotionWarpState> motionWarpStates)
        {
            ref Float32CharacterRuntimeStateData data = ref page.Data;
            data.NumericProfile = numericProfile;
            data.GameplayContentHash = gameplayContentHash;
            data.StateSchemaHash = stateSchemaHash;
            data.LastCompletedTick = lastCompletedTick;
            data.ActionStatePage = actions;
            data.InputRequestPage = inputRequests;
            data.EventSequence = eventSequence;
            data.ActionEventSequence = actionEventSequence;
            data.HandleAllocator = handleAllocator;
            data.ControlState = controlState;
            data.GameplayEffectState = gameplayEffectState;
            data.EquipmentState = equipmentState;
            data.MotionWarpStates = motionWarpStates;
            return page.State;
        }

        internal static Float32CharacterRuntimeState CreateInitial(
            Float32CharacterRuntimeStatePage page,
            SimulationNumericProfile numericProfile,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            CharacterControlRuntimeState controlState,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            Float32ActionStatePageReference actions,
            Float32InputRequestPageReference inputRequests,
            MotionWarpStatePageReference<Float32MotionWarpState> motionWarpStates)
        {
            Array.Sort(page.Abilities, s_CompareAbilities);
            return AdoptPrepared(page, numericProfile, gameplayContentHash, stateSchemaHash, 0,
                actions, inputRequests, 0, 0, 0, page.ControlStorage.CopyFrom(controlState),
                gameplayEffectState, equipmentState, motionWarpStates);
        }
    }

    internal struct Float32CharacterRuntimeStateData
    {
        internal AbilityStateValuePageReference<AbilityStateValue> CharacterBlackboards;
        internal SimulationNumericProfile NumericProfile;
        internal GameplayContentHash GameplayContentHash;
        internal StableHash StateSchemaHash;
        internal ulong LastCompletedTick;
        internal Float32ActionStatePageReference ActionStatePage;
        internal Float32InputRequestPageReference InputRequestPage;
        internal ulong EventSequence;
        internal ulong ActionEventSequence;
        internal ulong HandleAllocator;
        internal CharacterControlRuntimeState ControlState;
        internal GameplayEffectStateAggregate GameplayEffectState;
        internal EquipmentStateAggregate EquipmentState;
        internal MotionWarpStatePageReference<Float32MotionWarpState> MotionWarpStates;
    }

    internal sealed class Float32CharacterRuntimeStatePage
    {
        internal Float32CharacterRuntimeStatePage(Float32CharacterRuntimeStatePageBank bank, int abilityCount, int controlValueCount)
        {
            Bank = bank;
            Abilities = new Float32AbilityRuntimeState[abilityCount];
            ControlStorage = new CharacterControlRuntimeStateStorage(controlValueCount);
            TimelineValues = new List<AbilityTimelineRuntimeSnapshot>(Float32CharacterRuntimeStatePageBank.MaximumTimelineSnapshotCount);
            TimelineView = TimelineValues.AsReadOnly();
            State = new Float32CharacterRuntimeState(this);
        }

        internal readonly Float32CharacterRuntimeStatePageBank Bank;
        internal readonly Float32CharacterRuntimeState State;
        internal readonly Float32AbilityRuntimeState[] Abilities;
        internal readonly List<AbilityTimelineRuntimeSnapshot> TimelineValues;
        internal readonly ReadOnlyCollection<AbilityTimelineRuntimeSnapshot> TimelineView;
        internal CharacterControlRuntimeStateStorage ControlStorage;
        internal Float32CharacterRuntimeStateData Data;
        internal SimulationCharacterStatePayload CanonicalPayload;
        internal int Retainers;

        internal void Clear()
        {
            ClearCanonicalPayload();
            Data = default;
            Array.Clear(Abilities, 0, Abilities.Length);
            TimelineValues.Clear();
            ControlStorage.Clear();
        }

        internal void ClearCanonicalPayload()
        {
            CanonicalPayload.Release();
            CanonicalPayload = default;
        }
    }

    internal sealed class Float32CharacterRuntimeStatePageBank
    {
        internal const int MaximumTimelineSnapshotCount = AbilityTimelineRuntimeLimits.MaximumPlaybackCount;
        readonly List<Float32CharacterRuntimeStatePage> m_Pages = new List<Float32CharacterRuntimeStatePage>();
        readonly int m_AbilityCount;
        int m_ControlValueCount;
        AbilityTimelineSnapshotStorageBank m_DecodeStorage;
        internal SimulationCharacterStatePayloadStorage EncodingStorage { get; set; }

        internal void ReleaseEncodingStorage()
        {
            for (int index = 0; index < m_Pages.Count; index++)
                m_Pages[index].ClearCanonicalPayload();
            EncodingStorage = null;
        }
        internal AbilityTimelineSnapshotDecodeLayout DecodeLayout { get; private set; }

        internal Float32CharacterRuntimeStatePageBank(int abilityCount) => m_AbilityCount = abilityCount;

        internal void PrepareCapacity(int statePageCount)
        {
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new Float32CharacterRuntimeStatePage(this, m_AbilityCount, m_ControlValueCount));
        }

        internal void PrepareControlValueCapacity(int fieldCount)
        {
            m_ControlValueCount = fieldCount;
            for (int index = 0; index < m_Pages.Count; index++)
                m_Pages[index].ControlStorage = new CharacterControlRuntimeStateStorage(fieldCount);
        }

        internal void PrepareDecodeLayout(IAbilityTimelineRuntime runtime, int maximumPayloadBytes)
        {
            int clips = 0, exits = 0, pendingExits = 0, trees = 0;
            if (runtime != null)
                runtime.GetSnapshotCapacity(out clips, out exits, out pendingExits, out trees);
            DecodeLayout = new AbilityTimelineSnapshotDecodeLayout(MaximumTimelineSnapshotCount,
                maximumPayloadBytes, clips, exits, pendingExits, trees);
        }

        internal void PrepareDecodeStorage(int capacity)
        {
            m_DecodeStorage = new AbilityTimelineSnapshotStorageBank(DecodeLayout.ActiveClips,
                DecodeLayout.TreeDecisionExits, DecodeLayout.PendingTreeDecisionExits, DecodeLayout.ActiveTreeClips);
            m_DecodeStorage.PrepareCapacity(capacity);
        }

        internal AbilityTimelineSnapshotStorage BeginTimelineDecode() => m_DecodeStorage.BeginDecode();

        internal Float32CharacterRuntimeStatePage Begin(Float32CharacterRuntimeStatePage source = null)
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                Float32CharacterRuntimeStatePage page = m_Pages[index];
                if (page.Retainers != 0 || ReferenceEquals(page, source))
                    continue;
                page.Clear();
                page.ControlStorage.BeginWrite();
                return page;
            }
            throw new InvalidOperationException("Float32 Character state page capacity does not cover retained state versions.");
        }
    }
}
