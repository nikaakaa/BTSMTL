using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    public static class EquipmentProgramLayoutCompiler
    {
        public static EquipmentProgramLayout Compile(
            CharacterEquipmentRuntimeBinding binding,
            IReadOnlyList<ProgramCatalogEntry> catalog,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramProducer> producers)
        {
            EquipmentProgramLayout layout = binding.Layout.WithOperationBindings(CompileOperationBindings(catalog, references));
            layout.ValidateProducerBindings(producers);
            return layout;
        }

        static IReadOnlyList<EquipmentProgramOperationBinding> CompileOperationBindings(
            IReadOnlyList<ProgramCatalogEntry> catalog,
            IReadOnlyList<ProgramReference> references)
        {
            Dictionary<int, ProgramCatalogEntry> entries = catalog.ToDictionary(value => value.Index);
            var slots = new Dictionary<int, EquipmentSlotId>();
            var routes = new Dictionary<int, EquipmentActionRouteId>();
            var equipment = new Dictionary<int, EquipmentId>();
            var parameters = new Dictionary<int, EquipmentParameterId>();
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (!reference.HasSourceOperation || reference.Kind != ProgramReferenceKind.CatalogEntry)
                    continue;
                if (!entries.TryGetValue(reference.TargetIndex, out ProgramCatalogEntry entry))
                    throw new InvalidDataException($"Equipment operation reference '{reference.Identity}' targets an unknown catalog entry.");
                int operation = reference.SourceOperation.Value;
                switch (entry.Kind)
                {
                    case ProgramCatalogEntryKind.EquipmentSlot:
                        AddUnique(slots, operation, new EquipmentSlotId(Trim(entry.Identity, "equipment:slot:")), "Slot");
                        break;
                    case ProgramCatalogEntryKind.EquipmentRoute:
                        AddUnique(routes, operation, new EquipmentActionRouteId(Trim(entry.Identity, "equipment:route:")), "Route");
                        break;
                    case ProgramCatalogEntryKind.EquipmentDefinition:
                        AddUnique(equipment, operation, new EquipmentId(Trim(entry.Identity, "equipment:item:")), "Equipment");
                        break;
                    case ProgramCatalogEntryKind.EquipmentFeatureParameter:
                        ParseParameterSchema(entry.Identity, out _, out EquipmentParameterId parameterId);
                        AddUnique(parameters, operation, parameterId, "Parameter");
                        break;
                }
            }
            int[] operations = slots.Keys.Concat(routes.Keys).Concat(equipment.Keys).Concat(parameters.Keys).Distinct().OrderBy(value => value).ToArray();
            var result = new EquipmentProgramOperationBinding[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                int operation = operations[i];
                slots.TryGetValue(operation, out EquipmentSlotId slotId);
                routes.TryGetValue(operation, out EquipmentActionRouteId routeId);
                equipment.TryGetValue(operation, out EquipmentId equipmentId);
                parameters.TryGetValue(operation, out EquipmentParameterId parameterId);
                result[i] = new EquipmentProgramOperationBinding(new OperationHandle(operation), slotId, routeId, equipmentId, parameterId);
            }
            return result;
        }

        static void AddUnique<T>(Dictionary<int, T> values, int operation, T value, string kind)
        {
            if (!values.TryAdd(operation, value))
                throw new InvalidDataException($"Equipment operation '{operation}' has duplicate {kind} bindings.");
        }

        static void ParseParameterSchema(string identity, out EquipmentFeatureId featureId, out EquipmentParameterId parameterId)
        {
            const string prefix = "equipment:feature:";
            const string marker = ":parameter:";
            int index = identity.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
            if (!identity.StartsWith(prefix, StringComparison.Ordinal) || index < 0)
                throw new InvalidDataException($"Equipment Parameter schema identity '{identity}' is invalid.");
            featureId = new EquipmentFeatureId(identity.Substring(prefix.Length, index - prefix.Length));
            parameterId = new EquipmentParameterId(identity.Substring(index + marker.Length));
        }

        static string Trim(string value, string prefix) => value != null && value.StartsWith(prefix, StringComparison.Ordinal)
            ? value.Substring(prefix.Length)
            : throw new InvalidDataException($"Equipment identity '{value}' does not start with '{prefix}'.");
    }
}
