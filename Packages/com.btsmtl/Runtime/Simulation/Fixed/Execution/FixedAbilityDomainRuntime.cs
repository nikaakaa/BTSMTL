using System;
using System.Collections.Generic;
using ThirdPersonPerformance.Instrumentation;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedAbilityDomainRuntime
    {
        readonly GameplayAbilityExecutionBinding m_Skill;
        internal FixedValuePortInputs ValuePorts { get; set; }
        readonly IGameplayAbilityExecutionServices m_Services;
        readonly FixedActionRuntime m_Actions;
        readonly FixedActionStateStore m_ActionStore;
        readonly FixedAbilityOperationControlRuntime m_Control;
        readonly FixedBlackboardRuntime m_Blackboard;
        readonly List<FixedActionInstanceState> m_CurrentActions;
        readonly HashSet<ulong> m_StoppingInstances;
#if UNITY_EDITOR
        readonly FixedAbilityExecutionWorkspace m_Workspace;
#endif

        public FixedAbilityDomainRuntime(
            GameplayAbilityExecutionBinding skill,
            IGameplayAbilityExecutionServices services,
            FixedActionRuntime actions,
            FixedActionStateStore actionStore,
            FixedAbilityOperationControlRuntime control,
            FixedBlackboardRuntime blackboard,
            FixedAbilityExecutionWorkspace workspace)
        {
            m_Skill = skill ?? throw new ArgumentNullException(nameof(skill));
            m_Services = services ?? throw new ArgumentNullException(nameof(services));
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            m_ActionStore = actionStore ?? throw new ArgumentNullException(nameof(actionStore));
            m_Control = control ?? throw new ArgumentNullException(nameof(control));
            m_CurrentActions = workspace.ActionTickSnapshot;
            m_StoppingInstances = workspace.StoppingActionInstances;
#if UNITY_EDITOR
            m_Workspace = workspace;
#endif
            m_Blackboard = blackboard;
        }

        internal FixedBlackboardWriteStatus ApplyBlackboardCommand(FixedBlackboardWriteCommand command, ulong sequence)
        {
            if (command.Owner.ScopeKind == ProgramScopeKind.Character)
                return m_Blackboard.ApplyCommand(m_Control.Cursor, command, sequence, default);
            IReadOnlyList<FixedActionInstanceState> actions = m_ActionStore.ActionInstances;
            for (int i = 0; i < actions.Count; i++)
            {
                FixedActionInstanceState action = actions[i];
                if (action.InstanceId != command.ActionInstanceId)
                    continue;
                if (!action.IsActive || action.SkillId != command.Ability.AbilityId ||
                    action.SkillExecutionGeneration != command.SkillGeneration)
                    return FixedBlackboardWriteStatus.InstanceEnded;
                using (m_ActionStore.EnterSkillExecution(action))
                using (m_ActionStore.PushSkillExecution(action))
                {
                    return m_Blackboard.ApplyCommand(m_Control.Cursor, command, sequence, action);
                }
            }
            return FixedBlackboardWriteStatus.InstanceEnded;
        }

        [PerformanceProbe("simulation.operation.ability-tick")]
        public void Tick()
        {
            GameplayAbilityExecutionBinding skill = m_Skill;
            try
            {
#if UNITY_EDITOR
                FixedCharacterEvaluationOutput output = m_Workspace.TraceOutput;
                long stageStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                m_ActionStore.CopyCurrentActions(skill.SkillId, m_CurrentActions);
                for (int i = 0; i < m_CurrentActions.Count; i++)
                    ProcessExisting(m_CurrentActions[i], skill, m_StoppingInstances);
#if UNITY_EDITOR
                long stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                output.EvaluationAbilitiesExistingElapsedStopwatchTicks += stageFinished - stageStarted;
                stageStarted = stageFinished;
#endif

                m_Actions.TryCommitPending(skill.SkillId);
#if UNITY_EDITOR
                stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                output.EvaluationAbilitiesPendingElapsedStopwatchTicks += stageFinished - stageStarted;
                stageStarted = stageFinished;
#endif
                m_CurrentActions.Clear();
                m_ActionStore.CopyCurrentActions(skill.SkillId, m_CurrentActions);
                for (int i = 0; i < m_CurrentActions.Count; i++)
                    TickActive(m_CurrentActions[i], skill, m_StoppingInstances);
#if UNITY_EDITOR
                output.EvaluationAbilitiesActiveElapsedStopwatchTicks +=
                    System.Diagnostics.Stopwatch.GetTimestamp() - stageStarted;
#endif
            }
            finally
            {
                m_CurrentActions.Clear();
                m_StoppingInstances.Clear();
            }
        }

        void ProcessExisting(
            FixedActionInstanceState action,
            GameplayAbilityExecutionBinding skill,
            HashSet<ulong> stoppingInstances)
        {
            if (m_Actions.StopIfEquipmentContextStale(action))
            {
                using (m_ActionStore.EnterSkillExecution(action))
                {
                    if (m_Control.IsActive(skill.EntryOperation))
                        m_Control.ForceStop(
                            skill.EntryOperation,
                            OperationStopContext.ActionContextEnded(skill.EntryOperation));
                }
                m_Actions.ClearTerminalResources(action.InstanceId);
                m_ActionStore.RemoveSkillExecution(action.InstanceId);
                return;
            }

            bool removeFrame = false;
            using (m_ActionStore.EnterSkillExecution(action))
            {
                if (m_Control.IsStopping(skill.EntryOperation))
                {
                    if (action.State == SimulationActionState.Aborted || action.State == SimulationActionState.Rejected)
                    {
                        m_Control.ForceStop(
                            skill.EntryOperation,
                            OperationStopContext.ActionContextEnded(skill.EntryOperation));
                        removeFrame = true;
                    }
                    else
                    {
                        OperationStopStatus stop = m_Control.ContinueStop(skill.EntryOperation);
                        if (stop == OperationStopStatus.Failed)
                            throw new InvalidOperationException($"Skill '{skill.SkillId}' EntryOperation stop failed.");
                        if (stop == OperationStopStatus.Running)
                            stoppingInstances.Add(action.InstanceId);
                        else if (!m_Control.IsActive(skill.EntryOperation))
                            removeFrame = true;
                    }
                }
                else if (!action.IsActive)
                {
                    if (!m_Control.IsActive(skill.EntryOperation))
                    {
                        removeFrame = true;
                    }
                    else if (action.State == SimulationActionState.Aborted || action.State == SimulationActionState.Rejected)
                    {
                        m_Control.ForceStop(
                            skill.EntryOperation,
                            OperationStopContext.ActionContextEnded(skill.EntryOperation));
                        removeFrame = true;
                    }
                    else
                    {
                        OperationStopStatus stop = m_Control.RequestStop(
                            skill.EntryOperation,
                            OperationStopContext.ActionContextEnded(skill.EntryOperation));
                        if (stop == OperationStopStatus.Failed)
                            throw new InvalidOperationException($"Skill '{skill.SkillId}' EntryOperation stop failed.");
                        if (stop == OperationStopStatus.Running)
                            stoppingInstances.Add(action.InstanceId);
                        else
                            removeFrame = true;
                    }
                }
            }
            if (!removeFrame)
                return;
            m_Actions.ClearTerminalResources(action.InstanceId);
            m_ActionStore.RemoveSkillExecution(action.InstanceId);
        }

        void TickActive(
            FixedActionInstanceState action,
            GameplayAbilityExecutionBinding skill,
            HashSet<ulong> stoppingInstances)
        {
            if (!action.IsActive || stoppingInstances.Contains(action.InstanceId))
                return;
            using (m_ActionStore.EnterSkillExecution(action))
            {
                if (!action.SkillEntryOperation.Equals(skill.EntryOperation))
                    throw new InvalidOperationException($"Skill '{skill.SkillId}' Action instance is bound to a different EntryOperation.");
                OperationRunnableStatus status = m_Control.ReadStatus(skill.EntryOperation);
                if (status == OperationRunnableStatus.Success || status == OperationRunnableStatus.Failure)
                {
                    if (action.SkillExecutionGeneration == 0)
                    {
                        OperationStopStatus reset = m_Control.RequestStop(
                            skill.EntryOperation,
                            OperationStopContext.ActionContextEnded(skill.EntryOperation));
                        if (reset != OperationStopStatus.Completed)
                            throw new InvalidOperationException($"Skill '{skill.SkillId}' previous EntryOperation state could not reset.");
                        status = m_Control.ReadStatus(skill.EntryOperation);
                    }
                    else
                    {
                        Resolve(action, skill, status == OperationRunnableStatus.Success);
                        return;
                    }
                }
                if (action.SkillExecutionGeneration != 0 && status == OperationRunnableStatus.Dormant)
                {
                    Resolve(action, skill, false);
                    return;
                }
                if (action.SkillExecutionGeneration != 0 &&
                    m_Control.ReadGeneration(skill.EntryOperation) != action.SkillExecutionGeneration)
                    throw new InvalidOperationException($"Skill '{skill.SkillId}' Action instance generation does not match its EntryOperation.");
                using (m_ActionStore.PushSkillExecution(action))
                {
                    ValuePorts?.ConsumeTriggers(m_Control);
                    OperationExecutionResult result = m_Control.Tick(skill.EntryOperation);
                    FixedActionInstanceState current = m_ActionStore.RequireActiveTransient(
                        FixedActionInstanceReference.FromInstance(action));
                    if (!current.IsActive)
                        return;
                    m_ActionStore.RetainEvaluatedAction(current);
                    if (result == OperationExecutionResult.Success || result == OperationExecutionResult.Failure)
                        Resolve(current, skill, result == OperationExecutionResult.Success);
                }
            }
        }

        void Resolve(FixedActionInstanceState action, GameplayAbilityExecutionBinding skill, bool success)
        {
            SimulationExecutionSource source = SimulationExecutionSource.FromSkillOperation(
                skill.EntryOperation,
                m_Services.SourcePath(skill.EntryOperation));
            if (!success)
            {
                m_Actions.ResolveLifecycle(
                    action,
                    source,
                    AbilityLifecycleTransition.Abort,
                    "SkillExecutionFailed");
                return;
            }
            m_Actions.ResolveLifecycle(
                action,
                source,
                GameplayAbilityEndTriggerNames.ExecutionCompleted,
                AbilityLifecycleTransition.Complete,
                "SkillCompleted");
        }
    }
}
