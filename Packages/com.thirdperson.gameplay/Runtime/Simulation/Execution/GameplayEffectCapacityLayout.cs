using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public enum GameplayEffectCapacityDimension : byte
    {
        DeclarationVersion,
        ContentVersion,
        ActiveEffects,
        CommandsPerTick,
        LifecycleExecutionsPerTick,
        ExternalTagSources,
        TagsPerExternalSource,
        SourceTagSnapshotEntries,
        ChangesPerTick,
        TentativeChangeEntries,
        FactsPerTick,
        PresentationRequestsPerTick,
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
        FailureOutputReservation,
        SourceTagRemovalReservation,
        SourceIngressEntries,
        SourceCommandResults
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
        readonly ReadOnlyCollection<GameplayEffectCapacityRow> m_Rows;

        GameplayEffectRuntimeCapacityLayout(StableHash contentRevision,
            GameplayEffectActorCapacity actor, GameplayEffectContentLayout content,
            int sourceWidth, int queryTagWidth, int modifierWidth,
            int additionalQueueWidth, int applicationsPerTick, int tentativeChangeWidth,
            int failureOutputReservation, int sourceTagRemovalReservation)
        {
            ContentRevision = contentRevision;
            Actor = actor;
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
            TentativeChangeWidth = tentativeChangeWidth;
            FailureOutputReservation = failureOutputReservation;
            SourceTagRemovalReservation = sourceTagRemovalReservation;
            m_Rows = Array.AsReadOnly(new[]
            {
                Row(GameplayEffectCapacityDimension.DeclarationVersion, Actor.DeclarationVersion, "CharacterGameplayEffectProfile", "GameplayEffectActorCapacity.FormatVersion"),
                Row(GameplayEffectCapacityDimension.ActiveEffects, Actor.ActiveEffects, "CharacterGameplayEffectProfile", "Actor.ActiveEffects"),
                Row(GameplayEffectCapacityDimension.CommandsPerTick, Actor.CommandsPerTick, "CharacterGameplayEffectProfile", "Actor.CommandsPerTick; root Apply/Remove admission"),
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
                Row(GameplayEffectCapacityDimension.ChangesPerTick, Actor.ChangesPerTick, "Profile / mutation admission", "Total GE changes; successful changes <= Actor.ChangesPerTick - FailureOutputReservation - SourceTagRemovalReservation"),
                Row(GameplayEffectCapacityDimension.TentativeChangeEntries, TentativeChangeWidth, "GE output / content", "Actor.ChangesPerTick + bounded application, lifecycle, active-effect and external-source output records"),
                Row(GameplayEffectCapacityDimension.FactsPerTick, Actor.FactsPerTick, "Profile / Finalize owner", "Total GE facts; successful facts <= Actor.FactsPerTick - FailureOutputReservation - SourceTagRemovalReservation"),
                Row(GameplayEffectCapacityDimension.FailureOutputReservation, FailureOutputReservation, "Command admission / once-Tick Advance", "2 * CommandsPerTick + 1; existing output admission reservation"),
                Row(GameplayEffectCapacityDimension.SourceTagRemovalReservation, SourceTagRemovalReservation, "Action terminal / Equipment and Literal source removal", "(ExternalTagSources + CommandsPerTick) * TagsPerExternalSource; initial external sources plus admitted installations"),
                Row(GameplayEffectCapacityDimension.PresentationRequestsPerTick, Actor.PresentationRequestsPerTick, "Profile / Cue owner", "Actor.PresentationRequestsPerTick; reserve actual output before mutation"),
            });
        }

        public StableHash ContentRevision { get; }
        public GameplayEffectActorCapacity Actor { get; }
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
        public int TentativeChangeWidth { get; }
        public int FailureOutputReservation { get; }
        public int SourceTagRemovalReservation { get; }
        public int SuccessfulChangesPerTick => Actor.ChangesPerTick - FailureOutputReservation - SourceTagRemovalReservation;
        public int SuccessfulFactsPerTick => Actor.FactsPerTick - FailureOutputReservation - SourceTagRemovalReservation;
        public IReadOnlyList<GameplayEffectCapacityRow> Rows => m_Rows;

        public static bool TryPrepare(GameplayEffectCatalogData content,
            GameplayEffectContentLayout contentLayout, in GameplayEffectActorCapacity actorCapacity,
            string sourceIdentity, StableHash contentRevision,
            out GameplayEffectRuntimeCapacityLayout layout,
            out GameplayEffectPreparationFailure failure)
        {
            layout = null;
            string identity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            if (content.Version != GameplayEffectCatalogData.FormatVersion)
            {
                failure = new GameplayEffectPreparationFailure(GameplayEffectPreparationFailureKind.UnsupportedVersion,
                    GameplayEffectCapacityDimension.ContentVersion, identity,
                    GameplayEffectCatalogData.FormatVersion, content.Version);
                return false;
            }
            if (!actorCapacity.TryValidate(contentLayout, identity, out failure))
                return false;
            GameplayEffectPreparationFailure current = default;
            GameplayEffectActorCapacity actor = actorCapacity;
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
                    (long)actor.ActiveEffects * contentLayout.MaximumRemovalAdditionalCount, out int removalAdditional) ||
                !Width(GameplayEffectCapacityDimension.SourceEntries,
                    1L + actor.ActiveEffects + actor.ExternalTagSources, out int sourceWidth) ||
                !Width(GameplayEffectCapacityDimension.QueryTagEntries,
                    contentLayout.InitialTagCount + (long)actor.ActiveEffects * contentLayout.MaximumGrantedTags +
                    (long)actor.ExternalTagSources * actor.TagsPerExternalSource, out int queryTagWidth) ||
                !Budget(GameplayEffectCapacityDimension.SourceTagSnapshotEntries, queryTagWidth, actor.SourceTagSnapshotEntries) ||
                !Width(GameplayEffectCapacityDimension.ModifierEntries,
                    (long)actor.ActiveEffects * contentLayout.MaximumPersistentModifiers, out int modifierWidth) ||
                !Width(GameplayEffectCapacityDimension.AdditionalApplications,
                    (long)actor.CommandsPerTick * Math.Max(contentLayout.MaximumApplicationAdditionalCount, removalAdditional) +
                    (long)actor.LifecycleExecutionsPerTick * contentLayout.MaximumPeriodAdditionalCount + removalAdditional,
                    out int additionalQueueWidth) ||
                !Width(GameplayEffectCapacityDimension.ApplicationsPerTick,
                    (long)actor.CommandsPerTick + additionalQueueWidth, out int applicationsPerTick) ||
                !Width(GameplayEffectCapacityDimension.TentativeChangeEntries,
                    actor.ChangesPerTick +
                    ((long)applicationsPerTick + actor.LifecycleExecutionsPerTick + actor.ActiveEffects) *
                    ((long)contentLayout.AttributeCount * (contentLayout.MaximumAttributeMutations + 2L * contentLayout.MaximumPersistentModifiers) +
                        2L * contentLayout.MaximumGrantedTags + 2L * contentLayout.MaximumCues + 3) +
                    2L * (actor.CommandsPerTick + actor.ExternalTagSources) * actor.TagsPerExternalSource +
                    actor.CommandsPerTick + 1L, out int tentativeChangeWidth))
            {
                failure = current;
                return false;
            }
            layout = new GameplayEffectRuntimeCapacityLayout(contentRevision, actor, contentLayout, sourceWidth, queryTagWidth,
                modifierWidth, additionalQueueWidth, applicationsPerTick, tentativeChangeWidth,
                failureOutputReservation, sourceTagRemovalReservation);
            failure = default;
            return true;

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


}
