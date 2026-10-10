using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public enum GameplayEffectCapacityDimension : byte
    {
        DeclarationVersion,
        ContentVersion,
        SessionOwner,
        ActorCount,
        ActiveEffects,
        CommandsPerTick,
        LifecycleExecutionsPerTick,
        ExternalTagSources,
        TagsPerExternalSource,
        SourceTagSnapshotEntries,
        PendingPredictionKeys,
        PredictionRecordsPerKey,
        ChangesPerTick,
        TentativeChangeEntries,
        FactsPerTick,
        PresentationRequestsPerTick,
        CandidateTicks,
        RetainedOutputs,
        SavepointDepth,
        RetainedStateVersions,
        RecordedFrames,
        RetainedBranchStateVersions,
        PredictionWindowTicks,
        RetainedLifecycleRevisionTicks,
        EncodedCheckpoints,
        CapturePayloads,
        RestorePayloads,
        SourceEntries,
        QueryTagEntries,
        FrozenTagEntries,
        ModifierEntries,
        AttributeEntries,
        ParameterEntries,
        SourceCaptureEntries,
        TargetCaptureEntries,
        AdditionalApplications,
        ApplicationsPerTick,
        StatePages,
        AggregateObjects,
        CapturePages,
        OutputPages,
        Specs,
        ActiveObjects,
        SavepointRestoreBatches,
        PredictionRecords,
        PredictionRecordObjects,
        LifecycleRevisions,
        EncodedPayloads,
        FailureOutputReservation,
        SourceTagRemovalReservation,
        SourceIngressEntries,
        SourceCommandResults,
        RetainedTextReferences
    }

    public enum GameplayEffectPreparationFailureKind : byte
    {
        MissingBudget,
        ContentRequirementExceedsCapacity,
        UnsupportedVersion,
        StorageLimitExceeded
    }

    public readonly struct GameplayEffectPreparationFailure
    {
        public GameplayEffectPreparationFailure(GameplayEffectPreparationFailureKind kind,
            GameplayEffectCapacityDimension dimension, string contentIdentity, long required, long available)
        {
            Kind = kind;
            Dimension = dimension;
            ContentIdentity = contentIdentity;
            Required = required;
            Available = available;
        }

        public GameplayEffectPreparationFailureKind Kind { get; }
        public GameplayEffectCapacityDimension Dimension { get; }
        public string ContentIdentity { get; }
        public long Required { get; }
        public long Available { get; }
        public override string ToString() =>
            $"Gameplay Effect preparation {Kind}: {ContentIdentity}, {Dimension}, required={Required}, available={Available}.";
    }

    public sealed class GameplayEffectPreparationException : InvalidOperationException
    {
        public GameplayEffectPreparationException(GameplayEffectPreparationFailure failure) : base(failure.ToString())
        {
            Failure = failure;
        }

        public GameplayEffectPreparationFailure Failure { get; }
    }

    [Serializable]
    public struct GameplayEffectActorCapacity
    {
        public const int FormatVersion = 1;
        public int DeclarationVersion;
        public int ActiveEffects;
        public int CommandsPerTick;
        public int LifecycleExecutionsPerTick;
        public int ExternalTagSources;
        public int TagsPerExternalSource;
        public int SourceTagSnapshotEntries;
        public int PendingPredictionKeys;
        public int PredictionRecordsPerKey;
        public int ChangesPerTick;
        public int FactsPerTick;
        public int PresentationRequestsPerTick;

        public bool TryValidate(GameplayEffectContentLayout content, string identity,
            out GameplayEffectPreparationFailure failure)
        {
            if (DeclarationVersion != FormatVersion)
            {
                failure = new GameplayEffectPreparationFailure(DeclarationVersion == 0
                    ? GameplayEffectPreparationFailureKind.MissingBudget
                    : GameplayEffectPreparationFailureKind.UnsupportedVersion,
                    GameplayEffectCapacityDimension.DeclarationVersion, identity, FormatVersion, DeclarationVersion);
                return false;
            }
            GameplayEffectPreparationFailure current = default;
            if (!Require(GameplayEffectCapacityDimension.ActiveEffects, content.HasActiveEffects ? 1 : 0, ActiveEffects) ||
                !Require(GameplayEffectCapacityDimension.CommandsPerTick, 1, CommandsPerTick) ||
                !Require(GameplayEffectCapacityDimension.LifecycleExecutionsPerTick, content.HasPeriodicEffects ? 1 : 0, LifecycleExecutionsPerTick) ||
                !Require(GameplayEffectCapacityDimension.ExternalTagSources, 0, ExternalTagSources) ||
                !Require(GameplayEffectCapacityDimension.TagsPerExternalSource, 0, TagsPerExternalSource) ||
                !Require(GameplayEffectCapacityDimension.SourceTagSnapshotEntries, content.InitialTagCount, SourceTagSnapshotEntries) ||
                !Require(GameplayEffectCapacityDimension.PendingPredictionKeys, 0, PendingPredictionKeys) ||
                !Require(GameplayEffectCapacityDimension.PredictionRecordsPerKey,
                    PendingPredictionKeys > 0 ? 1L + content.MaximumApplicationAdditionalCount : 0, PredictionRecordsPerKey) ||
                !Require(GameplayEffectCapacityDimension.ChangesPerTick, Math.Max(1, content.MaximumAttributeMutations), ChangesPerTick) ||
                !Require(GameplayEffectCapacityDimension.FactsPerTick, 1, FactsPerTick) ||
                !Require(GameplayEffectCapacityDimension.PresentationRequestsPerTick, content.MaximumCues, PresentationRequestsPerTick))
            {
                failure = current;
                return false;
            }
            failure = default;
            return true;

            bool Require(GameplayEffectCapacityDimension dimension, long required, int available)
            {
                if (available >= required)
                    return true;
                current = new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.ContentRequirementExceedsCapacity,
                    dimension, identity, required, available);
                return false;
            }
        }
    }

    public readonly struct GameplayEffectScheduleCapacity
    {
        public GameplayEffectScheduleCapacity(int maximumStepCount, int retainedOutputs,
            int retainedStateVersions, int retainedBranchStateVersions, int predictionWindowTicks,
            int retainedLifecycleRevisionTicks)
        {
            MaximumStepCount = maximumStepCount;
            RetainedOutputs = retainedOutputs;
            RetainedStateVersions = retainedStateVersions;
            RetainedBranchStateVersions = retainedBranchStateVersions;
            PredictionWindowTicks = predictionWindowTicks;
            RetainedLifecycleRevisionTicks = retainedLifecycleRevisionTicks;
        }

        public int MaximumStepCount { get; }
        public int RetainedOutputs { get; }
        public int RetainedStateVersions { get; }
        public int RetainedBranchStateVersions { get; }
        public int PredictionWindowTicks { get; }
        public int RetainedLifecycleRevisionTicks { get; }
    }

    public readonly struct GameplayEffectCheckpointCapacity
    {
        public GameplayEffectCheckpointCapacity(int encodedCheckpoints, int capturePayloads, int restorePayloads)
        {
            EncodedCheckpoints = encodedCheckpoints;
            CapturePayloads = capturePayloads;
            RestorePayloads = restorePayloads;
        }

        public int EncodedCheckpoints { get; }
        public int CapturePayloads { get; }
        public int RestorePayloads { get; }
    }

    public readonly struct GameplayEffectSessionCapacity
    {
        public GameplayEffectSessionCapacity(SimulationSessionId sessionId, StableHash rosterHash, int actorCount,
            int candidateTicks, int retainedOutputs, int savepointDepth, int retainedStateVersions,
            int retainedBranchStateVersions, int predictionWindowTicks, int retainedLifecycleRevisionTicks,
            int encodedCheckpoints, int capturePayloads, int restorePayloads, int recordedFrameCapacity)
        {
            SessionId = sessionId;
            RosterHash = rosterHash;
            ActorCount = actorCount;
            CandidateTicks = candidateTicks;
            RetainedOutputs = retainedOutputs;
            SavepointDepth = savepointDepth;
            RetainedStateVersions = retainedStateVersions;
            RetainedBranchStateVersions = retainedBranchStateVersions;
            PredictionWindowTicks = predictionWindowTicks;
            RetainedLifecycleRevisionTicks = retainedLifecycleRevisionTicks;
            EncodedCheckpoints = encodedCheckpoints;
            CapturePayloads = capturePayloads;
            RestorePayloads = restorePayloads;
            RecordedFrameCapacity = recordedFrameCapacity;
            long statePageCount = 2L + candidateTicks + savepointDepth + retainedStateVersions + retainedBranchStateVersions;
            if (statePageCount > int.MaxValue)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.StorageLimitExceeded, GameplayEffectCapacityDimension.StatePages,
                    sessionId.Value, statePageCount, int.MaxValue));
            StatePageCount = (int)statePageCount;
        }

        public SimulationSessionId SessionId { get; }
        public StableHash RosterHash { get; }
        public int ActorCount { get; }
        public int CandidateTicks { get; }
        public int RetainedOutputs { get; }
        public int SavepointDepth { get; }
        public int RetainedStateVersions { get; }
        public int RetainedBranchStateVersions { get; }
        public int PredictionWindowTicks { get; }
        public int RetainedLifecycleRevisionTicks { get; }
        public int EncodedCheckpoints { get; }
        public int CapturePayloads { get; }
        public int RestorePayloads { get; }
        public int RecordedFrameCapacity { get; }
        public int StatePageCount { get; }

        public static GameplayEffectSessionCapacity Prepare(SimulationSessionId sessionId, StableHash rosterHash,
            int actorCount, in GameplayEffectScheduleCapacity schedule, in GameplayEffectCheckpointCapacity checkpoints,
            int recordedFrameCapacity)
        {
            long retainedStateVersions = (long)schedule.RetainedStateVersions + recordedFrameCapacity;
            if (retainedStateVersions > int.MaxValue)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.StorageLimitExceeded,
                    GameplayEffectCapacityDimension.RetainedStateVersions, sessionId.Value,
                    retainedStateVersions, int.MaxValue));
            return new GameplayEffectSessionCapacity(sessionId, rosterHash, actorCount, schedule.MaximumStepCount,
                schedule.RetainedOutputs, GameplayEffectRuntimeCapacityLayout.MaximumSavepointDepth,
                (int)retainedStateVersions, schedule.RetainedBranchStateVersions, schedule.PredictionWindowTicks,
                schedule.RetainedLifecycleRevisionTicks, checkpoints.EncodedCheckpoints, checkpoints.CapturePayloads,
                checkpoints.RestorePayloads, recordedFrameCapacity);
        }
    }

    public readonly struct GameplayEffectCapacityRow
    {
        public GameplayEffectCapacityRow(GameplayEffectCapacityDimension dimension, int value,
            string inputOwner, string formula)
        {
            Dimension = dimension;
            Value = value;
            InputOwner = inputOwner;
            Formula = formula;
        }

        public GameplayEffectCapacityDimension Dimension { get; }
        public int Value { get; }
        public string InputOwner { get; }
        public string Formula { get; }
    }

    public sealed class GameplayEffectRuntimeCapacityLayout
    {
        public const int MaximumSavepointDepth = 2;
        readonly ReadOnlyCollection<GameplayEffectCapacityRow> m_Rows;

        GameplayEffectRuntimeCapacityLayout(CharacterGameplayEffectRuntimeBinding binding,
            in GameplayEffectSessionCapacity session, int sourceWidth, int queryTagWidth, int modifierWidth,
            int additionalQueueWidth, int applicationsPerTick, int statePageCount, int outputPageCount,
            int predictionRecordWidth, int specCount, int activeObjectCount, int savepointRestoreBatches, int capturePageCount,
            int predictionRecordCount, int lifecycleRevisionWidth, int encodedPayloadCount, int tentativeChangeWidth,
            int failureOutputReservation, int sourceTagRemovalReservation, int maximumRetainedTextReferences)
        {
            ContentRevision = binding.ContentRevision;
            Actor = binding.ActorCapacity;
            Session = session;
            GameplayEffectContentLayout content = binding.ContentLayout;
            SourceWidth = sourceWidth;
            QueryTagWidth = queryTagWidth;
            FrozenTagWidth = content.TagCount;
            ModifierWidth = modifierWidth;
            AttributeWidth = content.AttributeCount;
            ParameterWidth = content.MaximumParameterWidth;
            SourceCaptureWidth = content.MaximumSourceCaptureWidth;
            TargetCaptureWidth = content.MaximumTargetCaptureWidth;
            AdditionalQueueWidth = additionalQueueWidth;
            ApplicationsPerTick = applicationsPerTick;
            StatePageCount = statePageCount;
            OutputPageCount = outputPageCount;
            PredictionRecordWidth = predictionRecordWidth;
            SpecCount = specCount;
            ActiveObjectCount = activeObjectCount;
            SavepointRestoreBatches = savepointRestoreBatches;
            CapturePageCount = capturePageCount;
            PredictionRecordCount = predictionRecordCount;
            LifecycleRevisionWidth = lifecycleRevisionWidth;
            EncodedPayloadCount = encodedPayloadCount;
            TentativeChangeWidth = tentativeChangeWidth;
            FailureOutputReservation = failureOutputReservation;
            SourceTagRemovalReservation = sourceTagRemovalReservation;
            MaximumRetainedTextReferences = maximumRetainedTextReferences;
            m_Rows = Array.AsReadOnly(new[]
            {
                Row(GameplayEffectCapacityDimension.DeclarationVersion, Actor.DeclarationVersion, "CharacterGameplayEffectProfile", "GameplayEffectActorCapacity.FormatVersion"),
                Row(GameplayEffectCapacityDimension.ActorCount, Session.ActorCount, "Session locked roster", "Roster.Count; SessionId and RosterHash bind the layout"),
                Row(GameplayEffectCapacityDimension.ActiveEffects, Actor.ActiveEffects, "CharacterGameplayEffectProfile", "Actor.ActiveEffects"),
                Row(GameplayEffectCapacityDimension.CommandsPerTick, Actor.CommandsPerTick, "CharacterGameplayEffectProfile", "Actor.CommandsPerTick; root Apply/Remove/reconciliation admission"),
                Row(GameplayEffectCapacityDimension.LifecycleExecutionsPerTick, Actor.LifecycleExecutionsPerTick, "CharacterGameplayEffectProfile", "Actor.LifecycleExecutionsPerTick; accepted Period executions"),
                Row(GameplayEffectCapacityDimension.ExternalTagSources, Actor.ExternalTagSources, "CharacterGameplayEffectProfile", "Actor.ExternalTagSources; Action, Equipment and Literal sources except exact Literal(initial)"),
                Row(GameplayEffectCapacityDimension.TagsPerExternalSource, Actor.TagsPerExternalSource, "CharacterGameplayEffectProfile", "Actor.TagsPerExternalSource"),
                Row(GameplayEffectCapacityDimension.SourceTagSnapshotEntries, Actor.SourceTagSnapshotEntries, "CharacterGameplayEffectProfile", "Actor.SourceTagSnapshotEntries >= QueryTagWidth; explicit Source snapshot admission"),
                Row(GameplayEffectCapacityDimension.SourceEntries, SourceWidth, "Profile / Character / Action / Equipment / Literal / Effect", "1 Character source + ActiveEffects + ExternalTagSources"),
                Row(GameplayEffectCapacityDimension.QueryTagEntries, QueryTagWidth, "Profile / content GrantedTags", "InitialTagCount + ActiveEffects * MaximumGrantedTags + ExternalTagSources * TagsPerExternalSource"),
                Row(GameplayEffectCapacityDimension.FrozenTagEntries, FrozenTagWidth, "GameplayTagCatalog", "TagCount"),
                Row(GameplayEffectCapacityDimension.ModifierEntries, ModifierWidth, "Profile / Effect component layout", "ActiveEffects * MaximumPersistentModifiers"),
                Row(GameplayEffectCapacityDimension.AttributeEntries, AttributeWidth, "Gameplay Attribute registry", "AttributeCount"),
                Row(GameplayEffectCapacityDimension.ParameterEntries, ParameterWidth, "Effect declarations", "max(ParameterIds.Count)"),
                Row(GameplayEffectCapacityDimension.SourceCaptureEntries, SourceCaptureWidth, "Additional closure", "max(Source Attribute capture union width)"),
                Row(GameplayEffectCapacityDimension.TargetCaptureEntries, TargetCaptureWidth, "Effect declarations", "max(Target Attribute capture width)"),
                Row(GameplayEffectCapacityDimension.AdditionalApplications, AdditionalQueueWidth, "Profile / Additional closure", "CommandsPerTick * max(ApplicationAdditional, ActiveEffects * RemovalAdditional) + LifecycleExecutionsPerTick * PeriodAdditional + ActiveEffects * RemovalAdditional"),
                Row(GameplayEffectCapacityDimension.ApplicationsPerTick, ApplicationsPerTick, "Profile / Additional closure", "CommandsPerTick + AdditionalApplications"),
                Row(GameplayEffectCapacityDimension.CandidateTicks, Session.CandidateTicks, "Schedule runtime factory / model policy", "MaximumStepCount"),
                Row(GameplayEffectCapacityDimension.RetainedOutputs, Session.RetainedOutputs, "Schedule / packet owner", "Schedule.GameplayEffectCapacity.RetainedOutputs"),
                Row(GameplayEffectCapacityDimension.SavepointDepth, Session.SavepointDepth, "GE root / Action activation / Equipment transaction", "1 GE root + 1 outer Action or Equipment savepoint"),
                Row(GameplayEffectCapacityDimension.RecordedFrames, Session.RecordedFrameCapacity, "Session execution owner / ScenePlay PoseHistory", "Declared RecordedFrameCapacity before state creation"),
                Row(GameplayEffectCapacityDimension.RetainedStateVersions, Session.RetainedStateVersions, "Source model history / recorded frame owner", "Schedule.GameplayEffectCapacity.RetainedStateVersions + Session.RecordedFrameCapacity"),
                Row(GameplayEffectCapacityDimension.RetainedBranchStateVersions, Session.RetainedBranchStateVersions, "Source model checkpoint owner", "Schedule.GameplayEffectCapacity.RetainedBranchStateVersions"),
                Row(GameplayEffectCapacityDimension.PredictionWindowTicks, Session.PredictionWindowTicks, "Source model policy", "Schedule.GameplayEffectCapacity.PredictionWindowTicks"),
                Row(GameplayEffectCapacityDimension.RetainedLifecycleRevisionTicks, Session.RetainedLifecycleRevisionTicks, "Source model lifecycle acceptance window", "Schedule.GameplayEffectCapacity.RetainedLifecycleRevisionTicks"),
                Row(GameplayEffectCapacityDimension.StatePages, StatePageCount, "Session / transaction / state history", "1 committed + 1 workspace + CandidateTicks + SavepointDepth + RetainedStateVersions + RetainedBranchStateVersions"),
                Row(GameplayEffectCapacityDimension.AggregateObjects, AggregateObjectCount, "state page owner", "StatePageCount"),
                Row(GameplayEffectCapacityDimension.OutputPages, OutputPageCount, "Session / packet owner", "CandidateTicks + RetainedOutputs"),
                Row(GameplayEffectCapacityDimension.PendingPredictionKeys, Actor.PendingPredictionKeys, "Profile / prediction admission", "Actor.PendingPredictionKeys"),
                Row(GameplayEffectCapacityDimension.PredictionRecordsPerKey, Actor.PredictionRecordsPerKey, "Profile / prediction admission", "Actor.PredictionRecordsPerKey"),
                Row(GameplayEffectCapacityDimension.PredictionRecords, PredictionRecordWidth, "Profile", "PendingPredictionKeys * PredictionRecordsPerKey per version"),
                Row(GameplayEffectCapacityDimension.PredictionRecordObjects, PredictionRecordCount, "state / journal / deferred restore owners", "PredictionRecordWidth * (StatePageCount + SavepointRestoreBatches) + ApplicationsPerTick when prediction is enabled"),
                Row(GameplayEffectCapacityDimension.Specs, SpecCount, "state / journal / output owners", "(ActiveEffects + PredictionRecordWidth) * StatePageCount + ApplicationsPerTick * (OutputPageCount + 1 current output owner)"),
                Row(GameplayEffectCapacityDimension.SavepointRestoreBatches, SavepointRestoreBatches, "GE root command / Equipment or Action transaction / once-Tick Advance", "(CommandsPerTick + 1 Advance) * SavepointDepth"),
                Row(GameplayEffectCapacityDimension.ActiveObjects, ActiveObjectCount, "state / journal / retired Scratch owners", "(ActiveEffects + PredictionRecordWidth) * StatePageCount + ApplicationsPerTick + ActiveEffects * SavepointRestoreBatches + CommandsPerTick * PredictionRecordsPerKey when pending keys are declared"),
                Row(GameplayEffectCapacityDimension.CapturePages, CapturePageCount, "Spec / query / decoded source owners", "2 * SpecCount + StatePageCount + SourceWidth * StatePageCount"),
                Row(GameplayEffectCapacityDimension.LifecycleRevisions, LifecycleRevisionWidth, "Request acceptance window / live instance / referenced authoritative subject", "ActiveEffects live instance requests + ApplicationsPerTick * (RetainedLifecycleRevisionTicks + 1) window requests + ActiveEffects + PredictionRecordWidth subject points"),
                Row(GameplayEffectCapacityDimension.ChangesPerTick, Actor.ChangesPerTick, "Profile / mutation admission", "Total GE changes; successful changes <= Actor.ChangesPerTick - FailureOutputReservation - SourceTagRemovalReservation"),
                Row(GameplayEffectCapacityDimension.TentativeChangeEntries, TentativeChangeWidth, "GE root savepoint / content", "AcceptedChanges + bounded applications/lifecycle/Active/prediction complete tentative mutation records"),
                Row(GameplayEffectCapacityDimension.FactsPerTick, Actor.FactsPerTick, "Profile / Finalize owner", "Total GE facts; successful facts <= Actor.FactsPerTick - FailureOutputReservation - SourceTagRemovalReservation"),
                Row(GameplayEffectCapacityDimension.FailureOutputReservation, FailureOutputReservation, "Command admission / once-Tick Advance", "2 * CommandsPerTick + 1; each command failure plus reconciliation, one atomic Advance failure"),
                Row(GameplayEffectCapacityDimension.SourceTagRemovalReservation, SourceTagRemovalReservation, "Action terminal / Equipment and Literal source removal", "(ExternalTagSources + CommandsPerTick) * TagsPerExternalSource; initial external sources plus admitted installations"),
                Row(GameplayEffectCapacityDimension.PresentationRequestsPerTick, Actor.PresentationRequestsPerTick, "Profile / Cue owner", "Actor.PresentationRequestsPerTick; reserve actual output before mutation"),
                Row(GameplayEffectCapacityDimension.EncodedCheckpoints, Session.EncodedCheckpoints, "SimulationSessionHistory", "MaximumRetainedCheckpoints"),
                Row(GameplayEffectCapacityDimension.CapturePayloads, Session.CapturePayloads, "History and RuntimeHandle replay rollback owners", "MaximumSimultaneousCapturePayloads"),
                Row(GameplayEffectCapacityDimension.RestorePayloads, Session.RestorePayloads, "History restore owner", "MaximumRestorePayloads; synchronously borrowed encoded pages do not add a lease"),
                Row(GameplayEffectCapacityDimension.EncodedPayloads, EncodedPayloadCount, "Session checkpoint / codec owner", "EncodedCheckpoints + CapturePayloads + RestorePayloads; no retained GAS page after encode"),
                Row(GameplayEffectCapacityDimension.RetainedTextReferences, MaximumRetainedTextReferences, "Spec / Active / prediction / source / revision / tentative record owners", "2 * SpecCount + 2 * ActiveObjectCount + 4 * PredictionRecordCount + 2 * SourceWidth * (StatePageCount + 1) + 12 * LifecycleRevisionWidth * (StatePageCount + 1) + 12 * TentativeChangeWidth")
            });
        }

        public StableHash ContentRevision { get; }
        public GameplayEffectActorCapacity Actor { get; }
        public GameplayEffectSessionCapacity Session { get; }
        public int ActiveEffectWidth => Actor.ActiveEffects;
        public int SourceWidth { get; }
        public int QueryTagWidth { get; }
        public int FrozenTagWidth { get; }
        public int ModifierWidth { get; }
        public int AttributeWidth { get; }
        public int ParameterWidth { get; }
        public int SourceCaptureWidth { get; }
        public int TargetCaptureWidth { get; }
        public int AdditionalQueueWidth { get; }
        public int ApplicationsPerTick { get; }
        public int StatePageCount { get; }
        public int AggregateObjectCount => StatePageCount;
        public int OutputPageCount { get; }
        public int PredictionRecordWidth { get; }
        public int SpecCount { get; }
        public int ActiveObjectCount { get; }
        public int SavepointRestoreBatches { get; }
        public int CapturePageCount { get; }
        public int PredictionRecordCount { get; }
        public int LifecycleRevisionWidth { get; }
        public int EncodedPayloadCount { get; }
        public int TentativeChangeWidth { get; }
        public int FailureOutputReservation { get; }
        public int SourceTagRemovalReservation { get; }
        public int MaximumRetainedTextReferences { get; }
        public int SuccessfulChangesPerTick => Actor.ChangesPerTick - FailureOutputReservation - SourceTagRemovalReservation;
        public int SuccessfulFactsPerTick => Actor.FactsPerTick - FailureOutputReservation - SourceTagRemovalReservation;
        public IReadOnlyList<GameplayEffectCapacityRow> Rows => m_Rows;

        public static bool TryPrepare(CharacterGameplayEffectRuntimeBinding binding,
            in GameplayEffectSessionCapacity session, out GameplayEffectRuntimeCapacityLayout layout,
            out GameplayEffectPreparationFailure failure)
        {
            layout = null;
            string identity = binding.SourceIdentity;
            GameplayEffectPreparationFailure current = default;
            if (!session.SessionId.IsValid || !session.RosterHash.IsValid)
            {
                failure = new GameplayEffectPreparationFailure(GameplayEffectPreparationFailureKind.MissingBudget,
                    GameplayEffectCapacityDimension.SessionOwner, identity, 1, 0);
                return false;
            }
            if (!Require(GameplayEffectCapacityDimension.ActorCount, 1, session.ActorCount) ||
                !Require(GameplayEffectCapacityDimension.CandidateTicks, 1, session.CandidateTicks) ||
                !Require(GameplayEffectCapacityDimension.RetainedOutputs, 0, session.RetainedOutputs) ||
                !Require(GameplayEffectCapacityDimension.SavepointDepth, 1, session.SavepointDepth) ||
                !Require(GameplayEffectCapacityDimension.RetainedStateVersions, 0, session.RetainedStateVersions) ||
                !Require(GameplayEffectCapacityDimension.RecordedFrames, 0, session.RecordedFrameCapacity) ||
                !Require(GameplayEffectCapacityDimension.RetainedBranchStateVersions, 0, session.RetainedBranchStateVersions) ||
                !Require(GameplayEffectCapacityDimension.PredictionWindowTicks, 0, session.PredictionWindowTicks) ||
                !Require(GameplayEffectCapacityDimension.RetainedLifecycleRevisionTicks, 0, session.RetainedLifecycleRevisionTicks) ||
                !Require(GameplayEffectCapacityDimension.EncodedCheckpoints, 0, session.EncodedCheckpoints) ||
                !Require(GameplayEffectCapacityDimension.CapturePayloads, 0, session.CapturePayloads) ||
                !Require(GameplayEffectCapacityDimension.RestorePayloads, 0, session.RestorePayloads))
            {
                failure = current;
                return false;
            }
            GameplayEffectActorCapacity actor = binding.ActorCapacity;
            GameplayEffectContentLayout content = binding.ContentLayout;
            int statePageCount = session.StatePageCount;
            if (!Width(GameplayEffectCapacityDimension.FailureOutputReservation,
                    2L * actor.CommandsPerTick + 1, out int failureOutputReservation) ||
                !Width(GameplayEffectCapacityDimension.SourceTagRemovalReservation,
                    ((long)actor.ExternalTagSources + actor.CommandsPerTick) * actor.TagsPerExternalSource,
                    out int sourceTagRemovalReservation) ||
                !Budget(GameplayEffectCapacityDimension.ChangesPerTick,
                    (long)failureOutputReservation + sourceTagRemovalReservation, actor.ChangesPerTick) ||
                !Budget(GameplayEffectCapacityDimension.FactsPerTick,
                    (long)failureOutputReservation + sourceTagRemovalReservation, actor.FactsPerTick) ||
                !Width(GameplayEffectCapacityDimension.AdditionalApplications,
                    (long)actor.ActiveEffects * content.MaximumRemovalAdditionalCount, out int removalAdditional) ||
                !Width(GameplayEffectCapacityDimension.SourceEntries,
                    1L + actor.ActiveEffects + actor.ExternalTagSources, out int sourceWidth) ||
                !Width(GameplayEffectCapacityDimension.QueryTagEntries,
                    content.InitialTagCount + (long)actor.ActiveEffects * content.MaximumGrantedTags +
                    (long)actor.ExternalTagSources * actor.TagsPerExternalSource, out int queryTagWidth) ||
                !Budget(GameplayEffectCapacityDimension.SourceTagSnapshotEntries, queryTagWidth, actor.SourceTagSnapshotEntries) ||
                !Width(GameplayEffectCapacityDimension.ModifierEntries,
                    (long)actor.ActiveEffects * content.MaximumPersistentModifiers, out int modifierWidth) ||
                !Width(GameplayEffectCapacityDimension.AdditionalApplications,
                    (long)actor.CommandsPerTick * Math.Max(content.MaximumApplicationAdditionalCount, removalAdditional) +
                    (long)actor.LifecycleExecutionsPerTick * content.MaximumPeriodAdditionalCount + removalAdditional,
                    out int additionalQueueWidth) ||
                !Width(GameplayEffectCapacityDimension.ApplicationsPerTick,
                    (long)actor.CommandsPerTick + additionalQueueWidth, out int applicationsPerTick) ||
                !Width(GameplayEffectCapacityDimension.OutputPages,
                    (long)session.CandidateTicks + session.RetainedOutputs, out int outputPageCount) ||
                !Width(GameplayEffectCapacityDimension.PredictionRecords,
                    (long)actor.PendingPredictionKeys * actor.PredictionRecordsPerKey, out int predictionRecordWidth) ||
                !Width(GameplayEffectCapacityDimension.SavepointRestoreBatches,
                    (actor.CommandsPerTick + 1L) * session.SavepointDepth, out int savepointRestoreBatches) ||
                !Width(GameplayEffectCapacityDimension.ActiveObjects,
                    ((long)actor.ActiveEffects + predictionRecordWidth) * statePageCount, out int retainedActiveObjects) ||
                !Width(GameplayEffectCapacityDimension.ActiveObjects,
                    (long)actor.ActiveEffects * savepointRestoreBatches, out int restoredActiveObjects) ||
                !Width(GameplayEffectCapacityDimension.ActiveObjects,
                    (long)retainedActiveObjects + applicationsPerTick + restoredActiveObjects +
                    (actor.PendingPredictionKeys > 0 ? (long)actor.CommandsPerTick * actor.PredictionRecordsPerKey : 0),
                    out int activeObjectCount) ||
                !Width(GameplayEffectCapacityDimension.Specs,
                    retainedActiveObjects + applicationsPerTick * (outputPageCount + 1L), out int specCount) ||
                !Width(GameplayEffectCapacityDimension.CapturePages,
                    2L * specCount + ((long)sourceWidth + 1) * statePageCount, out int capturePageCount) ||
                !Width(GameplayEffectCapacityDimension.PredictionRecordObjects,
                    predictionRecordWidth * ((long)statePageCount + savepointRestoreBatches) +
                    (actor.PendingPredictionKeys > 0 ? applicationsPerTick : 0), out int predictionRecordCount) ||
                !Width(GameplayEffectCapacityDimension.LifecycleRevisions,
                    2L * actor.ActiveEffects + predictionRecordWidth +
                    (long)applicationsPerTick * (session.RetainedLifecycleRevisionTicks + 1L),
                    out int lifecycleRevisionWidth) ||
                !Width(GameplayEffectCapacityDimension.EncodedPayloads,
                    (long)session.EncodedCheckpoints + session.CapturePayloads + session.RestorePayloads,
                    out int encodedPayloadCount) ||
                !Width(GameplayEffectCapacityDimension.TentativeChangeEntries,
                    actor.ChangesPerTick +
                    ((long)applicationsPerTick + actor.LifecycleExecutionsPerTick + actor.ActiveEffects + predictionRecordWidth) *
                    ((long)content.AttributeCount * (content.MaximumAttributeMutations + 2L * content.MaximumPersistentModifiers) +
                        2L * content.MaximumGrantedTags + 2L * content.MaximumCues + 3) +
                    (long)predictionRecordWidth * content.AttributeCount * content.AttributeCount +
                    2L * (actor.CommandsPerTick + actor.ExternalTagSources) * actor.TagsPerExternalSource +
                    actor.CommandsPerTick + 1L, out int tentativeChangeWidth) ||
                !Width(GameplayEffectCapacityDimension.RetainedTextReferences,
                    (long)sourceWidth * (statePageCount + 1L), out int retainedSourceVersions) ||
                !Width(GameplayEffectCapacityDimension.RetainedTextReferences,
                    (long)lifecycleRevisionWidth * (statePageCount + 1L), out int retainedRevisionVersions) ||
                !Width(GameplayEffectCapacityDimension.RetainedTextReferences,
                    2L * specCount + 2L * activeObjectCount + 4L * predictionRecordCount +
                    2L * retainedSourceVersions + 12L * retainedRevisionVersions + 12L * tentativeChangeWidth,
                    out int maximumRetainedTextReferences))
            {
                failure = current;
                return false;
            }
            layout = new GameplayEffectRuntimeCapacityLayout(binding, in session, sourceWidth, queryTagWidth,
                modifierWidth, additionalQueueWidth, applicationsPerTick, statePageCount, outputPageCount,
                predictionRecordWidth, specCount, activeObjectCount, savepointRestoreBatches, capturePageCount, predictionRecordCount,
                lifecycleRevisionWidth, encodedPayloadCount, tentativeChangeWidth, failureOutputReservation, sourceTagRemovalReservation,
                maximumRetainedTextReferences);
            failure = default;
            return true;

            bool Require(GameplayEffectCapacityDimension dimension, long required, int available)
            {
                if (available >= required)
                    return true;
                current = new GameplayEffectPreparationFailure(GameplayEffectPreparationFailureKind.MissingBudget,
                    dimension, identity, required, available);
                return false;
            }

            bool Width(GameplayEffectCapacityDimension dimension, long required, out int value)
            {
                if (required > int.MaxValue)
                {
                    value = 0;
                    current = new GameplayEffectPreparationFailure(GameplayEffectPreparationFailureKind.StorageLimitExceeded,
                        dimension, identity, required, int.MaxValue);
                    return false;
                }
                value = (int)required;
                return true;
            }

            bool Budget(GameplayEffectCapacityDimension dimension, long required, int available)
            {
                if (available >= required)
                    return true;
                current = new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.ContentRequirementExceedsCapacity,
                    dimension, identity, required, available);
                return false;
            }
        }

        static GameplayEffectCapacityRow Row(GameplayEffectCapacityDimension dimension, int value,
            string owner, string formula) => new GameplayEffectCapacityRow(dimension, value, owner, formula);
    }

    public static class GameplayEffectActorCapacityCodec
    {
        public static void Write(CanonicalWriter writer, in GameplayEffectActorCapacity capacity)
        {
            writer.WriteInt32(capacity.DeclarationVersion);
            writer.WriteInt32(capacity.ActiveEffects);
            writer.WriteInt32(capacity.CommandsPerTick);
            writer.WriteInt32(capacity.LifecycleExecutionsPerTick);
            writer.WriteInt32(capacity.ExternalTagSources);
            writer.WriteInt32(capacity.TagsPerExternalSource);
            writer.WriteInt32(capacity.SourceTagSnapshotEntries);
            writer.WriteInt32(capacity.PendingPredictionKeys);
            writer.WriteInt32(capacity.PredictionRecordsPerKey);
            writer.WriteInt32(capacity.ChangesPerTick);
            writer.WriteInt32(capacity.FactsPerTick);
            writer.WriteInt32(capacity.PresentationRequestsPerTick);
        }

        public static GameplayEffectActorCapacity Read(CanonicalReader reader) => new GameplayEffectActorCapacity
        {
            DeclarationVersion = reader.ReadInt32(),
            ActiveEffects = reader.ReadInt32(),
            CommandsPerTick = reader.ReadInt32(),
            LifecycleExecutionsPerTick = reader.ReadInt32(),
            ExternalTagSources = reader.ReadInt32(),
            TagsPerExternalSource = reader.ReadInt32(),
            SourceTagSnapshotEntries = reader.ReadInt32(),
            PendingPredictionKeys = reader.ReadInt32(),
            PredictionRecordsPerKey = reader.ReadInt32(),
            ChangesPerTick = reader.ReadInt32(),
            FactsPerTick = reader.ReadInt32(),
            PresentationRequestsPerTick = reader.ReadInt32()
        };
    }
}
