using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    internal readonly struct SimulationInputRequestState : IEquatable<SimulationInputRequestState>
    {
        public SimulationInputRequestState(
            CanonicalTextValue requestId,
            ulong sequence,
            ulong sourceTick,
            ulong expireTick,
            int priority,
            bool consumed = false)
        {
            RequestId = requestId.IsNull ? CanonicalTextValue.Empty : requestId;
            Sequence = sequence;
            SourceTick = sourceTick;
            ExpireTick = expireTick;
            Priority = priority;
            Consumed = consumed;
        }

        public CanonicalTextValue RequestId { get; }
        public ulong Sequence { get; }
        public ulong SourceTick { get; }
        public ulong ExpireTick { get; }
        public int Priority { get; }
        public bool Consumed { get; }
        public bool Equals(SimulationInputRequestState other) =>
            CanonicalTextValue.Equals(RequestId, other.RequestId, StringComparison.Ordinal) &&
            Sequence == other.Sequence &&
            SourceTick == other.SourceTick &&
            ExpireTick == other.ExpireTick &&
            Priority == other.Priority &&
            Consumed == other.Consumed;
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(RequestId) && Sequence != 0;

        public void Retain() => RequestId.Retain();
        public void Release() => RequestId.Release();

        public SimulationInputRequestState Consume() =>
            IsValid
                ? new SimulationInputRequestState(RequestId, Sequence, SourceTick, ExpireTick, Priority, true)
                : this;
    }

    internal static class SimulationInputRequestStateCodec
    {
        public static void Write(CanonicalWriter writer, SimulationInputRequestState value)
        {
            writer.WriteBoolean(value.IsValid);
            if (!value.IsValid)
                return;
            writer.WriteText(value.RequestId);
            writer.WriteUInt64(value.Sequence);
            writer.WriteUInt64(value.SourceTick);
            writer.WriteUInt64(value.ExpireTick);
            writer.WriteInt32(value.Priority);
            writer.WriteBoolean(value.Consumed);
        }

        public static SimulationInputRequestState Read(CanonicalReader reader)
        {
            if (!reader.ReadBoolean())
                return default;
            var value = new SimulationInputRequestState(
                reader.ReadText(),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                reader.ReadInt32(),
                reader.ReadBoolean());
            if (!value.IsValid)
                throw new InvalidDataException("Simulation input request state identity is invalid.");
            return value;
        }
    }

    internal enum ActionAdmissionEvaluationMode : byte
    {
        PreviewReplacement = 1,
        CommitActivation = 2
    }

    public enum ActionAdmissionRejectReason : byte
    {
        None = 0,
        TargetBlocked = 1,
        ActiveSourceNotCancelable = 2,
        SourceActionStillActive = 3,
        TargetSnapshotRequired = 4,
        RequiredTagsMissing = 5,
        ActionCapacityExceeded = 6,
        ReplacementSourceMissing = 7,
        StartupRequirementsFailed = 8,
        ActivationRequestPending = 9
    }

    internal readonly struct ActionAdmissionActiveAction
    {
        public ActionAdmissionActiveAction(string actionId, in GameplayAbilitySourceContext sourceContext)
        {
            ActionId = actionId;
            SourceContext = sourceContext;
        }

        public string ActionId { get; }
        public GameplayAbilitySourceContext SourceContext { get; }
        public ulong InstanceId => SourceContext.InstanceId;
        public CharacterSkillId SkillId => SourceContext.SkillId;
    }

    internal readonly struct ActionAdmissionTargetCandidate
    {
        public ActionAdmissionTargetCandidate(CanonicalTextValue targetId)
        {
            TargetId = targetId.IsNull ? CanonicalTextValue.Empty : targetId;
        }

        public CanonicalTextValue TargetId { get; }
        public bool HasTarget => !TargetId.IsEmpty;
    }

    internal readonly struct ActionAdmissionRequest
    {
        public ActionAdmissionRequest(
            ActionAdmissionProfile targetProfile,
            ActionAdmissionTargetCandidate targetCandidate,
            ActionAdmissionEvaluationMode mode,
            ActionAdmissionActiveAction replacementActiveAction = default,
            ulong executingActionInstanceId = 0)
        {
            TargetProfile = targetProfile ?? throw new ArgumentNullException(nameof(targetProfile));
            TargetCandidate = targetCandidate;
            Mode = mode;
            ReplacementActiveAction = replacementActiveAction;
            ExecutingActionInstanceId = executingActionInstanceId;
        }

        public ActionAdmissionProfile TargetProfile { get; }
        public ActionAdmissionTargetCandidate TargetCandidate { get; }
        public ActionAdmissionEvaluationMode Mode { get; }
        public ActionAdmissionActiveAction ReplacementActiveAction { get; }
        public GameplayAbilitySourceContext ReplacementSource => ReplacementActiveAction.SourceContext;
        public ulong ExecutingActionInstanceId { get; }
    }

    internal readonly struct ActionAdmissionDecision
    {
        public ActionAdmissionDecision(
            bool allowed,
            ActionAdmissionRejectReason rejectReason,
            string activeSourceActionId,
            ulong activeSourceActionInstanceId = 0,
            GameplayEffectApplyResult startupFailure = default)
        {
            if (allowed && rejectReason != ActionAdmissionRejectReason.None)
                throw new ArgumentException("Allowed Action admission cannot carry a rejection reason.", nameof(rejectReason));
            if (!allowed && rejectReason == ActionAdmissionRejectReason.None)
                throw new ArgumentException("Rejected Action admission requires a reason.", nameof(rejectReason));
            Allowed = allowed;
            RejectReason = rejectReason;
            ActiveSourceActionId = activeSourceActionId ?? string.Empty;
            ActiveSourceActionInstanceId = activeSourceActionInstanceId;
            StartupFailure = startupFailure;
        }

        public bool Allowed { get; }
        public ActionAdmissionRejectReason RejectReason { get; }
        public string ActiveSourceActionId { get; }
        public ulong ActiveSourceActionInstanceId { get; }
        public GameplayEffectApplyResult StartupFailure { get; }
    }

    internal interface IActionAdmissionReadPort
    {
        IReadOnlyList<string> OwnedGameplayTags { get; }
        int GameplayTagCapacity { get; }
        int ActionCount { get; }
        bool TryReadActiveAction(int index, out ActionAdmissionActiveAction action);
        IReadOnlyList<string> ReadAbilityTags(CharacterSkillId skillId);
        bool TryGetGameplayTagParent(string tag, out string parentTag);
        GameplayEffectApplyResult EvaluateStartupEffects();
    }

    internal sealed class ActionTagQuery
    {
        public ActionTagQuery(string[] all, string[] any, string[] none)
        {
            All = all ?? Array.Empty<string>();
            Any = any ?? Array.Empty<string>();
            None = none ?? Array.Empty<string>();
        }

        public string[] All { get; }
        public string[] Any { get; }
        public string[] None { get; }
        public bool IsEmpty => All.Length == 0 && Any.Length == 0 && None.Length == 0;
    }

    internal sealed class ActionAdmissionProfile
    {
        public ActionAdmissionProfile(
            string actionId,
            ActionTargetRequirement targetRequirement,
            int maxConcurrentInstances,
            string[] grantedTags,
            ActionTagQuery required,
            ActionTagQuery block,
            ActionTagQuery cancel)
        {
            ActionId = SimulationIdentity.Require(actionId, nameof(actionId));
            if (!Enum.IsDefined(typeof(ActionTargetRequirement), targetRequirement))
                throw new ArgumentOutOfRangeException(nameof(targetRequirement));
            if (maxConcurrentInstances <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentInstances));
            TargetRequirement = targetRequirement;
            MaxConcurrentInstances = maxConcurrentInstances;
            GrantedTags = grantedTags;
            Required = required ?? throw new ArgumentNullException(nameof(required));
            Block = block ?? throw new ArgumentNullException(nameof(block));
            Cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
        }

        public string ActionId { get; }
        public ActionTargetRequirement TargetRequirement { get; }
        public int MaxConcurrentInstances { get; }
        public string[] GrantedTags { get; }
        public ActionTagQuery Required { get; }
        public ActionTagQuery Block { get; }
        public ActionTagQuery Cancel { get; }
    }

    internal static class ActionAdmissionProfileCompiler
    {
        const string ActionPrefix = "action:";

        internal static IReadOnlyList<ActionStartupEffect> CompileStartupEffects(ProgramCatalogEntry entry)
        {
            if (entry.Revision != 3)
                throw new InvalidDataException("Ability startup declaration version is invalid.");
            var effects = new List<ActionStartupEffect>();
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                ProgramCatalogField field = entry.Fields[i];
                if (!field.Name.StartsWith("Startup:", StringComparison.Ordinal))
                    continue;
                string prefix = $"Startup:{effects.Count:D4}:";
                if (field.Name != prefix + "Effect" || field.Kind != ProgramCatalogFieldKind.Identity ||
                    !field.Identity.StartsWith("effect:", StringComparison.Ordinal))
                    throw new InvalidDataException("Ability startup effect declaration is incomplete.");
                var parameters = new List<string>();
                var constants = new List<int>();
                string parameterPrefix = prefix + "Parameter:";
                while (i + 1 < entry.Fields.Count && entry.Fields[i + 1].Name.StartsWith(parameterPrefix, StringComparison.Ordinal))
                {
                    field = entry.Fields[++i];
                    if (field.Kind != ProgramCatalogFieldKind.Constant || field.Name.Length == parameterPrefix.Length)
                        throw new InvalidDataException("Ability startup parameter declaration is invalid.");
                    parameters.Add(field.Name.Substring(parameterPrefix.Length));
                    constants.Add(field.ConstantIndex);
                }
                effects.Add(new ActionStartupEffect(entry.Fields[i - parameters.Count].Identity.Substring("effect:".Length),
                    parameters.ToArray(), constants.ToArray()));
            }
            return effects.ToArray();
        }

        public static ActionAdmissionProfile Compile(ProgramCatalogEntry entry, Func<int, int> readInt32Constant, Func<string, string> normalizeTag)
        {
            if (entry == null || entry.Kind != ProgramCatalogEntryKind.Action ||
                !entry.Identity.StartsWith(ActionPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException("Action catalog entry is invalid.", nameof(entry));
            }
            if (readInt32Constant == null)
                throw new ArgumentNullException(nameof(readInt32Constant));
            var grantedTags = new SortedSet<string>(StringComparer.Ordinal);
            var requiredAll = new List<string>();
            var requiredAny = new List<string>();
            var requiredNone = new List<string>();
            var blockAll = new List<string>();
            var blockAny = new List<string>();
            var blockNone = new List<string>();
            var cancelAll = new List<string>();
            var cancelAny = new List<string>();
            var cancelNone = new List<string>();
            ActionTargetRequirement targetRequirement = default;
            bool hasTargetRequirement = false;
            int maxConcurrentInstances = 0;
            bool hasMaxConcurrentInstances = false;
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                ProgramCatalogField field = entry.Fields[i];
                if (string.Equals(field.Name, "TargetRequirement", StringComparison.Ordinal))
                {
                    if (hasTargetRequirement || field.Kind != ProgramCatalogFieldKind.Constant)
                        throw new InvalidOperationException($"Action catalog '{entry.Identity}' has an invalid TargetRequirement field.");
                    int value = readInt32Constant(field.ConstantIndex);
                    targetRequirement = (ActionTargetRequirement)value;
                    if (!Enum.IsDefined(typeof(ActionTargetRequirement), targetRequirement))
                        throw new InvalidOperationException($"Action catalog '{entry.Identity}' has unknown target requirement '{value}'.");
                    hasTargetRequirement = true;
                    continue;
                }
                if (string.Equals(field.Name, "MaxConcurrentInstances", StringComparison.Ordinal))
                {
                    if (hasMaxConcurrentInstances || field.Kind != ProgramCatalogFieldKind.Constant)
                        throw new InvalidOperationException($"Action catalog '{entry.Identity}' has an invalid MaxConcurrentInstances field.");
                    maxConcurrentInstances = readInt32Constant(field.ConstantIndex);
                    if (maxConcurrentInstances <= 0)
                        throw new InvalidOperationException($"Action catalog '{entry.Identity}' has non-positive MaxConcurrentInstances '{maxConcurrentInstances}'.");
                    hasMaxConcurrentInstances = true;
                    continue;
                }
                if (field.Kind != ProgramCatalogFieldKind.Identity || string.IsNullOrWhiteSpace(field.Identity))
                    continue;
                if (field.Name.StartsWith("GrantedTags:", StringComparison.Ordinal))
                {
                    string tag = normalizeTag(field.Identity);
                    if (!grantedTags.Add(tag))
                        throw new InvalidDataException($"Action catalog '{entry.Identity}' grants duplicate Tag '{tag}'.");
                }
                else if (field.Name.StartsWith("Required:All:", StringComparison.Ordinal))
                    requiredAll.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Required:Any:", StringComparison.Ordinal))
                    requiredAny.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Required:None:", StringComparison.Ordinal))
                    requiredNone.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Block:All:", StringComparison.Ordinal))
                    blockAll.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Block:Any:", StringComparison.Ordinal))
                    blockAny.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Block:None:", StringComparison.Ordinal))
                    blockNone.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Cancel:All:", StringComparison.Ordinal))
                    cancelAll.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Cancel:Any:", StringComparison.Ordinal))
                    cancelAny.Add(normalizeTag(field.Identity));
                else if (field.Name.StartsWith("Cancel:None:", StringComparison.Ordinal))
                    cancelNone.Add(normalizeTag(field.Identity));
            }
            if (!hasTargetRequirement)
                throw new InvalidOperationException($"Action catalog '{entry.Identity}' has no TargetRequirement field.");
            if (!hasMaxConcurrentInstances)
                throw new InvalidOperationException($"Action catalog '{entry.Identity}' has no MaxConcurrentInstances field.");
            var grantedTagSnapshot = new string[grantedTags.Count];
            grantedTags.CopyTo(grantedTagSnapshot);
            return new ActionAdmissionProfile(
                entry.Identity.Substring(ActionPrefix.Length),
                targetRequirement,
                maxConcurrentInstances,
                grantedTagSnapshot,
                new ActionTagQuery(requiredAll.ToArray(), requiredAny.ToArray(), requiredNone.ToArray()),
                new ActionTagQuery(blockAll.ToArray(), blockAny.ToArray(), blockNone.ToArray()),
                new ActionTagQuery(cancelAll.ToArray(), cancelAny.ToArray(), cancelNone.ToArray()));
        }
    }

    public sealed class ActionStartupEffect
    {
        internal ActionStartupEffect(string effectId, string[] parameterIds, int[] constantIndices)
        {
            EffectId = effectId;
            ParameterIds = parameterIds;
            ConstantIndices = constantIndices;
        }

        public string EffectId { get; }
        public IReadOnlyList<string> ParameterIds { get; }
        public IReadOnlyList<int> ConstantIndices { get; }
    }

    internal sealed class ActionAdmissionControl
    {
        readonly IActionAdmissionReadPort m_Port;
        readonly HashSet<string> m_OwnedTags;
        readonly HashSet<string> m_ActiveSourceTags;

        public ActionAdmissionControl(IActionAdmissionReadPort port)
        {
            m_Port = port ?? throw new ArgumentNullException(nameof(port));
            m_OwnedTags = new HashSet<string>(port.GameplayTagCapacity, StringComparer.Ordinal);
            m_ActiveSourceTags = new HashSet<string>(port.GameplayTagCapacity, StringComparer.Ordinal);
        }

        public ActionAdmissionDecision Evaluate(ActionAdmissionRequest request)
        {
            m_OwnedTags.Clear();
            m_ActiveSourceTags.Clear();
            try
            {
                GameplayEffectApplyResult startup = m_Port.EvaluateStartupEffects();
                if (!startup.AcceptedMutation)
                    return new ActionAdmissionDecision(false, ActionAdmissionRejectReason.StartupRequirementsFailed,
                        string.Empty, startupFailure: startup);
                bool hasRequiredQuery = !request.TargetProfile.Required.IsEmpty;
                bool hasBlockQuery = !request.TargetProfile.Block.IsEmpty;
                if (hasRequiredQuery || hasBlockQuery)
                    AddTags(m_OwnedTags, m_Port.OwnedGameplayTags);

                if (hasRequiredQuery && !MatchesQuery(request.TargetProfile.Required, m_OwnedTags))
                    return Reject(ActionAdmissionRejectReason.RequiredTagsMissing, string.Empty, 0);

                if (hasBlockQuery && MatchesQuery(request.TargetProfile.Block, m_OwnedTags))
                    return Reject(ActionAdmissionRejectReason.TargetBlocked, string.Empty, 0);

                if (request.TargetProfile.TargetRequirement == ActionTargetRequirement.SnapshotRequired &&
                    !request.TargetCandidate.HasTarget)
                {
                    return Reject(ActionAdmissionRejectReason.TargetSnapshotRequired, string.Empty, 0);
                }

                int activeTargetCount = 0;
                ulong activeTargetInstanceId = 0;
                ActionAdmissionActiveAction replacementSource = request.ReplacementActiveAction;
                for (int index = 0; index < m_Port.ActionCount; index++)
                {
                    if (!m_Port.TryReadActiveAction(index, out ActionAdmissionActiveAction active))
                        continue;
                    if (string.Equals(active.ActionId, request.TargetProfile.ActionId, StringComparison.Ordinal))
                    {
                        activeTargetCount++;
                        activeTargetInstanceId = active.InstanceId;
                    }
                }

                if (request.ReplacementSource.IsValid)
                {

                    if (request.Mode == ActionAdmissionEvaluationMode.CommitActivation)
                        return Reject(ActionAdmissionRejectReason.SourceActionStillActive, replacementSource.ActionId, replacementSource.InstanceId);
                    AddTags(m_ActiveSourceTags, m_Port.ReadAbilityTags(replacementSource.SkillId));
                    return !request.TargetProfile.Cancel.IsEmpty &&
                           MatchesQuery(request.TargetProfile.Cancel, m_ActiveSourceTags)
                        ? new ActionAdmissionDecision(true, ActionAdmissionRejectReason.None, replacementSource.ActionId, replacementSource.InstanceId)
                        : Reject(ActionAdmissionRejectReason.ActiveSourceNotCancelable, replacementSource.ActionId, replacementSource.InstanceId);
                }

                if (activeTargetCount >= request.TargetProfile.MaxConcurrentInstances)
                {
                    if (request.Mode == ActionAdmissionEvaluationMode.PreviewReplacement &&
                        activeTargetCount == 1 &&
                        request.ExecutingActionInstanceId != 0 &&
                        activeTargetInstanceId == request.ExecutingActionInstanceId)
                        return new ActionAdmissionDecision(true, ActionAdmissionRejectReason.None, string.Empty);
                    return Reject(ActionAdmissionRejectReason.ActionCapacityExceeded, request.TargetProfile.ActionId, 0);
                }
                return new ActionAdmissionDecision(true, ActionAdmissionRejectReason.None, string.Empty);
            }
            finally
            {
                m_OwnedTags.Clear();
                m_ActiveSourceTags.Clear();
            }
        }

        ActionAdmissionDecision Reject(
            ActionAdmissionRejectReason reason,
            string activeSourceActionId,
            ulong activeSourceActionInstanceId)
        {
            return new ActionAdmissionDecision(false, reason, activeSourceActionId, activeSourceActionInstanceId);
        }

        bool MatchesQuery(ActionTagQuery query, HashSet<string> owned)
        {
            for (int i = 0; i < query.All.Length; i++)
                if (!HasMatchingTag(owned, query.All[i]))
                    return false;
            bool matchedAny = query.Any.Length == 0;
            for (int i = 0; i < query.Any.Length && !matchedAny; i++)
                matchedAny = HasMatchingTag(owned, query.Any[i]);
            if (!matchedAny)
                return false;
            for (int i = 0; i < query.None.Length; i++)
                if (HasMatchingTag(owned, query.None[i]))
                    return false;
            return true;
        }

        bool HasMatchingTag(HashSet<string> owned, string query)
            => owned.Contains(query);

        void AddTags(HashSet<string> destination, IReadOnlyList<string> tags)
        {
            for (int i = 0; i < tags.Count; i++)
                AddTag(destination, tags[i]);
        }

        void AddTag(HashSet<string> destination, string tag)
        {
            if (!destination.Add(tag))
                return;
            while (m_Port.TryGetGameplayTagParent(tag, out string parent) && parent.Length != 0)
            {
                if (!destination.Add(parent))
                    break;
                tag = parent;
            }
        }
    }

    public readonly struct GameplayAbilityExecutionIdentity : IEquatable<GameplayAbilityExecutionIdentity>
    {
        public GameplayAbilityExecutionIdentity(
            CharacterSkillId abilityId,
            StableHash contentHash,
            StableHash stateSchemaHash,
            OperationSetVersion operationSetVersion,
            SimulationNumericProfile numericProfile)
        {
            if (!abilityId.IsValid || !contentHash.IsValid || !stateSchemaHash.IsValid ||
                !operationSetVersion.IsValid || !numericProfile.IsValid)
            {
                throw new ArgumentException("Ability execution services identity is incomplete.");
            }
            AbilityId = abilityId;
            ContentHash = contentHash;
            StateSchemaHash = stateSchemaHash;
            OperationSetVersion = operationSetVersion;
            NumericProfile = numericProfile;
        }

        public CharacterSkillId AbilityId { get; }
        public StableHash ContentHash { get; }
        public StableHash StateSchemaHash { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public bool IsValid =>
            AbilityId.IsValid &&
            ContentHash.IsValid &&
            StateSchemaHash.IsValid &&
            OperationSetVersion.IsValid &&
            NumericProfile.IsValid;

        public bool Equals(GameplayAbilityExecutionIdentity other) =>
            AbilityId.Equals(other.AbilityId) &&
            ContentHash.Equals(other.ContentHash) &&
            StateSchemaHash.Equals(other.StateSchemaHash) &&
            OperationSetVersion.Equals(other.OperationSetVersion) &&
            NumericProfile.Equals(other.NumericProfile);

        public override bool Equals(object obj) => obj is GameplayAbilityExecutionIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(AbilityId, ContentHash, StateSchemaHash, OperationSetVersion, NumericProfile);
        public static bool operator ==(GameplayAbilityExecutionIdentity left, GameplayAbilityExecutionIdentity right) => left.Equals(right);
        public static bool operator !=(GameplayAbilityExecutionIdentity left, GameplayAbilityExecutionIdentity right) => !left.Equals(right);

        public void Require(GameplayAbilityExecutionIdentity actual)
        {
            if (!Equals(actual))
            {
                throw new InvalidOperationException(
                    $"Ability execution services identity mismatch: expected '{AbilityId}/{ContentHash}/{StateSchemaHash}/{NumericProfile.Id}', received '{actual.AbilityId}/{actual.ContentHash}/{actual.StateSchemaHash}/{actual.NumericProfile.Id}'.");
            }
        }
    }

    internal interface IGameplayAbilityExecutionServices
    {
        GameplayAbilityExecutionIdentity Identity { get; }
        OperationExecutionTopology Topology { get; }
        string SourcePath(OperationHandle operation);
        string SkillOperationIdentity(OperationHandle operation);
        void RequireIdentity(GameplayAbilityExecutionIdentity identity);
    }

    internal enum ExecutionWorkspaceScope : byte
    {
        SessionTransaction = 1,
        ActorEvaluation = 2
    }

    internal enum DirtyPageOwnership : byte
    {
        Empty = 0,
        WorkspaceOwned = 1,
        Published = 2,
        Discarded = 3
    }

    internal readonly struct ExecutionWorkspaceLease
    {
        public ExecutionWorkspaceLease(ExecutionWorkspaceScope scope, ulong generation)
        {
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            Scope = scope;
            Generation = generation;
        }

        public ExecutionWorkspaceScope Scope { get; }
        public ulong Generation { get; }
        public bool IsValid => Generation != 0;
    }

    internal sealed class FrozenExecutionBuffer<T> : IReadOnlyList<T>
    {
        readonly T[] m_Values;

        internal FrozenExecutionBuffer(T[] values)
        {
            m_Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public int Count => m_Values.Length;
        public T this[int index] => m_Values[index];
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)m_Values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => m_Values.GetEnumerator();
    }

    internal sealed class ExecutionWorkspaceBuffer<T> : IReadOnlyList<T>
    {
        readonly List<T> m_Values;

        public ExecutionWorkspaceBuffer(int initialCapacity = 0)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            m_Values = new List<T>(initialCapacity);
        }

        public int Count => m_Values.Count;
        public int Capacity => m_Values.Capacity;
        internal List<T> Values => m_Values;
        public T this[int index]
        {
            get => m_Values[index];
            set => m_Values[index] = value;
        }

        public void Add(T value) => m_Values.Add(value);

        public void EnsureCapacity(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (m_Values.Capacity < capacity)
                m_Values.Capacity = capacity;
        }

        public void Clear() => m_Values.Clear();

        public IEnumerator<T> GetEnumerator() => m_Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => m_Values.GetEnumerator();

        public FrozenExecutionBuffer<TFrozen> Freeze<TFrozen>(Func<T, TFrozen> freeze)
        {
            if (freeze == null)
                throw new ArgumentNullException(nameof(freeze));
            var values = new TFrozen[m_Values.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = freeze(m_Values[i]);
            return new FrozenExecutionBuffer<TFrozen>(values);
        }
    }

    public sealed class NestedExecutionWorkspaceBuffer<T>
    {
        readonly List<List<T>> m_Buffers = new List<List<T>>();
        int m_Depth;

        public List<T> Acquire()
        {
            if (m_Depth == m_Buffers.Count)
                m_Buffers.Add(new List<T>(1));
            List<T> buffer = m_Buffers[m_Depth++];
            buffer.Clear();
            return buffer;
        }

        public void Release(List<T> buffer)
        {
            if (m_Depth == 0 || !ReferenceEquals(m_Buffers[m_Depth - 1], buffer))
                throw new InvalidOperationException("Nested execution workspace buffer release order is invalid.");
            buffer.Clear();
            m_Depth--;
        }

        public void Reset()
        {
            if (m_Depth != 0)
                throw new InvalidOperationException("Nested execution workspace buffer still has an active lease.");
            for (int i = 0; i < m_Buffers.Count; i++)
                m_Buffers[i].Clear();
        }
    }

    internal interface IExecutionWorkspaceScratch
    {
        void Reset();
    }

    internal sealed class SessionExecutionWorkspace<TCompletedStep, TEgressScratch>
    {
        readonly object m_Gate = new object();
        bool m_InUse;
        ulong m_Generation;

        public ExecutionWorkspaceBuffer<TCompletedStep> CompletedSteps { get; } =
            new ExecutionWorkspaceBuffer<TCompletedStep>();
        public ExecutionWorkspaceBuffer<TEgressScratch> Egress { get; } =
            new ExecutionWorkspaceBuffer<TEgressScratch>();

        public ExecutionWorkspaceLease BeginTransaction()
        {
            lock (m_Gate)
            {
                if (m_InUse)
                    throw new InvalidOperationException("Session execution workspace is already in use.");
                m_InUse = true;
                m_Generation = checked(m_Generation + 1);
                if (m_Generation == 0)
                    throw new OverflowException("Session execution workspace generation overflowed.");
                Reset();
                return new ExecutionWorkspaceLease(ExecutionWorkspaceScope.SessionTransaction, m_Generation);
            }
        }

        public void Require(ExecutionWorkspaceLease lease)
        {
            if (!m_InUse || lease.Scope != ExecutionWorkspaceScope.SessionTransaction || lease.Generation != m_Generation)
                throw new InvalidOperationException("Session execution workspace lease is stale or belongs to another owner.");
        }

        public void EndTransaction(ExecutionWorkspaceLease lease)
        {
            lock (m_Gate)
            {
                Require(lease);
                Reset();
                m_InUse = false;
            }
        }

        void Reset()
        {
            CompletedSteps.Clear();
            Egress.Clear();
        }
    }

}
