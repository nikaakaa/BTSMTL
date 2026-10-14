using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal interface IFPAbilityAvailabilityReadOwner
    {
        SimulationTick Tick { get; }
        bool HasMoveInput { get; }
        CharacterLocomotionMode LocomotionMode { get; }
        int ActionCount { get; }
        GameplayAbilitySourceContext ReadActiveSource(int index);
        ulong ReadSourceStartTick(int index);
        bool TryReadSource(in GameplayAbilitySourceContext source, out AbilityLifecyclePhase phase);
        SimulationInputRequestState ReadInputRequest(string requestId);
        bool IsInputRequestPending(in SimulationInputRequestState request);
        bool HasGameplayEvent(string eventId);
        bool EvaluateRuleCondition(CharacterSkillId skillId, in GameplayAbilitySourceContext source, OperationHandle condition);
        void PreviewRule(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission);
    }

    internal sealed class FPAbilityActivationRuleRuntime :
        IGameplayAbilityActivationRulePort, IGameplayAbilityDecisionReadPort
    {
        readonly SimulationActorBinding m_Actor;
        readonly GameplayAbilityActivationRuleRunner m_Runner;
        IFPAbilityAvailabilityReadOwner m_AvailabilityRead;
        FPCharacterControlRuntime m_Control;
        IFPInputRequestStatePort m_Input;
        IFPActionRuntimeStatePort m_Actions;

        public FPAbilityActivationRuleRuntime(SimulationActorBinding actor)
        {
            m_Actor = actor;
            var bindings = new GameplayAbilityExecutionBinding[actor.AbilityInstallations.Installations.Count];
            var requiredRequests = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < bindings.Length; i++)
            {
                bindings[i] = actor.AbilityInstallations.Installations[i].Data.Binding;
                foreach (string request in actor.AbilityInstallations.Installations[i].Layout.InputRequestIds)
                    requiredRequests.Add(request);
            }
            actor.InputRuntimeBinding.RequireRequests(requiredRequests);
            m_Runner = GameplayAbilityActivationRuleRunner.Prepare(bindings, this);
        }

        public void BeforeAbility(FPCharacterControlRuntime control,
            IFPInputRequestStatePort input, IFPActionRuntimeStatePort actions)
        {
            m_Control = control;
            m_Input = input;
            m_Actions = actions;
            m_Runner.BeforeAbility(this);
        }

        public bool AfterAbility() => m_Runner.AfterAbility(this);
        public void TraceDecision(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState inputRequest, string decision)
        {
            if (m_Actor.CharacterTraceSink.Enabled)
                m_Actor.CharacterTraceSink.AddRuleDecision(
                    SimulationExecutionSource.FromAbilityRule(ability.SkillId, rule.Id, rule.Phase),
                    decision, in source, inputRequest.Sequence);
        }

        public void QueryAvailability(GameplayAbilityRulePhase phase,
            IFPAbilityAvailabilityReadOwner read, out GameplayAbilityAvailabilityResult result)
        {
            IFPAbilityAvailabilityReadOwner previous = m_AvailabilityRead;
            m_AvailabilityRead = read;
            try
            {
                m_Runner.QueryAvailability(phase, this, out result);
            }
            finally
            {
                m_AvailabilityRead = previous;
            }
        }

        public string[] PrepareTriggerRequests(GameplayAbilityExecutionBinding ability) =>
            m_Actor.InputRuntimeBinding.PrepareAbilityTriggerRequests(ability.SkillId,
                m_Actor.AbilityInstallations.GameplayEffectCatalog.IsTagOrParent);

        public CharacterSkillId[] PrepareSources(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule)
        {
            if (!rule.SourceQuery.IsValid)
                return Array.Empty<CharacterSkillId>();
            var query = m_Actor.AbilityInstallations.Require(ability.SkillId).Services.RequireTagQuery(rule.SourceQuery);
            var candidates = new List<GameplayAbilityExecutionBinding>(m_Actor.AbilityInstallations.Installations.Count);
            var orders = new HashSet<int>();
            for (int i = 0; i < m_Actor.AbilityInstallations.Installations.Count; i++)
            {
                var installation = m_Actor.AbilityInstallations.Installations[i];
                GameplayAbilityExecutionBinding candidate = installation.Data.Binding;
                if (!m_Actor.AbilityInstallations.GameplayEffectCatalog.Matches(query,
                        candidate.Tags))
                    continue;
                if (!orders.Add(candidate.SourceOrder))
                    throw new InvalidOperationException("来源分类查询命中的技能来源顺序重复。");
                candidates.Add(candidate);
            }
            candidates.Sort((left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
            var result = new CharacterSkillId[candidates.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = candidates[i].SkillId;
            return result;
        }

        public bool HasMoveInput => m_AvailabilityRead == null ? m_Control.HasMoveInput : m_AvailabilityRead.HasMoveInput;
        public CharacterLocomotionMode LocomotionMode => m_AvailabilityRead == null ? m_Control.LocomotionMode : m_AvailabilityRead.LocomotionMode;

        public bool BlockMovingTurn
        {
            get
            {
                IReadOnlyList<FPActionInstanceState> actions = m_Actions.GetActionInstances();
                for (int i = 0; i < actions.Count; i++)
                    if (actions[i].IsActive &&
                        m_Actor.AbilityInstallations.RequireActionBinding(actions[i].SkillId).BlockMovingTurn)
                        return true;
                return false;
            }
        }

        public bool TryReadInputRequest(string requestId, out SimulationInputRequestState request)
        {
            request = m_AvailabilityRead == null
                ? m_Input.GetInputRequest(requestId) : m_AvailabilityRead.ReadInputRequest(requestId);
            SimulationTick tick = m_AvailabilityRead == null ? m_Input.Tick : m_AvailabilityRead.Tick;
            if (!request.IsValid || request.Consumed || request.ExpireTick < tick.Value)
                return false;
            return m_AvailabilityRead == null
                ? !m_Actor.IsInputRequestPending(in request) : !m_AvailabilityRead.IsInputRequestPending(in request);
        }

        public bool HasGameplayEvent(string eventId) => m_AvailabilityRead == null
            ? m_Actor.AbilityExecutionInput.HasGameplayEvent(m_Actor.ActorId, eventId)
            : m_AvailabilityRead.HasGameplayEvent(eventId);

        public bool HasOpportunity(CharacterSkillId candidate, OperationHandle query, in GameplayAbilitySourceContext source) =>
            m_Actor.EffectExecutionScratch.Target.TryFindOpportunity(GameplayEffectScope.Action(source),
                m_Actor.AbilityInstallations.Require(candidate).Services.RequireTagQuery(query), out _, out _);

        public int GetSourceSlotCount(CharacterSkillId abilityId) => m_AvailabilityRead != null
            ? m_AvailabilityRead.ActionCount : m_Actions.GetActionInstanceIndices(abilityId).Count;

        public bool TryReadActiveSource(CharacterSkillId abilityId, int slot,
            out GameplayAbilitySourceContext source, out ulong startTick)
        {
            if (m_AvailabilityRead != null)
            {
                source = m_AvailabilityRead.ReadActiveSource(slot);
                startTick = m_AvailabilityRead.ReadSourceStartTick(slot);
                return source.IsValid && source.SkillId == abilityId;
            }
            IReadOnlyList<int> indices = m_Actions.GetActionInstanceIndices(abilityId);
            FPActionInstanceState action = m_Actions.GetActionInstances()[indices[slot]];
            source = action.IsActive ? action.SourceContext : default;
            startTick = action.StartTick;
            return action.IsActive;
        }

        bool TryReadSource(in GameplayAbilitySourceContext source, out AbilityLifecyclePhase phase)
        {
            if (m_AvailabilityRead != null)
                return m_AvailabilityRead.TryReadSource(in source, out phase);
            IReadOnlyList<FPActionInstanceState> actions = m_Actions.GetActionInstances();
            for (int i = 0; i < actions.Count; i++)
            {
                FPActionInstanceState current = actions[i];
                if (current.InstanceId != source.InstanceId || current.SkillId != source.SkillId ||
                    current.SkillExecutionGeneration != source.SkillExecutionGeneration ||
                    current.SegmentGeneration != source.SegmentGeneration || !current.IsActive)
                    continue;
                phase = (AbilityLifecyclePhase)(byte)current.Phase;
                return true;
            }
            phase = default;
            return false;
        }

        public bool IsAbilityActive(CharacterSkillId queryOwner, OperationHandle queryOperation)
        {
            var query = m_Actor.AbilityInstallations.Require(queryOwner).Services.RequireTagQuery(queryOperation);
            if (m_AvailabilityRead != null)
            {
                for (int index = 0; index < m_AvailabilityRead.ActionCount; index++)
                {
                    GameplayAbilitySourceContext source = m_AvailabilityRead.ReadActiveSource(index);
                    if (!source.IsValid)
                        continue;
                    GameplayAbilityExecutionBinding ability = m_Actor.AbilityInstallations.Require(source.SkillId).Data.Binding;
                    if (m_Actor.AbilityInstallations.GameplayEffectCatalog.Matches(query, ability.Tags))
                        return true;
                }
                return false;
            }
            IReadOnlyList<FPActionInstanceState> actions = m_Actions.GetActionInstances();
            for (int i = 0; i < actions.Count; i++)
            {
                FPActionInstanceState action = actions[i];
                if (!action.IsActive || !action.SkillId.IsValid)
                    continue;
                var installation = m_Actor.AbilityInstallations.Require(action.SkillId);
                GameplayAbilityExecutionBinding ability = installation.Data.Binding;
                if (m_Actor.AbilityInstallations.GameplayEffectCatalog.Matches(query,
                        ability.Tags))
                    return true;
            }
            return false;
        }

        public bool IsSelectedSourcePhase(in GameplayAbilitySourceContext source, AbilityLifecyclePhase expectedPhase) =>
            TryReadSource(in source, out AbilityLifecyclePhase phase) && phase == expectedPhase;

        public bool SelectedSourceEffectsAllowCandidate(CharacterSkillId candidate, in GameplayAbilitySourceContext source)
        {
            IReadOnlyList<string> candidateTags = m_Actor.AbilityInstallations.Require(candidate).Data.Binding.Tags;
            bool hasSource = source.IsValid && TryReadSource(in source, out _);
            IReadOnlyList<string> sourceTags = hasSource
                ? m_Actor.AbilityInstallations.Require(source.SkillId).Data.Binding.Tags : Array.Empty<string>();
            FPGameplayEffectTarget effects = m_Actor.EffectExecutionScratch.Target;
            return !effects.TryFindAbilityBlock(candidateTags, GameplayEffectScope.Actor, out _) &&
                !effects.TryFindReplacementSourceRejection(candidateTags, sourceTags, hasSource, out _) &&
                (!hasSource || !effects.TryFindAbilityBlock(candidateTags, GameplayEffectScope.Action(source), out _));
        }

        public bool Evaluate(CharacterSkillId abilityId, OperationHandle condition,
            in GameplayAbilitySourceContext source, GameplayAbilityRuleEvaluationMode mode) =>
            mode == GameplayAbilityRuleEvaluationMode.Availability
                ? m_AvailabilityRead.EvaluateRuleCondition(abilityId, in source, condition)
                : m_Actor.GetInvocation(abilityId).EvaluateRuleCondition(in source, condition, mode);

        public void Preview(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission) =>
            m_AvailabilityRead.PreviewRule(ability, rule, in source, in inputRequest, out admission);

        public ActionActivationSubmission Submit(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState inputRequest) =>
            m_Actor.ActionRuntimes[ability.SkillId].SubmitRule(ability, rule, in source, in inputRequest);

        public void ExitToMovement(GameplayAbilityExecutionBinding ability,
            GameplayAbilityMovementExitRuleBinding rule, in GameplayAbilitySourceContext source) =>
            m_Actor.ActionRuntimes[ability.SkillId].ExitToMovement(ability, rule, in source);
    }
}
