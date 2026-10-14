using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using MotionContributionBuffer = ThirdPersonSimulation.MotionContributionBuffer<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal interface IFPAbilityActivationPort
    {
        FPAbilityOutputSavepoint CreateOutputSavepoint();
        void RestoreOutput(FPAbilityOutputSavepoint savepoint);
        ActionActivationSubmission SubmitRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest);
        void PreviewRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission);
        void ExitToMovement(GameplayAbilityExecutionBinding ability,
            GameplayAbilityMovementExitRuleBinding rule, in GameplayAbilitySourceContext source);
        bool IsActionInstanceStopComplete(in FPActionInstanceState action);
        bool ResolveLifecycle(
            FPActionInstanceState action,
            SimulationExecutionSource source,
            string trigger,
            AbilityLifecycleTransition defaultTransition,
            CanonicalTextValue reason);
    }

    internal interface IFPAbilityExecutionServiceFactory
    {
        FPAbilityExecutionAssembly Create(
            FPGameplayAbilityExecutionData executionData,
            FPGameplayAbilityExecutionServices executionServices,
            IFPAbilityActionBindingProvider actionBindings,
            IFPAbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            FPAbilityExecutionFrame frame,
            FPAbilityOperationControlRuntime control,
            FPAbilityExecutionWorkspace workspace);
    }

    public interface IFPAbilityActionBindingProvider
    {
        GameplayAbilityExecutionBinding RequireActionBinding(CharacterSkillId abilityId);
        internal ActionAdmissionProfile RequireAdmissionProfile(CharacterSkillId abilityId, string actionId);
    }

    internal readonly struct FPAbilityExecutionAssembly
    {
        public FPAbilityExecutionAssembly(
            FPInputRuntime input,
            FPActionRuntime actions,
            FPGameplayEffectOperationRuntime gameplayEffects,
            FPEquipmentRuntime equipment,
            FPBlackboardRuntime blackboard,
            FPMotionAccumulator motion,
            FPAbilityOperationControlRuntime control,
            FPAbilityDomainRuntime domain)
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

        public FPInputRuntime Input { get; }
        public IFPAbilityActivationPort Actions => ActionRuntime;
        internal FPActionRuntime ActionRuntime { get; }
        public FPGameplayEffectOperationRuntime GameplayEffects { get; }
        public FPEquipmentRuntime Equipment { get; }
        public FPBlackboardRuntime Blackboard { get; }
        public FPMotionAccumulator Motion { get; }
        public FPAbilityOperationControlRuntime Control { get; }
        public FPAbilityDomainRuntime Domain { get; }
    }

    internal sealed class FPAbilityInvocationRuntime
    {
        IFPSkillExecutionState m_SkillState;
        readonly FPAbilityExecutionWorkspace m_Workspace;
        readonly FPAbilityExecutionFrame m_Frame;
        readonly FPInputRuntime m_Input;
        readonly FPActionRuntime m_Actions;
        readonly FPGameplayEffectOperationRuntime m_GameplayEffects;
        readonly IEquipmentActionContextReader m_Equipment;
        readonly FPBlackboardRuntime m_Blackboard;
        readonly FPMotionAccumulator m_Motion;
        readonly FPAbilityOperationControlRuntime m_Control;
        readonly FPAbilityDomainRuntime m_Domain;
        readonly FPAbilityExecutionContext m_Execution;
        bool m_Begun;
        bool m_Beginning;
        bool m_Completed;
        bool m_Accepted;
        ulong m_BlackboardSequence;
        readonly List<(ulong Sequence, FPBlackboardWriteCommand Command)> m_BlackboardCommands = new();
        readonly List<FPBlackboardWriteResult> m_BlackboardCandidates = new();
        readonly List<FPBlackboardWriteResult> m_BlackboardResults = new();
        readonly List<FPBlackboardWriteCommand> m_ExpiredBlackboardOverrides = new();
        int m_PreparedCommandCount;

        internal void PrepareEvaluationInputs()
        {
            m_PreparedCommandCount = m_BlackboardCommands.Count;
            m_Frame.ValuePorts?.PrepareEvaluation();
        }

        internal void AdoptEvaluationInputs() => m_Frame.ValuePorts?.AdoptEvaluation();
        internal void DiscardEvaluationInputs() => m_Frame.ValuePorts?.DiscardEvaluation();

        public FPAbilityInvocationRuntime(
            FPAbilityExecutionContext execution,
            ActorId actorId,
            IFPAbilityActionBindingProvider actionBindings,
            IFPAbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            FPAbilityExecutionWorkspace workspace,
            IFPAbilityExecutionServiceFactory serviceFactory)
        {
            execution = execution ?? throw new ArgumentNullException(nameof(execution));
            AbilityId = execution.Data.AbilityId;
            m_Execution = execution;
            actionBindings = actionBindings ?? throw new ArgumentNullException(nameof(actionBindings));
            domainRuntimeFactory = domainRuntimeFactory ?? throw new ArgumentNullException(nameof(domainRuntimeFactory));
            m_Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            m_Control = new FPAbilityOperationControlRuntime(execution.Data);
            serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
            m_Frame = new FPAbilityExecutionFrame(execution.Data, execution.Layout,
                execution.Services, actorId, new FPTraceSink(execution.Data, execution.Layout), workspace);
            FPAbilityExecutionAssembly assembly = serviceFactory.Create(execution.Data,
                execution.Services, actionBindings, domainRuntimeFactory, equipmentLayout,
                m_Frame, m_Control, workspace);
            m_Input = assembly.Input;
            m_Actions = assembly.ActionRuntime;
            m_GameplayEffects = assembly.GameplayEffects;
            m_Equipment = assembly.Equipment;
            m_Blackboard = assembly.Blackboard;
            m_Motion = assembly.Motion;
            m_Domain = assembly.Domain;
        }

        public CharacterSkillId AbilityId { get; }
        internal bool IsBlackboardLocked(ulong action, ulong generation, int slot) =>
            m_Frame.ValuePorts != null && m_Frame.ValuePorts.IsBlackboardLocked(action, generation, slot);

        internal void BindValuePorts(FPValuePortInputs values)
        {
            m_Frame.ValuePorts = values;
            m_Domain.ValuePorts = values;
            values.BlackboardCommands = m_BlackboardCommands;
            m_ExpiredBlackboardOverrides.Capacity = values.BlackboardOverrides.Capacity;
        }

        internal void PrepareStateExecutionText(int maximumRetainedTextReferences) =>
            m_Control.PrepareStateExecutionText(maximumRetainedTextReferences);

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
            m_ExpiredBlackboardOverrides.Clear();
        }

        internal void BindAvailabilityRead(IFPAbilityFrameReadPort read) =>
            m_Frame.ExchangeAvailabilityRead(read);

        internal void BindDecisionRead(IGameplayAbilityDecisionReadPort read) => m_Frame.DecisionRead = read;

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

        public bool EvaluateRuleCondition(in GameplayAbilitySourceContext source,
            OperationHandle condition, GameplayAbilityRuleEvaluationMode mode)
        {
            GameplayAbilitySourceContext previousSource = m_Frame.SelectedSource;
            GameplayAbilityRuleEvaluationMode previousMode = m_Frame.RuleEvaluationMode;
            m_Frame.SelectedSource = source;
            m_Frame.RuleEvaluationMode = mode;
            try
            {
                return m_Control.EvaluateRuleCondition(condition);
            }
            finally
            {
                m_Frame.SelectedSource = previousSource;
                m_Frame.RuleEvaluationMode = previousMode;
            }
        }

        internal ulong QueueBlackboardWrite(FPBlackboardWriteCommand command)
        {
            if (command.Mutation != FPPortInputMutation.Assign && command.Mutation != FPPortInputMutation.Override &&
                command.Mutation != FPPortInputMutation.Release)
                throw new ArgumentException("黑板输入仅支持赋值、锁定或解除。", nameof(command));
            if (command.Mutation != FPPortInputMutation.Assign && m_Frame.ValuePorts == null)
                throw new InvalidOperationException("当前 Session Source 未装配黑板覆盖输入。");
            if (command.Mutation != FPPortInputMutation.Assign && !m_Frame.ValuePorts.BlackboardOverrides.IsPrepared)
                throw new InvalidOperationException("当前会话未准备黑板覆盖输入；请从录制预览入口重新启动。");
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

        ulong EnqueueBlackboardWrite(FPBlackboardWriteCommand command)
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

        internal void BeginBlackboardTransaction()
        {
            m_BlackboardCandidates.Clear();
            m_ExpiredBlackboardOverrides.Clear();
        }

        internal void PublishBlackboardResults()
        {
            for (int index = 0; index < m_ExpiredBlackboardOverrides.Count; index++)
                m_Frame.ValuePorts.BlackboardOverrides.Expire(m_ExpiredBlackboardOverrides[index]);
            m_ExpiredBlackboardOverrides.Clear();
            for (int i = 0; i < m_BlackboardCandidates.Count; i++)
            {
                FPBlackboardWriteResult result = m_BlackboardCandidates[i];
                if (result.Status == FPBlackboardWriteStatus.Applied)
                {
                    if (result.Command.Mutation != FPPortInputMutation.Assign)
                        m_Frame.ValuePorts.CommitBlackboardInput(result.Command, result.Sequence, result.Tick);
                    FPActionReplayIdentity origin = default;
                    if (result.Command.ActionInstanceId != 0)
                    {
                        var actions = m_Frame.ReadActionInstances();
                        for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                            if (actions[actionIndex].InstanceId == result.Command.ActionInstanceId)
                            {
                                origin = FPActionReplayIdentity.Capture(actions[actionIndex]);
                                break;
                            }
                    }
                    FPCharacterInputTraceModule.RecordBlackboardWrite(m_Frame.ActorId, in result, in origin);
                }
            }
            m_BlackboardResults.AddRange(m_BlackboardCandidates);
            m_BlackboardCommands.RemoveRange(0, m_BlackboardCandidates.Count);
            m_BlackboardCandidates.Clear();
        }

        internal bool TryTakeBlackboardResult(ulong sequence, out FPBlackboardWriteResult result)
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

        public void Begin(in FPAbilityInvocationContext context)
        {
            if (m_Begun)
            {
                if (!m_Completed)
                    throw new InvalidOperationException(
                        $"FP Ability invocation evaluation is already active for '{AbilityId}' " +
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

        public void TransferMotionContributionsTo(MotionContributionBuffer contributions)
        {
            m_Workspace.TransferMotionContributionsTo(contributions);
        }

        public void ApplyTimelineMotionWarps(ref ResolvedMotionChannel action)
        {
            RequireEvaluation();
            m_Motion.ApplyTimelineMotionWarps(ref action);
        }


        public IFPAbilityActivationPort Actions => m_Actions;
        internal FPGameplayEffectOperationRuntime GameplayEffects => m_GameplayEffects;
        public bool HasGameplayEffects => m_GameplayEffects != null;
        public IEquipmentActionContextReader Equipment => m_Equipment;

        public void BeginEvaluation(
            bool diagnosticsEnabled,
            bool captureMotion,
            bool captureValues,
            bool captureControlFlow,
            bool captureBlackboard)
        {
            if (m_Begun)
                throw new InvalidOperationException(
                    $"FP Ability invocation evaluation is already active for '{m_Frame.ActorId}/'{AbilityId}' " +
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
                "FP Ability invocation has no declared Gameplay Effect service.")).ApplyIngress(ingress);
        }

        public void AdvanceGameplayEffects()
        {
            RequireEvaluation();
            (m_GameplayEffects ?? throw new InvalidOperationException(
                "FP Ability invocation has no declared Gameplay Effect service.")).Advance();
        }

        public void ApplyInputBindings()
        {
            RequireEvaluation();
            m_Input.ApplyBlackboardInputBindings(m_Blackboard
#if UNITY_EDITOR
                , m_Workspace.TraceOutput
#endif
                );
        }

        internal void ApplyBlackboardInputs(bool applyBlackboardCommands)
        {
            RequireEvaluation();
            if (m_Frame.ValuePorts != null)
            {
                FPBlackboardInputOverrides overrides = m_Frame.ValuePorts.BlackboardOverrides;
                for (int index = 0; index < overrides.Count; index++)
                {
                    ref readonly FPBlackboardInputOverrides.Entry entry = ref overrides.At(index);
                    bool superseded = applyBlackboardCommands && m_Frame.ValuePorts.HasPendingBlackboardInput(entry.Command);
                    if (!superseded && m_Domain.ApplyBlackboardCommand(entry.Command, entry.Sequence) != FPBlackboardWriteStatus.Applied)
                        m_ExpiredBlackboardOverrides.Add(entry.Command);
                }
            }
            if (applyBlackboardCommands)
                for (int i = m_BlackboardCandidates.Count; i < m_PreparedCommandCount; i++)
                {
                    var request = m_BlackboardCommands[i];
                    FPBlackboardWriteStatus status = m_Domain.ApplyBlackboardCommand(request.Command, request.Sequence);
                    m_BlackboardCandidates.Add(new FPBlackboardWriteResult(request.Sequence, request.Command, m_Frame.Tick.Value, status));
                }
        }

        public void Tick()
        {
            RequireEvaluation();
            m_Domain.Tick();
        }

        public void Complete(FPCharacterEvaluationOutput output)
        {
            RequireEvaluation();
            if (m_Completed)
                throw new InvalidOperationException("FP Ability invocation has already completed.");
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

        public void Accept(FPCharacterRuntimeStateTransaction runtimeState)
        {
            if (!m_Completed || m_Accepted)
                throw new InvalidOperationException("FP Ability invocation cannot accept its current candidate.");
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
                throw new InvalidOperationException("FP Ability invocation is not in its evaluation phase.");
        }
    }
}
