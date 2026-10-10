using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed partial class Float32GameplayEffectTarget
    {
        ActorId IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ActorId => m_ActorId;
        ulong IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.Tick => m_Tick.Value;
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.HasActiveEffects =>
            m_State != null ? m_State.ActiveEffects.Count > 0 : m_CommittedState.ActiveEffectCount > 0;
        int IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ChangeCount => m_Changes.Count;
        ulong IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ChangeCursor => m_State.ChangeCursor;
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.IsDisposed => m_SavepointPort == null;
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RemoveQueryIsValid(PortableTagQuery query) => query != null;
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.BeginRemoveResult() => m_Scratch.RemovedHandleCount = 0;
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AppendRemovedHandle(ulong handle) => m_Scratch.RemovedHandles[m_Scratch.RemovedHandleCount++] = handle;
        ReadOnlyMemory<ulong> IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RemovedHandles => new ReadOnlyMemory<ulong>(
            m_Scratch.RemovedHandles, 0, m_Scratch.RemovedHandleCount);

        GameplayEffectApplicationIdentity IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.DescribeApplication(SimulationGameplayEffectApplication application)
        {
            return new GameplayEffectApplicationIdentity(application.EffectId, application.AuthoritativeInstanceId,
                application.AuthoritativeLifecycleRevision, application.Context.IsValid ? ToCommon(application.Context) : default);
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TryPrepare(
            SimulationGameplayEffectApplication application,
            out GameplayEffectPreparedSpec<PortableEffectSpecState> prepared,
            out GameplayEffectApplyResult failure)
        {
            EnsureWorkingState();
            return m_Admission.TryPrepare(application, out prepared, out failure);
        }

        GameplayEffectPreparedSpec<PortableEffectSpecState> IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.DescribeSpec(PortableEffectSpecState spec) => DescribeSpec(spec);
        int IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ComponentCount(PortableEffectSpecState spec) => spec.Definition.Components.Length;
        GameplayEffectComponentDescriptor IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.DescribeComponent(PortableEffectSpecState spec, int componentIndex) => DescribeComponent(spec.Definition.Components[componentIndex]);
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EvaluateTagRequirement(PortableEffectSpecState spec, int componentIndex) => EvaluateTagRequirement(spec, (PortableTagRequirementsComponent)spec.Definition.Components[componentIndex]);
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EvaluateAttributeRequirement(PortableEffectSpecState spec, int componentIndex) => EvaluateAttributeRequirement(spec, (PortableAttributeRequirementsComponent)spec.Definition.Components[componentIndex]);
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.MatchesEffectId(PortableEffectSpecState spec, CanonicalTextValue effectId) =>
            CanonicalTextValue.Equals(spec.Definition.Id, effectId, StringComparison.Ordinal);
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.MatchesEffectTagQuery(PortableEffectSpecState spec, PortableTagQuery tagQuery) =>
            m_State.Catalog.Matches(tagQuery, spec.Definition.EffectTags);
        PortableActiveEffectState IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.FindActiveByHandle(ulong handle) => m_State.FindActiveByHandle(handle);
        PortableActiveEffectState IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.FindActiveByInstance(ulong instanceId) => m_State.FindActiveByInstance(instanceId);
        IReadOnlyList<PortableActiveEffectState> IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AcquireActiveEffects()
        {
            List<PortableActiveEffectState> values = m_Scratch.ActiveEffects.Acquire();
            for (int i = 0; i < m_State.ActiveEffects.Count; i++)
                values.Add(m_State.ActiveEffects[i]);
            return values;
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ReleaseActiveEffects(IReadOnlyList<PortableActiveEffectState> activeEffects)
        {
            m_Scratch.ActiveEffects.Release((List<PortableActiveEffectState>)activeEffects);
        }

        PortableActiveEffectState IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.CreateActive(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec,
            ulong handle,
            ulong instanceId,
            ulong startTick,
            ulong endTick,
            ulong insertionSequence,
            ulong lifecycleRevision)
        {
            PortableActiveEffectState active = m_Scratch.AcquireActiveState();
            active.Handle = handle;
            active.InstanceId = instanceId;
            active.Spec = spec.TargetSpec;
            active.Spec.Retain();
            active.StartTick = startTick;
            active.EndTick = endTick;
            active.InsertionSequence = insertionSequence;
            active.StackCount = 1;
            active.Inhibited = false;
            active.LifecycleRevision = lifecycleRevision;
            active.LifecycleContext = spec.Context;
            return active;
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AddActive(PortableActiveEffectState active) => m_State.AddActive(active);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RemoveActive(PortableActiveEffectState active) => m_State.RemoveActive(active);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RemapActiveInstance(PortableActiveEffectState active, ulong instanceId) => m_State.RemapActiveInstance(active, instanceId);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RefreshActiveEffectsDirty() => m_State.RefreshActiveEffectsDirty();
        ulong IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.GetNextPeriod(ulong instanceId) => m_State.GetNextPeriod(instanceId);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.SetNextPeriod(ulong instanceId, ulong tick) => m_State.SetNextPeriod(instanceId, tick);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.DeactivatePersistent(PortableActiveEffectState active) => DeactivatePersistent(active);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ActivateCurrentModifier(PortableActiveEffectState active, int componentIndex) =>
            ActivateCurrentModifier(active, (PortableModifierComponent)active.Spec.Definition.Components[componentIndex]);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ActivateGrantedTags(PortableActiveEffectState active) => ActivateGrantedTags(active);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ExecuteNumericComponent(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec,
            PortableActiveEffectState active,
            ulong handle,
            int stackCount,
            int componentIndex) => ExecuteNumericComponent(spec.TargetSpec, handle, stackCount, spec.TargetSpec.Definition.Components[componentIndex]);

        int IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AdditionalEffectCount(PortableEffectSpecState spec, int componentIndex) =>
            ((PortableAdditionalEffectsComponent)spec.Definition.Components[componentIndex]).Effects.Length;

        GameplayEffectAdditionalTrigger IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.DescribeAdditionalEffectTrigger(
            PortableEffectSpecState spec,
            int componentIndex,
            int effectIndex) => ToCommon(((PortableAdditionalEffectsComponent)spec.Definition.Components[componentIndex]).Effects[effectIndex].Trigger);
        string IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AdditionalEffectId(PortableEffectSpecState spec, int componentIndex, int effectIndex) =>
            ((PortableAdditionalEffectsComponent)spec.Definition.Components[componentIndex]).Effects[effectIndex].EffectId;

        SimulationGameplayEffectApplication IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.BuildAdditionalApplication(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec,
            ulong instanceId,
            int componentIndex,
            int effectIndex) => BuildAdditionalApplication(
                spec.TargetSpec,
                componentIndex,
                effectIndex,
                ToTarget(spec.Context));

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ReleaseAdditionalApplication(SimulationGameplayEffectApplication application) =>
            ReleaseAdditionalApplication(application);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EmitCue(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec,
            ulong instanceId,
            int componentIndex,
            bool trackPrediction)
        {
            var cue = (PortableCueComponent)spec.TargetSpec.Definition.Components[componentIndex];
            AddCue(cue.CueId, cue.Trigger, spec.TargetSpec.Definition, instanceId, ToTarget(spec.Context), trackPrediction);
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RegisterCause(
            ulong handle,
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec,
            ulong instanceId) => m_Causes[handle] = new PortableEffectCause(spec.TargetSpec.Definition, instanceId, ToTarget(spec.Context));

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EmitLifecycle(PortableActiveEffectState active, GameplayEffectLifecycleKind lifecycle) => AddLifecycle(active, ToTarget(lifecycle));

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EmitLifecycle(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec,
            ulong instanceId,
            GameplayEffectLifecycleKind lifecycle,
            ulong startTick,
            ulong endTick,
            int stackCount,
            ulong revision,
            bool instant) => AddLifecycle(spec.TargetSpec.Definition, instanceId, ToTarget(lifecycle), ToTarget(spec.Context), startTick, endTick, stackCount, revision, instant);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EmitFailure(
            GameplayEffectExecutionFailure failure) => AddFailure(failure);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EmitReconciliation(GameplayEffectReconciliationResult result, GameplayEffectContextIdentity context) =>
            AddReconciliation(result, context);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TrimChanges(int count) => TrimChanges(count);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RebuildCauses() => RebuildCauses();

        Float32AbilityExecutionSavepoint IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.CreateSavepoint()
        {
            EnsureWorkingState();
            return m_SavepointPort.CreateSavepoint();
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.Restore(Float32AbilityExecutionSavepoint savepoint) => m_SavepointPort.Restore(savepoint);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.Release(Float32AbilityExecutionSavepoint savepoint) => m_SavepointPort.Release(savepoint);
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.SavepointIsActive(Float32AbilityExecutionSavepoint savepoint) => m_SavepointPort.SavepointDepth >= savepoint.Depth;
        ulong IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AllocateHandle() => m_AllocateHandle();

        PortablePredictionRecord IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.CreatePrediction(
            GameplayEffectPreparedSpec<PortableEffectSpecState> spec,
            ulong handle,
            ulong instanceId,
            bool createdActive,
            bool hasActiveBefore,
            GameplayEffectActiveControlSnapshot activeBefore,
            GameplayEffectLifecycleKind mutationKind,
            ulong activeBeforeInsertionSequence)
        {
            PortablePredictionRecord prediction = m_Scratch.AcquirePredictionRecord();
            prediction.Spec = spec.TargetSpec;
            prediction.CausalContext = spec.Context;
            prediction.Handle = handle;
            prediction.InstanceId = instanceId;
            prediction.PredictedInstanceId = instanceId;
            prediction.CreatedActive = createdActive;
            prediction.HasActiveBefore = hasActiveBefore;
            prediction.ActiveBefore = activeBefore;
            prediction.MutationKind = mutationKind;
            prediction.ActiveBeforeInsertionSequence = activeBeforeInsertionSequence;
            prediction.Confirmed = false;
            prediction.AfterLifecycleRevision = 0;
            prediction.AfterChangeCursor = 0;
            prediction.AuthoritativeLifecycleRevision = 0;
            return prediction;
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.SetCurrentPrediction(PortablePredictionRecord prediction) => m_CurrentPrediction = prediction;

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.CompletePrediction(PortablePredictionRecord prediction)
        {
            if (prediction.Spec.Definition.DurationPolicy != PortableEffectDurationPolicy.Instant)
                prediction.AfterLifecycleRevision = m_State.FindActiveByHandle(prediction.Handle).LifecycleRevision;
            prediction.AfterChangeCursor = m_State.ChangeCursor;
            for (int i = 0; i < prediction.Attributes.Count; i++)
            {
                string attribute = prediction.Attributes.Keys[i];
                PortableAttributeState value = m_State.RequireAttribute(attribute);
                prediction.Attributes[attribute] = prediction.Attributes.Values[i].WithAfterRevision(value.Revision);
            }
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.CancelPrediction(PortablePredictionRecord prediction)
        {
            m_Scratch.CancelPredictionRecord(prediction);
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TryGetPredictions(
            ulong predictionKey,
            out IReadOnlyList<PortablePredictionRecord> predictions)
        {
            return m_State.TryGetJournalRecords(predictionKey, out predictions);
        }

        IReadOnlyList<ulong> IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AcquirePredictionKeys()
        {
            List<ulong> values = m_Scratch.PredictionKeys.Acquire();
            m_State.CopyJournalKeys(values);
            return values;
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TryResolveLifecyclePrediction(ulong handle, out PortablePredictionRecord prediction) =>
            m_State.TryResolveLifecyclePrediction(handle, out prediction);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ReleasePredictionKeys(IReadOnlyList<ulong> keys)
        {
            m_Scratch.PredictionKeys.Release((List<ulong>)keys);
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.AddPrediction(PortablePredictionRecord prediction)
        {
            m_State.AddJournalRecord(prediction);
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RemovePrediction(ulong predictionKey, PortablePredictionRecord prediction) => m_State.RemoveJournalRecord(predictionKey, prediction);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.ConfirmPrediction(PortablePredictionRecord prediction, ulong instanceId, ulong lifecycleRevision,
            GameplayEffectContextIdentity lifecycleContext) =>
            m_State.ConfirmJournalRecord(prediction, instanceId, lifecycleRevision, lifecycleContext);

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.CanRestorePredictionAttributes(PortablePredictionRecord prediction, out GameplayEffectReconciliationResult conflict)
        {
            for (int i = 0; i < prediction.Attributes.Count; i++)
            {
                PortablePredictionAttributeSnapshot attribute = prediction.Attributes.Values[i];
                PortableAttributeState current = m_State.RequireAttribute(attribute.AttributeId);
                if (current.Revision != attribute.AfterRevision)
                {
                    conflict = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.Conflict,
                        default, 0, 0, 0, prediction.InstanceId, attribute.AfterRevision, current.Revision,
                        attribute.AttributeId, "PredictionAttributeRevisionConflict");
                    return false;
                }
            }
            conflict = default;
            return true;
        }

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RestorePredictionAttributes(PortablePredictionRecord prediction)
        {
            for (int i = 0; i < prediction.Attributes.Count; i++)
            {
                m_State.RestorePredictedAttributeOwned(prediction.Attributes.Values[i], prediction.Handle,
                    out IReadOnlyList<PortableAttributeChange> changes);
                AddAttributeChanges(changes);
            }
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.CorrectionCoversPrediction(PortablePredictionRecord prediction,
            IGameplayEffectAuthoritativeCorrection correction, out GameplayEffectReconciliationResult missing)
        {
            if ((prediction.CreatedActive || prediction.HasActiveBefore) &&
                (correction.Coverage & GameplayEffectCorrectionCoverage.ActiveEffect) == 0)
            {
                missing = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.IncompleteCorrection,
                    default, 0, 0, 0, prediction.InstanceId, reason: "AuthoritativeActiveCoverageMissing");
                return false;
            }
            for (int i = 0; i < prediction.Attributes.Count; i++)
            {
                string attributeId = prediction.Attributes.Keys[i];
                if ((correction.Coverage & GameplayEffectCorrectionCoverage.Attributes) != 0 &&
                    correction.CoversAttribute(attributeId))
                    continue;
                missing = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.IncompleteCorrection,
                    default, 0, 0, 0, prediction.InstanceId, attributeId: attributeId,
                    reason: "AuthoritativeAttributeCoverageMissing");
                return false;
            }
            for (int i = 0; i < prediction.CueIds.Count; i++)
            {
                string cueId = prediction.CueIds[i];
                if ((correction.Coverage & GameplayEffectCorrectionCoverage.Cues) != 0 && correction.CoversCue(cueId))
                    continue;
                GameplayEffectExecutionFailure failure = new GameplayEffectExecutionFailure(
                    prediction.Spec.Definition.Id, prediction.InstanceId, GameplayEffectLifecycleKind.Corrected,
                    prediction.Spec.Definition.Id, prediction.CausalContext, m_Tick.Value,
                    GameplayEffectApplyResultKind.InvalidContext, "AuthoritativeCueCoverageMissing", cueId);
                missing = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.IncompleteCorrection,
                    default, 0, 0, 0, prediction.InstanceId, reason: failure.Reason, failure: failure);
                return false;
            }
            missing = default;
            return true;
        }

        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TryApplyCorrection(GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            out GameplayEffectReconciliationResult result) => TryApplyAuthoritativeCorrection(command, out result);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.EmitPredictionCueRemoval(PortablePredictionRecord prediction, string cueId) =>
            AddCue(cueId, PortableCueTrigger.Removed, prediction.Spec.Definition, prediction.InstanceId, ToTarget(prediction.CausalContext), false);

        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TrimLifecycleRevisions()
        {
            EnsureWorkingState();
            m_State.TrimLifecycleRevisions(m_Tick.Value);
        }
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TryGetLifecycleRevision(in GameplayEffectLifecycleRevisionKey key, out GameplayEffectLifecycleRevisionEntry entry) => m_State.TryGetLifecycleRevision(key, out entry);
        int IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.LifecycleRevisionCount => m_State.LifecycleRevisionCount;
        int IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.LifecycleRevisionCapacity => m_State.LifecycleRevisionCapacity;
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TryGetAuthoritativeLifecycleAfter(ulong instanceId, ulong afterCursor, out GameplayEffectLifecycleRevisionEntry entry) =>
            m_State.TryGetAuthoritativeLifecycleAfter(instanceId, afterCursor, out entry);
        bool IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.HasLifecycleSubjectReferences(ulong instanceId, ulong acceptedChangeCursor) =>
            m_State.HasLifecycleSubjectReferences(instanceId, acceptedChangeCursor);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.TrimLifecycleSubjectRevisions() => m_State.TrimLifecycleSubjectRevisions();
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.SetLifecycleRevision(in GameplayEffectLifecycleRevisionKey key, in GameplayEffectLifecycleRevisionEntry entry) => m_State.SetLifecycleRevision(key, entry);
        void IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState, PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, Float32AbilityExecutionSavepoint>.RefreshJournalDirty() => m_State.RefreshJournalDirty();

    }
}
