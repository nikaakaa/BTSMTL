using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedGameplayEffectOperationRuntime : FixedOperationModule,
        IFixedGameplayTagQuery,
        IFixedGameplayEffectActionPort
    {
		readonly FixedAbilityExecutionFrame m_Frame;
        readonly IFixedActionContextReader m_Actions;
        readonly Func<ulong> m_AllocateHandle;
        readonly FixedFactSink m_Facts;
        readonly FixedGameplayEffectTarget m_GameplayEffects;
        readonly FixedGameplayEffectExecutionScratch m_Scratch;
        bool m_EvaluationActive;
        SimulationTick m_ActiveTick;

		public FixedGameplayEffectOperationRuntime(
			FixedGameplayAbilityExecutionAccess access,
			FixedAbilityExecutionFrame frame,
            IFixedActionContextReader actions,
            FixedHandleAllocator handles,
            FixedFactSink facts,
			FixedGameplayEffectExecutionScratch scratch)
			: base(access)
		{
			m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            m_AllocateHandle = handles.Next;
            m_Facts = facts;
            m_GameplayEffects = (scratch ?? throw new ArgumentNullException(nameof(scratch))).Target;
            m_Scratch = scratch;
        }

        public GameplayCommandBatchAdmission ReceiveCommandBatch(int commands, SimulationExecutionSource source)
        {
            GameplayCommandBatchAdmission admission = m_Scratch.ReceiveCommandBatch(m_Frame.ActorId,
                m_Frame.Tick.Value, commands, source);
            if (!admission.Accepted)
                m_Frame.RecordCommandBatchRejection(admission);
            return admission;
        }

        public void ReportStartupFailure(GameplayEffectApplyResult failure, SimulationExecutionSource source,
            bool requiresCommandAdmission)
        {
            if (requiresCommandAdmission && !ReceiveCommandBatch(1, source).Accepted)
                return;
            m_GameplayEffects.ReportFailure(failure.Failure);
            RecordChanges(new ActivationId(source, 1), "action:startup-failed");
        }

        public void CancelActionMutation() => m_GameplayEffects.CancelMutation();

        public void BeginEvaluation()
        {
            if (m_EvaluationActive)
                throw new InvalidOperationException(
                    $"Gameplay Effect evaluation is already active for '{m_Frame.ActorId}' " +
                    $"at '{m_ActiveTick.Value}' while beginning '{m_Frame.Tick.Value}' " +
                    $"for '{m_Frame.Data.AbilityId}'.");
            m_ActiveTick = m_Frame.Tick;
            m_GameplayEffects.Begin(
				m_Frame.SavepointPort,
                m_Frame.GameplayEffectState,
                Access.Services.GameplayEffectCatalog,
                m_Frame.ActorId,
                m_Frame.Tick,
                m_AllocateHandle,
                m_Frame.Services);
            m_EvaluationActive = true;
        }

        public void EndEvaluation()
        {
            if (!m_EvaluationActive)
                return;
            m_EvaluationActive = false;
            m_GameplayEffects.End();
            m_ActiveTick = default;
        }

        public IReadOnlyList<string> OwnedTags => m_GameplayEffects.OwnedTags;
        public bool HasTag(string tag) => m_GameplayEffects.HasTag(tag);
        public bool Matches(PortableTagQuery query) => m_GameplayEffects.Matches(query);
        public GameplayEffectApplyResult PreviewStartupEffect(string effectId, IReadOnlyList<SimulationSetByCallerValue> values) =>
            m_GameplayEffects.PreviewStartupEffect(effectId, values);

        public GameplayEffectApplyResult ApplyStartupEffect(string effectId, IReadOnlyList<SimulationSetByCallerValue> values,
            SimulationGameplayEffectContext context, SimulationExecutionSource source)
        {
            PortableEffectDefinition definition = Access.Services.GameplayEffectCatalog.RequireEffect(effectId);
            GameplayEffectApplyResult result = m_GameplayEffects.Apply(SimulationGameplayEffectApplication.FromCompiled(
                definition.Id, definition.Revision, context, values, Array.Empty<SimulationAttributeCapture>(), null));
            RecordChanges(new ActivationId(source, 1), "action:startup-effect");
            return result;
        }

        public GameplayEffectReconciliationResult ReconcileAction(ulong actionInstanceId, ulong predictionKey,
            AbilityLifecycleTransition transition, IReadOnlyList<SimulationGameplayEffectLifecycleIngress> commands,
            SimulationExecutionSource source, bool requiresCommandReception)
        {
            if (requiresCommandReception && !ReceiveCommandBatch(Math.Max(1, commands.Count), source).Accepted)
                return new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.NotReceived,
                    new GameplayEffectReconciliationScope(GameplayEffectReconciliationScopeKind.PredictionKey,
                        predictionKey, string.Empty, 0), 0, 0, 0, reason: "GameplayCommandBatchCapacityExceeded");
            GameplayEffectReconciliationResult result = m_GameplayEffects.ReconcileAction(actionInstanceId, predictionKey, transition, commands);
            RecordChanges(new ActivationId(source, 1), "action:reconciliation");
            return result;
        }

        public GameplayEffectApplyResult CommitActionMutation(SimulationExecutionSource source)
        {
            GameplayEffectApplyResult result = m_GameplayEffects.CommitMutation();
            if (result.Succeeded)
                RecordChanges(new ActivationId(source, 1), "action:mutation");
            return result;
        }

        public GameplayEffectApplyResult SetActionTags(ulong actionInstanceId, ActionAdmissionProfile profile) => m_GameplayEffects.SetActionTags(actionInstanceId, profile);
        public void RemoveActionTags(ulong actionInstanceId) => m_GameplayEffects.RemoveActionTags(actionInstanceId);
        public void ClearConfirmedAction(ulong actionInstanceId) => m_GameplayEffects.ClearConfirmedAction(actionInstanceId);

        public GameplayEffectApplyResult SetEquipmentTags(GameplayTagSourceIdentityValue sourceId, IReadOnlyList<string> tags) =>
            m_GameplayEffects.SetEquipmentTags(sourceId, tags);

        public void RemoveEquipmentTags(GameplayTagSourceIdentityValue sourceId) =>
            m_GameplayEffects.RemoveEquipmentTags(sourceId);

        public GameplayEffectApplyResult ApplyEquipmentPassive(string effectId)
        {
            PortableEffectDefinition definition = Access.Services.GameplayEffectCatalog.RequireEffect(effectId);
            var context = new SimulationGameplayEffectContext(
                m_Frame.ActorId,
                m_Frame.ActorId,
                0,
                0,
                0,
                m_Frame.Tick.Value,
                SimulationGameplayEffectApplicationMode.Confirmed);
            GameplayEffectApplyResult result = m_GameplayEffects.Apply(
                SimulationGameplayEffectApplication.FromCompiled(
                    definition.Id,
                    definition.Revision,
                    context,
                    Array.Empty<SimulationSetByCallerValue>(),
                    Array.Empty<SimulationAttributeCapture>(),
                    null));
            return result;
        }

        public GameplayEffectRemoveResult RemoveEquipmentPassive(ulong handle)
        {
            return m_GameplayEffects.Remove(new GameplayEffectRemoveRequest<PortableTagQuery>(
                GameplayEffectRemoveSelector.Handle,
                handle,
                string.Empty,
                m_Frame.ActorId,
                null));
        }

        public GameplayEffectApplyResult CommitEquipmentMutation(SimulationOperation source)
        {
            GameplayEffectApplyResult result = m_GameplayEffects.CommitMutation();
            if (result.Succeeded)
                RecordChanges(source, "equipment:commit");
            return result;
        }

        public void CancelEquipmentMutation() => m_GameplayEffects.CancelMutation();

        public void ReportEquipmentFailure(SimulationOperation source, GameplayEffectExecutionFailure failure)
        {
            m_GameplayEffects.ReportFailure(failure);
            RecordChanges(source, "equipment:contribution-failed");
        }

        public void ApplyIngress(SimulationIngress ingress)
        {
            if (ingress.Header.ActorId != m_Frame.ActorId)
                throw new InvalidOperationException($"Simulation ingress '{ingress.Header.FactIdentity}' targets '{ingress.Header.ActorId}', expected '{m_Frame.ActorId}'.");
            switch (ingress.Header.Kind)
            {
                case SimulationIngressKind.GameplayResult:
                    m_GameplayEffects.ApplyGameplayResult(ingress.GameplayResult);
                    break;
                case SimulationIngressKind.GameplayEffectLifecycle:
                    m_GameplayEffects.ApplyLifecycle(ingress.GameplayEffectLifecycle);
                    break;
                case SimulationIngressKind.AttributeValue:
                    m_GameplayEffects.ApplyAttribute(ingress.AttributeValue);
                    break;
                default:
                    throw new InvalidOperationException($"Gameplay Effect runtime cannot apply ingress kind '{ingress.Header.Kind}'.");
            }
            RecordChanges(new ActivationId(SimulationExecutionSource.FromGameplayEffect(), 1), "ingress");
        }

        public void Advance()
        {
            m_GameplayEffects.Advance();
            RecordChanges(new ActivationId(SimulationExecutionSource.FromGameplayEffect(), 1), "gameplay-effect:advance");
        }

        public bool Apply(SimulationOperation operation, out ulong appliedHandle)
        {
            appliedHandle = 0;
            ProgramCatalogEntry definition = RequireGameplayEffectCatalog(operation);
            ulong configuredRevision = GetUInt64Constant(operation, OperationNamedConstant.DefinitionRevision, 0);
            if (configuredRevision != (ulong)definition.Revision)
                throw new InvalidOperationException($"Gameplay Effect operation '{SourcePath(operation)}' revision '{configuredRevision}' does not match catalog revision '{definition.Revision}'.");

            CanonicalTextValue contextId = GetStringConstant(operation, OperationNamedConstant.ActionContext, string.Empty);
            ulong actionInstanceId = 0;
            ulong predictionKey = 0;
            if (!CanonicalTextValue.IsNullOrEmpty(contextId))
            {
                if (m_Actions.FindActive(contextId, out FixedActionInstanceState action) < 0)
                    return false;
                actionInstanceId = action.InstanceId;
                predictionKey = action.PredictionKey;
            }
            bool predicted = GetBooleanConstant(operation, OperationNamedConstant.Predicted, false);
            if (predicted && (actionInstanceId == 0 || predictionKey == 0))
                return false;

            SimulationSetByCallerValue[] values = m_Frame.Services.SetByCallerValues(operation.Handle);
            var context = new SimulationGameplayEffectContext(
                m_Frame.ActorId,
                m_Frame.ActorId,
                actionInstanceId,
                predictionKey,
                0,
                m_Frame.Tick.Value,
                predicted ? SimulationGameplayEffectApplicationMode.Predicted : SimulationGameplayEffectApplicationMode.Confirmed);
            SimulationGameplayEffectApplication application = SimulationGameplayEffectApplication.FromCompiled(
                definition.Identity,
                checked((uint)definition.Revision),
                context,
                values,
                Array.Empty<SimulationAttributeCapture>(),
                null);
            if (!ReceiveCommandBatch(1, SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation))).Accepted)
                return false;
            GameplayEffectApplyResult result = m_GameplayEffects.Apply(application);
            RecordChanges(operation, "operation:apply-effect");
            appliedHandle = result.Succeeded ? result.Handle : 0;
            return result.Succeeded;
        }

        public void SubmitAttack(SimulationOperation operation)
        {
            ProgramCatalogEntry catalog = RequireGameplayEffectCatalog(operation);
            PortableEffectDefinition definition = Access.Services.GameplayEffectCatalog.RequireEffect(catalog.Identity);
            FixedActionInstanceState action;
            if (!m_Actions.TryGetCurrentSkillExecution(out action) || !action.IsActive)
                throw new InvalidOperationException("Attack query has no owning Action instance.");
            AbilityTreeClipInvocation clip = m_Frame.TreeClipInvocation;
            FixedCombatQuery query = m_Frame.CombatQueries.Append();
            query.Identity = new CombatAttackIdentity(action.InstanceId, action.SegmentGeneration,
                action.SkillId, operation.Handle, clip.ClipAuthoringId,
                clip.PlaybackGeneration, clip.BranchRevision, clip.Cycle, clip.TimelineRuntimeHandle);
            query.Action = new TimelineActionContextIdentity(action.ActionId, action.ContextId,
                action.InstanceId, action.PredictionKey, action.TargetSnapshot.TargetId, action.SkillId,
                action.SkillEntryOperation, action.SkillExecutionGeneration);
            query.Definition = definition;
            query.NodeId = clip.NodeAuthoringId;
            query.AttackEventId = GetStringConstant(operation, OperationNamedConstant.AttackEventId, string.Empty);
            query.Origin = m_Frame.BodyFacts.Position;
            query.Yaw = m_Frame.BodyFacts.Yaw;
            query.PreviousTime = clip.PreviousTime;
            query.Time = clip.Time;
            query.WindowStartTime = clip.WindowStartTime;
            query.BodyStartFraction = clip.BodyStartFraction;
            query.BodyEndFraction = clip.BodyEndFraction;
            query.Header = m_Facts.Next(operation);
            query.RequestId = query.Header.Sequence;
        }

        public GameplayEffectRemoveResult Remove(SimulationOperation operation, ulong handle)
        {
            if (operation.Integer0 < byte.MinValue || operation.Integer0 > byte.MaxValue)
                return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.InvalidRequest, default);
            var selector = (GameplayEffectRemoveSelector)(byte)operation.Integer0;
            string effectId = GetStringConstant(operation, OperationNamedConstant.Effect, string.Empty);
            PortableTagQuery query = selector == GameplayEffectRemoveSelector.EffectTagQuery
                ? m_Frame.Services.RequireTagQuery(operation.Handle)
                : null;
            var request = new GameplayEffectRemoveRequest<PortableTagQuery>(selector, handle, effectId, m_Frame.ActorId, query);
            GameplayCommandBatchAdmission admission = ReceiveCommandBatch(1,
                SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)));
            if (!admission.Accepted)
                return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.NotReceived, default);
            GameplayEffectRemoveResult result = m_GameplayEffects.Remove(request);
            RecordChanges(operation, "operation:remove-effect");
            return result;
        }

        public AbilityStateValue ReadAttribute(SimulationOperation operation, string outputPort)
        {
            if (!m_GameplayEffects.TryGetAttribute(operation.Text0, out FixedScalar baseValue, out FixedScalar currentValue, out _))
            {
                if (CanonicalTextValue.Equals(outputPort, "m_Valid", StringComparison.Ordinal))
                    return AbilityStateValue.FromBoolean(false);
                return AbilityStateValue.FromScalar(FixedScalar.Zero);
            }
            if (CanonicalTextValue.Equals(outputPort, "m_Valid", StringComparison.Ordinal))
                return AbilityStateValue.FromBoolean(true);
            if (CanonicalTextValue.Equals(outputPort, "m_BaseValue", StringComparison.Ordinal))
                return AbilityStateValue.FromScalar(baseValue);
            if (CanonicalTextValue.Equals(outputPort, "m_CurrentValue", StringComparison.Ordinal))
                return AbilityStateValue.FromScalar(currentValue);
            throw new InvalidOperationException($"Gameplay Attribute output port '{outputPort}' is unknown.");
        }

        void RecordChanges(SimulationOperation source, string cause)
        {
            if (m_GameplayEffects.PendingChanges.Count != 0)
                RecordChanges(m_Facts.Activation(source), cause);
        }

        void RecordChanges(ActivationId source, string cause)
        {
            IReadOnlyList<PortableEffectRuntimeChange> changes = m_GameplayEffects.PendingChanges;
            try
            {
                for (int i = 0; i < changes.Count; i++)
                {
                    PortableEffectRuntimeChange change = changes[i];
                    m_Frame.RecordGameplayEffectChange(in change, source, cause);
                }
            }
            finally
            {
                m_GameplayEffects.ClearChanges();
            }
        }

        ProgramCatalogEntry RequireGameplayEffectCatalog(SimulationOperation operation)
        {
            ProgramCatalogEntry found = null;
            IReadOnlyList<ProgramReference> references = References(operation.Handle, ProgramReferenceKind.CatalogEntry);
            for (int index = 0; index < references.Count; index++)
            {
                ProgramReference reference = references[index];
                ProgramCatalogEntry candidate = m_Ability.CatalogEntries[reference.TargetIndex];
                if (candidate.Kind != ProgramCatalogEntryKind.GameplayEffect)
                    continue;
                if (found != null)
                    throw new InvalidOperationException($"Gameplay Effect operation '{SourcePath(operation)}' has multiple Effect catalog references.");
                found = candidate;
            }
            return found ?? throw new InvalidOperationException($"Gameplay Effect operation '{SourcePath(operation)}' has no Effect catalog reference.");
        }

        ulong GetUInt64Constant(SimulationOperation operation, OperationNamedConstant field, ulong fallback)
        {
            ProgramConstant constant = FindConstant(operation, field);
            return constant != null && constant.Kind == ProgramConstantKind.UInt64 ? constant.UInt64 : fallback;
        }

    }
}


