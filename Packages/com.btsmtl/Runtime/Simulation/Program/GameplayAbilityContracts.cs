using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    public readonly struct GameplayAbilityExecutionEndRule
    {
        public GameplayAbilityExecutionEndRule(
            string trigger,
            int transition,
            string actionWindowType,
            string reason)
        {
            Trigger = SimulationIdentity.Require(trigger, nameof(trigger));
            if (!IsKnownTrigger(Trigger))
                throw new ArgumentException("Gameplay Ability end trigger is invalid.", nameof(trigger));
            if (!IsTerminalTransition(transition))
                throw new ArgumentOutOfRangeException(nameof(transition));
            Transition = transition;
            ActionWindowType = actionWindowType ?? string.Empty;
            Reason = reason ?? string.Empty;
            if (Trigger == GameplayAbilityEndTriggerNames.ActionWindowClosed &&
                string.IsNullOrWhiteSpace(ActionWindowType))
                throw new ArgumentException("Action window end rule requires a window type.", nameof(actionWindowType));
        }

        public string Trigger { get; }
        public int Transition { get; }
        public string ActionWindowType { get; }
        public string Reason { get; }

        static bool IsTerminalTransition(int transition) =>
            transition == 2 ||
            transition == 3 ||
            transition == 4 ||
            transition == 7;

        static bool IsKnownTrigger(string trigger) =>
            string.Equals(trigger, GameplayAbilityEndTriggerNames.ExecutionCompleted, StringComparison.Ordinal) ||
            string.Equals(trigger, GameplayAbilityEndTriggerNames.CancelRequested, StringComparison.Ordinal) ||
            string.Equals(trigger, GameplayAbilityEndTriggerNames.InterruptRequested, StringComparison.Ordinal) ||
            string.Equals(trigger, GameplayAbilityEndTriggerNames.AbortRequested, StringComparison.Ordinal) ||
            string.Equals(trigger, GameplayAbilityEndTriggerNames.ActionWindowClosed, StringComparison.Ordinal);
    }

    public static class GameplayAbilityEndTriggerNames
    {
        public const string ExecutionCompleted = "ExecutionCompleted";
        public const string CancelRequested = "CancelRequested";
        public const string InterruptRequested = "InterruptRequested";
        public const string AbortRequested = "AbortRequested";
        public const string ActionWindowClosed = "ActionWindowClosed";
    }

    public readonly struct GameplayAbilityDependency : IEquatable<GameplayAbilityDependency>
    {
        public GameplayAbilityDependency(string subgraphIdentity, string callSiteIdentity)
        {
            SubgraphIdentity = SimulationIdentity.Require(subgraphIdentity, nameof(subgraphIdentity));
            CallSiteIdentity = SimulationIdentity.Require(callSiteIdentity, nameof(callSiteIdentity));
        }

        public string SubgraphIdentity { get; }
        public string CallSiteIdentity { get; }
        public bool IsValid => !string.IsNullOrEmpty(SubgraphIdentity) && !string.IsNullOrEmpty(CallSiteIdentity);
        public bool Equals(GameplayAbilityDependency other) =>
            string.Equals(SubgraphIdentity, other.SubgraphIdentity, StringComparison.Ordinal) &&
            string.Equals(CallSiteIdentity, other.CallSiteIdentity, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GameplayAbilityDependency other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SubgraphIdentity, CallSiteIdentity);
    }

    public sealed class GameplayAbilityEntrySignature
    {
        readonly ReadOnlyCollection<CharacterControlParameterDescriptor> m_Parameters;

        public GameplayAbilityEntrySignature(IEnumerable<CharacterControlParameterDescriptor> parameters)
        {
            var values = parameters == null
                ? new List<CharacterControlParameterDescriptor>()
                : new List<CharacterControlParameterDescriptor>(parameters);
            var ids = new HashSet<CharacterControlParameterId>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null || !ids.Add(values[i].Id))
                    throw new ArgumentException("Character skill entry parameters are invalid or duplicated.", nameof(parameters));
            }
            values.Sort((left, right) => left.Id.CompareTo(right.Id));
            m_Parameters = values.AsReadOnly();
        }

        public IReadOnlyList<CharacterControlParameterDescriptor> Parameters => m_Parameters;
    }

    public sealed class GameplayAbilityExecutionBinding
    {
        public GameplayAbilityExecutionBinding(
            CharacterSkillId skillId,
            string admissionProfileId,
            string entryIdentity,
            OperationHandle entryOperation,
            string actionContextId,
            IEnumerable<GameplayAbilityDependency> dependencies,
            IEnumerable<GameplayAbilityExecutionEndRule> endRules,
            IReadOnlyList<GameplayAbilityActivationRuleBinding> activationRules,
            IReadOnlyList<GameplayAbilityMovementExitRuleBinding> movementExitRules,
            bool blockMovingTurn,
            IReadOnlyList<ActionStartupEffect> startupEffects,
            int candidateOrder,
            int sourceOrder,
            IReadOnlyList<string> tags)
        {
            if (!skillId.IsValid || !entryOperation.IsValid)
                throw new ArgumentException("Character SkillProgram binding is incomplete.");
            SkillId = skillId;
            AdmissionProfileId = SimulationIdentity.Require(admissionProfileId, nameof(admissionProfileId));
            EntryIdentity = SimulationIdentity.Require(entryIdentity, nameof(entryIdentity));
            EntryOperation = entryOperation;
            ActionContextId = SimulationIdentity.Require(actionContextId, nameof(actionContextId));
            m_Dependencies = FreezeDependencies(dependencies);
            m_EndRules = FreezeEndRules(endRules);
            ActivationRules = activationRules;
            MovementExitRules = movementExitRules;
            BlockMovingTurn = blockMovingTurn;
            StartupEffects = startupEffects;
            CandidateOrder = candidateOrder;
            SourceOrder = sourceOrder;
            Tags = tags;
        }

        readonly ReadOnlyCollection<GameplayAbilityDependency> m_Dependencies;
        readonly ReadOnlyCollection<GameplayAbilityExecutionEndRule> m_EndRules;

        public CharacterSkillId SkillId { get; }
        public string AdmissionProfileId { get; }
        public string EntryIdentity { get; }
        public OperationHandle EntryOperation { get; }
        public string ActionContextId { get; }
        public bool ConsumeSourceInputRequest { get; private set; }
        public string TargetInputValueId { get; private set; } = string.Empty;
        public string TargetKey { get; private set; } = string.Empty;
        public IReadOnlyList<GameplayAbilityDependency> Dependencies => m_Dependencies;
        public IReadOnlyList<GameplayAbilityExecutionEndRule> EndRules => m_EndRules;
        public IReadOnlyList<GameplayAbilityActivationRuleBinding> ActivationRules { get; }
        public IReadOnlyList<GameplayAbilityMovementExitRuleBinding> MovementExitRules { get; }
        public bool BlockMovingTurn { get; }
        public IReadOnlyList<ActionStartupEffect> StartupEffects { get; }
        public int CandidateOrder { get; }
        public int SourceOrder { get; }
        public IReadOnlyList<string> Tags { get; }

        public GameplayAbilityExecutionBinding WithInputBinding(in GameplayAbilityGrantInputBinding input)
        {
            var binding = (GameplayAbilityExecutionBinding)MemberwiseClone();
            binding.ConsumeSourceInputRequest = input.ConsumeSourceInputRequest;
            binding.TargetInputValueId = input.TargetInputValueId;
            binding.TargetKey = input.TargetKey;
            return binding;
        }

        public bool TryGetEndRule(
            string trigger,
            string actionWindowType,
            out GameplayAbilityExecutionEndRule rule)
        {
            string triggerName = SimulationIdentity.Require(trigger, nameof(trigger));
            string windowType = actionWindowType ?? string.Empty;
            for (int i = 0; i < m_EndRules.Count; i++)
            {
                GameplayAbilityExecutionEndRule candidate = m_EndRules[i];
                if (string.Equals(candidate.Trigger, triggerName, StringComparison.Ordinal) &&
                    (triggerName != GameplayAbilityEndTriggerNames.ActionWindowClosed ||
                     string.Equals(candidate.ActionWindowType, windowType, StringComparison.Ordinal)))
                {
                    rule = candidate;
                    return true;
                }
            }
            rule = default;
            return false;
        }

        static ReadOnlyCollection<GameplayAbilityDependency> FreezeDependencies(
            IEnumerable<GameplayAbilityDependency> dependencies)
        {
            var values = dependencies == null
                ? new List<GameplayAbilityDependency>()
                : new List<GameplayAbilityDependency>(dependencies);
            var seen = new HashSet<GameplayAbilityDependency>();
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].IsValid || !seen.Add(values[i]))
                    throw new ArgumentException("Character SkillProgram dependencies are invalid or duplicated.", nameof(dependencies));
            }
            values.Sort((left, right) =>
            {
                int bySubgraph = string.CompareOrdinal(left.SubgraphIdentity, right.SubgraphIdentity);
                return bySubgraph != 0
                    ? bySubgraph
                    : string.CompareOrdinal(left.CallSiteIdentity, right.CallSiteIdentity);
            });
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<GameplayAbilityExecutionEndRule> FreezeEndRules(
            IEnumerable<GameplayAbilityExecutionEndRule> endRules)
        {
            var values = endRules == null
                ? new List<GameplayAbilityExecutionEndRule>()
                : new List<GameplayAbilityExecutionEndRule>(endRules);
            values.Sort((left, right) =>
            {
                int byTrigger = string.CompareOrdinal(left.Trigger, right.Trigger);
                return byTrigger != 0 ? byTrigger : left.Transition.CompareTo(right.Transition);
            });
            return values.AsReadOnly();
        }
    }

    public sealed class GameplayAbilityExecutionCatalog
    {
        readonly ReadOnlyCollection<GameplayAbilityExecutionBinding> m_Bindings;

        public GameplayAbilityExecutionCatalog(
            IReadOnlyList<ProgramCatalogEntry> entries,
            IReadOnlyList<ProgramReference> references)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (references == null)
                throw new ArgumentNullException(nameof(references));
            var values = new List<GameplayAbilityExecutionBinding>();
            for (int i = 0; i < entries.Count; i++)
            {
                ProgramCatalogEntry entry = entries[i];
                if (entry.Kind != ProgramCatalogEntryKind.AbilityProgram)
                    continue;
                OperationHandle operation = OperationHandle.Invalid;
                for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
                {
                    ProgramReference reference = references[referenceIndex];
                    if (reference.Kind != ProgramReferenceKind.CatalogEntry ||
                        reference.TargetIndex != entry.Index || !reference.SourceOperation.IsValid)
                        continue;
                    if (operation.IsValid)
                        throw new InvalidDataException($"AbilityProgram '{entry.Identity}' has multiple entry operations.");
                    operation = reference.SourceOperation;
                }
                if (!operation.IsValid)
                    throw new InvalidDataException($"AbilityProgram '{entry.Identity}' has no entry operation reference.");
                string skillValue = RequirePrefix(entry.Identity, "ability:");
                string admissionProfileIdentity = RequireIdentity(entry, "AdmissionProfile", "action:");
                if (!ContainsEntry(entries, ProgramCatalogEntryKind.Action, $"action:{admissionProfileIdentity}"))
                    throw new InvalidDataException($"AbilityProgram '{entry.Identity}' references missing admission profile '{admissionProfileIdentity}'.");
                ReadRelations(
                    entry,
                    out List<GameplayAbilityDependency> dependencies,
                    out List<GameplayAbilityExecutionEndRule> endRules);
                values.Add(new GameplayAbilityExecutionBinding(
                    new CharacterSkillId(skillValue),
                    admissionProfileIdentity,
                    RequireIdentity(entry, "EntryIdentity", null),
                    operation,
                    RequireIdentity(entry, "ActionContext", null),
                    dependencies,
                    endRules,
                    ReadActivationRules(entry.Identity, entries, references),
                    ReadMovementExitRules(entry.Identity, entries, references),
                    bool.Parse(RequireIdentity(entry, "BlockMovingTurn", null)),
                    ActionAdmissionProfileCompiler.CompileStartupEffects(entry),
                    int.Parse(RequireIdentity(entry, "CandidateOrder", null)),
                    int.Parse(RequireIdentity(entry, "SourceOrder", null)),
                    ReadTags(entry)));
            }
            values.Sort((left, right) => left.SkillId.CompareTo(right.SkillId));
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i - 1].SkillId == values[i].SkillId)
                    throw new InvalidDataException($"AbilityProgram '{values[i].SkillId}' is duplicated.");
            }
            m_Bindings = values.AsReadOnly();
        }

        public IReadOnlyList<GameplayAbilityExecutionBinding> Bindings => m_Bindings;

        static IReadOnlyList<string> ReadTags(ProgramCatalogEntry entry)
        {
            var fields = new List<ProgramCatalogField>();
            for (int index = 0; index < entry.Fields.Count; index++)
                if (entry.Fields[index].Name.StartsWith("Tags:", StringComparison.Ordinal))
                    fields.Add(entry.Fields[index]);
            fields.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            var tags = new string[fields.Count];
            for (int index = 0; index < fields.Count; index++)
            {
                ProgramCatalogField field = fields[index];
                if (field.Kind != ProgramCatalogFieldKind.Identity ||
                    field.Name != $"Tags:{index:D4}")
                    throw new InvalidDataException("AbilityProgram classification Tags declaration is incomplete.");
                tags[index] = field.Identity;
            }
            return tags;
        }

        static IReadOnlyList<GameplayAbilityActivationRuleBinding> ReadActivationRules(string owner,
            IReadOnlyList<ProgramCatalogEntry> entries, IReadOnlyList<ProgramReference> references)
        {
            var rules = new List<GameplayAbilityActivationRuleBinding>();
            for (int i = 0; i < entries.Count; i++)
            {
                ProgramCatalogEntry entry = entries[i];
                if (entry.Kind != ProgramCatalogEntryKind.AbilityActivationRule ||
                    RequireIdentity(entry, "Ability", null) != owner)
                    continue;
                rules.Add(new GameplayAbilityActivationRuleBinding(
                    RequireIdentity(entry, "RuleId", null),
                    Enum.Parse<GameplayAbilityRulePhase>(RequireIdentity(entry, "Phase", null)),
                    int.Parse(RequireIdentity(entry, "Order", null)),
                    ReadRuleOperation(entry, references, "Applicability", true),
                    ReadRuleOperation(entry, references, "Condition", true),
                    ReadRuleOperation(entry, references, "SourceQuery", false),
                    bool.Parse(RequireIdentity(entry, "RequiresSource", null)),
                    OptionalIdentity(entry, "ActivationEntry", null),
                    Enum.Parse<GameplayAbilityRuleTargetPolicy>(RequireIdentity(entry, "TargetPolicy", null)),
                    Enum.Parse<GameplayAbilityRuleContinuation>(RequireIdentity(entry, "OnFailure", null)),
                    Enum.Parse<GameplayAbilityRuleContinuation>(RequireIdentity(entry, "OnSubmitted", null)),
                    OptionalIdentity(entry, "TriggerEvent", null)));
            }
            rules.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order == 0 ? string.CompareOrdinal(left.Id, right.Id) : order;
            });
            return rules.AsReadOnly();
        }

        static IReadOnlyList<GameplayAbilityMovementExitRuleBinding> ReadMovementExitRules(string owner,
            IReadOnlyList<ProgramCatalogEntry> entries, IReadOnlyList<ProgramReference> references)
        {
            var rules = new List<GameplayAbilityMovementExitRuleBinding>();
            for (int i = 0; i < entries.Count; i++)
            {
                ProgramCatalogEntry entry = entries[i];
                if (entry.Kind != ProgramCatalogEntryKind.AbilityMovementExitRule ||
                    RequireIdentity(entry, "Ability", null) != owner)
                    continue;
                rules.Add(new GameplayAbilityMovementExitRuleBinding(
                    RequireIdentity(entry, "RuleId", null),
                    int.Parse(RequireIdentity(entry, "Order", null)),
                    ReadRuleOperation(entry, references, "Condition", true),
                    RequireIdentity(entry, "Window", null),
                    RequireIdentity(entry, "Reason", null)));
            }
            rules.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order == 0 ? string.CompareOrdinal(left.Id, right.Id) : order;
            });
            return rules.AsReadOnly();
        }

        static OperationHandle ReadRuleOperation(ProgramCatalogEntry entry,
            IReadOnlyList<ProgramReference> references, string role, bool required)
        {
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (reference.Kind == ProgramReferenceKind.CatalogEntry &&
                    reference.TargetIndex == entry.Index && reference.ExternalIdentity == role)
                    return reference.SourceOperation;
            }
            if (required)
                throw new InvalidDataException($"Ability规则'{entry.Identity}'缺少'{role}'条件图引用。");
            return OperationHandle.Invalid;
        }

        public GameplayAbilityExecutionBinding Require(CharacterSkillId skillId)
        {
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                if (m_Bindings[i].SkillId == skillId)
                    return m_Bindings[i];
            }
            throw new InvalidOperationException($"AbilityProgram '{skillId}' is absent from the Program catalog.");
        }

        static string RequirePrefix(string value, string prefix)
        {
            if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException($"AbilityProgram identity '{value}' has no '{prefix}' prefix.");
            return SimulationIdentity.Require(value.Substring(prefix.Length), nameof(value));
        }

        static bool ContainsEntry(
            IReadOnlyList<ProgramCatalogEntry> entries,
            ProgramCatalogEntryKind kind,
            string identity)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Kind == kind && string.Equals(entries[i].Identity, identity, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static string RequireIdentity(ProgramCatalogEntry entry, string name, string prefix)
        {
            string value = OptionalIdentity(entry, name, prefix);
            return value.Length == 0
                ? throw new InvalidDataException($"AbilityProgram '{entry.Identity}' has no '{name}' field.")
                : value;
        }

        static string OptionalIdentity(ProgramCatalogEntry entry, string name, string prefix)
        {
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                ProgramCatalogField field = entry.Fields[i];
                if (!string.Equals(field.Name, name, StringComparison.Ordinal))
                    continue;
                if (field.Kind != ProgramCatalogFieldKind.Identity)
                    throw new InvalidDataException($"AbilityProgram '{entry.Identity}' field '{name}' is not an identity.");
                if (string.IsNullOrEmpty(field.Identity))
                    return string.Empty;
                if (prefix != null)
                    return RequirePrefix(field.Identity, prefix);
                return field.Identity;
            }
            return string.Empty;
        }

        static void ReadRelations(
            ProgramCatalogEntry entry,
            out List<GameplayAbilityDependency> dependencies,
            out List<GameplayAbilityExecutionEndRule> endRules)
        {
            var subgraphs = new Dictionary<int, string>();
            var callSites = new Dictionary<int, string>();
            var endRuleValues = new Dictionary<int, EndRuleData>();
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                ProgramCatalogField field = entry.Fields[i];
                if (field.Name.StartsWith("Dependency:", StringComparison.Ordinal))
                {
                    if (field.Kind != ProgramCatalogFieldKind.Identity)
                        throw new InvalidDataException($"SkillProgram '{entry.Identity}' dependency field '{field.Name}' is not an identity.");
                    ReadDependencyField(entry, field, out int index, out bool subgraph);
                    var destination = subgraph ? subgraphs : callSites;
                    if (!destination.TryAdd(index, field.Identity))
                        throw new InvalidDataException($"SkillProgram '{entry.Identity}' dependency index '{index}' is duplicated.");
                    continue;
                }
                if (field.Name.StartsWith("EndRule:", StringComparison.Ordinal))
                    ReadEndRuleField(entry, field, endRuleValues);
            }

            dependencies = new List<GameplayAbilityDependency>();
            var dependencyIndexes = new HashSet<int>(subgraphs.Keys);
            dependencyIndexes.UnionWith(callSites.Keys);
            foreach (int index in dependencyIndexes.OrderBy(value => value))
            {
                if (!subgraphs.TryGetValue(index, out string subgraph) || !callSites.TryGetValue(index, out string callSite))
                    throw new InvalidDataException($"SkillProgram '{entry.Identity}' dependency index '{index}' is incomplete.");
                dependencies.Add(new GameplayAbilityDependency(subgraph, callSite));
            }
            endRules = endRuleValues
                .OrderBy(value => value.Key)
                .Select(value => new GameplayAbilityExecutionEndRule(value.Value.Trigger, value.Value.Transition, value.Value.ActionWindowType, value.Value.Reason))
                .ToList();
        }

        static void ReadEndRuleField(
            ProgramCatalogEntry entry,
            ProgramCatalogField field,
            IDictionary<int, EndRuleData> values)
        {
            if (field.Kind != ProgramCatalogFieldKind.Identity)
                throw new InvalidDataException($"AbilityProgram '{entry.Identity}' end rule field '{field.Name}' is not an identity.");
            string suffix = field.Name.Substring("EndRule:".Length);
            int separator = suffix.IndexOf(':');
            if (separator <= 0 || !int.TryParse(suffix.Substring(0, separator), out int index) || index < 0)
                throw new InvalidDataException($"AbilityProgram '{entry.Identity}' end rule field '{field.Name}' is malformed.");
            string component = suffix.Substring(separator + 1);
            if (!values.TryGetValue(index, out EndRuleData current))
                current = new EndRuleData();
            string trigger = current.Trigger;
            int transition = current.Transition;
            string actionWindowType = current.ActionWindowType;
            string reason = current.Reason;
            switch (component)
            {
                case "Trigger":
                    trigger = field.Identity;
                    break;
                case "Transition":
                    if (!int.TryParse(field.Identity, out transition) || transition <= 0)
                        throw new InvalidDataException($"AbilityProgram '{entry.Identity}' end rule field '{field.Name}' has an invalid transition.");
                    break;
                case "Window":
                    actionWindowType = field.Identity;
                    break;
                case "Reason":
                    reason = field.Identity;
                    break;
                default:
                    throw new InvalidDataException($"AbilityProgram '{entry.Identity}' end rule field '{field.Name}' has an unknown component.");
            }
            values[index] = new EndRuleData { Trigger = trigger, Transition = transition, ActionWindowType = actionWindowType, Reason = reason };
        }

        sealed class EndRuleData
        {
            public string Trigger = "ExecutionCompleted";
            public int Transition = 2;
            public string ActionWindowType = string.Empty;
            public string Reason = string.Empty;
        }

        static void ReadDependencyField(
            ProgramCatalogEntry entry,
            ProgramCatalogField field,
            out int index,
            out bool subgraph)
        {
            string suffix = field.Name.Substring("Dependency:".Length);
            int separator = suffix.IndexOf(':');
            if (separator <= 0)
                throw new InvalidDataException($"SkillProgram '{entry.Identity}' dependency field '{field.Name}' is malformed.");
            if (!int.TryParse(suffix.Substring(0, separator), out index) || index < 0)
                throw new InvalidDataException($"SkillProgram '{entry.Identity}' dependency field '{field.Name}' has an invalid index.");
            string component = suffix.Substring(separator + 1);
            if (string.Equals(component, "Subgraph", StringComparison.Ordinal))
                subgraph = true;
            else if (string.Equals(component, "CallSite", StringComparison.Ordinal))
                subgraph = false;
            else
                throw new InvalidDataException($"SkillProgram '{entry.Identity}' dependency field '{field.Name}' has an unknown component.");
        }

        static int ReadIndexedField(ProgramCatalogEntry entry, string fieldName, string prefix)
        {
            string value = fieldName.Substring(prefix.Length);
            if (!int.TryParse(value, out int index) || index < 0)
                throw new InvalidDataException($"SkillProgram '{entry.Identity}' field '{fieldName}' has an invalid index.");
            return index;
        }
    }

    public enum GameplayAbilityProviderKind : byte
    {
        Input = 1,
        GameplayEffect = 2,
        Equipment = 3,
        CharacterState = 4
    }

    public enum GameplayAbilityProviderValueKind : byte
    {
        None = 0,
        Boolean = 1,
        Number = 2,
        Vector2 = 3,
        Vector3 = 4,
        Yaw = 5,
        ActionTargetSnapshot = 6
    }

    public static class GameplayAbilityProviderContractVersions
    {
        public const int Input = 1;
        public const int GameplayEffect = 1;
        public const int Equipment = 1;
        public const int CharacterState = 1;

        public static int Require(GameplayAbilityProviderKind kind) => kind switch
        {
            GameplayAbilityProviderKind.Input => Input,
            GameplayAbilityProviderKind.GameplayEffect => GameplayEffect,
            GameplayAbilityProviderKind.Equipment => Equipment,
            GameplayAbilityProviderKind.CharacterState => CharacterState,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public readonly struct GameplayAbilityProviderRequirement : IEquatable<GameplayAbilityProviderRequirement>
    {
        public GameplayAbilityProviderRequirement(
            GameplayAbilityProviderKind kind,
            string dependencyIdentity,
            string providerIdentity,
            string memberIdentity,
            GameplayAbilityProviderValueKind valueKind,
            int memberRevision)
        {
            if (!Enum.IsDefined(typeof(GameplayAbilityProviderKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(GameplayAbilityProviderValueKind), valueKind) || memberRevision <= 0)
                throw new ArgumentOutOfRangeException(nameof(memberRevision));
            Kind = kind;
            DependencyIdentity = SimulationIdentity.Require(dependencyIdentity, nameof(dependencyIdentity));
            ProviderIdentity = RequireProviderIdentity(kind, providerIdentity);
            MemberIdentity = SimulationIdentity.Require(memberIdentity, nameof(memberIdentity));
            ValueKind = valueKind;
            MemberRevision = memberRevision;
            ProviderSemanticVersion = GameplayAbilityProviderContractVersions.Require(kind);
        }

        public GameplayAbilityProviderKind Kind { get; }
        public string DependencyIdentity { get; }
        public string ProviderIdentity { get; }
        public string MemberIdentity { get; }
        public GameplayAbilityProviderValueKind ValueKind { get; }
        public int MemberRevision { get; }
        public int ProviderSemanticVersion { get; }

        public bool Equals(GameplayAbilityProviderRequirement other) =>
            Kind == other.Kind &&
            string.Equals(DependencyIdentity, other.DependencyIdentity, StringComparison.Ordinal) &&
            string.Equals(ProviderIdentity, other.ProviderIdentity, StringComparison.Ordinal) &&
            string.Equals(MemberIdentity, other.MemberIdentity, StringComparison.Ordinal) &&
            ValueKind == other.ValueKind &&
            MemberRevision == other.MemberRevision &&
            ProviderSemanticVersion == other.ProviderSemanticVersion;

        public override bool Equals(object obj) =>
            obj is GameplayAbilityProviderRequirement other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Kind, DependencyIdentity, ProviderIdentity, MemberIdentity, ValueKind, MemberRevision, ProviderSemanticVersion);

        internal static string RequireProviderIdentity(GameplayAbilityProviderKind kind, string value)
        {
            string identity = SimulationIdentity.Require(value, nameof(value));
            bool valid = kind == GameplayAbilityProviderKind.CharacterState
                ? CharacterStateProviderFields.IsOwner(identity)
                : CharacterSkillProviderOwners.IsAssetOwner(identity);
            if (!valid)
                throw new ArgumentException($"Gameplay Ability provider '{identity}' is invalid for '{kind}'.", nameof(value));
            return identity;
        }
    }

    public readonly struct GameplayAbilityProviderMemberBinding
    {
        public GameplayAbilityProviderMemberBinding(
            GameplayAbilityProviderKind kind,
            string providerIdentity,
            string memberIdentity,
            GameplayAbilityProviderValueKind valueKind,
            int memberRevision,
            string runtimeHandle)
        {
            if (!Enum.IsDefined(typeof(GameplayAbilityProviderKind), kind) ||
                !Enum.IsDefined(typeof(GameplayAbilityProviderValueKind), valueKind) ||
                memberRevision <= 0)
                throw new ArgumentOutOfRangeException();
            Kind = kind;
            ProviderIdentity = GameplayAbilityProviderRequirement.RequireProviderIdentity(kind, providerIdentity);
            MemberIdentity = SimulationIdentity.Require(memberIdentity, nameof(memberIdentity));
            ValueKind = valueKind;
            MemberRevision = memberRevision;
            RuntimeHandle = SimulationIdentity.Require(runtimeHandle, nameof(runtimeHandle));
        }

        public GameplayAbilityProviderKind Kind { get; }
        public string ProviderIdentity { get; }
        public string MemberIdentity { get; }
        public GameplayAbilityProviderValueKind ValueKind { get; }
        public int MemberRevision { get; }
        public string RuntimeHandle { get; }
    }

    public readonly struct GameplayAbilityProviderBindingEntry
    {
        public GameplayAbilityProviderBindingEntry(
            GameplayAbilityProviderKind kind,
            string providerIdentity,
            int semanticVersion,
            IEnumerable<GameplayAbilityProviderMemberBinding> members)
        {
            if (!Enum.IsDefined(typeof(GameplayAbilityProviderKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (semanticVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(semanticVersion));
            Kind = kind;
            ProviderIdentity = GameplayAbilityProviderRequirement
                .RequireProviderIdentity(kind, providerIdentity);
            SemanticVersion = semanticVersion;
            var values = new List<GameplayAbilityProviderMemberBinding>(members ?? Array.Empty<GameplayAbilityProviderMemberBinding>());
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                GameplayAbilityProviderMemberBinding member = values[i];
                if (member.Kind != kind || !string.Equals(member.ProviderIdentity, ProviderIdentity, StringComparison.Ordinal) ||
                    !identities.Add(member.MemberIdentity))
                    throw new ArgumentException("Gameplay Ability provider members are invalid or duplicated.", nameof(members));
            }
            values.Sort((left, right) => string.CompareOrdinal(left.MemberIdentity, right.MemberIdentity));
            Members = new ReadOnlyCollection<GameplayAbilityProviderMemberBinding>(values);
        }

        public GameplayAbilityProviderKind Kind { get; }
        public string ProviderIdentity { get; }
        public int SemanticVersion { get; }
        public IReadOnlyList<GameplayAbilityProviderMemberBinding> Members { get; }

        public bool TryGetMember(string memberIdentity, out GameplayAbilityProviderMemberBinding member)
        {
            string identity = memberIdentity ?? string.Empty;
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].MemberIdentity, identity, StringComparison.Ordinal))
                {
                    member = Members[i];
                    return true;
                }
            }
            member = default;
            return false;
        }
    }

    public sealed class GameplayAbilityProviderBinding
    {
        readonly ReadOnlyDictionary<GameplayAbilityProviderKind, GameplayAbilityProviderBindingEntry> m_Providers;

        public GameplayAbilityProviderBinding(IEnumerable<GameplayAbilityProviderBindingEntry> providers)
        {
            var values = new Dictionary<GameplayAbilityProviderKind, GameplayAbilityProviderBindingEntry>();
            foreach (GameplayAbilityProviderBindingEntry provider in providers ?? Array.Empty<GameplayAbilityProviderBindingEntry>())
            {
                if (!values.TryAdd(provider.Kind, provider))
                    throw new ArgumentException($"Gameplay Ability provider '{provider.Kind}' is duplicated.", nameof(providers));
            }
            m_Providers = new ReadOnlyDictionary<GameplayAbilityProviderKind, GameplayAbilityProviderBindingEntry>(values);
        }

        public bool TryGet(
            GameplayAbilityProviderKind kind,
            out string providerIdentity)
        {
            if (m_Providers.TryGetValue(kind, out GameplayAbilityProviderBindingEntry provider))
            {
                providerIdentity = provider.ProviderIdentity;
                return true;
            }
            providerIdentity = string.Empty;
            return false;
        }

        public bool TryGetEntry(
            GameplayAbilityProviderKind kind,
            out GameplayAbilityProviderBindingEntry provider) =>
            m_Providers.TryGetValue(kind, out provider);

        public bool TryGetMember(
            GameplayAbilityProviderKind kind,
            string memberIdentity,
            out GameplayAbilityProviderMemberBinding member)
        {
            if (m_Providers.TryGetValue(kind, out GameplayAbilityProviderBindingEntry provider))
                return provider.TryGetMember(memberIdentity, out member);
            member = default;
            return false;
        }
    }

    public sealed class GameplayAbilityProviderContract
    {
        readonly ReadOnlyCollection<GameplayAbilityProviderRequirement> m_Requirements;

        GameplayAbilityProviderContract(IEnumerable<GameplayAbilityProviderRequirement> requirements)
        {
            var values = new List<GameplayAbilityProviderRequirement>(requirements ?? Array.Empty<GameplayAbilityProviderRequirement>());
            values.Sort((left, right) =>
            {
                int byKind = left.Kind.CompareTo(right.Kind);
                return byKind != 0
                    ? byKind
                    : string.CompareOrdinal(left.DependencyIdentity, right.DependencyIdentity);
            });
            for (int i = 1; i < values.Count; i++)
            {
                GameplayAbilityProviderRequirement previous = values[i - 1];
                GameplayAbilityProviderRequirement current = values[i];
                if (previous.Kind != current.Kind ||
                    !string.Equals(previous.DependencyIdentity, current.DependencyIdentity, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(previous.ProviderIdentity, current.ProviderIdentity, StringComparison.Ordinal))
                    throw new InvalidDataException($"Gameplay Ability dependency '{current.DependencyIdentity}' binds multiple providers.");
                values.RemoveAt(i--);
            }
            m_Requirements = values.AsReadOnly();
        }

        public IReadOnlyList<GameplayAbilityProviderRequirement> Requirements => m_Requirements;

        public static GameplayAbilityProviderContract Create(
            IReadOnlyList<ProgramCatalogEntry> catalogEntries,
            Func<int, int> readInt32Constant)
        {
            if (catalogEntries == null)
                throw new ArgumentNullException(nameof(catalogEntries));
            if (readInt32Constant == null)
                throw new ArgumentNullException(nameof(readInt32Constant));
            var requirements = new List<GameplayAbilityProviderRequirement>();
            for (int i = 0; i < catalogEntries.Count; i++)
            {
                ProgramCatalogEntry entry = catalogEntries[i];
                if (!TryResolveKind(entry.Kind, out GameplayAbilityProviderKind kind))
                    continue;
                requirements.Add(new GameplayAbilityProviderRequirement(
                    kind,
                    entry.Identity,
                    RequireProviderIdentity(entry),
                    entry.Identity,
                    ResolveValueKind(entry, kind, readInt32Constant),
                    entry.Revision));
            }
            return new GameplayAbilityProviderContract(requirements);
        }

        public void RequireBinding(GameplayAbilityProviderBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            for (int i = 0; i < m_Requirements.Count; i++)
            {
                GameplayAbilityProviderRequirement requirement = m_Requirements[i];
                if (!binding.TryGetEntry(requirement.Kind, out GameplayAbilityProviderBindingEntry provider))
                    throw new InvalidOperationException(
                        $"Gameplay Ability dependency '{requirement.DependencyIdentity}' requires unbound provider '{requirement.ProviderIdentity}' of kind '{requirement.Kind}'.");
                if (!string.Equals(provider.ProviderIdentity, requirement.ProviderIdentity, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Gameplay Ability dependency '{requirement.DependencyIdentity}' requires provider '{requirement.ProviderIdentity}', but binding supplies '{provider.ProviderIdentity}'.");
                if (provider.SemanticVersion != requirement.ProviderSemanticVersion)
                    throw new InvalidOperationException(
                        $"Gameplay Ability dependency '{requirement.DependencyIdentity}' requires provider version '{requirement.ProviderSemanticVersion}', but binding supplies '{provider.SemanticVersion}'.");
                if (!provider.TryGetMember(requirement.MemberIdentity, out GameplayAbilityProviderMemberBinding member))
                    throw new InvalidOperationException(
                        $"Gameplay Ability dependency '{requirement.DependencyIdentity}' requires missing provider member '{requirement.MemberIdentity}'.");
                if (member.ValueKind != requirement.ValueKind || member.MemberRevision != requirement.MemberRevision || string.IsNullOrEmpty(member.RuntimeHandle))
                    throw new InvalidOperationException(
                        $"Gameplay Ability dependency '{requirement.DependencyIdentity}' provider member '{requirement.MemberIdentity}' has an incompatible contract.");
            }
        }

        static GameplayAbilityProviderValueKind ResolveValueKind(
            ProgramCatalogEntry entry,
            GameplayAbilityProviderKind kind,
            Func<int, int> readInt32Constant)
        {
            if (entry.Kind == ProgramCatalogEntryKind.InputValue)
            {
                int raw = readInt32Constant == null
                    ? throw new ArgumentNullException(nameof(readInt32Constant))
                    : readInt32Constant(RequireInt32Field(entry, "ValueType"));
                return (ProgramInputValueKind)raw switch
                {
                    ProgramInputValueKind.Boolean => GameplayAbilityProviderValueKind.Boolean,
                    ProgramInputValueKind.Scalar => GameplayAbilityProviderValueKind.Number,
                    ProgramInputValueKind.Vector2 => GameplayAbilityProviderValueKind.Vector2,
                    ProgramInputValueKind.Vector3 => GameplayAbilityProviderValueKind.Vector3,
                    ProgramInputValueKind.Yaw => GameplayAbilityProviderValueKind.Yaw,
                    ProgramInputValueKind.ActionTargetSnapshot => GameplayAbilityProviderValueKind.ActionTargetSnapshot,
                    _ => throw new InvalidDataException($"Gameplay Ability input provider member '{entry.Identity}' has an invalid value kind.")
                };
            }
            if (entry.Kind == ProgramCatalogEntryKind.GameplayTag)
                return GameplayAbilityProviderValueKind.Boolean;
            if (entry.Kind == ProgramCatalogEntryKind.Attribute)
                return GameplayAbilityProviderValueKind.Number;
            if (entry.Kind == ProgramCatalogEntryKind.CharacterState)
            {
                string field = entry.Identity.StartsWith("character-state:", StringComparison.Ordinal)
                    ? entry.Identity.Substring("character-state:".Length)
                    : string.Empty;
                if (string.Equals(field, "move-facing-angle", StringComparison.Ordinal))
                    return GameplayAbilityProviderValueKind.Number;
                return ToProviderValueKind(CharacterStateProviderFields.ValueKind(field));
            }
            return GameplayAbilityProviderValueKind.None;
        }

        static int RequireInt32Field(ProgramCatalogEntry entry, string name)
        {
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                ProgramCatalogField field = entry.Fields[i];
                if (string.Equals(field.Name, name, StringComparison.Ordinal) && field.Kind == ProgramCatalogFieldKind.Constant)
                    return field.ConstantIndex;
            }
            throw new InvalidDataException($"Gameplay Ability provider member '{entry.Identity}' has no '{name}' field.");
        }

        static GameplayAbilityProviderValueKind ToProviderValueKind(SemanticValueKind kind) => kind switch
        {
            SemanticValueKind.Boolean => GameplayAbilityProviderValueKind.Boolean,
            SemanticValueKind.Number => GameplayAbilityProviderValueKind.Number,
            SemanticValueKind.Vector2 => GameplayAbilityProviderValueKind.Vector2,
            SemanticValueKind.Vector3 => GameplayAbilityProviderValueKind.Vector3,
            SemanticValueKind.Yaw => GameplayAbilityProviderValueKind.Yaw,
            _ => throw new InvalidDataException($"Gameplay Ability Character State provider member kind '{kind}' is unsupported.")
        };

        static bool TryResolveKind(
            ProgramCatalogEntryKind kind,
            out GameplayAbilityProviderKind providerKind)
        {
            switch (kind)
            {
                case ProgramCatalogEntryKind.InputValue:
                case ProgramCatalogEntryKind.InputRequest:
                    providerKind = GameplayAbilityProviderKind.Input;
                    return true;
                case ProgramCatalogEntryKind.GameplayTag:
                case ProgramCatalogEntryKind.Attribute:
                case ProgramCatalogEntryKind.GameplayEffect:
                    providerKind = GameplayAbilityProviderKind.GameplayEffect;
                    return true;
                case ProgramCatalogEntryKind.EquipmentSlot:
                case ProgramCatalogEntryKind.EquipmentRoute:
                case ProgramCatalogEntryKind.EquipmentFeature:
                case ProgramCatalogEntryKind.EquipmentFeatureParameter:
                case ProgramCatalogEntryKind.EquipmentFeatureLocalState:
                case ProgramCatalogEntryKind.EquipmentRouteImplementation:
                case ProgramCatalogEntryKind.EquipmentDefinition:
                case ProgramCatalogEntryKind.EquipmentParameterValue:
                case ProgramCatalogEntryKind.EquipmentInitialLoadout:
                case ProgramCatalogEntryKind.EquipmentVisualBinding:
                    providerKind = GameplayAbilityProviderKind.Equipment;
                    return true;
                case ProgramCatalogEntryKind.CharacterState:
                    providerKind = GameplayAbilityProviderKind.CharacterState;
                    return true;
                default:
                    providerKind = default;
                    return false;
            }
        }

        static string RequireProviderIdentity(ProgramCatalogEntry entry)
        {
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                ProgramCatalogField field = entry.Fields[i];
                if (!string.Equals(field.Name, "ProviderOwner", StringComparison.Ordinal))
                    continue;
                if (field.Kind != ProgramCatalogFieldKind.Identity || string.IsNullOrWhiteSpace(field.Identity))
                    throw new InvalidDataException($"Gameplay Ability dependency '{entry.Identity}' has an invalid ProviderOwner field.");
                return field.Identity;
            }
            throw new InvalidDataException($"Gameplay Ability dependency '{entry.Identity}' has no ProviderOwner field.");
        }
    }
}
