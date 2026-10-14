using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.FPGameplay.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation.FPGameplay
{
    internal readonly struct FPActionActivationSavepoint
    {
        internal FPActionActivationSavepoint(FPAbilityExecutionSavepoint state,
            FPAbilityOutputSavepoint output, IFPAbilityActivationPort sourceOwner,
            FPAbilityOutputSavepoint sourceOutput)
        {
            State = state;
            Output = output;
            SourceOwner = sourceOwner;
            SourceOutput = sourceOutput;
        }

        internal FPAbilityExecutionSavepoint State { get; }
        internal FPAbilityOutputSavepoint Output { get; }
        internal IFPAbilityActivationPort SourceOwner { get; }
        internal FPAbilityOutputSavepoint SourceOutput { get; }
    }

    internal sealed class FPActionRuntime : FPOperationModule, IFPAbilityActivationPort, IFPActionAdmissionQuery, IActionAdmissionReadPort, IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>, IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>, IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>
    {
        readonly FPAbilityExecutionFrame m_Frame;
        readonly IFPAbilityActionBindingProvider m_ActionBindings;
        readonly IReadOnlyDictionary<CharacterSkillId, IFPAbilityActivationPort> m_ActionRuntimes;
        readonly FPInputRuntime m_InputRuntime;
        readonly FPActionStateStore m_Actions;
        readonly IFPBlackboardPort m_Blackboard;
        readonly IFPGameplayTagQuery m_GameplayTags;
        readonly IFPGameplayEffectActionPort m_GameplayEffectActions;
        readonly IReadOnlyList<ActionStartupEffect> m_StartupEffects;
        readonly SimulationSetByCallerValue[][] m_StartupValues;
        readonly FPHandleAllocator m_Handles;
        readonly FPFactSink m_Facts;
        readonly FPTraceSink m_Trace;
        readonly IEquipmentActionContextProvider m_EquipmentContext;
        readonly Func<OperationHandle, bool> m_IsOperationStopComplete;
        readonly ActionSkillActivationFlow<SimulationActionTargetSnapshot, SimulationOperation, FPActionInstanceState, FPActionActivationSavepoint> m_Activation;
        readonly ActionSkillCommitFlow<SimulationActionTargetSnapshot, FPActionInstanceState> m_Commit;
        readonly AbilityExecution<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress> m_Lifecycle;

        public FPActionRuntime(
            FPGameplayAbilityExecutionAccess access,
            IFPAbilityActionBindingProvider actionBindings,
            IReadOnlyDictionary<CharacterSkillId, IFPAbilityActivationPort> actionRuntimes,
            FPAbilityExecutionFrame frame,
            FPInputRuntime inputRuntime,
            FPActionStateStore actions,
            IFPBlackboardPort blackboard,
            IFPGameplayTagQuery gameplayTags,
            IFPGameplayEffectActionPort gameplayEffectActions,
            FPHandleAllocator handles,
            FPFactSink facts,
            FPTraceSink trace,
            IEquipmentActionContextProvider equipmentContext,
            Func<OperationHandle, bool> isOperationStopComplete)
            : base(access)
        {
            m_ActionBindings = actionBindings ?? throw new ArgumentNullException(nameof(actionBindings));
            m_ActionRuntimes = actionRuntimes;
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_InputRuntime = inputRuntime;
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            m_Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            m_GameplayTags = gameplayTags;
            m_GameplayEffectActions = gameplayEffectActions;
            m_StartupEffects = actionBindings.RequireActionBinding(access.Data.AbilityId).StartupEffects;
            m_StartupValues = new SimulationSetByCallerValue[m_StartupEffects.Count][];
            for (int i = 0; i < m_StartupEffects.Count; i++)
            {
                ActionStartupEffect startup = m_StartupEffects[i];
                var values = new SimulationSetByCallerValue[startup.ParameterIds.Count];
                for (int j = 0; j < values.Length; j++)
                {
                    ProgramConstant constant = access.Data.Constants[startup.ConstantIndices[j]];
                    if (constant.Kind != ProgramConstantKind.Scalar)
                        throw new InvalidDataException("Ability startup parameter must be a scalar constant.");
                    values[j] = new SimulationSetByCallerValue(startup.ParameterIds[j], constant.Scalar);
                }
                m_StartupValues[i] = values;
            }
            m_Handles = handles;
            m_Facts = facts;
            m_Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            m_EquipmentContext = equipmentContext;
            m_IsOperationStopComplete = isOperationStopComplete;
            m_Commit = new ActionSkillCommitFlow<SimulationActionTargetSnapshot, FPActionInstanceState>(this);
            m_Lifecycle = new AbilityExecution<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>(this);
            m_Activation = new ActionSkillActivationFlow<SimulationActionTargetSnapshot, SimulationOperation, FPActionInstanceState, FPActionActivationSavepoint>(new ActionAdmissionControl(this), this, m_Commit);
        }

        public bool Activate<TTarget>(OperationControlCursor<TTarget> cursor, SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ActionAdmissionProfile profile = RequireAdmissionProfile(operation);
            string requestId = GetStringConstant(operation, OperationNamedConstant.SourceInputRequest, string.Empty);
            SimulationInputRequestState inputRequest = m_Frame.ReadInputRequest(requestId);
            return m_Activation.ActivateImmediate(
                new ActionSkillActivationCandidate<SimulationActionTargetSnapshot, SimulationOperation>(
                    m_Frame.Data.AbilityId,
                    RequireAbility(m_Frame.Data.AbilityId).EntryOperation,
                    GetStringConstant(operation, OperationNamedConstant.ActionContext, string.Empty),
                    inputRequest,
                    GetBooleanConstant(operation, OperationNamedConstant.ConsumeSourceInputRequest, true),
                    GetStringConstant(operation, OperationNamedConstant.TargetKey, string.Empty),
                    ReadActionTargetSnapshot(cursor, operation),
                    SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)),
                    default,
                    operation,
                    requiresSourceInputRequest: !string.IsNullOrEmpty(requestId)),
                profile);
        }

        public bool IsContextActive(CanonicalTextValue contextId) => m_Actions.IsContextActive(contextId);
        public bool IsAbilityActive(CharacterSkillId abilityId) => m_Actions.IsAbilityActive(abilityId);
        public bool IsAbilityCompleted(CharacterSkillId abilityId) => m_Actions.IsAbilityCompleted(abilityId);
        public ulong CompletedAbilityInstanceId(CharacterSkillId abilityId) => m_Actions.CompletedAbilityInstanceId(abilityId);

        public void BindCurrentSkillExecution(OperationHandle operation, ulong generation)
        {
            if (!m_Actions.TryGetCurrentSkillExecution(out FPActionInstanceState action) ||
                !action.SkillEntryOperation.Equals(operation))
                return;
            m_Actions.BindSkillExecution(action, operation, generation);
        }

        public bool ResolveLifecycle(
            FPActionInstanceState action,
            SimulationExecutionSource source,
            AbilityLifecycleTransition transition,
            CanonicalTextValue reason)
        {
            return m_Lifecycle.Resolve(action, source, transition, reason);
        }

        public bool ResolveLifecycle(
            FPActionInstanceState action,
            SimulationExecutionSource source,
            string trigger,
            AbilityLifecycleTransition defaultTransition,
            CanonicalTextValue reason)
        {
            GameplayAbilityExecutionBinding binding = RequireAbility(action.SkillId);
            return m_Lifecycle.Resolve(action, binding, trigger, source, defaultTransition, reason);
        }

        public ActionAdmissionDecision PreviewActivation<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            in GameplayAbilitySourceContext replacementSource)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            CharacterSkillId candidateSkillId = new CharacterSkillId(GetStringConstant(operation, OperationNamedConstant.CandidateAbility, string.Empty));
            GameplayAbilityExecutionBinding candidate = RequireAbility(candidateSkillId);
            ActionAdmissionProfile profile = m_ActionBindings.RequireAdmissionProfile(candidateSkillId, candidate.AdmissionProfileId);
            ulong executingInstanceId = m_Actions.TryGetCurrentSkillExecution(out FPActionInstanceState action)
                ? action.InstanceId : 0;
            return m_Activation.Preview(candidateSkillId, profile, ReadActionTargetSnapshot(cursor, operation),
                in replacementSource, executingInstanceId);
        }

        ActionSkillActivationCandidate<SimulationActionTargetSnapshot, SimulationOperation> RuleCandidate(
            GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState inputRequest,
            ActionAdmissionProfile profile)
        {
            SimulationActionTargetSnapshot grantTarget = rule.TargetPolicy != GameplayAbilityRuleTargetPolicy.Grant ||
                profile.TargetRequirement == ActionTargetRequirement.None ||
                string.IsNullOrEmpty(ability.TargetInputValueId)
                    ? SimulationActionTargetSnapshot.None
                    : m_InputRuntime.ReadValue(ability.TargetInputValueId, ProgramInputValueKind.ActionTargetSnapshot).ActionTargetSnapshot;
            GameplayEffectOpportunityReference opportunity = default;
            if (rule.OpportunityQuery.IsValid)
                m_GameplayEffectActions.TryFindOpportunity(GameplayEffectScope.Action(source),
                    Access.Services.RequireTagQuery(rule.OpportunityQuery), out opportunity, out grantTarget);
            return new ActionSkillActivationCandidate<SimulationActionTargetSnapshot, SimulationOperation>(
                ability.SkillId, ability.EntryOperation, ability.ActionContextId,
                inputRequest, ability.ConsumeSourceInputRequest, ability.TargetKey,
                grantTarget, SimulationExecutionSource.FromAbilityRule(ability.SkillId, rule.Id, rule.Phase),
                default, replacementSource: source, activationEntryId: rule.ActivationEntryId,
                targetPolicy: rule.TargetPolicy, requiresSourceInputRequest: string.IsNullOrEmpty(rule.TriggerEventId),
                requiresOpportunity: rule.OpportunityQuery.IsValid, opportunity: opportunity);
        }

        public ActionActivationSubmission SubmitRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest)
        {
            m_Actions.RequireActivationEntry(rule.ActivationEntryId);
            ActionAdmissionProfile profile = RequireAdmissionProfile(ability.AdmissionProfileId);
            return m_Activation.ActivatePending(RuleCandidate(ability, rule, in source, in inputRequest, profile), profile);
        }

        public void PreviewRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission)
        {
            ActionAdmissionProfile profile = RequireAdmissionProfile(ability.AdmissionProfileId);
            ulong executingInstanceId = m_Actions.TryGetCurrentSkillExecution(out FPActionInstanceState action)
                ? action.InstanceId : 0;
            admission = m_Activation.Preview(RuleCandidate(ability, rule, in source, in inputRequest, profile),
                profile, executingInstanceId);
        }

        public void ExitToMovement(GameplayAbilityExecutionBinding ability,
            GameplayAbilityMovementExitRuleBinding rule, in GameplayAbilitySourceContext source)
        {
            SimulationExecutionSource cause = SimulationExecutionSource.FromAbilityRule(
                ability.SkillId, rule.Id, GameplayAbilityRulePhase.AfterAbility);
            if (!TryReadSource(in source, out FPActionInstanceState action) || !action.IsActive)
            {
                m_Trace.AddActionResult(cause, ability.AdmissionProfileId, ability.SkillId, source.InstanceId,
                    m_Frame.InputSequence, SimulationActionResultKind.Rejected, "ReplacementSourceChanged", ruleSource: in source);
                return;
            }
            m_Lifecycle.Resolve(action, ability, GameplayAbilityEndTriggerNames.ActionWindowClosed,
                cause, AbilityLifecycleTransition.Cancel, rule.Reason, rule.WindowType);
        }

        public bool StopIfEquipmentContextStale(FPActionInstanceState action)
        {
            if (!action.IsActive || !action.EquipmentContext.IsValid ||
                m_EquipmentContext != null && m_EquipmentContext.IsCurrentActionContext(action.EquipmentContext))
                return false;
            m_Lifecycle.Resolve(
                action,
                RequireAbility(action.SkillId),
                GameplayAbilityEndTriggerNames.AbortRequested,
                action.Source,
                AbilityLifecycleTransition.Abort,
                "EquipmentGenerationChanged");
            return true;
        }

        public bool TryCommitPending(CharacterSkillId skillId)
        {
            GameplayAbilityExecutionBinding skill = RequireAbility(skillId);
            ActionAdmissionProfile profile = RequireAdmissionProfile(skill.AdmissionProfileId);
            return m_Activation.TryCommitPending(skillId, profile);
        }

        public bool SetActionPhase<TTarget>(OperationControlCursor<TTarget> cursor, SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            if (!m_Actions.TryGetCurrentSkillExecution(out FPActionInstanceState action) || !action.IsActive)
                return false;
            m_Actions.WriteState(action.WithPhase((SimulationActionPhase)operation.Integer0));
            return true;
        }

        public bool SubmitLifecycle(SimulationOperation operation)
        {
            return m_Lifecycle.Submit(
                GetStringConstant(operation, OperationNamedConstant.ActionContext, string.Empty),
                operation.Integer0,
                operation.Text0,
                SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)));
        }

        public void ApplyIngress(SimulationIngress ingress)
        {
            if (ingress.Header.ActorId != m_Frame.ActorId)
                throw new InvalidOperationException($"Simulation ingress '{ingress.Header.FactIdentity}' targets '{ingress.Header.ActorId}', expected '{m_Frame.ActorId}'.");
            if (ingress.Header.Kind != SimulationIngressKind.ActionLifecycle)
                throw new InvalidOperationException($"Action runtime cannot apply ingress kind '{ingress.Header.Kind}'.");
            SimulationActionLifecycleIngress payload = ingress.ActionLifecycle;
            m_Lifecycle.ApplyIngress(new AbilityLifecycleIngress(
                ingress.Header.FactIdentity,
                payload.ActionInstanceId,
                payload.PredictionKey,
                payload.InputSequence,
                (int)payload.TransitionType,
                ingress.Header.SourceTick,
                payload.Reason), payload.GameplayEffectCommands);
        }

        GameplayEffectApplyResult IActionAdmissionReadPort.EvaluateStartupEffects(CharacterSkillId candidateSkillId) =>
            ((FPActionRuntime)m_ActionRuntimes[candidateSkillId]).EvaluateStartupEffects();

        GameplayEffectApplyResult EvaluateStartupEffects()
        {
            for (int i = 0; i < m_StartupEffects.Count; i++)
            {
                GameplayEffectApplyResult result = m_GameplayEffectActions.PreviewStartupEffect(
                    m_StartupEffects[i].EffectId, m_StartupValues[i]);
                if (!result.AcceptedMutation)
                    return result;
            }
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
        }

        GameplayCommandBatchAdmission IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.ReceiveStartupCommands(
            SimulationExecutionSource source) =>
            m_GameplayEffectActions == null
                ? new GameplayCommandBatchAdmission(m_Frame.ActorId, m_Frame.Tick.Value, 0, 0, 0, 0, source)
                : m_GameplayEffectActions.ReceiveCommandBatch(m_StartupEffects.Count + 1, source);

        bool IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.ApplyStartupEffects(
            FPActionInstanceState action, out GameplayEffectApplyResult failure)
        {
            var context = new SimulationGameplayEffectContext(m_Frame.ActorId, m_Frame.ActorId,
                action.InstanceId, action.PredictionKey, 0, m_Frame.Tick.Value,
                SimulationGameplayEffectApplicationMode.Predicted);
            for (int i = 0; i < m_StartupEffects.Count; i++)
            {
                GameplayEffectApplyResult result = m_GameplayEffectActions.ApplyStartupEffect(
                    m_StartupEffects[i].EffectId, m_StartupValues[i], context, action.Source);
                if (!result.AcceptedMutation)
                {
                    failure = result;
                    return false;
                }
            }
            failure = default;
            return true;
        }

        GameplayEffectApplyResult IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.CommitActionMutation(
            SimulationExecutionSource source) => m_GameplayEffectActions != null
                ? m_GameplayEffectActions.CommitActionMutation(source)
                : new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);

        FPActionActivationSavepoint IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.CreateMutationSavepoint(
            ulong sourceInstanceId)
        {
            FPAbilityExecutionSavepoint state = m_Frame.SavepointPort.CreateActivationSavepoint();
            IFPAbilityActivationPort sourceOwner = null;
            FPAbilityOutputSavepoint sourceOutput = default;
            if (sourceInstanceId != 0)
            {
                m_Actions.TryGetInstance(sourceInstanceId, out FPActionInstanceState source);
                sourceOwner = m_ActionRuntimes[source.SkillId];
                if (ReferenceEquals(sourceOwner, this))
                    sourceOwner = null;
                else
                    sourceOutput = sourceOwner.CreateOutputSavepoint();
            }
            return new FPActionActivationSavepoint(state, m_Frame.CreateOutputSavepoint(), sourceOwner, sourceOutput);
        }

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.RestoreMutation(
            FPActionActivationSavepoint savepoint)
        {
            m_Frame.SavepointPort.Restore(savepoint.State);
            RestoreOutput(savepoint.Output);
            if (savepoint.SourceOwner != null)
                savepoint.SourceOwner.RestoreOutput(savepoint.SourceOutput);
            if (m_GameplayEffectActions != null)
                m_GameplayEffectActions.CancelActionMutation();
        }

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.ReleaseMutation(
            FPActionActivationSavepoint savepoint) => m_Frame.SavepointPort.Release(savepoint.State);

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.MutationIsActive(
            FPActionActivationSavepoint savepoint) => m_Frame.SavepointPort.SavepointDepth >= savepoint.State.Depth;

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.ReportStartupFailure(
            GameplayEffectApplyResult failure, SimulationExecutionSource source, bool requiresCommandAdmission) =>
            m_GameplayEffectActions.ReportStartupFailure(failure, source, requiresCommandAdmission);

        public FPAbilityOutputSavepoint CreateOutputSavepoint() => m_Frame.CreateOutputSavepoint();

        public void RestoreOutput(FPAbilityOutputSavepoint savepoint) => m_Frame.RestoreOutput(savepoint);

        GameplayEffectReconciliationResult IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.Reconcile(
            FPActionInstanceState action, AbilityLifecycleTransition transition,
            IReadOnlyList<SimulationGameplayEffectLifecycleIngress> commands, SimulationExecutionSource source, bool requiresCommandReception) =>
            m_GameplayEffectActions != null
                ? m_GameplayEffectActions.ReconcileAction(action.InstanceId, action.PredictionKey, transition, commands, source, requiresCommandReception)
                : new GameplayEffectReconciliationResult(commands.Count != 0
                        ? GameplayEffectReconciliationResultKind.InvalidRequest
                        : transition == AbilityLifecycleTransition.Confirm
                            ? GameplayEffectReconciliationResultKind.Confirmed
                            : transition == AbilityLifecycleTransition.Reject
                                ? GameplayEffectReconciliationResultKind.Rejected : GameplayEffectReconciliationResultKind.Corrected,
                    new GameplayEffectReconciliationScope(GameplayEffectReconciliationScopeKind.PredictionKey, action.PredictionKey, string.Empty, 0),
                    0, 0, 0, reason: "GameplayEffectCapabilityMissing");

        void IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.RemoveActionTags(
            FPActionInstanceState action, SimulationExecutionSource source)
        {
            if (m_GameplayEffectActions != null)
            {
                GameplayEffectRemoveResult released = m_GameplayEffectActions.ReleaseActionEffects(action.InstanceId);
                if (released.Kind != GameplayEffectRemoveResultKind.Removed && released.Kind != GameplayEffectRemoveResultKind.NoMatch)
                    throw new InvalidOperationException($"Action effect release failed: {released.Failure.Reason}.");
                m_GameplayEffectActions.RemoveActionTags(action.InstanceId);
                GameplayEffectApplyResult committed = m_GameplayEffectActions.CommitActionMutation(source);
                if (!committed.AcceptedMutation)
                    throw new InvalidOperationException($"Action effect mutation failed: {committed.Reason}.");
            }
        }

        void EmitActionFact(SimulationExecutionSource source, FPActionInstanceState action)
        {
            SimulationEventHeader header = m_Facts.Next(source, SourceGeneration(source));
            m_Facts.Add(new GameplayFact(header, new ActionFact(
                action.InstanceId,
                action.PredictionKey,
                action.InputSequence,
                action.ActionId,
                action.SkillId,
                action.LastTransition,
                action.Phase,
                action.State,
                action.Reason,
                action.EquipmentContext)));
        }

        ulong SourceGeneration(SimulationExecutionSource source)
        {
            if (!source.IsSkillOperation)
                return 1;
            int slot = m_Frame.Layout.FindOperationStateSlot(
                source.Operation,
                ProgramStateSemantic.RunnableActivationGeneration);
            ulong generation = slot < 0 ? 1UL : m_Frame.ReadState(slot).UInt64;
            return generation == 0 ? 1UL : generation;
        }

        GameplayAbilityExecutionBinding RequireAbility(CharacterSkillId abilityId) =>
            m_ActionBindings.RequireActionBinding(abilityId);

        ActionAdmissionProfile RequireAdmissionProfile(SimulationOperation operation) =>
            Access.Services.RequireAdmissionProfile(operation.Handle);

        ActionAdmissionProfile RequireAdmissionProfile(string actionId) =>
            Access.Services.RequireAdmissionProfile(actionId);

		IReadOnlyList<string> IActionAdmissionReadPort.OwnedGameplayTags =>
            m_GameplayTags == null ? Array.Empty<string>() : m_GameplayTags.OwnedTags;

        int IActionAdmissionReadPort.ActionCount => m_Actions.ActionInstances.Count;

        bool TryReadSource(in GameplayAbilitySourceContext source, out FPActionInstanceState action) =>
            m_Actions.TryGetInstance(source.InstanceId, out action) && action.SourceContext.Equals(source);

        ActionReplacementStopStatus IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.ReadReplacementStopStatus(in GameplayAbilitySourceContext source)
        {
            if (!TryReadSource(in source, out FPActionInstanceState action))
                return ActionReplacementStopStatus.SourceChanged;
            return m_ActionRuntimes[action.SkillId].IsActionInstanceStopComplete(in action)
                ? ActionReplacementStopStatus.Complete : ActionReplacementStopStatus.Waiting;
        }

        bool IFPAbilityActivationPort.IsActionInstanceStopComplete(in FPActionInstanceState action)
        {
            if (action.IsActive || !action.SkillEntryOperation.IsValid)
                return false;
            if (m_Actions.IsSkillExecutionActive(action.InstanceId))
                return m_IsOperationStopComplete(action.SkillEntryOperation);
            if (!m_Actions.HasSkillExecutionFrame(action.InstanceId))
                return true;
            using (m_Actions.EnterSkillExecution(action))
                return m_IsOperationStopComplete(action.SkillEntryOperation);
        }

        IReadOnlyList<string> IActionAdmissionReadPort.ReadAbilityTags(CharacterSkillId skillId) =>
            m_ActionBindings.RequireActionBinding(skillId).Tags;

        bool IActionAdmissionReadPort.TryFindAbilityBlock(CharacterSkillId candidateSkillId, GameplayEffectScope scope, out ulong effectHandle)
        {
            effectHandle = 0;
            return m_GameplayEffectActions != null && m_GameplayEffectActions.TryFindAbilityBlock(
                m_ActionBindings.RequireActionBinding(candidateSkillId).Tags, scope, out effectHandle);
        }

        bool IActionAdmissionReadPort.TryFindReplacementSourceRejection(CharacterSkillId candidateSkillId, in GameplayAbilitySourceContext source,
            out ulong effectHandle)
        {
            effectHandle = 0;
            return m_GameplayEffectActions != null && m_GameplayEffectActions.TryFindReplacementSourceRejection(
                m_ActionBindings.RequireActionBinding(candidateSkillId).Tags,
                source.IsValid ? m_ActionBindings.RequireActionBinding(source.SkillId).Tags : Array.Empty<string>(),
                source.IsValid, out effectHandle);
        }

        int IActionAdmissionReadPort.GameplayTagCapacity => Access.Services.GameplayEffectCatalog == null
            ? 0 : Access.Services.GameplayEffectCatalog.TagParents.Count;

        bool IActionAdmissionReadPort.TryGetGameplayTagParent(string tag, out string parentTag) =>
            Access.Services.GameplayEffectCatalog.TagParents.TryGetValue(tag, out parentTag);

        ulong IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.InputSequence => m_Frame.InputSequence;

        ulong IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.Tick => m_Frame.Tick.Value;

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.TraceEnabled => m_Trace.Enabled;

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.TryReadInputRequest(
            CanonicalTextValue requestId, out SimulationInputRequestState request) =>
            m_InputRuntime.HasRequest(requestId, out request);

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.IsInputRequestPending(in SimulationInputRequestState request) =>
            m_Actions.IsInputRequestPending(in request);

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.ClearInputRequest(CanonicalTextValue requestId) => m_InputRuntime.ClearRequest(requestId);

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.ConsumeOpportunity(
            GameplayEffectOpportunityReference opportunity, SimulationExecutionSource source) =>
            m_GameplayEffectActions.ConsumeOpportunity(opportunity, source);

        SimulationActionTargetSnapshot IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.NoneTarget =>
            SimulationActionTargetSnapshot.None;

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.TryReadReplacementSource(
            in GameplayAbilitySourceContext source, out ActionAdmissionActiveAction activeSource,
            out SimulationActionTargetSnapshot targetSnapshot)
        {
            if (!TryReadSource(in source, out FPActionInstanceState action) || !action.IsActive)
            {
                activeSource = default;
                targetSnapshot = default;
                return false;
            }
            activeSource = new ActionAdmissionActiveAction(action.ActionId, in source);
            targetSnapshot = action.TargetSnapshot;
            return true;
        }

        CanonicalTextValue IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.TargetId(
            SimulationActionTargetSnapshot targetSnapshot) => targetSnapshot.TargetId;

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.HasPendingRequest(string actionId)
        {
            return m_Actions.HasPendingRequest(actionId);
        }

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.StageRequest(
            ActionSkillActivationRequest<SimulationActionTargetSnapshot> request)
        {
            m_Actions.StageRequest(
                new SimulationActionActivationRequestState(
                    request.ActionId,
                    request.SkillId,
                    request.SkillEntryOperation,
                    request.ContextId,
                    request.SourceInputRequest,
                    request.InputSequence,
                    request.StartTick,
                    request.TargetKey,
                    request.TargetSnapshot,
                    request.Source,
                    request.EquipmentContext,
                    request.ReplacementSource,
                    request.ActivationEntryId,
                    request.ConsumeSourceInputRequest, request.Opportunity));
        }

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.TryReadPendingRequest(
            CharacterSkillId skillId,
            out ActionSkillActivationRequest<SimulationActionTargetSnapshot> request)
        {
            int requestIndex = m_Actions.FindPendingSkill(skillId, out SimulationActionActivationRequestState staged);
            if (requestIndex < 0)
            {
                request = default;
                return false;
            }
            request = new ActionSkillActivationRequest<SimulationActionTargetSnapshot>(
                staged.ActionId,
                staged.SkillId,
                staged.SkillEntryOperation,
                staged.ContextId,
                staged.SourceInputRequest,
                staged.InputSequence,
                staged.StartTick,
                staged.TargetKey,
                staged.TargetSnapshot,
                staged.Source,
                staged.EquipmentContext,
                staged.ReplacementSource,
                staged.ActivationEntryId,
                staged.ConsumeSourceInputRequest, staged.Opportunity);
            return true;
        }

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.ClearPendingRequest(
            ActionSkillActivationRequest<SimulationActionTargetSnapshot> request)
        {
            int requestIndex = m_Actions.FindPendingRequest(
                request.ActionId,
                request.SkillId,
                request.SkillEntryOperation,
                request.ContextId,
                request.InputSequence,
                request.StartTick,
                request.ReplacementSource);
            if (requestIndex < 0)
                throw new InvalidOperationException($"Action '{request.ActionId}' has no matching pending activation request.");
            m_Actions.ClearRequestAt(requestIndex);
        }

        ulong IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.NextActionInstanceId() =>
            m_Handles.Next();

        ulong IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.NextPredictionKey() =>
            m_Actions.NextSequence();

        FPActionInstanceState IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.CreatePredictedAction(
            ActionSkillActivationRequest<SimulationActionTargetSnapshot> request,
            ulong instanceId,
            ulong predictionKey) =>
            new FPActionInstanceState(
                request.ActionId,
                request.SkillId,
                request.SkillEntryOperation,
                0,
                request.ContextId,
                instanceId,
                predictionKey,
                request.SourceInputRequestId,
                request.InputSequence,
                m_Frame.Tick.Value,
                request.TargetKey,
                request.TargetSnapshot,
                request.Source,
                SimulationActionPhase.Startup,
                SimulationActionState.Predicted,
                SimulationActionLifecycleTransitionType.None,
                m_Frame.Tick.Value,
                0,
                string.Empty,
                request.EquipmentContext,
                activationEntryId: request.ActivationEntryId);

        void IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.WriteAction(
            FPActionInstanceState action, in GameplayAbilitySourceContext replacementSource) =>
            m_Actions.WriteState(action, replacementSource);

        public bool TryReadSegmentGeneration(out ulong segmentGeneration)
        {
            if (m_Actions.TryGetCurrentSkillExecution(out FPActionInstanceState action))
            {
                segmentGeneration = action.SegmentGeneration;
                return true;
            }
            segmentGeneration = 0;
            return false;
        }

        public bool TryAdvanceSegmentGeneration(out ulong actionInstanceId)
        {
            actionInstanceId = 0;
            if (!m_Actions.TryGetCurrentSkillExecution(out FPActionInstanceState action))
                return false;
            ulong next = checked(action.SegmentGeneration + 1);
            m_Actions.WriteState(action.WithSegmentGeneration(next));
            actionInstanceId = action.InstanceId;
            return true;
        }

        GameplayEffectApplyResult IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.SetActionTags(
            ulong actionInstanceId,
            ActionAdmissionProfile profile)
        {
            if (m_GameplayEffectActions == null)
            {
                string[] tags = profile.GrantedTags;
                int tagCount = tags.Length;
                for (int i = 0; i < tagCount; i++)
                    if (!CanonicalTextValue.IsNullOrEmpty(tags[i]))
                        throw new InvalidOperationException(
                            "Action tags require the declared Gameplay Effect service.");
                return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, 0, 0, string.Empty);
            }
            return m_GameplayEffectActions.SetActionTags(actionInstanceId, profile);
        }

        void IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.ClearRequest(
            ActionSkillActivationRequest<SimulationActionTargetSnapshot> request) =>
            m_Actions.ClearRequestAt(m_Actions.FindPendingRequest(
                request.ActionId,
                request.SkillId,
                request.SkillEntryOperation,
                request.ContextId,
                request.InputSequence,
                request.StartTick,
                request.ReplacementSource));

        void IActionSkillCommitPort<SimulationActionTargetSnapshot, FPActionInstanceState>.EmitActionFact(
            SimulationExecutionSource source,
            FPActionInstanceState action)
        {
            EmitActionFact(source, action);
            m_Frame.Combat.OnActionCommitted(action.SkillId);
        }

        bool IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.InterruptActive(
            in GameplayAbilitySourceContext replacementSource,
            SimulationExecutionSource source,
            CanonicalTextValue reason)
        {
            m_Actions.TryGetInstance(replacementSource.InstanceId, out FPActionInstanceState action);
            return m_ActionRuntimes[action.SkillId].ResolveLifecycle(
                action,
                source,
                GameplayAbilityEndTriggerNames.InterruptRequested,
                AbilityLifecycleTransition.Interrupt,
                reason);
        }

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.Trace(
            SimulationOperation operation,
            string code,
            ActionSkillTraceSeverity severity,
            string detail) => m_Trace.Add(operation, code, ToTraceSeverity(severity), detail);

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.Trace(
            SimulationExecutionSource source,
            string code,
            ActionSkillTraceSeverity severity,
            string detail) => m_Trace.Add(source, code, ToTraceSeverity(severity), detail);

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.TraceActionResult(
            SimulationOperation operation,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason, in GameplayAbilitySourceContext ruleSource, ulong effectHandle) => m_Trace.AddActionResult(
                operation,
                actionId,
                skillId,
                actionInstanceId,
                inputSequence,
                result,
                reason, ruleSource: in ruleSource, effectHandle: effectHandle);

        void IActionSkillActivationPort<SimulationActionTargetSnapshot, SimulationOperation, FPActionActivationSavepoint>.TraceActionResult(
            SimulationExecutionSource source,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason, in GameplayAbilitySourceContext ruleSource, ulong effectHandle) => m_Trace.AddActionResult(
                source,
                actionId,
                skillId,
                actionInstanceId,
                inputSequence,
                result,
                reason, ruleSource: in ruleSource, effectHandle: effectHandle);

        ulong IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.Tick => m_Frame.Tick.Value;

        IReadOnlyList<FPActionInstanceState> IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.ActionStates =>
            m_Frame.ReadActionInstances();

        bool IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.TryFindActive(
            CanonicalTextValue contextId,
            out FPActionInstanceState action) =>
            m_Actions.FindActive(contextId, out action) >= 0;

        bool IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.TryFindActive(
            CharacterSkillId skillId,
            out FPActionInstanceState action) =>
            m_Actions.FindActive(skillId, out action) >= 0;

        bool IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.IsActive(FPActionInstanceState action) => action.IsActive;

        string IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.ActionId(FPActionInstanceState action) => action.ActionId;

        CharacterSkillId IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.SkillId(FPActionInstanceState action) => action.SkillId;

        ulong IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.InstanceId(FPActionInstanceState action) => action.InstanceId;

        ulong IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.PredictionKey(FPActionInstanceState action) => action.PredictionKey;

        ulong IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.InputSequence(FPActionInstanceState action) => action.InputSequence;

        CanonicalTextValue IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.Reason(FPActionInstanceState action) => action.Reason;

        SimulationExecutionSource IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.Source(FPActionInstanceState action) => action.Source;

        EquipmentActionContext IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.EquipmentContext(FPActionInstanceState action) => action.EquipmentContext;

        AbilityLifecyclePhase IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.Phase(FPActionInstanceState action) =>
            (AbilityLifecyclePhase)(byte)action.Phase;

        AbilityLifecycleState IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.State(FPActionInstanceState action) =>
            (AbilityLifecycleState)(byte)action.State;

        IDisposable IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.EnterExecution(FPActionInstanceState action)
        {
            if (!action.SkillId.IsValid)
                return null;
            if (m_Actions.IsSkillExecutionActive(action.InstanceId))
                return null;
            if (m_Actions.TryGetCurrentSkillExecution(out FPActionInstanceState current))
            {
                if (current.InstanceId != action.InstanceId)
                    throw new InvalidOperationException("Skill execution lifecycle scope does not match the active Action instance.");
                return null;
            }
            return m_Actions.EnterSkillExecution(action);
        }

        FPActionInstanceState IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.WithLifecycle(
            FPActionInstanceState action,
            AbilityLifecycleUpdate update) =>
            action.WithLifecycle(
                (SimulationActionPhase)(byte)update.Phase,
                (SimulationActionState)(byte)update.State,
                (SimulationActionLifecycleTransitionType)(byte)update.Transition,
                update.TransitionTick,
                update.SourceTick,
                update.Reason);

        void IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.WriteState(FPActionInstanceState action) =>
            m_Actions.WriteState(action);

        void IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.EmitActionFact(
            SimulationExecutionSource source,
            FPActionInstanceState action) => EmitActionFact(source, action);

		public void ClearTerminalResources(ulong actionInstanceId)
		{
			if (m_GameplayEffectActions != null)
			{
				m_GameplayEffectActions.ClearConfirmedAction(actionInstanceId);
			}
		}

        ulong IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.SourceGeneration(SimulationExecutionSource source) =>
            SourceGeneration(source);

        bool IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.TraceEnabled => m_Trace.Enabled;

        void IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.Trace(
            SimulationExecutionSource source,
            string code,
            ActionSkillTraceSeverity severity,
            string detail,
            ulong generation) => m_Trace.Add(source, code, ToTraceSeverity(severity), detail, generation);

        void IAbilityLifecyclePort<FPActionInstanceState, SimulationGameplayEffectLifecycleIngress>.TraceActionResult(
            SimulationExecutionSource source,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason) => m_Trace.AddActionResult(
                source,
                actionId,
                skillId,
                actionInstanceId,
                inputSequence,
                result,
                reason,
                SourceGeneration(source));

        bool IActionAdmissionReadPort.TryReadActiveAction(int index, out ActionAdmissionActiveAction value)
        {
            FPActionInstanceState action = m_Actions.ActionInstances[index];
            value = action.IsActive
                ? new ActionAdmissionActiveAction(action.ActionId, action.SourceContext)
                : default;
            return action.IsActive;
        }

        SimulationActionTargetSnapshot ReadActionTargetSnapshot<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            BlackboardTargetProjection target = m_Layout.TargetProjection(operation.Handle);
            if (!target.IsValid)
                return SimulationActionTargetSnapshot.None;
            return new SimulationActionTargetSnapshot(
                m_Blackboard.Read(cursor, operation, target.Identity).Identity,
                m_Blackboard.Read(cursor, operation, target.Position).Vector3,
                default(QuantumSimulationMath).Yaw(m_Blackboard.Read(cursor, operation, target.Yaw).Scalar));
        }

        static SimulationTraceSeverity ToTraceSeverity(ActionSkillTraceSeverity severity) =>
            severity == ActionSkillTraceSeverity.Detail
                ? SimulationTraceSeverity.Detail
                : SimulationTraceSeverity.Information;
    }
}
