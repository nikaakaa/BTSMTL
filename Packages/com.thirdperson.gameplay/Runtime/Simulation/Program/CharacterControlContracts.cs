using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public static class CharacterStateProviderFields
    {
        public const string OwnerPrefix = "control-module:";
        public const string Position = "movement.position";
        public const string Velocity = "movement.velocity";
        public const string VerticalVelocity = "movement.vertical-velocity";
        public const string BodyYaw = "movement.body-yaw";
        public const string Grounded = "movement.grounded";
        public const string MoveFacingAngle = "move-facing-angle";
        public const string MemberPrefix = "character-state";
        public const int MemberRevision = 1;

        public static string Owner(string controlModuleId) =>
            string.IsNullOrWhiteSpace(controlModuleId)
                ? string.Empty
                : OwnerPrefix + controlModuleId.Trim();

        public static bool IsVector3(string field) =>
            string.Equals(field, Position, StringComparison.Ordinal) ||
            string.Equals(field, Velocity, StringComparison.Ordinal);

        public static bool IsScalar(string field) =>
            string.Equals(field, VerticalVelocity, StringComparison.Ordinal);

        public static bool IsYaw(string field) =>
            string.Equals(field, BodyYaw, StringComparison.Ordinal);

        public static bool IsBoolean(string field) =>
            string.Equals(field, Grounded, StringComparison.Ordinal);

        public static bool IsValid(string field) =>
            IsVector3(field) || IsScalar(field) || IsYaw(field) || IsBoolean(field);

        public static bool IsOwner(string owner) =>
            !string.IsNullOrWhiteSpace(owner) &&
            owner.StartsWith(OwnerPrefix, StringComparison.Ordinal) &&
            owner.Length > OwnerPrefix.Length;

        public static bool IsOwnerForModule(string owner, string controlModuleId) =>
            IsOwner(owner) &&
            !string.IsNullOrWhiteSpace(controlModuleId) &&
            string.Equals(owner, Owner(controlModuleId), StringComparison.Ordinal);

        public static SemanticValueKind ValueKind(string field)
        {
            if (IsVector3(field))
                return SemanticValueKind.Vector3;
            if (IsScalar(field))
                return SemanticValueKind.Number;
            if (IsYaw(field))
                return SemanticValueKind.Yaw;
            if (IsBoolean(field))
                return SemanticValueKind.Boolean;
            throw new InvalidOperationException($"Character State field '{field}' is not supported.");
        }

        public static GameplayAbilityProviderBindingEntry BuildProvider(string controlModuleId)
        {
            string owner = Owner(controlModuleId);
            var fields = new[] { Position, Velocity, VerticalVelocity, BodyYaw, Grounded, MoveFacingAngle };
            var members = new GameplayAbilityProviderMemberBinding[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                members[i] = new GameplayAbilityProviderMemberBinding(GameplayAbilityProviderKind.CharacterState,
                    owner, $"{MemberPrefix}:{fields[i]}", ProviderValueKind(fields[i]), MemberRevision,
                    $"control-state:{controlModuleId}:{fields[i]}");
            return new GameplayAbilityProviderBindingEntry(GameplayAbilityProviderKind.CharacterState,
                owner, GameplayAbilityProviderContractVersions.CharacterState, members);
        }

        public static GameplayAbilityProviderValueKind ProviderValueKind(string field) =>
            string.Equals(field, MoveFacingAngle, StringComparison.Ordinal)
                ? GameplayAbilityProviderValueKind.Number
                : ValueKind(field) switch
                {
                    SemanticValueKind.Boolean => GameplayAbilityProviderValueKind.Boolean,
                    SemanticValueKind.Number => GameplayAbilityProviderValueKind.Number,
                    SemanticValueKind.Vector3 => GameplayAbilityProviderValueKind.Vector3,
                    SemanticValueKind.Yaw => GameplayAbilityProviderValueKind.Yaw,
                    _ => throw new InvalidOperationException($"Character State field '{field}' is not supported.")
                };
    }

    public static class CharacterSkillProviderOwners
    {
        public const string AssetPrefix = "asset:";

        public static string Asset(string assetGuid) =>
            string.IsNullOrWhiteSpace(assetGuid)
                ? string.Empty
                : AssetPrefix + assetGuid.Trim();

        public static bool IsAssetOwner(string owner) =>
            !string.IsNullOrWhiteSpace(owner) &&
            owner.StartsWith(AssetPrefix, StringComparison.Ordinal) &&
            owner.Length > AssetPrefix.Length;

        public static bool IsAssetOwner(string owner, string expectedOwner) =>
            IsAssetOwner(owner) &&
            IsAssetOwner(expectedOwner) &&
            string.Equals(owner, expectedOwner, StringComparison.Ordinal);
    }

    public enum CharacterControlNumericComparison : byte
    {
        Less = 1,
        LessOrEqual = 2,
        Equal = 3,
        Greater = 4,
        GreaterOrEqual = 5
    }

    public sealed class CharacterControlStateDescriptor
    {
        public CharacterControlStateDescriptor(CharacterControlStateId id, int evaluationOrder)
        {
            if (!id.IsValid || evaluationOrder < 0)
                throw new ArgumentException("Character control state descriptor is incomplete.");
            Id = id;
            EvaluationOrder = evaluationOrder;
        }

        public CharacterControlStateId Id { get; }
        public int EvaluationOrder { get; }
    }

    public sealed class CharacterControlTransitionDescriptor
    {
        public CharacterControlTransitionDescriptor(
            CharacterControlTransitionId id,
            CharacterControlStateId source,
            CharacterControlStateId target,
            int priority,
            int evaluationOrder)
        {
            if (!id.IsValid || !source.IsValid || !target.IsValid || evaluationOrder < 0)
                throw new ArgumentException("Character control transition descriptor is incomplete.");
            Id = id;
            Source = source;
            Target = target;
            Priority = priority;
            EvaluationOrder = evaluationOrder;
        }

        public CharacterControlTransitionId Id { get; }
        public CharacterControlStateId Source { get; }
        public CharacterControlStateId Target { get; }
        public int Priority { get; }
        public int EvaluationOrder { get; }
    }

    public enum CharacterControlStateSemantic : ushort
    {
        ActiveState = 1,
        EnteredTick = 2,
        Transition = 3,
        StateValue = 4
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CharacterControlStateFieldDescriptor
    {
        public CharacterControlStateFieldDescriptor(
            CharacterControlStateFieldId id,
            CharacterControlStateValueKind valueKind,
            CharacterControlStateSemantic semantic)
        {
            Id = id;
            ValueKind = valueKind;
            Semantic = semantic;
            RequireValid();
        }

        public CharacterControlStateFieldId Id { get; }
        public CharacterControlStateValueKind ValueKind { get; }
        public CharacterControlStateSemantic Semantic { get; }

        [CanonicalValidation]
        void RequireValid()
        {
            if (!Id.IsValid || !IsValid(ValueKind, Semantic))
                throw new ArgumentException("Character control state field is incomplete.");
        }

        static bool IsValid(CharacterControlStateValueKind valueKind, CharacterControlStateSemantic semantic)
        {
            return semantic switch
            {
                CharacterControlStateSemantic.ActiveState => valueKind == CharacterControlStateValueKind.Identity,
                CharacterControlStateSemantic.EnteredTick => valueKind == CharacterControlStateValueKind.UInt64,
                CharacterControlStateSemantic.Transition => valueKind == CharacterControlStateValueKind.Identity,
                CharacterControlStateSemantic.StateValue => valueKind == CharacterControlStateValueKind.Boolean ||
                    valueKind == CharacterControlStateValueKind.Int32 ||
                    valueKind == CharacterControlStateValueKind.UInt64 ||
                    valueKind == CharacterControlStateValueKind.Identity ||
                    valueKind == CharacterControlStateValueKind.Number,
                _ => false
            };
        }
    }

    public sealed class CharacterControlParameterDescriptor
    {
        public CharacterControlParameterDescriptor(CharacterControlParameterId id, SemanticValueKind valueKind, double numericValue = 0d)
        {
            if (!id.IsValid || !Enum.IsDefined(typeof(SemanticValueKind), valueKind) || double.IsNaN(numericValue) || double.IsInfinity(numericValue))
                throw new ArgumentException("Character control parameter descriptor is incomplete.");
            Id = id;
            ValueKind = valueKind;
            NumericValue = numericValue;
        }

        public CharacterControlParameterId Id { get; }
        public SemanticValueKind ValueKind { get; }
        public double NumericValue { get; }
    }

    public sealed class CharacterControlParameterValue
    {
        public CharacterControlParameterValue(
            CharacterControlParameterId id,
            SemanticValueKind valueKind,
            double numericValue)
        {
            if (!id.IsValid ||
                !Enum.IsDefined(typeof(SemanticValueKind), valueKind) ||
                double.IsNaN(numericValue) ||
                double.IsInfinity(numericValue))
            {
                throw new ArgumentException("Character control parameter value is incomplete.");
            }
            Id = id;
            ValueKind = valueKind;
            NumericValue = numericValue;
        }

        public CharacterControlParameterId Id { get; }
        public SemanticValueKind ValueKind { get; }
        public double NumericValue { get; }
    }

    public sealed class CharacterControlParameterSet
    {
        readonly ReadOnlyCollection<CharacterControlParameterValue> m_Values;
        readonly Dictionary<CharacterControlParameterId, CharacterControlParameterValue> m_ById;

        public CharacterControlParameterSet(IEnumerable<CharacterControlParameterValue> values)
        {
            var sorted = new List<CharacterControlParameterValue>(values ?? Array.Empty<CharacterControlParameterValue>());
            sorted.Sort((left, right) => left.Id.CompareTo(right.Id));
            m_ById = new Dictionary<CharacterControlParameterId, CharacterControlParameterValue>();
            for (int i = 0; i < sorted.Count; i++)
            {
                CharacterControlParameterValue value = sorted[i]
                    ?? throw new ArgumentException("Character control parameter set contains a missing value.", nameof(values));
                if (!m_ById.TryAdd(value.Id, value))
                    throw new ArgumentException($"Character control parameter '{value.Id}' is duplicated.", nameof(values));
            }
            m_Values = sorted.AsReadOnly();
            var hashParts = new List<string> { "character-control-parameters/1" };
            for (int i = 0; i < sorted.Count; i++)
            {
                CharacterControlParameterValue value = sorted[i];
                hashParts.Add(value.Id.Value);
                hashParts.Add(((int)value.ValueKind).ToString(System.Globalization.CultureInfo.InvariantCulture));
                hashParts.Add(value.NumericValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            ContentHash = StableHash.Compute(hashParts.ToArray());
        }

        public IReadOnlyList<CharacterControlParameterValue> Values => m_Values;
        public StableHash ContentHash { get; }

        public CharacterControlParameterValue Require(CharacterControlParameterId id) =>
            m_ById.TryGetValue(id, out CharacterControlParameterValue value)
                ? value
                : throw new InvalidOperationException($"Character control parameter '{id}' is not declared.");

        public double ReadNumeric(CharacterControlParameterId id) => Require(id).NumericValue;
    }

    public enum CharacterControlMotionExecutionMode : byte
    {
        Once = 1,
        Timed = 2,
        Continuous = 3
    }

    public enum CharacterControlMotionDisplacementMode : byte
    {
        ConstantSpeed = 1,
        SourceCurve = 2
    }

    public enum CharacterControlMotionSpace : byte
    {
        ActorLocal = 0,
        World = 1,
        CameraRelative = 2
    }

    public sealed class CharacterControlMotionDescriptor
    {
        public CharacterControlMotionDescriptor(
            string binding,
            SimulationInputValueId input,
            double moveSpeed,
            double turnSpeedDegrees,
            CharacterControlMotionExecutionMode executionMode,
            double durationSeconds,
            string sourceMotionIdentity = "",
            CharacterControlMotionDisplacementMode displacementMode = CharacterControlMotionDisplacementMode.ConstantSpeed,
            CharacterControlMotionSpace space = CharacterControlMotionSpace.World,
            int priority = 0,
            bool consumeLowerChannels = false)
        {
            Binding = SimulationIdentity.Require(binding, nameof(binding));
            if (!input.IsValid || moveSpeed < 0d || turnSpeedDegrees < 0d ||
                !Enum.IsDefined(typeof(CharacterControlMotionExecutionMode), executionMode) ||
                !Enum.IsDefined(typeof(CharacterControlMotionDisplacementMode), displacementMode) ||
                !Enum.IsDefined(typeof(CharacterControlMotionSpace), space) ||
                double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds < 0d)
                throw new ArgumentException("Character control motion descriptor is incomplete.");
            if (executionMode == CharacterControlMotionExecutionMode.Timed && durationSeconds <= 0d)
                throw new ArgumentException("Timed character control motion requires a duration.", nameof(durationSeconds));
            if (displacementMode == CharacterControlMotionDisplacementMode.SourceCurve && string.IsNullOrEmpty(sourceMotionIdentity))
                throw new ArgumentException("Source curve character control motion requires a source identity.", nameof(sourceMotionIdentity));
            if (displacementMode == CharacterControlMotionDisplacementMode.ConstantSpeed && !string.IsNullOrEmpty(sourceMotionIdentity))
                throw new ArgumentException("Constant speed character control motion cannot declare a source identity.", nameof(sourceMotionIdentity));
            Input = input;
            MoveSpeed = moveSpeed;
            TurnSpeedDegrees = turnSpeedDegrees;
            ExecutionMode = executionMode;
            DurationSeconds = durationSeconds;
            SourceMotionIdentity = sourceMotionIdentity ?? string.Empty;
            DisplacementMode = displacementMode;
            Space = space;
            Priority = priority;
            ConsumeLowerChannels = consumeLowerChannels;
        }

        public string Binding { get; }
        public SimulationInputValueId Input { get; }
        public double MoveSpeed { get; }
        public double TurnSpeedDegrees { get; }
        public CharacterControlMotionExecutionMode ExecutionMode { get; }
        public double DurationSeconds { get; }
        public string SourceMotionIdentity { get; }
        public CharacterControlMotionDisplacementMode DisplacementMode { get; }
        public CharacterControlMotionSpace Space { get; }
        public int Priority { get; }
        public bool ConsumeLowerChannels { get; }
    }

    public sealed class CharacterControlModuleContract
    {
        readonly ReadOnlyCollection<CharacterControlStateDescriptor> m_States;
        readonly ReadOnlyCollection<CharacterControlTransitionDescriptor> m_Transitions;
        readonly ReadOnlyCollection<CharacterControlStateFieldDescriptor> m_StateFields;
        readonly ReadOnlyCollection<CharacterControlParameterDescriptor> m_Parameters;
        readonly ReadOnlyCollection<SimulationInputValueId> m_InputValues;
        readonly ReadOnlyCollection<CharacterControlMotionDescriptor> m_Motions;
        readonly CharacterControlStateSchema m_StateSchema;

        public CharacterControlModuleContract(
            CharacterControlModuleId moduleId,
            int semanticVersion,
            CharacterControlStateId initialState,
            IEnumerable<CharacterControlStateDescriptor> states,
            IEnumerable<CharacterControlTransitionDescriptor> transitions,
            IEnumerable<CharacterControlStateFieldDescriptor> stateFields,
            IEnumerable<CharacterControlParameterDescriptor> parameters,
            IEnumerable<SimulationInputValueId> inputValues,
            IEnumerable<CharacterControlMotionDescriptor> motions)
        {
            if (!moduleId.IsValid || semanticVersion <= 0)
                throw new ArgumentException("Character control module contract is incomplete.");
            ModuleId = moduleId;
            SemanticVersion = semanticVersion;
            m_States = Freeze(
                states,
                value => value.Id,
                "state",
                (left, right) => Compare(left.EvaluationOrder, right.EvaluationOrder, left.Id.CompareTo(right.Id)));
            m_Transitions = Freeze(
                transitions,
                value => value.Id,
                "transition",
                (left, right) => Compare(
                    left.Priority,
                    right.Priority,
                    Compare(left.EvaluationOrder, right.EvaluationOrder, left.Id.CompareTo(right.Id))));
            m_StateFields = Freeze(stateFields, value => value.Id, "state field", (left, right) => left.Id.CompareTo(right.Id));
            m_Parameters = Freeze(parameters, value => value.Id, "parameter", (left, right) => left.Id.CompareTo(right.Id));
            m_InputValues = Freeze(inputValues, value => value, "input value");
            m_Motions = Freeze(motions, value => value.Binding, "motion", (left, right) => string.CompareOrdinal(left.Binding, right.Binding));
            InitialState = initialState;
            ValidateStateGraph();
            m_StateSchema = new CharacterControlStateSchema(this);
        }

        public CharacterControlModuleId ModuleId { get; }
        public int SemanticVersion { get; }
        public CharacterControlStateId InitialState { get; }
        public IReadOnlyList<CharacterControlStateDescriptor> States => m_States;
        public IReadOnlyList<CharacterControlTransitionDescriptor> Transitions => m_Transitions;
        public IReadOnlyList<CharacterControlStateFieldDescriptor> StateFields => m_StateFields;
        public IReadOnlyList<CharacterControlParameterDescriptor> Parameters => m_Parameters;
        public IReadOnlyList<SimulationInputValueId> InputValues => m_InputValues;
        public IReadOnlyList<CharacterControlMotionDescriptor> Motions => m_Motions;
        public CharacterControlStateSchema StateSchema => m_StateSchema;

        public bool TryResolveParameterSet(
            IEnumerable<CharacterControlParameterValue> overrides,
            out CharacterControlParameterSet resolved,
            out IReadOnlyList<string> errors)
        {
            var messages = new List<string>();
            var declared = new Dictionary<CharacterControlParameterId, CharacterControlParameterDescriptor>();
            for (int i = 0; i < m_Parameters.Count; i++)
                declared.Add(m_Parameters[i].Id, m_Parameters[i]);

            var values = new Dictionary<CharacterControlParameterId, CharacterControlParameterValue>();
            foreach (CharacterControlParameterValue value in overrides ?? Array.Empty<CharacterControlParameterValue>())
            {
                if (value == null)
                {
                    messages.Add("Character control parameter configuration contains a missing value.");
                    continue;
                }
                if (!declared.TryGetValue(value.Id, out CharacterControlParameterDescriptor descriptor))
                {
                    messages.Add($"Character control parameter '{value.Id}' is not declared by module '{ModuleId}'.");
                    continue;
                }
                if (value.ValueKind != descriptor.ValueKind)
                {
                    messages.Add($"Character control parameter '{value.Id}' has kind '{value.ValueKind}', expected '{descriptor.ValueKind}'.");
                    continue;
                }
                if (!values.TryAdd(value.Id, value))
                    messages.Add($"Character control parameter '{value.Id}' is configured more than once.");
            }

            var resolvedValues = new List<CharacterControlParameterValue>(m_Parameters.Count);
            for (int i = 0; i < m_Parameters.Count; i++)
            {
                CharacterControlParameterDescriptor descriptor = m_Parameters[i];
                resolvedValues.Add(values.TryGetValue(descriptor.Id, out CharacterControlParameterValue value)
                    ? value
                    : new CharacterControlParameterValue(descriptor.Id, descriptor.ValueKind, descriptor.NumericValue));
            }

            errors = messages.AsReadOnly();
            resolved = messages.Count == 0 ? new CharacterControlParameterSet(resolvedValues) : null;
            return messages.Count == 0;
        }

        void ValidateStateGraph()
        {
            var stateIds = new HashSet<CharacterControlStateId>();
            for (int i = 0; i < m_States.Count; i++)
                stateIds.Add(m_States[i].Id);
            if (!InitialState.IsValid || !stateIds.Contains(InitialState))
                throw new ArgumentException("Character control module initial state is not declared.", nameof(InitialState));
            for (int i = 0; i < m_Transitions.Count; i++)
            {
                CharacterControlTransitionDescriptor transition = m_Transitions[i];
                if (!stateIds.Contains(transition.Source) || !stateIds.Contains(transition.Target))
                    throw new ArgumentException(
                        $"Character control transition '{transition.Id}' references an undeclared state.",
                        nameof(m_Transitions));
            }
        }

        static ReadOnlyCollection<T> Freeze<T, TKey>(
            IEnumerable<T> source,
            Func<T, TKey> key,
            string label,
            Comparison<T> comparison = null)
            where T : class
        {
            var values = source == null ? new List<T>() : new List<T>(source);
            var keys = new HashSet<TKey>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null || !keys.Add(key(values[i])))
                    throw new ArgumentException($"Character control {label} identity is invalid or duplicated.", nameof(source));
            }
            if (comparison != null)
                values.Sort(comparison);
            return values.AsReadOnly();
        }

        static int Compare(int left, int right, int tieBreaker) => left != right ? left.CompareTo(right) : tieBreaker;

        static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> source, Func<T, T> key, string label)
            where T : struct
        {
            var values = source == null ? new List<T>() : new List<T>(source);
            var keys = new HashSet<T>();
            for (int i = 0; i < values.Count; i++)
            {
                if (!keys.Add(key(values[i])))
                    throw new ArgumentException($"Character control {label} identity is duplicated.", nameof(source));
            }
            values.Sort((left, right) => Comparer<T>.Default.Compare(left, right));
            return values.AsReadOnly();
        }
    }

    public sealed class CharacterControlRuntimeBinding
    {
        public CharacterControlRuntimeBinding(
            CharacterControlModuleId moduleId,
            int semanticVersion,
            CharacterControlParameterSet parameters,
            CharacterControlMotionBindingCatalog motionBindings)
        {
            if (!moduleId.IsValid || semanticVersion <= 0)
                throw new ArgumentException("Character control runtime binding identity is incomplete.");
            ModuleId = moduleId;
            SemanticVersion = semanticVersion;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            MotionBindings = motionBindings ?? throw new ArgumentNullException(nameof(motionBindings));
            var hashParts = new List<string>(5)
            {
                "character-control-runtime-binding/6",
                moduleId.Value,
                semanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                parameters.ContentHash.ToString(),
                motionBindings.ContentHash.ToString()
            };
            BindingHash = StableHash.Compute(hashParts.ToArray());
        }

        public CharacterControlModuleId ModuleId { get; }
        public int SemanticVersion { get; }
        public CharacterControlParameterSet Parameters { get; }
        public CharacterControlMotionBindingCatalog MotionBindings { get; }
        public StableHash BindingHash { get; }

        public void RequireContract(CharacterControlModuleContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.ModuleId != ModuleId || contract.SemanticVersion != SemanticVersion)
                throw new InvalidOperationException(
                    $"Character control runtime binding '{ModuleId}/{SemanticVersion}' does not match module contract '{contract.ModuleId}/{contract.SemanticVersion}'.");
            if (!contract.TryResolveParameterSet(
                    Parameters.Values,
                    out CharacterControlParameterSet resolved,
                    out IReadOnlyList<string> errors) ||
                !resolved.ContentHash.Equals(Parameters.ContentHash))
            {
                throw new InvalidOperationException(
                    errors == null || errors.Count == 0
                        ? $"Character control runtime binding parameters do not match module contract '{ModuleId}'."
                        : string.Join(" ", errors));
            }
            MotionBindings.RequireContract(contract);
        }
    }

    public sealed class CharacterControlModuleCatalog
    {
        readonly struct ModuleEntry
        {
            public ModuleEntry(Func<ICharacterControlModule> factory, CharacterControlModuleContract contract)
            {
                Factory = factory;
                Contract = contract;
            }

            public Func<ICharacterControlModule> Factory { get; }
            public CharacterControlModuleContract Contract { get; }
        }

        readonly Dictionary<CharacterControlModuleId, ModuleEntry> m_Entries;

        // 每次 Require 新建模块实例:模块内部持有可变状态机,作用域必须按 actor 隔离。
        public CharacterControlModuleCatalog(IEnumerable<Func<ICharacterControlModule>> factories)
        {
            m_Entries = new Dictionary<CharacterControlModuleId, ModuleEntry>();
            if (factories == null)
                return;
            foreach (Func<ICharacterControlModule> factory in factories)
            {
                if (factory == null)
                    throw new ArgumentException("Character control module catalog contains a missing factory.", nameof(factories));
                ICharacterControlModule probe = factory();
                if (probe == null || probe.Contract == null)
                    throw new ArgumentException("Character control module catalog contains an incomplete module.", nameof(factories));
                if (!m_Entries.TryAdd(probe.Contract.ModuleId, new ModuleEntry(factory, probe.Contract)))
                    throw new ArgumentException(
                        $"Character control module '{probe.Contract.ModuleId}' is registered more than once.",
                        nameof(factories));
            }
        }

        public ICharacterControlModule Require(CharacterControlModuleId moduleId)
        {
            if (!moduleId.IsValid)
                throw new ArgumentException("Character control module identity is invalid.", nameof(moduleId));
            if (!m_Entries.TryGetValue(moduleId, out ModuleEntry entry))
                throw new InvalidOperationException($"Character control module '{moduleId}' is not installed.");
            return entry.Factory();
        }

        public CharacterControlModuleContract RequireContract(CharacterControlModuleId moduleId)
        {
            if (!moduleId.IsValid)
                throw new ArgumentException("Character control module identity is invalid.", nameof(moduleId));
            if (!m_Entries.TryGetValue(moduleId, out ModuleEntry entry))
                throw new InvalidOperationException($"Character control module '{moduleId}' is not installed.");
            return entry.Contract;
        }
    }

}
