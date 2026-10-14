using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
	internal sealed class FixedEquipmentRuntime : FixedOperationModule,
		IEquipmentRuntimePort,
		IEquipmentActionContextProvider
	{
		readonly FixedAbilityExecutionFrame m_Frame;
		readonly FixedActionStateStore m_Actions;
		readonly FixedHandleAllocator m_Handles;
		readonly FixedGameplayEffectOperationRuntime m_GameplayEffects;
		readonly FixedFactSink m_Facts;
        readonly FixedTraceSink m_Trace;
        readonly EquipmentProgramLayout m_EquipmentLayout;
        readonly EquipmentRuntimeControl m_Control;
		readonly Dictionary<int, EquipmentChangeOutcome> m_Outcomes = new Dictionary<int, EquipmentChangeOutcome>();
		readonly MutationScope m_MutationScope;
		public FixedEquipmentRuntime(
			FixedGameplayAbilityExecutionAccess access,
			FixedAbilityExecutionFrame frame,
			FixedActionStateStore actions,
			FixedHandleAllocator handles,
            FixedGameplayEffectOperationRuntime gameplayEffects,
            FixedFactSink facts,
            FixedTraceSink trace,
			EquipmentProgramLayout equipmentLayout)
			: base(access)
		{
			m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
			m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
			m_Handles = handles;
			m_GameplayEffects = gameplayEffects;
            m_Facts = facts;
            m_Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            m_EquipmentLayout = equipmentLayout ?? throw new ArgumentNullException(nameof(equipmentLayout));
			m_Control = new EquipmentRuntimeControl(this);
			m_MutationScope = new MutationScope(m_Ability.StateSlots.Count);
		}

		public bool TryReadActionContext(EquipmentActionRouteId routeId, out EquipmentActionContext context)
		{
			context = default;
			if (!m_EquipmentLayout.CapabilityEnabled || !routeId.IsValid)
				return false;
			EquipmentProgramRoute route = m_EquipmentLayout.RequireRoute(routeId);
			EquipmentSlotState slot = ReadState().RequireSlot(route.OwnerSlotId);
			if (!slot.IsEquipped)
				return false;
			if (!m_EquipmentLayout.TryGetRouteImplementation(slot.FeatureId, routeId, out _))
			{
				if (route.MissingImplementation == EquipmentRouteMissingImplementation.RejectComposition)
					throw new InvalidOperationException($"Equipment Route '{routeId}' has no implementation for Feature '{slot.FeatureId}'.");
				return false;
			}
			context = slot.ActionContext(routeId);
			return context.IsValid;
		}

		public bool HasActionRoute(EquipmentActionRouteId routeId)
		{
			if (!m_EquipmentLayout.CapabilityEnabled || !routeId.IsValid)
				return false;
			return m_EquipmentLayout.HasRoute(routeId);
		}

		public bool IsAbilityBinding(EquipmentActionContext context, CharacterSkillId abilityId)
		{
            if (!context.IsValid || !abilityId.IsValid || !IsCurrentActionContext(context))
				return false;
			return m_EquipmentLayout.TryGetRouteImplementation(context.FeatureId, context.RouteId, out EquipmentProgramRouteImplementation implementation) &&
				implementation.AbilityId == abilityId;
		}

		public bool IsCurrentActionContext(EquipmentActionContext context)
		{
			if (!context.IsValid || !m_EquipmentLayout.TryGetRouteImplementation(context.FeatureId, context.RouteId, out _))
				return false;
			EquipmentProgramRoute route = m_EquipmentLayout.RequireRoute(context.RouteId);
			EquipmentSlotState slot = ReadState().RequireSlot(route.OwnerSlotId);
			return slot.IsEquipped &&
				slot.SlotId == context.SlotId &&
				slot.EquipmentId == context.EquipmentId &&
				slot.FeatureId == context.FeatureId &&
				slot.Revision == context.EquipmentRevision;
		}

		public void BeginEvaluation()
		{
			m_Outcomes.Clear();
			if (!m_EquipmentLayout.CapabilityEnabled)
				return;
			SimulationOperation source = m_Ability.Operations[m_Layout.RootOperation.Value];
			m_Control.PrepareEvaluation(source.Handle);
			TraceSnapshot(source);
		}

		public void EndEvaluation()
		{
			if (!m_EquipmentLayout.CapabilityEnabled)
				return;
			SimulationOperation source = m_Ability.Operations[m_Layout.RootOperation.Value];
			m_Control.CancelOrphanedPending(source.Handle);
		}

		public AbilityStateValue Evaluate(SimulationOperation operation, string outputPort, FixedValueInputLease inputs)
		{
			switch (operation.Code)
			{
				case SimulationOperationCode.ReadEquipmentIdentity:
				{
					EquipmentSlotState slot = RequireSlotState(operation);
					if (string.Equals(outputPort, "m_Equipment", StringComparison.Ordinal) ||
						string.Equals(outputPort, "m_Output", StringComparison.Ordinal))
						return AbilityStateValue.FromIdentity(slot.EquipmentId.Value ?? string.Empty);
					if (string.Equals(outputPort, "m_Feature", StringComparison.Ordinal))
						return AbilityStateValue.FromIdentity(slot.FeatureId.Value ?? string.Empty);
					if (string.Equals(outputPort, "m_Revision", StringComparison.Ordinal))
						return AbilityStateValue.FromUInt64(slot.Revision);
					if (string.Equals(outputPort, "m_Equipped", StringComparison.Ordinal))
						return AbilityStateValue.FromBoolean(slot.IsEquipped);
					throw new InvalidOperationException($"Equipment identity output '{outputPort}' is unknown.");
				}
				case SimulationOperationCode.ReadEquipmentParameter:
					return ReadParameter(operation, inputs);
				case SimulationOperationCode.RequestEquipmentChange:
				case SimulationOperationCode.BeginEquipmentChange:
				case SimulationOperationCode.CommitEquipmentChange:
				case SimulationOperationCode.CancelEquipmentChange:
					return ReadOutcome(operation, outputPort);
				default:
					throw new InvalidOperationException($"Operation '{operation.Code}' is not an Equipment value operation.");
			}
		}

		public bool Execute<TTarget>(OperationControlCursor<TTarget> cursor, SimulationOperation operation, FixedValueInputLease inputs)
			where TTarget : struct, IOperationControlTarget<TTarget>
		{
			switch (operation.Code)
			{
				case SimulationOperationCode.RequestEquipmentChange:
					return Capture(operation, m_Control.Validate(BuildChangeRequest(operation, inputs))).Succeeded;
				case SimulationOperationCode.BeginEquipmentChange:
					return Capture(operation, m_Control.Begin(BuildChangeRequest(operation, inputs))).Succeeded;
				case SimulationOperationCode.CommitEquipmentChange:
				{
					EquipmentChangeOutcome outcome = m_Control.Commit(
						operation.Handle,
						new EquipmentChangeId(ReadUInt64(inputs)));
					return Capture(operation, outcome).Succeeded;
				}
				case SimulationOperationCode.CancelEquipmentChange:
				{
					EquipmentChangeOutcome outcome = m_Control.Cancel(
						operation.Handle,
						new EquipmentChangeId(ReadUInt64(inputs)));
					return Capture(operation, outcome).Succeeded;
				}
				default:
					throw new InvalidOperationException($"Operation '{operation.Code}' is not an Equipment execution operation.");
			}
		}


		AbilityStateValue ReadParameter(SimulationOperation operation, FixedValueInputLease inputs)
		{
			EquipmentSlotState slot = RequireSlotState(operation);
			ulong expectedRevision = ReadUInt64(inputs);
			if (!slot.IsEquipped || expectedRevision == 0)
				throw new InvalidOperationException($"Equipment parameter operation '{SourcePath(operation)}' requires an explicit Slot revision.");
			if (slot.Revision != expectedRevision)
				throw new InvalidOperationException($"Equipment parameter operation '{SourcePath(operation)}' revision is stale.");
			EquipmentParameterId parameterId = m_EquipmentLayout.RequireOperationParameter(operation.Handle);
			EquipmentProgramParameter parameter = m_EquipmentLayout.RequireParameter(slot.EquipmentId, parameterId);
			if (parameter.FeatureId != slot.FeatureId)
				throw new InvalidOperationException($"Equipment parameter '{parameterId}' does not belong to Feature '{slot.FeatureId}'.");
			return ToStateValue(parameter.Value);
		}

		EquipmentChangeRequest BuildChangeRequest(SimulationOperation operation, FixedValueInputLease inputs)
		{
			EquipmentProgramSlot slot = RequireSlot(operation);
			EquipmentId target = TryRequireItem(operation, out EquipmentProgramItem item) ? item.EquipmentId : default;
			EquipmentSlotState current = ReadState().RequireSlot(slot.SlotId);
			ulong expectedRevision = ReadUInt64(inputs);
			if (expectedRevision == 0)
				throw new InvalidOperationException($"Equipment change operation '{SourcePath(operation)}' requires an explicit revision.");
			ulong actionInstanceId = 0;
			string actionContext = GetStringConstant(operation, OperationNamedConstant.ActionContext, string.Empty);
			if (!string.IsNullOrEmpty(actionContext) && m_Actions.FindActive(actionContext, out FixedActionInstanceState action) >= 0)
				actionInstanceId = action.InstanceId;
			return new EquipmentChangeRequest(slot.SlotId, target, expectedRevision, actionInstanceId);
		}

		static ulong ReadUInt64(FixedValueInputLease inputs)
		{
			AbilityStateValue value = inputs.FindByKind(ProgramStateValueKind.UInt64);
			return value.Kind == ProgramStateValueKind.UInt64 ? value.UInt64 : 0;
		}

		AbilityStateValue ReadOutcome(SimulationOperation operation, string outputPort)
		{
			if (!m_Outcomes.TryGetValue(operation.Handle.Value, out EquipmentChangeOutcome outcome))
				throw new InvalidOperationException($"Equipment change operation '{SourcePath(operation)}' has not executed in the current evaluation.");
			if (string.Equals(outputPort, "m_Accepted", StringComparison.Ordinal) ||
				string.Equals(outputPort, "m_Begun", StringComparison.Ordinal) ||
				string.Equals(outputPort, "m_Committed", StringComparison.Ordinal) ||
				string.Equals(outputPort, "m_Cancelled", StringComparison.Ordinal))
				return AbilityStateValue.FromBoolean(outcome.Succeeded);
			if (string.Equals(outputPort, "m_ChangeId", StringComparison.Ordinal))
				return AbilityStateValue.FromUInt64(outcome.ChangeId.Value);
			if (string.Equals(outputPort, "m_Failure", StringComparison.Ordinal))
				return AbilityStateValue.FromInt32((int)outcome.Failure);
			throw new InvalidOperationException($"Equipment change output '{outputPort}' is unknown.");
		}

		EquipmentChangeOutcome Capture(SimulationOperation operation, EquipmentChangeOutcome outcome)
		{
			m_Outcomes[operation.Handle.Value] = outcome;
			TraceOutcome(operation, outcome);
			return outcome;
		}

		EquipmentProgramSlot RequireSlot(SimulationOperation operation)
		{
			return m_EquipmentLayout.RequireSlot(m_EquipmentLayout.RequireOperationSlot(operation.Handle));
		}

		EquipmentSlotState RequireSlotState(SimulationOperation operation) => ReadState().RequireSlot(RequireSlot(operation).SlotId);

		bool TryRequireItem(SimulationOperation operation, out EquipmentProgramItem item)
		{
			if (!m_EquipmentLayout.TryGetOperationEquipment(operation.Handle, out EquipmentId equipmentId))
			{
				item = null;
				return false;
			}
			item = m_EquipmentLayout.RequireItem(equipmentId);
			return true;
		}

		void TraceOutcome(SimulationOperation operation, EquipmentChangeOutcome outcome)
		{
			if (m_Trace.Enabled)
				m_Trace.Add(operation, "equipment_change", outcome.Succeeded ? SimulationTraceSeverity.Information : SimulationTraceSeverity.Warning, $"{operation.Code}:success={outcome.Succeeded}:change={outcome.ChangeId.Value}:failure={outcome.Failure}");
		}

		void TraceSnapshot(SimulationOperation source)
		{
			if (!m_Trace.Enabled)
				return;
			EquipmentStateAggregate aggregate = ReadState();
			for (int i = 0; i < aggregate.Slots.Count; i++)
			{
				EquipmentSlotState slot = aggregate.Slots[i];
				m_Trace.Add(source, "equipment_snapshot", SimulationTraceSeverity.Detail, $"slot={slot.SlotId}:equipment={slot.EquipmentId}:feature={slot.FeatureId}:revision={slot.Revision}:generation={slot.Generation}:tagSource={slot.TagSource}:effects={string.Join(",", slot.PassiveEffectHandles)}");
			}
			PendingEquipmentChange pending = aggregate.PendingChange;
			if (pending.IsValid)
				m_Trace.Add(source, "equipment_snapshot", SimulationTraceSeverity.Detail, $"pending={pending.ChangeId}:{pending.SlotId}:{pending.FromEquipmentId}->{pending.ToEquipmentId}:action={pending.SourceActionInstanceId}:begin={pending.BeginTick}");
			PendingEquipmentChange resolved = aggregate.LastResolvedChange;
			if (resolved.IsValid)
				m_Trace.Add(source, "equipment_snapshot", SimulationTraceSeverity.Detail, $"resolved={resolved.ChangeId}:{resolved.State}:{resolved.SlotId}:{resolved.FromEquipmentId}->{resolved.ToEquipmentId}:begin={resolved.BeginTick}:tick={resolved.ResolvedTick}");
		}

		static AbilityStateValue ToStateValue(EquipmentRuntimeParameterValue value) => value.Kind switch
		{
			EquipmentParameterValueKind.Boolean => AbilityStateValue.FromBoolean(value.Boolean),
			EquipmentParameterValueKind.Int32 => AbilityStateValue.FromInt32(value.Int32),
			EquipmentParameterValueKind.Scalar => AbilityStateValue.FromScalar(FixedScalar.FromDouble(value.X)),
			EquipmentParameterValueKind.Vector2 => AbilityStateValue.FromVector2(new FixedVector2(FixedScalar.FromDouble(value.X), FixedScalar.FromDouble(value.Y))),
			EquipmentParameterValueKind.Vector3 => AbilityStateValue.FromVector3(new FixedVector3(FixedScalar.FromDouble(value.X), FixedScalar.FromDouble(value.Y), FixedScalar.FromDouble(value.Z))),
			EquipmentParameterValueKind.Yaw => AbilityStateValue.FromYaw(new FixedYaw(FixedScalar.FromDouble(value.X))),
			EquipmentParameterValueKind.GameplayTag => AbilityStateValue.FromIdentity(value.Identity),
			EquipmentParameterValueKind.GameplayEffect => AbilityStateValue.FromIdentity(value.Identity),
			EquipmentParameterValueKind.AnimationProducer => AbilityStateValue.FromIdentity(value.Identity),
			_ => throw new InvalidOperationException($"Equipment parameter kind '{value.Kind}' is unsupported.")
		};

		ActorId IEquipmentRuntimePort.ActorId => m_Frame.ActorId;
		ulong IEquipmentRuntimePort.Tick => m_Frame.Tick.Value;
		EquipmentProgramLayout IEquipmentRuntimePort.Layout => m_EquipmentLayout;
		public EquipmentStateAggregate ReadState() => m_Frame.ReadEquipmentState();
		public void WriteState(EquipmentStateAggregate state) => m_Frame.EquipmentState.SetEquipmentState(state);
		EquipmentChangeId IEquipmentRuntimePort.AllocateChangeId() => new EquipmentChangeId(m_Handles.Next());
		bool IEquipmentRuntimePort.HasActiveActionConflict(EquipmentSlotState slot, ulong sourceActionInstanceId)
		{
			for (int index = 0; index < m_Actions.ActionInstances.Count; index++)
			{
				FixedActionInstanceState active = m_Actions.ActionInstances[index];
				if (active.IsActive && active.InstanceId != sourceActionInstanceId && active.EquipmentContext.IsValid &&
					active.EquipmentContext.SlotId == slot.SlotId && active.EquipmentContext.EquipmentRevision == slot.Revision)
					return true;
			}
			return false;
		}
		bool IEquipmentRuntimePort.IsActionActive(ulong actionInstanceId)
		{
			return m_Actions.TryGetInstance(actionInstanceId, out FixedActionInstanceState action) && action.IsActive;
		}
		void IEquipmentRuntimePort.ResetLocalState(EquipmentFeatureId featureId, EquipmentLocalStateId stateId)
		{
			EquipmentProgramLocalState localState = m_EquipmentLayout.RequireLocalState(featureId, stateId);
			m_Frame.EquipmentState.SetEquipmentState(
				m_Frame.ReadEquipmentState().WithLocalState(featureId, stateId, localState.DefaultValue));
		}
		EquipmentPassiveHandles IEquipmentRuntimePort.AcquirePassiveHandles(EquipmentFeatureId featureId) => ReadState().Storage.AcquirePassiveHandles(featureId);
		void IEquipmentRuntimePort.ReleasePassiveHandles(in EquipmentPassiveHandles handles) => ReadState().Storage.ReleasePassiveHandles(handles);
		GameplayEffectApplyResult IEquipmentRuntimePort.SetTags(GameplayTagSourceIdentityValue sourceId, IReadOnlyList<string> tags) => RequireGameplayEffects().SetEquipmentTags(sourceId, tags);
		void IEquipmentRuntimePort.RemoveTags(GameplayTagSourceIdentityValue sourceId) => RequireGameplayEffects().RemoveEquipmentTags(sourceId);
		GameplayEffectApplyResult IEquipmentRuntimePort.ApplyPassiveEffect(string effectId) => RequireGameplayEffects().ApplyEquipmentPassive(effectId);
		GameplayEffectRemoveResult IEquipmentRuntimePort.RemovePassiveEffect(ulong handle) => RequireGameplayEffects().RemoveEquipmentPassive(handle);
		void IEquipmentRuntimePort.EmitContributionFailure(OperationHandle source, GameplayEffectExecutionFailure failure) =>
			RequireGameplayEffects().ReportEquipmentFailure(RequireSource(source), failure);
        GameplayCommandBatchAdmission IEquipmentRuntimePort.ReceiveGameplayCommands(OperationHandle source, int commands)
        {
            SimulationOperation operation = RequireSource(source);
            return RequireGameplayEffects().ReceiveCommandBatch(commands,
                SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)));
        }

		IEquipmentMutationScope IEquipmentRuntimePort.BeginMutation()
		{
			return m_MutationScope.Begin(this, m_Frame, m_Frame.SavepointPort);
		}
		GameplayEffectApplyResult IEquipmentRuntimePort.CommitEffectOutputs(OperationHandle source) => RequireGameplayEffects().CommitEquipmentMutation(RequireSource(source));
		void IEquipmentRuntimePort.CancelEffectOutputs() => RequireGameplayEffects().CancelEquipmentMutation();
		void IEquipmentRuntimePort.EmitLifecycle(OperationHandle source, EquipmentSlotState before, EquipmentSlotState after, PendingEquipmentChangeState state, EquipmentChangeId changeId)
		{
			SimulationEventHeader header = m_Facts.Next(RequireSource(source));
			m_Facts.Add(new GameplayFact(header, new EquipmentLifecycleFact(after.SlotId,
                before.EquipmentId, after.EquipmentId, before.Revision, after.Revision, changeId, state)));
		}

		SimulationOperation RequireSource(OperationHandle source)
		{
			if (!source.IsValid || source.Value >= m_Ability.Operations.Count)
				throw new InvalidOperationException($"Equipment source Operation '{source}' is absent from the Program.");
			SimulationOperation operation = m_Ability.Operations[source.Value];
			if (!operation.Handle.Equals(source))
				throw new InvalidOperationException($"Equipment source Operation '{source}' does not match Program order.");
			return operation;
		}

		FixedGameplayEffectOperationRuntime RequireGameplayEffects() => m_GameplayEffects ??
			throw new InvalidOperationException("Fixed Equipment operation requires the Gameplay Effect service.");

		sealed class MutationScope : IEquipmentMutationScope
		{
			FixedEquipmentRuntime m_Owner;
			FixedAbilityExecutionFrame m_Frame;
			IFixedAbilityExecutionSavepointPort m_SavepointPort;
			FixedAbilityExecutionSavepoint m_Savepoint;
			FixedAbilityOutputSavepoint m_OutputSavepoint;
			readonly AbilityStateValue[] m_Values;
			bool m_Completed;

			public MutationScope(int stateSlotCount)
			{
				m_Values = new AbilityStateValue[stateSlotCount];
			}

			public MutationScope Begin(
				FixedEquipmentRuntime owner,
				FixedAbilityExecutionFrame frame,
				IFixedAbilityExecutionSavepointPort savepointPort)
			{
				m_Owner = owner;
				m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
				m_SavepointPort = savepointPort ?? throw new ArgumentNullException(nameof(savepointPort));
				m_Savepoint = m_SavepointPort.CreateSavepoint();
				m_OutputSavepoint = frame.CreateOutputSavepoint();
				for (int i = 0; i < m_Values.Length; i++)
					m_Values[i] = frame.SkillState.Get(i);
				m_Completed = false;
				return this;
			}

			public void Complete()
			{
				if (m_Completed)
					throw new InvalidOperationException("Equipment mutation scope is already completed.");
				m_SavepointPort.Release(m_Savepoint);
				m_Completed = true;
				Return();
			}

			public void Dispose()
			{
				if (m_Completed)
					return;
				m_SavepointPort.Restore(m_Savepoint);
				for (int i = 0; i < m_Values.Length; i++)
					m_Frame.SkillState.Set(i, m_Values[i]);
				m_Frame.RestoreOutput(m_OutputSavepoint);
				m_Owner.RequireGameplayEffects().CancelEquipmentMutation();
				m_Completed = true;
				Return();
			}

			void Return()
			{
				Array.Clear(m_Values, 0, m_Values.Length);
				m_Owner = null;
				m_Frame = null;
				m_SavepointPort = null;
				m_Savepoint = default;
				m_OutputSavepoint = default;
			}
		}
	}
}
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        

