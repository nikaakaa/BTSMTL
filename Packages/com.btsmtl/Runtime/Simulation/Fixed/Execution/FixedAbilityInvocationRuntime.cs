using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal interface IFixedAbilityActivationPort
    {
        FixedAbilityOutputSavepoint CreateOutputSavepoint();
        void RestoreOutput(FixedAbilityOutputSavepoint savepoint);
        ActionActivationSubmission SubmitRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest);
        void PreviewRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission);
        void ExitToMovement(GameplayAbilityExecutionBinding ability,
            GameplayAbilityMovementExitRuleBinding rule, in GameplayAbilitySourceContext source);
        bool IsActionInstanceStopComplete(in FixedActionInstanceState action);
        bool ResolveLifecycle(
            FixedActionInstanceState action,
            SimulationExecutionSource source,
            string trigger,
            AbilityLifecycleTransition defaultTransition,
            CanonicalTextValue reason);
    }

    internal interface IFixedAbilityExecutionServiceFactory
    {
        FixedAbilityExecutionAssembly Create(
            FixedGameplayAbilityExecutionData executionData,
            FixedGameplayAbilityExecutionServices executionServices,
            IFixedAbilityActionBindingProvider actionBindings,
            IFixedAbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            FixedAbilityExecutionFrame frame,
            FixedAbilityOperationControlRuntime control,
            FixedAbilityExecutionWorkspace workspace);
    }

    public interface IFixedAbilityActionBindingProvider
    {
        GameplayAbilityExecutionBinding RequireActionBinding(CharacterSkillId abilityId);
        internal ActionAdmissionProfile RequireAdmissionProfile(CharacterSkillId abilityId, string actionId);
    }

    internal readonly struct FixedAbilityExecutionAssembly
    {
        public FixedAbilityExecutionAssembly(
            FixedInputRuntime input,
            FixedActionRuntime actions,
            FixedGameplayEffectOperationRuntime gameplayEffects,
            FixedEquipmentRuntime equipment,
            FixedBlackboardRuntime blackboard,
            FixedMotionAccumulator motion,
            FixedAbilityOperationControlRuntime control,
            FixedAbilityDomainRuntime domain)
        {
            Input = input;
            ActionRuntime = actions ?? throw new ArgumentNullException(nameof(actions));
            GameplayEffects = gameplayEffects;
            Equipment = equipment;
            Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            Control = control ?? throw new ArgumentNullException(nameof(control));
            Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        }

        public FixedInputRuntime Input { get; }
        public IFixedAbilityActivationPort Actions => ActionRuntime;
        internal FixedActionRuntime ActionRuntime { get; }
        public FixedGameplayEffectOperationRuntime GameplayEffects { get; }
        public FixedEquipmentRuntime Equipment { get; }
        public FixedBlackboardRuntime Blackboard { get; }
        public FixedMotionAccumulator Motion { get; }
        public FixedAbilityOperationControlRuntime Control { get; }
        public FixedAbilityDomainRuntime Domain { get; }
    }

    internal sealed class FixedAbilityInvocationRuntime
    {
        IFixedSkillExecutionState m_SkillState;
        readonly FixedAbilityExecutionWorkspace m_Workspace;
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedInputRuntime m_Input;
        readonly FixedActionRuntime m_Actions;
        readonly FixedGameplayEffectOperationRuntime m_GameplayEffects;
        readonly IEquipmentActionContextReader m_Equipment;
        readonly FixedBlackboardRuntime m_Blackboard;
        readonly FixedMotionAccumulator m_Motion;
        readonly FixedAbilityOperationControlRuntime m_Control;
        readonly FixedAbilityDomainRuntime m_Domain;
        readonly FixedAbilityExecutionContext m_Execution;
        bool m_Begun;
        bool m_Beginning;
        bool m_Completed;
        bool m_Accepted;
        ulong m_BlackboardSequence;
        readonly List<(ulong Sequence, FixedBlackboardWriteCommand Command)> m_BlackboardCommands = new();
        readonly List<FixedBlackboardWriteResult> m_BlackboardCandidates = new();
        readonly List<FixedBlackboardWriteResult> m_BlackboardResults = new();

        public FixedAbilityInvocationRuntime(
            FixedAbilityExecutionContext execution,
            ActorId actorId,
            IFixedAbilityActionBindingProvider actionBindings,
            IFixedAbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            FixedAbilityExecutionWorkspace workspace,
            FixedAbilityOperationControlRuntime control,
            IFixedAbilityExecutionServiceFactory serviceFactory)
        {
            execution = execution ?? throw new ArgumentNullException(nameof(execution));
            AbilityId = execution.Data.AbilityId;
            m_Execution = execution;
            actionBindings = actionBindings ?? throw new ArgumentNullException(nameof(actionBindings));
            domainRuntimeFactory = domainRuntimeFactory ?? throw new ArgumentNullException(nameof(domainRuntimeFactory));
            control = control ?? throw new ArgumentNullException(nameof(control));
            m_Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            m_Control = control;
            serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
            m_Frame = new FixedAbilityExecutionFrame(execution.Data, execution.Layout,
                execution.Services, actorId, execution.Trace, workspace);
            FixedAbilityExecutionAssembly assembly = serviceFactory.Create(execution.Data,
                execution.Services, actionBindings, domainRuntimeFactory, equipmentLayout,
                m_Frame, control, workspace);
            m_Input = assembly.Input;
            m_Actions = assembly.ActionRuntime;
            m_GameplayEffects = assembly.GameplayEffects;
            m_Equipment = assembly.Equipment;
            m_Blackboard = assembly.Blackboard;
            m_Motion = assembly.Motion;
            m_Domain = assembly.Domain;
        }

        public CharacterSkillId AbilityId { get; }
        internal void BindValuePorts(FixedValuePortInputs values)
        {
            m_Frame.ValuePorts = values;
            m_Domain.ValuePorts = values;
        }

        internal void PrepareStateExecutionText(int maximumSessionCheckpointPayloadBytes,
            int maximumRetainedTextReferences) =>
            m_Control.PrepareStateExecutionText(maximumSessionCheckpointPayloadBytes, maximumRetainedTextReferences);

        internal void DisposeStateExecutionText()
        {
            ClearBlackboardWrites();
            m_Control.Dispose();
        }

        internal void ClearBlackboardWrites()
        {
            for (int i = 0; i < m_BlackboardCommands.Count; i++)
                m_BlackboardCommands[i].Command.Release();
            for (int i = 0; i < m_BlackboardResults.Count; i++)
                m_BlackboardResults[i].Release();
            m_BlackboardCommands.Clear();
            m_BlackboardResults.Clear();
            m_BlackboardCandidates.Clear();
        }

        internal void BindAvailabilityRead(IFixedAbilityFrameReadPort read) =>
            m_Frame.ExchangeAvailabilityRead(read);

        internal void PreviewAvailability(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission)
        {
            GameplayAbilitySourceContext previousSource = m_Frame.SelectedSource;
            GameplayAbilityRuleEvaluationMode previousMode = m_Frame.RuleEvaluationMode;
            m_Frame.SelectedSource = source;
            m_Frame.RuleEvaluationMode = GameplayAbilityRuleEvaluationMode.Availability;
            try { m_Actions.PreviewRule(ability, rule, in source, in inputRequest, out admission); }
            finally
            {
                m_Frame.SelectedSource = previousSource;
                m_Frame.RuleEvaluationMode = previousMode;
            }
        }

        public bool EvaluateRuleCondition(IGameplayAbilityDecisionReadPort read,
            in GameplayAbilitySourceContext source, OperationHandle condition, GameplayAbilityRuleEvaluationMode mode)
        {
            IGameplayAbilityDecisionReadPort previousRead = m_Frame.DecisionRead;
            GameplayAbilitySourceContext previousSource = m_Frame.SelectedSource;
            GameplayAbilityRuleEvaluationMode previousMode = m_Frame.RuleEvaluationMode;
            m_Frame.DecisionRead = read;
            m_Frame.SelectedSource = source;
            m_Frame.RuleEvaluationMode = mode;
            try
            {
                return m_Control.EvaluateRuleCondition(condition);
            }
            finally
            {
                m_Frame.DecisionRead = previousRead;
                m_Frame.SelectedSource = previousSource;
                m_Frame.RuleEvaluationMode = previousMode;
            }
        }

        internal ulong QueueBlackboardWrite(FixedBlackboardWriteCommand command)
        {
            if (!m_Execution.Services.Identity.Equals(command.Ability))
                throw new InvalidOperationException("黑板写入目标编译版本已变化。");
            if (command.StateSlot < 0 || command.StateSlot >= m_Execution.Data.StateSlots.Count ||
                m_Execution.Data.StateSlots[command.StateSlot].Semantic != ProgramStateSemantic.BlackboardValue)
                throw new ArgumentException("黑板写入目标不是声明的变量槽。", nameof(command));
            ProgramStateSlot slot = m_Execution.Data.StateSlots[command.StateSlot];
            SimulationBlackboardSlotGroup group = m_Execution.Services.BlackboardGroup(command.StateSlot);
            if (slot.ValueKind != command.Value.Kind)
                throw new ArgumentException("黑板写入值类型与声明不匹配。", nameof(command));
            if (!command.Owner.IsValid || group.Scope.Kind != command.Owner.ScopeKind ||
                group.CompiledOwnerIndex != command.Owner.CompiledOwnerIndex ||
                (group.Scope.Kind == ProgramScopeKind.Character
                    ? command.ActionInstanceId != 0 || command.SkillGeneration != 0
                    : command.ActionInstanceId == 0 || command.SkillGeneration == 0))
                throw new ArgumentException("黑板写入目标作用域或技能实例不完整。", nameof(command));
            return EnqueueBlackboardWrite(command);
        }

        ulong EnqueueBlackboardWrite(FixedBlackboardWriteCommand command)
        {
            int capacity = m_BlackboardCommands.Count + m_BlackboardResults.Count + 1;
            m_BlackboardCommands.Capacity = System.Math.Max(m_BlackboardCommands.Capacity, capacity);
            m_BlackboardCandidates.Capacity = System.Math.Max(m_BlackboardCandidates.Capacity, capacity);
            m_BlackboardResults.Capacity = System.Math.Max(m_BlackboardResults.Capacity, capacity);
            command.Retain();
            ulong sequence = checked(++m_BlackboardSequence);
            m_BlackboardCommands.Add((sequence, command));
            return sequence;
        }

        internal void BeginBlackboardTransaction() => m_BlackboardCandidates.Clear();

        internal void PublishBlackboardResults()
        {
            for (int i = 0; i < m_BlackboardCandidates.Count; i++)
            {
                FixedBlackboardWriteResult result = m_BlackboardCandidates[i];
                if (result.Status == FixedBlackboardWriteStatus.Applied)
                {
                    FixedActionReplayIdentity origin = default;
                    if (result.Command.ActionInstanceId != 0)
                    {
                        var actions = m_Frame.ReadActionInstances();
                        for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                            if (actions[actionIndex].InstanceId == result.Command.ActionInstanceId)
                            {
                                origin = FixedActionReplayIdentity.Capture(actions[actionIndex]);
                                break;
                            }
                    }
                    FixedCharacterInputTraceModule.RecordBlackboardWrite(m_Frame.ActorId, in result, in origin);
                }
            }
            m_BlackboardResults.AddRange(m_BlackboardCandidates);
            m_BlackboardCommands.RemoveRange(0, m_BlackboardCandidates.Count);
            m_BlackboardCandidates.Clear();
        }

        internal bool TryTakeBlackboardResult(ulong sequence, out FixedBlackboardWriteResult result)
        {
            for (int i = 0; i < m_BlackboardResults.Count; i++)
                if (m_BlackboardResults[i].Sequence == sequence)
                {
                    result = m_BlackboardResults[i];
                    m_BlackboardResults.RemoveAt(i);
                    return true;
                }
            result = default;
            return false;
        }

        public void Begin(in FixedAbilityInvocationContext context)
        {
            if (m_Begun)
            {
                if (!m_Completed)
                    throw new InvalidOperationException(
                        $"Fixed Ability invocation evaluation is already active for '{AbilityId}' " +
                        $"at '{m_Frame.Tick.Value}' while beginning '{context.Tick.Value}'.");
                m_Begun = false;
                m_Completed = false;
                m_Accepted = false;
            }

            m_SkillState = context.SkillState ?? throw new ArgumentNullException(nameof(context.SkillState));
            m_Beginning = true;
            m_Frame.Begin(context);

            m_Beginning = false;
            m_Completed = false;
            m_Accepted = false;
        }

        public void CopyMotionContributionsTo(FixedMotionContributionScratch contributions)
        {
            m_Workspace.CopyMotionContributionsTo(contributions);
            m_Workspace.ClearMotionContributions();
        }

        public void ApplyTimelineMotionWarps(ref ResolvedMotionChannel action)
        {
            RequireEvaluation();
            m_Motion.ApplyTimelineMotionWarps(ref action);
        }


        public IFixedAbilityActivationPort Actions => m_Actions;
        internal FixedGameplayEffectOperationRuntime GameplayEffects => m_GameplayEffects;
        public bool HasGameplayEffects => m_GameplayEffects != null;
        public IEquipmentActionContextReader Equipment => m_Equipment;

        public bool HasActionWindowProjection(in GameplayAbilitySourceContext source, string windowTag)
        {
            IReadOnlyList<SimulationActionWindowProjectionCandidate> projections = m_Workspace.ActionWindowProjections;
            for (int i = 0; i < projections.Count; i++)
                if (projections[i].SourceContext.Equals(source) &&
                    m_Execution.Services.GameplayEffectCatalog.IsTagOrParent(projections[i].WindowTag, windowTag))
                    return true;
            return false;
        }

        public void BeginEvaluation(
            bool diagnosticsEnabled,
            bool captureMotion,
            bool captureValues,
            bool captureControlFlow,
            bool captureBlackboard)
        {
            if (m_Begun)
                throw new InvalidOperationException(
                    $"Fixed Ability invocation evaluation is already active for '{m_Frame.ActorId}/'{AbilityId}' " +
                    $"at tick '{m_Frame.Tick.Value}'.");
            m_Control.BeginEvaluation(diagnosticsEnabled, captureMotion, captureValues, captureControlFlow, captureBlackboard);
            m_Begun = true;
        }

        public void ApplyActionIngress(SimulationIngress ingress)
        {
            RequireEvaluation();
            m_Actions.ApplyIngress(ingress);
        }

        public void ApplyGameplayEffectIngress(SimulationIngress ingress)
        {
            RequireEvaluation();
            (m_GameplayEffects ?? throw new InvalidOperationException(
                "Fixed Ability invocation has no declared Gameplay Effect service.")).ApplyIngress(ingress);
        }

        public void AdvanceGameplayEffects()
        {
            RequireEvaluation();
            (m_GameplayEffects ?? throw new InvalidOperationException(
                "Fixed Ability invocation has no declared Gameplay Effect service.")).Advance();
        }

        public void ApplyInputBindings()
        {
            RequireEvaluation();
            m_Input.ApplyBlackboardInputBindings(m_Blackboard);
        }

        public void Tick(bool applyBlackboardCommands)
        {
            RequireEvaluation();
            if (applyBlackboardCommands)
                for (int i = m_BlackboardCandidates.Count; i < m_BlackboardCommands.Count; i++)
                {
                    var request = m_BlackboardCommands[i];
                    FixedBlackboardWriteStatus status = m_Domain.ApplyBlackboardCommand(request.Command, request.Sequence);
                    m_BlackboardCandidates.Add(new FixedBlackboardWriteResult(request.Sequence, request.Command, m_Frame.Tick.Value, status));
                }
            m_Domain.Tick();
        }

        public void Complete(FixedCharacterEvaluationOutput output)
        {
            RequireEvaluation();
            if (m_Completed)
                throw new InvalidOperationException("Fixed Ability invocation has already completed.");
            m_Control.EndEvaluation();
            int factOffset = output.Facts.Count;
            int presentationOffset = output.Presentation.Count;
            output.Facts.AddRange(m_Workspace.Facts);
            output.Presentation.AddRange(m_Workspace.Presentation);
            output.Trace.AddRange(m_Workspace.Trace);
            output.CommandBatchRejections.AddRange(m_Workspace.CommandBatchRejections);
            for (int index = 0; index < m_Workspace.GameplayEffectProjectionCount; index++)
            {
                output.GameplayEffectProjections[output.GameplayEffectProjectionCount] =
                    m_Workspace.GameplayEffectProjections[index].Offset(factOffset, presentationOffset);
                output.GameplayEffectProjectionCount++;
            }
            m_Frame.End();
            m_Completed = true;
        }

        public void Accept(FixedCharacterRuntimeStateTransaction runtimeState)
        {
            if (!m_Completed || m_Accepted)
                throw new InvalidOperationException("Fixed Ability invocation cannot accept its current candidate.");
            runtimeState.AcceptAbility(m_SkillState);
            m_SkillState.Dispose();
            m_Accepted = true;
        }

        public void Abort()
        {
            if (m_Beginning)
            {
                m_Frame?.End();
                m_SkillState?.Dispose();
                m_Beginning = false;
                m_SkillState = null;
                return;
            }

            if (!m_Begun)
                return;
            if (!m_Completed)
            {
                m_Control.EndEvaluation();
                m_Frame.End();
            }
            if (!m_Accepted)
                m_SkillState.Dispose();
            m_Begun = false;
            m_Completed = false;
            m_Accepted = false;
            m_SkillState = null;
        }

        void RequireEvaluation()
        {
            if (!m_Begun || m_Completed)
                throw new InvalidOperationException("Fixed Ability invocation is not in its evaluation phase.");
        }
    }
}
