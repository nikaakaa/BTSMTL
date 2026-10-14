using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<Photon.Deterministic.FP, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.FPGameplay.FPActionInstanceReference>;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.FPGameplay.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation.FPGameplay
{
    [ValueOperations]
    public readonly partial struct FPAbilityRuntimeState
    {
        FPAbilityRuntimeState(
            AbilityStateValuePageReference<AbilityStateValue> stateValues,
            GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState)
        {
            StateValues = stateValues;
            AbilityExecutionState = abilityExecutionState;
        }

        [ValueResource]
        public AbilityStateValuePageReference<AbilityStateValue> StateValues { get; }
        [ValueResource("RetainStateStorage", "ReleaseStateStorage")]
        public GameplayAbilityExecutionAggregate<AbilityStateValue> AbilityExecutionState { get; }
        public GameplayAbilityExecutionIdentity AbilityIdentity => StateValues.Set.Bank.Identity;

        internal bool SharesStateWith(FPAbilityRuntimeState other) =>
            ReferenceEquals(StateValues.Set, other.StateValues.Set) &&
            StateValues.Generation == other.StateValues.Generation &&
            ReferenceEquals(AbilityExecutionState, other.AbilityExecutionState);

        internal static FPAbilityRuntimeState Adopt(
            AbilityStateValuePageReference<AbilityStateValue> stateValues,
            GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState)
        {
            return new FPAbilityRuntimeState(
                stateValues,
                abilityExecutionState);
        }
    }

    public readonly partial struct FPCharacterRuntimeState
    {
        readonly FPCharacterRuntimeStatePage m_Page;
        readonly uint m_Generation;

        internal FPCharacterRuntimeState(FPCharacterRuntimeStatePage page)
        {
            m_Page = page;
            m_Generation = page.Generation;
        }

        ref readonly FPCharacterRuntimeStateData Data
        {
            get
            {
                m_Page.RequireGeneration(m_Generation);
                return ref m_Page.Data;
            }
        }

        public bool IsValid => m_Page != null;
        internal FPCharacterRuntimeStatePageBank StatePages => m_Page.Bank;
        internal bool SharesStateWith(FPCharacterRuntimeState other) =>
            ReferenceEquals(m_Page, other.m_Page) && m_Generation == other.m_Generation;

        static readonly Comparison<AbilityTimelineRuntimeSnapshot> s_TimelineSnapshotComparison =
            (left, right) => left.RuntimeHandle.CompareTo(right.RuntimeHandle);
        static readonly Comparison<FPAbilityRuntimeState> s_AbilityStateComparison =
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
        public IReadOnlyList<FPAbilityRuntimeState> Abilities => Data.Abilities.Values;
        internal FPAbilityStateDirectoryReference AbilityDirectory => Data.Abilities;
        internal MotionWarpStatePageReference<MotionWarpState> MotionWarpStates => Data.MotionWarpStates;
        public AbilityStateValuePageReference<AbilityStateValue> CharacterBlackboards => Data.CharacterBlackboards;

        internal void RetainStatePages()
        {
            m_Page.Retain(m_Generation);
            Data.Retain();
        }

        internal void ReleaseStatePages()
        {
            Data.Release();
            m_Page.Release();
        }

        internal FPCharacterRuntimeStatePage BeginPreparedRead()
        {
            FPCharacterRuntimeStatePage page = m_Page.Bank.Begin();
            page.Data = Data;
            page.Data.ControlState = page.ControlStorage.CopyFrom(Data.ControlState);
            new FPCharacterRuntimeState(page).RetainStatePages();
            return page;
        }
        internal FPActionStatePageReference ActionStatePage => Data.ActionStatePage;
        internal ReadOnlySpan<SimulationActionActivationRequestState> ActionActivationRequests => ActionStatePage.Requests;
        internal ReadOnlySpan<FPActionInstanceState> ActionInstances => ActionStatePage.Actions;
        internal FPInputRequestPageReference InputRequestPage => Data.InputRequestPage;
        internal ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> InputRequests => InputRequestPage.Values;
        public ref readonly CharacterRuntimeStateMetadata Metadata => ref Data.Metadata;
        internal ulong EventSequence => Data.Metadata.EventSequence;
        internal ulong ActionEventSequence => Data.Metadata.ActionEventSequence;
        internal ulong HandleAllocator => Data.Metadata.HandleAllocator;
        public CharacterControlRuntimeState ControlState => Data.ControlState;

        public bool TryGetAttributeValue(string id, out FP baseValue, out FP currentValue, out ulong revision)
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
            ReadOnlySpan<FPActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].IsActive && actions[i].SkillId == abilityId)
                    return true;
            return false;
        }

        public bool HasActionExecution(ulong actionInstanceId)
        {
            IReadOnlyList<FPAbilityRuntimeState> abilities = Abilities;
            for (int i = 0; i < abilities.Count; i++)
                if (abilities[i].AbilityExecutionState.Find(actionInstanceId) != null)
                    return true;
            return false;
        }

        public bool TryCaptureActionReplayIdentity(ulong instance, out FPActionReplayIdentity origin)
        {
            ReadOnlySpan<FPActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].InstanceId == instance)
                {
                    origin = FPActionReplayIdentity.Capture(in actions[i]);
                    return true;
                }
            origin = default;
            return false;
        }

        public bool TryResolveActionReplayIdentity(in FPActionReplayIdentity origin, out ulong instance)
        {
            instance = 0;
            ReadOnlySpan<FPActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (origin.Matches(in actions[i]))
                {
                    if (instance != 0)
                        return false;
                    instance = actions[i].InstanceId;
                }
            return instance != 0;
        }

        public bool TryBindBlackboardWrite(FPGameplayAbilityExecutionInstallation installation, ulong instance,
            int slot, AbilityStateValue value, out FPBlackboardWriteCommand command) =>
            FPBlackboardSnapshotReader.TryBindWrite(installation, this, instance, slot, value, out command);

        public (ulong InstanceId, SimulationActionState State, CanonicalTextValue Reason)? FindActionForInputRequest(
            string requestId, ulong sequence)
        {
            ReadOnlySpan<FPActionInstanceState> actions = ActionInstances;
            for (int i = 0; i < actions.Length; i++)
            {
                ref readonly FPActionInstanceState action = ref actions[i];
                if (action.InputSequence == sequence &&
                    CanonicalTextValue.Equals(action.SourceInputRequestId, requestId, StringComparison.Ordinal))
                    return (action.InstanceId, action.State, action.Reason);
            }
            return null;
        }

        public bool HasPendingInputRequest(string requestId, ulong sequence)
        {
            FPInputRequestPage page = InputRequestPage.Page;
            for (int i = 0; i < page.PendingCount; i++)
                if (page.PendingRequests[i].Sequence == sequence && page.PendingRequests[i].RequestId.Equals(requestId))
                    return true;
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
        internal FPTimelineSnapshotPageReference TimelineSnapshotPage => Data.TimelineSnapshots;

        internal FPCharacterRuntimeState WithTimelineOutputs(
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
            FPTimelineSnapshotPageReference committedSnapshots = Data.TimelineSnapshots.Page.Bank.Capture(snapshots);
            FPCharacterRuntimeStatePage page = m_Page.Bank.Begin();
            page.Data = Data;
            page.Data.ControlState = page.ControlStorage.CopyFrom(ControlState);
            page.Data.TimelineSnapshots = committedSnapshots;
            return new FPCharacterRuntimeState(page);
        }

        bool RetainTimelineSnapshot(in AbilityTimelineRuntimeSnapshot snapshot)
        {
            if (snapshot.State != BTSMTL.Timeline.TimelineRuntimePlaybackState.Completed && snapshot.State != BTSMTL.Timeline.TimelineRuntimePlaybackState.Stopped)
                return true;
            ReadOnlySpan<FPActionInstanceState> actions = ActionInstances;
            for (int index = 0; index < actions.Length; index++)
            {
                FPActionInstanceState action = actions[index];
                if (action.IsActive && action.InstanceId == snapshot.ActionContext.InstanceId &&
                    action.SkillExecutionGeneration == snapshot.ActionContext.SkillExecutionGeneration)
                    return true;
            }
            return false;
        }

        internal static FPCharacterRuntimeState CreateInitial(
            FPCharacterRuntimeStatePageBank statePages,
            AbilityStateValuePageReference<AbilityStateValue> characterBlackboards,
            FPAbilityStateDirectoryReference abilities,
            FPActionStatePageReference actions,
            FPInputRequestPageReference inputRequests,
            SimulationNumericProfile numericProfile,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            CharacterControlRuntimeState controlState,
            GameplayEffectStateAggregate gameplayEffectState,
            EquipmentStateAggregate equipmentState,
            FPTimelineSnapshotPageReference timelineSnapshots,
            MotionWarpStatePageReference<MotionWarpState> motionWarpStates)
        {
            Array.Sort(abilities.Page.Values, s_AbilityStateComparison);
            FPCharacterRuntimeStatePage page = statePages.Begin();
            page.Data = FPCharacterRuntimeStateData.FromValueStorage(
                CharacterBlackboards: characterBlackboards,
                Metadata: new CharacterRuntimeStateMetadata(numericProfile, gameplayContentHash, stateSchemaHash, 0, 0, 0, 0),
                Abilities: abilities,
                ActionStatePage: actions,
                InputRequestPage: inputRequests,
                ControlState: page.ControlStorage.CopyFrom(controlState),
                GameplayEffectState: gameplayEffectState,
                EquipmentState: equipmentState,
                TimelineSnapshots: timelineSnapshots,
                MotionWarpStates: motionWarpStates);
            return new FPCharacterRuntimeState(page);
        }

    }

    [ValueStorage]
    [ValueOperations(ExplicitResources = true, CopyMethod = nameof(CopyFrom))]
    internal partial struct FPCharacterRuntimeStateData
    {
        [ValueResource]
        internal AbilityStateValuePageReference<AbilityStateValue> CharacterBlackboards;
        internal CharacterRuntimeStateMetadata Metadata;
        [ValueResource]
        internal FPAbilityStateDirectoryReference Abilities;
        [ValueResource]
        internal FPActionStatePageReference ActionStatePage;
        [ValueResource]
        internal FPInputRequestPageReference InputRequestPage;
        internal CharacterControlRuntimeState ControlState;
        [ValueResource("RetainSnapshots", "ReleaseSnapshots")]
        internal GameplayEffectStateAggregate GameplayEffectState;
        [ValueResource(Optional = true)]
        internal EquipmentStateAggregate EquipmentState;
        [ValueResource]
        internal FPTimelineSnapshotPageReference TimelineSnapshots;
        [ValueResource]
        internal MotionWarpStatePageReference<MotionWarpState> MotionWarpStates;

        internal partial void CopyFrom(in FPCharacterRuntimeStateData source);
    }

    internal sealed partial class FPCharacterRuntimeStatePage
    {
        internal FPCharacterRuntimeStatePage(FPCharacterRuntimeStatePageBank bank, int controlValueCount)
        {
            Bank = bank;
            ControlStorage = new CharacterControlRuntimeStateStorage(controlValueCount);
        }
        internal FPCharacterRuntimeStatePageBank Bank { get; }
        internal CharacterControlRuntimeStateStorage ControlStorage { get; set; }
        internal FPCharacterRuntimeStateData Data;
        internal uint Generation;
        internal int Retainers;

        internal FPCharacterRuntimeState CompleteRead(in FPCharacterRuntimeStateData data)
        {
            Data.CopyFrom(data);
            var value = new FPCharacterRuntimeState(this);
            value.RetainStatePages();
            return value;
        }

        internal void ReleaseRead() => new FPCharacterRuntimeState(this).ReleaseStatePages();

        internal void RequireGeneration(uint generation)
        {
            if (Generation != generation)
                throw new InvalidOperationException("FP Character state page has already been reused.");
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
                Data = default;
                ControlStorage.Clear();
            }
        }

    }

    internal sealed partial class FPCharacterRuntimeStatePageBank
    {
        readonly List<FPCharacterRuntimeStatePage> m_Pages = new List<FPCharacterRuntimeStatePage>();
        int m_ControlValueCount;
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
                m_Pages.Add(new FPCharacterRuntimeStatePage(this, m_ControlValueCount));
        }

        internal FPCharacterRuntimeStatePage Begin()
        {
            for (int i = 0; i < m_Pages.Count; i++)
            {
                FPCharacterRuntimeStatePage page = m_Pages[i];
                if (page.Retainers != 0)
                    continue;
                page.Generation++;
                page.ControlStorage.BeginWrite();
                return page;
            }
            throw new InvalidOperationException("FP Character state page capacity does not cover the active transaction.");
        }
    }

    public sealed class FPCommittedCharacterControlState : ICommittedCharacterControlState, IDisposable
    {
        FPCharacterRuntimeState m_State;

        public CharacterControlRuntimeState ControlState => m_State.ControlState;
        public IReadOnlyList<AbilityTimelineRuntimeSnapshot> TimelineSnapshots => m_State.TimelineSnapshots;
        public bool IsAbilityActive(CharacterSkillId abilityId) => m_State.IsAbilityActive(abilityId);

        public bool TryGetAttributeValue(string id, out FP baseValue, out FP currentValue, out ulong revision)
        {
            if (m_State.IsValid)
                return m_State.TryGetAttributeValue(id, out baseValue, out currentValue, out revision);
            baseValue = default;
            currentValue = default;
            revision = 0;
            return false;
        }

        public void Set(FPCharacterRuntimeState state)
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

    internal sealed partial class FPTimelineSnapshotPageBank
    {
        internal const int MaximumSnapshotCount = AbilityTimelineRuntimeLimits.MaximumPlaybackCount;
        readonly List<FPTimelineSnapshotPage> m_Pages = new List<FPTimelineSnapshotPage>();

        internal FPTimelineSnapshotPageBank()
        {
            PrepareCapacity(0);
        }

        internal void PrepareCapacity(int capacity)
        {
            if (capacity <= m_Pages.Count)
                return;
            m_Pages.Capacity = capacity;
            while (m_Pages.Count < capacity)
                m_Pages.Add(new FPTimelineSnapshotPage(this));
        }

        internal FPTimelineSnapshotPageReference Begin()
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                FPTimelineSnapshotPage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                page.Values.Clear();
                page.Generation++;
                return new FPTimelineSnapshotPageReference(page, page.Generation);
            }
            throw new InvalidOperationException("FP Timeline snapshot page capacity does not cover the active transaction.");
        }

        internal FPTimelineSnapshotPageReference Capture(List<AbilityTimelineRuntimeSnapshot> snapshots)
        {
            if (snapshots.Count > MaximumSnapshotCount)
                throw new InvalidOperationException("FP Timeline snapshot count exceeds the canonical state capacity.");
            FPTimelineSnapshotPageReference result = Begin();
            result.Page.Values.AddRange(snapshots);
            return result;
        }
    }

    internal sealed class FPTimelineSnapshotPage
    {
        internal FPTimelineSnapshotPage(FPTimelineSnapshotPageBank bank)
        {
            Bank = bank;
            Values = new List<AbilityTimelineRuntimeSnapshot>(FPTimelineSnapshotPageBank.MaximumSnapshotCount);
            Snapshots = Values.AsReadOnly();
        }

        internal readonly FPTimelineSnapshotPageBank Bank;
        internal readonly List<AbilityTimelineRuntimeSnapshot> Values;
        internal readonly ReadOnlyCollection<AbilityTimelineRuntimeSnapshot> Snapshots;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly partial struct FPTimelineSnapshotPageReference
    {
        internal FPTimelineSnapshotPageReference(FPTimelineSnapshotPage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal FPTimelineSnapshotPage Page { get; }
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
                throw new InvalidOperationException("FP Timeline snapshot page is outside its retained lifetime.");
        }
    }
}
