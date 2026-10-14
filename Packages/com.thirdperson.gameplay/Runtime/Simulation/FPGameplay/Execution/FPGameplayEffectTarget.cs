using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using PortableMagnitude = ThirdPersonSimulation.GameplayEffectMagnitude<Photon.Deterministic.FP>;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed partial class FPGameplayEffectTarget : IGameplayEffectControlPort<
        SimulationGameplayEffectApplication,
        PortableEffectSpecState,
        PortableActiveEffectState,
        PortablePredictionRecord,
        PortableTagQuery,
        FPAbilityExecutionSavepoint>,
        IGameplayEffectApplicationAdmissionPort<
            SimulationGameplayEffectApplication,
            PortableEffectSpecState,
            FP>
    {
        IFPAbilityExecutionSavepointPort m_SavepointPort;
        IFPGameplayEffectStatePort m_EffectState;
        FPGameplayEffectRuntimeCatalog m_Catalog;
        GameplayEffectStateAggregate m_CommittedState;
        ActorId m_ActorId;
        SimulationTick m_Tick;
        FPAbilityExecutionWorkspace[] m_OutputWorkspaces;
        FPGameplayAbilityExecutionServices m_CueServices;
        int m_TickRate;
        Func<ulong> m_AllocateHandle;
        PortableEffectRuntimeChange[] Changes => m_Scratch.Changes;
        int m_ChangeCount;
        readonly Dictionary<ulong, PortableEffectCause> m_Causes;
        readonly FPGameplayEffectExecutionScratch m_Scratch;
        readonly GameplayEffectControlRuntime<
            SimulationGameplayEffectApplication,
            PortableEffectSpecState,
            PortableActiveEffectState,
            PortablePredictionRecord,
            PortableTagQuery,
            FPAbilityExecutionSavepoint> m_Control;
        readonly GameplayEffectApplicationAdmissionRuntime<
            SimulationGameplayEffectApplication,
            PortableEffectSpecState,
            FP> m_Admission;
        SimulationGameplayEffectState m_State;
        PortablePredictionRecord m_CurrentPrediction;

        internal FPGameplayEffectTarget(FPGameplayEffectExecutionScratch scratch)
        {
            if (scratch == null)
                throw new ArgumentNullException(nameof(scratch));
            m_Scratch = scratch;
            m_Causes = scratch.Causes;
            m_Control = new GameplayEffectControlRuntime<
                SimulationGameplayEffectApplication,
                PortableEffectSpecState,
                PortableActiveEffectState,
                PortablePredictionRecord,
                PortableTagQuery,
                FPAbilityExecutionSavepoint>(this);
            m_Admission = new GameplayEffectApplicationAdmissionRuntime<
                SimulationGameplayEffectApplication,
                PortableEffectSpecState,
                FP>(this, m_Scratch.SuppliedSourceAttributes);
        }

        public GameplayEffectReconciliationResult ReconcileAction(ulong actionInstanceId, ulong predictionKey,
            AbilityLifecycleTransition transition, IReadOnlyList<SimulationGameplayEffectLifecycleIngress> commands)
        {
            EnsureWorkingState();
            var scope = new GameplayEffectReconciliationScope(GameplayEffectReconciliationScopeKind.PredictionKey,
                predictionKey, string.Empty, 0);
            var context = new GameplayEffectContextIdentity(m_ActorId, m_ActorId, actionInstanceId, predictionKey, 0, m_Tick.Value, false);
            if (commands.Count == 0)
            {
                if (m_State.HasUnresolvedAction(actionInstanceId, predictionKey, m_ActorId))
                {
                    var missing = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.IncompleteCorrection,
                        scope, 0, 0, 0, reason: "ActionEffectCommandsMissing");
                    AddReconciliation(missing, context);
                    return missing;
                }
                return new GameplayEffectReconciliationResult(transition switch
                {
                    AbilityLifecycleTransition.Confirm => GameplayEffectReconciliationResultKind.Confirmed,
                    AbilityLifecycleTransition.Reject => GameplayEffectReconciliationResultKind.Rejected,
                    AbilityLifecycleTransition.Correct => GameplayEffectReconciliationResultKind.Corrected,
                    _ => throw new ArgumentOutOfRangeException(nameof(transition))
                }, scope, 0, 0, 0);
            }
            FPAbilityExecutionSavepoint savepoint = m_SavepointPort.CreateSavepoint();
            int changeCount = m_ChangeCount;
            GameplayEffectReconciliationResult result = default;
            try
            {
                SimulationGameplayEffectLifecycleOperation operation = transition switch
                {
                    AbilityLifecycleTransition.Confirm => SimulationGameplayEffectLifecycleOperation.Confirmed,
                    AbilityLifecycleTransition.Reject => SimulationGameplayEffectLifecycleOperation.Rejected,
                    AbilityLifecycleTransition.Correct => SimulationGameplayEffectLifecycleOperation.Corrected,
                    _ => throw new ArgumentOutOfRangeException(nameof(transition))
                };
                for (int i = 0; i < commands.Count; i++)
                {
                    SimulationGameplayEffectLifecycleIngress command = commands[i];
                    if (command.Context.SourceActorId != m_ActorId || command.Context.TargetActorId != m_ActorId ||
                        command.Scope.PredictionKey != predictionKey || command.Operation != operation)
                    {
                        result = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.InvalidRequest,
                            scope, command.InstanceId, command.LifecycleRevision, 0, reason: "ActionEffectCommandIdentityMismatch");

                    }
                    else
                        result = ApplyLifecycle(command);
                    if (!result.Succeeded)
                    {
                        m_SavepointPort.Restore(savepoint);
                        TrimChanges(changeCount);
                        RebuildCauses();
                        m_Control.RecordRestoredLifecycleResult(ToCommand(command), result);
                        if (result.Failure.IsValid)
                            AddFailure(result.Failure);
                        AddReconciliation(result, context);
                        return result;
                    }
                }
                if (m_State.HasUnresolvedAction(actionInstanceId, predictionKey, m_ActorId))
                {
                    m_SavepointPort.Restore(savepoint);
                    TrimChanges(changeCount);
                    RebuildCauses();
                    result = new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.IncompleteCorrection,
                        scope, 0, 0, 0, reason: "ActionEffectCommandsIncomplete");
                    AddReconciliation(result, context);
                    return result;
                }
                m_SavepointPort.Release(savepoint);
                return result;
            }
            catch
            {
                if (m_SavepointPort.SavepointDepth == savepoint.Depth)
                {
                    m_SavepointPort.Restore(savepoint);
                    TrimChanges(changeCount);
                    RebuildCauses();
                }
                throw;
            }
        }



        internal void Prepare(GameplayEffectRuntimeCapacityLayout capacity) =>
            m_Control.Prepare(capacity);

        internal void BindOutputWorkspaces(FPAbilityExecutionWorkspace[] workspaces) =>
            m_OutputWorkspaces = workspaces;

        void CountOutputs(out int successfulChanges, out int cues)
        {
            successfulChanges = 0;
            cues = 0;
            for (int workspace = 0; workspace < m_OutputWorkspaces.Length; workspace++)
            {
                successfulChanges += m_OutputWorkspaces[workspace].GameplayEffectSuccessfulChangeCount;
                cues += m_OutputWorkspaces[workspace].GameplayEffectCueCount;
            }
            for (int index = 0; index < m_ChangeCount; index++)
                CountOutput(in Changes[index], ref successfulChanges, ref cues);
        }

        static void CountOutput(in PortableEffectRuntimeChange change, ref int successfulChanges, ref int cues)
        {
            if (change.IsSuccessfulOutput)
                successfulChanges++;
            if (change.Kind == PortableEffectChangeKind.Cue)
                cues++;
        }

        internal FPGameplayEffectTarget Begin(
            IFPAbilityExecutionSavepointPort savepointPort,
            IFPGameplayEffectStatePort effectState,
            FPGameplayEffectRuntimeCatalog catalog,
            ActorId actorId,
            SimulationTick tick,
            Func<ulong> allocateHandle,
            FPGameplayAbilityExecutionServices cueServices)
        {
            m_CueServices = cueServices;
            m_SavepointPort = savepointPort ?? throw new ArgumentNullException(nameof(savepointPort));
            m_EffectState = effectState ?? throw new ArgumentNullException(nameof(effectState));
            m_Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            m_CommittedState = effectState.GetGameplayEffectAggregate();
            m_CommittedState.RetainSnapshots();
            m_ActorId = actorId;
            m_Tick = tick;
            m_TickRate = effectState.TickRate;
            m_AllocateHandle = allocateHandle ?? throw new ArgumentNullException(nameof(allocateHandle));
            m_State = null;
            m_CurrentPrediction = null;
            m_CommittedState.CollectActiveEffectIdentities(m_Scratch.ActiveIdentities);
            foreach (GameplayEffectActiveIdentity active in m_Scratch.ActiveIdentities)
                m_Causes[active.Handle] = new PortableEffectCause(active.Definition, active.InstanceId, active.Context);
            return this;
        }

        internal void End()
        {
            m_Control.Reset();
            m_State = null;
            m_CurrentPrediction = null;
            m_CommittedState.ReleaseSnapshots();
            m_CommittedState = default;
            m_SavepointPort = null;
            m_CueServices = null;
            m_EffectState = null;
            m_Catalog = null;
            m_AllocateHandle = null;
            ClearChanges();
            m_Causes.Clear();
            m_Scratch.ActiveIdentities.Clear();
        }

        public IReadOnlyList<string> OwnedTags => m_State != null ? m_State.OwnedTagsSnapshot : m_CommittedState.CopyOwnedTags();

        public bool HasTag(string tagId, GameplayEffectScope scope)
        {
            if (m_State != null)
                return m_State.HasTag(tagId, scope);
            return m_CommittedState.HasTag(tagId, scope);
        }

        public bool Matches(PortableTagQuery query, GameplayEffectScope scope)
        {
            if (scope.Kind == GameplayEffectScopeKind.Actor)
                return Matches(query);
            List<string> tags = m_Scratch.ScopeTags;
            tags.Clear();
            if (m_State != null)
                m_State.CopyScopeTagsTo(scope, tags);
            else
                foreach (KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>> source in m_CommittedState.TagSources)
                {
                    if (!source.Key.Scope.Equals(scope))
                        continue;
                    for (int i = 0; i < source.Value.Count; i++)
                        tags.Add(source.Value[i]);
                }
            return m_Catalog.Matches(query, tags);
        }

        public bool TryFindAbilityBlock(IReadOnlyList<string> candidateTags, GameplayEffectScope scope, out ulong effectHandle)
        {
            if (m_State != null)
            {
                for (int i = 0; i < m_State.ActiveEffects.Count; i++)
                {
                    PortableActiveEffectState active = m_State.ActiveEffects[i];
                    if (!active.Inhibited && active.Spec.Context.Scope.Equals(scope) &&
                        BlocksAbility(active.Spec.Definition, candidateTags))
                    {
                        effectHandle = active.Handle;
                        return true;
                    }
                }
            }
            else
            {
                foreach (PortableActiveEffectSnapshot active in m_CommittedState.ActiveEffects)
                {
                    if (!active.Inhibited && active.Spec.Context.Scope.Equals(scope) &&
                        BlocksAbility(active.Spec.Definition, candidateTags))
                    {
                        effectHandle = active.Handle;
                        return true;
                    }
                }
            }
            effectHandle = 0;
            return false;
        }

        bool BlocksAbility(PortableEffectDefinition definition, IReadOnlyList<string> candidateTags)
        {
            for (int i = 0; i < definition.Components.Length; i++)
                if (definition.Components[i] is PortableBlockAbilitiesComponent blocked &&
                    m_Catalog.Matches(blocked.Query, candidateTags))
                    return true;
            return false;
        }

        public bool TryFindReplacementSourceRejection(IReadOnlyList<string> candidateTags,
            IReadOnlyList<string> selectedSourceTags, bool hasSource, out ulong effectHandle)
        {
            if (m_State != null)
            {
                for (int i = 0; i < m_State.ActiveEffects.Count; i++)
                {
                    PortableActiveEffectState active = m_State.ActiveEffects[i];
                    if (!active.Inhibited && active.Spec.Context.Scope.Kind == GameplayEffectScopeKind.Actor &&
                        RejectsReplacementSource(active.Spec.Definition, candidateTags, selectedSourceTags, hasSource))
                    {
                        effectHandle = active.Handle;
                        return true;
                    }
                }
            }
            else
            {
                foreach (PortableActiveEffectSnapshot active in m_CommittedState.ActiveEffects)
                {
                    if (!active.Inhibited && active.Spec.Context.Scope.Kind == GameplayEffectScopeKind.Actor &&
                        RejectsReplacementSource(active.Spec.Definition, candidateTags, selectedSourceTags, hasSource))
                    {
                        effectHandle = active.Handle;
                        return true;
                    }
                }
            }
            effectHandle = 0;
            return false;
        }

        bool RejectsReplacementSource(PortableEffectDefinition definition, IReadOnlyList<string> candidateTags,
            IReadOnlyList<string> selectedSourceTags, bool hasSource)
        {
            for (int i = 0; i < definition.Components.Length; i++)
                if (definition.Components[i] is PortableReplacementSourceRequirementComponent requirement &&
                    m_Catalog.Matches(requirement.CandidateQuery, candidateTags) &&
                    (!hasSource || !m_Catalog.Matches(requirement.SelectedSourceQuery, selectedSourceTags)))
                    return true;
            return false;
        }

        public bool TryFindOpportunity(GameplayEffectScope scope, PortableTagQuery query,
            out GameplayEffectOpportunityReference opportunity, out SimulationActionTargetSnapshot target)
        {
            if (m_State != null)
            {
                for (int i = 0; i < m_State.ActiveEffects.Count; i++)
                {
                    PortableActiveEffectState effect = m_State.ActiveEffects[i];
                    if (!effect.Inhibited && MatchesOpportunity(effect.Spec.Context, effect.Spec.Definition, scope, query))
                    {
                        opportunity = new GameplayEffectOpportunityReference(effect.Handle, effect.Spec.Context.Success);
                        target = effect.Spec.Context.SuccessTarget;
                        return true;
                    }
                }
            }
            else
                foreach (PortableActiveEffectSnapshot effect in m_CommittedState.ActiveEffects)
                    if (!effect.Inhibited && MatchesOpportunity(effect.Spec.Context, effect.Spec.Definition, scope, query))
                    {
                        opportunity = new GameplayEffectOpportunityReference(effect.Handle, effect.Spec.Context.Success);
                        target = effect.Spec.Context.SuccessTarget;
                        return true;
                    }
            opportunity = default;
            target = default;
            return false;
        }

        public bool TryFindEffect(CanonicalTextValue effectId, GameplayEffectScope scope,
            out ulong handle, out SimulationGameplayEffectContext context, GameplayEffectHolderIdentity? holder = null)
        {
            if (m_State != null)
            {
                for (int i = 0; i < m_State.ActiveEffects.Count; i++)
                {
                    PortableActiveEffectState effect = m_State.ActiveEffects[i];
                    if (!effect.Inhibited && effect.Spec.Context.Scope.Equals(scope) &&
                        (!holder.HasValue || effect.Spec.Context.Holder.Equals(holder.Value)) &&
                        CanonicalTextValue.Equals(effectId, effect.Spec.Definition.Id, StringComparison.Ordinal))
                    {
                        handle = effect.Handle;
                        context = effect.Spec.Context;
                        return true;
                    }
                }
            }
            else
                foreach (PortableActiveEffectSnapshot effect in m_CommittedState.ActiveEffects)
                    if (!effect.Inhibited && effect.Spec.Context.Scope.Equals(scope) &&
                        (!holder.HasValue || effect.Spec.Context.Holder.Equals(holder.Value)) &&
                        CanonicalTextValue.Equals(effectId, effect.Spec.Definition.Id, StringComparison.Ordinal))
                    {
                        handle = effect.Handle;
                        context = effect.Spec.Context;
                        return true;
                    }
            handle = 0;
            context = default;
            return false;
        }

        bool MatchesOpportunity(in SimulationGameplayEffectContext context, PortableEffectDefinition definition,
            GameplayEffectScope scope, PortableTagQuery query)
        {
            if (!context.Success.IsValid || !context.Scope.Equals(scope))
                return false;
            List<string> tags = m_Scratch.ScopeTags;
            tags.Clear();
            for (int i = 0; i < definition.GrantedTags.Length; i++)
                tags.Add(definition.GrantedTags[i]);
            m_Catalog.TryResolveTagIdentity(context.Success.EventTag, out string eventTag);
            tags.Add(eventTag);
            return m_Catalog.Matches(query, tags);
        }

        public bool HasTag(string tagId)
        {
            if (m_State != null)
                return m_State.HasTag(tagId);
            if (!m_Catalog.TryResolveTagIdentity(tagId, out string query))
                return false;
            IReadOnlyList<string> owned = m_CommittedState.CopyOwnedTags();
            for (int i = 0; i < owned.Count; i++)
            {
                if (m_Catalog.IsTagOrParent(owned[i], query))
                    return true;
            }
            return false;
        }

        public bool Matches(PortableTagQuery query)
        {
            return m_State != null
                ? m_State.Matches(query)
                : m_Catalog.Matches(query, m_CommittedState.CopyOwnedTags());
        }

        public bool TryGetAttribute(CanonicalTextValue attributeId, out FP baseValue, out FP currentValue, out ulong revision)
        {
            if (m_Catalog.TryResolveAttributeIndex(attributeId, out int index))
            {
                if (m_State != null)
                {
                    PortableAttributeState value = m_State.AttributeAt(index);
                    baseValue = value.BaseValue;
                    currentValue = value.CurrentValue;
                    revision = value.Revision;
                }
                else
                    m_CommittedState.ReadAttributeAt(index, out baseValue, out currentValue, out revision);
                return true;
            }
            baseValue = FP._0;
            currentValue = FP._0;
            revision = 0;
            return false;
        }

        public GameplayEffectApplyResult Apply(SimulationGameplayEffectApplication application)
        {
            EnsureWorkingState();
            return m_Control.Apply(application);
        }

        public GameplayEffectRemoveResult Remove(GameplayEffectRemoveRequest<PortableTagQuery> request)
        {
            EnsureWorkingState();
            return m_Control.Remove(request);
        }

        internal bool HasHeldEffects(GameplayEffectHolderIdentity holder, ulong actionInstanceId = 0)
        {
            if (m_State != null)
            {
                for (int i = 0; i < m_State.ActiveEffects.Count; i++)
                    if (MatchesHolder(m_State.ActiveEffects[i].Spec.Context.Holder, holder, actionInstanceId))
                        return true;
            }
            else
                foreach (PortableActiveEffectSnapshot effect in m_CommittedState.ActiveEffects)
                    if (MatchesHolder(effect.Spec.Context.Holder, holder, actionInstanceId))
                        return true;
            return false;
        }

        static bool MatchesHolder(GameplayEffectHolderIdentity actual, GameplayEffectHolderIdentity holder, ulong actionInstanceId) =>
            actionInstanceId == 0 ? actual.Equals(holder) :
                actual.Lifetime != GameplayEffectLifetimeKind.Independent && actual.ActionInstanceId == actionInstanceId;

        [ThirdPersonPerformance.Instrumentation.PerformanceProbe("simulation.gameplay-effect.advance")]
        public GameplayEffectExecutionFailure Advance() => m_Control.Advance();

        public GameplayEffectApplyResult ApplyGameplayResult(SimulationGameplayResultIngress ingress) =>
            Apply(ingress.Application);

        public GameplayEffectReconciliationResult ApplyLifecycle(SimulationGameplayEffectLifecycleIngress ingress)
        {
            EnsureWorkingState();
            return m_Control.ApplyLifecycle(ToCommand(ingress));
        }

        public GameplayEffectApplyResult ApplyAttribute(SimulationAttributeValueIngress ingress)
        {
            EnsureWorkingState();
            GameplayEffectContextIdentity context = ingress.CauseContext.IsValid ? ToCommon(ingress.CauseContext) : default;
            GameplayEffectExecutionFailure failure;
            if (!m_Catalog.TryResolveAttributeIndex(ingress.AttributeId, out int attributeIndex))
            {
                failure = new GameplayEffectExecutionFailure(ingress.CauseEffectId, ingress.CauseEffectInstanceId,
                    GameplayEffectLifecycleKind.Applied, ingress.CauseEffectId, context, m_Tick.Value,
                    GameplayEffectApplyResultKind.InvalidContext, "AttributeUnregistered", ingress.AttributeId);
                AddFailure(failure);
                return new GameplayEffectApplyResult(failure);
            }
            PortableEffectDefinition definition = null;
            if (!CanonicalTextValue.IsNullOrEmpty(ingress.CauseEffectId) &&
                !m_Catalog.TryResolveEffect(ingress.CauseEffectId, out definition))
            {
                failure = new GameplayEffectExecutionFailure(ingress.CauseEffectId, ingress.CauseEffectInstanceId,
                    GameplayEffectLifecycleKind.Applied, ingress.CauseEffectId, context, m_Tick.Value,
                    GameplayEffectApplyResultKind.MissingDefinition, "EffectDefinitionMissing");
                AddFailure(failure);
                return new GameplayEffectApplyResult(failure);
            }
            FPAbilityExecutionSavepoint savepoint = m_SavepointPort.CreateSavepoint();
            int changeCount = m_ChangeCount;
            ulong causeHandle = 0;
            if (ingress.CauseEffectInstanceId != 0)
            {
                PortableActiveEffectState active = m_State.FindActiveByInstance(ingress.CauseEffectInstanceId);
                if (active != null)
                {
                    causeHandle = active.Handle;
                    m_Causes[causeHandle] = new PortableEffectCause(active.Spec.Definition,
                        ingress.CauseEffectInstanceId, ingress.CauseContext);
                }
            }
            if (causeHandle == 0 && definition != null && ingress.CauseContext.IsValid)
            {
                causeHandle = m_AllocateHandle();
                m_Causes[causeHandle] = new PortableEffectCause(definition, ingress.CauseEffectInstanceId, ingress.CauseContext);
            }
            if (m_State.ApplyAuthoritativeAttribute(m_Catalog.AttributeDependencies.AttributeId(attributeIndex), ingress.BaseValue, ingress.CurrentValue, ingress.ValueRevision,
                causeHandle, out IReadOnlyList<PortableAttributeChange> changes))
                AddAttributeChanges(changes);
            if (!((IGameplayEffectControlPort<SimulationGameplayEffectApplication, PortableEffectSpecState,
                PortableActiveEffectState, PortablePredictionRecord, PortableTagQuery, FPAbilityExecutionSavepoint>)this)
                .TryReserveChanges(changeCount, out failure))
            {
                m_SavepointPort.Restore(savepoint);
                TrimChanges(changeCount);
                RebuildCauses();
                AddFailure(failure);
                return new GameplayEffectApplyResult(failure);
            }
            m_SavepointPort.Release(savepoint);
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, ingress.CauseEffectInstanceId, string.Empty);
        }

        public void ClearConfirmedAction(ulong actionInstanceId)
        {
            EnsureWorkingState();
            m_Control.ClearConfirmedAction(actionInstanceId);
        }

        public GameplayEffectApplyResult SetActionTags(ulong actionInstanceId, ActionAdmissionProfile profile)
        {
            EnsureWorkingState();
            GameplayTagSourceIdentityValue source = GameplayTagSourceIdentityValue.Action(actionInstanceId);
            if (!TryAdmitExternalTags(source, profile.GrantedTags, out GameplayEffectExecutionFailure failure))
                return new GameplayEffectApplyResult(failure);
            SetTags(source, profile.GrantedTags, default);
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
        }

        public void RemoveActionTags(ulong actionInstanceId)
        {
            EnsureWorkingState();
            SetTags(GameplayTagSourceIdentityValue.Action(actionInstanceId), Array.Empty<string>(), default);
        }

        public GameplayEffectApplyResult SetEquipmentTags(GameplayTagSourceIdentityValue sourceId, IReadOnlyList<string> tags)
        {
            EnsureWorkingState();
            if (!TryAdmitExternalTags(sourceId, tags, out GameplayEffectExecutionFailure failure))
                return new GameplayEffectApplyResult(failure);
            SetTags(sourceId, tags, default);
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
        }

        public void RemoveEquipmentTags(GameplayTagSourceIdentityValue sourceId)
        {
            EnsureWorkingState();
            SetTags(sourceId, Array.Empty<string>(), default);
        }

        public ReadOnlySpan<PortableEffectRuntimeChange> PendingChanges => Changes.AsSpan(0, m_ChangeCount);
        public void ClearChanges() => TrimChanges(0);

        void AddChange(in PortableEffectRuntimeChange change)
        {
            change.Retain();
            Changes[m_ChangeCount++] = change;
        }

        public void CancelMutation()
        {
            ClearChanges();
            RebuildCauses();
        }

        bool EvaluateTagRequirement(PortableEffectSpecState spec, PortableTagRequirementsComponent requirement)
        {
            bool source = requirement.Source.IsEmpty || m_State.Catalog.Matches(requirement.Source, spec.SourceTags);
            bool target = requirement.Target.IsEmpty || m_State.Matches(requirement.Target);
            return source && target;
        }

        bool EvaluateAttributeRequirement(PortableEffectSpecState spec, PortableAttributeRequirementsComponent requirement)
        {
            FP value;
            if (requirement.Source == PortableAttributeSource.SourceSnapshot)
            {
                if (!spec.SourceAttributes.TryGetValue(requirement.AttributeId, out value))
                    return false;
            }
            else
            {
                value = m_State.RequireAttribute(requirement.AttributeId).CurrentValue;
            }
            if (!TryResolveMagnitude(spec, requirement.Threshold, out FP threshold, out _))
                return false;
            return CompareRequirement(value, threshold, requirement.Comparison);
        }

        void ActivateCurrentModifier(PortableActiveEffectState active, PortableModifierComponent modifier)
        {
            if (!TryResolveMagnitude(active.Spec, modifier.Magnitude, out FP value, out string liveAttribute))
                throw new InvalidOperationException($"Unable to resolve Gameplay Attribute modifier magnitude for '{modifier.AttributeId}'.");
            FP coefficient = modifier.Magnitude.Coefficient;
            FP postAdd = modifier.Magnitude.PostAdd;
            if (modifier.ScaleWithStack)
            {
                FP stack = default(QuantumSimulationMath).FromInt64(active.StackCount);
                if (CanonicalTextValue.IsNullOrEmpty(liveAttribute))
                    value *= stack;
                else
                {
                    coefficient *= stack;
                    postAdd *= stack;
                }
            }
            var state = new PortableAttributeModifierState(
                m_AllocateHandle(),
                active.Handle,
                modifier.Operation,
                value,
                modifier.Priority,
                modifier.ClampBound,
                CanonicalTextValue.IsNullOrEmpty(liveAttribute) ? -1 : m_State.Catalog.AttributeDependencies.AttributeIndex(liveAttribute),
                coefficient,
                postAdd,
                m_AllocateHandle());
            AddAttributeChanges(m_State.AddModifier(modifier.AttributeId, state));
        }

        void SetTags(GameplayTagSourceIdentityValue source, IReadOnlyList<string> tags, PortableEffectCause cause)
        {
            IReadOnlyList<string> previous = m_State.SourceTags(source);
            int left = 0;
            int right = 0;
            GameplayEffectContextIdentity context = cause.Context.IsValid ? ToCommon(cause.Context) :
                new GameplayEffectContextIdentity(m_ActorId, m_ActorId, source.Kind == 1 ? source.NumericIdentity : 0,
                    0, 0, m_Tick.Value, false);
            while (left < previous.Count || right < tags.Count)
            {
                int order = left == previous.Count ? 1 : right == tags.Count ? -1 :
                    CanonicalTextValue.CompareOrdinal(previous[left], tags[right]);
                if (order == 0)
                {
                    left++;
                    right++;
                    continue;
                }
                string tag = order < 0 ? previous[left++] : tags[right++];
                int before = m_State.TagCount(tag, source.Scope);
                AddChange(new PortableEffectRuntimeChange(NextChangeCursor(),
                    new GameplayTagRuntimeChange(source, tag, before, before + (order < 0 ? -1 : 1),
                        cause.Definition?.Id, cause.InstanceId, context)));
            }
            m_State.SetCompiledTags(source, tags);
        }

        void ActivateGrantedTags(PortableActiveEffectState active)
        {
            SetTags(GameplayTagSourceIdentityValue.Effect(active.Handle, active.Spec.Context.Scope), active.Spec.Definition.GrantedTags,
                m_Causes[active.Handle]);
        }

        void DeactivatePersistent(PortableActiveEffectState active)
        {
            PortableModifierRemovalCursor cursor = m_State.BeginModifierRemoval(active.Handle);
            while (m_State.RemoveNextModifier(ref cursor, out IReadOnlyList<PortableAttributeChange> changes))
                AddAttributeChanges(changes);
            SetTags(GameplayTagSourceIdentityValue.Effect(active.Handle, active.Spec.Context.Scope), Array.Empty<string>(),
                m_Causes[active.Handle]);
        }

        void ExecuteNumericComponent(
            PortableEffectSpecState spec,
            ulong handle,
            int stackCount,
            PortableEffectComponent component)
        {
            switch (component)
            {
                case PortableModifierComponent modifier when modifier.Application == PortableModifierApplication.BaseValue:
                {
                    if (!TryResolveMagnitude(spec, modifier.Magnitude, out FP magnitude, out _))
                        throw new InvalidOperationException($"Unable to resolve Gameplay Modifier magnitude for '{modifier.AttributeId}'.");
                    if (modifier.ScaleWithStack)
                        magnitude *= default(QuantumSimulationMath).FromInt64(stackCount);
                    CapturePredictionBefore(modifier.AttributeId);
                    AddAttributeChanges(m_State.MutateBase(modifier.AttributeId, modifier.Operation, magnitude, modifier.ClampBound, handle));
                    break;
                }
                case PortableExecutionComponent execution:
                {
                    for (int mutationIndex = 0; mutationIndex < execution.Mutations.Length; mutationIndex++)
                    {
                        PortableExecutionMutation mutation = execution.Mutations[mutationIndex];
                        if (!TryResolveMagnitude(spec, mutation.Magnitude, out FP magnitude, out _))
                            throw new InvalidOperationException($"Unable to resolve Gameplay Execution magnitude for '{mutation.AttributeId}'.");
                        magnitude *= default(QuantumSimulationMath).FromInt64(stackCount);
                        CapturePredictionBefore(mutation.AttributeId);
                        AddAttributeChanges(m_State.MutateBase(mutation.AttributeId, mutation.Operation, magnitude, mutation.ClampBound, handle));
                    }
                    break;
                }
            }
        }

        readonly struct MagnitudeInputs : IGameplayEffectMagnitudeInputs<FP>
        {
            readonly FPGameplayEffectTarget m_Target;

            internal MagnitudeInputs(FPGameplayEffectTarget target, PortableEffectSpecState spec)
            {
                m_Target = target;
                Spec = spec;
                StartupValues = null;
                PreviewSelf = false;
            }

            internal MagnitudeInputs(FPGameplayEffectTarget target, IReadOnlyList<SimulationSetByCallerValue> values)
            {
                Spec = null;
                m_Target = target;
                StartupValues = values;
                PreviewSelf = true;
            }

            internal readonly PortableEffectSpecState Spec;
            internal readonly IReadOnlyList<SimulationSetByCallerValue> StartupValues;
            public bool PreviewSelf { get; }

            public bool TryParameter(string id, out FP value)
            {
                if (!PreviewSelf)
                    return Spec.SetByCaller.TryGetValue(id, out value);
                for (int i = 0; i < StartupValues.Count; i++)
                    if (CanonicalTextValue.Equals(StartupValues[i].ParameterId, id, StringComparison.Ordinal))
                    {
                        value = StartupValues[i].Value;
                        return true;
                    }
                value = default;
                return false;
            }

            public bool TrySourceAttributeSnapshot(string id, out FP value) =>
                Spec.SourceAttributes.TryGetValue(id, out value);

            public bool TryTargetAttributeSnapshot(string id, out FP value) =>
                Spec.TargetAttributes.TryGetValue(id, out value);

            public FP ReadCurrentAttribute(string id) => m_Target.ReadCurrentAttribute(id);
        }

        bool TryResolveMagnitude(PortableEffectSpecState spec, PortableMagnitude magnitude, out FP value, out string liveAttribute) =>
            TryResolveMagnitude(new MagnitudeInputs(this, spec), magnitude, out value, out liveAttribute);

        bool TryResolveMagnitude(MagnitudeInputs input, PortableMagnitude magnitude, out FP value, out string liveAttribute) =>
            GameplayEffectMagnitudeCalculation<FP, QuantumSimulationMath>.TryResolve(input, magnitude, out value, out liveAttribute);

        FP ReadCurrentAttribute(string attributeId)
        {
            int index = m_Catalog.AttributeDependencies.AttributeIndex(attributeId);
            if (m_State != null) return m_State.AttributeAt(index).CurrentValue;
            m_CommittedState.ReadAttributeAt(index, out _, out FP current, out _);
            return current;
        }

        internal void BeginAvailabilityRead(GameplayEffectStateAggregate state,
            FPGameplayEffectRuntimeCatalog catalog, ActorId actorId, SimulationTick tick)
        {
            m_CommittedState = state;
            m_Catalog = catalog;
            m_ActorId = actorId;
            m_Tick = tick;
        }

        internal void EndAvailabilityRead()
        {
            m_CommittedState = default;
            m_Catalog = null;
            m_ActorId = default;
            m_Tick = default;
        }

        public GameplayEffectApplyResult PreviewStartupEffect(string effectId, IReadOnlyList<SimulationSetByCallerValue> values)
        {
            PortableEffectDefinition definition = m_Catalog.RequireEffect(effectId);
            var input = new MagnitudeInputs(this, values);
            for (int i = 0; i < definition.SetByCallerParameters.Length; i++)
                if (!input.TryParameter(definition.SetByCallerParameters[i], out _))
                    return StartupPreviewFailure(definition, GameplayEffectApplyResultKind.MissingParameter, "MissingSetByCaller", definition.SetByCallerParameters[i]);
            foreach (PortableEffectComponent component in definition.Components)
            {
                if (component is PortableTagRequirementsComponent tags && tags.Phase == PortableRequirementPhase.Application &&
                    (!m_Catalog.Matches(tags.Source, OwnedTags) ||
                     !m_Catalog.Matches(tags.Target, OwnedTags)))
                    return StartupPreviewFailure(definition, GameplayEffectApplyResultKind.RequirementFailed, "TagRequirementFailed");
                if (component is PortableAttributeRequirementsComponent attribute && attribute.Phase == PortableRequirementPhase.Application)
                {
                    if (!TryResolveMagnitude(input, attribute.Threshold, out FP threshold, out _) ||
                        !CompareRequirement(ReadCurrentAttribute(attribute.AttributeId), threshold, attribute.Comparison))
                        return StartupPreviewFailure(definition, GameplayEffectApplyResultKind.RequirementFailed, "AttributeRequirementFailed");
                }
            }
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
        }

        GameplayEffectApplyResult StartupPreviewFailure(PortableEffectDefinition definition,
            GameplayEffectApplyResultKind kind, CanonicalTextValue reason, CanonicalTextValue parameter = default) =>
            new GameplayEffectApplyResult(new GameplayEffectExecutionFailure(definition.Id, 0,
                GameplayEffectLifecycleKind.Applied, definition.Id,
                new GameplayEffectContextIdentity(m_ActorId, m_ActorId, 0, 0, 0, m_Tick.Value, false),
                m_Tick.Value, kind, reason, parameter));

        static bool CompareRequirement(FP value, FP threshold, PortableAttributeComparison comparison) => comparison switch
        {
            PortableAttributeComparison.Less => value < threshold,
            PortableAttributeComparison.LessOrEqual => value <= threshold,
            PortableAttributeComparison.Equal => value == threshold,
            PortableAttributeComparison.GreaterOrEqual => value >= threshold,
            PortableAttributeComparison.Greater => value > threshold,
            PortableAttributeComparison.NotEqual => value != threshold,
            _ => false
        };

        SimulationGameplayEffectApplication BuildAdditionalApplication(
            PortableEffectSpecState parent,
            int componentIndex,
            int effectIndex,
            SimulationGameplayEffectContext context)
        {
            var component = (PortableAdditionalEffectsComponent)parent.Definition.Components[componentIndex];
            PortableAdditionalEffect effect = component.Effects[effectIndex];
            PortableEffectDefinition definition = m_State.Catalog.RequireEffect(effect.EffectId);
            m_Scratch.AcquireAdditionalApplicationInputs(
                out List<SimulationSetByCallerValue> values,
                out List<SimulationAttributeCapture> attributes);
            for (int bindingIndex = 0; bindingIndex < effect.Bindings.Length; bindingIndex++)
            {
                PortableAdditionalParameterBinding binding = effect.Bindings[bindingIndex];
                FP value;
                if (binding.Source == PortableAdditionalParameterSource.ParentSetByCaller)
                {
                    if (!parent.SetByCaller.TryGetValue(binding.ParentParameterId, out value))
                        throw new InvalidOperationException($"Additional Effect parent parameter '{binding.ParentParameterId}' is unavailable.");
                }
                else
                {
                    value = binding.Constant;
                }
                values.Add(new SimulationSetByCallerValue(binding.ChildParameterId, value));
            }
            ReadOnlySpan<int> captureIndices = parent.Definition.Layout.AdditionalDeclaration(componentIndex, effectIndex).SourceCaptureIndices;
            foreach (int index in captureIndices)
                attributes.Add(new SimulationAttributeCapture(parent.SourceAttributes.Keys[index], parent.SourceAttributes.Values[index]));
            return SimulationGameplayEffectApplication.FromCompiled(
                definition.Id,
                definition.Revision,
                context,
                values,
                attributes,
                parent.SourceTags);
        }

        void AddReconciliation(GameplayEffectReconciliationResult result, GameplayEffectContextIdentity context) =>
            AddChange(new PortableEffectRuntimeChange(NextChangeCursor(), result, ToTarget(context)));


        void ReleaseAdditionalApplication(SimulationGameplayEffectApplication application) =>
            m_Scratch.ReleaseAdditionalApplicationInputs(application.SetByCallerValues);

        void CapturePredictionBefore(string attributeId)
        {
            if (m_CurrentPrediction == null || m_CurrentPrediction.Attributes.ContainsKey(attributeId))
                return;
            PortableAttributeState value = m_State.RequireAttribute(attributeId);
            m_CurrentPrediction.Attributes.Add(attributeId,
                new PortablePredictionAttributeSnapshot(value.BaseValue, value.CurrentValue, value.Revision, 0));
        }

        void AddLifecycle(PortableActiveEffectState active, SimulationGameplayEffectLifecycleOperation operation)
        {
            PortableEffectCause cause = m_Causes[active.Handle];
            AddLifecycle(active.Spec.Definition, active.InstanceId, operation, cause.Context,
                active.StartTick, active.EndTick, active.StackCount, active.LifecycleRevision, false);
        }

        void AddLifecycle(PortableEffectDefinition definition, ulong instanceId,
            SimulationGameplayEffectLifecycleOperation operation, SimulationGameplayEffectContext context,
            ulong startTick, ulong endTick, int stackCount, ulong revision, bool instant) =>
            AddChange(new PortableEffectRuntimeChange(NextChangeCursor(), definition, instanceId,
                operation, context, startTick, endTick, stackCount, revision, instant));

        void AddAttributeChanges(IReadOnlyList<PortableAttributeChange> changes)
        {
            for (int i = 0; i < changes.Count; i++)
            {
                PortableAttributeChange change = changes[i];
                if (m_CurrentPrediction != null && !m_CurrentPrediction.Attributes.ContainsKey(change.AttributeId))
                    m_CurrentPrediction.Attributes.Add(change.AttributeId,
                        new PortablePredictionAttributeSnapshot(change.BeforeBase,
                            change.BeforeCurrent, change.Revision - 1, 0));
                PortableEffectCause cause = m_Causes.TryGetValue(change.CauseHandle, out PortableEffectCause value) ? value : default;
                AddChange(new PortableEffectRuntimeChange(NextChangeCursor(), change,
                    cause.Definition?.Id, cause.InstanceId, cause.Context));
            }
        }

        void AddCue(string cueId, PortableCueTrigger trigger, PortableEffectDefinition definition,
            ulong instanceId, SimulationGameplayEffectContext context, bool trackPrediction = true)
        {
            AddChange(new PortableEffectRuntimeChange(NextChangeCursor(), cueId, trigger, definition, instanceId, context));
            if (trackPrediction && m_CurrentPrediction != null && !m_CurrentPrediction.CueIds.Contains(cueId))
                m_CurrentPrediction.CueIds.Add(cueId);
        }

        void AddFailure(GameplayEffectExecutionFailure failure) =>
            AddChange(new PortableEffectRuntimeChange(NextChangeCursor(), failure));



        public void ReportFailure(GameplayEffectExecutionFailure failure)
        {
            EnsureWorkingState();
            AddFailure(failure);
        }

        ulong NextChangeCursor()
        {
            m_State.ChangeCursor = checked(m_State.ChangeCursor + 1);
            return m_State.ChangeCursor;
        }

        void TrimChanges(int count)
        {
            for (int index = count; index < m_ChangeCount; index++)
                Changes[index].Release();
            Array.Clear(Changes, count, m_ChangeCount - count);
            m_ChangeCount = count;
        }

        void RebuildCauses()
        {
            m_Causes.Clear();
            if (m_State != null)
            {
                for (int i = 0; i < m_State.ActiveEffects.Count; i++)
                {
                    PortableActiveEffectState active = m_State.ActiveEffects[i];
                    m_Causes.Add(active.Handle, new PortableEffectCause(active.Spec.Definition, active.InstanceId, ToTarget(active.LifecycleContext, active.Spec.Context.SuccessTarget)));
                }
                return;
            }
            m_CommittedState.CollectActiveEffectIdentities(m_Scratch.ActiveIdentities);
            foreach (GameplayEffectActiveIdentity active in m_Scratch.ActiveIdentities)
                m_Causes.Add(active.Handle, new PortableEffectCause(active.Definition, active.InstanceId, active.Context));
        }

    }
}
