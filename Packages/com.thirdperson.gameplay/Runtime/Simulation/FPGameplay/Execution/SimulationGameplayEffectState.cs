using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using GameplayEffectPredictionStateData = ThirdPersonSimulation.GameplayEffectPredictionStateData<ThirdPersonSimulation.FPGameplay.PortablePredictionAttributeSnapshot>;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation.FPGameplay
{
    internal readonly struct PortableAttributeChange
    {
        public PortableAttributeChange(
            string attributeId,
            FP beforeBase,
            FP baseValue,
            FP beforeCurrent,
            FP currentValue,
            ulong revision,
            ulong causeHandle)
        {
            AttributeId = attributeId;
            BeforeBase = beforeBase;
            BaseValue = baseValue;
            BeforeCurrent = beforeCurrent;
            CurrentValue = currentValue;
            Revision = revision;
            CauseHandle = causeHandle;
        }



        public string AttributeId { get; }
        public FP BeforeBase { get; }
        public FP BaseValue { get; }
        public FP BeforeCurrent { get; }
        public FP CurrentValue { get; }
        public ulong Revision { get; }
        public ulong CauseHandle { get; }
    }

    internal readonly struct PortableAttributeBefore
    {
        public PortableAttributeBefore(FP @base, FP current, ulong revision)
        {
            Base = @base;
            Current = current;
            Revision = revision;
        }

        public FP Base { get; }
        public FP Current { get; }
        public ulong Revision { get; }
    }

    [ValueOperations(Resources = false)]
    internal readonly partial struct PortableAttributeModifierState
    {
        public PortableAttributeModifierState(
            ulong handle,
            ulong sourceEffectHandle,
            PortableModifierOperation operation,
            FP magnitude,
            int priority,
            PortableClampBound clampBound,
            int liveAttributeIndex,
            FP liveCoefficient,
            FP livePostAdd,
            ulong insertionSequence)
        {
            Handle = handle;
            SourceEffectHandle = sourceEffectHandle;
            Operation = operation;
            Magnitude = magnitude;
            Priority = priority;
            ClampBound = clampBound;
            LiveAttributeIndex = liveAttributeIndex;
            LiveCoefficient = liveCoefficient;
            LivePostAdd = livePostAdd;
            InsertionSequence = insertionSequence;
        }

        public readonly ulong Handle;
        public readonly ulong SourceEffectHandle;
        public readonly PortableModifierOperation Operation;
        public readonly FP Magnitude;
        public readonly int Priority;
        public readonly PortableClampBound ClampBound;
        public readonly int LiveAttributeIndex;
        public readonly FP LiveCoefficient;
        public readonly FP LivePostAdd;
        public readonly ulong InsertionSequence;

        [ValueValidation]
        void RequireRecord()
        {
            if (Handle == 0 || SourceEffectHandle == 0 || InsertionSequence == 0 || LiveAttributeIndex < -1)
                throw new InvalidDataException("Gameplay Attribute modifier identity is invalid.");
        }
    }

    [ValueOperations(Resources = false)]
    internal partial struct PortableAttributeData
    {
        public PortableAttributeDefinition Definition;
        public FP BaseValue;
        public FP CurrentValue;
        public ulong Revision;
    }

    [ValueStorageOwner]
    internal sealed partial class PortableAttributeState
    {
        [ValueStorageMember]
        internal PortableAttributeData Data;
        public List<PortableAttributeModifierState> Modifiers { get; } = new List<PortableAttributeModifierState>();
    }

    [ValueStorageOwner]
    internal readonly partial struct PortableAttributeSnapshot
    {
        internal PortableAttributeSnapshot(
            PortableAttributeState source,
            FPGameplayEffectModifierSnapshotReference modifiers)
        {
            Data = source.Data;
            m_Modifiers = modifiers;
        }

        [ValueStorageMember]
        internal readonly PortableAttributeData Data;
        readonly FPGameplayEffectModifierSnapshotReference m_Modifiers;
        public FPGameplayEffectModifierSnapshotReference Modifiers => m_Modifiers;
    }

    [ValueOperations(OwnsMemberResources = true, CopyMethod = nameof(CopyFrom), ClearMethod = nameof(Clear))]
    internal partial struct PortableEffectSpecData
    {
        public PortableEffectDefinition Definition;
        [ValueReferenceAccess]
        public SimulationGameplayEffectContext Context;
        public SortedList<string, FP> SetByCaller;
        public SortedList<string, FP> SourceAttributes;
        public SortedList<string, FP> TargetAttributes;
        public bool SourceTagSnapshotProvided;
        public GameplayTagSnapshot SourceTags;
        public GameplayTagSnapshot TargetTags;
        public ulong DurationTicks;
        public ulong PeriodTicks;

        internal partial void CopyFrom(in PortableEffectSpecData value);
        internal partial void Clear();


    }

    [ValueStorageOwner]
    internal sealed partial class PortableEffectSpecState
    {
        readonly FPGameplayEffectExecutionScratch m_Owner;
        int m_Retainers;
        [ValueStorageMember]
        internal PortableEffectSpecData Data;

        internal PortableEffectSpecState(FPGameplayEffectExecutionScratch owner, GameplayEffectRuntimeCapacityLayout capacity)
        {
            m_Owner = owner;
            Data.SetByCaller = new SortedList<string, FP>(StringComparer.Ordinal);
            Data.SourceAttributes = new SortedList<string, FP>(StringComparer.Ordinal);
            Data.TargetAttributes = new SortedList<string, FP>(StringComparer.Ordinal);
            Data.SourceTags = GameplayTagSnapshot.Empty;
            Data.TargetTags = GameplayTagSnapshot.Empty;
            SetByCaller.Capacity = capacity.ParameterWidth;
            SourceAttributes.Capacity = capacity.SourceCaptureWidth;
            TargetAttributes.Capacity = capacity.TargetCaptureWidth;
            _ = SetByCaller.Keys;
            _ = SetByCaller.Values;
            _ = SourceAttributes.Keys;
            _ = SourceAttributes.Values;
            _ = TargetAttributes.Keys;
            _ = TargetAttributes.Values;
        }





        internal void Retain()
        {
            m_Retainers++;
        }

        internal void Release()
        {
            if (--m_Retainers == 0)
                m_Owner.ReleaseSpec(this);
        }

        internal void Clear() => Data.Clear();

    }

    internal sealed class PortableActiveEffectState : GameplayEffectActiveControlState<PortableEffectSpecState>
    {
    }

    [ValueStorageOwner]
    internal readonly partial struct PortableActiveEffectSnapshot
    {
        internal PortableActiveEffectSnapshot(PortableActiveEffectState source)
        {
            Data = source.Data;
            Spec = source.Spec;
        }

        [ValueStorageMember]
        internal readonly GameplayEffectActiveStateData Data;
        internal readonly PortableEffectSpecState Spec;
    }

    internal static class PortableAttributeModifierComparer
    {
        internal static int Compare(PortableAttributeModifierState left, PortableAttributeModifierState right)
        {
            int insertion = left.InsertionSequence.CompareTo(right.InsertionSequence);
            return insertion != 0 ? insertion : left.Handle.CompareTo(right.Handle);
        }
    }

    internal static class PortableActiveEffectComparer
    {
        internal static int Compare(PortableActiveEffectState left, PortableActiveEffectState right)
        {
            int insertion = left.InsertionSequence.CompareTo(right.InsertionSequence);
            return insertion != 0 ? insertion : left.Handle.CompareTo(right.Handle);
        }
    }

    internal readonly struct GameplayEffectActiveIdentity
    {
        internal GameplayEffectActiveIdentity(
            ulong handle,
            ulong instanceId,
            PortableEffectDefinition definition,
            SimulationGameplayEffectContext context)
        {
            Handle = handle;
            InstanceId = instanceId;
            Definition = definition;
            Context = context;
        }

        internal ulong Handle { get; }
        internal ulong InstanceId { get; }
        internal PortableEffectDefinition Definition { get; }
        internal SimulationGameplayEffectContext Context { get; }
    }

    [ValueOperations(Resources = false)]
    internal readonly partial struct PortablePredictionAttributeSnapshot
    {
        public PortablePredictionAttributeSnapshot(FP baseValue, FP currentValue, ulong beforeRevision, ulong afterRevision)
        {
            BaseValue = baseValue;
            CurrentValue = currentValue;
            BeforeRevision = beforeRevision;
            AfterRevision = afterRevision;
        }

        public FP BaseValue { get; }
        public FP CurrentValue { get; }
        public ulong BeforeRevision { get; }
        public ulong AfterRevision { get; }

        public PortablePredictionAttributeSnapshot WithAfterRevision(ulong revision)
        {
            return new PortablePredictionAttributeSnapshot(BaseValue, CurrentValue, BeforeRevision, revision);
        }
    }

    internal sealed class PortablePredictionRecord : GameplayEffectPredictionControlState<PortableEffectSpecState, PortablePredictionAttributeSnapshot>
    {
        readonly FPGameplayEffectExecutionScratch m_Owner;
        int m_Retainers;

        internal PortablePredictionRecord(FPGameplayEffectExecutionScratch owner, int attributes, int cues)
        {
            m_Owner = owner;
            Attributes.Capacity = attributes;
            _ = Attributes.Keys;
            _ = Attributes.Values;
            CueIds.Capacity = cues;
        }

        internal void Retain()
        {
            m_Retainers++;
            Spec.Retain();
        }

        internal void Release()
        {
            Spec.Release();
            if (--m_Retainers == 0)
                m_Owner.CancelPredictionRecord(this);
        }

        internal void Clear()
        {
            Spec = null;
            Data.Clear();
        }

        protected override GameplayEffectContextIdentity DescribeContext() => CausalContext;
    }

    internal readonly partial struct GameplayEffectStateAggregate
    {
        readonly FPGameplayEffectSnapshotReference<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> m_TagSources;
        readonly FPGameplayEffectAttributeSnapshotReference m_Attributes;
        readonly FPGameplayEffectSnapshotReference<PortableActiveEffectSnapshot> m_ActiveEffects;
        readonly FPGameplayEffectSnapshotReference<KeyValuePair<ulong, ulong>> m_Periods;
        readonly FPGameplayEffectSnapshotReference<KeyValuePair<ulong, List<PortablePredictionRecord>>> m_Journal;
        readonly FPGameplayEffectExecutionScratch m_JournalOwner;
        readonly FPGameplayEffectSnapshotReference<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>> m_LastLifecycleRevisions;
        readonly GameplayTagSnapshot m_OwnedTags;
        readonly FPGameplayEffectRuntimeCatalog m_Catalog;

        internal GameplayEffectStateAggregate CreateChangedFrom(
            FPGameplayEffectRuntimeCatalog catalog,
            FPGameplayEffectAttributeSnapshotBank attributePages,
            FPGameplayEffectSnapshotBank<KeyValuePair<ulong, ulong>> periodPages,
            FPGameplayEffectSnapshotBank<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>> lifecyclePages,
            FPGameplayEffectSnapshotBank<PortableActiveEffectSnapshot> activeEffectPages,
            FPGameplayEffectSnapshotBank<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> tagSourcePages,

            FPGameplayEffectSnapshotBank<KeyValuePair<ulong, List<PortablePredictionRecord>>> journalPages,
            FPGameplayEffectExecutionScratch scratch,
            HashSet<string> ownedTagSet,

            GameplayTagSnapshotBank ownedTagPages,
            SortedList<GameplayTagSourceIdentityValue, IReadOnlyList<string>> tagSources,
            SortedList<string, PortableAttributeState> attributes,
            List<PortableActiveEffectState> activeEffects,
            SortedList<ulong, ulong> periods,
            SortedList<ulong, List<PortablePredictionRecord>> journal,
            SortedList<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> lastLifecycleRevisions,
            bool tagsChanged,
            bool attributesChanged,
            bool activeEffectsChanged,
            bool periodsChanged,
            bool journalChanged,
            bool lastLifecycleRevisionsChanged,
            ulong changeCursor)
        {
            FPGameplayEffectSnapshotReference<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> changedTags = tagsChanged
                ? CaptureChangedMap(tagSources, tagSourcePages)
                : m_TagSources;
            FPGameplayEffectAttributeSnapshotReference changedAttributes = attributesChanged
                ? CaptureChangedAttributes(attributes, attributePages)
                : m_Attributes;
            FPGameplayEffectSnapshotReference<PortableActiveEffectSnapshot> changedActiveEffects = activeEffectsChanged
                ? CaptureChangedActiveEffects(activeEffects, activeEffectPages)
                : m_ActiveEffects;
            FPGameplayEffectSnapshotReference<KeyValuePair<ulong, ulong>> changedPeriods = periodsChanged
                ? CaptureChangedMap(periods, periodPages)
                : m_Periods;
            FPGameplayEffectSnapshotReference<KeyValuePair<ulong, List<PortablePredictionRecord>>> changedJournal = journalChanged
                ? CaptureJournal(journal, journalPages, scratch)
                : m_Journal;
            FPGameplayEffectSnapshotReference<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>> changedRevisions = lastLifecycleRevisionsChanged
                ? CaptureChangedMap(lastLifecycleRevisions, lifecyclePages)
                : m_LastLifecycleRevisions;
            GameplayTagSnapshot ownedTags = tagsChanged
                ? CollectOwnedTags(changedTags.Values, ownedTagSet, ownedTagPages, m_OwnedTags ?? GameplayTagSnapshot.Empty)
                : m_OwnedTags;
            return new GameplayEffectStateAggregate(
                catalog,
                changedTags,
                changedAttributes,
                changedActiveEffects,
                changedPeriods,
                changedJournal,

                scratch,
                changedRevisions,
                ownedTags,
                changeCursor);
        }

        internal GameplayEffectStateAggregate(
            FPGameplayEffectRuntimeCatalog catalog,
            FPGameplayEffectSnapshotReference<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> tagSources,
            FPGameplayEffectAttributeSnapshotReference attributes,
            FPGameplayEffectSnapshotReference<PortableActiveEffectSnapshot> activeEffects,
            FPGameplayEffectSnapshotReference<KeyValuePair<ulong, ulong>> periods,
            FPGameplayEffectSnapshotReference<KeyValuePair<ulong, List<PortablePredictionRecord>>> journal,
            FPGameplayEffectExecutionScratch journalOwner,
            FPGameplayEffectSnapshotReference<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>> lastLifecycleRevisions,
            GameplayTagSnapshot ownedTags,
            ulong changeCursor)
        {
            m_Catalog = catalog;
            m_TagSources = tagSources;
            m_Attributes = attributes;
            m_ActiveEffects = activeEffects;
            m_Periods = periods;
            m_Journal = journal;

            m_JournalOwner = journalOwner;
            m_LastLifecycleRevisions = lastLifecycleRevisions;
            m_OwnedTags = ownedTags;
            ChangeCursor = changeCursor;
        }

        internal FPGameplayEffectRuntimeCatalog Catalog => m_Catalog;
        internal bool IsValid => m_Attributes.IsInitialized;
        internal void RetainSnapshots()
        {
            foreach (KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>> source in TagSources)
            {
                source.Key.Retain();
                if (source.Value is GameplayTagSnapshot tags)
                    tags.RetainStateStorage();
            }
            foreach (PortableActiveEffectSnapshot active in ActiveEffects)
            {
                active.Spec.Retain();
                active.LifecycleContext.Retain();
            }
            foreach (KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> revision in LastLifecycleRevisions)
            {
                revision.Key.Retain();
                revision.Value.Retain();
            }
            foreach (KeyValuePair<ulong, List<PortablePredictionRecord>> pair in Journal)
                for (int index = 0; index < pair.Value.Count; index++)
                    pair.Value[index].Retain();
            m_Journal.Retain();
            m_TagSources.Retain();
            m_Attributes.Retain();
            m_ActiveEffects.Retain();
            m_Periods.Retain();
            m_LastLifecycleRevisions.Retain();

            m_OwnedTags?.RetainStateStorage();
        }

        internal void ReleaseSnapshots()
        {
            foreach (KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>> source in TagSources)
            {
                source.Key.Release();
                if (source.Value is GameplayTagSnapshot tags)
                    tags.ReleaseStateStorage();
            }
            foreach (PortableActiveEffectSnapshot active in ActiveEffects)
            {
                active.Spec.Release();
                active.LifecycleContext.Release();
            }
            foreach (KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> revision in LastLifecycleRevisions)
            {
                revision.Key.Release();
                revision.Value.Release();
            }
            foreach (KeyValuePair<ulong, List<PortablePredictionRecord>> pair in Journal)
                for (int index = 0; index < pair.Value.Count; index++)
                    pair.Value[index].Release();
            if (m_Journal.Page != null && m_Journal.Page.Retainers == 1)
                foreach (KeyValuePair<ulong, List<PortablePredictionRecord>> pair in Journal)
                    m_JournalOwner.JournalGroups.Release(pair.Value);
            m_Journal.Release();
            m_TagSources.Release();
            m_Attributes.Release();
            m_ActiveEffects.Release();
            m_Periods.Release();
            m_LastLifecycleRevisions.Release();

            m_OwnedTags?.ReleaseStateStorage();
        }

        internal bool SharesStorageWith(GameplayEffectStateAggregate other)
        {
            return m_TagSources.SharesStorageWith(other.m_TagSources) &&
                m_Attributes.SharesStorageWith(other.m_Attributes) &&
                m_ActiveEffects.SharesStorageWith(other.m_ActiveEffects) &&
                m_Periods.SharesStorageWith(other.m_Periods) &&
                m_Journal.SharesStorageWith(other.m_Journal) &&
                m_LastLifecycleRevisions.SharesStorageWith(other.m_LastLifecycleRevisions) &&
                ReferenceEquals(m_OwnedTags, other.m_OwnedTags) &&
                ChangeCursor == other.ChangeCursor;
        }

        internal ulong ChangeCursor { get; }
        internal int ActiveEffectCount => m_ActiveEffects.Length;
        internal ReadOnlySpan<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> TagSources => m_TagSources.Values;
        internal ReadOnlySpan<KeyValuePair<string, PortableAttributeSnapshot>> Attributes => m_Attributes.Values;
        internal ReadOnlySpan<PortableActiveEffectSnapshot> ActiveEffects => m_ActiveEffects.Values;
        internal bool HasTag(string tag, GameplayEffectScope scope)
        {
            foreach (KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>> source in TagSources)
            {
                if (!source.Key.Scope.Equals(scope))
                    continue;
                for (int index = 0; index < source.Value.Count; index++)
                    if (m_Catalog.IsTagOrParent(source.Value[index], tag))
                        return true;
            }
            return false;
        }

        internal bool HasTagFromAction(string tag, GameplayEffectScope scope, in GameplayAbilitySourceContext source)
        {
            foreach (PortableActiveEffectSnapshot effect in ActiveEffects)
            {
                GameplayEffectHolderIdentity holder = effect.Spec.Context.Holder;
                if (effect.Inhibited || !effect.Spec.Context.Scope.Equals(scope) ||
                    holder.ActionInstanceId != source.InstanceId ||
                    holder.ExecutionGeneration != source.SkillExecutionGeneration)
                    continue;
                for (int index = 0; index < effect.Spec.Definition.GrantedTags.Length; index++)
                    if (m_Catalog.IsTagOrParent(effect.Spec.Definition.GrantedTags[index], tag))
                        return true;
            }
            return false;
        }
        internal ReadOnlySpan<KeyValuePair<ulong, ulong>> Periods => m_Periods.Values;
        internal ReadOnlySpan<KeyValuePair<ulong, List<PortablePredictionRecord>>> Journal => m_Journal.Values;
        internal ReadOnlySpan<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>> LastLifecycleRevisions => m_LastLifecycleRevisions.Values;

        internal bool MatchesTagSources(IReadOnlyDictionary<GameplayTagSourceIdentityValue, IReadOnlyList<string>> sources)
        {
            if (sources == null || m_TagSources.Length != sources.Count)
                return false;
            foreach (KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>> pair in m_TagSources)
            {
                if (!sources.TryGetValue(pair.Key, out IReadOnlyList<string> values) ||
                    !EqualStrings(pair.Value, values))
                    return false;
            }
            return true;
        }

        internal bool MatchesPeriods(IReadOnlyDictionary<ulong, ulong> periods)
        {
            if (periods == null || m_Periods.Length != periods.Count)
                return false;
            foreach (KeyValuePair<ulong, ulong> pair in m_Periods)
            {
                if (!periods.TryGetValue(pair.Key, out ulong value) || value != pair.Value)
                    return false;
            }
            return true;
        }

        internal bool MatchesAttributes(IReadOnlyDictionary<string, PortableAttributeState> attributes)
        {
            if (attributes == null || m_Attributes.Length != attributes.Count)
                return false;
            foreach (KeyValuePair<string, PortableAttributeSnapshot> pair in m_Attributes)
            {
                if (!attributes.TryGetValue(pair.Key, out PortableAttributeState value) ||
                    !MatchesAttribute(pair.Value, value))
                    return false;
            }
            return true;
        }

        internal bool MatchesAttributeAt(int index, PortableAttributeState value) =>
            MatchesAttribute(m_Attributes[index].Value, value);

        static bool MatchesAttribute(PortableAttributeSnapshot left, PortableAttributeState right)
        {
            return ReferenceEquals(left.Definition, right.Definition) &&
                left.BaseValue.Equals(right.BaseValue) &&
                left.CurrentValue.Equals(right.CurrentValue) &&
                left.Revision == right.Revision &&
                MatchesModifiers(left.Modifiers, right.Modifiers);
        }

        static bool MatchesModifiers(FPGameplayEffectModifierSnapshotReference left, List<PortableAttributeModifierState> right)
        {
            if (left.Length != right.Count)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                PortableAttributeModifierState leftValue = left[i];
                PortableAttributeModifierState rightValue = right[i];
                if (leftValue.Handle != rightValue.Handle ||
                    leftValue.SourceEffectHandle != rightValue.SourceEffectHandle ||
                    leftValue.Operation != rightValue.Operation ||
                    !leftValue.Magnitude.Equals(rightValue.Magnitude) ||
                    leftValue.Priority != rightValue.Priority ||
                    leftValue.ClampBound != rightValue.ClampBound ||
                    leftValue.LiveAttributeIndex != rightValue.LiveAttributeIndex ||
                    !leftValue.LiveCoefficient.Equals(rightValue.LiveCoefficient) ||
                    !leftValue.LivePostAdd.Equals(rightValue.LivePostAdd) ||
                    leftValue.InsertionSequence != rightValue.InsertionSequence)
                    return false;
            }
            return true;
        }

        internal bool MatchesLastLifecycleRevisions(IReadOnlyDictionary<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> revisions)
        {
            if (revisions == null || m_LastLifecycleRevisions.Length != revisions.Count)
                return false;
            foreach (KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> pair in m_LastLifecycleRevisions)
            {
                if (!revisions.TryGetValue(pair.Key, out GameplayEffectLifecycleRevisionEntry value) || !value.Matches(pair.Value))
                    return false;
            }
            return true;
        }

        internal bool MatchesActiveEffects(IReadOnlyList<PortableActiveEffectState> activeEffects)
        {
            if (activeEffects == null || m_ActiveEffects.Length != activeEffects.Count)
                return false;
            for (int i = 0; i < m_ActiveEffects.Length; i++)
            {
                if (!MatchesActiveEffect(m_ActiveEffects[i], activeEffects[i]))
                    return false;
            }
            return true;
        }

        static bool MatchesActiveEffect(PortableActiveEffectSnapshot left, PortableActiveEffectState right)
        {
            return left.Handle == right.Handle &&
                left.InstanceId == right.InstanceId &&
                left.StartTick == right.StartTick &&
                left.EndTick == right.EndTick &&
                left.InsertionSequence == right.InsertionSequence &&
                left.StackCount == right.StackCount &&
                left.Inhibited == right.Inhibited &&
                left.LifecycleRevision == right.LifecycleRevision &&
                MatchesContext(left.LifecycleContext, right.LifecycleContext) &&
                MatchesSpec(left.Spec, right.Spec);
        }

        internal bool MatchesJournal(IReadOnlyDictionary<ulong, List<PortablePredictionRecord>> journal)
        {
            if (journal == null || m_Journal.Length != journal.Count)
                return false;
            foreach (KeyValuePair<ulong, List<PortablePredictionRecord>> pair in m_Journal)
            {
                if (!journal.TryGetValue(pair.Key, out List<PortablePredictionRecord> records) ||
                    !MatchesRecords(pair.Value, records))
                    return false;
            }
            return true;
        }

        static bool MatchesRecords(IReadOnlyList<PortablePredictionRecord> left, IReadOnlyList<PortablePredictionRecord> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!MatchesRecord(left[i], right[i]))
                    return false;
            }
            return true;
        }

        static bool MatchesRecord(PortablePredictionRecord left, PortablePredictionRecord right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            return left.Handle == right.Handle &&
                left.InstanceId == right.InstanceId &&
                left.CausalContext.SourceActor == right.CausalContext.SourceActor &&
                left.CausalContext.TargetActor == right.CausalContext.TargetActor &&
                left.CausalContext.SourceActionInstanceId == right.CausalContext.SourceActionInstanceId &&
                left.CausalContext.PredictionKey == right.CausalContext.PredictionKey &&
                left.CausalContext.GameplayResultId == right.CausalContext.GameplayResultId &&
                left.CausalContext.SourceTick == right.CausalContext.SourceTick &&
                left.CausalContext.Predicted == right.CausalContext.Predicted &&
                left.CausalContext.Scope.Equals(right.CausalContext.Scope) &&
                left.CausalContext.Holder.Equals(right.CausalContext.Holder) &&
                left.CreatedActive == right.CreatedActive &&
                left.HasActiveBefore == right.HasActiveBefore &&
                left.MutationKind == right.MutationKind &&
                left.ActiveBeforeInsertionSequence == right.ActiveBeforeInsertionSequence &&
                left.Confirmed == right.Confirmed &&

                left.PredictedInstanceId == right.PredictedInstanceId &&
                left.AfterLifecycleRevision == right.AfterLifecycleRevision &&
                left.AfterChangeCursor == right.AfterChangeCursor &&
                left.AuthoritativeLifecycleRevision == right.AuthoritativeLifecycleRevision &&
                MatchesSnapshot(left.ActiveBefore, right.ActiveBefore) &&
                MatchesSpec(left.Spec, right.Spec) &&
                MatchesStrings(left.CueIds, right.CueIds) &&
                MatchesAttributes(left.Attributes, right.Attributes);
        }

        static bool MatchesSnapshot(GameplayEffectActiveControlSnapshot left, GameplayEffectActiveControlSnapshot right) =>
            left.InstanceId == right.InstanceId &&
            left.StartTick == right.StartTick &&
            left.EndTick == right.EndTick &&
            left.NextPeriodTick == right.NextPeriodTick &&
            left.StackCount == right.StackCount &&
            left.Inhibited == right.Inhibited &&
            left.LifecycleRevision == right.LifecycleRevision &&
            MatchesContext(left.LifecycleContext, right.LifecycleContext);

        internal static bool MatchesContext(in GameplayEffectContextIdentity left, in GameplayEffectContextIdentity right) =>
            left.SourceActor == right.SourceActor && left.TargetActor == right.TargetActor &&
            left.SourceActionInstanceId == right.SourceActionInstanceId && left.PredictionKey == right.PredictionKey &&
            left.GameplayResultId == right.GameplayResultId && left.SourceTick == right.SourceTick &&
            left.Predicted == right.Predicted && left.Scope.Equals(right.Scope) &&
            left.Holder.Equals(right.Holder) && left.Success.Equals(right.Success);

        static bool MatchesSpec(PortableEffectSpecState left, PortableEffectSpecState right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            return (ReferenceEquals(left.Definition, right.Definition) ||
                    string.Equals(left.Definition.Id, right.Definition.Id, StringComparison.Ordinal) &&
                    left.Definition.Revision == right.Definition.Revision) &&
                left.Context.SourceActorId.Equals(right.Context.SourceActorId) &&
                left.Context.TargetActorId.Equals(right.Context.TargetActorId) &&
                left.Context.SourceActionInstanceId == right.Context.SourceActionInstanceId &&
                left.Context.PredictionKey == right.Context.PredictionKey &&
                left.Context.GameplayResultId == right.Context.GameplayResultId &&
                left.Context.SourceTick == right.Context.SourceTick &&
                left.Context.ApplicationMode == right.Context.ApplicationMode &&
                left.Context.Scope.Equals(right.Context.Scope) &&
                left.Context.Holder.Equals(right.Context.Holder) &&
                left.Context.Success.Equals(right.Context.Success) &&
                left.Context.SuccessTarget.Equals(right.Context.SuccessTarget) &&
                left.SourceTagSnapshotProvided == right.SourceTagSnapshotProvided &&
                MatchesValues(left.SetByCaller, right.SetByCaller) &&
                MatchesValues(left.SourceAttributes, right.SourceAttributes) &&
                MatchesValues(left.TargetAttributes, right.TargetAttributes) &&
                MatchesStrings(left.SourceTags, right.SourceTags) &&
                MatchesStrings(left.TargetTags, right.TargetTags) &&
                left.DurationTicks == right.DurationTicks &&
                left.PeriodTicks == right.PeriodTicks;
        }

        static bool MatchesValues(SortedList<string, FP> left, SortedList<string, FP> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left.Keys[i], right.Keys[i], StringComparison.Ordinal) ||
                    !left.Values[i].Equals(right.Values[i]))
                    return false;
            }
            return true;
        }

        static bool MatchesStrings(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        static bool MatchesAttributes(
            SortedList<string, PortablePredictionAttributeSnapshot> left,
            SortedList<string, PortablePredictionAttributeSnapshot> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left.Keys[i], right.Keys[i], StringComparison.Ordinal))
                    return false;
                PortablePredictionAttributeSnapshot value = right.Values[i];
                PortablePredictionAttributeSnapshot leftValue = left.Values[i];
                if (!leftValue.BaseValue.Equals(value.BaseValue) ||
                    !leftValue.CurrentValue.Equals(value.CurrentValue) ||
                    leftValue.BeforeRevision != value.BeforeRevision ||
                    leftValue.AfterRevision != value.AfterRevision)
                    return false;
            }
            return true;
        }

        static bool EqualStrings(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        internal void CollectActiveEffectIdentities(List<GameplayEffectActiveIdentity> values)
        {
            values.Clear();
            for (int i = 0; i < m_ActiveEffects.Length; i++)
            {
                PortableActiveEffectSnapshot active = m_ActiveEffects[i];
                values.Add(new GameplayEffectActiveIdentity(
                    active.Handle,
                    active.InstanceId,
                    active.Spec.Definition,
                    new SimulationGameplayEffectContext(active.LifecycleContext.SourceActor, active.LifecycleContext.TargetActor,
                        active.LifecycleContext.SourceActionInstanceId, active.LifecycleContext.PredictionKey,
                        active.LifecycleContext.GameplayResultId, active.LifecycleContext.SourceTick,
                        active.LifecycleContext.Predicted ? SimulationGameplayEffectApplicationMode.Predicted :
                            SimulationGameplayEffectApplicationMode.Confirmed,
                        active.LifecycleContext.Scope, active.LifecycleContext.Holder,
                        active.LifecycleContext.Success, active.Spec.Context.SuccessTarget)));
            }
        }

        internal IReadOnlyList<string> CopyOwnedTags() => m_OwnedTags;

        internal void ReadAttributeAt(
            int index,
            out FP baseValue,
            out FP currentValue,
            out ulong revision)
        {
            PortableAttributeSnapshot value = m_Attributes[index].Value;
            baseValue = value.BaseValue;
            currentValue = value.CurrentValue;
            revision = value.Revision;
        }

        internal void CopyTagSourcesTo(IDictionary<GameplayTagSourceIdentityValue, IReadOnlyList<string>> tagSources)
        {
            foreach (KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>> pair in m_TagSources)
            {
                if (pair.Value is GameplayTagSnapshot tags)
                    tags.RetainStateStorage();
                pair.Key.Retain();
                tagSources.Add(pair.Key, pair.Value);
            }
        }

        internal void RestoreAttributesTo(IDictionary<string, PortableAttributeState> attributes)
        {
            foreach (KeyValuePair<string, PortableAttributeSnapshot> pair in m_Attributes)
            {
                PortableAttributeState target = attributes[pair.Key];
                PortableAttributeSnapshot source = pair.Value;
                target.Definition = source.Definition;
                target.BaseValue = source.BaseValue;
                target.CurrentValue = source.CurrentValue;
                target.Revision = source.Revision;
                target.Modifiers.Clear();
                FPGameplayEffectModifierSnapshotReference modifiers = source.Modifiers;
                for (int index = 0; index < modifiers.Length; index++)
                    target.Modifiers.Add(modifiers[index]);
            }
        }

        internal void RestoreActiveEffectsTo(IList<PortableActiveEffectState> activeEffects, FPGameplayEffectExecutionScratch scratch)
        {
            for (int i = 0; i < m_ActiveEffects.Length; i++)
                activeEffects.Add(RestoreActiveEffect(m_ActiveEffects[i], scratch));
        }

        internal void CopyPeriodsTo(IDictionary<ulong, ulong> periods)
        {
            foreach (KeyValuePair<ulong, ulong> pair in m_Periods)
                periods.Add(pair.Key, pair.Value);
        }

        internal void CopyJournalTo(IDictionary<ulong, List<PortablePredictionRecord>> journal, FPGameplayEffectExecutionScratch scratch)
        {
            foreach (KeyValuePair<ulong, List<PortablePredictionRecord>> pair in m_Journal)
            {
                List<PortablePredictionRecord> records = scratch.JournalGroups.Acquire();
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    PortablePredictionRecord record = ClonePrediction(pair.Value[i], scratch);
                    record.Retain();
                    records.Add(record);
                }
                journal.Add(pair.Key, records);
            }
        }

        internal void CopyLastLifecycleRevisionsTo(IDictionary<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> lastLifecycleRevisions)
        {
            foreach (KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> pair in m_LastLifecycleRevisions)
            {
                pair.Key.Retain();
                pair.Value.Retain();
                lastLifecycleRevisions.Add(pair.Key, pair.Value);
            }
        }

        static FPGameplayEffectAttributeSnapshotReference CaptureChangedAttributes(
            SortedList<string, PortableAttributeState> source,
            FPGameplayEffectAttributeSnapshotBank attributePages)
        {
            FPGameplayEffectAttributeSnapshotReference snapshot = attributePages.Begin();
            KeyValuePair<string, PortableAttributeSnapshot>[] result = snapshot.Page.Values;
            for (int i = 0; i < source.Count; i++)
            {
                string key = source.Keys[i];
                PortableAttributeState current = source.Values[i];
                List<PortableAttributeModifierState> modifiers = snapshot.Page.ModifierBuffers[i];
                modifiers.Clear();
                modifiers.AddRange(current.Modifiers);
                var value = new PortableAttributeSnapshot(current,
                    new FPGameplayEffectModifierSnapshotReference(modifiers, snapshot.Page));
                result[i] = new KeyValuePair<string, PortableAttributeSnapshot>(key, value);
            }
            return snapshot;
        }

        static FPGameplayEffectSnapshotReference<PortableActiveEffectSnapshot> CaptureChangedActiveEffects(
            IReadOnlyList<PortableActiveEffectState> source,
            FPGameplayEffectSnapshotBank<PortableActiveEffectSnapshot> pages)
        {
            FPGameplayEffectSnapshotReference<PortableActiveEffectSnapshot> snapshot = pages.Begin(source.Count);
            PortableActiveEffectSnapshot[] values = snapshot.Page.Values;
            for (int index = 0; index < source.Count; index++)
                values[index] = new PortableActiveEffectSnapshot(source[index]);
            return snapshot;
        }

        static FPGameplayEffectSnapshotReference<KeyValuePair<TKey, TValue>> CaptureChangedMap<TKey, TValue>(
            SortedList<TKey, TValue> source,
            FPGameplayEffectSnapshotBank<KeyValuePair<TKey, TValue>> pages)
        {
            FPGameplayEffectSnapshotReference<KeyValuePair<TKey, TValue>> snapshot = pages.Begin(source.Count);
            KeyValuePair<TKey, TValue>[] values = snapshot.Page.Values;
            for (int index = 0; index < source.Count; index++)
                values[index] = new KeyValuePair<TKey, TValue>(source.Keys[index], source.Values[index]);
            return snapshot;
        }

        static PortableActiveEffectState RestoreActiveEffect(
            in PortableActiveEffectSnapshot source,
            FPGameplayEffectExecutionScratch scratch)
        {
            PortableActiveEffectState active = scratch.AcquireActiveState();
            active.Spec = source.Spec;
            active.Spec.Retain();
            active.Data.CopyFrom(in source.Data);
            return active;
        }

        static FPGameplayEffectSnapshotReference<KeyValuePair<ulong, List<PortablePredictionRecord>>> CaptureJournal(
            SortedList<ulong, List<PortablePredictionRecord>> source,
            FPGameplayEffectSnapshotBank<KeyValuePair<ulong, List<PortablePredictionRecord>>> pages,
            FPGameplayEffectExecutionScratch scratch)
        {
            FPGameplayEffectSnapshotReference<KeyValuePair<ulong, List<PortablePredictionRecord>>> snapshot = pages.Begin(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                List<PortablePredictionRecord> records = scratch.JournalGroups.Acquire();
                List<PortablePredictionRecord> current = source.Values[i];
                for (int j = 0; j < current.Count; j++)
                    records.Add(ClonePrediction(current[j], scratch));
                snapshot.Page.Values[i] = new KeyValuePair<ulong, List<PortablePredictionRecord>>(source.Keys[i], records);
            }
            return snapshot;
        }

        static PortablePredictionRecord ClonePrediction(PortablePredictionRecord source, FPGameplayEffectExecutionScratch scratch)
        {
            PortablePredictionRecord record = scratch.AcquirePredictionRecord();
            CopyPrediction(source, record);
            return record;
        }

        static void CopyPrediction(PortablePredictionRecord source, PortablePredictionRecord result)
        {
            result.Spec = source.Spec;
            result.Data.CopyFrom(in source.Data);
        }

        static GameplayTagSnapshot CollectOwnedTags(
            ReadOnlySpan<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> sources,
            HashSet<string> tags,
            GameplayTagSnapshotBank pages,
            GameplayTagSnapshot baseline)
        {
            tags.Clear();
            foreach (KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>> source in sources)
            {
                if (source.Key.Scope.Kind != GameplayEffectScopeKind.Actor)
                    continue;
                for (int i = 0; i < source.Value.Count; i++)
                    tags.Add(source.Value[i]);
            }
            bool matches = tags.Count == baseline.Count;
            for (int i = 0; matches && i < baseline.Count; i++)
                matches = tags.Contains(baseline[i]);
            if (matches)
            {
                tags.Clear();
                return baseline;
            }
            GameplayTagSnapshot snapshot = pages.Begin(tags.Count);
            int index = 0;
            foreach (string tag in tags)
                snapshot.Write(index++, tag);
            snapshot.Sort();
            tags.Clear();
            return snapshot;
        }


    }

    internal readonly partial struct FPGameplayEffectModifierSnapshotReference
    {
        readonly List<PortableAttributeModifierState> m_Buffer;
        readonly FPGameplayEffectAttributeSnapshotPage m_Page;
        readonly uint m_Generation;

        internal FPGameplayEffectModifierSnapshotReference(
            List<PortableAttributeModifierState> buffer,
            FPGameplayEffectAttributeSnapshotPage page)
        {
            m_Buffer = buffer;
            m_Page = page;
            m_Generation = page.Generation;
        }

        public int Length
        {
            get
            {
                if (m_Page == null)
                    return 0;
                m_Page.RequireGeneration(m_Generation);
                return m_Buffer.Count;
            }
        }

        public PortableAttributeModifierState this[int index]
        {
            get
            {
                m_Page.RequireGeneration(m_Generation);
                return m_Buffer[index];
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator
        {
            readonly FPGameplayEffectModifierSnapshotReference m_Values;
            int m_Index;

            internal Enumerator(FPGameplayEffectModifierSnapshotReference values)
            {
                m_Values = values;
                m_Index = -1;
            }

            public bool MoveNext() => ++m_Index < m_Values.Length;
            public PortableAttributeModifierState Current => m_Values[m_Index];
        }
    }

    internal readonly partial struct FPGameplayEffectAttributeSnapshotReference
    {
        readonly uint m_Generation;

        internal FPGameplayEffectAttributeSnapshotReference(FPGameplayEffectAttributeSnapshotPage page)
        {
            Page = page;
            m_Generation = page.Generation;
        }

        internal FPGameplayEffectAttributeSnapshotPage Page { get; }
        internal bool IsInitialized => Page != null;
        internal ReadOnlySpan<KeyValuePair<string, PortableAttributeSnapshot>> Values
        {
            get
            {
                if (Page == null)
                    return ReadOnlySpan<KeyValuePair<string, PortableAttributeSnapshot>>.Empty;
                Page.RequireGeneration(m_Generation);
                return Page.Values;
            }
        }
        internal int Length => Values.Length;
        internal KeyValuePair<string, PortableAttributeSnapshot> this[int index] => Values[index];
        public ReadOnlySpan<KeyValuePair<string, PortableAttributeSnapshot>>.Enumerator GetEnumerator() => Values.GetEnumerator();

        internal bool SharesStorageWith(FPGameplayEffectAttributeSnapshotReference other) =>
            ReferenceEquals(Page, other.Page) && m_Generation == other.m_Generation;

        internal void Retain()
        {
            if (Page != null)
                Page.Retain(m_Generation);
        }

        internal void Release()
        {
            if (Page != null)
                Page.Release(m_Generation);
        }
    }

    internal sealed class FPGameplayEffectAttributeSnapshotPage
    {
        readonly FPGameplayEffectAttributeSnapshotBank m_Owner;
        readonly int m_Index;

        internal FPGameplayEffectAttributeSnapshotPage(FPGameplayEffectAttributeSnapshotBank owner, int pageIndex, int[] modifierCapacities)
        {
            m_Owner = owner;
            m_Index = pageIndex;
            Values = new KeyValuePair<string, PortableAttributeSnapshot>[modifierCapacities.Length];
            ModifierBuffers = new List<PortableAttributeModifierState>[modifierCapacities.Length];
            for (int index = 0; index < ModifierBuffers.Length; index++)
                ModifierBuffers[index] = new List<PortableAttributeModifierState>(modifierCapacities[index]);
        }

        internal readonly KeyValuePair<string, PortableAttributeSnapshot>[] Values;
        internal readonly List<PortableAttributeModifierState>[] ModifierBuffers;
        internal uint Generation;
        internal int Retainers;

        internal void RequireGeneration(uint generation)
        {
            if (Generation != generation)
                throw new InvalidOperationException("Gameplay Effect attribute snapshot page has already been released.");
        }

        internal void Retain(uint generation)
        {
            RequireGeneration(generation);
            Retainers++;
        }

        internal void Release(uint generation)
        {
            RequireGeneration(generation);
            if (--Retainers == 0)
            {
                Generation++;
                Array.Clear(Values, 0, Values.Length);
                for (int index = 0; index < ModifierBuffers.Length; index++)
                    ModifierBuffers[index].Clear();

                m_Owner.Return(m_Index);
            }
        }
    }

    internal sealed partial class FPGameplayEffectAttributeSnapshotBank
    {
        readonly FPGameplayEffectAttributeSnapshotPage[] m_Pages;
        readonly int[] m_Available;
        int m_AvailableCount;

        internal FPGameplayEffectAttributeSnapshotBank(FPGameplayEffectRuntimeCatalog catalog, IList<string> attributeIds)
        {
            int[] modifierCapacities = new int[attributeIds.Count];
            foreach (PortableEffectDefinition effect in catalog.Effects.Values)
            {
                int[] effectCounts = new int[attributeIds.Count];
                foreach (PortableEffectComponent component in effect.Components)
                    if (component is PortableModifierComponent modifier && modifier.Application == PortableModifierApplication.CurrentValue)
                        effectCounts[catalog.AttributeDependencies.AttributeIndex(modifier.AttributeId)]++;
                for (int i = 0; i < modifierCapacities.Length; i++)
                    modifierCapacities[i] = Math.Max(modifierCapacities[i], effectCounts[i] * catalog.CapacityLayout.ActiveEffectWidth);
            }
            int pageCount = catalog.CapacityLayout.StatePageCount;
            m_Pages = new FPGameplayEffectAttributeSnapshotPage[pageCount];
            m_Available = new int[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                m_Pages[i] = new FPGameplayEffectAttributeSnapshotPage(this, i, modifierCapacities);
                m_Available[i] = i;
            }
            m_AvailableCount = pageCount;
        }

        internal int AvailableCount => m_AvailableCount;

        internal FPGameplayEffectAttributeSnapshotReference Begin()
        {
            FPGameplayEffectAttributeSnapshotPage page = m_Pages[m_Available[--m_AvailableCount]];
            page.Generation++;
            return new FPGameplayEffectAttributeSnapshotReference(page);
        }

        internal void Return(int index) => m_Available[m_AvailableCount++] = index;
    }

    internal readonly partial struct FPGameplayEffectSnapshotReference<T> where T : struct
    {
        readonly FPGameplayEffectSnapshotPage<T> m_Page;
        readonly uint m_Generation;

        internal FPGameplayEffectSnapshotReference(FPGameplayEffectSnapshotPage<T> page)
        {
            m_Page = page;
            m_Generation = page.Generation;
        }

        internal ReadOnlySpan<T> Values
        {
            get
            {
                if (m_Page == null)
                    return ReadOnlySpan<T>.Empty;
                m_Page.RequireGeneration(m_Generation);
                return m_Page.Values.AsSpan(0, m_Page.Count);
            }
        }

        internal FPGameplayEffectSnapshotPage<T> Page => m_Page;
        internal int Length => Values.Length;
        internal T this[int index] => Values[index];
        public ReadOnlySpan<T>.Enumerator GetEnumerator() => Values.GetEnumerator();

        internal bool SharesStorageWith(FPGameplayEffectSnapshotReference<T> other) =>
            ReferenceEquals(m_Page, other.m_Page) && m_Generation == other.m_Generation;

        internal void Retain()
        {
            if (m_Page != null)
                m_Page.Retain(m_Generation);
        }

        internal void Release()
        {
            if (m_Page != null)
                m_Page.Release(m_Generation);
        }
    }

    internal sealed class FPGameplayEffectSnapshotPage<T> where T : struct
    {
        readonly FPGameplayEffectSnapshotBank<T> m_Owner;
        readonly int m_Index;

        internal FPGameplayEffectSnapshotPage(FPGameplayEffectSnapshotBank<T> owner, int index, int capacity)
        {
            m_Owner = owner;
            m_Index = index;
            Values = new T[capacity];
        }

        internal T[] Values;
        internal int Count;
        internal uint Generation;
        internal int Retainers;

        internal void Begin(int count)
        {
            Count = count;
            Generation++;
        }

        internal void RequireGeneration(uint generation)
        {
            if (Generation != generation)
                throw new InvalidOperationException("Gameplay Effect snapshot page has already been released.");
        }

        internal void Retain(uint generation)
        {
            RequireGeneration(generation);
            Retainers++;
        }

        internal void Release(uint generation)
        {
            RequireGeneration(generation);
            if (--Retainers == 0)
            {
                Generation++;
                Array.Clear(Values, 0, Count);
                Count = 0;

                m_Owner.Return(m_Index);
            }
        }
    }

    internal sealed partial class FPGameplayEffectSnapshotBank<T> where T : struct
    {
        readonly FPGameplayEffectSnapshotPage<T>[] m_Pages;
        readonly int[] m_Available;
        int m_AvailableCount;

        internal FPGameplayEffectSnapshotBank(int width, int pageCount)
        {
            m_Pages = new FPGameplayEffectSnapshotPage<T>[pageCount];
            m_Available = new int[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                m_Pages[i] = new FPGameplayEffectSnapshotPage<T>(this, i, width);
                m_Available[i] = i;
            }
            m_AvailableCount = pageCount;
        }

        internal int AvailableCount => m_AvailableCount;

        internal FPGameplayEffectSnapshotReference<T> Begin(int count)
        {
            FPGameplayEffectSnapshotPage<T> page = m_Pages[m_Available[--m_AvailableCount]];
            page.Begin(count);
            return new FPGameplayEffectSnapshotReference<T>(page);
        }

        internal void Return(int index) => m_Available[m_AvailableCount++] = index;
    }

    internal sealed partial class SimulationGameplayEffectState
    {
        readonly FPGameplayEffectRuntimeCatalog m_Catalog;
        readonly FPGameplayEffectExecutionScratch m_Scratch;
        readonly FPGameplayEffectAttributeSnapshotBank m_AttributeSnapshotPages;
        readonly FPGameplayEffectSnapshotBank<KeyValuePair<ulong, ulong>> m_PeriodSnapshotPages;
        readonly FPGameplayEffectSnapshotBank<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>> m_LifecycleSnapshotPages;
        readonly FPGameplayEffectSnapshotBank<PortableActiveEffectSnapshot> m_ActiveEffectSnapshotPages;
        readonly FPGameplayEffectSnapshotBank<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> m_TagSourceSnapshotPages;

        readonly FPGameplayEffectSnapshotBank<KeyValuePair<ulong, List<PortablePredictionRecord>>> m_JournalSnapshotPages;
        readonly GameplayTagSnapshotBank m_TagSnapshotPages;
        readonly GameplayTagSnapshotBank m_OwnedTagSnapshotPages;
        readonly SortedList<GameplayTagSourceIdentityValue, IReadOnlyList<string>> m_TagSources = new SortedList<GameplayTagSourceIdentityValue, IReadOnlyList<string>>();
        readonly FPGameplayAttributeAggregator m_AttributeAggregator;
        readonly List<PortableActiveEffectState> m_ActiveEffects = new List<PortableActiveEffectState>();
        readonly Dictionary<ulong, PortableActiveEffectState> m_ActiveByHandle = new Dictionary<ulong, PortableActiveEffectState>();
        readonly Dictionary<ulong, PortableActiveEffectState> m_ActiveByInstance = new Dictionary<ulong, PortableActiveEffectState>();
        readonly SortedList<ulong, ulong> m_Periods = new SortedList<ulong, ulong>();
        readonly SortedList<ulong, List<PortablePredictionRecord>> m_Journal = new SortedList<ulong, List<PortablePredictionRecord>>();
        readonly SortedList<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> m_LastLifecycleRevisions = new SortedList<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>();
        GameplayEffectStateAggregate m_Baseline;
        GameplayTagSnapshot m_OwnedTagsSnapshot;
        ulong m_ChangeCursor;
        bool m_TagsDirty;
        bool m_ActiveEffectsDirty;
        bool m_PeriodsDirty;
        bool m_JournalDirty;
        bool m_ChangeCursorDirty;
        bool m_LastLifecycleRevisionsDirty;

        public SimulationGameplayEffectState(
            FPGameplayEffectRuntimeCatalog catalog,
            FPGameplayEffectExecutionScratch scratch)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            m_Scratch = scratch ?? throw new ArgumentNullException(nameof(scratch));
            m_Catalog = catalog;
            GameplayEffectRuntimeCapacityLayout capacity = catalog.CapacityLayout;
            m_Scratch.Prepare(catalog, capacity);
            m_ActiveByHandle.EnsureCapacity(capacity.ActiveEffectWidth);
            m_ActiveByInstance.EnsureCapacity(capacity.ActiveEffectWidth);
            m_AttributeAggregator = new FPGameplayAttributeAggregator(catalog);
            m_AttributeAggregator.PrepareModifierCapacity(capacity.ActiveEffectWidth, catalog);
            m_AttributeSnapshotPages = new FPGameplayEffectAttributeSnapshotBank(catalog, m_AttributeAggregator.Attributes.Keys);
            m_PeriodSnapshotPages = new FPGameplayEffectSnapshotBank<KeyValuePair<ulong, ulong>>(capacity.ActiveEffectWidth, capacity.StatePageCount);
            m_LifecycleSnapshotPages = new FPGameplayEffectSnapshotBank<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>>(capacity.LifecycleRevisionWidth, capacity.StatePageCount);
            m_ActiveEffectSnapshotPages = new FPGameplayEffectSnapshotBank<PortableActiveEffectSnapshot>(capacity.ActiveEffectWidth, capacity.StatePageCount);
            m_TagSourceSnapshotPages = new FPGameplayEffectSnapshotBank<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>>(capacity.SourceWidth, capacity.StatePageCount);

            m_JournalSnapshotPages = new FPGameplayEffectSnapshotBank<KeyValuePair<ulong, List<PortablePredictionRecord>>>(capacity.Actor.PendingPredictionKeys, capacity.StatePageCount);
            m_TagSnapshotPages = new GameplayTagSnapshotBank(capacity.Actor.SourceTagSnapshotEntries, capacity.CapturePageCount);
            m_OwnedTagSnapshotPages = new GameplayTagSnapshotBank(capacity.FrozenTagWidth, capacity.StatePageCount);
            m_TagSources.Capacity = capacity.SourceWidth;
            _ = m_TagSources.Keys;
            _ = m_TagSources.Values;
            m_ActiveEffects.Capacity = capacity.ActiveEffectWidth;
            m_Periods.Capacity = capacity.ActiveEffectWidth;
            _ = m_Periods.Keys;
            _ = m_Periods.Values;
            m_LastLifecycleRevisions.Capacity = capacity.LifecycleRevisionWidth;
            _ = m_LastLifecycleRevisions.Keys;
            _ = m_LastLifecycleRevisions.Values;
            m_Journal.Capacity = capacity.Actor.PendingPredictionKeys;
            _ = m_Journal.Keys;
            _ = m_Journal.Values;
            Initialize();
            ValidateRuntimeClosure();
        }

        public FPGameplayEffectRuntimeCatalog Catalog => m_Catalog;
        internal int PredictionKeyCount => m_Journal.Count;
        internal int AvailableTagSnapshotPages => m_TagSnapshotPages.AvailableCount;
        internal int ExternalTagSourceCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < m_TagSources.Count; index++)
                {
                    if (m_TagSources.Keys[index].IsExternalSource)
                        count++;
                }
                return count;
            }
        }
        public IReadOnlyList<PortableActiveEffectState> ActiveEffects => m_ActiveEffects;
        public bool TryGetJournalRecords(ulong predictionKey, out IReadOnlyList<PortablePredictionRecord> records)
        {
            if (m_Journal.TryGetValue(predictionKey, out List<PortablePredictionRecord> values))
            {
                records = values;
                return true;
            }
            records = null;
            return false;
        }

        public void AddJournalRecord(PortablePredictionRecord record)
        {
            ulong key = record.CausalContext.PredictionKey;
            if (!m_Journal.TryGetValue(key, out List<PortablePredictionRecord> records))
            {
                records = m_Scratch.JournalGroups.Acquire();
                m_Journal.Add(key, records);
            }
            record.Retain();
            records.Add(record);
            RefreshJournalDirty();
        }

        internal bool HasUnresolvedAction(ulong action, ulong key, ActorId actor)
        {
            if (!m_Journal.TryGetValue(key, out List<PortablePredictionRecord> records))
                return false;
            for (int i = 0; i < records.Count; i++)
                if (!records[i].Confirmed && records[i].CausalContext.SourceActor == actor &&
                    records[i].CausalContext.SourceActionInstanceId == action)
                    return true;
            return false;
        }

        internal bool TryResolveLifecyclePrediction(ulong handle, out PortablePredictionRecord prediction)
        {
            PortableActiveEffectState active = FindActiveByHandle(handle);
            if (active != null && active.LifecycleContext.Predicted)
                for (int i = m_Journal.Count - 1; i >= 0; i--)
                {
                    List<PortablePredictionRecord> records = m_Journal.Values[i];
                    for (int j = records.Count - 1; j >= 0; j--)
                    {
                        PortablePredictionRecord record = records[j];
                        if (record.Handle == handle && !record.Confirmed && record.MutationKind != 0 &&
                            record.AfterLifecycleRevision == active.LifecycleRevision &&
                            GameplayEffectStateAggregate.MatchesContext(record.CausalContext, active.LifecycleContext))
                        {
                            prediction = record;
                            return true;
                        }
                    }
                }
            prediction = null;
            return false;
        }

        public void RemoveJournalRecords(ulong predictionKey)
        {
            if (m_Journal.TryGetValue(predictionKey, out List<PortablePredictionRecord> records))
            {
                for (int index = 0; index < records.Count; index++)
                    m_Scratch.DeferPredictionRelease(records[index]);
                m_Scratch.JournalGroups.Release(records);
                m_Journal.Remove(predictionKey);
            }
            RefreshJournalDirty();
        }

        public void RemoveJournalRecord(ulong predictionKey, PortablePredictionRecord record)
        {
            List<PortablePredictionRecord> records = m_Journal[predictionKey];
            for (int i = 0; i < records.Count; i++)
            {
                if (!ReferenceEquals(records[i], record))
                    continue;
                records.RemoveAt(i);
                m_Scratch.DeferPredictionRelease(record);
                if (records.Count == 0)
                {
                    m_Journal.Remove(predictionKey);
                    m_Scratch.JournalGroups.Release(records);
                }
                RefreshJournalDirty();
                return;
            }
        }

        public void ConfirmJournalRecord(PortablePredictionRecord record, ulong instanceId, ulong revision,
            GameplayEffectContextIdentity lifecycleContext)
        {
            ulong previousInstanceId = record.InstanceId;
            for (int keyIndex = 0; keyIndex < m_Journal.Count; keyIndex++)
            {
                List<PortablePredictionRecord> records = m_Journal.Values[keyIndex];
                for (int index = 0; index < records.Count; index++)
                {
                    PortablePredictionRecord other = records[index];
                    if (other.Handle != record.Handle || record.Handle == 0 && other.InstanceId != previousInstanceId)
                        continue;
                    other.InstanceId = instanceId;
                    if (!other.HasActiveBefore)
                        continue;
                    GameplayEffectActiveControlSnapshot before = other.ActiveBefore;
                    bool confirmedOwner = before.LifecycleRevision == record.AfterLifecycleRevision &&
                        GameplayEffectStateAggregate.MatchesContext(before.LifecycleContext, record.CausalContext);
                    other.ActiveBefore = new GameplayEffectActiveControlSnapshot(instanceId, before.StartTick,
                        before.EndTick, before.NextPeriodTick, before.StackCount, before.Inhibited,
                        confirmedOwner ? revision : before.LifecycleRevision,
                        confirmedOwner ? lifecycleContext : before.LifecycleContext);
                }
            }
            record.InstanceId = instanceId;
            record.Confirmed = true;
            record.AuthoritativeLifecycleRevision = revision;
            RefreshJournalDirty();
        }

        public void RemapActiveInstance(PortableActiveEffectState active, ulong instanceId)
        {
            ulong previousId = active.InstanceId;
            m_ActiveByInstance.Remove(previousId);
            active.InstanceId = instanceId;
            m_ActiveByInstance.Add(instanceId, active);
            for (int i = 0; i < m_Journal.Count; i++)
            {
                List<PortablePredictionRecord> records = m_Journal.Values[i];
                for (int j = 0; j < records.Count; j++)
                {
                    PortablePredictionRecord record = records[j];
                    if (record.Handle != active.Handle)
                        continue;
                    record.InstanceId = instanceId;
                    if (record.HasActiveBefore && record.ActiveBefore.InstanceId == previousId)
                    {
                        GameplayEffectActiveControlSnapshot before = record.ActiveBefore;
                        record.ActiveBefore = new GameplayEffectActiveControlSnapshot(instanceId, before.StartTick,
                            before.EndTick, before.NextPeriodTick, before.StackCount, before.Inhibited,
                            before.LifecycleRevision, before.LifecycleContext);
                    }
                }
            }
            RefreshJournalDirty();
            if (m_Periods.TryGetValue(previousId, out ulong period))
            {
                m_Periods.Remove(previousId);
                m_Periods.Add(instanceId, period);
                RefreshPeriodsDirty();
            }
        }

        public void CopyJournalKeys(List<ulong> keys)
        {
            keys.Clear();
            for (int i = 0; i < m_Journal.Count; i++)
                keys.Add(m_Journal.Keys[i]);
        }
        internal int LifecycleRevisionCount => m_LastLifecycleRevisions.Count;
        internal int LifecycleRevisionCapacity => m_Catalog.CapacityLayout.LifecycleRevisionWidth;

        internal bool TryGetLifecycleRevision(in GameplayEffectLifecycleRevisionKey key,
            out GameplayEffectLifecycleRevisionEntry entry) => m_LastLifecycleRevisions.TryGetValue(key, out entry);

        internal bool TryGetAuthoritativeLifecycleAfter(ulong instanceId, ulong afterCursor,
            out GameplayEffectLifecycleRevisionEntry entry)
        {
            GameplayEffectLifecycleRevisionKey key = GameplayEffectLifecycleRevisionKey.ForActiveSubject(instanceId);
            return m_LastLifecycleRevisions.TryGetValue(key, out entry) && entry.AcceptedChangeCursor > afterCursor;
        }

        internal bool HasLifecycleSubjectReferences(ulong instanceId, ulong acceptedChangeCursor)
        {
            if (FindActiveByInstance(instanceId) != null)
                return true;
            for (int keyIndex = 0; keyIndex < m_Journal.Count; keyIndex++)
            {
                List<PortablePredictionRecord> records = m_Journal.Values[keyIndex];
                for (int index = 0; index < records.Count; index++)
                {
                    PortablePredictionRecord record = records[index];
                    if (!record.Confirmed && record.InstanceId == instanceId &&
                        acceptedChangeCursor > record.AfterChangeCursor)
                        return true;
                }
            }
            return false;
        }

        internal void SetLifecycleRevision(in GameplayEffectLifecycleRevisionKey key,
            in GameplayEffectLifecycleRevisionEntry entry)
        {
            int index = m_LastLifecycleRevisions.IndexOfKey(key);
            entry.Retain();
            if (index >= 0)
                m_LastLifecycleRevisions.Values[index].Release();
            else
                key.Retain();
            m_LastLifecycleRevisions[key] = entry;
            RefreshLifecycleRevisionsDirty();
        }

        internal void TrimLifecycleSubjectRevisions()
        {
            bool changed = false;
            for (int index = m_LastLifecycleRevisions.Count - 1; index >= 0; index--)
            {
                GameplayEffectLifecycleRevisionKey key = m_LastLifecycleRevisions.Keys[index];
                if (key.Kind != GameplayEffectLifecycleRevisionKeyKind.ActiveSubject ||
                    HasLifecycleSubjectReferences(key.InstanceId, m_LastLifecycleRevisions.Values[index].AcceptedChangeCursor))
                    continue;
                m_LastLifecycleRevisions.Keys[index].Release();
                m_LastLifecycleRevisions.Values[index].Release();
                m_LastLifecycleRevisions.RemoveAt(index);
                changed = true;
            }
            if (changed)
                RefreshLifecycleRevisionsDirty();
        }

        internal void TrimLifecycleRevisions(ulong tick)
        {
            ulong window = (ulong)m_Catalog.CapacityLayout.Session.RetainedLifecycleRevisionTicks;
            ulong firstRetainedTick = tick > window ? tick - window : 0;
            bool journalChanged = false;
            for (int keyIndex = m_Journal.Count - 1; keyIndex >= 0; keyIndex--)
            {
                List<PortablePredictionRecord> records = m_Journal.Values[keyIndex];
                for (int recordIndex = records.Count - 1; recordIndex >= 0; recordIndex--)
                {
                    PortablePredictionRecord record = records[recordIndex];
                    if (!record.Confirmed || HasRetainedConfirmation(record, firstRetainedTick))
                        continue;
                    records.RemoveAt(recordIndex);
                    m_Scratch.DeferPredictionRelease(record);
                    journalChanged = true;
                }
                if (records.Count == 0)
                {
                    m_Journal.RemoveAt(keyIndex);
                    m_Scratch.JournalGroups.Release(records);
                }
            }
            if (journalChanged)
                RefreshJournalDirty();
            TrimLifecycleSubjectRevisions();
            bool revisionsChanged = false;
            for (int index = m_LastLifecycleRevisions.Count - 1; index >= 0; index--)
            {
                GameplayEffectLifecycleRevisionKey key = m_LastLifecycleRevisions.Keys[index];
                GameplayEffectLifecycleRevisionEntry entry = m_LastLifecycleRevisions.Values[index];
                if (key.Kind == GameplayEffectLifecycleRevisionKeyKind.ActiveSubject ||
                    entry.AcceptedTick >= firstRetainedTick ||
                    key.Kind == GameplayEffectLifecycleRevisionKeyKind.Instance && FindActiveByInstance(key.InstanceId) != null)
                    continue;
                m_LastLifecycleRevisions.Keys[index].Release();
                m_LastLifecycleRevisions.Values[index].Release();
                m_LastLifecycleRevisions.RemoveAt(index);
                revisionsChanged = true;
            }
            if (revisionsChanged)
                RefreshLifecycleRevisionsDirty();
        }

        bool HasRetainedConfirmation(PortablePredictionRecord record, ulong firstRetainedTick)
        {
            var scope = new GameplayEffectReconciliationScope(GameplayEffectReconciliationScopeKind.PredictionKey,
                record.CausalContext.PredictionKey, string.Empty, 0);
            GameplayEffectLifecycleRevisionKey key = GameplayEffectLifecycleRevisionKey.ForReconciliation(record.CausalContext.SourceActor, scope);
            if (m_LastLifecycleRevisions.TryGetValue(key, out GameplayEffectLifecycleRevisionEntry entry) && entry.AcceptedTick >= firstRetainedTick)
                return true;
            scope = new GameplayEffectReconciliationScope(GameplayEffectReconciliationScopeKind.Effect,
                record.CausalContext.PredictionKey, record.Spec.Definition.Id, 0);
            key = GameplayEffectLifecycleRevisionKey.ForReconciliation(record.CausalContext.SourceActor, scope);
            if (m_LastLifecycleRevisions.TryGetValue(key, out entry) && entry.AcceptedTick >= firstRetainedTick)
                return true;
            scope = new GameplayEffectReconciliationScope(GameplayEffectReconciliationScopeKind.Instance,
                record.CausalContext.PredictionKey, record.Spec.Definition.Id, record.PredictedInstanceId);
            key = GameplayEffectLifecycleRevisionKey.ForReconciliation(record.CausalContext.SourceActor, scope);
            return m_LastLifecycleRevisions.TryGetValue(key, out entry) && entry.AcceptedTick >= firstRetainedTick;
        }

        void RefreshLifecycleRevisionsDirty() => m_LastLifecycleRevisionsDirty = !m_Baseline.IsValid ||
            !m_Baseline.MatchesLastLifecycleRevisions(m_LastLifecycleRevisions);
        public ulong ChangeCursor
        {
            get => m_ChangeCursor;
            set
            {
                if (m_ChangeCursor == value)
                    return;
                m_ChangeCursor = value;
                m_ChangeCursorDirty = !m_Baseline.IsValid || m_Baseline.ChangeCursor != value;
            }
        }

        public bool HasChanges =>
            m_TagsDirty ||
            m_AttributeAggregator.HasChanges ||
            m_ActiveEffectsDirty ||
            m_PeriodsDirty ||
            m_JournalDirty ||
            m_ChangeCursorDirty ||
            m_LastLifecycleRevisionsDirty;

        public IReadOnlyList<string> OwnedTagsSnapshot => QueryTags;

        internal GameplayTagSnapshot QueryTags
        {
            get
            {
                if (m_OwnedTagsSnapshot != null)
                    return m_OwnedTagsSnapshot;
                int count = 0;
                for (int i = 0; i < m_TagSources.Count; i++)
                    if (m_TagSources.Keys[i].Scope.Kind == GameplayEffectScopeKind.Actor)
                        count += m_TagSources.Values[i].Count;
                GameplayTagSnapshot snapshot = m_TagSnapshotPages.Begin(count);
                snapshot.RetainStateStorage();
                int offset = 0;
                for (int i = 0; i < m_TagSources.Count; i++)
                {
                    if (m_TagSources.Keys[i].Scope.Kind != GameplayEffectScopeKind.Actor)
                        continue;
                    IReadOnlyList<string> tags = m_TagSources.Values[i];
                    for (int j = 0; j < tags.Count; j++)
                        snapshot.Write(offset++, tags[j]);
                }
                snapshot.Sort();
                m_OwnedTagsSnapshot = snapshot;
                return snapshot;
            }
        }

        internal GameplayTagSnapshot CaptureTags(IReadOnlyList<string> tags) =>
            m_TagSnapshotPages.Capture(tags);

        void InvalidateQueryTags()
        {
            m_OwnedTagsSnapshot?.ReleaseStateStorage();
            m_OwnedTagsSnapshot = null;
        }

        public bool HasTag(string tagId)
        {
            if (!m_Catalog.TryResolveTagIdentity(tagId, out string query))
                return false;
            IReadOnlyList<string> owned = OwnedTagsSnapshot;
            for (int i = 0; i < owned.Count; i++)
            {
                if (m_Catalog.IsTagOrParent(owned[i], query))
                    return true;
            }
            return false;
        }

        public bool Matches(PortableTagQuery query)
        {
            return m_Catalog.Matches(query, OwnedTagsSnapshot);
        }

        public bool HasTag(string tagId, GameplayEffectScope scope)
        {
            for (int i = 0; i < m_TagSources.Count; i++)
            {
                if (!m_TagSources.Keys[i].Scope.Equals(scope))
                    continue;
                IReadOnlyList<string> tags = m_TagSources.Values[i];
                for (int j = 0; j < tags.Count; j++)
                    if (m_Catalog.IsTagOrParent(tags[j], tagId))
                        return true;
            }
            return false;
        }

        internal void CopyScopeTagsTo(GameplayEffectScope scope, List<string> output)
        {
            for (int i = 0; i < m_TagSources.Count; i++)
            {
                if (!m_TagSources.Keys[i].Scope.Equals(scope))
                    continue;
                IReadOnlyList<string> tags = m_TagSources.Values[i];
                for (int j = 0; j < tags.Count; j++)
                    output.Add(tags[j]);
            }
        }

        internal void SetCompiledTags(GameplayTagSourceIdentityValue source, IReadOnlyList<string> values)
        {
            m_TagSources.TryGetValue(source, out IReadOnlyList<string> current);
            ApplyTagSource(source, current, EqualStrings(current, values) ? current : values);
        }

        void ApplyTagSource(GameplayTagSourceIdentityValue source, IReadOnlyList<string> current, IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                RemoveTagSource(source);
                return;
            }
            if (ReferenceEquals(current, values))
                return;
            if (values is GameplayTagSnapshot nextTags)
                nextTags.RetainStateStorage();
            if (current is GameplayTagSnapshot previousTags)
                previousTags.ReleaseStateStorage();
            if (current == null)
                source.Retain();
            m_TagSources[source] = values;
            InvalidateQueryTags();
            RefreshTagsDirty();
        }

        internal IReadOnlyList<string> SourceTags(GameplayTagSourceIdentityValue source) =>
            m_TagSources.TryGetValue(source, out IReadOnlyList<string> tags) ? tags : Array.Empty<string>();

        internal int TagCount(string tag, GameplayEffectScope scope)
        {
            int count = 0;
            for (int i = 0; i < m_TagSources.Count; i++)
            {
                if (!m_TagSources.Keys[i].Scope.Equals(scope))
                    continue;
                IReadOnlyList<string> values = m_TagSources.Values[i];
                for (int j = 0; j < values.Count; j++)
                    if (string.Equals(values[j], tag, StringComparison.Ordinal))
                        count++;
            }
            return count;
        }

        public void RemoveTagSource(GameplayTagSourceIdentityValue sourceId)
        {
            if (m_TagSources.TryGetValue(sourceId, out IReadOnlyList<string> tags))
            {
                int index = m_TagSources.IndexOfKey(sourceId);
                m_TagSources.Keys[index].Release();
                m_TagSources.RemoveAt(index);
                if (tags is GameplayTagSnapshot snapshot)
                    snapshot.ReleaseStateStorage();
                InvalidateQueryTags();
                RefreshTagsDirty();
            }
        }

        public bool TryGetAttribute(CanonicalTextValue attributeId, out PortableAttributeState value)
        {
            if (m_Catalog.TryResolveAttributeIndex(attributeId, out int index))
            {
                value = AttributeAt(index);
                return true;
            }
            value = null;
            return false;
        }

        internal PortableAttributeState AttributeAt(int index) => m_AttributeAggregator.Attributes.Values[index];

        public PortableAttributeState RequireAttribute(string attributeId) =>
            m_AttributeAggregator.Attributes[attributeId];

        public IReadOnlyList<PortableAttributeChange> MutateBase(string attributeId,
            PortableModifierOperation operation, FP magnitude, PortableClampBound clampBound, ulong causeHandle) =>
            m_AttributeAggregator.MutateBase(attributeId, operation, magnitude, clampBound, causeHandle);

        public IReadOnlyList<PortableAttributeChange> AddModifier(string attributeId, PortableAttributeModifierState modifier) =>
            m_AttributeAggregator.AddModifier(attributeId, modifier);

        public PortableModifierRemovalCursor BeginModifierRemoval(ulong sourceEffectHandle) =>
            m_AttributeAggregator.BeginModifierRemoval(sourceEffectHandle);

        public bool RemoveNextModifier(ref PortableModifierRemovalCursor cursor, out IReadOnlyList<PortableAttributeChange> changes) =>
            m_AttributeAggregator.RemoveNextModifier(ref cursor, out changes);

        public bool ApplyAuthoritativeAttribute(string attributeId, FP baseValue, FP currentValue,
            ulong revision, ulong causeHandle, out IReadOnlyList<PortableAttributeChange> changes) =>
            m_AttributeAggregator.ApplyAuthoritativeAttribute(attributeId, baseValue, currentValue, revision, causeHandle, out changes);

        public void RestorePredictedAttributeOwned(string attributeId, PortablePredictionAttributeSnapshot snapshot, ulong causeHandle,
            out IReadOnlyList<PortableAttributeChange> changes) =>
            m_AttributeAggregator.RestorePredictedAttributeOwned(attributeId, snapshot, causeHandle, out changes);

        internal IReadOnlyList<PortableAttributeChange> ApplyCorrectionAttribute(SimulationAttributeValueIngress value, ulong causeHandle)
        {
            m_Catalog.TryResolveAttributeIndex(value.AttributeId, out int index);
            return m_AttributeAggregator.ApplyCorrectionAttribute(m_Catalog.AttributeDependencies.AttributeId(index),
                value.BaseValue, value.CurrentValue, value.ValueRevision, causeHandle);
        }

        public PortableActiveEffectState FindActiveByHandle(ulong handle)
        {
            return m_ActiveByHandle.TryGetValue(handle, out PortableActiveEffectState active) ? active : null;
        }

        public PortableActiveEffectState FindActiveByInstance(ulong instanceId)
        {
            return m_ActiveByInstance.TryGetValue(instanceId, out PortableActiveEffectState active) ? active : null;
        }

        public void AddActive(PortableActiveEffectState active)
        {
            if (active == null || active.Handle == 0 || active.InstanceId == 0 || active.Spec == null)
                throw new ArgumentException("Active Gameplay Effect identity is incomplete.", nameof(active));
            if (m_ActiveByHandle.ContainsKey(active.Handle) || m_ActiveByInstance.ContainsKey(active.InstanceId))
                throw new InvalidOperationException($"Duplicate Active Gameplay Effect '{active.Handle}/{active.InstanceId}'.");
            int insertionIndex = m_ActiveEffects.Count;
            while (insertionIndex > 0 && PortableActiveEffectComparer.Compare(m_ActiveEffects[insertionIndex - 1], active) > 0)
                insertionIndex--;
            m_ActiveEffects.Insert(insertionIndex, active);
            m_ActiveByHandle.Add(active.Handle, active);
            m_ActiveByInstance.Add(active.InstanceId, active);
            RefreshActiveEffectsDirty();
        }

        public void RemoveActive(PortableActiveEffectState active)
        {
            if (active == null || !m_ActiveEffects.Remove(active))
                throw new InvalidOperationException("Active Gameplay Effect removal target is missing.");
            m_ActiveByHandle.Remove(active.Handle);
            m_ActiveByInstance.Remove(active.InstanceId);
            m_Scratch.RetireActiveState(active);
            RefreshActiveEffectsDirty();
            if (m_Periods.Remove(active.InstanceId))
                RefreshPeriodsDirty();
        }

        public ulong GetNextPeriod(ulong instanceId)
        {
            return m_Periods.TryGetValue(instanceId, out ulong value) ? value : 0;
        }
        public void SetNextPeriod(ulong instanceId, ulong tick)
        {
            if (tick == 0)
            {
                if (m_Periods.Remove(instanceId))
                    RefreshPeriodsDirty();
                return;
            }
            if (m_Periods.TryGetValue(instanceId, out ulong current) && current == tick)
                return;
            m_Periods[instanceId] = tick;
            RefreshPeriodsDirty();
        }

        public void RefreshActiveEffectsDirty()
        {
            m_ActiveEffectsDirty = !m_Baseline.IsValid || !m_Baseline.MatchesActiveEffects(m_ActiveEffects);
        }

        public void RefreshJournalDirty()
        {
            m_JournalDirty = !m_Baseline.IsValid || !m_Baseline.MatchesJournal(m_Journal);
        }

        public GameplayEffectStateAggregate Freeze()
        {
            if (!m_Baseline.IsValid || HasChanges)
            {
                GameplayEffectStateAggregate next = m_Baseline.CreateChangedFrom(
                    m_Catalog,
                    m_AttributeSnapshotPages,
                    m_PeriodSnapshotPages,
                    m_LifecycleSnapshotPages,
                    m_ActiveEffectSnapshotPages,
                    m_TagSourceSnapshotPages,

                    m_JournalSnapshotPages,
                    m_Scratch,
                    m_Scratch.FrozenOwnedTags,
                    m_OwnedTagSnapshotPages,
                    m_TagSources,
                    m_AttributeAggregator.Attributes,
                    m_ActiveEffects,
                    m_Periods,
                    m_Journal,
                    m_LastLifecycleRevisions,
                    !m_Baseline.IsValid || m_TagsDirty,
                    !m_Baseline.IsValid || m_AttributeAggregator.HasChanges,
                    !m_Baseline.IsValid || m_ActiveEffectsDirty,
                    !m_Baseline.IsValid || m_PeriodsDirty,
                    !m_Baseline.IsValid || m_JournalDirty,
                    !m_Baseline.IsValid || m_LastLifecycleRevisionsDirty,
                    m_ChangeCursor);
                next.RetainSnapshots();
                m_Baseline.ReleaseSnapshots();
                m_Baseline = next;
            }
            else
                return m_Baseline;
            ClearDirty();
            return m_Baseline;
        }

        internal bool IsCurrentSnapshot(GameplayEffectStateAggregate aggregate)
        {
            return aggregate.IsValid && m_Baseline.SharesStorageWith(aggregate) && !HasChanges;
        }

        public void Restore(GameplayEffectStateAggregate aggregate)
        {
            if (!aggregate.IsValid)
                throw new ArgumentNullException(nameof(aggregate));
            bool tagsMatch = aggregate.MatchesTagSources(m_TagSources);
            bool attributesMatch = aggregate.MatchesAttributes(m_AttributeAggregator.Attributes);
            bool activeEffectsMatch = aggregate.MatchesActiveEffects(m_ActiveEffects);
            bool periodsMatch = aggregate.MatchesPeriods(m_Periods);
            bool journalMatch = aggregate.MatchesJournal(m_Journal);
            bool revisionsMatch = aggregate.MatchesLastLifecycleRevisions(m_LastLifecycleRevisions);
            if (!tagsMatch)
            {
                ReleaseWorkingTagSources();
                aggregate.CopyTagSourcesTo(m_TagSources);
                InvalidateQueryTags();
            }
            if (!attributesMatch)
                aggregate.RestoreAttributesTo(m_AttributeAggregator.Attributes);
            m_AttributeAggregator.RebuildDependencies();
            if (!activeEffectsMatch)
            {
                m_Scratch.RetireActiveStates(m_ActiveEffects);
                m_ActiveEffects.Clear();
                aggregate.RestoreActiveEffectsTo(m_ActiveEffects, m_Scratch);
                RebuildActiveIndexes();
            }
            if (!periodsMatch)
            {
                m_Periods.Clear();
                aggregate.CopyPeriodsTo(m_Periods);
            }
            if (!journalMatch)
            {
                for (int journalIndex = 0; journalIndex < m_Journal.Count; journalIndex++)
                {
                    List<PortablePredictionRecord> records = m_Journal.Values[journalIndex];
                    for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
                        m_Scratch.DeferPredictionRelease(records[recordIndex]);
                    m_Scratch.JournalGroups.Release(records);
                }
                m_Journal.Clear();
                aggregate.CopyJournalTo(m_Journal, m_Scratch);
            }
            if (!revisionsMatch)
            {
                ReleaseWorkingLifecycleRevisions();
                aggregate.CopyLastLifecycleRevisionsTo(m_LastLifecycleRevisions);
            }
            aggregate.RetainSnapshots();
            m_Baseline.ReleaseSnapshots();
            m_Baseline = aggregate;
            m_ChangeCursor = aggregate.ChangeCursor;
            ClearDirty();
        }



        void ReleaseWorkingTagSources()
        {
            for (int i = 0; i < m_TagSources.Count; i++)
            {
                m_TagSources.Keys[i].Release();
                if (m_TagSources.Values[i] is GameplayTagSnapshot tags)
                    tags.ReleaseStateStorage();
            }
            m_TagSources.Clear();
            InvalidateQueryTags();
        }

        void ReleaseWorkingLifecycleRevisions()
        {
            for (int index = 0; index < m_LastLifecycleRevisions.Count; index++)
            {
                m_LastLifecycleRevisions.Keys[index].Release();
                m_LastLifecycleRevisions.Values[index].Release();
            }
            m_LastLifecycleRevisions.Clear();
        }

        internal void ReleaseStorage()
        {
            m_Baseline.ReleaseSnapshots();
            m_Baseline = default;
            ClearWorkingStorage();
            ClearDirty();
        }

        void ClearWorkingStorage()
        {
            ReleaseWorkingTagSources();
            for (int i = 0; i < m_ActiveEffects.Count; i++)
                m_Scratch.ReturnDecodedActiveState(m_ActiveEffects[i]);
            m_ActiveEffects.Clear();
            m_ActiveByHandle.Clear();
            m_ActiveByInstance.Clear();
            for (int i = 0; i < m_Journal.Count; i++)
            {
                List<PortablePredictionRecord> records = m_Journal.Values[i];
                for (int j = 0; j < records.Count; j++)
                    records[j].Release();
                m_Scratch.JournalGroups.Release(records);
            }
            m_Journal.Clear();
            m_Periods.Clear();
            ReleaseWorkingLifecycleRevisions();
            for (int i = 0; i < m_AttributeAggregator.Attributes.Count; i++)
                m_AttributeAggregator.Attributes.Values[i].Modifiers.Clear();
        }

        void Initialize()
        {
            m_Baseline = default;
            IReadOnlyList<string> initialTags = m_Catalog.InitialTags;
            if (initialTags.Count > 0)
                m_TagSources.Add(GameplayTagSourceIdentityValue.Literal("initial"), initialTags);
            m_AttributeAggregator.Initialize();
            m_ChangeCursor = 0;
            ClearDirty();
        }

        void ClearDirty()
        {
            m_TagsDirty = false;
            m_AttributeAggregator.AcceptBaseline(m_Baseline);
            m_ActiveEffectsDirty = false;
            m_PeriodsDirty = false;
            m_JournalDirty = false;
            m_ChangeCursorDirty = false;
            m_LastLifecycleRevisionsDirty = false;
        }

        void RebuildActiveIndexes()
        {
            m_ActiveByHandle.Clear();
            m_ActiveByInstance.Clear();
            for (int i = 0; i < m_ActiveEffects.Count; i++)
            {
                PortableActiveEffectState active = m_ActiveEffects[i];
                m_ActiveByHandle.Add(active.Handle, active);
                m_ActiveByInstance.Add(active.InstanceId, active);
            }
        }

        void RefreshTagsDirty()
        {
            m_TagsDirty = !m_Baseline.IsValid || !m_Baseline.MatchesTagSources(m_TagSources);
        }

        void RefreshPeriodsDirty()
        {
            m_PeriodsDirty = !m_Baseline.IsValid || !m_Baseline.MatchesPeriods(m_Periods);
        }

        void ValidateRuntimeClosure()
        {
            for (int i = 0; i < m_Periods.Count; i++)
            {
                ulong instanceId = m_Periods.Keys[i];
                PortableActiveEffectState active = FindActiveByInstance(instanceId);
                if (active == null || active.Spec.PeriodTicks == 0)
                    throw new InvalidDataException($"Gameplay Effect period references unknown or non-periodic instance '{instanceId}'.");
            }
            for (int index = 0; index < m_AttributeAggregator.Attributes.Count; index++)
            {
                PortableAttributeState attribute = m_AttributeAggregator.Attributes.Values[index];
                for (int i = 0; i < attribute.Modifiers.Count; i++)
                {
                    if (FindActiveByHandle(attribute.Modifiers[i].SourceEffectHandle) == null)
                        throw new InvalidDataException($"Gameplay Attribute modifier '{attribute.Modifiers[i].Handle}' references unknown Active Effect.");
                }
            }
        }

        static bool EqualStrings(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

    }
}

