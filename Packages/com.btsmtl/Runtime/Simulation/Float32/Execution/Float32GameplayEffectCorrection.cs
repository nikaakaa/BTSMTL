using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed partial class Float32GameplayEffectTarget
    {
        bool TryApplyAuthoritativeCorrection(
            GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            out GameplayEffectReconciliationResult result)
        {
            var correction = (SimulationGameplayEffectCorrection)command.Correction;
            if (!correction.IsComplete)
                return CorrectionFailure(command, "AuthoritativePayloadIncomplete", out result);
            m_State.TryGetJournalRecords(command.Scope.PredictionKey, out IReadOnlyList<PortablePredictionRecord> records);
            bool activeCovered = (correction.Coverage & GameplayEffectCorrectionCoverage.ActiveEffect) != 0;
            int attributeCount = (correction.Coverage & GameplayEffectCorrectionCoverage.Attributes) != 0
                ? correction.Attributes.Count : 0;
            int cueCount = (correction.Coverage & GameplayEffectCorrectionCoverage.Cues) != 0
                ? correction.Cues.Count : 0;
            ulong handle = 0;
            PortableEffectDefinition definition = default;
            int cueLimit = 0;
            int predictedCueRemovals = 0;
            int scopedRecords = 0;
            ulong subjectInstanceId = 0;
            for (int i = 0; i < records.Count; i++)
            {
                PortablePredictionRecord record = records[i];
                if (!CorrectionScopeMatches(record, command.Scope, command.Context.SourceActor))
                    continue;
                if (activeCovered && definition != default &&
                    (handle != record.Handle || !ReferenceEquals(definition, record.Spec.Definition)))
                    return CorrectionFailure(command, "AuthoritativeActiveRequiresInstanceScope", out result);
                handle = record.Handle;
                scopedRecords++;
                subjectInstanceId = record.InstanceId;
                definition = record.Spec.Definition;
                cueLimit += record.Spec.Definition.Layout.Cues;
                predictedCueRemovals += record.CueIds.Count;
                for (int j = 0; j < record.CueIds.Count; j++)
                    if (!m_CueServices.TryGetGameplayCueProducer(definition.Id, record.CueIds[j], out _))
                        return CorrectionFailure(command, "AuthoritativeCueProducerMissing", out result,
                            parameter: record.CueIds[j], effectId: definition.Id);
            }
            PortableActiveEffectState active = activeCovered ? m_State.FindActiveByHandle(handle) : null;
            if (cueCount > cueLimit)
                return CorrectionFailure(command, "AuthoritativeCueScopeExceeded", out result,
                    parameter: nameof(SimulationGameplayEffectCorrection.Cues), required: cueCount, available: cueLimit);
            if (!TryAdmitCorrectionResources(command, correction, activeCovered ? definition : null,
                    activeCovered && correction.HasActiveEffect && active == default, predictedCueRemovals,
                    out GameplayEffectExecutionFailure capacityFailure))
            {
                result = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.ExecutionFailed,
                    command.Scope, command.InstanceId, command.LifecycleRevision, 0,
                    reason: capacityFailure.Reason, failure: capacityFailure);
                return false;
            }

            if (activeCovered && correction.HasActiveEffect &&
                !TryValidateCorrectionSpec(command, correction, definition, handle, records, out result))
                return false;
            for (int i = 0; i < attributeCount; i++)
            {
                SimulationAttributeValueIngress attribute = correction.Attributes[i];
                if (!attribute.IsValid || i > 0 &&
                    CanonicalTextValue.CompareOrdinal(correction.Attributes[i - 1].AttributeId, attribute.AttributeId) >= 0)
                    return CorrectionFailure(command, "AuthoritativeAttributeOrderInvalid", out result,
                        attributeId: attribute.AttributeId);
                PortablePredictionRecord owner = FindCorrectionAttributeOwner(records, command, correction, handle, attribute);
                if (owner == default)
                    return CorrectionFailure(command, "AuthoritativeAttributeOutsideScope", out result,
                        GameplayEffectReconciliationResultKind.Conflict, attributeId: attribute.AttributeId);
            }
            int cueStart = m_Changes.Count;
            for (int i = 0; i < cueCount; i++)
            {
                GameplayCueFact cue = correction.Cues[i];
                if (!cue.IsValid || !cue.EffectContext.IsValid || cue.EffectContext.IsPredicted ||
                    i > 0 && CompareCorrectionCues(correction.Cues[i - 1], cue) >= 0)
                    return CorrectionFailure(command, "AuthoritativeCueOrderInvalid", out result, parameter: cue.CueId);
                if (!TryCorrectionCueTrigger(cue.TriggerId, out PortableCueTrigger trigger))
                    return CorrectionFailure(command, "AuthoritativeCueTriggerInvalid", out result, parameter: cue.CueId);
                PortablePredictionRecord owner = FindCorrectionCueOwner(records, command, activeCovered, handle, cue);
                if (owner == default)
                    return CorrectionFailure(command, "AuthoritativeCueOutsideScope", out result,
                        GameplayEffectReconciliationResultKind.Conflict, parameter: cue.CueId, effectId: cue.SourceId);
                if (!CorrectionCueIsDeclared(owner, cue.CueId, trigger) ||
                    !m_CueServices.TryGetGameplayCueProducer(owner.Spec.Definition.Id, cue.CueId, out _))
                    return CorrectionFailure(command, "AuthoritativeCueProducerInvalid", out result,
                        parameter: cue.CueId, effectId: owner.Spec.Definition.Id);
                m_Changes.Add(new PortableEffectRuntimeChange(0, cue.CueId, trigger,
                    owner.Spec.Definition, cue.SourceInstanceId, cue.EffectContext));
            }
            for (int i = 0; i < records.Count; i++)
            {
                PortablePredictionRecord record = records[i];
                if (!CorrectionScopeMatches(record, command.Scope, command.Context.SourceActor))
                    continue;
                for (int j = 0; j < record.CueIds.Count; j++)
                {
                    bool covered = false;
                    for (int c = 0; c < cueCount; c++)
                    {
                        PortableEffectRuntimeChange cue = m_Changes[cueStart + c];
                        if (ReferenceEquals(cue.Definition, record.Spec.Definition) &&
                            cue.InstanceId == CorrectionInstanceId(record, command, activeCovered, handle) &&
                            CorrectionContextsMatch(cue.Context, record.Spec.Context) &&
                            CanonicalTextValue.Equals(cue.CueId, record.CueIds[j], StringComparison.Ordinal))
                        {
                            covered = true;
                            break;
                        }
                    }
                    if (!covered)
                        return CorrectionFailure(command, "AuthoritativeCueSourceCoverageMissing", out result,
                            parameter: record.CueIds[j], effectId: record.Spec.Definition.Id);
                }
            }

            for (int i = 0; i < records.Count; i++)
            {
                PortablePredictionRecord record = records[i];
                if (!CorrectionScopeMatches(record, command.Scope, command.Context.SourceActor))
                    continue;
                for (int j = 0; j < record.CueIds.Count; j++)
                    AddCue(record.CueIds[j], PortableCueTrigger.Removed, record.Spec.Definition,
                        record.InstanceId, record.Spec.Context, false);
            }
            if (activeCovered)
            {
                if (active != default)
                    DeactivatePersistent(active);
                if (!correction.HasActiveEffect)
                {
                    if (active != default)
                        m_State.RemoveActive(active);
                }
                else
                {
                    PortableEffectSpecState spec = RestoreAuthoritativeSpec(correction.Spec, definition);
                    if (active == default)
                    {
                        active = m_Scratch.AcquireActiveState();
                        active.Handle = handle;
                        active.InstanceId = correction.Active.InstanceId;
                        active.InsertionSequence = handle;
                        active.Spec = spec;
                        spec.Retain();
                        RestoreCorrectedActive(active, correction.Active, command.Context);
                        m_State.AddActive(active);
                    }
                    else
                    {
                        m_Scratch.DeferSpecRelease(active.Spec);
                        active.Spec = spec;
                        spec.Retain();
                        if (active.InstanceId != correction.Active.InstanceId)
                            m_State.RemapActiveInstance(active, correction.Active.InstanceId);
                        RestoreCorrectedActive(active, correction.Active, command.Context);
                    }
                    m_Causes[handle] = new PortableEffectCause(definition, active.InstanceId, ToTarget(active.LifecycleContext));
                    m_State.SetNextPeriod(active.InstanceId, correction.Active.NextPeriodTick);
                    if (!active.Inhibited)
                    {
                        for (int i = 0; i < definition.Components.Length; i++)
                            if (definition.Components[i] is PortableModifierComponent modifier &&
                                modifier.Application == PortableModifierApplication.CurrentValue)
                                ActivateCurrentModifier(active, modifier);
                        ActivateGrantedTags(active);
                    }
                    m_State.RefreshActiveEffectsDirty();
                    AddLifecycle(active, SimulationGameplayEffectLifecycleOperation.Corrected);
                }
            }
            for (int i = 0; i < attributeCount; i++)
            {
                SimulationAttributeValueIngress attribute = correction.Attributes[i];
                PortablePredictionRecord owner = CorrectionAttributeOwner(records, command,
                    attribute.AttributeId, attribute.CauseEffectId);
                SimulationGameplayEffectContext context = CanonicalTextValue.IsNullOrEmpty(attribute.CauseEffectId)
                    ? AuthoritativeCorrectionContext(owner.Spec.Context, command.Context.SourceTick)
                    : attribute.CauseContext;
                m_Causes[owner.Handle] = new PortableEffectCause(owner.Spec.Definition,
                    CorrectionInstanceId(owner, command, activeCovered, handle), context);
                AddAttributeChanges(m_State.ApplyCorrectionAttribute(attribute, owner.Handle));
            }
            FinishAuthoritativeCueChanges(cueStart, cueCount);
            result = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.Corrected,
                command.Scope, activeCovered ? correction.HasActiveEffect ? active.InstanceId : subjectInstanceId
                    : command.InstanceId, command.LifecycleRevision, scopedRecords);
            return true;
        }

        bool TryValidateCorrectionSpec(GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            SimulationGameplayEffectCorrection correction, PortableEffectDefinition definition, ulong handle,
            IReadOnlyList<PortablePredictionRecord> records, out GameplayEffectReconciliationResult result)
        {
            SimulationGameplayEffectSpecSnapshot snapshot = correction.Spec;
            if (snapshot.Context.TargetActorId != m_ActorId || snapshot.Context.IsPredicted ||
                !CanonicalTextValue.Equals(snapshot.EffectId, definition.Id, StringComparison.Ordinal) ||
                snapshot.DefinitionRevision != definition.Revision || correction.DefinitionRevision != definition.Revision)
                return CorrectionFailure(command, "AuthoritativeSpecIdentityMismatch", out result);
            bool contextMatched = false;
            for (int i = 0; i < records.Count; i++)
                if (CorrectionScopeMatches(records[i], command.Scope, command.Context.SourceActor) &&
                    records[i].Handle == handle && CorrectionContextsMatch(snapshot.Context, records[i].Spec.Context))
                {
                    contextMatched = true;
                    break;
                }
            if (!contextMatched)
                return CorrectionFailure(command, "AuthoritativeSpecSourceOutsideScope", out result,
                    GameplayEffectReconciliationResultKind.Conflict);
            if (snapshot.SetByCallerValues.Count != definition.SetByCallerParameters.Length ||
                snapshot.SourceAttributes.Count != definition.SourceSnapshotAttributes.Count ||
                snapshot.TargetAttributes.Count != definition.TargetSnapshotAttributes.Count)
                return CorrectionFailure(command, "AuthoritativeSpecCapturesIncomplete", out result);
            for (int i = 0; i < snapshot.SetByCallerValues.Count; i++)
                if (!CanonicalTextValue.Equals(snapshot.SetByCallerValues[i].ParameterId,
                        definition.SetByCallerParameters[i], StringComparison.Ordinal))
                    return CorrectionFailure(command, "AuthoritativeParameterIdentityMismatch", out result,
                        parameter: snapshot.SetByCallerValues[i].ParameterId);
            for (int i = 0; i < snapshot.SourceAttributes.Count; i++)
                if (!CanonicalTextValue.Equals(snapshot.SourceAttributes[i].AttributeId,
                        definition.SourceSnapshotAttributes[i], StringComparison.Ordinal))
                    return CorrectionFailure(command, "AuthoritativeSourceCaptureIdentityMismatch", out result,
                        attributeId: snapshot.SourceAttributes[i].AttributeId);
            for (int i = 0; i < snapshot.TargetAttributes.Count; i++)
                if (!CanonicalTextValue.Equals(snapshot.TargetAttributes[i].AttributeId,
                        definition.TargetSnapshotAttributes[i], StringComparison.Ordinal))
                    return CorrectionFailure(command, "AuthoritativeTargetCaptureIdentityMismatch", out result,
                        attributeId: snapshot.TargetAttributes[i].AttributeId);
            for (int i = 0; i < snapshot.SourceTags.Count; i++)
                if (!m_Catalog.ContainsTag(snapshot.SourceTags[i]) || i > 0 &&
                    (snapshot.SourceTagSnapshotProvided
                        ? CanonicalTextValue.CompareOrdinal(snapshot.SourceTags[i - 1], snapshot.SourceTags[i]) > 0
                        : CanonicalTextValue.CompareOrdinal(snapshot.SourceTags[i - 1], snapshot.SourceTags[i]) >= 0))
                    return CorrectionFailure(command, "AuthoritativeSourceTagsInvalid", out result, parameter: snapshot.SourceTags[i]);
            for (int i = 0; i < snapshot.TargetTags.Count; i++)
                if (!m_Catalog.ContainsTag(snapshot.TargetTags[i]) || i > 0 &&
                    CanonicalTextValue.CompareOrdinal(snapshot.TargetTags[i - 1], snapshot.TargetTags[i]) >= 0)
                    return CorrectionFailure(command, "AuthoritativeTargetTagsInvalid", out result, parameter: snapshot.TargetTags[i]);
            GameplayEffectActiveControlSnapshot active = correction.Active;
            if (!definition.Layout.CreatesActiveEffect || active.InstanceId == 0 || active.StartTick == 0 ||
                active.StackCount < 1 || active.StackCount > definition.MaxStacks ||
                active.LifecycleRevision != command.LifecycleRevision || active.InstanceId != command.InstanceId ||
                (definition.DurationPolicy == PortableEffectDurationPolicy.Duration
                    ? snapshot.DurationTicks == 0 || active.EndTick <= active.StartTick
                    : snapshot.DurationTicks != 0 || active.EndTick != 0) ||
                (definition.HasPeriod
                    ? snapshot.PeriodTicks == 0 || active.NextPeriodTick <= active.StartTick
                    : snapshot.PeriodTicks != 0 || active.NextPeriodTick != 0))
                return CorrectionFailure(command, "AuthoritativeActiveIdentityMismatch", out result);
            PortableActiveEffectState collision = m_State.FindActiveByInstance(active.InstanceId);
            if (collision != default && collision.Handle != handle)
                return CorrectionFailure(command, "AuthoritativeInstanceConflict", out result,
                    GameplayEffectReconciliationResultKind.Conflict);
            result = default;
            return true;
        }

        PortablePredictionRecord FindCorrectionAttributeOwner(IReadOnlyList<PortablePredictionRecord> records,
            GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            SimulationGameplayEffectCorrection correction, ulong activeHandle, SimulationAttributeValueIngress attribute)
        {
            bool hasCause = !CanonicalTextValue.IsNullOrEmpty(attribute.CauseEffectId);
            if (hasCause && (!attribute.CauseContext.IsValid || attribute.CauseContext.IsPredicted) ||
                !hasCause && (attribute.CauseEffectInstanceId != 0 || attribute.CauseContext.IsValid))
                return null;
            PortablePredictionRecord owner = CorrectionAttributeOwner(records, command,
                attribute.AttributeId, attribute.CauseEffectId);
            if (owner != default && hasCause &&
                (attribute.CauseEffectInstanceId != CorrectionInstanceId(owner, command,
                    (correction.Coverage & GameplayEffectCorrectionCoverage.ActiveEffect) != 0, activeHandle) ||
                 !CorrectionContextsMatch(attribute.CauseContext, owner.Spec.Context)))
                return null;
            return owner;
        }

        static PortablePredictionRecord CorrectionAttributeOwner(IReadOnlyList<PortablePredictionRecord> records,
            GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command, CanonicalTextValue attributeId, CanonicalTextValue effectId)
        {
            for (int i = 0; i < records.Count; i++)
            {
                PortablePredictionRecord record = records[i];
                if (CorrectionScopeMatches(record, command.Scope, command.Context.SourceActor) &&
                    PredictionHasAttribute(record, attributeId) &&
                    (CanonicalTextValue.IsNullOrEmpty(effectId) || CanonicalTextValue.Equals(effectId, record.Spec.Definition.Id, StringComparison.Ordinal)))
                    return record;
            }
            return null;
        }

        PortablePredictionRecord FindCorrectionCueOwner(IReadOnlyList<PortablePredictionRecord> records,
            GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            bool activeCovered, ulong activeHandle, GameplayCueFact cue)
        {
            for (int i = 0; i < records.Count; i++)
            {
                PortablePredictionRecord record = records[i];
                if (CorrectionScopeMatches(record, command.Scope, command.Context.SourceActor) &&
                    CanonicalTextValue.Equals(cue.SourceId, record.Spec.Definition.Id, StringComparison.Ordinal) &&
                    cue.SourceInstanceId == CorrectionInstanceId(record, command, activeCovered, activeHandle) &&
                    CorrectionContextsMatch(cue.EffectContext, record.Spec.Context))
                    return record;
            }
            return null;
        }

        ulong CorrectionInstanceId(PortablePredictionRecord record,
            GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            bool activeCovered, ulong activeHandle)
        {
            if (activeCovered && record.Handle == activeHandle && command.InstanceId != 0)
                return command.InstanceId;
            PortableActiveEffectState active = m_State.FindActiveByHandle(record.Handle);
            return active != default ? active.InstanceId : record.InstanceId;
        }

        static bool CorrectionContextsMatch(SimulationGameplayEffectContext left, SimulationGameplayEffectContext right) =>
            left.SourceActorId == right.SourceActorId && left.TargetActorId == right.TargetActorId &&
            left.SourceActionInstanceId == right.SourceActionInstanceId && left.PredictionKey == right.PredictionKey &&
            left.GameplayResultId == right.GameplayResultId;

        static SimulationGameplayEffectContext AuthoritativeCorrectionContext(SimulationGameplayEffectContext source, ulong sourceTick) =>
            new SimulationGameplayEffectContext(source.SourceActorId, source.TargetActorId, source.SourceActionInstanceId,
                source.PredictionKey, source.GameplayResultId, sourceTick, SimulationGameplayEffectApplicationMode.Confirmed);

        static int CompareCorrectionCues(GameplayCueFact left, GameplayCueFact right)
        {
            int order = CanonicalTextValue.CompareOrdinal(left.SourceId, right.SourceId);
            if (order != 0)
                return order;
            order = left.SourceInstanceId.CompareTo(right.SourceInstanceId);
            if (order != 0)
                return order;
            order = CanonicalTextValue.CompareOrdinal(left.CueId, right.CueId);
            if (order != 0)
                return order;
            order = left.EffectContext.SourceActionInstanceId.CompareTo(right.EffectContext.SourceActionInstanceId);
            return order != 0 ? order : left.EffectContext.GameplayResultId.CompareTo(right.EffectContext.GameplayResultId);
        }

        static bool PredictionHasAttribute(PortablePredictionRecord record, CanonicalTextValue attributeId)
        {
            for (int i = 0; i < record.Attributes.Count; i++)
                if (attributeId.Equals(record.Attributes.Keys[i]))
                    return true;
            return false;
        }

        static bool CorrectionCueIsDeclared(PortablePredictionRecord record, CanonicalTextValue cueId, PortableCueTrigger trigger)
        {
            for (int i = 0; i < record.Spec.Definition.Components.Length; i++)
                if (record.Spec.Definition.Components[i] is PortableCueComponent cue &&
                    CanonicalTextValue.Equals(cue.CueId, cueId, StringComparison.Ordinal) &&
                    (cue.Trigger == trigger || trigger == PortableCueTrigger.Removed && record.CueIds.Contains(cue.CueId)))
                    return true;
            return false;
        }

        static bool TryCorrectionCueTrigger(CanonicalTextValue identity, out PortableCueTrigger trigger)
        {
            if (identity.Equals(nameof(PortableCueTrigger.OnActive))) trigger = PortableCueTrigger.OnActive;
            else if (identity.Equals(nameof(PortableCueTrigger.WhileActive))) trigger = PortableCueTrigger.WhileActive;
            else if (identity.Equals(nameof(PortableCueTrigger.Executed))) trigger = PortableCueTrigger.Executed;
            else if (identity.Equals(nameof(PortableCueTrigger.Removed))) trigger = PortableCueTrigger.Removed;
            else if (identity.Equals(nameof(PortableCueTrigger.Expired))) trigger = PortableCueTrigger.Expired;
            else { trigger = default; return false; }
            return true;
        }

        void FinishAuthoritativeCueChanges(int start, int count)
        {
            if (count == 0)
                return;
            int mutationCount = m_Changes.Count - start - count;
            m_Changes.Reverse(start, count);
            m_Changes.Reverse(start + count, mutationCount);
            m_Changes.Reverse(start, count + mutationCount);
            int cueStart = start + mutationCount;
            for (int i = 0; i < count; i++)
            {
                PortableEffectRuntimeChange cue = m_Changes[cueStart + i];
                m_Changes[cueStart + i] = new PortableEffectRuntimeChange(NextChangeCursor(), cue.CueId,
                    cue.Trigger, cue.Definition, cue.InstanceId, cue.Context);
            }
        }

        PortableEffectSpecState RestoreAuthoritativeSpec(SimulationGameplayEffectSpecSnapshot snapshot, PortableEffectDefinition definition)
        {
            PortableEffectSpecState spec = m_Scratch.AcquireSpec(definition, snapshot.Context);
            for (int i = 0; i < snapshot.SetByCallerValues.Count; i++)
                spec.SetByCaller.Add(definition.SetByCallerParameters[i], snapshot.SetByCallerValues[i].Value);
            for (int i = 0; i < snapshot.SourceAttributes.Count; i++)
                spec.SourceAttributes.Add(definition.SourceSnapshotAttributes[i], snapshot.SourceAttributes[i].Value);
            for (int i = 0; i < snapshot.TargetAttributes.Count; i++)
                spec.TargetAttributes.Add(definition.TargetSnapshotAttributes[i], snapshot.TargetAttributes[i].Value);
            spec.SourceTagSnapshotProvided = snapshot.SourceTagSnapshotProvided;
            spec.SetSourceTags(m_State.CaptureTags(snapshot.SourceTags));
            spec.SetTargetTags(m_State.CaptureTags(snapshot.TargetTags));
            spec.DurationTicks = snapshot.DurationTicks;
            spec.PeriodTicks = snapshot.PeriodTicks;
            return spec;
        }

        static void RestoreCorrectedActive(PortableActiveEffectState active, GameplayEffectActiveControlSnapshot snapshot,
            GameplayEffectContextIdentity context)
        {
            active.StartTick = snapshot.StartTick;
            active.EndTick = snapshot.EndTick;
            active.StackCount = snapshot.StackCount;
            active.Inhibited = snapshot.Inhibited;
            active.LifecycleRevision = snapshot.LifecycleRevision;
            active.LifecycleContext = new GameplayEffectContextIdentity(context.SourceActor, context.TargetActor,
                context.SourceActionInstanceId, context.PredictionKey, context.GameplayResultId, context.SourceTick, false);
        }

        static bool CorrectionScopeMatches(PortablePredictionRecord record, GameplayEffectReconciliationScope scope, ActorId sourceActor) =>
            record.CausalContext.SourceActor == sourceActor && !record.Confirmed && (scope.Kind == GameplayEffectReconciliationScopeKind.PredictionKey ||
                CanonicalTextValue.Equals(record.Spec.Definition.Id, scope.EffectId, StringComparison.Ordinal) &&
                (scope.Kind != GameplayEffectReconciliationScopeKind.Instance || record.PredictedInstanceId == scope.PredictedInstanceId));

        bool CorrectionFailure(GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> command,
            CanonicalTextValue reason, out GameplayEffectReconciliationResult result,
            GameplayEffectReconciliationResultKind kind = GameplayEffectReconciliationResultKind.IncompleteCorrection,
            CanonicalTextValue attributeId = default, CanonicalTextValue parameter = default, CanonicalTextValue effectId = default, int required = 0, int available = 0)
        {
            CanonicalTextValue owner = effectId.IsNull ? command.EffectId : effectId;
            var failure = new GameplayEffectExecutionFailure(owner, command.InstanceId, GameplayEffectLifecycleKind.Corrected,
                owner, command.Context, m_Tick.Value, GameplayEffectApplyResultKind.InvalidContext, reason,
                parameter.IsNull ? attributeId : parameter, required: required, available: available);
            result = new GameplayEffectReconciliationResult(kind, command.Scope, command.InstanceId,
                command.LifecycleRevision, 0, attributeId: attributeId, reason: reason, failure: failure);
            return false;
        }
    }
}
