using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    public sealed class EquipmentProgramSlot
    {
        public EquipmentProgramSlot(EquipmentSlotId slotId, EquipmentSlotRequirement requirement, EquipmentId initialEquipmentId)
        {
            if (!slotId.IsValid || !Enum.IsDefined(typeof(EquipmentSlotRequirement), requirement))
                throw new ArgumentException("Equipment Program Slot is invalid.");
            if (requirement == EquipmentSlotRequirement.Required && !initialEquipmentId.IsValid)
                throw new ArgumentException("Required Equipment Program Slot has no initial Equipment.");
            SlotId = slotId;
            Requirement = requirement;
            InitialEquipmentId = initialEquipmentId;
        }

        public EquipmentSlotId SlotId { get; }
        public EquipmentSlotRequirement Requirement { get; }
        public EquipmentId InitialEquipmentId { get; }
    }

    public sealed class EquipmentProgramFeature
    {
        readonly ReadOnlyCollection<string> m_GrantedTags;
        readonly ReadOnlyCollection<string> m_PassiveEffects;

        public EquipmentProgramFeature(
            EquipmentFeatureId featureId,
            EquipmentFeatureRevision revision,
            string codeBindingId,
            IEnumerable<string> grantedTags,
            IEnumerable<string> passiveEffects,
            WorldCapability requiredWorldCapabilities)
        {
            if (!featureId.IsValid || !revision.IsValid || string.IsNullOrEmpty(codeBindingId))
                throw new ArgumentException("Equipment Program Feature identity is invalid.");
            FeatureId = featureId;
            Revision = revision;
            CodeBindingId = SimulationIdentity.Require(codeBindingId, nameof(codeBindingId));
            m_GrantedTags = StableIdentities(grantedTags?.Select(value => value.StartsWith("tag:", StringComparison.Ordinal) ? value : "tag:" + value), "Equipment granted Tag");
            m_PassiveEffects = StableIdentities(passiveEffects?.Select(value => value.StartsWith("effect:", StringComparison.Ordinal) ? value : "effect:" + value), "Equipment passive Effect");
            RequiredWorldCapabilities = requiredWorldCapabilities;
        }

        public EquipmentFeatureId FeatureId { get; }
        public EquipmentFeatureRevision Revision { get; }
        public string CodeBindingId { get; }
        public IReadOnlyList<string> GrantedTags => m_GrantedTags;
        public IReadOnlyList<string> PassiveEffects => m_PassiveEffects;
        public WorldCapability RequiredWorldCapabilities { get; }

        static ReadOnlyCollection<string> StableIdentities(IEnumerable<string> source, string label)
        {
            string[] values = (source ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < values.Length; i++)
            {
                SimulationIdentity.Require(values[i], label);
                if (i > 0 && string.Equals(values[i - 1], values[i], StringComparison.Ordinal))
                    throw new InvalidDataException($"{label} '{values[i]}' is duplicated.");
            }
            return Array.AsReadOnly(values);
        }
    }

    public sealed class EquipmentProgramItem
    {
        public EquipmentProgramItem(
            EquipmentId equipmentId,
            EquipmentSlotId slotId,
            EquipmentFeatureId featureId,
            EquipmentVisualBindingId visualBindingId)
        {
            if (!equipmentId.IsValid || !slotId.IsValid || !featureId.IsValid || !visualBindingId.IsValid)
                throw new ArgumentException("Equipment Program Item is invalid.");
            EquipmentId = equipmentId;
            SlotId = slotId;
            FeatureId = featureId;
            VisualBindingId = visualBindingId;
        }

        public EquipmentId EquipmentId { get; }
        public EquipmentSlotId SlotId { get; }
        public EquipmentFeatureId FeatureId { get; }
        public EquipmentVisualBindingId VisualBindingId { get; }
    }

    public sealed class EquipmentProgramRoute
    {
        public EquipmentProgramRoute(
            EquipmentActionRouteId routeId,
            EquipmentSlotId ownerSlotId,
            string inputRequestId,
            EquipmentRouteRequestConsumption requestConsumption,
            EquipmentRouteMissingImplementation missingImplementation)
        {
            if (!routeId.IsValid || !ownerSlotId.IsValid || string.IsNullOrEmpty(inputRequestId) ||
                !Enum.IsDefined(typeof(EquipmentRouteRequestConsumption), requestConsumption) ||
                !Enum.IsDefined(typeof(EquipmentRouteMissingImplementation), missingImplementation))
            {
                throw new ArgumentException("Equipment Program Route is invalid.");
            }
            RouteId = routeId;
            OwnerSlotId = ownerSlotId;
            InputRequestId = inputRequestId;
            RequestConsumption = requestConsumption;
            MissingImplementation = missingImplementation;
        }

        public EquipmentActionRouteId RouteId { get; }
        public EquipmentSlotId OwnerSlotId { get; }
        public string InputRequestId { get; }
        public EquipmentRouteRequestConsumption RequestConsumption { get; }
        public EquipmentRouteMissingImplementation MissingImplementation { get; }
    }

    public sealed class EquipmentProgramRouteImplementation
    {
        readonly ReadOnlyCollection<EquipmentParameterId> m_RequiredParameterIds;
        readonly ReadOnlyCollection<string> m_RequiredProducerIds;

        public EquipmentProgramRouteImplementation(
            EquipmentFeatureId featureId,
            EquipmentActionRouteId routeId,
            CharacterSkillId abilityId,
            IEnumerable<EquipmentParameterId> requiredParameterIds = null,
            IEnumerable<string> requiredProducerIds = null)
        {
            if (!featureId.IsValid || !routeId.IsValid || !abilityId.IsValid)
                throw new ArgumentException("Equipment Route implementation is invalid.");
            FeatureId = featureId;
            RouteId = routeId;
            AbilityId = abilityId;
            m_RequiredParameterIds = StableParameters(requiredParameterIds);
            m_RequiredProducerIds = StableIdentities(requiredProducerIds, "Equipment required Producer");
        }

        public EquipmentFeatureId FeatureId { get; }
        public EquipmentActionRouteId RouteId { get; }
        public CharacterSkillId AbilityId { get; }
        public IReadOnlyList<EquipmentParameterId> RequiredParameterIds => m_RequiredParameterIds;
        public IReadOnlyList<string> RequiredProducerIds => m_RequiredProducerIds;

        static ReadOnlyCollection<EquipmentParameterId> StableParameters(IEnumerable<EquipmentParameterId> source)
        {
            EquipmentParameterId[] values = (source ?? Array.Empty<EquipmentParameterId>()).OrderBy(value => value.Value, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i].IsValid || i > 0 && values[i - 1] == values[i])
                    throw new InvalidDataException("Equipment required Parameter identities are invalid or duplicated.");
            }
            return Array.AsReadOnly(values);
        }

        static ReadOnlyCollection<string> StableIdentities(IEnumerable<string> source, string label)
        {
            string[] values = (source ?? Array.Empty<string>()).Select(value => SimulationIdentity.Require(value, label)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < values.Length; i++)
                if (string.Equals(values[i - 1], values[i], StringComparison.Ordinal))
                    throw new InvalidDataException($"{label} '{values[i]}' is duplicated.");
            return Array.AsReadOnly(values);
        }
    }

    public sealed class EquipmentProgramParameter
    {
        public EquipmentProgramParameter(
            EquipmentId equipmentId,
            EquipmentFeatureId featureId,
            EquipmentParameterId parameterId,
            EquipmentParameterValueKind valueKind,
            EquipmentRuntimeParameterValue value)
        {
            if (!equipmentId.IsValid || !featureId.IsValid || !parameterId.IsValid ||
                !Enum.IsDefined(typeof(EquipmentParameterValueKind), valueKind) || value == null || value.Kind != valueKind)
            {
                throw new ArgumentException("Equipment Program Parameter is invalid.");
            }
            EquipmentId = equipmentId;
            FeatureId = featureId;
            ParameterId = parameterId;
            ValueKind = valueKind;
            Value = value;
        }

        public EquipmentId EquipmentId { get; }
        public EquipmentFeatureId FeatureId { get; }
        public EquipmentParameterId ParameterId { get; }
        public EquipmentParameterValueKind ValueKind { get; }
        public EquipmentRuntimeParameterValue Value { get; }
    }

    public sealed class EquipmentProgramLocalState
    {
        public EquipmentProgramLocalState(
            EquipmentFeatureId featureId,
            EquipmentLocalStateId stateId,
            EquipmentRuntimeStateValueKind valueKind,
            EquipmentRuntimeStateValue defaultValue)
        {
            if (!featureId.IsValid || !stateId.IsValid || !EquipmentRuntimeStateValue.IsValidKind(valueKind) ||
                defaultValue.Kind != valueKind)
                throw new ArgumentException("Equipment Program local state is invalid.");
            FeatureId = featureId;
            StateId = stateId;
            ValueKind = valueKind;
            DefaultValue = defaultValue;
        }

        public EquipmentFeatureId FeatureId { get; }
        public EquipmentLocalStateId StateId { get; }
        public EquipmentRuntimeStateValueKind ValueKind { get; }
        public EquipmentRuntimeStateValue DefaultValue { get; }
    }

    [ValueOperations]
    public readonly partial struct EquipmentLocalStateValue
    {
        public EquipmentLocalStateValue(
            EquipmentFeatureId featureId,
            EquipmentLocalStateId stateId,
            EquipmentRuntimeStateValue value)
        {
            FeatureId = featureId;
            StateId = stateId;
            Value = value;
            RequireIdentity();
        }

        [ValueValidation]
        void RequireIdentity()
        {
            if (!FeatureId.IsValid || !StateId.IsValid)
                throw new ArgumentException("Equipment local state value is invalid.");
        }

        public EquipmentFeatureId FeatureId { get; }
        public EquipmentLocalStateId StateId { get; }
        public EquipmentRuntimeStateValue Value { get; }
    }

    public sealed class EquipmentProgramOperationBinding
    {
        public EquipmentProgramOperationBinding(
            OperationHandle operation,
            EquipmentSlotId slotId,
            EquipmentActionRouteId routeId,
            EquipmentId equipmentId,
            EquipmentParameterId parameterId)
        {
            if (!operation.IsValid || !slotId.IsValid && !routeId.IsValid && !equipmentId.IsValid && !parameterId.IsValid)
                throw new ArgumentException("Equipment Program operation binding is invalid.");
            Operation = operation;
            SlotId = slotId;
            RouteId = routeId;
            EquipmentId = equipmentId;
            ParameterId = parameterId;
        }

        public OperationHandle Operation { get; }
        public EquipmentSlotId SlotId { get; }
        public EquipmentActionRouteId RouteId { get; }
        public EquipmentId EquipmentId { get; }
        public EquipmentParameterId ParameterId { get; }
    }

    public sealed partial class EquipmentProgramLayout
    {
        readonly ReadOnlyCollection<EquipmentProgramSlot> m_Slots;
        readonly ReadOnlyCollection<EquipmentProgramFeature> m_Features;
        readonly ReadOnlyCollection<EquipmentProgramItem> m_Items;
        readonly ReadOnlyCollection<EquipmentProgramRoute> m_Routes;
        readonly ReadOnlyCollection<EquipmentProgramRouteImplementation> m_RouteImplementations;
        readonly ReadOnlyCollection<EquipmentProgramParameter> m_Parameters;
        readonly ReadOnlyCollection<EquipmentProgramLocalState> m_LocalStates;
        readonly ReadOnlyCollection<EquipmentProgramOperationBinding> m_OperationBindings;
        readonly Dictionary<EquipmentSlotId, EquipmentProgramSlot> m_SlotById;
        readonly Dictionary<EquipmentFeatureId, EquipmentProgramFeature> m_FeatureById;
        readonly Dictionary<string, EquipmentProgramFeature> m_FeatureByCodeBinding;
        readonly Dictionary<EquipmentId, EquipmentProgramItem> m_ItemById;
        readonly Dictionary<EquipmentActionRouteId, EquipmentProgramRoute> m_RouteById;
        readonly Dictionary<(EquipmentFeatureId, EquipmentActionRouteId), EquipmentProgramRouteImplementation> m_RouteImplementationByKey;
        readonly Dictionary<(EquipmentId, EquipmentParameterId), EquipmentProgramParameter> m_ParameterByKey;
        readonly Dictionary<(EquipmentFeatureId, EquipmentLocalStateId), EquipmentProgramLocalState> m_LocalStateByKey;
        readonly Dictionary<int, EquipmentProgramOperationBinding> m_OperationBindingByHandle;

        public EquipmentProgramLayout(
            bool capabilityEnabled,
            IEnumerable<EquipmentProgramSlot> slots,
            IEnumerable<EquipmentProgramFeature> features,
            IEnumerable<EquipmentProgramItem> items,
            IEnumerable<EquipmentProgramRoute> routes,
            IEnumerable<EquipmentProgramRouteImplementation> routeImplementations,
            IEnumerable<EquipmentProgramParameter> parameters,
            IEnumerable<EquipmentProgramLocalState> localStates,
            IEnumerable<EquipmentProgramOperationBinding> operationBindings)
        {
            CapabilityEnabled = capabilityEnabled;
            m_Slots = Canonical(slots, value => value.SlotId.ToString(), "Equipment Slot");
            m_Features = Canonical(features, value => value.FeatureId.Value, "Equipment Feature");
            m_Items = Canonical(items, value => value.EquipmentId.Value, "Equipment Item");
            m_Routes = Canonical(routes, value => value.RouteId.Value, "Equipment Route");
            m_RouteImplementations = Canonical(routeImplementations, value => $"{value.FeatureId.Value}:{value.RouteId.Value}", "Equipment Route implementation");
            m_Parameters = Canonical(parameters, value => $"{value.EquipmentId.Value}:{value.ParameterId.Value}", "Equipment Parameter");
            m_LocalStates = Canonical(localStates, value => $"{value.FeatureId.Value}:{value.StateId.Value}", "Equipment local state");
            m_OperationBindings = Canonical(operationBindings, value => value.Operation.Value.ToString("D10", System.Globalization.CultureInfo.InvariantCulture), "Equipment operation binding");
            if (!capabilityEnabled && (m_Slots.Count != 0 || m_Features.Count != 0 || m_Items.Count != 0 || m_Routes.Count != 0))
                throw new InvalidDataException("Equipment catalog exists while capability is disabled.");
            if (capabilityEnabled && m_Slots.Count == 0)
                throw new InvalidDataException("Equipment capability has no Slot catalog.");
            m_SlotById = m_Slots.ToDictionary(value => value.SlotId);
            m_FeatureById = m_Features.ToDictionary(value => value.FeatureId);
            m_FeatureByCodeBinding = new Dictionary<string, EquipmentProgramFeature>(StringComparer.Ordinal);
            for (int i = 0; i < m_Features.Count; i++)
            {
                EquipmentProgramFeature feature = m_Features[i];
                if (!m_FeatureByCodeBinding.TryAdd(feature.CodeBindingId, feature))
                    throw new InvalidDataException($"Equipment Feature code binding '{feature.CodeBindingId}' is duplicated.");
            }
            m_ItemById = m_Items.ToDictionary(value => value.EquipmentId);
            m_RouteById = m_Routes.ToDictionary(value => value.RouteId);
            m_RouteImplementationByKey = m_RouteImplementations.ToDictionary(value => (value.FeatureId, value.RouteId));
            m_ParameterByKey = m_Parameters.ToDictionary(value => (value.EquipmentId, value.ParameterId));
            m_LocalStateByKey = m_LocalStates.ToDictionary(value => (value.FeatureId, value.StateId));
            m_OperationBindingByHandle = m_OperationBindings.ToDictionary(value => value.Operation.Value);
            ValidateClosure();
        }

        public bool CapabilityEnabled { get; }
        public IReadOnlyList<EquipmentProgramSlot> Slots => m_Slots;
        public IReadOnlyList<EquipmentProgramFeature> Features => m_Features;
        public IReadOnlyList<EquipmentProgramItem> Items => m_Items;
        public IReadOnlyList<EquipmentProgramRoute> Routes => m_Routes;
        public IReadOnlyList<EquipmentProgramRouteImplementation> RouteImplementations => m_RouteImplementations;
        public IReadOnlyList<EquipmentProgramParameter> Parameters => m_Parameters;
        public IReadOnlyList<EquipmentProgramLocalState> LocalStates => m_LocalStates;
        public IReadOnlyList<EquipmentProgramOperationBinding> OperationBindings => m_OperationBindings;

        [ValueUpdate]
        public partial EquipmentProgramLayout WithOperationBindings(IEnumerable<EquipmentProgramOperationBinding> operationBindings);

        public EquipmentProgramSlot RequireSlot(EquipmentSlotId slotId) =>
            m_SlotById.TryGetValue(slotId, out EquipmentProgramSlot value) ? value : throw new InvalidOperationException($"Equipment Slot '{slotId}' is absent from Program.");
        public EquipmentProgramFeature RequireFeature(EquipmentFeatureId featureId) =>
            m_FeatureById.TryGetValue(featureId, out EquipmentProgramFeature value) ? value : throw new InvalidOperationException($"Equipment Feature '{featureId}' is absent from Program.");
        public EquipmentProgramFeature RequireFeatureByCodeBinding(string codeBindingId) =>
            m_FeatureByCodeBinding.TryGetValue(SimulationIdentity.Require(codeBindingId, nameof(codeBindingId)), out EquipmentProgramFeature value)
                ? value
                : throw new InvalidOperationException($"Equipment Feature code binding '{codeBindingId}' is absent from Program.");
        public EquipmentProgramItem RequireItem(EquipmentId equipmentId) =>
            m_ItemById.TryGetValue(equipmentId, out EquipmentProgramItem value) ? value : throw new InvalidOperationException($"Equipment '{equipmentId}' is absent from Program.");
        public EquipmentProgramRoute RequireRoute(EquipmentActionRouteId routeId) =>
            m_RouteById.TryGetValue(routeId, out EquipmentProgramRoute value) ? value : throw new InvalidOperationException($"Equipment Route '{routeId}' is absent from Program.");
        public bool HasRoute(EquipmentActionRouteId routeId) =>
            routeId.IsValid && m_RouteById.ContainsKey(routeId);
        public bool TryGetRouteImplementation(EquipmentFeatureId featureId, EquipmentActionRouteId routeId, out EquipmentProgramRouteImplementation value) =>
            m_RouteImplementationByKey.TryGetValue((featureId, routeId), out value);
        public EquipmentProgramParameter RequireParameter(EquipmentId equipmentId, EquipmentParameterId parameterId) =>
            m_ParameterByKey.TryGetValue((equipmentId, parameterId), out EquipmentProgramParameter value) ? value : throw new InvalidOperationException($"Equipment Parameter '{equipmentId}/{parameterId}' is absent from Program.");
        public EquipmentProgramLocalState RequireLocalState(EquipmentFeatureId featureId, EquipmentLocalStateId stateId) =>
            m_LocalStateByKey.TryGetValue((featureId, stateId), out EquipmentProgramLocalState value) ? value : throw new InvalidOperationException($"Equipment local state '{featureId}/{stateId}' is absent from Program.");
        public EquipmentSlotId RequireOperationSlot(OperationHandle operation)
        {
            EquipmentProgramOperationBinding binding = RequireOperationBinding(operation);
            return binding.SlotId.IsValid ? binding.SlotId : throw new InvalidOperationException($"Equipment operation '{operation}' has no Slot binding.");
        }
        public EquipmentActionRouteId RequireOperationRoute(OperationHandle operation)
        {
            EquipmentProgramOperationBinding binding = RequireOperationBinding(operation);
            return binding.RouteId.IsValid ? binding.RouteId : throw new InvalidOperationException($"Equipment operation '{operation}' has no Route binding.");
        }
        public EquipmentParameterId RequireOperationParameter(OperationHandle operation)
        {
            EquipmentProgramOperationBinding binding = RequireOperationBinding(operation);
            return binding.ParameterId.IsValid ? binding.ParameterId : throw new InvalidOperationException($"Equipment operation '{operation}' has no Parameter binding.");
        }
        public bool TryGetOperationEquipment(OperationHandle operation, out EquipmentId equipmentId)
        {
            if (m_OperationBindingByHandle.TryGetValue(operation.Value, out EquipmentProgramOperationBinding binding) && binding.EquipmentId.IsValid)
            {
                equipmentId = binding.EquipmentId;
                return true;
            }
            equipmentId = default;
            return false;
        }

        EquipmentProgramOperationBinding RequireOperationBinding(OperationHandle operation) =>
            m_OperationBindingByHandle.TryGetValue(operation.Value, out EquipmentProgramOperationBinding value)
                ? value
                : throw new InvalidOperationException($"Equipment operation '{operation}' has no compiled binding.");

        void ValidateClosure()
        {
            for (int i = 0; i < m_Items.Count; i++)
            {
                EquipmentProgramItem item = m_Items[i];
                if (!m_SlotById.ContainsKey(item.SlotId) || !m_FeatureById.ContainsKey(item.FeatureId))
                    throw new InvalidDataException($"Equipment Item '{item.EquipmentId}' has a dangling Slot or Feature.");
            }
            for (int i = 0; i < m_Slots.Count; i++)
            {
                EquipmentProgramSlot slot = m_Slots[i];
                if (!slot.InitialEquipmentId.IsValid)
                    continue;
                EquipmentProgramItem item = RequireItem(slot.InitialEquipmentId);
                if (item.SlotId != slot.SlotId)
                    throw new InvalidDataException($"Equipment Slot '{slot.SlotId}' initial item targets another Slot.");
            }
            for (int i = 0; i < m_Routes.Count; i++)
            {
                if (!m_SlotById.ContainsKey(m_Routes[i].OwnerSlotId))
                    throw new InvalidDataException($"Equipment Route '{m_Routes[i].RouteId}' owner Slot is absent.");
            }
            for (int i = 0; i < m_LocalStates.Count; i++)
                if (!m_FeatureById.ContainsKey(m_LocalStates[i].FeatureId))
                    throw new InvalidDataException($"Equipment local state '{m_LocalStates[i].FeatureId}/{m_LocalStates[i].StateId}' feature is absent.");
            for (int i = 0; i < m_RouteImplementations.Count; i++)
            {
                EquipmentProgramRouteImplementation implementation = m_RouteImplementations[i];
                if (!m_FeatureById.ContainsKey(implementation.FeatureId) || !m_RouteById.ContainsKey(implementation.RouteId))
                    throw new InvalidDataException($"Equipment Route implementation '{implementation.FeatureId}/{implementation.RouteId}' has a dangling Feature or Route.");
                if (m_RouteById[implementation.RouteId].OwnerSlotId.IsValid &&
                    !m_SlotById.ContainsKey(m_RouteById[implementation.RouteId].OwnerSlotId))
                    throw new InvalidDataException($"Equipment Route implementation '{implementation.FeatureId}/{implementation.RouteId}' has a dangling owner Slot.");
                for (int parameterIndex = 0; parameterIndex < implementation.RequiredParameterIds.Count; parameterIndex++)
                {
                    EquipmentParameterId parameterId = implementation.RequiredParameterIds[parameterIndex];
                    if (!m_Parameters.Any(value => value.FeatureId == implementation.FeatureId && value.ParameterId == parameterId))
                        throw new InvalidDataException($"Equipment Route implementation '{implementation.FeatureId}/{implementation.RouteId}' requires unknown Parameter '{parameterId}'.");
                    for (int itemIndex = 0; itemIndex < m_Items.Count; itemIndex++)
                    {
                        EquipmentProgramItem item = m_Items[itemIndex];
                        if (item.FeatureId == implementation.FeatureId && !m_ParameterByKey.ContainsKey((item.EquipmentId, parameterId)))
                            throw new InvalidDataException($"Equipment Route implementation '{implementation.FeatureId}/{implementation.RouteId}' requires Parameter '{parameterId}' on Equipment '{item.EquipmentId}'.");
                    }
                }
            }
        }

        internal void ValidateProducerBindings(IReadOnlyList<ProgramProducer> producers)
        {
            if (producers == null)
                throw new ArgumentNullException(nameof(producers));
            var identities = new HashSet<string>(producers.Select(value => value.Identity), StringComparer.Ordinal);
            for (int i = 0; i < m_RouteImplementations.Count; i++)
                for (int producerIndex = 0; producerIndex < m_RouteImplementations[i].RequiredProducerIds.Count; producerIndex++)
                    if (!identities.Contains(m_RouteImplementations[i].RequiredProducerIds[producerIndex]))
                        throw new InvalidDataException($"Equipment Route implementation '{m_RouteImplementations[i].FeatureId}/{m_RouteImplementations[i].RouteId}' requires unknown Producer '{m_RouteImplementations[i].RequiredProducerIds[producerIndex]}'.");
        }



        static ReadOnlyCollection<T> Canonical<T>(IEnumerable<T> source, Func<T, string> identity, string label)
        {
            T[] values = (source ?? Array.Empty<T>()).OrderBy(identity, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null)
                    throw new InvalidDataException($"{label} contains null.");
                if (i > 0 && string.Equals(identity(values[i - 1]), identity(values[i]), StringComparison.Ordinal))
                    throw new InvalidDataException($"{label} identity '{identity(values[i])}' is duplicated.");
            }
            return Array.AsReadOnly(values);
        }
    }

    public enum PendingEquipmentChangeState : byte
    {
        Pending = 1,
        Committed = 2,
        Cancelled = 3
    }

    [ValueOperations]
    public readonly partial struct PendingEquipmentChange
    {
        public PendingEquipmentChange(
            EquipmentChangeId changeId,
            EquipmentSlotId slotId,
            EquipmentId fromEquipmentId,
            EquipmentId toEquipmentId,
            ulong sourceActionInstanceId,
            ulong beginTick,
            ulong resolvedTick,
            PendingEquipmentChangeState state)
        {
            if (!IsValidState(state))
                throw new ArgumentException("Pending Equipment Change state is invalid.");
            ChangeId = changeId;
            SlotId = slotId;
            FromEquipmentId = fromEquipmentId;
            ToEquipmentId = toEquipmentId;
            SourceActionInstanceId = sourceActionInstanceId;
            BeginTick = beginTick;
            ResolvedTick = resolvedTick;
            State = state;
            RequireRecord();
        }

        [ValueValidation]
        void RequireRecord()
        {
            if (!ChangeId.IsValid || !SlotId.IsValid || BeginTick == 0)
                throw new ArgumentException("Pending Equipment Change is invalid.");
            if (State == PendingEquipmentChangeState.Pending && ResolvedTick != 0 ||
                State != PendingEquipmentChangeState.Pending && ResolvedTick < BeginTick)
                throw new ArgumentException("Equipment Change resolution tick is inconsistent.");
        }

        public EquipmentChangeId ChangeId { get; }
        public EquipmentSlotId SlotId { get; }
        public EquipmentId FromEquipmentId { get; }
        public EquipmentId ToEquipmentId { get; }
        public ulong SourceActionInstanceId { get; }
        public ulong BeginTick { get; }
        public ulong ResolvedTick { get; }
        public PendingEquipmentChangeState State { get; }
        internal static bool IsValidState(PendingEquipmentChangeState state) =>
            state is PendingEquipmentChangeState.Pending or PendingEquipmentChangeState.Committed or PendingEquipmentChangeState.Cancelled;
        public bool IsValid => ChangeId.IsValid;
        public bool IsPending => IsValid && State == PendingEquipmentChangeState.Pending;

        public PendingEquipmentChange Resolve(PendingEquipmentChangeState state, ulong resolvedTick)
        {
            if (!IsPending || state == PendingEquipmentChangeState.Pending)
                throw new InvalidOperationException("Only an active Equipment Change can be resolved.");
            return new PendingEquipmentChange(
                ChangeId,
                SlotId,
                FromEquipmentId,
                ToEquipmentId,
                SourceActionInstanceId,
                BeginTick,
                resolvedTick,
                state);
        }
    }

    [ValueOperations]
    public readonly partial struct EquipmentSlotState
    {

        public EquipmentSlotState(
            EquipmentSlotId slotId,
            EquipmentId equipmentId,
            EquipmentFeatureId featureId,
            EquipmentFeatureRevision featureRevision,
            EquipmentVisualBindingId visualBindingId,
            ulong revision,
            ulong generation,
            bool contributionsInstalled,
            GameplayTagSourceIdentityValue tagSource,
            EquipmentPassiveHandles passiveEffectHandles)
        {
            SlotId = slotId;
            EquipmentId = equipmentId;
            FeatureId = featureId;
            FeatureRevision = featureRevision;
            VisualBindingId = visualBindingId;
            Revision = revision;
            Generation = generation;
            ContributionsInstalled = contributionsInstalled;
            TagSource = tagSource;
            Handles = passiveEffectHandles;
            RequireRecord();
        }

        [ValueValidation]
        void RequireRecord()
        {
            if (!SlotId.IsValid || Revision == 0 || Generation == 0)
                throw new ArgumentException("Equipment Slot state is invalid.");
            bool empty = !EquipmentId.IsValid;
            bool hasAnyContributionIdentity = FeatureId.IsValid || FeatureRevision.IsValid || VisualBindingId.IsValid;
            bool hasCompleteContributionIdentity = FeatureId.IsValid && FeatureRevision.IsValid && VisualBindingId.IsValid;
            if (empty && hasAnyContributionIdentity || !empty && !hasCompleteContributionIdentity || empty && ContributionsInstalled)
            {
                throw new ArgumentException("Equipment Slot state contribution is inconsistent.");
            }
            for (int i = 0; i < Handles.Count; i++)
            {
                if (Handles[i] == 0)
                    throw new ArgumentException("Equipment Slot passive Effect handle is zero.", nameof(Handles));
                for (int prior = 0; prior < i; prior++)
                {
                    if (Handles[prior] == Handles[i])
                        throw new ArgumentException("Equipment Slot passive Effect handles are duplicated.", nameof(Handles));
                }
            }
            if (empty && (!TagSource.IsEmpty || Handles.Count != 0))
                throw new ArgumentException("Empty Equipment Slot cannot own contributions.");
        }

        public EquipmentSlotId SlotId { get; }
        public EquipmentId EquipmentId { get; }
        public EquipmentFeatureId FeatureId { get; }
        public EquipmentFeatureRevision FeatureRevision { get; }
        public EquipmentVisualBindingId VisualBindingId { get; }
        public ulong Revision { get; }
        public ulong Generation { get; }
        public bool ContributionsInstalled { get; }
        public GameplayTagSourceIdentityValue TagSource { get; }
        public IReadOnlyList<ulong> PassiveEffectHandles => Handles.Values;
        public EquipmentPassiveHandles Handles { get; }
        public bool IsEquipped => EquipmentId.IsValid;
        public EquipmentActionContext ActionContext(EquipmentActionRouteId routeId) => IsEquipped
            ? new EquipmentActionContext(SlotId, EquipmentId, FeatureId, Revision, routeId)
            : default;
        public EquipmentVisualSelection CreateVisualSelection(ActorId actorId, ulong sourceTick) =>
            new EquipmentVisualSelection(actorId, SlotId, EquipmentId, VisualBindingId, Revision, sourceTick);

        public bool Equals(EquipmentSlotState other) =>
            SlotId == other.SlotId &&
            EquipmentId == other.EquipmentId &&
            FeatureId == other.FeatureId &&
            FeatureRevision == other.FeatureRevision &&
            VisualBindingId == other.VisualBindingId &&
            Revision == other.Revision &&
            Generation == other.Generation &&
            ContributionsInstalled == other.ContributionsInstalled &&
            TagSource.Equals(other.TagSource) &&
            SameHandles(PassiveEffectHandles, other.PassiveEffectHandles);

        static bool SameHandles(IReadOnlyList<ulong> left, IReadOnlyList<ulong> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }
    }

    [ValueBorrowedSequence(typeof(EquipmentStatePageBank))]
    public readonly struct EquipmentPassiveHandles
    {
        readonly EquipmentPassiveHandlePage m_Page;
        readonly ulong m_Generation;

        internal EquipmentPassiveHandles(EquipmentPassiveHandlePage page)
        {
            m_Page = page;
            m_Generation = page.Generation;
        }

        public int Count => m_Page == null ? 0 : m_Page.Count;
        public ulong this[int index]
        {
            get => m_Page.Values[index];
            set => m_Page.Values[index] = value;
        }

        internal IReadOnlyList<ulong> Values => m_Page == null ? Array.Empty<ulong>() : m_Page;
        internal ReadOnlySpan<ulong> Span => MutableSpan;
        internal Span<ulong> MutableSpan => m_Page == null ? Span<ulong>.Empty : m_Page.Values.AsSpan(0, m_Page.Count);
        internal void RetainStateStorage()
        {
            if (m_Page == null)
                return;
            if (m_Generation != m_Page.Generation)
                throw new InvalidOperationException("Equipment passive handles are outside their retained lifetime.");
            m_Page.References++;
        }

        internal void ReleaseStateStorage()
        {
            if (m_Page != null)
                m_Page.References--;
        }
    }

    internal sealed class EquipmentPassiveHandlePage : IReadOnlyList<ulong>
    {
        internal EquipmentPassiveHandlePage(int capacity) => Values = new ulong[capacity];

        internal readonly ulong[] Values;
        internal ulong Generation;
        internal int References;
        public int Count { get; internal set; }
        public ulong this[int index] => Values[index];
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<ulong> IEnumerable<ulong>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<ulong>
        {
            readonly EquipmentPassiveHandlePage m_Page;
            int m_Index;
            internal Enumerator(EquipmentPassiveHandlePage page) { m_Page = page; m_Index = -1; }
            public ulong Current => m_Page.Values[m_Index];
            object System.Collections.IEnumerator.Current => Current;
            public bool MoveNext() => ++m_Index < m_Page.Count;
            public void Reset() => m_Index = -1;
            public void Dispose() { }
        }
    }

    public sealed class EquipmentStatePageBank
    {
        readonly EquipmentStateAggregate[] m_Pages;
        readonly EquipmentPassiveHandlePage[] m_HandlePages;

        public EquipmentStatePageBank(EquipmentProgramLayout layout, int statePageCount, int savepointDepth)
        {
            Layout = layout;
            m_Pages = new EquipmentStateAggregate[statePageCount];
            for (int index = 0; index < m_Pages.Length; index++)
                m_Pages[index] = new EquipmentStateAggregate(this);
            int maximumPassiveEffects = 0;
            for (int feature = 0; feature < layout.Features.Count; feature++)
                maximumPassiveEffects = Math.Max(maximumPassiveEffects, layout.Features[feature].PassiveEffects.Count);
            m_HandlePages = new EquipmentPassiveHandlePage[checked(statePageCount * layout.Slots.Count + savepointDepth)];
            for (int index = 0; index < m_HandlePages.Length; index++)
                m_HandlePages[index] = new EquipmentPassiveHandlePage(maximumPassiveEffects);
        }

        public EquipmentProgramLayout Layout { get; }

        internal EquipmentStateAggregate Begin(EquipmentStateAggregate source = null)
        {
            for (int index = 0; index < m_Pages.Length; index++)
            {
                EquipmentStateAggregate page = m_Pages[index];
                if (page.References != 0 || ReferenceEquals(page, source))
                    continue;
                page.CopyFrom(source);
                return page;
            }
            throw new InvalidOperationException("Equipment state pages are exhausted by retained state versions and the active transaction.");
        }

        public EquipmentStateAggregate CreateInitial()
        {
            EquipmentStateAggregate state = Begin();
            for (int index = 0; index < Layout.Slots.Count; index++)
            {
                EquipmentProgramSlot definition = Layout.Slots[index];
                EquipmentSlotState slot;
                if (definition.InitialEquipmentId.IsValid)
                {
                    EquipmentProgramItem item = Layout.RequireItem(definition.InitialEquipmentId);
                    EquipmentProgramFeature feature = Layout.RequireFeature(item.FeatureId);
                    slot = new EquipmentSlotState(definition.SlotId, item.EquipmentId, item.FeatureId,
                        feature.Revision, item.VisualBindingId, 1, 1, false, GameplayTagSourceIdentityValue.Empty, default);
                }
                else
                    slot = new EquipmentSlotState(definition.SlotId, default, default, default, default,
                        1, 1, false, GameplayTagSourceIdentityValue.Empty, default);
                state.SetSlot(index, slot);
            }
            return state;
        }

        public EquipmentPassiveHandles AcquirePassiveHandles(EquipmentFeatureId featureId) =>
            AcquirePassiveHandles(Layout.RequireFeature(featureId).PassiveEffects.Count);

        internal EquipmentPassiveHandles AcquirePassiveHandles(int count)
        {
            if (count == 0)
                return default;
            for (int index = 0; index < m_HandlePages.Length; index++)
            {
                EquipmentPassiveHandlePage page = m_HandlePages[index];
                if (page.References != 0)
                    continue;
                page.Count = count;
                page.Generation++;
                page.References = 1;
                return new EquipmentPassiveHandles(page);
            }
            throw new InvalidOperationException("Equipment passive handle pages are exhausted by retained slots and active installation.");
        }

        public void ReleasePassiveHandles(in EquipmentPassiveHandles handles) => handles.ReleaseStateStorage();

    }

    [ValueOperations(ExplicitResources = true, RetainMethod = "RetainMembers", ReleaseMethod = "ReleaseMembers")]
    public sealed partial class EquipmentStateAggregate
    {
        readonly EquipmentStatePageBank m_Bank;
        [ValueResource(Elements = true)]
        readonly EquipmentSlotState[] m_Slots;
        [ValueResource(Elements = true)]
        readonly EquipmentLocalStateValue[] m_LocalStates;
        internal int References { get; private set; }
        internal EquipmentStatePageBank Storage => m_Bank;

        internal EquipmentStateAggregate(EquipmentStatePageBank bank)
        {
            m_Bank = bank;
            m_Slots = new EquipmentSlotState[bank.Layout.Slots.Count];
            m_LocalStates = new EquipmentLocalStateValue[bank.Layout.LocalStates.Count];
            for (int index = 0; index < m_LocalStates.Length; index++)
            {
                EquipmentProgramLocalState definition = bank.Layout.LocalStates[index];
                m_LocalStates[index] = new EquipmentLocalStateValue(definition.FeatureId, definition.StateId, definition.DefaultValue);
            }
        }

        public IReadOnlyList<EquipmentSlotState> Slots => m_Slots;
        public IReadOnlyList<EquipmentLocalStateValue> LocalStates => m_LocalStates;
        [ValueResource]
        public PendingEquipmentChange PendingChange { get; private set; }
        [ValueResource]
        public PendingEquipmentChange LastResolvedChange { get; private set; }

        internal void CopyFrom(EquipmentStateAggregate source)
        {
            if (source == null)
            {
                Array.Clear(m_Slots, 0, m_Slots.Length);
                for (int index = 0; index < m_LocalStates.Length; index++)
                {
                    EquipmentProgramLocalState definition = m_Bank.Layout.LocalStates[index];
                    m_LocalStates[index] = new EquipmentLocalStateValue(definition.FeatureId, definition.StateId, definition.DefaultValue);
                }
                PendingChange = default;
                LastResolvedChange = default;
                return;
            }
            Array.Copy(source.m_Slots, m_Slots, m_Slots.Length);
            Array.Copy(source.m_LocalStates, m_LocalStates, m_LocalStates.Length);
            PendingChange = source.PendingChange;
            LastResolvedChange = source.LastResolvedChange;
        }

        internal void Retain()
        {
            if (References++ != 0)
                return;
            RetainMembers();
        }

        internal void Release()
        {
            if (--References != 0)
                return;
            ReleaseMembers();
        }

        internal void SetSlot(int index, EquipmentSlotState slot)
        {
            if (References != 0)
            {
                slot.Retain();
                m_Slots[index].Release();
            }
            m_Slots[index] = slot;
        }

        internal void RestoreLocalState(int index, in EquipmentLocalStateValue value)
        {
            if (References != 0)
            {
                value.Retain();
                m_LocalStates[index].Release();
            }
            m_LocalStates[index] = value;
        }

        internal void RestoreChanges(PendingEquipmentChange pending, PendingEquipmentChange resolved)
        {
            if (References != 0)
            {
                pending.Retain();
                resolved.Retain();
                PendingChange.Release();
                LastResolvedChange.Release();
            }
            PendingChange = pending;
            LastResolvedChange = resolved;
        }

        static int CompareJoinedIdentity(string leftFeature, string leftState, string rightFeature, string rightState)
        {
            int leftLength = leftFeature.Length + 1 + leftState.Length;
            int rightLength = rightFeature.Length + 1 + rightState.Length;
            int length = Math.Min(leftLength, rightLength);
            for (int i = 0; i < length; i++)
            {
                char leftValue = i < leftFeature.Length ? leftFeature[i] :
                    i == leftFeature.Length ? ':' : leftState[i - leftFeature.Length - 1];
                char rightValue = i < rightFeature.Length ? rightFeature[i] :
                    i == rightFeature.Length ? ':' : rightState[i - rightFeature.Length - 1];
                if (leftValue != rightValue)
                    return leftValue < rightValue ? -1 : 1;
            }
            return leftLength.CompareTo(rightLength);
        }

        public EquipmentSlotState RequireSlot(EquipmentSlotId slotId)
        {
            int index = FindSlot(slotId);
            if (index >= 0)
                return m_Slots[index];
            throw new InvalidOperationException($"Equipment state Slot '{slotId}' is absent.");
        }

        int FindSlot(EquipmentSlotId slotId)
        {
            int low = 0;
            int high = m_Slots.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = m_Slots[middle].SlotId.CompareTo(slotId);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return -1;
        }

        public EquipmentRuntimeStateValue RequireLocalState(EquipmentFeatureId featureId, EquipmentLocalStateId stateId)
        {
            int index = FindLocalState(featureId, stateId);
            if (index >= 0)
                return m_LocalStates[index].Value;
            throw new InvalidOperationException($"Equipment local state '{featureId}/{stateId}' is absent.");
        }

        int FindLocalState(EquipmentFeatureId featureId, EquipmentLocalStateId stateId)
        {
            int low = 0;
            int high = m_LocalStates.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = CompareJoinedIdentity(m_LocalStates[middle].FeatureId.Value,
                    m_LocalStates[middle].StateId.Value, featureId.Value, stateId.Value);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return -1;
        }

        public EquipmentStateAggregate WithSlot(EquipmentSlotState slot)
        {
            int index = FindSlot(slot.SlotId);
            if (index < 0)
                throw new InvalidOperationException($"Equipment state Slot '{slot.SlotId}' is absent.");
            if (m_Slots[index].Equals(slot))
                return this;
            EquipmentStateAggregate next = m_Bank.Begin(this);
            next.SetSlot(index, slot);
            return next;
        }

        public EquipmentStateAggregate WithLocalState(EquipmentFeatureId featureId,
            EquipmentLocalStateId stateId, EquipmentRuntimeStateValue value)
        {
            int index = FindLocalState(featureId, stateId);
            if (index < 0)
                throw new InvalidOperationException($"Equipment local state '{featureId}/{stateId}' is absent.");
            EquipmentRuntimeStateValue current = m_LocalStates[index].Value;
            if (current.Equals(value))
                return this;
            if (current.Kind != value.Kind)
                throw new InvalidOperationException($"Equipment local state '{featureId}/{stateId}' value kind changed.");
            EquipmentStateAggregate next = m_Bank.Begin(this);
            next.m_LocalStates[index] = new EquipmentLocalStateValue(featureId, stateId, value);
            return next;
        }

        public EquipmentStateAggregate WithPending(PendingEquipmentChange pending)
        {
            EquipmentStateAggregate next = m_Bank.Begin(this);
            next.PendingChange = pending;
            return next;
        }

        public EquipmentStateAggregate ResolvePending(PendingEquipmentChangeState state, ulong resolvedTick)
        {
            if (!PendingChange.IsPending)
                throw new InvalidOperationException("Equipment aggregate has no active pending change.");
            EquipmentStateAggregate next = m_Bank.Begin(this);
            next.PendingChange = default;
            next.LastResolvedChange = PendingChange.Resolve(state, resolvedTick);
            return next;
        }

        public EquipmentStateAggregate WithSlotAndResolvedPending(EquipmentSlotState slot,
            PendingEquipmentChangeState state, ulong resolvedTick)
        {
            if (!PendingChange.IsPending)
                throw new InvalidOperationException("Equipment aggregate has no active pending change.");
            if (slot.SlotId != PendingChange.SlotId)
                throw new InvalidOperationException($"Equipment pending change targets '{PendingChange.SlotId.Value}', not '{slot.SlotId.Value}'.");
            int index = FindSlot(slot.SlotId);
            if (index < 0)
                throw new InvalidOperationException($"Equipment state Slot '{slot.SlotId}' is absent.");
            EquipmentStateAggregate next = m_Bank.Begin(this);
            next.SetSlot(index, slot);
            next.PendingChange = default;
            next.LastResolvedChange = PendingChange.Resolve(state, resolvedTick);
            return next;
        }
    }

}
