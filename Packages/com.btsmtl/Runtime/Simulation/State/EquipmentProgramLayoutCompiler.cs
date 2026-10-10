using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    public enum EquipmentCatalogConstantKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt64 = 3,
        String = 4,
        Scalar = 5,
        Vector2 = 6,
        Vector3 = 7,
        Yaw = 8
    }

    public readonly struct EquipmentCatalogConstant
    {
        public EquipmentCatalogConstant(
            EquipmentCatalogConstantKind kind,
            bool boolean,
            int int32,
            ulong uint64,
            string text,
            double x = 0d,
            double y = 0d,
            double z = 0d)
        {
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            Text = text ?? string.Empty;
            X = x;
            Y = y;
            Z = z;
        }

        public EquipmentCatalogConstantKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public string Text { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public static class EquipmentProgramLayoutCompiler
    {
        public static EquipmentProgramLayout Compile(
            CharacterEquipmentRuntimeBinding binding,
            IReadOnlyList<ProgramCatalogEntry> catalog,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramProducer> producers)
        {
            if (binding == null || catalog == null || references == null || producers == null)
                throw new ArgumentNullException();
            return Compile(binding, catalog, references, producers, true);
        }

        public static EquipmentProgramLayout CompileRoleStateLayout(
            CharacterEquipmentRuntimeBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            return Compile(
                binding,
                Array.Empty<ProgramCatalogEntry>(),
                Array.Empty<ProgramReference>(),
                Array.Empty<ProgramProducer>(),
                false);
        }

        static EquipmentProgramLayout Compile(
            CharacterEquipmentRuntimeBinding binding,
            IReadOnlyList<ProgramCatalogEntry> catalog,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramProducer> producers,
            bool validateProducerBindings)
        {
            if (binding == null || catalog == null || references == null ||
                validateProducerBindings && producers == null)
                throw new ArgumentNullException();
            var reader = new CanonicalReader(binding.CatalogBytes);
            if (reader.ReadInt32() != CharacterEquipmentRuntimeBinding.CatalogFormatVersion)
                throw new InvalidDataException("Character Equipment runtime catalog format is unsupported.");
            var slots = new List<EquipmentProgramSlot>(ReadCount(reader, "Equipment Slot"));
            for (int i = 0; i < slots.Capacity; i++)
            {
                EquipmentSlotId slotId = new EquipmentSlotId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                EquipmentSlotRequirement requirement = (EquipmentSlotRequirement)reader.ReadByte();
                string equipment = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                slots.Add(new EquipmentProgramSlot(
                    slotId,
                    requirement,
                    string.IsNullOrEmpty(equipment) ? default : new EquipmentId(equipment)));
            }
            var features = new List<EquipmentProgramFeature>(ReadCount(reader, "Equipment Feature"));
            for (int i = 0; i < features.Capacity; i++)
            {
                EquipmentFeatureId featureId = new EquipmentFeatureId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                var revision = new EquipmentFeatureRevision(reader.ReadUInt64());
                string codeBinding = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                string[] tags = ReadStrings(reader, "Equipment granted Tag");
                string[] passiveEffects = ReadStrings(reader, "Equipment passive Effect");
                WorldCapability capabilities = (WorldCapability)reader.ReadUInt64();
                features.Add(new EquipmentProgramFeature(featureId, revision, codeBinding, tags, passiveEffects, capabilities));
            }
            var items = new List<EquipmentProgramItem>(ReadCount(reader, "Equipment Item"));
            for (int i = 0; i < items.Capacity; i++)
            {
                items.Add(new EquipmentProgramItem(
                    new EquipmentId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    new EquipmentSlotId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    new EquipmentFeatureId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    new EquipmentVisualBindingId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()))));
            }
            var routes = new List<EquipmentProgramRoute>(ReadCount(reader, "Equipment Route"));
            for (int i = 0; i < routes.Capacity; i++)
            {
                routes.Add(new EquipmentProgramRoute(
                    new EquipmentActionRouteId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    new EquipmentSlotId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    (EquipmentRouteRequestConsumption)reader.ReadByte(),
                    (EquipmentRouteMissingImplementation)reader.ReadByte()));
            }
            var routeImplementations = new List<EquipmentProgramRouteImplementation>(ReadCount(reader, "Equipment Route implementation"));
            for (int i = 0; i < routeImplementations.Capacity; i++)
            {
                EquipmentFeatureId featureId = new EquipmentFeatureId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                EquipmentActionRouteId routeId = new EquipmentActionRouteId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                CharacterSkillId abilityId = new CharacterSkillId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                string[] parameters = ReadStrings(reader, "Equipment required Parameter");
                string[] requiredProducers = ReadStrings(reader, "Equipment required Producer");
                routeImplementations.Add(new EquipmentProgramRouteImplementation(
                    featureId,
                    routeId,
                    abilityId,
                    parameters.Select(value => new EquipmentParameterId(value)),
                    requiredProducers));
            }
            var parametersByValue = new List<EquipmentProgramParameter>(ReadCount(reader, "Equipment Parameter"));
            for (int i = 0; i < parametersByValue.Capacity; i++)
            {
                EquipmentId equipmentId = new EquipmentId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                EquipmentFeatureId featureId = new EquipmentFeatureId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                EquipmentParameterId parameterId = new EquipmentParameterId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                EquipmentParameterValueKind valueKind = (EquipmentParameterValueKind)reader.ReadByte();
                EquipmentRuntimeParameterValue value = ReadParameterValue(reader, valueKind);
                parametersByValue.Add(new EquipmentProgramParameter(equipmentId, featureId, parameterId, valueKind, value));
            }
            var localStates = new List<EquipmentProgramLocalState>(ReadCount(reader, "Equipment local state"));
            for (int i = 0; i < localStates.Capacity; i++)
            {
                EquipmentFeatureId featureId = new EquipmentFeatureId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                EquipmentLocalStateId stateId = new EquipmentLocalStateId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                EquipmentRuntimeStateValueKind valueKind = ReadEnum<EquipmentRuntimeStateValueKind>(reader.ReadByte());
                localStates.Add(new EquipmentProgramLocalState(featureId, stateId, valueKind, ReadStateValue(reader, valueKind)));
            }
            reader.RequireComplete();
            IReadOnlyList<EquipmentProgramOperationBinding> operationBindings = CompileOperationBindings(catalog, references);
            var layout = new EquipmentProgramLayout(true, slots, features, items, routes, routeImplementations, parametersByValue, localStates, operationBindings);
            if (validateProducerBindings)
                layout.ValidateProducerBindings(producers);
            return layout;
        }

        public static EquipmentProgramLayout Compile(
            bool capabilityEnabled,
            IReadOnlyList<ProgramCatalogEntry> catalog,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramProducer> producers,
            Func<int, EquipmentCatalogConstant> constant)
        {
            if (!capabilityEnabled)
                return new EquipmentProgramLayout(false, null, null, null, null, null, null, null, null);
            if (catalog == null || references == null || producers == null || constant == null)
                throw new ArgumentNullException();
            var initial = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentInitialLoadout))
            {
                string slot = Trim(Identity(entry, "Slot"), "equipment:slot:");
                bool hasEquipment = Boolean(entry, "HasEquipment", constant);
                string equipment = hasEquipment ? Trim(Identity(entry, "Equipment"), "equipment:item:") : string.Empty;
                if (!initial.TryAdd(slot, equipment))
                    throw new InvalidDataException($"Equipment Initial Loadout Slot '{slot}' is duplicated.");
            }
            var slots = new List<EquipmentProgramSlot>();
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentSlot))
            {
                string slot = Trim(entry.Identity, "equipment:slot:");
                if (!initial.TryGetValue(slot, out string equipment))
                    throw new InvalidDataException($"Equipment Slot '{slot}' has no Initial Loadout entry.");
                slots.Add(new EquipmentProgramSlot(
                    new EquipmentSlotId(slot),
                    (EquipmentSlotRequirement)Int32(entry, "Requirement", constant),
                    OptionalEquipment(equipment)));
            }
            var features = new List<EquipmentProgramFeature>();
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentFeature))
            {
                features.Add(new EquipmentProgramFeature(
                    new EquipmentFeatureId(Trim(entry.Identity, "equipment:feature:")),
                    new EquipmentFeatureRevision(UInt64(entry, "FeatureRevision", constant)),
                    Trim(Identity(entry, "CodeBinding"), "equipment:code:"),
                    Identities(entry, "GrantedTag:").Select(value => Trim(value, "tag:")),
                    Identities(entry, "PassiveEffect:").Select(value => Trim(value, "effect:")),
                    (WorldCapability)UInt64(entry, "RequiredWorldCapabilities", constant)));
            }
            var items = new List<EquipmentProgramItem>();
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentDefinition))
            {
                items.Add(new EquipmentProgramItem(
                    new EquipmentId(Trim(entry.Identity, "equipment:item:")),
                    new EquipmentSlotId(Trim(Identity(entry, "Slot"), "equipment:slot:")),
                    new EquipmentFeatureId(Trim(Identity(entry, "Feature"), "equipment:feature:")),
                    new EquipmentVisualBindingId(Trim(Identity(entry, "VisualBinding"), "equipment:visual:"))));
            }
            var routes = new List<EquipmentProgramRoute>();
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentRoute))
            {
                routes.Add(new EquipmentProgramRoute(
                    new EquipmentActionRouteId(Trim(entry.Identity, "equipment:route:")),
                    new EquipmentSlotId(Trim(Identity(entry, "OwnerSlot"), "equipment:slot:")),
                    Trim(Identity(entry, "InputRequest"), "input:request:"),
                    (EquipmentRouteRequestConsumption)Int32(entry, "RequestConsumption", constant),
                    (EquipmentRouteMissingImplementation)Int32(entry, "MissingImplementation", constant)));
            }
            var routeImplementations = new List<EquipmentProgramRouteImplementation>();
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentRouteImplementation))
            {
                EquipmentFeatureId featureId = new EquipmentFeatureId(Trim(Identity(entry, "Feature"), "equipment:feature:"));
                EquipmentActionRouteId routeId = new EquipmentActionRouteId(Trim(Identity(entry, "Route"), "equipment:route:"));
                CharacterSkillId abilityId = new CharacterSkillId(Trim(Identity(entry, "Ability"), "ability:"));
                if (!catalog.Any(value => value.Kind == ProgramCatalogEntryKind.AbilityProgram && string.Equals(value.Identity, $"ability:{abilityId.Value}", StringComparison.Ordinal)))
                    throw new InvalidDataException($"Equipment Route implementation '{featureId}/{routeId}' references an unknown Ability '{abilityId}'.");
                var requiredParameters = new List<EquipmentParameterId>();
                foreach (string identity in Identities(entry, "RequiredParameter:"))
                {
                    ParseParameterSchema(identity, out EquipmentFeatureId parameterFeatureId, out EquipmentParameterId parameterId);
                    if (parameterFeatureId != featureId)
                        throw new InvalidDataException($"Equipment Route implementation '{featureId}/{routeId}' references Parameter '{identity}' from another Feature.");
                    requiredParameters.Add(parameterId);
                }
                IReadOnlyList<string> requiredProducers = Identities(entry, "RequiredProducer:").ToArray();
                routeImplementations.Add(new EquipmentProgramRouteImplementation(
                    featureId,
                    routeId,
                    abilityId,
                    requiredParameters,
                    requiredProducers));
            }
            var parameters = new List<EquipmentProgramParameter>();
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentParameterValue))
            {
                string equipmentId = Trim(Identity(entry, "Equipment"), "equipment:item:");
                string schema = Identity(entry, "Schema");
                ParseParameterSchema(schema, out EquipmentFeatureId featureId, out EquipmentParameterId parameterId);
                parameters.Add(new EquipmentProgramParameter(
                    new EquipmentId(equipmentId),
                    featureId,
                    parameterId,
                    (EquipmentParameterValueKind)Int32(entry, "ValueKind", constant),
                    ToRuntimeValue(
                        (EquipmentParameterValueKind)Int32(entry, "ValueKind", constant),
                        constant(Field(entry, "Value", ProgramCatalogFieldKind.Constant).ConstantIndex))));
            }
            var localStates = new List<EquipmentProgramLocalState>();
            foreach (ProgramCatalogEntry entry in catalog.Where(value => value.Kind == ProgramCatalogEntryKind.EquipmentFeatureLocalState))
            {
                ParseLocalState(entry.Identity, out EquipmentFeatureId featureId, out EquipmentLocalStateId stateId);
                ProgramStateValueKind programKind = (ProgramStateValueKind)Int32(entry, "ValueKind", constant);
                EquipmentRuntimeStateValueKind valueKind = ToRuntimeStateValueKind(programKind);
                localStates.Add(new EquipmentProgramLocalState(
                    featureId,
                    stateId,
                    valueKind,
                    ToRuntimeStateValue(
                        valueKind,
                        constant(Field(entry, "DefaultValue", ProgramCatalogFieldKind.Constant).ConstantIndex))));
            }
            IReadOnlyList<EquipmentProgramOperationBinding> operationBindings = CompileOperationBindings(catalog, references);
            var layout = new EquipmentProgramLayout(true, slots, features, items, routes, routeImplementations, parameters, localStates, operationBindings);
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

        static EquipmentRuntimeParameterValue ToRuntimeValue(
            EquipmentParameterValueKind kind,
            EquipmentCatalogConstant value)
        {
            return kind switch
            {
                EquipmentParameterValueKind.Boolean when value.Kind == EquipmentCatalogConstantKind.Boolean =>
                    new EquipmentRuntimeParameterValue(kind, value.Boolean, 0, 0, 0, 0, 0, string.Empty),
                EquipmentParameterValueKind.Int32 when value.Kind == EquipmentCatalogConstantKind.Int32 =>
                    new EquipmentRuntimeParameterValue(kind, false, value.Int32, 0, 0, 0, 0, string.Empty),
                EquipmentParameterValueKind.Scalar when value.Kind == EquipmentCatalogConstantKind.Scalar =>
                    new EquipmentRuntimeParameterValue(kind, false, 0, 0, value.X, 0, 0, string.Empty),
                EquipmentParameterValueKind.Vector2 when value.Kind == EquipmentCatalogConstantKind.Vector2 =>
                    new EquipmentRuntimeParameterValue(kind, false, 0, 0, value.X, value.Y, 0, string.Empty),
                EquipmentParameterValueKind.Vector3 when value.Kind == EquipmentCatalogConstantKind.Vector3 =>
                    new EquipmentRuntimeParameterValue(kind, false, 0, 0, value.X, value.Y, value.Z, string.Empty),
                EquipmentParameterValueKind.Yaw when value.Kind == EquipmentCatalogConstantKind.Yaw =>
                    new EquipmentRuntimeParameterValue(kind, false, 0, 0, value.X, 0, 0, string.Empty),
                EquipmentParameterValueKind.GameplayTag when value.Kind == EquipmentCatalogConstantKind.String =>
                    new EquipmentRuntimeParameterValue(kind, false, 0, 0, 0, 0, 0, value.Text),
                EquipmentParameterValueKind.GameplayEffect when value.Kind == EquipmentCatalogConstantKind.String =>
                    new EquipmentRuntimeParameterValue(kind, false, 0, 0, 0, 0, 0, value.Text),
                EquipmentParameterValueKind.AnimationProducer when value.Kind == EquipmentCatalogConstantKind.String =>
                    new EquipmentRuntimeParameterValue(kind, false, 0, 0, 0, 0, 0, value.Text),
                _ => throw new InvalidDataException($"Equipment parameter '{kind}' value kind '{value.Kind}' is invalid.")
            };
        }

        static EquipmentRuntimeParameterValue ReadParameterValue(
            CanonicalReader reader,
            EquipmentParameterValueKind kind)
        {
            return new EquipmentRuntimeParameterValue(
                kind,
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadUInt64(),
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadDouble(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
        }

        static EquipmentRuntimeStateValue ReadStateValue(
            CanonicalReader reader,
            EquipmentRuntimeStateValueKind kind)
        {
            return new EquipmentRuntimeStateValue(
                kind,
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadUInt64(),
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadDouble(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
        }

        static EquipmentRuntimeStateValueKind ToRuntimeStateValueKind(ProgramStateValueKind kind) => kind switch
        {
            ProgramStateValueKind.Boolean => EquipmentRuntimeStateValueKind.Boolean,
            ProgramStateValueKind.Int32 => EquipmentRuntimeStateValueKind.Int32,
            ProgramStateValueKind.UInt64 => EquipmentRuntimeStateValueKind.UInt64,
            ProgramStateValueKind.Scalar => EquipmentRuntimeStateValueKind.Scalar,
            ProgramStateValueKind.Vector2 => EquipmentRuntimeStateValueKind.Vector2,
            ProgramStateValueKind.Vector3 => EquipmentRuntimeStateValueKind.Vector3,
            ProgramStateValueKind.Yaw => EquipmentRuntimeStateValueKind.Yaw,
            ProgramStateValueKind.Identity => EquipmentRuntimeStateValueKind.Identity,
            _ => throw new InvalidDataException($"Equipment local state kind '{kind}' is unsupported.")
        };

        static EquipmentRuntimeStateValue ToRuntimeStateValue(
            EquipmentRuntimeStateValueKind kind,
            EquipmentCatalogConstant value)
        {
            return kind switch
            {
                EquipmentRuntimeStateValueKind.Boolean when value.Kind == EquipmentCatalogConstantKind.Boolean =>
                    new EquipmentRuntimeStateValue(kind, value.Boolean, 0, 0, 0, 0, 0, string.Empty),
                EquipmentRuntimeStateValueKind.Int32 when value.Kind == EquipmentCatalogConstantKind.Int32 =>
                    new EquipmentRuntimeStateValue(kind, false, value.Int32, 0, 0, 0, 0, string.Empty),
                EquipmentRuntimeStateValueKind.UInt64 when value.Kind == EquipmentCatalogConstantKind.UInt64 =>
                    new EquipmentRuntimeStateValue(kind, false, 0, value.UInt64, 0, 0, 0, string.Empty),
                EquipmentRuntimeStateValueKind.Scalar when value.Kind == EquipmentCatalogConstantKind.Scalar =>
                    new EquipmentRuntimeStateValue(kind, false, 0, 0, value.X, 0, 0, string.Empty),
                EquipmentRuntimeStateValueKind.Vector2 when value.Kind == EquipmentCatalogConstantKind.Vector2 =>
                    new EquipmentRuntimeStateValue(kind, false, 0, 0, value.X, value.Y, 0, string.Empty),
                EquipmentRuntimeStateValueKind.Vector3 when value.Kind == EquipmentCatalogConstantKind.Vector3 =>
                    new EquipmentRuntimeStateValue(kind, false, 0, 0, value.X, value.Y, value.Z, string.Empty),
                EquipmentRuntimeStateValueKind.Yaw when value.Kind == EquipmentCatalogConstantKind.Yaw =>
                    new EquipmentRuntimeStateValue(kind, false, 0, 0, value.X, 0, 0, string.Empty),
                EquipmentRuntimeStateValueKind.Identity when value.Kind == EquipmentCatalogConstantKind.String =>
                    new EquipmentRuntimeStateValue(kind, false, 0, 0, 0, 0, 0, value.Text),
                _ => throw new InvalidDataException($"Equipment local state '{kind}' default kind '{value.Kind}' is invalid.")
            };
        }

        static T ReadEnum<T>(byte value) where T : struct, Enum
        {
            T result = (T)Enum.ToObject(typeof(T), value);
            return Enum.IsDefined(typeof(T), result)
                ? result
                : throw new InvalidDataException($"Equipment state enum '{typeof(T).Name}' value '{value}' is invalid.");
        }

        static string[] ReadStrings(CanonicalReader reader, string label)
        {
            int count = ReadCount(reader, label);
            var values = new string[count];
            for (int i = 0; i < values.Length; i++)
                values[i] = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            return values;
        }

        static int ReadCount(CanonicalReader reader, string label)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000)
                throw new InvalidDataException($"{label} count '{count}' is invalid.");
            return count;
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

        static void ParseLocalState(string identity, out EquipmentFeatureId featureId, out EquipmentLocalStateId stateId)
        {
            const string prefix = "equipment:feature:";
            const string marker = ":state:";
            int index = identity.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
            if (!identity.StartsWith(prefix, StringComparison.Ordinal) || index < 0)
                throw new InvalidDataException($"Equipment local state identity '{identity}' is invalid.");
            featureId = new EquipmentFeatureId(identity.Substring(prefix.Length, index - prefix.Length));
            stateId = new EquipmentLocalStateId(identity.Substring(index + marker.Length));
        }

        static ProgramCatalogField Field(ProgramCatalogEntry entry, string name, ProgramCatalogFieldKind kind)
        {
            ProgramCatalogField field = entry.Fields.SingleOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));
            if (field == null || field.Kind != kind)
                throw new InvalidDataException($"Equipment catalog '{entry.Identity}' field '{name}' is missing or has the wrong kind.");
            return field;
        }

        static string Identity(ProgramCatalogEntry entry, string name) => Field(entry, name, ProgramCatalogFieldKind.Identity).Identity;
        static IEnumerable<string> Identities(ProgramCatalogEntry entry, string prefix) =>
            entry.Fields.Where(value => value.Kind == ProgramCatalogFieldKind.Identity && value.Name.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(value => value.Name, StringComparer.Ordinal).Select(value => value.Identity);
        static bool Boolean(ProgramCatalogEntry entry, string name, Func<int, EquipmentCatalogConstant> constant)
        {
            EquipmentCatalogConstant value = constant(Field(entry, name, ProgramCatalogFieldKind.Constant).ConstantIndex);
            return value.Kind == EquipmentCatalogConstantKind.Boolean ? value.Boolean : throw new InvalidDataException($"Equipment catalog '{entry.Identity}' field '{name}' is not Boolean.");
        }
        static int Int32(ProgramCatalogEntry entry, string name, Func<int, EquipmentCatalogConstant> constant)
        {
            EquipmentCatalogConstant value = constant(Field(entry, name, ProgramCatalogFieldKind.Constant).ConstantIndex);
            return value.Kind == EquipmentCatalogConstantKind.Int32 ? value.Int32 : throw new InvalidDataException($"Equipment catalog '{entry.Identity}' field '{name}' is not Int32.");
        }
        static ulong UInt64(ProgramCatalogEntry entry, string name, Func<int, EquipmentCatalogConstant> constant)
        {
            EquipmentCatalogConstant value = constant(Field(entry, name, ProgramCatalogFieldKind.Constant).ConstantIndex);
            return value.Kind == EquipmentCatalogConstantKind.UInt64 ? value.UInt64 : throw new InvalidDataException($"Equipment catalog '{entry.Identity}' field '{name}' is not UInt64.");
        }
        static string Trim(string value, string prefix) => value != null && value.StartsWith(prefix, StringComparison.Ordinal)
            ? value.Substring(prefix.Length)
            : throw new InvalidDataException($"Equipment identity '{value}' does not start with '{prefix}'.");
        static EquipmentId OptionalEquipment(string value) => string.IsNullOrEmpty(value) ? default : new EquipmentId(value);
    }
}
