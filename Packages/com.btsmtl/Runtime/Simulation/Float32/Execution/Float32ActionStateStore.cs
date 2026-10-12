using System;
using System.Collections.Generic;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation
{
	internal readonly struct Float32ActionInstanceState : IEquatable<Float32ActionInstanceState>
    {
		public Float32ActionInstanceState(
			string actionId,
			CharacterSkillId skillId,
			OperationHandle skillEntryOperation,
			ulong skillExecutionGeneration,
			CanonicalTextValue contextId,
            ulong instanceId,
            ulong predictionKey,
            CanonicalTextValue sourceInputRequestId,
            ulong inputSequence,
            ulong startTick,
            CanonicalTextValue targetKey,
            SimulationActionTargetSnapshot targetSnapshot,
            SimulationExecutionSource source,
            SimulationActionPhase phase,
            SimulationActionState state,
            SimulationActionLifecycleTransitionType lastTransition,
            ulong lastTransitionTick,
            ulong lastTransitionSourceTick,
            CanonicalTextValue reason,
            EquipmentActionContext equipmentContext = default,
            ulong segmentGeneration = 0,
            string activationEntryId = "")
        {
			ActionId = actionId ?? string.Empty;
			SkillId = skillId;
			SkillEntryOperation = skillEntryOperation;
			SkillExecutionGeneration = skillExecutionGeneration;
            ContextId = contextId.IsNull ? CanonicalTextValue.Empty : contextId;
            InstanceId = instanceId;
            PredictionKey = predictionKey;
            SourceInputRequestId = sourceInputRequestId.IsNull ? CanonicalTextValue.Empty : sourceInputRequestId;
            InputSequence = inputSequence;
            StartTick = startTick;
            TargetKey = targetKey.IsNull ? CanonicalTextValue.Empty : targetKey;
            TargetSnapshot = targetSnapshot;
            Source = source;
            Phase = phase;
            State = state;
            LastTransition = lastTransition;
            LastTransitionTick = lastTransitionTick;
            LastTransitionSourceTick = lastTransitionSourceTick;
            Reason = reason.IsNull ? CanonicalTextValue.Empty : reason;
            EquipmentContext = equipmentContext;
            SegmentGeneration = segmentGeneration;
            ActivationEntryId = activationEntryId ?? string.Empty;
        }

		public string ActionId { get; }
		public CharacterSkillId SkillId { get; }
		public OperationHandle SkillEntryOperation { get; }
		public ulong SkillExecutionGeneration { get; }
		public ulong SegmentGeneration { get; }
        public GameplayAbilitySourceContext SourceContext => new GameplayAbilitySourceContext(
            SkillId, InstanceId, SkillExecutionGeneration, SegmentGeneration);
        public string ActivationEntryId { get; }
        public CanonicalTextValue ContextId { get; }
        public ulong InstanceId { get; }
        public ulong PredictionKey { get; }
        public CanonicalTextValue SourceInputRequestId { get; }
        public ulong InputSequence { get; }
        public ulong StartTick { get; }
        public CanonicalTextValue TargetKey { get; }
        public SimulationActionTargetSnapshot TargetSnapshot { get; }
        public SimulationExecutionSource Source { get; }
        public SimulationActionPhase Phase { get; }
        public SimulationActionState State { get; }
        public SimulationActionLifecycleTransitionType LastTransition { get; }
        public ulong LastTransitionTick { get; }
        public ulong LastTransitionSourceTick { get; }
		public CanonicalTextValue Reason { get; }
		public EquipmentActionContext EquipmentContext { get; }
		public bool Equals(Float32ActionInstanceState other) =>
			CanonicalTextValue.Equals(ActionId, other.ActionId, StringComparison.Ordinal) &&
			SkillId.Equals(other.SkillId) &&
			SkillEntryOperation.Equals(other.SkillEntryOperation) &&
			SkillExecutionGeneration == other.SkillExecutionGeneration &&
			SegmentGeneration == other.SegmentGeneration &&
            CanonicalTextValue.Equals(ActivationEntryId, other.ActivationEntryId, StringComparison.Ordinal) &&
			CanonicalTextValue.Equals(ContextId, other.ContextId, StringComparison.Ordinal) &&
			InstanceId == other.InstanceId &&
			PredictionKey == other.PredictionKey &&
			CanonicalTextValue.Equals(SourceInputRequestId, other.SourceInputRequestId, StringComparison.Ordinal) &&
			InputSequence == other.InputSequence &&
			StartTick == other.StartTick &&
			CanonicalTextValue.Equals(TargetKey, other.TargetKey, StringComparison.Ordinal) &&
			TargetSnapshot.Equals(other.TargetSnapshot) &&
			Source.Equals(other.Source) &&
			Phase == other.Phase &&
			State == other.State &&
			LastTransition == other.LastTransition &&
			LastTransitionTick == other.LastTransitionTick &&
			LastTransitionSourceTick == other.LastTransitionSourceTick &&
			CanonicalTextValue.Equals(Reason, other.Reason, StringComparison.Ordinal) &&
			EquipmentContext.Equals(other.EquipmentContext);
        public bool IsValid =>
            !CanonicalTextValue.IsNullOrEmpty(ActionId) &&
            !CanonicalTextValue.IsNullOrEmpty(ContextId) &&
            InstanceId != 0 &&
            PredictionKey != 0 &&
            InputSequence != 0 &&
			StartTick != 0 &&
			Source.IsValid &&
			(!Source.IsAbilityRule || SkillId.IsValid && SkillEntryOperation.IsValid);
        public bool IsTerminal =>
            State == SimulationActionState.Rejected ||
            State == SimulationActionState.Cancelled ||
            State == SimulationActionState.Interrupted ||
            State == SimulationActionState.Aborted ||
            State == SimulationActionState.Ended;
        public bool IsActive => IsValid && !IsTerminal;

        public Float32ActionInstanceState WithLifecycle(
            SimulationActionPhase phase,
            SimulationActionState state,
            SimulationActionLifecycleTransitionType transition,
            ulong transitionTick,
            ulong sourceTick,
            CanonicalTextValue reason)
        {
			return new Float32ActionInstanceState(
				ActionId,
				SkillId,
				SkillEntryOperation,
				SkillExecutionGeneration,
				ContextId,
                InstanceId,
                PredictionKey,
                SourceInputRequestId,
                InputSequence,
                StartTick,
                TargetKey,
                TargetSnapshot,
                Source,
                phase,
                state,
                transition,
                transitionTick,
                sourceTick,
                reason,
				EquipmentContext,
            SegmentGeneration, ActivationEntryId);
		}

		public Float32ActionInstanceState WithSegmentGeneration(ulong segmentGeneration)
		{
			return new Float32ActionInstanceState(
				ActionId,
				SkillId,
				SkillEntryOperation,
				SkillExecutionGeneration,
				ContextId,
				InstanceId,
				PredictionKey,
				SourceInputRequestId,
				InputSequence,
				StartTick,
				TargetKey,
				TargetSnapshot,
				Source,
				Phase,
				State,
				LastTransition,
				LastTransitionTick,
				LastTransitionSourceTick,
				Reason,
				EquipmentContext,
				segmentGeneration, ActivationEntryId);
		}

        public Float32ActionInstanceState WithPhase(SimulationActionPhase phase) =>
            WithLifecycle(phase, State, LastTransition, LastTransitionTick, LastTransitionSourceTick, Reason);

		public Float32ActionInstanceState WithSkillExecution(OperationHandle entryOperation, ulong generation)
		{
			return new Float32ActionInstanceState(
				ActionId,
				SkillId,
				entryOperation,
				generation,
				ContextId,
				InstanceId,
				PredictionKey,
				SourceInputRequestId,
				InputSequence,
				StartTick,
				TargetKey,
				TargetSnapshot,
				Source,
				Phase,
				State,
				LastTransition,
				LastTransitionTick,
				LastTransitionSourceTick,
				Reason,
				EquipmentContext,
            SegmentGeneration, ActivationEntryId);
		}

        public void Retain() { ContextId.Retain(); SourceInputRequestId.Retain(); TargetKey.Retain(); TargetSnapshot.Retain(); Source.Retain(); Reason.Retain(); EquipmentContext.SlotId.Retain(); }
        public void Release() { EquipmentContext.SlotId.Release(); Reason.Release(); Source.Release(); TargetSnapshot.Release(); TargetKey.Release(); SourceInputRequestId.Release(); ContextId.Release(); }
    }

    [CanonicalContract]
    internal readonly partial struct Float32ActionInstanceReference
    {
        public Float32ActionInstanceReference(
            string actionId,
            CanonicalTextValue contextId,
            ulong instanceId,
            ulong predictionKey,
            CharacterSkillId skillId = default,
            OperationHandle skillEntryOperation = default,
            ulong skillExecutionGeneration = 0)
        {
            if (skillId.IsValid != skillEntryOperation.IsValid || !skillId.IsValid && skillExecutionGeneration != 0)
                throw new ArgumentException("Action instance skill execution identity is incomplete.");
            ActionId = actionId ?? string.Empty;
            ContextId = contextId.IsNull ? CanonicalTextValue.Empty : contextId;
            InstanceId = instanceId;
            PredictionKey = predictionKey;
            SkillId = skillId;
            SkillEntryOperation = skillEntryOperation;
            SkillExecutionGeneration = skillExecutionGeneration;
        }

        [CanonicalDeclaredText]
        public string ActionId { get; }
        public CanonicalTextValue ContextId { get; }
        public ulong InstanceId { get; }
        public ulong PredictionKey { get; }
        public CharacterSkillId SkillId { get; }
        public OperationHandle SkillEntryOperation { get; }
        public ulong SkillExecutionGeneration { get; }
        public bool Equals(Float32ActionInstanceReference other) =>
            CanonicalTextValue.Equals(ActionId, other.ActionId, StringComparison.Ordinal) &&
            CanonicalTextValue.Equals(ContextId, other.ContextId, StringComparison.Ordinal) &&
            InstanceId == other.InstanceId &&
            PredictionKey == other.PredictionKey &&
            SkillId.Equals(other.SkillId) &&
            SkillEntryOperation.Equals(other.SkillEntryOperation) &&
            SkillExecutionGeneration == other.SkillExecutionGeneration;
        public bool HasSkillExecution => SkillId.IsValid && SkillEntryOperation.IsValid;
        public bool HasAnyIdentity =>
            !CanonicalTextValue.IsNullOrEmpty(ActionId) ||
            !CanonicalTextValue.IsNullOrEmpty(ContextId) ||
            InstanceId != 0 ||
            PredictionKey != 0 ||
            SkillId.IsValid ||
            SkillEntryOperation.IsValid ||
            SkillExecutionGeneration != 0;
        public bool IsTransientValid =>
            !CanonicalTextValue.IsNullOrEmpty(ActionId) &&
            !CanonicalTextValue.IsNullOrEmpty(ContextId) &&
            InstanceId != 0 &&
            PredictionKey != 0 &&
            (!HasSkillExecution && SkillExecutionGeneration == 0 || HasSkillExecution);
        public bool IsValid =>
            IsTransientValid &&
            (!HasSkillExecution || SkillExecutionGeneration != 0);

        public bool MatchesSkillExecution(Float32ActionInstanceState state) =>
            IsValid &&
            state.IsValid &&
            state.SkillId == SkillId &&
            state.SkillEntryOperation.Equals(SkillEntryOperation) &&
            (!HasSkillExecution || state.SkillExecutionGeneration == SkillExecutionGeneration);

        public bool MatchesTransientSkillExecution(Float32ActionInstanceState state) =>
            IsTransientValid &&
            state.IsValid &&
            state.SkillId == SkillId &&
            state.SkillEntryOperation.Equals(SkillEntryOperation) &&
            (SkillExecutionGeneration == 0 || state.SkillExecutionGeneration == SkillExecutionGeneration);

        public static Float32ActionInstanceReference FromInstance(Float32ActionInstanceState state)
        {
            return state.IsValid
                ? new Float32ActionInstanceReference(
                    state.ActionId,
                    state.ContextId,
                    state.InstanceId,
                    state.PredictionKey,
                    state.SkillId,
                    state.SkillEntryOperation,
                    state.SkillExecutionGeneration)
                : default;
        }

        public void Retain() { ContextId.Retain(); }
        public void Release() { ContextId.Release(); }
    }

	internal sealed class Float32ActionStateStore : Float32OperationModule, IFloat32ActionContextReader, IFloat32SkillExecutionStateAccess, IGameplayAbilityExecutionStorage<AbilityStateValue>
	{
		readonly Float32AbilityExecutionFrame m_Frame;
		Float32ActionInstanceReference? m_CurrentSkillExecution;
        readonly List<Float32ActionInstanceState> m_EvaluatedActions = new(8);
        readonly Dictionary<ulong, int> m_EvaluatedActionIndexes = new();
		readonly GameplayAbilityExecutionManager<AbilityStateValue> m_SkillExecution;
		readonly TraceExecutionScope m_TraceExecutionScope = new();

        public Float32ActionStateStore(
            Float32GameplayAbilityExecutionAccess access,
            Float32AbilityExecutionFrame frame)
            : base(access)
		{
			m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
			m_SkillExecution = new GameplayAbilityExecutionManager<AbilityStateValue>(this);
			m_Frame.BindSkillExecutionStateAccess(this);
		}

		public void BeginEvaluation()
		{
			if (m_CurrentSkillExecution.HasValue)
				throw new InvalidOperationException("Skill execution stack retained transient state across evaluations.");
			m_SkillExecution.BeginEvaluation();
		}

		public void EndEvaluation()
		{
			if (m_CurrentSkillExecution.HasValue)
				throw new InvalidOperationException("Skill execution stack has an unclosed runtime scope.");
			m_SkillExecution.EndEvaluation();
            m_EvaluatedActions.Clear();
            m_EvaluatedActionIndexes.Clear();
		}

		public IDisposable EnterSkillExecution(Float32ActionInstanceState action)
		{
			if (!action.IsValid || !action.SkillId.IsValid || !action.SkillEntryOperation.IsValid)
				throw new ArgumentException("Skill execution action identity is incomplete.", nameof(action));
            RetainEvaluatedAction(action);
			IDisposable execution = m_SkillExecution.Enter(new AbilityExecutionContext(
				action.SkillId,
				action.SkillEntryOperation,
				action.InstanceId,
				action.PredictionKey,
				action.SkillExecutionGeneration));
			return m_TraceExecutionScope.Begin(
				execution,
				m_Frame.PushActionTraceContext(action.InstanceId, action.SkillId, action.SkillEntryOperation));
		}

		public bool RemoveSkillExecution(ulong actionInstanceId)
		{
			return m_SkillExecution.Remove(actionInstanceId);
		}

		public bool IsContextActive(CanonicalTextValue contextId)
		{
			IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			for (int i = 0; i < actions.Count; i++)
			{
				Float32ActionInstanceState action = actions[i];
				if (action.IsActive && CanonicalTextValue.Equals(action.ContextId, contextId.IsNull ? CanonicalTextValue.Empty : contextId, StringComparison.Ordinal))
					return true;
			}
			return false;
		}

        public bool IsActivationEntry(string entryId) =>
            TryGetCurrentSkillExecution(out Float32ActionInstanceState action) &&
            CanonicalTextValue.Equals(action.ActivationEntryId, entryId, StringComparison.Ordinal);

        public void RequireActivationEntry(string entryId)
        {
            if (CanonicalTextValue.IsNullOrEmpty(entryId))
                return;
            for (int i = 0; i < m_Ability.Operations.Count; i++)
            {
                var operation = m_Ability.Operations[i];
                if (operation.Code == SimulationOperationCode.ActivationEntry &&
                    CanonicalTextValue.Equals(operation.Text0, entryId, StringComparison.Ordinal))
                    return;
            }
            throw new InvalidOperationException($"Ability has no activation entry '{entryId}'.");
        }
        public bool IsCurrentExecutionContextActive()
        {
            return TryGetCurrentSkillExecution(out Float32ActionInstanceState action) && action.IsActive;
        }

        public bool IsAbilityActive(CharacterSkillId abilityId)
        {
            IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].SkillId == abilityId && actions[i].IsActive)
                    return true;
            return false;
        }

		public bool IsSkillExecutionActive(ulong actionInstanceId) =>
			m_SkillExecution.IsActive(actionInstanceId);

		public bool HasSkillExecutionFrame(ulong actionInstanceId) =>
			m_SkillExecution.HasFrame(actionInstanceId);

		public bool IsAbilityCompleted(CharacterSkillId abilityId) => CompletedAbilityInstanceId(abilityId) != 0;

		public ulong CompletedAbilityInstanceId(CharacterSkillId abilityId)
		{
            if (m_Frame.AvailabilityRead != null)
            {
                ulong instance = 0, tick = 0;
                var snapshot = m_Frame.AvailabilityRead.Actions;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var action = snapshot[i];
                    if (action.SkillId == abilityId && action.State == SimulationActionState.Ended &&
                        (instance == 0 || action.LastTransitionTick > tick || action.LastTransitionTick == tick && action.InstanceId > instance))
                    { instance = action.InstanceId; tick = action.LastTransitionTick; }
                }
                return instance;
            }

			ulong result = 0;
			ulong resultTick = 0;
			IReadOnlyList<int> indices = m_Frame.ActionState.GetActionInstanceIndices(abilityId);
			IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			for (int i = 0; i < indices.Count; i++)
			{
				Float32ActionInstanceState action = actions[indices[i]];
				if (action.State != SimulationActionState.Ended)
					continue;
				if (result == 0 ||
					action.LastTransitionTick > resultTick ||
					action.LastTransitionTick == resultTick && action.InstanceId > result)
				{
					result = action.InstanceId;
					resultTick = action.LastTransitionTick;
				}
			}
			return result;
		}

		public int FindActive(CharacterSkillId skillId, out Float32ActionInstanceState state)
		{
            if (m_Frame.AvailabilityRead != null)
            {
                int queryFound = -1;
                state = default;
                var snapshot = m_Frame.AvailabilityRead.Actions;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var current = snapshot[i];
                    if (current.SkillId != skillId || !current.IsActive) continue;
                    if (queryFound >= 0) throw new InvalidOperationException("Skill resolves multiple Action instances.");
                    queryFound = i; state = current;
                }
                return queryFound;
            }

			int found = -1;
			state = default;
			IReadOnlyList<int> indices = m_Frame.ActionState.GetActionInstanceIndices(skillId);
			for (int i = 0; i < indices.Count; i++)
				MatchActive(indices[i], skillId, ref found, ref state);
			return found;
		}

		public bool TryGetActiveAbilityInstanceId(CharacterSkillId abilityId, out ulong instanceId)
		{
            if (m_Frame.AvailabilityRead != null)
            {
                instanceId = 0;
                var snapshot = m_Frame.AvailabilityRead.Actions;
                for (int i = 0; i < snapshot.Count; i++)
                    if (snapshot[i].SkillId == abilityId && snapshot[i].IsActive)
                    {
                        if (instanceId != 0) { instanceId = 0; return false; }
                        instanceId = snapshot[i].InstanceId;
                    }
                return instanceId != 0;
            }

			ulong found = 0;
			IReadOnlyList<int> indices = m_Frame.ActionState.GetActionInstanceIndices(abilityId);
			IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			for (int i = 0; i < indices.Count; i++)
			{
				Float32ActionInstanceState state = actions[indices[i]];
				if (!state.IsActive)
					continue;
				if (found != 0)
				{
					instanceId = 0;
					return false;
				}
				found = state.InstanceId;
			}
			instanceId = found;
			return found != 0;
		}

		public int FindCurrent(CharacterSkillId skillId, out Float32ActionInstanceState state)
		{
            if (m_Frame.AvailabilityRead != null)
            {
                int queryFound = -1;
                state = default;
                var snapshot = m_Frame.AvailabilityRead.Actions;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var current = snapshot[i];
                    if (current.SkillId != skillId) continue;
                    if (queryFound >= 0) throw new InvalidOperationException("Skill resolves multiple Action instances.");
                    queryFound = i; state = current;
                }
                return queryFound;
            }

			int found = -1;
			state = default;
			IReadOnlyList<int> indices = m_Frame.ActionState.GetActionInstanceIndices(skillId);
			IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			for (int i = 0; i < indices.Count; i++)
			{
				Float32ActionInstanceState candidate = actions[indices[i]];
				if (found >= 0)
					throw new InvalidOperationException($"Skill '{skillId}' resolves multiple Action instances.");
				found = indices[i];
				state = candidate;
			}
			return found;
		}

		public void CopyCurrentActions(CharacterSkillId skillId, List<Float32ActionInstanceState> results)
		{
            if (m_Frame.AvailabilityRead != null)
            {
                var snapshot = m_Frame.AvailabilityRead.Actions;
                for (int i = 0; i < snapshot.Count; i++)
                    if (snapshot[i].SkillId == skillId) results.Add(snapshot[i]);
                return;
            }

			IReadOnlyList<int> indices = m_Frame.ActionState.GetActionInstanceIndices(skillId);
			IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			for (int i = 0; i < indices.Count; i++)
				results.Add(actions[indices[i]]);
		}

		public IReadOnlyList<Float32ActionInstanceState> ActionInstances => m_Frame.ReadActionInstances();

		public Float32ActionInstanceState BindSkillExecution(
			Float32ActionInstanceState action,
			OperationHandle entryOperation,
			ulong generation)
		{
			if (!action.IsActive || !action.SkillId.IsValid || !entryOperation.IsValid || generation == 0)
				throw new ArgumentException("Skill execution binding is incomplete.", nameof(action));
			if (action.SkillEntryOperation.IsValid && !action.SkillEntryOperation.Equals(entryOperation))
				throw new InvalidOperationException($"Skill '{action.SkillId}' changed EntryOperation for Action instance '{action.InstanceId}'.");
			if (action.SkillExecutionGeneration != 0 && action.SkillExecutionGeneration != generation)
				throw new InvalidOperationException($"Skill '{action.SkillId}' changed execution generation for Action instance '{action.InstanceId}'.");
			m_SkillExecution.BindGeneration(action.InstanceId, generation);
			Float32ActionInstanceState next = action.WithSkillExecution(entryOperation, generation);
			WriteState(next);
            RetainEvaluatedAction(next);
			return next;
		}

		public SkillExecutionScope PushSkillExecution(Float32ActionInstanceState action)
		{
			if (!action.IsActive || !action.SkillId.IsValid || !action.SkillEntryOperation.IsValid)
				throw new ArgumentException("Skill execution owner is incomplete.", nameof(action));
			Float32ActionInstanceReference reference = Float32ActionInstanceReference.FromInstance(action);
			Float32ActionInstanceReference? previous = m_CurrentSkillExecution;
			m_CurrentSkillExecution = reference;
			return new SkillExecutionScope(
				this,
				reference,
				previous,
				m_Frame.PushActionTraceContext(action.InstanceId, action.SkillId, action.SkillEntryOperation));
		}

		public bool TryGetCurrentSkillExecution(out Float32ActionInstanceState action)
		{
            if (m_Frame.AvailabilityRead != null)
            {
                action = default;
                return m_Frame.SelectedSource.IsValid && m_Frame.TryReadAction(m_Frame.SelectedSource.InstanceId, out action);
            }

			if (!m_CurrentSkillExecution.HasValue)
			{
				action = default;
				return false;
			}
            action = RequireActiveTransient(m_CurrentSkillExecution.Value);
            return action.IsActive;
        }

        public int FindActive(CanonicalTextValue contextId, out Float32ActionInstanceState state)
        {
            int found = -1;
            state = default;
            IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
            if (!CanonicalTextValue.IsNullOrEmpty(contextId))
            {
                for (int i = 0; i < actions.Count; i++)
                    MatchActive(i, contextId, ref found, ref state);
            }
            else
            {
                for (int i = 0; i < actions.Count; i++)
                    MatchActive(i, string.Empty, ref found, ref state);
            }
            return found;
        }

		public Float32ActionInstanceState FindOnlyActive()
		{
			Float32ActionInstanceState result = default;
			IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			for (int i = 0; i < actions.Count; i++)
			{
				Float32ActionInstanceState current = actions[i];
				if (!current.IsActive)
                    continue;
                if (result.IsActive)
                    throw new InvalidOperationException("Action Context is ambiguous because multiple actions are active.");
                result = current;
            }
            return result;
        }

        public Float32ActionInstanceState RequireActive(Float32ActionInstanceState expected)
        {
            if (!expected.IsActive)
                return default;
            int slot = FindActive(expected.ContextId, out Float32ActionInstanceState current);
            return slot >= 0 && current.SourceContext.Equals(expected.SourceContext) ? current : default;
        }

		public Float32ActionInstanceState RequireActive(Float32ActionInstanceReference reference)
        {
            if (!reference.IsValid)
                return default;
			if (!TryGetInstance(reference.InstanceId, out Float32ActionInstanceState current))
				return default;
			return current.IsActive &&
				   CanonicalTextValue.Equals(current.ActionId, reference.ActionId, StringComparison.Ordinal) &&
                   CanonicalTextValue.Equals(current.ContextId, reference.ContextId, StringComparison.Ordinal) &&
                   current.PredictionKey == reference.PredictionKey &&
                   reference.MatchesSkillExecution(current)
				? current
				: default;
		}

		bool IFloat32SkillExecutionStateAccess.TryGet(int slotIndex, out AbilityStateValue value)
		{
			return m_SkillExecution.TryGet(slotIndex, out value);
		}

		bool IFloat32SkillExecutionStateAccess.TrySet(int slotIndex, AbilityStateValue value)
		{
			return m_SkillExecution.TrySet(slotIndex, value);
		}

		bool IFloat32SkillExecutionStateAccess.TryReset(int slotIndex)
		{
			return m_SkillExecution.TryReset(slotIndex);
		}

        public Float32ActionInstanceState RequireActiveTransient(Float32ActionInstanceReference reference)
        {
            if (!reference.IsTransientValid)
                return default;
			if (!TryGetInstance(reference.InstanceId, out Float32ActionInstanceState current))
				return default;
            return current.IsActive &&
                   CanonicalTextValue.Equals(current.ActionId, reference.ActionId, StringComparison.Ordinal) &&
                   CanonicalTextValue.Equals(current.ContextId, reference.ContextId, StringComparison.Ordinal) &&
                   current.PredictionKey == reference.PredictionKey &&
                   reference.MatchesTransientSkillExecution(current)
                ? current
                : default;
        }

		public bool ContainsInstance(ulong instanceId)
		{
			return m_Frame.TryReadAction(instanceId, out _);
		}

        public bool TryGetEvaluatedInstance(ulong instanceId, out Float32ActionInstanceState state)
        {
            if (m_EvaluatedActionIndexes.TryGetValue(instanceId, out int index))
            {
                state = m_EvaluatedActions[index];
                return true;
            }
            state = default;
            return false;
        }

        // Timeline motion resolves after lifecycle changes and may outlive the action's state slot.
        internal void RetainEvaluatedAction(Float32ActionInstanceState action)
        {
            if (m_EvaluatedActionIndexes.TryGetValue(action.InstanceId, out int index))
            {
                m_EvaluatedActions[index] = action;
                return;
            }
            m_EvaluatedActions.Add(action);
            m_EvaluatedActionIndexes.Add(action.InstanceId, m_EvaluatedActions.Count - 1);
        }

        public SimulationActionTargetSnapshot ReadTargetSnapshot(ulong instanceId)
        {
            if (!m_Frame.TryReadAction(instanceId, out Float32ActionInstanceState action))
                throw new InvalidOperationException("TreeClip Camera activation has no owning Action state.");
            return action.TargetSnapshot;
        }

		public bool TryGetInstance(ulong instanceId, out Float32ActionInstanceState state)
		{
			return m_Frame.TryReadAction(instanceId, out state);
		}

        public void WriteState(Float32ActionInstanceState action, GameplayAbilitySourceContext replacementSource = default)
        {
            if (TryFindInstanceIndex(action.InstanceId, out int existingIndex))
            {
                ReplaceAction(existingIndex, action);
                return;
            }
            int index = replacementSource.IsValid
                ? TryFindInstanceIndex(replacementSource.InstanceId, out int sourceIndex) ? sourceIndex : -1
                : FindEmptyInstanceIndex(action.ActionId);
			if (index < 0)
				throw new InvalidOperationException($"Action '{action.ActionId}' has no free instance state capacity.");
            IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			if (index < actions.Count)
			{
				Float32ActionInstanceState previous = actions[index];
				if (previous.IsValid && previous.IsTerminal)
					m_SkillExecution.Remove(previous.InstanceId);
				m_Frame.ActionState.ReplaceActionInstanceAt(index, action);
			}
			else
				m_Frame.ActionState.AddActionInstance(action);
        }

		void ReplaceAction(int index, Float32ActionInstanceState action)
		{
			m_Frame.ActionState.ReplaceActionInstanceAt(index, action);
		}

		public bool HasPendingRequest(string actionId)
		{
			IReadOnlyList<SimulationActionActivationRequestState> requests =
				m_Frame.ReadPendingRequests();
			for (int i = 0; i < requests.Count; i++)
			{
				SimulationActionActivationRequestState request = requests[i];
				if (request.IsValid && CanonicalTextValue.Equals(request.ActionId, actionId, StringComparison.Ordinal))
					return true;
			}
			return false;
		}

		public void StageRequest(SimulationActionActivationRequestState state)
		{
			IReadOnlyList<SimulationActionActivationRequestState> current = m_Frame.ReadPendingRequests();
			int count = 0;
			for (int i = 0; i < current.Count; i++)
				if (CanonicalTextValue.Equals(current[i].ActionId, state.ActionId, StringComparison.Ordinal))
					count++;
			if (count >= m_Layout.ActionCapacity(state.ActionId))
				throw new InvalidOperationException($"Action '{state.ActionId}' has no free activation request capacity.");
            m_Frame.ActionState.AddActivationRequest(state);
        }

		public int FindPendingSkill(
			CharacterSkillId skillId,
			out SimulationActionActivationRequestState request)
		{
			int found = -1;
			request = default;
			IReadOnlyList<SimulationActionActivationRequestState> requests = m_Frame.ReadPendingRequests();
			for (int i = 0; i < requests.Count; i++)
			{
				SimulationActionActivationRequestState candidate = requests[i];
				if (!candidate.IsValid || !candidate.Source.IsAbilityRule || candidate.SkillId != skillId)
					continue;
				if (found >= 0)
					throw new InvalidOperationException($"Skill '{skillId}' has multiple pending Action activation requests.");
				found = i;
				request = candidate;
			}
			return found;
		}

		public int FindPendingRequest(
			string actionId,
			CharacterSkillId skillId,
			OperationHandle entryOperation,
			CanonicalTextValue contextId,
			ulong inputSequence,
			ulong startTick,
            in GameplayAbilitySourceContext replacementSource)
		{
			IReadOnlyList<SimulationActionActivationRequestState> requests = m_Frame.ReadPendingRequests();
			for (int i = 0; i < requests.Count; i++)
			{
				SimulationActionActivationRequestState candidate = requests[i];
				if (candidate.IsValid && candidate.SkillId == skillId &&
					CanonicalTextValue.Equals(candidate.ActionId, actionId, StringComparison.Ordinal) &&
					candidate.SkillEntryOperation.Equals(entryOperation) &&
					CanonicalTextValue.Equals(candidate.ContextId, contextId, StringComparison.Ordinal) &&
                    candidate.InputSequence == inputSequence && candidate.StartTick == startTick &&
                    candidate.ReplacementSource.Equals(replacementSource))
					return i;
			}
			return -1;
		}

        public bool IsInputRequestPending(in SimulationInputRequestState input)
		{
			IReadOnlyList<SimulationActionActivationRequestState> requests = m_Frame.ReadPendingRequests();
			for (int i = 0; i < requests.Count; i++)
			{
                SimulationActionActivationRequestState pending = requests[i];
                if (pending.InputSequence == input.Sequence &&
                    pending.SourceInputRequestId.Equals(input.RequestId))
                    return true;
			}
            return false;
		}

		public void ClearRequestAt(int index)
		{
            m_Frame.ActionState.RemoveActivationRequestAt(index);
		}

		bool TryFindInstanceIndex(ulong instanceId, out int index)
		{
			return m_Frame.ActionState.TryFindActionInstanceIndex(instanceId, out index);
		}

		int FindEmptyInstanceIndex(string actionId)
		{
			IReadOnlyList<Float32ActionInstanceState> actions = m_Frame.ReadActionInstances();
			int count = 0;
			int terminalIndex = -1;
			for (int i = 0; i < actions.Count; i++)
			{
				Float32ActionInstanceState action = actions[i];
				if (action.ActionId == actionId)
					count++;
				if (action.ActionId == actionId && action.IsTerminal)
					return i;
				if (terminalIndex < 0 && action.IsTerminal)
					terminalIndex = i;
			}
			return count < m_Layout.ActionCapacity(actionId)
				? terminalIndex >= 0 ? terminalIndex : actions.Count
				: -1;
		}

		public ulong NextSequence()
		{
			return m_Frame.ActionState.NextActionEventSequence();
		}

		bool IGameplayAbilityExecutionStorage<AbilityStateValue>.IsAbilityStateSlot(int slotIndex) =>
			m_Layout.IsSkillExecutionStateSlot(slotIndex);

		bool IGameplayAbilityExecutionStorage<AbilityStateValue>.IsValueValid(
			int slotIndex,
			AbilityStateValue value) =>
			value.Kind == m_Ability.StateSlots[slotIndex].ValueKind;

		AbilityStateValue IGameplayAbilityExecutionStorage<AbilityStateValue>.DefaultValue(int slotIndex) =>
            Access.Services.DefaultValue(slotIndex);

		GameplayAbilityExecutionAggregate<AbilityStateValue> IGameplayAbilityExecutionStorage<AbilityStateValue>.ReadAggregate() =>
			m_Frame.ReadAbilityState();

		void IGameplayAbilityExecutionStorage<AbilityStateValue>.WriteAggregate(
			GameplayAbilityExecutionAggregate<AbilityStateValue> aggregate) =>
			m_Frame.SkillState.SetAbilityExecutionState(aggregate);

		void MatchActive(
			int index,
			CanonicalTextValue contextId,
            ref int found,
            ref Float32ActionInstanceState state)
        {
            Float32ActionInstanceState candidate = m_Frame.ReadActionInstances()[index];
            if (!candidate.IsActive ||
                !CanonicalTextValue.IsNullOrEmpty(contextId) && !CanonicalTextValue.Equals(candidate.ContextId, contextId, StringComparison.Ordinal))
                return;
            if (found >= 0)
                throw new InvalidOperationException($"Action Context '{contextId}' resolves multiple active Action instances.");
			found = index;
			state = candidate;
		}

		void MatchActive(
			int index,
			CharacterSkillId skillId,
			ref int found,
			ref Float32ActionInstanceState state)
		{
			Float32ActionInstanceState candidate = m_Frame.ReadActionInstances()[index];
			if (!candidate.IsActive)
				return;
			if (found >= 0)
				throw new InvalidOperationException($"Skill '{skillId}' resolves multiple active Action instances.");
			found = index;
			state = candidate;
		}

		void PopSkillExecution(Float32ActionInstanceReference expected, Float32ActionInstanceReference? previous)
		{
			if (!m_CurrentSkillExecution.HasValue || !m_CurrentSkillExecution.Value.Equals(expected))
				throw new InvalidOperationException("Skill execution scope is unbalanced.");
			m_CurrentSkillExecution = previous;
		}

		internal struct SkillExecutionScope : IDisposable
		{
			readonly Float32ActionStateStore m_Owner;
			readonly Float32ActionInstanceReference m_Expected;
			readonly Float32ActionInstanceReference? m_Previous;
			Float32AbilityExecutionFrame.ActionTraceContextScope m_TraceScope;
			bool m_Disposed;

			public SkillExecutionScope(
				Float32ActionStateStore owner,
				Float32ActionInstanceReference expected,
				Float32ActionInstanceReference? previous,
				Float32AbilityExecutionFrame.ActionTraceContextScope traceScope)
			{
				m_Owner = owner;
				m_Expected = expected;
				m_Previous = previous;
				m_TraceScope = traceScope;
				m_Disposed = false;
			}

			public void Dispose()
			{
				if (m_Disposed)
					return;
				m_Disposed = true;
				m_Owner.PopSkillExecution(m_Expected, m_Previous);
				m_TraceScope.Dispose();
			}
		}

		sealed class TraceExecutionScope : IDisposable
		{
			IDisposable m_Execution;
			Float32AbilityExecutionFrame.ActionTraceContextScope m_Trace;
			bool m_Disposed;

			public TraceExecutionScope Begin(IDisposable execution, Float32AbilityExecutionFrame.ActionTraceContextScope trace)
			{
				m_Execution = execution;
				m_Trace = trace;
				m_Disposed = false;
				return this;
			}

			public void Dispose()
			{
				if (m_Disposed)
					return;
				m_Disposed = true;
				m_Execution.Dispose();
				m_Trace.Dispose();
				m_Execution = null;
				m_Trace = default;
			}
		}

	}
}

