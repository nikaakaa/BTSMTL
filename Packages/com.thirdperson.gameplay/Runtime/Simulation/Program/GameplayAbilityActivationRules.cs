using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public enum GameplayAbilityRulePhase : byte
    {
        BeforeAbility = 0,
        AfterAbility = 1
    }

    public enum GameplayAbilityRuleContinuation : byte
    {
        Continue = 0,
        EndPhase = 1
    }

    public enum GameplayAbilityRuleTargetPolicy : byte
    {
        Grant = 0,
        SelectedSource = 1
    }

    public enum CharacterLocomotionMode : byte
    {
        Idle = 0,
        Walking = 1,
        Running = 2,
        Turning = 3
    }

    [CanonicalContract]
    public readonly partial struct GameplayAbilitySourceContext : IEquatable<GameplayAbilitySourceContext>
    {
        public GameplayAbilitySourceContext(CharacterSkillId skillId, ulong instanceId,
            ulong skillExecutionGeneration, ulong segmentGeneration)
        {
            SkillId = skillId;
            InstanceId = instanceId;
            SkillExecutionGeneration = skillExecutionGeneration;
            SegmentGeneration = segmentGeneration;
        }

        public CharacterSkillId SkillId { get; }
        public ulong InstanceId { get; }
        public ulong SkillExecutionGeneration { get; }
        public ulong SegmentGeneration { get; }
        public bool IsValid => SkillId.IsValid && InstanceId != 0;
        public bool Equals(GameplayAbilitySourceContext other) => SkillId == other.SkillId &&
            InstanceId == other.InstanceId && SkillExecutionGeneration == other.SkillExecutionGeneration &&
            SegmentGeneration == other.SegmentGeneration;
        public override bool Equals(object value) => value is GameplayAbilitySourceContext other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SkillId, InstanceId, SkillExecutionGeneration, SegmentGeneration);
    }

    internal enum GameplayAbilityRuleEvaluationMode : byte
    {
        Activation = 0,
        Availability = 1
    }

    public enum GameplayAbilityAvailabilityReason : byte
    {
        NoInput = 0,
        Allowed = 1,
        NoCandidate = 2,
        SourceMissing = 3,
        NotApplicable = 4,
        ConditionRejected = 5,
        AdmissionRejected = 6
    }

    public readonly struct GameplayAbilityAvailabilityResult
    {
        internal GameplayAbilityAvailabilityResult(GameplayAbilityAvailabilityReason reason,
            GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState inputRequest,
            in ActionAdmissionDecision admission)
        {
            Reason = reason;
            Ability = ability;
            Rule = rule;
            Source = source;
            InputRequest = inputRequest;
            Admission = admission;
        }

        public GameplayAbilityAvailabilityReason Reason { get; }
        public bool Allowed => Reason == GameplayAbilityAvailabilityReason.Allowed;
        public GameplayAbilityExecutionBinding Ability { get; }
        public GameplayAbilityActivationRuleBinding Rule { get; }
        public GameplayAbilitySourceContext Source { get; }
        internal readonly SimulationInputRequestState InputRequest;
        public CanonicalTextValue InputRequestId => InputRequest.RequestId;
        public ulong InputSequence => InputRequest.Sequence;
        public ulong InputSourceTick => InputRequest.SourceTick;
        public ulong InputExpireTick => InputRequest.ExpireTick;
        public int InputPriority => InputRequest.Priority;
        public bool InputConsumed => InputRequest.Consumed;
        internal readonly ActionAdmissionDecision Admission;
        public ActionAdmissionRejectReason AdmissionRejectReason => Admission.RejectReason;
        public string AdmissionSourceActionId => Admission.ActiveSourceActionId;
        public ulong AdmissionSourceActionInstanceId => Admission.ActiveSourceActionInstanceId;
        public GameplayEffectApplyResultKind StartupFailureCode => Admission.StartupFailure.Kind;
        public CanonicalTextValue StartupFailureReason => Admission.StartupFailure.Reason;
        public CanonicalTextValue StartupFailureParameterId => Admission.StartupFailure.ParameterId;
        public void CopyStartupFailure(out GameplayEffectExecutionFailure failure) => failure = Admission.StartupFailure.Failure;
        public void Retain()
        {
            InputRequest.Retain();
            Admission.StartupFailure.Retain();
        }
        public void Release()
        {
            InputRequest.Release();
            Admission.StartupFailure.Release();
        }
    }

    public interface IGameplayAbilityAvailabilityQuery
    {
        void QueryAbilityAvailability(GameplayAbilityRulePhase phase, out GameplayAbilityAvailabilityResult result);
    }

    public interface IGameplayAbilityAvailabilitySessionReadPort
    {
        void QueryAbilityAvailability(ActorId actorId, GameplayAbilityRulePhase phase, out GameplayAbilityAvailabilityResult result);
    }

    public interface IGameplayAbilityDecisionReadPort
    {
        bool HasMoveInput { get; }
        CharacterLocomotionMode LocomotionMode { get; }
        bool IsAbilityActive(CharacterSkillId queryOwner, OperationHandle query);
        bool IsSelectedSourcePhase(in GameplayAbilitySourceContext source, AbilityLifecyclePhase phase);
        bool SelectedSourceEffectsAllowCandidate(CharacterSkillId candidate, in GameplayAbilitySourceContext source);
    }

    public sealed class GameplayAbilityActivationRuleBinding
    {
        public GameplayAbilityActivationRuleBinding(
            string id,
            GameplayAbilityRulePhase phase,
            int order,
            OperationHandle applicability,
            OperationHandle condition,
            OperationHandle sourceQuery,
            bool requiresSource,
            string activationEntryId,
            GameplayAbilityRuleTargetPolicy targetPolicy,
            GameplayAbilityRuleContinuation onFailure,
            GameplayAbilityRuleContinuation onSubmitted,
            string triggerEventId = "",
            OperationHandle opportunityQuery = default)
        {
            Id = id;
            Phase = phase;
            Order = order;
            Applicability = applicability;
            Condition = condition;
            SourceQuery = sourceQuery;
            RequiresSource = requiresSource;
            ActivationEntryId = activationEntryId;
            TargetPolicy = targetPolicy;
            OnFailure = onFailure;
            OnSubmitted = onSubmitted;
            TriggerEventId = triggerEventId;
            OpportunityQuery = opportunityQuery;
        }

        public string Id { get; }
        public GameplayAbilityRulePhase Phase { get; }
        public int Order { get; }
        public OperationHandle Applicability { get; }
        public OperationHandle Condition { get; }
        public OperationHandle SourceQuery { get; }
        public bool RequiresSource { get; }
        public string ActivationEntryId { get; }
        public GameplayAbilityRuleTargetPolicy TargetPolicy { get; }
        public GameplayAbilityRuleContinuation OnFailure { get; }
        public GameplayAbilityRuleContinuation OnSubmitted { get; }
        public string TriggerEventId { get; }
        public OperationHandle OpportunityQuery { get; }
    }

    public readonly struct GameplayAbilityMovementExitRuleBinding
    {
        public GameplayAbilityMovementExitRuleBinding(
            string id,
            int order,
            OperationHandle condition,
            string windowType,
            string reason)
        {
            Id = id;
            Order = order;
            Condition = condition;
            WindowType = windowType;
            Reason = reason;
        }

        public string Id { get; }
        public int Order { get; }
        public OperationHandle Condition { get; }
        public string WindowType { get; }
        public string Reason { get; }
    }

    internal interface IGameplayAbilityActivationRulePort
    {
        CharacterSkillId[] PrepareSources(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule);
        string[] PrepareTriggerRequests(GameplayAbilityExecutionBinding ability);
        bool TryReadInputRequest(string requestId, out SimulationInputRequestState request);
        bool HasGameplayEvent(string eventId);
        bool HasOpportunity(CharacterSkillId candidate, OperationHandle query, in GameplayAbilitySourceContext source);
        int GetSourceSlotCount(CharacterSkillId abilityId);
        bool TryReadActiveSource(CharacterSkillId abilityId, int slot,
            out GameplayAbilitySourceContext source, out ulong startTick);
        bool Evaluate(CharacterSkillId abilityId, OperationHandle condition,
            in GameplayAbilitySourceContext source, GameplayAbilityRuleEvaluationMode mode);
        void Preview(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest, out ActionAdmissionDecision admission);
        ActionActivationSubmission Submit(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, in GameplayAbilitySourceContext source,
            in SimulationInputRequestState inputRequest);
        void ExitToMovement(GameplayAbilityExecutionBinding ability,
            GameplayAbilityMovementExitRuleBinding rule, in GameplayAbilitySourceContext source);
        void TraceDecision(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState inputRequest, string decision);
    }

    internal readonly struct PreparedGameplayAbilityActivationRule
    {
        public PreparedGameplayAbilityActivationRule(GameplayAbilityExecutionBinding ability,
            GameplayAbilityActivationRuleBinding rule, CharacterSkillId[] sources, string[] requests)
        {
            Ability = ability;
            Rule = rule;
            Sources = sources;
            Requests = requests;
        }

        public GameplayAbilityExecutionBinding Ability { get; }
        public GameplayAbilityActivationRuleBinding Rule { get; }
        public CharacterSkillId[] Sources { get; }
        public string[] Requests { get; }
    }

    internal sealed class GameplayAbilityActivationRuleRunner
    {
        readonly PreparedGameplayAbilityActivationRule[][] m_BeforeByRequest;
        readonly PreparedGameplayAbilityActivationRule[] m_BeforeEvents;
        readonly PreparedGameplayAbilityActivationRule[] m_After;
        readonly GameplayAbilityExecutionBinding[] m_MovementExits;
        readonly string[] m_BeforeRequests;

        public GameplayAbilityActivationRuleRunner(
            PreparedGameplayAbilityActivationRule[] before,
            PreparedGameplayAbilityActivationRule[] after,
            GameplayAbilityExecutionBinding[] movementExits)
        {
            m_After = after;
            m_MovementExits = movementExits;
            var rulesByRequest = new SortedDictionary<string, List<PreparedGameplayAbilityActivationRule>>(StringComparer.Ordinal);
            var events = new List<PreparedGameplayAbilityActivationRule>();
            for (int i = 0; i < before.Length; i++)
            {
                if (!string.IsNullOrEmpty(before[i].Rule.TriggerEventId))
                {
                    events.Add(before[i]);
                    continue;
                }
                for (int request = 0; request < before[i].Requests.Length; request++)
                {
                    string requestId = before[i].Requests[request];
                    if (!rulesByRequest.TryGetValue(requestId, out List<PreparedGameplayAbilityActivationRule> rules))
                    {
                        rules = new List<PreparedGameplayAbilityActivationRule>();
                        rulesByRequest.Add(requestId, rules);
                    }
                    rules.Add(before[i]);
                }
            }
            m_BeforeEvents = events.ToArray();
            m_BeforeRequests = new string[rulesByRequest.Count];
            m_BeforeByRequest = new PreparedGameplayAbilityActivationRule[rulesByRequest.Count][];
            int requestIndex = 0;
            foreach (KeyValuePair<string, List<PreparedGameplayAbilityActivationRule>> entry in rulesByRequest)
            {
                m_BeforeRequests[requestIndex] = entry.Key;
                m_BeforeByRequest[requestIndex] = entry.Value.ToArray();
                requestIndex++;
            }
        }

        public static GameplayAbilityActivationRuleRunner Prepare(IReadOnlyList<GameplayAbilityExecutionBinding> abilities,
            IGameplayAbilityActivationRulePort port)
        {
            var before = new List<PreparedGameplayAbilityActivationRule>();
            var after = new List<PreparedGameplayAbilityActivationRule>();
            var exits = new List<GameplayAbilityExecutionBinding>();
            var candidateOrders = new HashSet<int>();
            var orders = new HashSet<(GameplayAbilityRulePhase Phase, string Request, int Order)>();
            for (int i = 0; i < abilities.Count; i++)
                if (!candidateOrders.Add(abilities[i].CandidateOrder))
                    throw new InvalidOperationException("已授予技能的候选顺序重复。");
            for (int i = 0; i < abilities.Count; i++)
            {
                GameplayAbilityExecutionBinding ability = abilities[i];
                string[] inputRequests = ability.ActivationRules.Count > 0
                    ? port.PrepareTriggerRequests(ability) : Array.Empty<string>();
                for (int ruleIndex = 0; ruleIndex < ability.ActivationRules.Count; ruleIndex++)
                {
                    GameplayAbilityActivationRuleBinding rule = ability.ActivationRules[ruleIndex];
                    bool eventTriggered = !string.IsNullOrEmpty(rule.TriggerEventId);
                    string[] requests = eventTriggered ? Array.Empty<string>() : inputRequests;
                    if (!eventTriggered && requests.Length == 0)
                        throw new InvalidOperationException("技能入口规则的 TriggerQuery 没有匹配已声明输入。");
                    if (rule.RequiresSource && !rule.SourceQuery.IsValid)
                        throw new InvalidOperationException("必需来源的规则缺少来源分类查询。");
                    for (int request = 0; request < requests.Length; request++)
                        if (!orders.Add((rule.Phase, requests[request], rule.Order)))
                            throw new InvalidOperationException($"输入'{requests[request]}'阶段'{rule.Phase}'规则顺序'{rule.Order}'重复。");
                    (rule.Phase == GameplayAbilityRulePhase.BeforeAbility ? before : after)
                        .Add(new PreparedGameplayAbilityActivationRule(ability, rule, port.PrepareSources(ability, rule), requests));
                }
                if (ability.MovementExitRules.Count > 0)
                    exits.Add(ability);
            }
            int Compare(PreparedGameplayAbilityActivationRule left, PreparedGameplayAbilityActivationRule right)
            {
                int order = left.Rule.Order.CompareTo(right.Rule.Order);
                return order == 0 ? left.Ability.CandidateOrder.CompareTo(right.Ability.CandidateOrder) : order;
            }
            before.Sort(Compare);
            after.Sort(Compare);
            exits.Sort((left, right) =>
            {
                int order = left.MovementExitRules[0].Order.CompareTo(right.MovementExitRules[0].Order);
                return order == 0 ? left.CandidateOrder.CompareTo(right.CandidateOrder) : order;
            });
            return new GameplayAbilityActivationRuleRunner(before.ToArray(), after.ToArray(), exits.ToArray());
        }

        public void BeforeAbility(IGameplayAbilityActivationRulePort port)
        {
            if (Run(m_BeforeEvents, port, null, default))
                return;
            int requestIndex = SelectBeforeRequest(port, out SimulationInputRequestState selectedRequest);
            if (requestIndex >= 0)
                Run(m_BeforeByRequest[requestIndex], port, m_BeforeRequests[requestIndex], in selectedRequest);
        }

        int SelectBeforeRequest(IGameplayAbilityActivationRulePort port, out SimulationInputRequestState selected)
        {
            selected = default;
            int selectedIndex = -1;
            int priority = int.MinValue;
            for (int i = 0; i < m_BeforeRequests.Length; i++)
            {
                if (port.TryReadInputRequest(m_BeforeRequests[i], out SimulationInputRequestState request) &&
                    request.Priority > priority)
                {
                    selected = request;
                    selectedIndex = i;
                    priority = request.Priority;
                }
            }
            return selectedIndex;
        }

        public bool AfterAbility(IGameplayAbilityActivationRulePort port)
        {
            if (Run(m_After, port, null, default))
                return false;
            bool continueRunning = false;
            for (int i = 0; i < m_MovementExits.Length; i++)
            {
                GameplayAbilityExecutionBinding ability = m_MovementExits[i];
                int slots = port.GetSourceSlotCount(ability.SkillId);
                IReadOnlyList<GameplayAbilityMovementExitRuleBinding> rules = ability.MovementExitRules;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    GameplayAbilityMovementExitRuleBinding rule = rules[ruleIndex];
                    GameplayAbilitySourceContext selected = default;
                    ulong selectedStartTick = 0;
                    for (int slot = 0; slot < slots; slot++)
                    {
                        if (!port.TryReadActiveSource(ability.SkillId, slot, out GameplayAbilitySourceContext candidate, out ulong startTick) ||
                            !port.Evaluate(ability.SkillId, rule.Condition, in candidate, GameplayAbilityRuleEvaluationMode.Activation))
                            continue;
                        if (!selected.IsValid || IsEarlier(in candidate, startTick, in selected, selectedStartTick))
                        {
                            selected = candidate;
                            selectedStartTick = startTick;
                        }
                    }
                    if (!selected.IsValid)
                        continue;
                    port.ExitToMovement(ability, rule, in selected);
                    continueRunning = true;
                    break;
                }
            }
            return continueRunning;
        }

        public void QueryAvailability(GameplayAbilityRulePhase phase,
            IGameplayAbilityActivationRulePort port, out GameplayAbilityAvailabilityResult result)
        {
            if (phase == GameplayAbilityRulePhase.BeforeAbility &&
                QueryRules(m_BeforeEvents, port, null, default, out result))
                return;
            SimulationInputRequestState selectedRequest = default;
            string selectedRequestId = null;
            PreparedGameplayAbilityActivationRule[] rules = m_After;
            if (phase == GameplayAbilityRulePhase.BeforeAbility)
            {
                int requestIndex = SelectBeforeRequest(port, out selectedRequest);
                if (requestIndex < 0)
                {
                    result = default;
                    return;
                }
                selectedRequestId = m_BeforeRequests[requestIndex];
                rules = m_BeforeByRequest[requestIndex];
            }
            QueryRules(rules, port, selectedRequestId, in selectedRequest, out result);
        }

        static bool QueryRules(PreparedGameplayAbilityActivationRule[] rules,
            IGameplayAbilityActivationRulePort port, string selectedRequestId,
            in SimulationInputRequestState selectedRequest, out GameplayAbilityAvailabilityResult result)
        {
            ActionAdmissionDecision emptyAdmission = default;
            result = new GameplayAbilityAvailabilityResult(
                GameplayAbilityAvailabilityReason.NoCandidate, null, null, default, default, in emptyAdmission);
            for (int i = 0; i < rules.Length; i++)
            {
                PreparedGameplayAbilityActivationRule prepared = rules[i];
                if (!HasInput(in prepared, port, selectedRequestId, in selectedRequest, out SimulationInputRequestState inputRequest))
                    continue;
                GameplayAbilityAvailabilityReason reason = Evaluate(prepared, port,
                    GameplayAbilityRuleEvaluationMode.Availability, out GameplayAbilitySourceContext source);
                ActionAdmissionDecision admission = default;
                if (reason == GameplayAbilityAvailabilityReason.Allowed)
                {
                    port.Preview(prepared.Ability, prepared.Rule, in source, in inputRequest, out admission);
                    if (!admission.Allowed)
                        reason = GameplayAbilityAvailabilityReason.AdmissionRejected;
                }
                result = new GameplayAbilityAvailabilityResult(reason, prepared.Ability, prepared.Rule, in source, in inputRequest, in admission);
                if (result.Allowed)
                    return true;
                if (reason != GameplayAbilityAvailabilityReason.SourceMissing && reason != GameplayAbilityAvailabilityReason.NotApplicable &&
                    prepared.Rule.OnFailure == GameplayAbilityRuleContinuation.EndPhase)
                    return true;
            }
            return false;
        }

        static bool HasInput(in PreparedGameplayAbilityActivationRule prepared, IGameplayAbilityActivationRulePort port,
            string selectedRequestId, in SimulationInputRequestState selectedRequest, out SimulationInputRequestState inputRequest)
        {
            inputRequest = default;
            if (!string.IsNullOrEmpty(prepared.Rule.TriggerEventId))
                return port.HasGameplayEvent(prepared.Rule.TriggerEventId);
            if (selectedRequest.IsValid)
            {
                if (!port.TryReadInputRequest(selectedRequestId, out SimulationInputRequestState request) ||
                    request.Sequence != selectedRequest.Sequence)
                    return false;
                inputRequest = selectedRequest;
                return true;
            }
            int priority = int.MinValue;
            for (int index = 0; index < prepared.Requests.Length; index++)
            {
                string requestId = prepared.Requests[index];
                if (!port.TryReadInputRequest(requestId, out SimulationInputRequestState request))
                    continue;
                if (request.Priority > priority)
                {
                    inputRequest = request;
                    priority = request.Priority;
                }
            }
            return inputRequest.IsValid;
        }

        static GameplayAbilityAvailabilityReason Evaluate(in PreparedGameplayAbilityActivationRule prepared,
            IGameplayAbilityActivationRulePort port, GameplayAbilityRuleEvaluationMode mode,
            out GameplayAbilitySourceContext source)
        {
            source = default;
            for (int sourceIndex = 0; sourceIndex < prepared.Sources.Length; sourceIndex++)
            {
                CharacterSkillId sourceSkill = prepared.Sources[sourceIndex];
                int slots = port.GetSourceSlotCount(sourceSkill);
                GameplayAbilitySourceContext earliestActive = default;
                GameplayAbilitySourceContext earliestApplicable = default;
                ulong activeStartTick = 0;
                ulong applicableStartTick = 0;
                ulong selectedStartTick = 0;
                for (int slot = 0; slot < slots; slot++)
                {
                    if (!port.TryReadActiveSource(sourceSkill, slot, out GameplayAbilitySourceContext candidate, out ulong startTick))
                        continue;
                    if (!earliestActive.IsValid || IsEarlier(in candidate, startTick, in earliestActive, activeStartTick))
                    {
                        earliestActive = candidate;
                        activeStartTick = startTick;
                    }
                    if (!port.Evaluate(prepared.Ability.SkillId, prepared.Rule.Applicability, in candidate, mode))
                        continue;
                    if (!earliestApplicable.IsValid || IsEarlier(in candidate, startTick, in earliestApplicable, applicableStartTick))
                    {
                        earliestApplicable = candidate;
                        applicableStartTick = startTick;
                    }
                    if (prepared.Rule.OpportunityQuery.IsValid &&
                        !port.HasOpportunity(prepared.Ability.SkillId, prepared.Rule.OpportunityQuery, in candidate) ||
                        !port.Evaluate(prepared.Ability.SkillId, prepared.Rule.Condition, in candidate, mode))
                        continue;
                    if (!source.IsValid || IsEarlier(in candidate, startTick, in source, selectedStartTick))
                    {
                        source = candidate;
                        selectedStartTick = startTick;
                    }
                }
                if (!earliestActive.IsValid)
                    continue;
                if (source.IsValid)
                    return GameplayAbilityAvailabilityReason.Allowed;
                source = earliestApplicable.IsValid ? earliestApplicable : earliestActive;
                return earliestApplicable.IsValid
                    ? GameplayAbilityAvailabilityReason.ConditionRejected : GameplayAbilityAvailabilityReason.NotApplicable;
            }
            if (prepared.Rule.RequiresSource)
                return GameplayAbilityAvailabilityReason.SourceMissing;
            if (!port.Evaluate(prepared.Ability.SkillId, prepared.Rule.Applicability, in source, mode))
                return GameplayAbilityAvailabilityReason.NotApplicable;
            return port.Evaluate(prepared.Ability.SkillId, prepared.Rule.Condition, in source, mode)
                ? GameplayAbilityAvailabilityReason.Allowed : GameplayAbilityAvailabilityReason.ConditionRejected;
        }

        static bool IsEarlier(in GameplayAbilitySourceContext candidate, ulong startTick,
            in GameplayAbilitySourceContext selected, ulong selectedStartTick) =>
            startTick < selectedStartTick || startTick == selectedStartTick && candidate.InstanceId < selected.InstanceId;

        static bool Run(PreparedGameplayAbilityActivationRule[] rules,
            IGameplayAbilityActivationRulePort port, string selectedRequestId, in SimulationInputRequestState selectedRequest)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                PreparedGameplayAbilityActivationRule prepared = rules[i];
                GameplayAbilityExecutionBinding ability = prepared.Ability;
                GameplayAbilityActivationRuleBinding rule = prepared.Rule;
                if (!HasInput(in prepared, port, selectedRequestId, in selectedRequest, out SimulationInputRequestState inputRequest))
                    continue;
                GameplayAbilityAvailabilityReason reason = Evaluate(prepared, port,
                    GameplayAbilityRuleEvaluationMode.Activation, out GameplayAbilitySourceContext source);
                if (reason == GameplayAbilityAvailabilityReason.SourceMissing || reason == GameplayAbilityAvailabilityReason.NotApplicable)
                {
                    port.TraceDecision(ability, rule, in source, in inputRequest, "activation_rule_not_applicable");
                    continue;
                }
                bool allowed = reason == GameplayAbilityAvailabilityReason.Allowed;
                port.TraceDecision(ability, rule, in source, in inputRequest,
                    allowed ? "activation_rule_matched" : "activation_rule_condition_false");
                ActionActivationSubmission submission = allowed
                    ? port.Submit(ability, rule, in source, in inputRequest) : ActionActivationSubmission.Rejected;
                if (submission == ActionActivationSubmission.StartupRejected)
                    return true;
                bool submitted = submission == ActionActivationSubmission.Submitted;
                GameplayAbilityRuleContinuation continuation = submitted ? rule.OnSubmitted : rule.OnFailure;
                if (continuation == GameplayAbilityRuleContinuation.EndPhase)
                    return true;
            }
            return false;
        }
    }

    internal enum ActionActivationSubmission : byte
    {
        Rejected = 0,
        Submitted = 1,
        StartupRejected = 2
    }
}
