using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public enum GameplayEffectScopeKind : byte
    {
        Actor = 0,
        ActionInstance = 1
    }

    public enum GameplayEffectQueryTarget : byte
    {
        Actor = 0,
        CurrentAction = 1,
        SelectedSource = 2
    }

    [ValueOperations]
    public readonly partial struct GameplayEffectScope : IEquatable<GameplayEffectScope>
    {
        GameplayEffectScope(ulong actionInstanceId, ulong executionGeneration)
        {
            ActionInstanceId = actionInstanceId;
            ExecutionGeneration = executionGeneration;
        }

        public static GameplayEffectScope Actor => default;
        public static GameplayEffectScope Action(ulong actionInstanceId, ulong executionGeneration) =>
            new GameplayEffectScope(actionInstanceId, executionGeneration);
        public static GameplayEffectScope Action(in GameplayAbilitySourceContext source) =>
            Action(source.InstanceId, source.SkillExecutionGeneration);
        public GameplayEffectScopeKind Kind => ActionInstanceId == 0
            ? GameplayEffectScopeKind.Actor : GameplayEffectScopeKind.ActionInstance;
        public ulong ActionInstanceId { get; }
        public ulong ExecutionGeneration { get; }
        public bool Equals(GameplayEffectScope other) => ActionInstanceId == other.ActionInstanceId &&
            ExecutionGeneration == other.ExecutionGeneration;
        public override bool Equals(object value) => value is GameplayEffectScope other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ActionInstanceId, ExecutionGeneration);
    }

    public enum GameplayEffectLifetimeKind : byte
    {
        Independent = 0,
        Action = 1,
        Invocation = 2
    }

    [ValueOperations]
    [ValueStorage]
    public readonly partial struct GameplayEffectHolderIdentity : IEquatable<GameplayEffectHolderIdentity>
    {
        GameplayEffectHolderIdentity(GameplayEffectLifetimeKind lifetime, ulong actionInstanceId,
            ulong executionGeneration, ulong runtimeHandle, CanonicalTextValue contentId,
            ulong generation, ulong branchRevision, int cycle)
        {
            Lifetime = lifetime;
            ActionInstanceId = actionInstanceId;
            ExecutionGeneration = executionGeneration;
            RuntimeHandle = runtimeHandle;
            ContentId = contentId;
            Generation = generation;
            BranchRevision = branchRevision;
            Cycle = cycle;
        }

        public static GameplayEffectHolderIdentity Independent => default;
        public static GameplayEffectHolderIdentity Action(ulong actionInstanceId, ulong executionGeneration) =>
            new GameplayEffectHolderIdentity(GameplayEffectLifetimeKind.Action, actionInstanceId,
                executionGeneration, 0, default, 0, 0, 0);
        public static GameplayEffectHolderIdentity Invocation(ulong actionInstanceId, ulong executionGeneration,
            ulong runtimeHandle, CanonicalTextValue contentId, ulong generation, ulong branchRevision, int cycle) =>
            new GameplayEffectHolderIdentity(GameplayEffectLifetimeKind.Invocation, actionInstanceId,
                executionGeneration, runtimeHandle, contentId, generation, branchRevision, cycle);
        public GameplayEffectLifetimeKind Lifetime { get; }
        public ulong ActionInstanceId { get; }
        public ulong ExecutionGeneration { get; }
        public ulong RuntimeHandle { get; }
        public CanonicalTextValue ContentId { get; }
        public ulong Generation { get; }
        public ulong BranchRevision { get; }
        public int Cycle { get; }
        public bool Equals(GameplayEffectHolderIdentity other) => Lifetime == other.Lifetime &&
            ActionInstanceId == other.ActionInstanceId && ExecutionGeneration == other.ExecutionGeneration &&
            RuntimeHandle == other.RuntimeHandle && ContentId.Equals(other.ContentId) &&
            Generation == other.Generation && BranchRevision == other.BranchRevision && Cycle == other.Cycle;
        public override bool Equals(object value) => value is GameplayEffectHolderIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((byte)Lifetime, ActionInstanceId,
            ExecutionGeneration, RuntimeHandle, ContentId, Generation, BranchRevision, Cycle);


    }

    [ValueOperations]
    public readonly partial struct GameplayEffectSuccessContext : IEquatable<GameplayEffectSuccessContext>
    {
        public GameplayEffectSuccessContext(CanonicalTextValue eventTag, ulong eventId, ulong sourceTick,
            ulong acceptedTick, CanonicalTextValue sourceSkillId, ulong sourceInstanceId,
            ulong sourceSkillExecutionGeneration, ulong sourceSegmentGeneration)
        {
            EventTag = eventTag;
            EventId = eventId;
            SourceTick = sourceTick;
            AcceptedTick = acceptedTick;
            SourceSkillId = sourceSkillId;
            SourceInstanceId = sourceInstanceId;
            SourceSkillExecutionGeneration = sourceSkillExecutionGeneration;
            SourceSegmentGeneration = sourceSegmentGeneration;
        }

        public CanonicalTextValue EventTag { get; }
        public ulong EventId { get; }
        public ulong SourceTick { get; }
        public ulong AcceptedTick { get; }
        public CanonicalTextValue SourceSkillId { get; }
        public ulong SourceInstanceId { get; }
        public ulong SourceSkillExecutionGeneration { get; }
        public ulong SourceSegmentGeneration { get; }
        [ValuePresence]
        public bool IsValid => EventId != 0;
        public bool Equals(GameplayEffectSuccessContext other) => EventTag.Equals(other.EventTag) &&
            EventId == other.EventId && SourceTick == other.SourceTick && AcceptedTick == other.AcceptedTick &&
            SourceSkillId.Equals(other.SourceSkillId) && SourceInstanceId == other.SourceInstanceId &&
            SourceSkillExecutionGeneration == other.SourceSkillExecutionGeneration &&
            SourceSegmentGeneration == other.SourceSegmentGeneration;
        public override bool Equals(object value) => value is GameplayEffectSuccessContext other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(EventTag, EventId, SourceTick, AcceptedTick,
            SourceSkillId, SourceInstanceId, SourceSkillExecutionGeneration, SourceSegmentGeneration);



        [ValueValidation]
        void RequireSourceSkill() => SimulationIdentity.Require(SourceSkillId, "value");
}

    [ValueOperations]
    public readonly partial struct GameplayEffectOpportunityReference : IEquatable<GameplayEffectOpportunityReference>
    {
        public GameplayEffectOpportunityReference(ulong handle, GameplayEffectSuccessContext success)
        {
            Handle = handle;
            Success = success;
        }
        public ulong Handle { get; }
        public GameplayEffectSuccessContext Success { get; }
        public bool IsValid => Handle != 0;
        public bool Equals(GameplayEffectOpportunityReference other) => Handle == other.Handle && Success.Equals(other.Success);
        public override bool Equals(object value) => value is GameplayEffectOpportunityReference other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Handle, Success);


    }

    internal enum GameplayEffectDurationKind : byte
    {
        Instant = 1,
        Duration = 2,
        Infinite = 3
    }

    internal enum GameplayEffectStackingKind : byte
    {
        None = 0,
        BySource = 1,
        ByTarget = 2
    }

    internal enum GameplayEffectDurationUpdateKind : byte
    {
        Keep = 0,
        Refresh = 1,
        Extend = 2
    }

    internal enum GameplayEffectPeriodUpdateKind : byte
    {
        Keep = 0,
        Reset = 1
    }

    internal enum GameplayEffectOverflowKind : byte
    {
        Reject = 0,
        ReplaceOldest = 1,
        ApplyOverflowEffects = 2
    }

    public enum GameplayEffectLifecycleKind : byte
    {
        Applied = 1,
        Confirmed = 2,
        Rejected = 3,
        StackChanged = 4,
        Inhibited = 5,
        Resumed = 6,
        PeriodExecuted = 7,
        Removed = 8,
        Expired = 9,
        Corrected = 10,
        Overflow = 11
    }

    public enum GameplayEffectApplyResultKind : byte
    {
        Applied = 1,
        Rejected = 2,
        MissingDefinition = 3,
        InvalidContext = 4,
        InvalidPrediction = 5,
        MissingParameter = 6,
        UndeclaredParameter = 7,
        RequirementFailed = 8,
        OverflowRejected = 9,
        DefinitionRevisionMismatch = 10,
        OverflowEffectsApplied = 11,
        CapacityExceeded = 12,
        ExecutionFailed = 13
    }

    internal enum GameplayEffectRemoveSelector : byte
    {
        Handle = 0,
        EffectId = 1,
        SourceActor = 2,
        EffectTagQuery = 3,
        Holder = 4,
        HeldByAction = 5
    }

    internal enum GameplayEffectComponentKind : byte
    {
        Modifier = 1,
        GrantedTags = 2,
        TagRequirement = 3,
        AttributeRequirement = 4,
        Execution = 5,
        AdditionalEffects = 6,
        Cue = 7,
        BlockAbilities = 8,
        ReplacementSourceRequirement = 9
    }

    internal enum GameplayEffectRequirementPhase : byte
    {
        Application = 1,
        Ongoing = 2,
        Removal = 3
    }

    internal enum GameplayEffectModifierPhase : byte
    {
        BaseValue = 1,
        CurrentValue = 2
    }

    internal enum GameplayEffectAdditionalTrigger : byte
    {
        Applied = 1,
        Period = 2,
        Removed = 3,
        Overflow = 4
    }

    internal enum GameplayEffectCueTrigger : byte
    {
        OnActive = 1,
        WhileActive = 2,
        Executed = 3,
        Removed = 4,
        Expired = 5
    }

    internal readonly struct GameplayEffectComponentDescriptor
    {
        public GameplayEffectComponentDescriptor(
            GameplayEffectComponentKind kind,
            GameplayEffectRequirementPhase requirementPhase = default,
            GameplayEffectModifierPhase modifierPhase = default,
            GameplayEffectCueTrigger cueTrigger = default)
        {
            Kind = kind;
            RequirementPhase = requirementPhase;
            ModifierPhase = modifierPhase;
            CueTrigger = cueTrigger;
        }

        public GameplayEffectComponentKind Kind { get; }
        public GameplayEffectRequirementPhase RequirementPhase { get; }
        public GameplayEffectModifierPhase ModifierPhase { get; }
        public GameplayEffectCueTrigger CueTrigger { get; }
    }

    [ValueOperations]
    public readonly partial struct GameplayEffectContextIdentity
    {
        public GameplayEffectContextIdentity(
            ActorId sourceActor,
            ActorId targetActor,
            ulong sourceActionInstanceId,
            ulong predictionKey,
            ulong gameplayResultId,
            ulong sourceTick,
            bool predicted,
            GameplayEffectScope scope = default,
            GameplayEffectHolderIdentity holder = default,
            GameplayEffectSuccessContext success = default)
        {
            SourceActor = sourceActor;
            TargetActor = targetActor;
            SourceActionInstanceId = sourceActionInstanceId;
            PredictionKey = predictionKey;
            GameplayResultId = gameplayResultId;
            SourceTick = sourceTick;
            Predicted = predicted;
            Scope = scope;
            Holder = holder;
            Success = success;
            RequireIdentity();
        }

        public ActorId SourceActor { get; }
        public ActorId TargetActor { get; }
        public ulong SourceActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong GameplayResultId { get; }
        public ulong SourceTick { get; }
        public bool Predicted { get; }
        public GameplayEffectScope Scope { get; }
        public GameplayEffectHolderIdentity Holder { get; }
        public GameplayEffectSuccessContext Success { get; }
        [ValuePresence]
        public bool IsValid => SourceActor.IsValid && TargetActor.IsValid && SourceTick != 0;


        [ValueValidation]
        void RequireIdentity()
        {
            if (!SourceActor.IsValid || !TargetActor.IsValid || SourceTick == 0)
                throw new ArgumentException("Gameplay Effect context identity is incomplete.");
            if (Predicted && (SourceActionInstanceId == 0 || PredictionKey == 0))
                throw new ArgumentException("Predicted Gameplay Effect context requires Action and prediction identities.");
        }
}

    internal readonly struct GameplayEffectControlDescriptor
    {
        public GameplayEffectControlDescriptor(
            string effectId,
            uint revision,
            GameplayEffectDurationKind duration,
            GameplayEffectStackingKind stacking,
            int maximumStacks,
            GameplayEffectDurationUpdateKind durationUpdate,
            GameplayEffectPeriodUpdateKind periodUpdate,
            GameplayEffectOverflowKind overflow,
            bool executeOnApplication)
        {
            EffectId = SimulationIdentity.Require(effectId, nameof(effectId));
            if (revision == 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            if (maximumStacks < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumStacks));
            Revision = revision;
            Duration = duration;
            Stacking = stacking;
            MaximumStacks = maximumStacks;
            DurationUpdate = durationUpdate;
            PeriodUpdate = periodUpdate;
            Overflow = overflow;
            ExecuteOnApplication = executeOnApplication;
        }

        public string EffectId { get; }
        public uint Revision { get; }
        public GameplayEffectDurationKind Duration { get; }
        public GameplayEffectStackingKind Stacking { get; }
        public int MaximumStacks { get; }
        public GameplayEffectDurationUpdateKind DurationUpdate { get; }
        public GameplayEffectPeriodUpdateKind PeriodUpdate { get; }
        public GameplayEffectOverflowKind Overflow { get; }
        public bool ExecuteOnApplication { get; }
    }

    internal readonly struct GameplayEffectPreparedSpec<TSpec>
    {
        public GameplayEffectPreparedSpec(
            GameplayEffectControlDescriptor descriptor,
            GameplayEffectContextIdentity context,
            TSpec targetSpec,
            ulong durationTicks,
            ulong periodTicks)
        {
            if (!context.IsValid)
                throw new ArgumentException("Gameplay Effect prepared spec requires a valid context.", nameof(context));
            Descriptor = descriptor;
            Context = context;
            TargetSpec = targetSpec;
            DurationTicks = durationTicks;
            PeriodTicks = periodTicks;
        }

        public GameplayEffectControlDescriptor Descriptor { get; }
        public GameplayEffectContextIdentity Context { get; }
        public TSpec TargetSpec { get; }
        public ulong DurationTicks { get; }
        public ulong PeriodTicks { get; }
    }

    internal readonly struct GameplayEffectApplicationIdentity
    {
        public GameplayEffectApplicationIdentity(
            CanonicalTextValue effectId,
            ulong authoritativeInstanceId,
            ulong authoritativeLifecycleRevision,
            GameplayEffectContextIdentity context)
        {
            EffectId = effectId.IsNull ? CanonicalTextValue.Empty : effectId;
            AuthoritativeInstanceId = authoritativeInstanceId;
            AuthoritativeLifecycleRevision = authoritativeLifecycleRevision;
            Context = context;
        }

        public CanonicalTextValue EffectId { get; }
        public ulong AuthoritativeInstanceId { get; }
        public ulong AuthoritativeLifecycleRevision { get; }
        public GameplayEffectContextIdentity Context { get; }
        public void Retain()
        {
            EffectId.Retain();
            Context.Retain();
        }

        public void Release()
        {
            EffectId.Release();
            Context.Release();
        }
    }

    [ValueOperations]
    public readonly partial struct GameplayEffectExecutionFailure
    {
        public GameplayEffectExecutionFailure(
            CanonicalTextValue ownerEffectId,
            ulong ownerInstanceId,
            GameplayEffectLifecycleKind trigger,
            CanonicalTextValue requestedEffectId,
            GameplayEffectContextIdentity context,
            ulong tick,
            GameplayEffectApplyResultKind code,
            CanonicalTextValue reason,
            CanonicalTextValue parameterId = default,
            CanonicalTextValue capacityDimension = default,
            int required = 0,
            int available = 0)
        {
            OwnerEffectId = ownerEffectId;
            OwnerInstanceId = ownerInstanceId;
            Trigger = trigger;
            RequestedEffectId = requestedEffectId;
            Context = context;
            Tick = tick;
            Code = code;
            Reason = reason;
            ParameterId = parameterId;
            CapacityDimension = capacityDimension;
            Required = required;
            Available = available;
        }

        public CanonicalTextValue OwnerEffectId { get; }
        public ulong OwnerInstanceId { get; }
        public GameplayEffectLifecycleKind Trigger { get; }
        public CanonicalTextValue RequestedEffectId { get; }
        public GameplayEffectContextIdentity Context { get; }
        public ulong Tick { get; }
        public GameplayEffectApplyResultKind Code { get; }
        public CanonicalTextValue Reason { get; }
        public CanonicalTextValue ParameterId { get; }
        public CanonicalTextValue CapacityDimension { get; }
        public int Required { get; }
        public int Available { get; }
        [ValuePresence]
        public bool IsValid => Code != 0;

    }

    internal readonly struct GameplayEffectApplyResult
    {
        public GameplayEffectApplyResult(
            GameplayEffectApplyResultKind kind,
            ulong handle,
            ulong instanceId,
            CanonicalTextValue reason,
            CanonicalTextValue parameterId = default)
        {
            Kind = kind;
            Handle = handle;
            InstanceId = instanceId;
            Reason = reason.IsNull ? CanonicalTextValue.Empty : reason;
            ParameterId = parameterId;
            Failure = default;
        }

        public GameplayEffectApplyResult(GameplayEffectExecutionFailure failure)
        {
            Kind = failure.Code;
            Handle = 0;
            InstanceId = failure.OwnerInstanceId;
            Reason = failure.Reason;
            ParameterId = failure.ParameterId;
            Failure = failure;
        }

        public GameplayEffectApplyResultKind Kind { get; }
        public ulong Handle { get; }
        public ulong InstanceId { get; }
        public CanonicalTextValue Reason { get; }
        public CanonicalTextValue ParameterId { get; }
        public GameplayEffectExecutionFailure Failure { get; }
        public bool Succeeded => Kind == GameplayEffectApplyResultKind.Applied;
        public bool AcceptedMutation => Succeeded || Kind == GameplayEffectApplyResultKind.OverflowEffectsApplied;
        public void Retain()
        {
            Reason.Retain();
            ParameterId.Retain();
            Failure.Retain();
        }

        public void Release()
        {
            Reason.Release();
            ParameterId.Release();
            Failure.Release();
        }
    }

    public enum GameplayEffectRemoveResultKind : byte
    {
        Removed = 1,
        NoMatch = 2,
        ExecutionFailed = 3,
        InvalidRequest = 4,
        Disposed = 5,
        NotReceived = 6
    }

    public readonly struct GameplayEffectRemoveResult
    {
        public GameplayEffectRemoveResult(
            GameplayEffectRemoveResultKind kind,
            ReadOnlyMemory<ulong> handles,
            GameplayEffectExecutionFailure failure = default)
        {
            Kind = kind;
            Handles = handles;
            Failure = failure;

        }


        public GameplayEffectRemoveResultKind Kind { get; }
        public ReadOnlyMemory<ulong> Handles { get; }
        public GameplayEffectExecutionFailure Failure { get; }
        public int RemovedCount => Kind == GameplayEffectRemoveResultKind.Removed ? Handles.Length : 0;
        public bool Succeeded => Kind == GameplayEffectRemoveResultKind.Removed;
        public void Retain() => Failure.Retain();
        public void Release() => Failure.Release();
    }

    public enum GameplayEffectReconciliationScopeKind : byte
    {
        PredictionKey = 1,
        Effect = 2,
        Instance = 3
    }

    [ValueOperations]
    public readonly partial struct GameplayEffectReconciliationScope
    {
        public GameplayEffectReconciliationScope(
            GameplayEffectReconciliationScopeKind kind,
            ulong predictionKey,
            CanonicalTextValue effectId,
            ulong predictedInstanceId)
        {
            Kind = kind;
            PredictionKey = predictionKey;
            EffectId = effectId;
            PredictedInstanceId = predictedInstanceId;
        }

        public GameplayEffectReconciliationScopeKind Kind { get; }
        public ulong PredictionKey { get; }
        public CanonicalTextValue EffectId { get; }
        public ulong PredictedInstanceId { get; }


        [ValuePresence]
        public bool IsValid => Kind != 0;
}

    public enum GameplayEffectReconciliationResultKind : byte
    {
        Confirmed = 1,
        Rejected = 2,
        Corrected = 3,
        NoMatch = 4,
        Duplicate = 5,
        Conflict = 6,
        IncompleteCorrection = 7,
        InvalidRequest = 8,
        Disposed = 9,
        ExecutionFailed = 10,
        Applied = 11,
        NotReceived = 12
    }

    [ValueOperations]
    public readonly partial struct GameplayEffectReconciliationResult
    {
        public GameplayEffectReconciliationResult(
            GameplayEffectReconciliationResultKind kind,
            GameplayEffectReconciliationScope scope,
            ulong authoritativeInstanceId,
            ulong authoritativeRevision,
            int recordCount,
            ulong predictedInstanceId = 0,
            ulong predictedRevision = 0,
            ulong currentRevision = 0,
            CanonicalTextValue attributeId = default,
            CanonicalTextValue reason = default,
            GameplayEffectExecutionFailure failure = default)
        {
            Kind = kind;
            Scope = scope;
            AuthoritativeInstanceId = authoritativeInstanceId;
            AuthoritativeRevision = authoritativeRevision;
            RecordCount = recordCount;
            PredictedInstanceId = predictedInstanceId;
            PredictedRevision = predictedRevision;
            CurrentRevision = currentRevision;
            AttributeId = attributeId;
            Reason = reason;
            Failure = failure;
        }

        public GameplayEffectReconciliationResultKind Kind { get; }
        public GameplayEffectReconciliationScope Scope { get; }
        public ulong AuthoritativeInstanceId { get; }
        public ulong AuthoritativeRevision { get; }
        public int RecordCount { get; }
        public ulong PredictedInstanceId { get; }
        public ulong PredictedRevision { get; }
        public ulong CurrentRevision { get; }
        public CanonicalTextValue AttributeId { get; }
        public CanonicalTextValue Reason { get; }
        public GameplayEffectExecutionFailure Failure { get; }
        public bool Succeeded => Kind == GameplayEffectReconciliationResultKind.Confirmed ||
            Kind == GameplayEffectReconciliationResultKind.Rejected ||
            Kind == GameplayEffectReconciliationResultKind.Corrected ||
            Kind == GameplayEffectReconciliationResultKind.Duplicate ||
            Kind == GameplayEffectReconciliationResultKind.Applied;


        [ValuePresence]
        public bool IsValid => Kind != 0;
}

    [Flags]
    public enum GameplayEffectCorrectionCoverage : byte
    {
        ActiveEffect = 1,
        Attributes = 2,
        Cues = 4
    }

    internal interface IGameplayEffectAuthoritativeCorrection
    {
        GameplayEffectCorrectionCoverage Coverage { get; }
        bool HasActiveEffect { get; }
        uint DefinitionRevision { get; }
        GameplayEffectActiveControlSnapshot Active { get; }
        bool CoversAttribute(CanonicalTextValue attributeId);
        bool CoversCue(CanonicalTextValue cueId);
    }

    [ValueOperations]
    public readonly partial struct GameplayTagRuntimeChange
    {
        public GameplayTagRuntimeChange(
            GameplayTagSourceIdentityValue sourceIdentity,
            string tagId,
            int beforeCount,
            int afterCount,
            string causeEffectId,
            ulong causeInstanceId,
            GameplayEffectContextIdentity context)
        {
            SourceIdentity = sourceIdentity;
            TagId = tagId;
            BeforeCount = beforeCount;
            AfterCount = afterCount;
            CauseEffectId = causeEffectId;
            CauseInstanceId = causeInstanceId;
            Context = context;
        }

        public GameplayTagSourceIdentityValue SourceIdentity { get; }
        [ValueTextRules(Declared = true)]
        public string TagId { get; }
        public int BeforeCount { get; }
        public int AfterCount { get; }
        [ValueTextRules(Declared = true)]
        public string CauseEffectId { get; }
        public ulong CauseInstanceId { get; }
        public GameplayEffectContextIdentity Context { get; }

    }

    internal readonly struct GameplayEffectRemoveRequest<TTagQuery>
    {
        public GameplayEffectRemoveRequest(
            GameplayEffectRemoveSelector selector,
            ulong handle,
            CanonicalTextValue effectId,
            ActorId sourceActor,
            TTagQuery tagQuery,
            GameplayEffectHolderIdentity holder = default,
            ulong holderActionInstanceId = 0)
        {
            Selector = selector;
            Handle = handle;
            EffectId = effectId.IsNull ? CanonicalTextValue.Empty : effectId;
            SourceActor = sourceActor;
            TagQuery = tagQuery;
            Holder = holder;
            HolderActionInstanceId = holderActionInstanceId;
        }

        public GameplayEffectRemoveSelector Selector { get; }
        public ulong Handle { get; }
        public CanonicalTextValue EffectId { get; }
        public ActorId SourceActor { get; }
        public TTagQuery TagQuery { get; }
        public GameplayEffectHolderIdentity Holder { get; }
        public ulong HolderActionInstanceId { get; }
    }

    [ValueOperations]
    public readonly partial struct GameplayEffectActiveControlSnapshot
    {
        public GameplayEffectActiveControlSnapshot(
            ulong instanceId,
            ulong startTick,
            ulong endTick,
            ulong nextPeriodTick,
            int stackCount,
            bool inhibited,
            ulong lifecycleRevision,
            GameplayEffectContextIdentity lifecycleContext)
        {
            InstanceId = instanceId;
            StartTick = startTick;
            EndTick = endTick;
            NextPeriodTick = nextPeriodTick;
            StackCount = stackCount;
            Inhibited = inhibited;
            LifecycleRevision = lifecycleRevision;
            LifecycleContext = lifecycleContext;
        }

        public ulong InstanceId { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public ulong NextPeriodTick { get; }
        public int StackCount { get; }
        public bool Inhibited { get; }
        public ulong LifecycleRevision { get; }
        public GameplayEffectContextIdentity LifecycleContext { get; }
        [ValuePresence]
        public bool IsValid => InstanceId != 0;
    }

    [ValueOperations(OwnsMemberResources = true, CopyMethod = nameof(CopyFrom), ClearMethod = nameof(Clear))]
    internal partial struct GameplayEffectActiveStateData
    {
        public ulong Handle;
        public ulong InstanceId;
        public ulong StartTick;
        public ulong EndTick;
        public ulong InsertionSequence;
        public int StackCount;
        public bool Inhibited;
        public ulong LifecycleRevision;
        public GameplayEffectContextIdentity LifecycleContext;

        internal partial void CopyFrom(in GameplayEffectActiveStateData value);
        internal partial void Clear();
    }

    [ValueStorageOwner]
    internal partial class GameplayEffectActiveControlState<TSpec> : IGameplayEffectActiveControl<TSpec>
    {
        [ValueStorageMember]
        internal GameplayEffectActiveStateData Data;
        public TSpec Spec { get; set; }
    }

    [ValueOperations(OwnsMemberResources = true, CopyMethod = nameof(CopyFrom), ClearMethod = nameof(Clear))]
    internal partial struct GameplayEffectPredictionStateData<TAttributeSnapshot>
        where TAttributeSnapshot : unmanaged
    {
        public GameplayEffectContextIdentity CausalContext;
        public ulong Handle;
        public ulong InstanceId;
        public bool CreatedActive;
        public ulong PredictedInstanceId;
        public GameplayEffectLifecycleKind MutationKind;
        public ulong ActiveBeforeInsertionSequence;
        public GameplayEffectActiveControlSnapshot ActiveBefore;
        public bool Confirmed;
        public ulong AfterLifecycleRevision;
        public ulong AfterChangeCursor;
        public ulong AuthoritativeLifecycleRevision;
        public List<string> CueIds;
        public SortedList<string, TAttributeSnapshot> Attributes;

        internal partial void CopyFrom(in GameplayEffectPredictionStateData<TAttributeSnapshot> value);
        internal partial void Clear();
    }

    [ValueStorageOwner]
    internal abstract partial class GameplayEffectPredictionControlState<TSpec, TAttributeSnapshot> : IGameplayEffectPredictionControl<TSpec>
        where TAttributeSnapshot : unmanaged
    {
        [ValueStorageMember]
        internal GameplayEffectPredictionStateData<TAttributeSnapshot> Data;

        protected GameplayEffectPredictionControlState()
        {
            Data.CueIds = new List<string>();
            Data.Attributes = new SortedList<string, TAttributeSnapshot>(StringComparer.Ordinal);
        }
        public TSpec Spec { get; set; }
        public bool HasActiveBefore => ActiveBefore.IsValid;

        ulong IGameplayEffectPredictionControl<TSpec>.PredictionKey => DescribeContext().PredictionKey;
        ulong IGameplayEffectPredictionControl<TSpec>.SourceActionInstanceId => DescribeContext().SourceActionInstanceId;
        GameplayEffectContextIdentity IGameplayEffectPredictionControl<TSpec>.Context => DescribeContext();
        IReadOnlyList<string> IGameplayEffectPredictionControl<TSpec>.CueIds => CueIds;

        protected abstract GameplayEffectContextIdentity DescribeContext();
    }

    internal interface IGameplayEffectActiveControl<TSpec>
    {
        ulong Handle { get; set; }
        ulong InstanceId { get; set; }
        TSpec Spec { get; set; }
        ulong StartTick { get; set; }
        ulong EndTick { get; set; }
        ulong InsertionSequence { get; set; }
        int StackCount { get; set; }
        bool Inhibited { get; set; }
        ulong LifecycleRevision { get; set; }
        GameplayEffectContextIdentity LifecycleContext { get; set; }
    }

    internal interface IGameplayEffectPredictionControl<TSpec>
    {
        TSpec Spec { get; }
        GameplayEffectContextIdentity Context { get; }
        ulong PredictionKey { get; }
        ulong SourceActionInstanceId { get; }
        ulong Handle { get; }
        ulong InstanceId { get; set; }
        bool CreatedActive { get; }
        ulong PredictedInstanceId { get; }
        bool HasActiveBefore { get; }
        GameplayEffectLifecycleKind MutationKind { get; }
        ulong ActiveBeforeInsertionSequence { get; }
        GameplayEffectActiveControlSnapshot ActiveBefore { get; }
        bool Confirmed { get; set; }
        ulong AfterLifecycleRevision { get; set; }
        ulong AfterChangeCursor { get; set; }
        ulong AuthoritativeLifecycleRevision { get; set; }
        IReadOnlyList<string> CueIds { get; }
    }

    internal readonly struct GameplayEffectLifecycleCommand<TApplication>
    {
        public GameplayEffectLifecycleCommand(
            GameplayEffectLifecycleKind kind,
            CanonicalTextValue effectId,
            ulong instanceId,
            GameplayEffectContextIdentity context,
            ulong startTick,
            ulong endTick,
            int stackCount,
            ulong lifecycleRevision,
            TApplication authoritativeApplication,
            GameplayEffectReconciliationScope scope,
            IGameplayEffectAuthoritativeCorrection correction)
        {
            Kind = kind;
            EffectId = effectId.IsNull ? CanonicalTextValue.Empty : effectId;
            InstanceId = instanceId;
            Context = context;
            StartTick = startTick;
            EndTick = endTick;
            StackCount = stackCount;
            LifecycleRevision = lifecycleRevision;
            AuthoritativeApplication = authoritativeApplication;
            Scope = scope;
            Correction = correction;
        }

        public GameplayEffectLifecycleKind Kind { get; }
        public CanonicalTextValue EffectId { get; }
        public ulong InstanceId { get; }
        public GameplayEffectContextIdentity Context { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public int StackCount { get; }
        public ulong LifecycleRevision { get; }
        public TApplication AuthoritativeApplication { get; }
        public GameplayEffectReconciliationScope Scope { get; }
        public IGameplayEffectAuthoritativeCorrection Correction { get; }
    }

    internal interface IGameplayEffectControlPort<
        TApplication,
        TSpec,
        TActive,
        TPrediction,
        TTagQuery,
        TSavepoint>
        where TActive : class, IGameplayEffectActiveControl<TSpec>
        where TPrediction : class, IGameplayEffectPredictionControl<TSpec>
    {
        ActorId ActorId { get; }
        ulong Tick { get; }
        bool HasActiveEffects { get; }
        bool IsDisposed { get; }
        bool RemoveQueryIsValid(TTagQuery query);
        void BeginRemoveResult();
        void AppendRemovedHandle(ulong handle);
        ReadOnlyMemory<ulong> RemovedHandles { get; }
        GameplayEffectApplicationIdentity DescribeApplication(TApplication application);
        bool TryPrepare(TApplication application, out GameplayEffectPreparedSpec<TSpec> spec, out GameplayEffectApplyResult failure);
        GameplayEffectPreparedSpec<TSpec> DescribeSpec(TSpec spec);
        int ComponentCount(TSpec spec);
        GameplayEffectComponentDescriptor DescribeComponent(TSpec spec, int componentIndex);
        bool EvaluateTagRequirement(TSpec spec, int componentIndex);
        bool EvaluateAttributeRequirement(TSpec spec, int componentIndex);
        bool MatchesEffectId(TSpec spec, CanonicalTextValue effectId);
        bool MatchesEffectTagQuery(TSpec spec, TTagQuery tagQuery);
        TActive FindActiveByHandle(ulong handle);
        TActive FindActiveByInstance(ulong instanceId);
        IReadOnlyList<TActive> AcquireActiveEffects();
        void ReleaseActiveEffects(IReadOnlyList<TActive> activeEffects);
        TActive CreateActive(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong handle,
            ulong instanceId,
            ulong startTick,
            ulong endTick,
            ulong insertionSequence,
            ulong lifecycleRevision);

        bool TryAdmitActive(GameplayEffectPreparedSpec<TSpec> spec, GameplayEffectLifecycleKind kind, out GameplayEffectExecutionFailure failure);
        bool TryAdmitPrediction(GameplayEffectPreparedSpec<TSpec> spec, out GameplayEffectExecutionFailure failure);
        bool TryAdmitLifecycle(GameplayEffectPreparedSpec<TSpec> spec, GameplayEffectLifecycleKind kind, out GameplayEffectExecutionFailure failure);
        bool TryReserveChanges(int changeStart, out GameplayEffectExecutionFailure failure);
        void AddActive(TActive active);
        void RemoveActive(TActive active);
        void RemapActiveInstance(TActive active, ulong instanceId);
        void RefreshActiveEffectsDirty();
        ulong GetNextPeriod(ulong instanceId);
        void SetNextPeriod(ulong instanceId, ulong tick);
        void DeactivatePersistent(TActive active);
        void ActivateCurrentModifier(TActive active, int componentIndex);
        void ActivateGrantedTags(TActive active);
        void ExecuteNumericComponent(
            GameplayEffectPreparedSpec<TSpec> spec,
            TActive active,
            ulong handle,
            int stackCount,
            int componentIndex);
        int AdditionalEffectCount(TSpec spec, int componentIndex);
        GameplayEffectAdditionalTrigger DescribeAdditionalEffectTrigger(
            TSpec spec,
            int componentIndex,
            int effectIndex);
        string AdditionalEffectId(TSpec spec, int componentIndex, int effectIndex);
        TApplication BuildAdditionalApplication(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong instanceId,
            int componentIndex,
            int effectIndex);
        void ReleaseAdditionalApplication(TApplication application);
        void EmitCue(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong instanceId,
            int componentIndex,
            bool trackPrediction);
        void RegisterCause(ulong handle, GameplayEffectPreparedSpec<TSpec> spec, ulong instanceId);
        void EmitLifecycle(TActive active, GameplayEffectLifecycleKind lifecycle);
        void EmitLifecycle(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong instanceId,
            GameplayEffectLifecycleKind lifecycle,
            ulong startTick,
            ulong endTick,
            int stackCount,
            ulong revision,
            bool instant);
        void EmitFailure(GameplayEffectExecutionFailure failure);
        void EmitReconciliation(GameplayEffectReconciliationResult result, GameplayEffectContextIdentity context);
        int ChangeCount { get; }
        ulong ChangeCursor { get; }
        void TrimChanges(int count);
        void RebuildCauses();
        TSavepoint CreateSavepoint();
        void Restore(TSavepoint savepoint);
        void Release(TSavepoint savepoint);
        bool SavepointIsActive(TSavepoint savepoint);
        ulong AllocateHandle();
        TPrediction CreatePrediction(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong handle,
            ulong instanceId,
            bool createdActive,
            GameplayEffectActiveControlSnapshot activeBefore,
            GameplayEffectLifecycleKind mutationKind,
            ulong activeBeforeInsertionSequence);
        void SetCurrentPrediction(TPrediction prediction);
        void CompletePrediction(TPrediction prediction);
        void CancelPrediction(TPrediction prediction);
        bool TryGetPredictions(ulong predictionKey, out IReadOnlyList<TPrediction> predictions);
        bool TryResolveLifecyclePrediction(ulong handle, out TPrediction prediction);
        IReadOnlyList<ulong> AcquirePredictionKeys();
        void ReleasePredictionKeys(IReadOnlyList<ulong> keys);
        void AddPrediction(TPrediction prediction);
        void RemovePrediction(ulong predictionKey, TPrediction prediction);
        void ConfirmPrediction(TPrediction prediction, ulong instanceId, ulong lifecycleRevision, GameplayEffectContextIdentity lifecycleContext);
        bool CanRestorePredictionAttributes(TPrediction prediction, out GameplayEffectReconciliationResult conflict);
        void RestorePredictionAttributes(TPrediction prediction);
        bool CorrectionCoversPrediction(TPrediction prediction, IGameplayEffectAuthoritativeCorrection correction, out GameplayEffectReconciliationResult missing);
        bool TryApplyCorrection(GameplayEffectLifecycleCommand<TApplication> command, out GameplayEffectReconciliationResult result);
        void EmitPredictionCueRemoval(TPrediction prediction, string cueId);
        void TrimLifecycleRevisions();
        bool TryGetLifecycleRevision(in GameplayEffectLifecycleRevisionKey key, out GameplayEffectLifecycleRevisionEntry entry);
        int LifecycleRevisionCount { get; }
        int LifecycleRevisionCapacity { get; }
        bool TryGetAuthoritativeLifecycleAfter(ulong instanceId, ulong afterCursor, out GameplayEffectLifecycleRevisionEntry entry);
        bool HasLifecycleSubjectReferences(ulong instanceId, ulong acceptedChangeCursor);
        void TrimLifecycleSubjectRevisions();
        void SetLifecycleRevision(in GameplayEffectLifecycleRevisionKey key, in GameplayEffectLifecycleRevisionEntry entry);
        void RefreshJournalDirty();
    }

    internal enum GameplayEffectAttributeSnapshotKind : byte
    {
        Source = 1,
        Target = 2
    }

    internal interface IGameplayEffectApplicationAdmissionPort<TApplication, TSpec, TScalar>
        where TSpec : class
    {
        ActorId ActorId { get; }
        bool ContextIsValid(TApplication application);
        ActorId SourceActorId(TApplication application);
        ActorId TargetActorId(TApplication application);
        bool IsPredicted(TApplication application);
        ulong SourceActionInstanceId(TApplication application);
        ulong PredictionKey(TApplication application);
        ulong AuthoritativeInstanceId(TApplication application);
        uint ApplicationDefinitionRevision(TApplication application);
        bool TryCreateSpec(TApplication application, out TSpec spec, out GameplayEffectApplyResult failure);
        uint DefinitionRevision(TSpec spec);
        int SuppliedParameterCount(TApplication application);
        CanonicalTextValue SuppliedParameterId(TApplication application, int index);
        TScalar SuppliedParameterValue(TApplication application, int index);
        int RequiredParameterCount(TSpec spec);
        string RequiredParameterId(TSpec spec, int index);
        bool ContainsParameter(TSpec spec, string parameterId);
        void AddParameter(TSpec spec, string parameterId, TScalar value);
        int SuppliedSourceAttributeCount(TApplication application);
        CanonicalTextValue SuppliedSourceAttributeId(TApplication application, int index);
        CanonicalTextValue RequiredSourceAttributeKey(TSpec spec, int index);
        TScalar SuppliedSourceAttributeValue(TApplication application, int index);
        IReadOnlyList<string> RequiredSnapshotAttributes(TSpec spec, GameplayEffectAttributeSnapshotKind kind);
        TScalar ReadTargetAttribute(string attributeId);
        void AddSourceAttribute(TSpec spec, string attributeId, TScalar value);
        void AddTargetAttribute(TSpec spec, string attributeId, TScalar value);
        IReadOnlyList<string> TargetTags();
        bool HasSourceTagSnapshot(TApplication application);
        int SourceTagCount(TApplication application);
        IReadOnlyList<string> SourceTags(TApplication application);
        void SetTargetTags(TSpec spec, IReadOnlyList<string> tags);
        void SetSourceTags(TSpec spec, IReadOnlyList<string> tags);
        bool RequiresDuration(TSpec spec);
        bool HasPeriod(TSpec spec);
        bool TryResolveDurationTicks(TSpec spec, out ulong ticks);
        bool TryResolvePeriodTicks(TSpec spec, out ulong ticks);
        void SetDurationTicks(TSpec spec, ulong ticks);
        void SetPeriodTicks(TSpec spec, ulong ticks);
        GameplayEffectPreparedSpec<TSpec> DescribeSpec(TSpec spec);
    }

    internal sealed class GameplayEffectApplicationAdmissionRuntime<TApplication, TSpec, TScalar>
        where TSpec : class
    {
        readonly IGameplayEffectApplicationAdmissionPort<TApplication, TSpec, TScalar> m_Port;
        readonly Dictionary<CanonicalTextValue, TScalar> m_SuppliedSourceAttributes;

        public GameplayEffectApplicationAdmissionRuntime(
            IGameplayEffectApplicationAdmissionPort<TApplication, TSpec, TScalar> port,
            Dictionary<CanonicalTextValue, TScalar> suppliedSourceAttributes)
        {
            m_Port = port ?? throw new ArgumentNullException(nameof(port));
            m_SuppliedSourceAttributes = suppliedSourceAttributes ??
                throw new ArgumentNullException(nameof(suppliedSourceAttributes));
        }

        public bool TryPrepare(
            TApplication application,
            out GameplayEffectPreparedSpec<TSpec> prepared,
            out GameplayEffectApplyResult failure)
        {
            prepared = default;
            if (!m_Port.ContextIsValid(application) ||
                m_Port.TargetActorId(application) != m_Port.ActorId)
            {
                return Fail(application, GameplayEffectApplyResultKind.InvalidContext, "TargetActorMismatch", out failure);
            }
            if (!m_Port.TryCreateSpec(application, out TSpec spec, out failure))
                return false;
            if (m_Port.ApplicationDefinitionRevision(application) != m_Port.DefinitionRevision(spec))
            {
                return Fail(application, GameplayEffectApplyResultKind.DefinitionRevisionMismatch, "DefinitionRevisionMismatch", out failure);
            }
            if (m_Port.IsPredicted(application) &&
                (m_Port.SourceActionInstanceId(application) == 0 || m_Port.PredictionKey(application) == 0))
            {
                return Fail(application, GameplayEffectApplyResultKind.InvalidPrediction, "PredictionIdentityMissing", out failure);
            }

            for (int i = 0; i < m_Port.SuppliedParameterCount(application); i++)
            {
                CanonicalTextValue parameterId = m_Port.SuppliedParameterId(application, i);
                int parameterIndex = FindParameterIndex(spec, parameterId);
                if (parameterIndex < 0)
                {
                    return Fail(
                        application,
                        GameplayEffectApplyResultKind.UndeclaredParameter,
                        "UndeclaredSetByCaller",
                        out failure,
                        parameterId);
                }
                m_Port.AddParameter(spec, m_Port.RequiredParameterId(spec, parameterIndex), m_Port.SuppliedParameterValue(application, i));
            }
            for (int i = 0; i < m_Port.RequiredParameterCount(spec); i++)
            {
                string parameterId = m_Port.RequiredParameterId(spec, i);
                if (!m_Port.ContainsParameter(spec, parameterId))
                {
                    return Fail(
                        application,
                        GameplayEffectApplyResultKind.MissingParameter,
                        "MissingSetByCaller",
                        out failure,
                        parameterId);
                }
            }

            m_SuppliedSourceAttributes.Clear();
            try
            {
                for (int i = 0; i < m_Port.SuppliedSourceAttributeCount(application); i++)
                {
                    CanonicalTextValue attributeId = m_Port.SuppliedSourceAttributeId(application, i).Trim();
                    if (attributeId.IsEmpty)
                        throw new ArgumentException("Identity is required.", "GameplayAttribute");
                    CanonicalTextValue captureKey = GameplayEffectRuntimeIdentity.AttributeCaptureKey(attributeId);
                    m_SuppliedSourceAttributes.Add(captureKey, m_Port.SuppliedSourceAttributeValue(application, i));
                }
                bool selfSource = m_Port.SourceActorId(application) == m_Port.TargetActorId(application);
                IReadOnlyList<string> sourceAttributes = m_Port.RequiredSnapshotAttributes(spec, GameplayEffectAttributeSnapshotKind.Source);
                for (int i = 0; i < sourceAttributes.Count; i++)
                {
                    string attributeId = sourceAttributes[i];
                    if (m_SuppliedSourceAttributes.TryGetValue(m_Port.RequiredSourceAttributeKey(spec, i), out TScalar supplied))
                        m_Port.AddSourceAttribute(spec, attributeId, supplied);
                    else if (selfSource)
                        m_Port.AddSourceAttribute(spec, attributeId, m_Port.ReadTargetAttribute(attributeId));
                    else
                    {
                        return Fail(
                            application,
                            GameplayEffectApplyResultKind.InvalidContext,
                            "SourceAttributeSnapshotMissing",
                            out failure,
                            attributeId);
                    }
                }
                IReadOnlyList<string> targetAttributes = m_Port.RequiredSnapshotAttributes(spec, GameplayEffectAttributeSnapshotKind.Target);
                for (int i = 0; i < targetAttributes.Count; i++)
                {
                    string attributeId = targetAttributes[i];
                    m_Port.AddTargetAttribute(spec, attributeId, m_Port.ReadTargetAttribute(attributeId));
                }

                IReadOnlyList<string> targetTags = m_Port.TargetTags();
                m_Port.SetTargetTags(spec, targetTags);
                if (m_Port.HasSourceTagSnapshot(application))
                {
                    m_Port.SetSourceTags(spec, m_Port.SourceTags(application));
                }
                else
                {
                    m_Port.SetSourceTags(spec, selfSource ? targetTags : Array.Empty<string>());
                }
            }
            finally
            {
                m_SuppliedSourceAttributes.Clear();
            }

            if (m_Port.RequiresDuration(spec))
            {
                if (!m_Port.TryResolveDurationTicks(spec, out ulong durationTicks))
                    return Fail(application, GameplayEffectApplyResultKind.InvalidContext, "DurationMagnitudeInvalid", out failure);
                m_Port.SetDurationTicks(spec, durationTicks);
            }
            if (m_Port.HasPeriod(spec))
            {
                if (!m_Port.TryResolvePeriodTicks(spec, out ulong periodTicks))
                    return Fail(application, GameplayEffectApplyResultKind.InvalidContext, "PeriodMagnitudeInvalid", out failure);
                m_Port.SetPeriodTicks(spec, periodTicks);
            }

            prepared = m_Port.DescribeSpec(spec);
            failure = default;
            return true;
        }

        int FindParameterIndex(TSpec spec, CanonicalTextValue parameterId)
        {
            for (int i = 0; i < m_Port.RequiredParameterCount(spec); i++)
                if (parameterId == m_Port.RequiredParameterId(spec, i))
                    return i;
            return -1;
        }

        bool Fail(
            TApplication application,
            GameplayEffectApplyResultKind kind,
            string reason,
            out GameplayEffectApplyResult failure,
            CanonicalTextValue parameterId = default)
        {
            failure = new GameplayEffectApplyResult(
                kind,
                0,
                m_Port.AuthoritativeInstanceId(application),
                reason,
                parameterId);
            return false;
        }
    }
}
