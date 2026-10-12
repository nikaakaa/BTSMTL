using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.Fixed.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedAbilityRuntimeState
    {
        FixedAbilityRuntimeState(
            AbilityStateValuePageReference<AbilityStateValue> stateValues,
            GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState)
        {
            StateValues = stateValues;
            AbilityExecutionState = abilityExecutionState;
        }

        internal AbilityStateValuePageReference<AbilityStateValue> StateValues { get; }
        internal GameplayAbilityExecutionAggregate<AbilityStateValue> AbilityExecutionState { get; }
        public GameplayAbilityExecutionIdentity AbilityIdentity => StateValues.Set.Bank.Identity;

        internal bool SharesStateWith(FixedAbilityRuntimeState other) =>
            ReferenceEquals(StateValues.Set, other.StateValues.Set) &&
            StateValues.Generation == other.StateValues.Generation &&
            ReferenceEquals(AbilityExecutionState, other.AbilityExecutionState);

        internal static FixedAbilityRuntimeState Adopt(
            AbilityStateValuePageReference<AbilityStateValue> stateValues,
            GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState)
        {
            return new FixedAbilityRuntimeState(
                stateValues,
                abilityExecutionState);
        }
    }

    public readonly struct FixedCharacterRuntimeState
    {
        readonly FixedCharacterRuntimeStatePage m_Page;
        readonly uint m_Generation;

        FixedCharacterRuntimeState(FixedCharacterRuntimeStatePage page)
        {
            m_Page = page;
            m_Generation = page.Generation;
        }

        ref readonly FixedCharacterRuntimeStateData Data
        {
            get
            {
                m_Page.RequireGeneration(m_Generation);
                return ref m_Page.Data;
            }
        }

        public bool IsValid => m_Page != null;
        internal FixedCharacterRuntimeStatePageBank StatePages => m_Page.Bank;
        internal bool SharesStateWith(FixedCharacterRuntimeState other) =>
            ReferenceEquals(m_Page, other.m_Page) && m_Generation == other.m_Generation;
        internal void ReleaseStateStorage() => m_Page.Release();
        internal SimulationCharacterStatePayload CanonicalPayload => m_Page.CanonicalPayload;

        internal void DiscardDecodedStorage()
        {
            m_Page.ClearCanonicalPayload();
            m_Page.ControlStorage.Clear();
            m_Page.Data = default;
        }

        internal void SetCanonicalPayload(SimulationCharacterStatePayload payload)
        {
            m_Page.ClearCanonicalPayload();
            payload.Retain();
            m_Page.CanonicalPayload = payload;
        }

        static readonly Comparison<AbilityTimelineRuntimeSnapshot> s_TimelineSnapshotComparison =
            (left, right) => left.RuntimeHandle.CompareTo(right.RuntimeHandle);
        static readonly Comparison<FixedAbilityRuntimeState> s_AbilityStateComparison =
            (left, right) => left.AbilityIdentity.AbilityId.CompareTo(right.AbilityIdentity.AbilityId);

        public SimulationNumericProfile NumericProfile => Data.Metadata.NumericProfile;
        public GameplayContentHash GameplayContentHash => Data.Metadata.GameplayContentHash;
        public StableHash StateSchemaHash => Data.Metadata.StateSchemaHash;
        public ulong LastCompletedTick => Data.Metadata.LastCompletedTick;

        public bool TryGetEquipmentState(out EquipmentStateAggregate equipment)
        {
            equipment = EquipmentState;
            return equipment != null;
        }
        public IReadOnlyList<FixedAbilityRuntimeState> Abilities => Data.Abilities.Values;
        internal FixedAbilityStateDirectoryReference AbilityDirectory => Data.Abilities;
        internal MotionWarpStatePageReference<FixedMotionWarpState> MotionWarpStates => Data.MotionWarpStates;
        internal AbilityStateValuePageReference<AbilityStateValue> CharacterBlackboards => Data.CharacterBlackboards;

        internal void RetainStatePages()
        {
            m_Page.Retain(m_Generation);
            Data.TimelineSnapshots.Retain();
            Data.GameplayEffectState.RetainSnapshots();
            Data.EquipmentState?.Retain();
            Data.EquipmentState?.Storage.CompleteDecodeAdoption(Data.EquipmentState);
            ActionStatePage.Retain();
            InputRequestPage.Retain();
            Data.Abilities.Retain();
            CharacterBlackboards.Retain();
            FixedAbilityRuntimeState[] values = Data.Abilities.Page.Values;
            for (int index = 0; index < values.Length; index++)
            {
                values[index].StateValues.Retain();
                values[index].AbilityExecutionState.RetainStateStorage();
            }
        }

        internal void ReleaseStatePages()
        {
            FixedAbilityRuntimeState[] values = Data.Abilities.Page.Values;
            for (int index = 0; index < values.Length; index++)
            {
                values[index].StateValues.Release();
                values[index].AbilityExecutionState.ReleaseStateStorage();
            }
            Data.Abilities.Release();
            CharacterBlackboards.Release();
            InputRequestPage.Release();
            ActionStatePage.Release();
            Data.TimelineSnapshots.Release();
            Data.GameplayEffectState.ReleaseSnapshots();
            Data.EquipmentState?.Release();
            m_Page.Release();
        }
        internal FixedActionStatePageReference ActionStatePage => Data.ActionStatePage;
        internal ReadOnlySpan<SimulationActionActivationRequestState> ActionActivationRequests => ActionStatePage.Requests;
        internal ReadOnlySpan<FixedActionInstanceState> ActionInstances => ActionStatePage.Actions;
        internal FixedInputRequestPageReference InputRequestPage => Data.InputRequestPage;
        internal ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> InputRequests => InputRequestPage.Values;
        internal ref readonly CharacterRuntimeStateMetadata Metadata => ref Data.Metadata;
        internal ulong EventSequence => Data.Metadata.EventSequence;
        internal ulong ActionEventSequence => Data.Metadata.ActionEventSequence;
        internal ulong HandleAllocator => Data.Metadata.HandleAllocator;
        public CharacterControlRuntimeState ControlState => Data.ControlState;

        public bool TryGetAttributeValue(string id, out FixedScalar baseValue, out FixedScalar currentValue, out ulong revision)
        {
            ReadOnlySpan<KeyValuePair<string, PortableAttributeSnapshot>> attributes = GameplayEffectState.Attributes;
            for (int i = 0; i < attributes.Length; i++)
                if (attributes[i].Key == id)
                {
                    baseValue = attributes[i].Value.BaseValue;
                    currentValue = attributes[i].Value.CurrentValue;
                    revision = attributes[i].Value.Revision;
                    return true;
                }
            baseValue = default;
            currentValue = default;
            revision = 0;
            return false;
        }

        public bool IsAbilityActive(CharacterSkillId abilityId)
        {
            ReadOnlySpan<FixedActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].IsActive && actions[i].SkillId == abilityId)
                    return true;
            return false;
        }

        public bool HasActionExecution(ulong actionInstanceId)
        {
            IReadOnlyList<FixedAbilityRuntimeState> abilities = Abilities;
            for (int i = 0; i < abilities.Count; i++)
                if (abilities[i].AbilityExecutionState.Find(actionInstanceId) != null)
                    return true;
            return false;
        }

        public bool TryCaptureActionReplayIdentity(ulong instance, out FixedActionReplayIdentity origin)
        {
            ReadOnlySpan<FixedActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].InstanceId == instance)
                {
                    origin = FixedActionReplayIdentity.Capture(in actions[i]);
                    return true;
                }
            origin = default;
            return false;
        }

        public bool TryResolveActionReplayIdentity(in FixedActionReplayIdentity origin, out ulong instance)
        {
            instance = 0;
            ReadOnlySpan<FixedActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (origin.Matches(in actions[i]))
                {
                    if (instance != 0)
                        return false;
                    instance = actions[i].InstanceId;
                }
            return instance != 0;
        }

        public bool TryBindBlackboardWrite(FixedGameplayAbilityExecutionInstallation installation, ulong instance,
            int slot, AbilityStateValue value, out FixedBlackboardWriteCommand command) =>
            FixedBlackboardSnapshotReader.TryBindWrite(installation, this, instance, slot, value, out command);

        public (ulong InstanceId, SimulationActionState State, CanonicalTextValue Reason)? FindActionForInputRequest(
            string requestId, ulong sequence)
        {
            ReadOnlySpan<FixedActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
            {
                ref readonly FixedActionInstanceState action = ref actions[i];
                if (action.InputSequence == sequence &&
                    CanonicalTextValue.Equals(action.SourceInputRequestId, requestId, StringComparison.Ordinal))
                    return (action.InstanceId, action.State, action.Reason);
            }
            return null;
        }

        public bool HasPendingInputRequest(string requestId, ulong sequence)
        {
            ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> requests = InputRequests;
            for (int i = 0; i < requests.Length; i++)
                if (requests[i].Value.Sequence == sequence && !requests[i].Value.Consumed &&
                    string.Equals(requests[i].Key, requestId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public bool HasInputRequest(string requestId)
        {
            ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> requests = InputRequests;
            for (int i = 0; i < requests.Length; i++)
                if (requests[i].Value.IsValid && string.Equals(requests[i].Key, requestId, StringComparison.Ordinal))
                    return true;
            return false;
        }
        internal GameplayEffectStateAggregate GameplayEffectState => Data.GameplayEffectState;
        internal EquipmentStateAggregate EquipmentState => Data.EquipmentState;
        public IReadOnlyList<AbilityTimelineRuntimeSnapshot> TimelineSnapshots => Data.TimelineSnapshots.Snapshots;
        internal FixedTimelineSnapshotPageReference TimelineSnapshotPage => Data.TimelineSnapshots;

        internal FixedCharacterRuntimeState WithTimelineOutputs(
            IAbilityTimelineRuntime timelineRuntime,
            List<AbilityTimelineAdvancePending> advances,
            List<AbilityTimelineStopPending> stops,
            List<AbilityTimelineRuntimeSnapshot> snapshots)
        {
            snapshots.Clear();
            ReadOnlyCollection<AbilityTimelineRuntimeSnapshot> sourceSnapshots = Data.TimelineSnapshots.Snapshots;
            for (int i = 0; i < sourceSnapshots.Count; i++)
                snapshots.Add(sourceSnapshots[i]);
            bool changed = advances.Count != 0;
            for (int i = 0; i < advances.Count; i++)
            {
                AbilityTimelineRuntimeSnapshot snapshot = timelineRuntime.CaptureRuntimeSnapshot(advances[i].RuntimeHandle);
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
                if (first < snapshots.Count && snapshots[first].RuntimeHandle == snapshot.RuntimeHandle)
                    snapshots[first] = snapshot;
                else
                    snapshots.Insert(first, snapshot);
            }
            for (int i = 0; i < stops.Count; i++)
                for (int index = 0; index < snapshots.Count; index++)
                {
                    if (snapshots[index].RuntimeHandle != stops[i].RuntimeHandle)
                        continue;
                    snapshots.RemoveAt(index);
                    changed = true;
                    break;
                }
            int retainedCount = 0;
            for (int index = 0; index < snapshots.Count; index++)
            {
                AbilityTimelineRuntimeSnapshot snapshot = snapshots[index];
                if (RetainTimelineSnapshot(snapshot))
                    snapshots[retainedCount++] = snapshot;
            }
            if (retainedCount != snapshots.Count)
            {
                snapshots.RemoveRange(retainedCount, snapshots.Count - retainedCount);
                changed = true;
            }
            if (!changed)
                return this;
            FixedTimelineSnapshotPageReference committedSnapshots = Data.TimelineSnapshots.Page.Bank.Capture(snapshots);
            FixedCharacterRuntimeStatePage page = m_Page.Bank.Begin();
            page.Data.CharacterBlackboards = CharacterBlackboards;
            return AdoptSnapshot(
                page,
                Metadata,
                Data.Abilities,
                ActionStatePage,
                InputRequestPage,
                page.ControlStorage.CopyFrom(ControlState),
                GameplayEffectState,
                EquipmentState,
                committedSnapshots,
                MotionWarpStates);
        }

        bool RetainTimelineSnapshot(in AbilityTimelineRuntimeSnapshot snapshot)
        {
            if (snapshot.State != BTSMTL.Timeline.TimelineRuntimePlaybackState.Completed && snapshot.State != BTSMTL.Timeline.TimelineRuntimePlaybackState.Stopped)
                return true;
            ReadOnlySpan<FixedActionInstanceState> actions = ActionInstances;
            for (int index = 0; index < actions.Length; index++)
            {
                FixedActionInstanceState action = actions[index];
                if (action.IsActive && action.InstanceId == snapshot.ActionContext.InstanceId &&
                    action.SkillExecutionGeneration == snapshot.ActionContext.SkillExecutionGeneration)
                    return true;
            }
            return false;
        }

        internal static FixedCharacterRuntimeState AdoptSnapshot(
            FixedCharacterRuntimeStatePage page,
            in CharacterRuntimeStateMetadata metadata,
            FixedAbilityStateDirectoryReference abilities,
            FixedActionStatePageReference actions,
            FixedInputRequestPageReference inputRequests,
            CharacterControlRuntimeState controlState,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            FixedTimelineSnapshotPageReference timelineSnapshots,
            MotionWarpStatePageReference<FixedMotionWarpState> motionWarpStates)
        {
            ref FixedCharacterRuntimeStateData data = ref page.Data;
            data.Metadata = metadata;
            data.Abilities = abilities;
            data.ActionStatePage = actions;
            data.InputRequestPage = inputRequests;
            data.ControlState = controlState;
            data.GameplayEffectState = gameplayEffectState;
            data.EquipmentState = equipmentState;
            data.TimelineSnapshots = timelineSnapshots;
            data.MotionWarpStates = motionWarpStates;
            return new FixedCharacterRuntimeState(page);
        }

        internal static FixedCharacterRuntimeState CreateInitial(
            FixedCharacterRuntimeStatePageBank statePages,
            AbilityStateValuePageReference<AbilityStateValue> characterBlackboards,
            FixedAbilityStateDirectoryReference abilities,
            FixedActionStatePageReference actions,
            FixedInputRequestPageReference inputRequests,
            SimulationNumericProfile numericProfile,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            CharacterControlRuntimeState controlState,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            FixedTimelineSnapshotPageReference timelineSnapshots,
            MotionWarpStatePageReference<FixedMotionWarpState> motionWarpStates)
        {
            Array.Sort(abilities.Page.Values, s_AbilityStateComparison);
            FixedCharacterRuntimeStatePage page = statePages.Begin();
            page.Data.CharacterBlackboards = characterBlackboards;
            return AdoptSnapshot(
                page,
                new CharacterRuntimeStateMetadata(numericProfile, gameplayContentHash, stateSchemaHash, 0, 0, 0, 0),
                abilities,
                actions,
                inputRequests,
                page.ControlStorage.CopyFrom(controlState),
                gameplayEffectState,
                equipmentState,
                timelineSnapshots,
                motionWarpStates);
        }

    }

    internal struct FixedCharacterRuntimeStateData
    {
        internal AbilityStateValuePageReference<AbilityStateValue> CharacterBlackboards;
        internal CharacterRuntimeStateMetadata Metadata;
        internal FixedAbilityStateDirectoryReference Abilities;
        internal FixedActionStatePageReference ActionStatePage;
        internal FixedInputRequestPageReference InputRequestPage;
        internal CharacterControlRuntimeState ControlState;
        internal GameplayEffectStateAggregate GameplayEffectState;
        internal EquipmentStateAggregate EquipmentState;
        internal FixedTimelineSnapshotPageReference TimelineSnapshots;
        internal MotionWarpStatePageReference<FixedMotionWarpState> MotionWarpStates;
    }

    internal sealed class FixedCharacterRuntimeStatePage
    {
        internal FixedCharacterRuntimeStatePage(FixedCharacterRuntimeStatePageBank bank, int controlValueCount)
        {
            Bank = bank;
            ControlStorage = new CharacterControlRuntimeStateStorage(controlValueCount);
        }
        internal FixedCharacterRuntimeStatePageBank Bank { get; }
        internal CharacterControlRuntimeStateStorage ControlStorage { get; set; }
        internal FixedCharacterRuntimeStateData Data;
        internal SimulationCharacterStatePayload CanonicalPayload;
        internal uint Generation;
        internal int Retainers;

        internal void RequireGeneration(uint generation)
        {
            if (Generation != generation)
                throw new InvalidOperationException("Fixed Character state page has already been reused.");
        }

        internal void Retain(uint generation)
        {
            RequireGeneration(generation);
            Retainers++;
        }

        internal void Release()
        {
            if (--Retainers == 0)
            {
                ClearCanonicalPayload();
                Data = default;
                ControlStorage.Clear();
            }
        }

        internal void ClearCanonicalPayload()
        {
            CanonicalPayload.Release();
            CanonicalPayload = default;
        }
    }

    internal sealed class FixedCharacterRuntimeStatePageBank
    {
        readonly List<FixedCharacterRuntimeStatePage> m_Pages = new List<FixedCharacterRuntimeStatePage>();
        int m_ControlValueCount;
        internal SimulationCharacterStatePayloadStorage EncodingStorage { get; set; }

        internal void ReleaseEncodingStorage()
        {
            for (int index = 0; index < m_Pages.Count; index++)
                m_Pages[index].ClearCanonicalPayload();
            EncodingStorage = null;
        }

        internal void PrepareControlValueCapacity(int fieldCount)
        {
            m_ControlValueCount = fieldCount;
            for (int index = 0; index < m_Pages.Count; index++)
                m_Pages[index].ControlStorage = new CharacterControlRuntimeStateStorage(fieldCount);
        }

        internal void PrepareCapacity(int statePageCount)
        {
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new FixedCharacterRuntimeStatePage(this, m_ControlValueCount));
        }

        internal FixedCharacterRuntimeStatePage Begin()
        {
            for (int i = 0; i < m_Pages.Count; i++)
            {
                FixedCharacterRuntimeStatePage page = m_Pages[i];
                if (page.Retainers != 0)
                    continue;
                page.ClearCanonicalPayload();
                page.Generation++;
                page.ControlStorage.BeginWrite();
                return page;
            }
            throw new InvalidOperationException("Fixed Character state page capacity does not cover the active transaction.");
        }
    }

    public sealed class FixedCommittedCharacterControlState : ICommittedCharacterControlState, IDisposable
    {
        FixedCharacterRuntimeState m_State;

        public CharacterControlRuntimeState ControlState => m_State.ControlState;
        public IReadOnlyList<AbilityTimelineRuntimeSnapshot> TimelineSnapshots => m_State.TimelineSnapshots;
        public bool IsAbilityActive(CharacterSkillId abilityId) => m_State.IsAbilityActive(abilityId);

        public bool TryGetAttributeValue(string id, out FixedScalar baseValue, out FixedScalar currentValue, out ulong revision)
        {
            if (m_State.IsValid)
                return m_State.TryGetAttributeValue(id, out baseValue, out currentValue, out revision);
            baseValue = default;
            currentValue = default;
            revision = 0;
            return false;
        }

        public void Set(FixedCharacterRuntimeState state)
        {
            state.RetainStatePages();
            if (m_State.IsValid)
                m_State.ReleaseStatePages();
            m_State = state;
        }

        public void Reset()
        {
            if (m_State.IsValid)
                m_State.ReleaseStatePages();
            m_State = default;
        }

        public void Dispose() => Reset();
    }

    internal sealed class FixedTimelineSnapshotPageBank
    {
        internal const int MaximumSnapshotCount = AbilityTimelineRuntimeLimits.MaximumPlaybackCount;
        readonly List<FixedTimelineSnapshotPage> m_Pages = new List<FixedTimelineSnapshotPage>();
        AbilityTimelineSnapshotStorageBank m_DecodeStorage;
        internal AbilityTimelineSnapshotDecodeLayout DecodeLayout { get; private set; }

        internal FixedTimelineSnapshotPageBank()
        {
            PrepareCapacity(0);
        }

        internal void PrepareCapacity(int capacity)
        {
            if (capacity <= m_Pages.Count)
                return;
            m_Pages.Capacity = capacity;
            while (m_Pages.Count < capacity)
                m_Pages.Add(new FixedTimelineSnapshotPage(this));
        }

        internal void PrepareDecodeLayout(IAbilityTimelineRuntime runtime, int maximumPayloadBytes)
        {
            int clips = 0, exits = 0, pendingExits = 0, trees = 0;
            if (runtime != null)
                runtime.GetSnapshotCapacity(out clips, out exits, out pendingExits, out trees);
            DecodeLayout = new AbilityTimelineSnapshotDecodeLayout(MaximumSnapshotCount, maximumPayloadBytes,
                clips, exits, pendingExits, trees);
        }

        internal void PrepareDecodeStorage(int capacity)
        {
            m_DecodeStorage = new AbilityTimelineSnapshotStorageBank(
                DecodeLayout.ActiveClips, DecodeLayout.TreeDecisionExits,
                DecodeLayout.PendingTreeDecisionExits, DecodeLayout.ActiveTreeClips);
            m_DecodeStorage.PrepareCapacity(capacity);
        }

        internal FixedTimelineSnapshotPageReference BeginDecode(out AbilityTimelineSnapshotStorage storage)
        {
            FixedTimelineSnapshotPageReference result = Begin();
            storage = m_DecodeStorage.BeginDecode();
            return result;
        }

        internal FixedTimelineSnapshotPageReference Begin()
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                FixedTimelineSnapshotPage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                page.Values.Clear();
                page.Generation++;
                return new FixedTimelineSnapshotPageReference(page, page.Generation);
            }
            throw new InvalidOperationException("Fixed Timeline snapshot page capacity does not cover the active transaction.");
        }

        internal FixedTimelineSnapshotPageReference Capture(List<AbilityTimelineRuntimeSnapshot> snapshots)
        {
            if (snapshots.Count > MaximumSnapshotCount)
                throw new InvalidOperationException("Fixed Timeline snapshot count exceeds the canonical state capacity.");
            FixedTimelineSnapshotPageReference result = Begin();
            result.Page.Values.AddRange(snapshots);
            return result;
        }
    }

    internal sealed class FixedTimelineSnapshotPage
    {
        internal FixedTimelineSnapshotPage(FixedTimelineSnapshotPageBank bank)
        {
            Bank = bank;
            Values = new List<AbilityTimelineRuntimeSnapshot>(FixedTimelineSnapshotPageBank.MaximumSnapshotCount);
            Snapshots = Values.AsReadOnly();
        }

        internal readonly FixedTimelineSnapshotPageBank Bank;
        internal readonly List<AbilityTimelineRuntimeSnapshot> Values;
        internal readonly ReadOnlyCollection<AbilityTimelineRuntimeSnapshot> Snapshots;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct FixedTimelineSnapshotPageReference
    {
        internal FixedTimelineSnapshotPageReference(FixedTimelineSnapshotPage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal FixedTimelineSnapshotPage Page { get; }
        internal ulong Generation { get; }
        internal ReadOnlyCollection<AbilityTimelineRuntimeSnapshot> Snapshots
        {
            get
            {
                RequireReadable();
                return Page.Snapshots;
            }
        }

        internal void Retain()
        {
            RequireReadable();
            if (Page.Retainers++ == 0)
                for (int index = 0; index < Page.Values.Count; index++)
                    Page.Values[index].RetainStateStorage();
        }

        internal void Release()
        {
            if (--Page.Retainers != 0)
                return;
            for (int index = 0; index < Page.Values.Count; index++)
                Page.Values[index].ReleaseStateStorage();
        }

        void RequireReadable()
        {
            if (Generation != Page.Generation)
                throw new InvalidOperationException("Fixed Timeline snapshot page is outside its retained lifetime.");
        }
    }
}
