using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
	internal sealed class Float32GameplayEffectOperationRuntime : Float32OperationModule,
		IFloat32GameplayTagQuery,
		IFloat32GameplayEffectActionPort
	{
		readonly Float32AbilityExecutionFrame m_Frame;
		readonly IFloat32ActionContextReader m_Actions;
		readonly Func<ulong> m_AllocateHandle;
		readonly Float32FactSink m_Facts;
		readonly Float32GameplayEffectTarget m_GameplayEffects;
        readonly Float32GameplayEffectExecutionScratch m_Scratch;
		bool m_EvaluationActive;
		SimulationTick m_ActiveTick;

		public Float32GameplayEffectOperationRuntime(
			Float32GameplayAbilityExecutionAccess access,
			Float32AbilityExecutionFrame frame,
			IFloat32ActionContextReader actions,
			Float32HandleAllocator handles,
			Float32FactSink facts,
			Float32GameplayEffectExecutionScratch scratch)
			: base(access)
		{
			m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
			m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
			m_AllocateHandle = handles.Next;
			m_Facts = facts;
			m_GameplayEffects = (scratch ?? throw new ArgumentNullException(nameof(scratch))).Target;
            m_Scratch = scratch;
		}

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

        public void ApplyInitialEffects()
        {
            var initialEffects = Access.Services.GameplayEffectCatalog.InitialEffects;
            if (initialEffects.Count == 0)
                return;
            SimulationExecutionSource source = SimulationExecutionSource.FromGameplayEffect();
            GameplayCommandBatchAdmission admission = ReceiveCommandBatch(initialEffects.Count, source);
            if (!admission.Accepted)
                throw new InvalidOperationException(
                    $"Character '{m_Frame.ActorId}' initial effects exceed '{admission.Dimension}': " +
                    $"{admission.Required} required, {admission.Available} available.");
            var context = new SimulationGameplayEffectContext(m_Frame.ActorId, m_Frame.ActorId,
                0, 0, 0, m_Frame.Tick.Value, SimulationGameplayEffectApplicationMode.Confirmed);
            for (int index = 0; index < initialEffects.Count; index++)
            {
                var initial = initialEffects[index];
                GameplayEffectApplyResult result = m_GameplayEffects.Apply(
                    SimulationGameplayEffectApplication.FromCompiled(initial.Definition.Id,
                        initial.Definition.Revision, context, initial.Parameters,
                        Array.Empty<SimulationAttributeCapture>(), null));
                RecordChanges(new ActivationId(source, 1), "character:initial-effect");
                if (!result.AcceptedMutation)
                    throw new InvalidOperationException(
                        $"Character '{m_Frame.ActorId}' initial effect '{initial.Definition.Id}' failed: " +
                        $"{result.Kind}, {result.Reason}, parameter '{result.ParameterId}', " +
                        $"capacity '{result.Failure.CapacityDimension}' " +
                        $"({result.Failure.Required}/{result.Failure.Available}).");
            }
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
        public bool HasTag(string tag, GameplayEffectScope scope) => m_GameplayEffects.HasTag(tag, scope);
        public bool Matches(PortableTagQuery query, GameplayEffectScope scope) => m_GameplayEffects.Matches(query, scope);
        public bool TryFindOpportunity(GameplayEffectScope scope, PortableTagQuery query,
            out GameplayEffectOpportunityReference opportunity, out SimulationActionTargetSnapshot target) =>
            m_GameplayEffects.TryFindOpportunity(scope, query, out opportunity, out target);

        public bool ConsumeOpportunity(GameplayEffectOpportunityReference opportunity, SimulationExecutionSource source)
        {
            if (!ReceiveCommandBatch(1, source).Accepted)
                return false;
            GameplayEffectRemoveResult result = m_GameplayEffects.Remove(new GameplayEffectRemoveRequest<PortableTagQuery>(
                GameplayEffectRemoveSelector.Handle, opportunity.Handle, default, default, null));
            RecordChanges(new ActivationId(source, 1), "action:consume-opportunity");
            return result.Kind == GameplayEffectRemoveResultKind.Removed;
        }
        public bool HasTag(string tag, GameplayEffectQueryTarget target) =>
            TryResolveQueryScope(target, out GameplayEffectScope scope) && m_GameplayEffects.HasTag(tag, scope);
        public AbilityStateValue ReadEffect(SimulationOperation operation, string outputPort)
        {
            ulong handle = 0;
            SimulationGameplayEffectContext context = default;
            bool found = TryResolveQueryScope((GameplayEffectQueryTarget)operation.Integer0, out GameplayEffectScope scope) &&
                m_GameplayEffects.TryFindEffect(operation.Text0, scope, out handle, out context);
            return outputPort switch
            {
                "m_Result" => AbilityStateValue.FromBoolean(found),
                "m_Handle" => AbilityStateValue.FromUInt64(handle),
                "m_EventTag" => AbilityStateValue.FromIdentity(context.Success.EventTag),
                "m_EventId" => AbilityStateValue.FromUInt64(context.Success.EventId),
                "m_SourceTick" => AbilityStateValue.FromUInt64(context.Success.SourceTick),
                "m_AcceptedTick" => AbilityStateValue.FromUInt64(context.Success.AcceptedTick),
                "m_SourceActionId" => AbilityStateValue.FromUInt64(context.SourceActionInstanceId),
                "m_TargetId" => AbilityStateValue.FromIdentity(context.SuccessTarget.TargetId),
                "m_Position" => AbilityStateValue.FromVector3(context.SuccessTarget.Position),
                "m_Yaw" => AbilityStateValue.FromScalar(context.SuccessTarget.Yaw.Degrees),
                _ => throw new InvalidOperationException("Gameplay Effect context output is unsupported.")
            };
        }
        public bool Matches(PortableTagQuery query, GameplayEffectQueryTarget target) =>
            TryResolveQueryScope(target, out GameplayEffectScope scope) && m_GameplayEffects.Matches(query, scope);

        bool TryResolveQueryScope(GameplayEffectQueryTarget target, out GameplayEffectScope scope)
        {
            switch (target)
            {
                case GameplayEffectQueryTarget.Actor:
                    scope = GameplayEffectScope.Actor;
                    return true;
                case GameplayEffectQueryTarget.SelectedSource:
                    scope = GameplayEffectScope.Action(m_Frame.SelectedSource);
                    return m_Frame.SelectedSource.IsValid;
                case GameplayEffectQueryTarget.CurrentAction:
                    if (m_Actions.TryGetCurrentSkillExecution(out Float32ActionInstanceState action))
                    {
                        scope = GameplayEffectScope.Action(action.SourceContext);
                        return true;
                    }
                    scope = default;
                    return false;
                default:
                    throw new InvalidOperationException("Gameplay Effect query target is unsupported.");
            }
        }
        public bool TryFindAbilityBlock(IReadOnlyList<string> candidateTags, GameplayEffectScope scope, out ulong effectHandle) =>
            m_GameplayEffects.TryFindAbilityBlock(candidateTags, scope, out effectHandle);
        public bool TryFindReplacementSourceRejection(IReadOnlyList<string> candidateTags,
            IReadOnlyList<string> selectedSourceTags, bool hasSource, out ulong effectHandle) =>
            m_GameplayEffects.TryFindReplacementSourceRejection(candidateTags, selectedSourceTags, hasSource, out effectHandle);
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

		public void ReportEquipmentFailure(SimulationOperation source, GameplayEffectExecutionFailure failure)
		{
			m_GameplayEffects.ReportFailure(failure);
			RecordChanges(source, "equipment:contribution-failed");
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
            PortableEffectDefinition effect = Access.Services.GameplayEffectCatalog.RequireEffect(definition.Identity);
            CanonicalTextValue successEvent = GetStringConstant(operation, OperationNamedConstant.SuccessEvent, string.Empty);
			ulong actionInstanceId = 0;
			ulong predictionKey = 0;
			ulong executionGeneration = 0;
            Float32ActionInstanceState action = default;
            if (!contextId.IsEmpty || effect.Scope == GameplayEffectScopeKind.ActionInstance ||
                effect.Lifetime != GameplayEffectLifetimeKind.Independent || !successEvent.IsEmpty)
			{
                if (!m_Actions.TryGetCurrentSkillExecution(out action) ||
                    !action.IsActive || !contextId.IsEmpty && !action.ContextId.Equals(contextId))
					return false;
				actionInstanceId = action.InstanceId;
				predictionKey = action.PredictionKey;
				executionGeneration = action.SkillExecutionGeneration;
			}
			bool predicted = GetBooleanConstant(operation, OperationNamedConstant.Predicted, false);
			if (predicted && (actionInstanceId == 0 || predictionKey == 0))
				return false;

			SimulationSetByCallerValue[] values = m_Frame.Services.SetByCallerValues(operation.Handle);
            GameplayEffectSuccessContext success = default;
            SimulationActionTargetSnapshot successTarget = default;
            ulong sourceTick = m_Frame.Tick.Value;
            ulong resultId = 0;
            if (!successEvent.IsEmpty)
            {
                if (!m_Frame.Input.TryReadActionEvent(m_Frame.ActorId, in action, successEvent,
                        out resultId, out sourceTick, out successTarget))
                    return false;
                success = new GameplayEffectSuccessContext(
                    GetStringConstant(operation, OperationNamedConstant.SuccessEventTag, string.Empty),
                    resultId, sourceTick, m_Frame.Tick.Value, action.SkillId.Value, action.InstanceId,
                    action.SkillExecutionGeneration, action.SegmentGeneration);
            }
            GameplayEffectScope scope = effect.Scope == GameplayEffectScopeKind.Actor
                ? GameplayEffectScope.Actor : GameplayEffectScope.Action(actionInstanceId, executionGeneration);
            GameplayEffectHolderIdentity holder = effect.Lifetime switch
            {
                GameplayEffectLifetimeKind.Independent => GameplayEffectHolderIdentity.Independent,
                GameplayEffectLifetimeKind.Action => GameplayEffectHolderIdentity.Action(actionInstanceId, executionGeneration),
                GameplayEffectLifetimeKind.Invocation => ClipHolder(m_Frame.TreeClipInvocation, executionGeneration),
                _ => throw new InvalidOperationException("Gameplay Effect lifetime is unsupported.")
            };
			var context = new SimulationGameplayEffectContext(
				m_Frame.ActorId,
				m_Frame.ActorId,
				actionInstanceId,
				predictionKey,
                resultId,
                sourceTick,
                predicted ? SimulationGameplayEffectApplicationMode.Predicted : SimulationGameplayEffectApplicationMode.Confirmed,
                scope, holder, success, successTarget);
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

        static GameplayEffectHolderIdentity ClipHolder(in AbilityTreeClipInvocation invocation, ulong executionGeneration) =>
            GameplayEffectHolderIdentity.Invocation(invocation.ActionInstanceId, executionGeneration,
                invocation.TimelineRuntimeHandle, invocation.ClipAuthoringId, invocation.PlaybackGeneration,
                invocation.BranchRevision, invocation.Cycle);

        public bool ReleaseTreeClipEffects(in AbilityTreeClipInvocation invocation)
        {
            if (!m_Actions.TryGetInstance(invocation.ActionInstanceId, out Float32ActionInstanceState action))
                throw new InvalidOperationException("TreeClip effect holder has no owning Action instance.");
            GameplayEffectHolderIdentity holder = ClipHolder(invocation, action.SkillExecutionGeneration);
            if (!m_GameplayEffects.HasHeldEffects(holder))
                return true;
            if (!ReceiveCommandBatch(1, SimulationExecutionSource.FromGameplayEffect()).Accepted)
                return false;
            var request = new GameplayEffectRemoveRequest<PortableTagQuery>(GameplayEffectRemoveSelector.Holder,
                0, default, default, null, holder);
            GameplayEffectRemoveResult result = m_GameplayEffects.Remove(request);
            RecordChanges(new ActivationId(SimulationExecutionSource.FromGameplayEffect(), invocation.PlaybackGeneration),
                "timeline:release-effects");
            return result.Kind == GameplayEffectRemoveResultKind.Removed || result.Kind == GameplayEffectRemoveResultKind.NoMatch;
        }

        public GameplayEffectRemoveResult ReleaseActionEffects(ulong actionInstanceId)
        {
            if (!m_GameplayEffects.HasHeldEffects(default, actionInstanceId))
                return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.NoMatch, default);
            if (!ReceiveCommandBatch(1, SimulationExecutionSource.FromGameplayEffect()).Accepted)
                return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.NotReceived, default);
            var request = new GameplayEffectRemoveRequest<PortableTagQuery>(GameplayEffectRemoveSelector.HeldByAction,
                0, default, default, null, holderActionInstanceId: actionInstanceId);
            return m_GameplayEffects.Remove(request);
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
			if (!m_GameplayEffects.TryGetAttribute(operation.Text0, out Float32Scalar baseValue, out Float32Scalar currentValue, out _))
			{
				if (CanonicalTextValue.Equals(outputPort, "m_Valid", StringComparison.Ordinal))
					return AbilityStateValue.FromBoolean(false);
				return AbilityStateValue.FromScalar(Float32Scalar.Zero);
			}
			if (CanonicalTextValue.Equals(outputPort, "m_Valid", StringComparison.Ordinal))
				return AbilityStateValue.FromBoolean(true);
			if (CanonicalTextValue.Equals(outputPort, "m_BaseValue", StringComparison.Ordinal))
				return AbilityStateValue.FromScalar(baseValue);
			if (CanonicalTextValue.Equals(outputPort, "m_CurrentValue", StringComparison.Ordinal))
				return AbilityStateValue.FromScalar(currentValue);
			throw new InvalidOperationException($"Gameplay Attribute output port '{outputPort}' is unknown.");
		}

		void RecordChanges(SimulationOperation source, CanonicalTextValue cause)
		{
			if (m_GameplayEffects.PendingChanges.Length != 0)
				RecordChanges(m_Facts.Activation(source), cause);
		}

		void RecordChanges(ActivationId source, CanonicalTextValue cause)
		{
			ReadOnlySpan<PortableEffectRuntimeChange> changes = m_GameplayEffects.PendingChanges;
			try
			{
				for (int i = 0; i < changes.Length; i++)
				{
					ref readonly PortableEffectRuntimeChange change = ref changes[i];
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

