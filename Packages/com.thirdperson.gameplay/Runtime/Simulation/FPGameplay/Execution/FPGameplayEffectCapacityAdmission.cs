using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed partial class FPGameplayEffectTarget
    {
        bool TryAdmitSpec(SimulationGameplayEffectApplication application, PortableEffectDefinition definition,
            out GameplayEffectApplyResult failure)
        {
            GameplayEffectRuntimeCapacityLayout capacity = m_Scratch.Capacity;
            GameplayEffectContextIdentity context = ToCommon(application.Context);
            if (context.Scope.Kind != definition.Scope || context.Holder.Lifetime != definition.Lifetime ||
                (definition.Scope == GameplayEffectScopeKind.ActionInstance || definition.Lifetime != GameplayEffectLifetimeKind.Independent) &&
                context.SourceActionInstanceId == 0 ||
                context.Success.IsValid && (context.Scope.Kind != GameplayEffectScopeKind.ActionInstance ||
                    !GameplayEffectScope.Action(context.Success.SourceInstanceId,
                        context.Success.SourceSkillExecutionGeneration).Equals(context.Scope)))
            {
                failure = new GameplayEffectApplyResult(GameplayEffectApplyResultKind.InvalidContext,
                    0, application.AuthoritativeInstanceId, "EffectScopeOrHolderMismatch");
                return false;
            }
            if (context.Success.IsValid && !m_Catalog.TryResolveTagIdentity(context.Success.EventTag, out _))
            {
                failure = new GameplayEffectApplyResult(GameplayEffectApplyResultKind.InvalidContext,
                    0, application.AuthoritativeInstanceId, "SuccessEventTagUnregistered");
                return false;
            }
            GameplayEffectExecutionFailure rejection;
            if (m_Scratch.SpecApplicationsThisTick == capacity.ApplicationsPerTick)
                rejection = CapacityFailure(definition.Id, application.AuthoritativeInstanceId, context,
                    GameplayEffectLifecycleKind.Applied, nameof(GameplayEffectCapacityDimension.ApplicationsPerTick),
                    m_Scratch.SpecApplicationsThisTick + 1, capacity.ApplicationsPerTick);
            else if (m_Scratch.AvailableSpecCount == 0)
                rejection = CapacityFailure(definition.Id, application.AuthoritativeInstanceId, context,
                    GameplayEffectLifecycleKind.Applied, nameof(GameplayEffectCapacityDimension.Specs), 1, 0);
            else if (application.SourceAttributeSnapshots.Count > capacity.SourceCaptureWidth)
                rejection = CapacityFailure(definition.Id, application.AuthoritativeInstanceId, context,
                    GameplayEffectLifecycleKind.Applied, nameof(GameplayEffectCapacityDimension.SourceCaptureEntries),
                    application.SourceAttributeSnapshots.Count, capacity.SourceCaptureWidth);
            else if (application.HasSourceTagSnapshot && application.SourceTagSnapshot.Count > capacity.Actor.SourceTagSnapshotEntries)
                rejection = CapacityFailure(definition.Id, application.AuthoritativeInstanceId, context,
                    GameplayEffectLifecycleKind.Applied, nameof(GameplayEffectCapacityDimension.SourceTagSnapshotEntries),
                    application.SourceTagSnapshot.Count, capacity.Actor.SourceTagSnapshotEntries);
            else if (application.HasSourceTagSnapshot && application.SourceTagSnapshot.Count > 0 &&
                !(application.SourceTagSnapshot is GameplayTagSnapshot) && m_State.AvailableTagSnapshotPages == 0)
                rejection = CapacityFailure(definition.Id, application.AuthoritativeInstanceId, context,
                    GameplayEffectLifecycleKind.Applied, nameof(GameplayEffectCapacityDimension.CapturePages), 1, 0);
            else
            {
                if (application.HasSourceTagSnapshot && !(application.SourceTagSnapshot is GameplayTagSnapshot))
                    for (int i = 0; i < application.SourceTagSnapshot.Count; i++)
                        if (!m_Catalog.ContainsTag(application.SourceTagSnapshot[i]))
                        {
                            failure = new GameplayEffectApplyResult(GameplayEffectApplyResultKind.InvalidContext,
                                0, application.AuthoritativeInstanceId, "SourceTagUnregistered", application.SourceTagSnapshot[i]);
                            return false;
                        }
                m_Scratch.SpecApplicationsThisTick++;
                failure = default;
                return true;
            }
            failure = new GameplayEffectApplyResult(rejection);
            return false;
        }



        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState,
            PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, FPAbilityExecutionSavepoint>.TryAdmitActive(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec, GameplayEffectLifecycleKind kind,
            out GameplayEffectExecutionFailure failure) =>
            TryAdmitActiveResources(spec.Descriptor.EffectId, 0, spec.Context, kind, out failure);

        bool TryAdmitActiveResources(CanonicalTextValue effectId, ulong instanceId, GameplayEffectContextIdentity context,
            GameplayEffectLifecycleKind kind, out GameplayEffectExecutionFailure failure)
        {
            if (m_State.ActiveEffects.Count == m_Scratch.Capacity.ActiveEffectWidth)
            {
                failure = CapacityFailure(effectId, instanceId, context, kind,
                    nameof(GameplayEffectCapacityDimension.ActiveEffects), m_State.ActiveEffects.Count + 1,
                    m_Scratch.Capacity.ActiveEffectWidth);
                return false;
            }
            if (m_Scratch.AvailableActiveCount == 0)
            {
                failure = CapacityFailure(effectId, instanceId, context, kind,
                    nameof(GameplayEffectCapacityDimension.ActiveObjects), 1, 0);
                return false;
            }
            failure = default;
            return true;
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState,
            PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, FPAbilityExecutionSavepoint>.TryAdmitPrediction(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec, out GameplayEffectExecutionFailure failure)
        {
            GameplayEffectActorCapacity actor = m_Scratch.Capacity.Actor;
            bool hasKey = m_State.TryGetJournalRecords(spec.Context.PredictionKey, out IReadOnlyList<PortablePredictionRecord> records);
            if (!hasKey && m_State.PredictionKeyCount == actor.PendingPredictionKeys)
            {
                failure = CapacityFailure(spec.Descriptor.EffectId, 0, spec.Context, GameplayEffectLifecycleKind.Applied,
                    nameof(GameplayEffectCapacityDimension.PendingPredictionKeys), m_State.PredictionKeyCount + 1,
                    actor.PendingPredictionKeys);
                return false;
            }
            int recordCount = hasKey ? records.Count : 0;
            if (recordCount == actor.PredictionRecordsPerKey)
            {
                failure = CapacityFailure(spec.Descriptor.EffectId, 0, spec.Context, GameplayEffectLifecycleKind.Applied,
                    nameof(GameplayEffectCapacityDimension.PredictionRecordsPerKey), recordCount + 1,
                    actor.PredictionRecordsPerKey);
                return false;
            }
            if (m_Scratch.AvailablePredictionCount == 0)
            {
                failure = CapacityFailure(spec.Descriptor.EffectId, 0, spec.Context, GameplayEffectLifecycleKind.Applied,
                    nameof(GameplayEffectCapacityDimension.PredictionRecordObjects), 1, 0);
                return false;
            }
            failure = default;
            return true;
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState,
            PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, FPAbilityExecutionSavepoint>.TryAdmitLifecycle(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec, GameplayEffectLifecycleKind kind,
            out GameplayEffectExecutionFailure failure)
        {
            if (m_Scratch.LifecycleExecutionsThisTick == m_Scratch.Capacity.Actor.LifecycleExecutionsPerTick)
            {
                failure = CapacityFailure(spec.Descriptor.EffectId, 0, spec.Context, kind,
                    nameof(GameplayEffectCapacityDimension.LifecycleExecutionsPerTick),
                    m_Scratch.LifecycleExecutionsThisTick + 1, m_Scratch.Capacity.Actor.LifecycleExecutionsPerTick);
                return false;
            }
            m_Scratch.LifecycleExecutionsThisTick++;
            failure = default;
            return true;
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState,
            PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, FPAbilityExecutionSavepoint>.TryReserveChanges(
            int changeStart, out GameplayEffectExecutionFailure failure)
        {
            if (m_ChangeCount == changeStart)
            {
                failure = default;
                return true;
            }
            GameplayEffectRuntimeCapacityLayout capacity = m_Scratch.Capacity;
            GameplayEffectActorCapacity actor = capacity.Actor;
            CountOutputs(out int successfulChanges, out int cues);
            string dimension;
            int required;
            int available;
            if (successfulChanges > capacity.SuccessfulChangesPerTick)
            {
                dimension = nameof(GameplayEffectCapacityDimension.ChangesPerTick);
                required = successfulChanges + capacity.FailureOutputReservation + capacity.SourceTagRemovalReservation;
                available = actor.ChangesPerTick;
            }
            else if (successfulChanges > capacity.SuccessfulFactsPerTick)
            {
                dimension = nameof(GameplayEffectCapacityDimension.FactsPerTick);
                required = successfulChanges + capacity.FailureOutputReservation + capacity.SourceTagRemovalReservation;
                available = actor.FactsPerTick;
            }
            else
            {
                if (cues <= actor.PresentationRequestsPerTick)
                {
                    failure = default;
                    return true;
                }
                dimension = nameof(GameplayEffectCapacityDimension.PresentationRequestsPerTick);
                required = cues;
                available = actor.PresentationRequestsPerTick;
            }
            ref readonly PortableEffectRuntimeChange change = ref Changes[changeStart];
            CanonicalTextValue effectId = change.Kind == PortableEffectChangeKind.Tag
                ? change.Tag.CauseEffectId : change.Definition?.Id ?? change.CauseEffectId;
            GameplayEffectContextIdentity context = change.Kind == PortableEffectChangeKind.Tag
                ? change.Tag.Context : change.Context.IsValid ? ToCommon(change.Context) : default;
            failure = CapacityFailure(effectId, change.InstanceId, context, GameplayEffectLifecycleKind.Applied,
                dimension, required, available);
            return false;
        }

        bool TryAdmitExternalTags(GameplayTagSourceIdentityValue source, IReadOnlyList<string> tags,
            out GameplayEffectExecutionFailure failure)
        {
            GameplayEffectActorCapacity actor = m_Scratch.Capacity.Actor;
            GameplayEffectContextIdentity context = new GameplayEffectContextIdentity(m_ActorId, m_ActorId,
                source.Kind == 1 ? source.NumericIdentity : 0, 0, 0, m_Tick.Value, false);

            if (source.IsExternalSource && tags.Count > 0 && m_State.SourceTags(source).Count == 0 &&
                m_State.ExternalTagSourceCount == actor.ExternalTagSources)
            {
                failure = CapacityFailure(string.Empty, 0, context, GameplayEffectLifecycleKind.Applied,
                    nameof(GameplayEffectCapacityDimension.ExternalTagSources), m_State.ExternalTagSourceCount + 1,
                    actor.ExternalTagSources);
                return false;
            }
            failure = default;
            return true;
        }

        bool TryAdmitCorrectionResources(GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            SimulationGameplayEffectCorrection correction, PortableEffectDefinition definition, bool needsActiveState,
            int predictedCueRemovals, out GameplayEffectExecutionFailure failure)
        {
            GameplayEffectRuntimeCapacityLayout capacity = m_Scratch.Capacity;
            int attributes = (correction.Coverage & GameplayEffectCorrectionCoverage.Attributes) != 0
                ? correction.Attributes.Count : 0;
            if (attributes > capacity.AttributeWidth)
            {
                failure = CapacityFailure(command.EffectId, command.InstanceId, command.Context, command.Kind,
                    nameof(GameplayEffectCapacityDimension.AttributeEntries), attributes, capacity.AttributeWidth);
                return false;
            }
            int cues = predictedCueRemovals + ((correction.Coverage & GameplayEffectCorrectionCoverage.Cues) != 0
                ? correction.Cues.Count : 0);
            int tentative = m_ChangeCount + cues;
            if (tentative > capacity.TentativeChangeWidth)
            {
                failure = CapacityFailure(command.EffectId, command.InstanceId, command.Context, command.Kind,
                    nameof(GameplayEffectCapacityDimension.TentativeChangeEntries), tentative, capacity.TentativeChangeWidth);
                return false;
            }
            CountOutputs(out int successfulChanges, out int recordedCues);
            int required = successfulChanges + cues + capacity.FailureOutputReservation + capacity.SourceTagRemovalReservation;
            if (required > capacity.Actor.ChangesPerTick)
            {
                failure = CapacityFailure(command.EffectId, command.InstanceId, command.Context, command.Kind,
                    nameof(GameplayEffectCapacityDimension.ChangesPerTick), required, capacity.Actor.ChangesPerTick);
                return false;
            }
            if (required > capacity.Actor.FactsPerTick)
            {
                failure = CapacityFailure(command.EffectId, command.InstanceId, command.Context, command.Kind,
                    nameof(GameplayEffectCapacityDimension.FactsPerTick), required, capacity.Actor.FactsPerTick);
                return false;
            }
            required = recordedCues + cues;
            if (required > capacity.Actor.PresentationRequestsPerTick)
            {
                failure = CapacityFailure(command.EffectId, command.InstanceId, command.Context, command.Kind,
                    nameof(GameplayEffectCapacityDimension.PresentationRequestsPerTick), required,
                    capacity.Actor.PresentationRequestsPerTick);
                return false;
            }
            if ((correction.Coverage & GameplayEffectCorrectionCoverage.ActiveEffect) != 0 && correction.HasActiveEffect)
            {
                if (m_Scratch.AvailableSpecCount == 0)
                {
                    failure = CapacityFailure(definition.Id, command.InstanceId, command.Context, command.Kind,
                        nameof(GameplayEffectCapacityDimension.Specs), 1, 0);
                    return false;
                }
                if (needsActiveState && !TryAdmitActiveResources(definition.Id, command.InstanceId,
                    command.Context, command.Kind, out failure))
                    return false;
                SimulationGameplayEffectSpecSnapshot snapshot = correction.Spec;
                if (snapshot.SetByCallerValues.Count > capacity.ParameterWidth)
                {
                    failure = CapacityFailure(definition.Id, command.InstanceId, command.Context, command.Kind,
                        nameof(GameplayEffectCapacityDimension.ParameterEntries), snapshot.SetByCallerValues.Count, capacity.ParameterWidth);
                    return false;
                }
                if (snapshot.SourceAttributes.Count > capacity.SourceCaptureWidth)
                {
                    failure = CapacityFailure(definition.Id, command.InstanceId, command.Context, command.Kind,
                        nameof(GameplayEffectCapacityDimension.SourceCaptureEntries), snapshot.SourceAttributes.Count, capacity.SourceCaptureWidth);
                    return false;
                }
                if (snapshot.TargetAttributes.Count > capacity.TargetCaptureWidth)
                {
                    failure = CapacityFailure(definition.Id, command.InstanceId, command.Context, command.Kind,
                        nameof(GameplayEffectCapacityDimension.TargetCaptureEntries), snapshot.TargetAttributes.Count, capacity.TargetCaptureWidth);
                    return false;
                }
                if (snapshot.SourceTags.Count > capacity.Actor.SourceTagSnapshotEntries ||
                    snapshot.TargetTags.Count > capacity.Actor.SourceTagSnapshotEntries)
                {
                    int width = snapshot.SourceTags.Count > snapshot.TargetTags.Count ? snapshot.SourceTags.Count : snapshot.TargetTags.Count;
                    failure = CapacityFailure(definition.Id, command.InstanceId, command.Context, command.Kind,
                        nameof(GameplayEffectCapacityDimension.SourceTagSnapshotEntries), width, capacity.Actor.SourceTagSnapshotEntries);
                    return false;
                }
                int pages = (snapshot.SourceTags.Count > 0 && !(snapshot.SourceTags is GameplayTagSnapshot) ? 1 : 0) +
                    (snapshot.TargetTags.Count > 0 && !(snapshot.TargetTags is GameplayTagSnapshot) ? 1 : 0);
                if (pages > m_State.AvailableTagSnapshotPages)
                {
                    failure = CapacityFailure(definition.Id, command.InstanceId, command.Context, command.Kind,
                        nameof(GameplayEffectCapacityDimension.CapturePages), pages, m_State.AvailableTagSnapshotPages);
                    return false;
                }
            }
            failure = default;
            return true;
        }

        public GameplayEffectApplyResult CommitMutation()
        {
            if (!(( IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState,
            PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, FPAbilityExecutionSavepoint>)this).TryReserveChanges(0, out GameplayEffectExecutionFailure failure))
                return new GameplayEffectApplyResult(failure);
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
        }

        GameplayEffectExecutionFailure CapacityFailure(CanonicalTextValue effectId, ulong instanceId,
            GameplayEffectContextIdentity context, GameplayEffectLifecycleKind trigger, string dimension,
            int required, int available) => new GameplayEffectExecutionFailure(effectId, instanceId,
                trigger, effectId, context, m_Tick.Value, GameplayEffectApplyResultKind.CapacityExceeded,
                "GameplayEffectCapacityExceeded", capacityDimension: dimension, required: required, available: available);
    }
}

