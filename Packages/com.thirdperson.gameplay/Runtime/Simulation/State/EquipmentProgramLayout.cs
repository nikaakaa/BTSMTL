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
            EquipmentProgramParameterValue value)
        {
            if (!equipmentId.IsValid || !featureId.IsValid || !parameterId.IsValid)
            {
                throw new ArgumentException("Equipment Program Parameter is invalid.");
            }
            EquipmentId = equipmentId;
            FeatureId = featureId;
            ParameterId = parameterId;
            Value = value;
        }

        public EquipmentId EquipmentId { get; }
        public EquipmentFeatureId FeatureId { get; }
        public EquipmentParameterId ParameterId { get; }
        public EquipmentProgramParameterValue Value { get; }
    }

    public sealed class EquipmentProgramLocalState
    {
        public EquipmentProgramLocalState(EquipmentFeatureId featureId, EquipmentLocalStateId stateId,
            EquipmentProgramLocalStateDefault defaultValue)
        {
            if (!featureId.IsValid || !stateId.IsValid)
                throw new ArgumentException("Equipment Program local state is invalid.");
            FeatureId = featureId;
            StateId = stateId;
            DefaultValue = defaultValue;
        }

        public EquipmentFeatureId FeatureId { get; }
        public EquipmentLocalStateId StateId { get; }
        public EquipmentProgramLocalStateDefault DefaultValue { get; }
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
        readonly Dictionary<EquipmentSlotId, EquipmentProgramSlot> m_SlotById;
        readonly Dictionary<EquipmentFeatureId, EquipmentProgramFeature> m_FeatureById;
        readonly Dictionary<string, EquipmentProgramFeature> m_FeatureByCodeBinding;
        readonly Dictionary<EquipmentId, EquipmentProgramItem> m_ItemById;
        readonly Dictionary<EquipmentActionRouteId, EquipmentProgramRoute> m_RouteById;
        readonly Dictionary<(EquipmentFeatureId, EquipmentActionRouteId), EquipmentProgramRouteImplementation> m_RouteImplementationByKey;
        readonly Dictionary<(EquipmentId, EquipmentParameterId), EquipmentProgramParameter> m_ParameterByKey;
        readonly Dictionary<(EquipmentFeatureId, EquipmentLocalStateId), EquipmentProgramLocalState> m_LocalStateByKey;

        public EquipmentProgramLayout(
            bool capabilityEnabled,
            IEnumerable<EquipmentProgramSlot> slots,
            IEnumerable<EquipmentProgramFeature> features,
            IEnumerable<EquipmentProgramItem> items,
            IEnumerable<EquipmentProgramRoute> routes,
            IEnumerable<EquipmentProgramRouteImplementation> routeImplementations,
            IEnumerable<EquipmentProgramParameter> parameters,
            IEnumerable<EquipmentProgramLocalState> localStates)
        {
            CapabilityEnabled = capabilityEnabled;
            m_Slots = Canonical(slots, value => value.SlotId.ToString(), "Equipment Slot");
            m_Features = Canonical(features, value => value.FeatureId.Value, "Equipment Feature");
            m_Items = Canonical(items, value => value.EquipmentId.Value, "Equipment Item");
            m_Routes = Canonical(routes, value => value.RouteId.Value, "Equipment Route");
            m_RouteImplementations = Canonical(routeImplementations, value => $"{value.FeatureId.Value}:{value.RouteId.Value}", "Equipment Route implementation");
            m_Parameters = Canonical(parameters, value => $"{value.EquipmentId.Value}:{value.ParameterId.Value}", "Equipment Parameter");
            m_LocalStates = Canonical(localStates, value => $"{value.FeatureId.Value}:{value.StateId.Value}", "Equipment local state");
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
}
