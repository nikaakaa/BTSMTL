using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

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
                defaultValue == null || defaultValue.Kind != valueKind)
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

    public sealed class EquipmentLocalStateValue
    {
        public EquipmentLocalStateValue(
            EquipmentFeatureId featureId,
            EquipmentLocalStateId stateId,
            EquipmentRuntimeStateValue value)
        {
            if (!featureId.IsValid || !stateId.IsValid || value == null)
                throw new ArgumentException("Equipment local state value is invalid.");
            FeatureId = featureId;
            StateId = stateId;
            Value = value;
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

    public sealed class EquipmentProgramLayout
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
            CatalogHash = ComputeHash();
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
        public StableHash CatalogHash { get; }

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

        StableHash ComputeHash()
        {
            using var writer = new CanonicalWriter();
            writer.WriteText("equipment-program-layout/v4");
            writer.WriteBoolean(CapabilityEnabled);
            writer.WriteInt32(m_Slots.Count);
            for (int i = 0; i < m_Slots.Count; i++)
            {
                writer.WriteText(m_Slots[i].SlotId.Value);
                writer.WriteByte((byte)m_Slots[i].Requirement);
                writer.WriteText(m_Slots[i].InitialEquipmentId.Value);
            }
            writer.WriteInt32(m_Features.Count);
            for (int i = 0; i < m_Features.Count; i++)
            {
                EquipmentProgramFeature feature = m_Features[i];
                writer.WriteText(feature.FeatureId.Value);
                writer.WriteUInt64(feature.Revision.Value);
                writer.WriteText(feature.CodeBindingId);
                writer.WriteUInt64((ulong)feature.RequiredWorldCapabilities);
                writer.WriteInt32(feature.GrantedTags.Count);
                for (int tag = 0; tag < feature.GrantedTags.Count; tag++) writer.WriteText(feature.GrantedTags[tag]);
                writer.WriteInt32(feature.PassiveEffects.Count);
                for (int effect = 0; effect < feature.PassiveEffects.Count; effect++) writer.WriteText(feature.PassiveEffects[effect]);
            }
            writer.WriteInt32(m_Items.Count);
            for (int i = 0; i < m_Items.Count; i++)
            {
                writer.WriteText(m_Items[i].EquipmentId.Value);
                writer.WriteText(m_Items[i].SlotId.Value);
                writer.WriteText(m_Items[i].FeatureId.Value);
                writer.WriteText(m_Items[i].VisualBindingId.Value);
            }
            writer.WriteInt32(m_Routes.Count);
            for (int i = 0; i < m_Routes.Count; i++)
            {
                writer.WriteText(m_Routes[i].RouteId.Value);
                writer.WriteText(m_Routes[i].OwnerSlotId.Value);
                writer.WriteText(m_Routes[i].InputRequestId);
                writer.WriteByte((byte)m_Routes[i].RequestConsumption);
                writer.WriteByte((byte)m_Routes[i].MissingImplementation);
            }
            writer.WriteInt32(m_RouteImplementations.Count);
            for (int i = 0; i < m_RouteImplementations.Count; i++)
            {
                writer.WriteText(m_RouteImplementations[i].FeatureId.Value);
                writer.WriteText(m_RouteImplementations[i].RouteId.Value);
                writer.WriteText(m_RouteImplementations[i].AbilityId.Value);
                writer.WriteInt32(m_RouteImplementations[i].RequiredParameterIds.Count);
                for (int parameter = 0; parameter < m_RouteImplementations[i].RequiredParameterIds.Count; parameter++)
                    writer.WriteText(m_RouteImplementations[i].RequiredParameterIds[parameter].Value);
                writer.WriteInt32(m_RouteImplementations[i].RequiredProducerIds.Count);
                for (int producer = 0; producer < m_RouteImplementations[i].RequiredProducerIds.Count; producer++)
                    writer.WriteText(m_RouteImplementations[i].RequiredProducerIds[producer]);
            }
            writer.WriteInt32(m_Parameters.Count);
            for (int i = 0; i < m_Parameters.Count; i++)
            {
                writer.WriteText(m_Parameters[i].EquipmentId.Value);
                writer.WriteText(m_Parameters[i].FeatureId.Value);
                writer.WriteText(m_Parameters[i].ParameterId.Value);
                writer.WriteByte((byte)m_Parameters[i].ValueKind);
                EquipmentRuntimeParameterValue value = m_Parameters[i].Value;
                writer.WriteBoolean(value.Boolean);
                writer.WriteInt32(value.Int32);
                writer.WriteUInt64(value.UInt64);
                writer.WriteDouble(value.X);
                writer.WriteDouble(value.Y);
                writer.WriteDouble(value.Z);
                writer.WriteText(value.Identity);
            }
            writer.WriteInt32(m_LocalStates.Count);
            for (int i = 0; i < m_LocalStates.Count; i++)
            {
                writer.WriteText(m_LocalStates[i].FeatureId.Value);
                writer.WriteText(m_LocalStates[i].StateId.Value);
                writer.WriteByte((byte)m_LocalStates[i].ValueKind);
                WriteStateValue(writer, m_LocalStates[i].DefaultValue);
            }
            writer.WriteInt32(m_OperationBindings.Count);
            for (int i = 0; i < m_OperationBindings.Count; i++)
            {
                EquipmentProgramOperationBinding binding = m_OperationBindings[i];
                writer.WriteInt32(binding.Operation.Value);
                writer.WriteText(binding.SlotId.Value);
                writer.WriteText(binding.RouteId.Value);
                writer.WriteText(binding.EquipmentId.Value);
                writer.WriteText(binding.ParameterId.Value);
            }
            return writer.ComputeHash();
        }

        static void WriteStateValue(CanonicalWriter writer, EquipmentRuntimeStateValue value)
        {
            writer.WriteBoolean(value.Boolean);
            writer.WriteInt32(value.Int32);
            writer.WriteUInt64(value.UInt64);
            writer.WriteDouble(value.X);
            writer.WriteDouble(value.Y);
            writer.WriteDouble(value.Z);
            writer.WriteText(value.Identity);
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

    public readonly struct PendingEquipmentChange
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
            if (!changeId.IsValid || !slotId.IsValid || beginTick == 0 || !IsValidState(state))
                throw new ArgumentException("Pending Equipment Change is invalid.");
            if (state == PendingEquipmentChangeState.Pending && resolvedTick != 0 ||
                state != PendingEquipmentChangeState.Pending && resolvedTick < beginTick)
            {
                throw new ArgumentException("Equipment Change resolution tick is inconsistent.");
            }
            ChangeId = changeId;
            SlotId = slotId;
            FromEquipmentId = fromEquipmentId;
            ToEquipmentId = toEquipmentId;
            SourceActionInstanceId = sourceActionInstanceId;
            BeginTick = beginTick;
            ResolvedTick = resolvedTick;
            State = state;
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

    public readonly struct EquipmentSlotState
    {
        readonly EquipmentPassiveHandles m_PassiveEffectHandles;

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
            if (!slotId.IsValid || revision == 0 || generation == 0)
                throw new ArgumentException("Equipment Slot state is invalid.");
            bool empty = !equipmentId.IsValid;
            bool hasAnyContributionIdentity = featureId.IsValid || featureRevision.IsValid || visualBindingId.IsValid;
            bool hasCompleteContributionIdentity = featureId.IsValid && featureRevision.IsValid && visualBindingId.IsValid;
            if (empty && hasAnyContributionIdentity || !empty && !hasCompleteContributionIdentity || empty && contributionsInstalled)
            {
                throw new ArgumentException("Equipment Slot state contribution is inconsistent.");
            }
            SlotId = slotId;
            EquipmentId = equipmentId;
            FeatureId = featureId;
            FeatureRevision = featureRevision;
            VisualBindingId = visualBindingId;
            Revision = revision;
            Generation = generation;
            ContributionsInstalled = contributionsInstalled;
            TagSource = tagSource;
            m_PassiveEffectHandles = passiveEffectHandles;
            for (int i = 0; i < m_PassiveEffectHandles.Count; i++)
            {
                if (m_PassiveEffectHandles[i] == 0)
                    throw new ArgumentException("Equipment Slot passive Effect handle is zero.", nameof(passiveEffectHandles));
                for (int prior = 0; prior < i; prior++)
                {
                    if (m_PassiveEffectHandles[prior] == m_PassiveEffectHandles[i])
                        throw new ArgumentException("Equipment Slot passive Effect handles are duplicated.", nameof(passiveEffectHandles));
                }
            }
            if (empty && (!TagSource.IsEmpty || m_PassiveEffectHandles.Count != 0))
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
        public IReadOnlyList<ulong> PassiveEffectHandles => m_PassiveEffectHandles.Values;
        internal EquipmentPassiveHandles Handles => m_PassiveEffectHandles;
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
        internal void Retain()
        {
            if (m_Page == null)
                return;
            if (m_Generation != m_Page.Generation)
                throw new InvalidOperationException("Equipment passive handles are outside their retained lifetime.");
            m_Page.References++;
        }

        internal void Release()
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
        readonly string[] m_Identities;
        readonly bool[] m_DecodedSlots;
        readonly bool[] m_DecodedLocalStates;
        EquipmentStateAggregate m_DecodeState;

        public EquipmentStatePageBank(ActorId actorId, EquipmentProgramLayout layout, int statePageCount, int savepointDepth)
        {
            Layout = layout;
            var identities = new SortedSet<string>(StringComparer.Ordinal) { actorId.ToString() };
            for (int index = 0; index < layout.Slots.Count; index++)
                identities.Add(layout.Slots[index].SlotId.ToString());
            for (int index = 0; index < layout.Features.Count; index++)
                identities.Add(layout.Features[index].FeatureId.Value);
            for (int index = 0; index < layout.Items.Count; index++)
            {
                identities.Add(layout.Items[index].EquipmentId.Value);
                identities.Add(layout.Items[index].VisualBindingId.Value);
            }
            for (int index = 0; index < layout.LocalStates.Count; index++)
                identities.Add(layout.LocalStates[index].StateId.Value);
            m_Identities = identities.ToArray();
            DecodeReader = new CanonicalReader(Array.Empty<byte>());
            m_DecodedSlots = new bool[layout.Slots.Count];
            m_DecodedLocalStates = new bool[layout.LocalStates.Count];
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
        internal CanonicalReader DecodeReader { get; }

        internal string ReadIdentity(CanonicalReader reader)
        {
            ReadOnlySpan<byte> identity = reader.ReadTextSegment().AsSpan();
            if (identity.Length == 0)
                return string.Empty;
            for (int index = 0; index < m_Identities.Length; index++)
            {
                if (CanonicalTextEncoding.EqualsEncoded(identity, m_Identities[index].AsSpan()))
                    return m_Identities[index];
            }
            throw new InvalidDataException("Equipment state identity is absent from the prepared Actor and Program catalog.");
        }

        internal int ReadSlotIndex(EquipmentSlotId slotId)
        {
            for (int index = 0; index < Layout.Slots.Count; index++)
            {
                if (Layout.Slots[index].SlotId != slotId)
                    continue;
                if (m_DecodedSlots[index])
                    throw new InvalidDataException($"Equipment state Slot '{slotId}' is duplicated.");
                m_DecodedSlots[index] = true;
                return index;
            }
            throw new InvalidDataException($"Equipment state Slot '{slotId}' is absent from the Program catalog.");
        }

        internal int ReadLocalStateIndex(EquipmentFeatureId featureId, EquipmentLocalStateId stateId)
        {
            for (int index = 0; index < Layout.LocalStates.Count; index++)
            {
                EquipmentProgramLocalState definition = Layout.LocalStates[index];
                if (definition.FeatureId != featureId || definition.StateId != stateId)
                    continue;
                if (m_DecodedLocalStates[index])
                    throw new InvalidDataException($"Equipment local state '{featureId}/{stateId}' is duplicated.");
                m_DecodedLocalStates[index] = true;
                return index;
            }
            throw new InvalidDataException($"Equipment local state '{featureId}/{stateId}' is absent from the Program catalog.");
        }

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

        public void ReleasePassiveHandles(in EquipmentPassiveHandles handles) => handles.Release();

        internal EquipmentStateAggregate BeginDecode()
        {
            ReleaseDecodedState();
            Array.Clear(m_DecodedSlots, 0, m_DecodedSlots.Length);
            Array.Clear(m_DecodedLocalStates, 0, m_DecodedLocalStates.Length);
            m_DecodeState = Begin();
            m_DecodeState.Retain();
            return m_DecodeState;
        }

        internal void ReleaseDecodedState()
        {
            if (m_DecodeState == null)
                return;
            m_DecodeState.Release();
            m_DecodeState = null;
        }

        internal void CompleteDecodeAdoption(EquipmentStateAggregate state)
        {
            if (ReferenceEquals(m_DecodeState, state))
                ReleaseDecodedState();
        }
    }

    public sealed class EquipmentStateAggregate
    {
        readonly EquipmentStatePageBank m_Bank;
        readonly EquipmentSlotState[] m_Slots;
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
                EquipmentRuntimeStateValue value = definition.DefaultValue;
                m_LocalStates[index] = new EquipmentLocalStateValue(definition.FeatureId, definition.StateId,
                    new EquipmentRuntimeStateValue(value.Kind, value.Boolean, value.Int32, value.UInt64,
                        value.X, value.Y, value.Z, value.Identity));
            }
        }

        public StableHash CatalogHash => m_Bank.Layout.CatalogHash;
        public IReadOnlyList<EquipmentSlotState> Slots => m_Slots;
        public IReadOnlyList<EquipmentLocalStateValue> LocalStates => m_LocalStates;
        public PendingEquipmentChange PendingChange { get; private set; }
        public PendingEquipmentChange LastResolvedChange { get; private set; }

        internal void CopyFrom(EquipmentStateAggregate source)
        {
            if (source == null)
            {
                Array.Clear(m_Slots, 0, m_Slots.Length);
                for (int index = 0; index < m_LocalStates.Length; index++)
                    m_LocalStates[index].Value.CopyFrom(m_Bank.Layout.LocalStates[index].DefaultValue);
                PendingChange = default;
                LastResolvedChange = default;
                return;
            }
            Array.Copy(source.m_Slots, m_Slots, m_Slots.Length);
            for (int index = 0; index < m_LocalStates.Length; index++)
                m_LocalStates[index].Value.CopyFrom(source.m_LocalStates[index].Value);
            PendingChange = source.PendingChange;
            LastResolvedChange = source.LastResolvedChange;
        }

        internal void Retain()
        {
            if (References++ != 0)
                return;
            for (int slot = 0; slot < m_Slots.Length; slot++)
            {
                m_Slots[slot].Handles.Retain();
                m_Slots[slot].SlotId.Retain();
                m_Slots[slot].TagSource.Retain();
            }
            for (int index = 0; index < m_LocalStates.Length; index++)
                m_LocalStates[index].Value.Identity.Retain();
            PendingChange.SlotId.Retain();
            LastResolvedChange.SlotId.Retain();
        }

        internal void Release()
        {
            if (--References != 0)
                return;
            for (int slot = 0; slot < m_Slots.Length; slot++)
            {
                m_Slots[slot].Handles.Release();
                m_Slots[slot].SlotId.Release();
                m_Slots[slot].TagSource.Release();
            }
            for (int index = 0; index < m_LocalStates.Length; index++)
                m_LocalStates[index].Value.Identity.Release();
            PendingChange.SlotId.Release();
            LastResolvedChange.SlotId.Release();
        }

        internal void SetSlot(int index, EquipmentSlotState slot)
        {
            if (References != 0)
            {
                slot.Handles.Retain();
                slot.SlotId.Retain();
                slot.TagSource.Retain();
                m_Slots[index].Handles.Release();
                m_Slots[index].SlotId.Release();
                m_Slots[index].TagSource.Release();
            }
            m_Slots[index] = slot;
        }

        internal void RestoreLocalState(int index, bool boolean, int int32, ulong uint64,
            double x, double y, double z, CanonicalTextValue identity)
        {
            if (References != 0)
            {
                identity.Retain();
                m_LocalStates[index].Value.Identity.Release();
            }
            m_LocalStates[index].Value.Restore(boolean, int32, uint64, x, y, z, identity);
        }

        internal void RestoreChanges(PendingEquipmentChange pending, PendingEquipmentChange resolved)
        {
            if (References != 0)
            {
                pending.SlotId.Retain();
                resolved.SlotId.Retain();
                PendingChange.SlotId.Release();
                LastResolvedChange.SlotId.Release();
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
            next.m_LocalStates[index].Value.CopyFrom(value);
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

    public static class EquipmentStateAggregateCodec
    {
        public static void Write(CanonicalWriter writer, EquipmentStateAggregate state)
        {
            if (writer == null || state == null)
                throw new ArgumentNullException();
            writer.WriteHash(state.CatalogHash);
            writer.WriteInt32(3);
            writer.WriteInt32(state.Slots.Count);
            for (int i = 0; i < state.Slots.Count; i++)
            {
                EquipmentSlotState slot = state.Slots[i];
                writer.WriteText(slot.SlotId.Value);
                writer.WriteText(slot.EquipmentId.Value);
                writer.WriteText(slot.FeatureId.Value);
                writer.WriteUInt64(slot.FeatureRevision.Value);
                writer.WriteText(slot.VisualBindingId.Value);
                writer.WriteUInt64(slot.Revision);
                writer.WriteUInt64(slot.Generation);
                writer.WriteBoolean(slot.ContributionsInstalled);
                slot.TagSource.Write(writer);
                writer.WriteInt32(slot.PassiveEffectHandles.Count);
                for (int handle = 0; handle < slot.PassiveEffectHandles.Count; handle++)
                    writer.WriteUInt64(slot.PassiveEffectHandles[handle]);
            }
            writer.WriteInt32(state.LocalStates.Count);
            for (int i = 0; i < state.LocalStates.Count; i++)
            {
                EquipmentLocalStateValue localState = state.LocalStates[i];
                writer.WriteText(localState.FeatureId.Value);
                writer.WriteText(localState.StateId.Value);
                writer.WriteByte((byte)localState.Value.Kind);
                WriteStateValue(writer, localState.Value);
            }
            WriteChange(writer, state.PendingChange);
            WriteChange(writer, state.LastResolvedChange);
        }

        static void WriteChange(CanonicalWriter writer, PendingEquipmentChange pending)
        {
            writer.WriteBoolean(pending.IsValid);
            if (!pending.IsValid)
                return;
            writer.WriteUInt64(pending.ChangeId.Value);
            writer.WriteText(pending.SlotId.Value);
            writer.WriteText(pending.FromEquipmentId.Value);
            writer.WriteText(pending.ToEquipmentId.Value);
            writer.WriteUInt64(pending.SourceActionInstanceId);
            writer.WriteUInt64(pending.BeginTick);
            writer.WriteUInt64(pending.ResolvedTick);
            writer.WriteByte((byte)pending.State);
        }

        public static EquipmentStateAggregate Read(ArraySegment<byte> bytes, EquipmentStatePageBank storage,
            ICanonicalTextResolver resolver = null)
        {
            CanonicalReader reader = storage.DecodeReader;
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            EquipmentProgramLayout layout = storage.Layout;
            try
            {
                EquipmentStateAggregate state = storage.BeginDecode();
                StableHash hash = reader.ReadHash();
                if (reader.ReadInt32() != 3)
                    throw new InvalidDataException("Equipment state schema version is unsupported.");
                if (!hash.Equals(layout.CatalogHash))
                    throw new InvalidDataException("Equipment state catalog identity does not match Program layout.");
                int count = reader.ReadInt32();
                if (count != layout.Slots.Count)
                    throw new InvalidDataException("Equipment state Slot count does not match Program layout.");
                for (int i = 0; i < count; i++)
                {
                    var slotId = new EquipmentSlotId(storage.ReadIdentity(reader));
                    int slotIndex = storage.ReadSlotIndex(slotId);
                    var equipmentId = ReadOptionalEquipment(storage.ReadIdentity(reader));
                    var featureId = ReadOptionalFeature(storage.ReadIdentity(reader));
                    ulong featureRevisionValue = reader.ReadUInt64();
                    var featureRevision = featureRevisionValue == 0 ? default : new EquipmentFeatureRevision(featureRevisionValue);
                    var visualBindingId = ReadOptionalVisual(storage.ReadIdentity(reader));
                    ulong revision = reader.ReadUInt64();
                    ulong generation = reader.ReadUInt64();
                    bool installed = reader.ReadBoolean();
                    GameplayTagSourceIdentityValue tagSource = GameplayTagSourceIdentityValue.Read(reader);
                    int handleCount = reader.ReadInt32();
                    int expectedHandleCount = 0;
                    if (equipmentId.IsValid)
                    {
                        EquipmentProgramItem item = layout.RequireItem(equipmentId);
                        EquipmentProgramFeature feature = layout.RequireFeature(featureId);
                        if (item.SlotId != slotId || item.FeatureId != featureId || item.VisualBindingId != visualBindingId || feature.Revision != featureRevision)
                            throw new InvalidDataException($"Equipment state Slot '{slotId}' contribution does not match Program catalog.");
                        if (installed)
                            expectedHandleCount = feature.PassiveEffects.Count;
                    }
                    if (handleCount != expectedHandleCount)
                        throw new InvalidDataException("Equipment passive Effect handle count does not match the installed Feature.");
                    EquipmentPassiveHandles handles = storage.AcquirePassiveHandles(handleCount);
                    try
                    {
                        for (int handle = 0; handle < handles.Count; handle++)
                            handles[handle] = reader.ReadUInt64();
                        state.SetSlot(slotIndex, new EquipmentSlotState(slotId, equipmentId, featureId,
                            featureRevision, visualBindingId, revision, generation, installed, tagSource, handles));
                    }
                    finally
                    {
                        storage.ReleasePassiveHandles(in handles);
                    }
                }
                int localStateCount = reader.ReadInt32();
                if (localStateCount != layout.LocalStates.Count)
                    throw new InvalidDataException("Equipment state local state count does not match the runtime layout.");
                for (int i = 0; i < localStateCount; i++)
                {
                    var featureId = new EquipmentFeatureId(storage.ReadIdentity(reader));
                    var stateId = new EquipmentLocalStateId(storage.ReadIdentity(reader));
                    int localStateIndex = storage.ReadLocalStateIndex(featureId, stateId);
                    EquipmentRuntimeStateValueKind valueKind = ReadStateValueKind(reader.ReadByte());
                    if (layout.LocalStates[localStateIndex].ValueKind != valueKind)
                        throw new InvalidDataException($"Equipment local state '{featureId}/{stateId}' value kind does not match the runtime layout.");
                    state.RestoreLocalState(localStateIndex, reader.ReadBoolean(), reader.ReadInt32(),
                        reader.ReadUInt64(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadText());
                }
                PendingEquipmentChange pending = ReadChange(reader, storage);
                PendingEquipmentChange resolved = ReadChange(reader, storage);
                if (pending.IsValid && !pending.IsPending || resolved.IsValid && resolved.IsPending)
                    throw new InvalidDataException("Equipment pending and resolved records have inconsistent lifecycle states.");
                state.RestoreChanges(pending, resolved);
                reader.RequireComplete();
                return state;
            }
            catch
            {
                storage.ReleaseDecodedState();
                throw;
            }
        }

        static void WriteStateValue(CanonicalWriter writer, EquipmentRuntimeStateValue value)
        {
            writer.WriteBoolean(value.Boolean);
            writer.WriteInt32(value.Int32);
            writer.WriteUInt64(value.UInt64);
            writer.WriteDouble(value.X);
            writer.WriteDouble(value.Y);
            writer.WriteDouble(value.Z);
            writer.WriteText(value.Identity);
        }

        static PendingEquipmentChange ReadChange(CanonicalReader reader, EquipmentStatePageBank storage)
        {
            if (!reader.ReadBoolean())
                return default;
            var change = new PendingEquipmentChange(
                new EquipmentChangeId(reader.ReadUInt64()),
                new EquipmentSlotId(storage.ReadIdentity(reader)),
                ReadOptionalEquipment(storage.ReadIdentity(reader)),
                ReadOptionalEquipment(storage.ReadIdentity(reader)),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                ReadChangeState(reader.ReadByte()));
            EquipmentProgramLayout layout = storage.Layout;
            layout.RequireSlot(change.SlotId);
            if (change.FromEquipmentId.IsValid) layout.RequireItem(change.FromEquipmentId);
            if (change.ToEquipmentId.IsValid) layout.RequireItem(change.ToEquipmentId);
            return change;
        }

        static EquipmentId ReadOptionalEquipment(string value) => string.IsNullOrEmpty(value) ? default : new EquipmentId(value);
        static EquipmentFeatureId ReadOptionalFeature(string value) => string.IsNullOrEmpty(value) ? default : new EquipmentFeatureId(value);
        static EquipmentVisualBindingId ReadOptionalVisual(string value) => string.IsNullOrEmpty(value) ? default : new EquipmentVisualBindingId(value);
        static EquipmentRuntimeStateValueKind ReadStateValueKind(byte value)
        {
            var result = (EquipmentRuntimeStateValueKind)value;
            return EquipmentRuntimeStateValue.IsValidKind(result) ? result :
                throw new InvalidDataException($"Equipment state enum '{nameof(EquipmentRuntimeStateValueKind)}' value '{value}' is invalid.");
        }

        static PendingEquipmentChangeState ReadChangeState(byte value)
        {
            var result = (PendingEquipmentChangeState)value;
            return PendingEquipmentChange.IsValidState(result)
                ? result
                : throw new InvalidDataException($"Equipment state enum '{nameof(PendingEquipmentChangeState)}' value '{value}' is invalid.");
        }
    }

    public static class EquipmentActionContextCodec
    {
        public static void Write(CanonicalWriter writer, EquipmentActionContext context)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.WriteBoolean(context.IsValid);
            if (!context.IsValid)
                return;
            writer.WriteText(context.SlotId.Value);
            writer.WriteText(context.EquipmentId.Value);
            writer.WriteText(context.FeatureId.Value);
            writer.WriteUInt64(context.EquipmentRevision);
            writer.WriteText(context.RouteId.Value);
        }

        public static EquipmentActionContext Read(CanonicalReader reader, EquipmentProgramLayout layout)
        {
            if (reader == null || layout == null)
                throw new ArgumentNullException();
            if (!reader.ReadBoolean())
                return default;
            ArraySegment<byte> slotIdentity = reader.ReadTextSegment();
            ArraySegment<byte> equipmentIdentity = reader.ReadTextSegment();
            ArraySegment<byte> featureIdentity = reader.ReadTextSegment();
            ulong revision = reader.ReadUInt64();
            ArraySegment<byte> routeIdentity = reader.ReadTextSegment();
            EquipmentProgramItem item = null;
            for (int index = 0; index < layout.Items.Count; index++)
            {
                EquipmentProgramItem candidate = layout.Items[index];
                if (CanonicalTextEncoding.EqualsEncoded(equipmentIdentity.AsSpan(), candidate.EquipmentId.Value.AsSpan()))
                {
                    item = candidate;
                    break;
                }
            }
            if (item == null || !CanonicalTextEncoding.EqualsEncoded(slotIdentity.AsSpan(), item.SlotId.Value.Span) ||
                !CanonicalTextEncoding.EqualsEncoded(featureIdentity.AsSpan(), item.FeatureId.Value.AsSpan()))
                throw new InvalidDataException("Equipment Action Context has no matching declared item and Slot.");
            EquipmentProgramRoute route = null;
            for (int index = 0; index < layout.Routes.Count; index++)
            {
                EquipmentProgramRoute candidate = layout.Routes[index];
                if (CanonicalTextEncoding.EqualsEncoded(routeIdentity.AsSpan(), candidate.RouteId.Value.AsSpan()))
                {
                    route = candidate;
                    break;
                }
            }
            if (route == null || route.OwnerSlotId != item.SlotId)
                throw new InvalidDataException("Equipment Action Context has no matching declared route.");
            var context = new EquipmentActionContext(item.SlotId, item.EquipmentId, item.FeatureId, revision, route.RouteId);
            if (!layout.TryGetRouteImplementation(context.FeatureId, context.RouteId, out _))
                throw new InvalidDataException($"Equipment Action Context '{context}' has no Feature route implementation.");
            return context;
        }
    }
}
