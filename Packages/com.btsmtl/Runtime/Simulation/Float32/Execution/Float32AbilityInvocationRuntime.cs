using MotionContributionBuffer = ThirdPersonSimulation.MotionContributionBuffer<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal interface IFloat32AbilityActivationPort
    {
        Float32AbilityOutputSavepoint CreateOutputSavepoint();
        void RestoreOutput(Float32AbilityOutputSavepoint savepoint);
        ActionActivationSubmission SubmitRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest);
        void PreviewRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission);
        void ExitToMovement(GameplayAbilityExecutionBinding ability,
            GameplayAbilityMovementExitRuleBinding rule, in GameplayAbilitySourceContext source);
        bool IsActionInstanceStopComplete(in Float32ActionInstanceState action);
        bool ResolveLifecycle(
            Float32ActionInstanceState action,
            SimulationExecutionSource source,
            string trigger,
            AbilityLifecycleTransition defaultTransition,
            CanonicalTextValue reason);
    }

    internal interface IFloat32AbilityExecutionServiceFactory
    {
        Float32AbilityExecutionAssembly Create(
            Float32GameplayAbilityExecutionData executionData,
            Float32GameplayAbilityExecutionServices executionServices,
            IFloat32AbilityActionBindingProvider actionBindings,
            IFloat32AbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            Float32AbilityExecutionFrame frame,
            Float32AbilityOperationControlRuntime control,
            Float32AbilityExecutionWorkspace workspace);
    }

    public interface IFloat32AbilityActionBindingProvider
    {
        GameplayAbilityExecutionBinding RequireActionBinding(CharacterSkillId abilityId);
        internal ActionAdmissionProfile RequireAdmissionProfile(CharacterSkillId abilityId, string actionId);
    }

    internal readonly struct Float32AbilityExecutionAssembly
    {
        public Float32AbilityExecutionAssembly(
            Float32InputRuntime input,
            Float32ActionRuntime actions,
            Float32GameplayEffectOperationRuntime gameplayEffects,
            Float32EquipmentRuntime equipment,
            Float32BlackboardRuntime blackboard,
            Float32MotionAccumulator motion,
            Float32AbilityOperationControlRuntime control,
            Float32AbilityDomainRuntime domain)
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

        public Float32InputRuntime Input { get; }
        public IFloat32AbilityActivationPort Actions => ActionRuntime;
        internal Float32ActionRuntime ActionRuntime { get; }
        public Float32GameplayEffectOperationRuntime GameplayEffects { get; }
        public Float32EquipmentRuntime Equipment { get; }
        public Float32BlackboardRuntime Blackboard { get; }
        public Float32MotionAccumulator Motion { get; }
        public Float32AbilityOperationControlRuntime Control { get; }
        public Float32AbilityDomainRuntime Domain { get; }
    }

    internal sealed class Float32AbilityInvocationRuntime
    {
        IFloat32SkillExecutionState m_SkillState;
        readonly Float32AbilityExecutionWorkspace m_Workspace;
        Float32AbilityExecutionFrame m_Frame;
        Float32InputRuntime m_Input;
        Float32ActionRuntime m_Actions;
        Float32GameplayEffectOperationRuntime m_GameplayEffects;
        IEquipmentActionContextReader m_Equipment;
        Float32BlackboardRuntime m_Blackboard;
        Float32MotionAccumulator m_Motion;
        readonly Float32AbilityOperationControlRuntime m_Control;
        Float32AbilityDomainRuntime m_Domain;
        readonly Float32AbilityExecutionContext m_Execution;
        readonly Float32TraceSink m_Trace;
        readonly IFloat32AbilityActionBindingProvider m_ActionBindings;
        readonly IFloat32AbilityDomainRuntimeFactory m_DomainRuntimeFactory;
        readonly EquipmentProgramLayout m_EquipmentLayout;
        readonly IFloat32AbilityExecutionServiceFactory m_ServiceFactory;
        bool m_Begun;
        bool m_Beginning;
        bool m_Completed;
        bool m_Accepted;

        public Float32AbilityInvocationRuntime(
            Float32AbilityExecutionContext execution,
            IFloat32AbilityActionBindingProvider actionBindings,
            IFloat32AbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            Float32AbilityExecutionWorkspace workspace,
            IFloat32AbilityExecutionServiceFactory serviceFactory)
        {
            execution = execution ?? throw new ArgumentNullException(nameof(execution));
            AbilityId = execution.Data.AbilityId;
            m_Execution = execution;
            m_Trace = new Float32TraceSink(execution.Data, execution.Layout);
            actionBindings = actionBindings ?? throw new ArgumentNullException(nameof(actionBindings));
            domainRuntimeFactory = domainRuntimeFactory ?? throw new ArgumentNullException(nameof(domainRuntimeFactory));
            m_Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            m_ActionBindings = actionBindings;
            m_DomainRuntimeFactory = domainRuntimeFactory;
            m_EquipmentLayout = equipmentLayout;
            m_Control = new Float32AbilityOperationControlRuntime(execution.Data);
            m_ServiceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        }

        internal void PrepareActor(ActorId actorId)
        {
                m_Frame = new Float32AbilityExecutionFrame(
                    m_Execution.Data,
                    m_Execution.Layout,
                    m_Execution.Services,
                    actorId,
                    m_Trace,
                    m_Workspace);

                Float32AbilityExecutionAssembly assembly = m_ServiceFactory.Create(
                    m_Execution.Data,
                    m_Execution.Services,
                    m_ActionBindings,
                    m_DomainRuntimeFactory,
                    m_EquipmentLayout,
                    m_Frame,
                    m_Control,
                    m_Workspace);
                m_Input = assembly.Input;
                m_Actions = assembly.ActionRuntime;
                m_GameplayEffects = assembly.GameplayEffects;
                m_Equipment = assembly.Equipment;
                m_Blackboard = assembly.Blackboard;
                m_Motion = assembly.Motion;
                m_Domain = assembly.Domain;

        }

        public CharacterSkillId AbilityId { get; }
        internal Float32AbilityOperationControlRuntime Control => m_Control;

        internal void BindAvailabilityRead(IFloat32AbilityFrameReadPort read) =>
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

        public void Begin(in Float32AbilityInvocationContext context)
        {
            if (m_Begun)
            {
                if (!m_Completed)
                    throw new InvalidOperationException(
                        $"Float32 Ability invocation evaluation is already active for '{AbilityId}' " +
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


        public IFloat32AbilityActivationPort Actions => m_Actions;
        internal Float32GameplayEffectOperationRuntime GameplayEffects => m_GameplayEffects;
        public bool HasGameplayEffects => m_GameplayEffects != null;
        public IEquipmentActionContextReader Equipment => m_Equipment;

        public void BeginEvaluation(
            bool diagnosticsEnabled,
            bool captureValues,
            bool captureControlFlow,
            bool captureBlackboard)
        {
            if (m_Begun)
                throw new InvalidOperationException(
                    $"Float32 Ability invocation evaluation is already active for '{AbilityId}' " +
                    $"at tick '{m_Frame.Tick.Value}'.");
            m_Control.BeginEvaluation(diagnosticsEnabled, captureValues, captureControlFlow, captureBlackboard);
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
                "Float32 Ability invocation has no declared Gameplay Effect service.")).ApplyIngress(ingress);
        }

        public void AdvanceGameplayEffects()
        {
            RequireEvaluation();
            (m_GameplayEffects ?? throw new InvalidOperationException(
                "Float32 Ability invocation has no declared Gameplay Effect service.")).Advance();
        }

        public void ApplyInputBindings()
        {
            RequireEvaluation();
            m_Input.ApplyBlackboardInputBindings(m_Blackboard);
        }

        public void Tick()
        {
            RequireEvaluation();
            m_Domain.Tick();
        }

        public void Complete(Float32CharacterEvaluationOutput output)
        {
            RequireEvaluation();
            if (m_Completed)
                throw new InvalidOperationException("Float32 Ability invocation has already completed.");
            m_Control.EndEvaluation();
            int factOffset = output.Facts.Count;
            int presentationOffset = output.Presentation.Count;
            for (int i = 0; i < m_Workspace.Facts.Count; i++) m_Workspace.Facts[i].Retain();
            output.Facts.AddRange(m_Workspace.Facts);
            for (int i = 0; i < m_Workspace.Presentation.Count; i++) m_Workspace.Presentation[i].Retain();
            output.Presentation.AddRange(m_Workspace.Presentation);
            for (int index = 0; index < m_Workspace.Trace.Count; index++)
                m_Workspace.Trace[index].Retain();
            output.Trace.AddRange(m_Workspace.Trace);
            output.CommandBatchRejections.AddRange(m_Workspace.CommandBatchRejections);
            for (int index = 0; index < m_Workspace.GameplayEffectProjectionCount; index++)
            {
                Float32GameplayEffectPendingProjection projection = m_Workspace.GameplayEffectProjections[index].Offset(factOffset, presentationOffset);
                projection.Retain();
                output.GameplayEffectProjections[output.GameplayEffectProjectionCount] = projection;
                output.GameplayEffectProjectionCount++;
            }
            m_Frame.End();
            m_Completed = true;
        }

        public void Accept(Float32CharacterRuntimeStateTransaction runtimeState)
        {
            if (!m_Completed || m_Accepted)
                throw new InvalidOperationException("Float32 Ability invocation cannot accept its current candidate.");
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
                throw new InvalidOperationException("Float32 Ability invocation is not in its evaluation phase.");
        }
    }
}
