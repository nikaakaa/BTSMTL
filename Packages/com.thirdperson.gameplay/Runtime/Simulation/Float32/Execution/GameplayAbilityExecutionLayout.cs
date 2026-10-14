using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation
{
    public readonly struct TypedStateAddress : IEquatable<TypedStateAddress>
    {
        readonly bool m_IsValid;

        internal TypedStateAddress(
            int slotIndex,
            ProgramStateValueKind valueKind,
            int partitionIndex,
            int pageIndex,
            int offset)
        {
            SlotIndex = slotIndex;
            ValueKind = valueKind;
            PartitionIndex = partitionIndex;
            PageIndex = pageIndex;
            Offset = offset;
            m_IsValid = true;
        }

        public int SlotIndex { get; }
        public ProgramStateValueKind ValueKind { get; }
        public int PartitionIndex { get; }
        public int PageIndex { get; }
        public int Offset { get; }
        public bool IsValid => m_IsValid;

        public bool Equals(TypedStateAddress other) =>
            m_IsValid == other.m_IsValid &&
            SlotIndex == other.SlotIndex &&
            ValueKind == other.ValueKind &&
            PartitionIndex == other.PartitionIndex &&
            PageIndex == other.PageIndex &&
            Offset == other.Offset;

        public override bool Equals(object obj) => obj is TypedStateAddress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_IsValid, SlotIndex, (int)ValueKind, PartitionIndex, PageIndex, Offset);
    }

    public sealed class TypedStatePartitionDescriptor
    {
        const int PageSize = 32;
        readonly IReadOnlyList<int> m_SlotIndexes;

        internal TypedStatePartitionDescriptor(
            int index,
            ProgramStateValueKind valueKind,
            IReadOnlyList<int> slotIndexes)
        {
            Index = index;
            ValueKind = valueKind;
            m_SlotIndexes = slotIndexes ?? throw new ArgumentNullException(nameof(slotIndexes));
        }

        public int Index { get; }
        public ProgramStateValueKind ValueKind { get; }
        public int SlotCount => m_SlotIndexes.Count;
        public int PageCount => (SlotCount + PageSize - 1) / PageSize;
        public IReadOnlyList<int> SlotIndexes => m_SlotIndexes;
    }

    public readonly struct BlackboardInputStateBinding
    {
        public BlackboardInputStateBinding(
            string inputId,
            ProgramInputValueKind inputKind,
            TypedStateAddress stateAddress, ProgramBlackboardInputProjection projection)
        {
            InputId = SimulationIdentity.Require(inputId, nameof(inputId));
            InputKind = inputKind;
            Projection = projection;
            StateAddress = stateAddress.IsValid
                ? stateAddress
                : throw new ArgumentException("Blackboard Input Binding state address is invalid.", nameof(stateAddress));
        }

        public string InputId { get; }
        public ProgramInputValueKind InputKind { get; }
        public ProgramBlackboardInputProjection Projection { get; }
        public TypedStateAddress StateAddress { get; }
    }

    public sealed class GameplayAbilityExecutionLayout
    {
        readonly IReadOnlyList<SimulationOperation> m_Operations;
        readonly IReadOnlyList<ProgramConstant> m_Constants;
        readonly IReadOnlyList<ProgramConstantInputBinding> m_ConstantInputBindings;
        readonly IReadOnlyList<ProgramControlFlowEdge> m_ControlFlow;
        readonly IReadOnlyList<ProgramReference> m_References;
        readonly IReadOnlyList<ProgramGraphCallFrame> m_GraphCallFrames;
        readonly IReadOnlyList<ProgramStateSlot> m_StateSlots;
        readonly IReadOnlyList<ProgramScopeLayout> m_Scopes;
        readonly IReadOnlyList<ProgramCatalogEntry> m_CatalogEntries;
        readonly IReadOnlyList<ProgramProducer> m_Producers;
        readonly int[] m_NamedConstantIndexes;
        readonly int[] m_FirstStateSlots;
        readonly Dictionary<string, int>[] m_StateSlotsByOwner;
        readonly TypedStateAddress[] m_TypedAddresses;
        readonly IReadOnlyList<TypedStatePartitionDescriptor> m_Partitions;
        readonly string[] m_InputRequestIds;
        readonly Dictionary<string, int> m_ActionCapacities;
        readonly int m_ActionInstanceCapacity;
        readonly int[] m_MotionWarpOperationIds;
        readonly int[] m_SkillExecutionStateSlots;
        readonly IReadOnlyList<BlackboardInputStateBinding> m_BlackboardInputBindings;
        readonly BlackboardTargetProjection[] m_ActionTargetSnapshotByOperation;
        readonly string[] m_OperationSourcePaths;
        readonly Dictionary<int, (ProgramExecutionSource Source, int Count)> m_OperationSources;
        readonly TimelineAnimationProducerIndex m_TimelineAnimationProducers;

        public GameplayAbilityExecutionLayout(
            Float32GraphValueLayout valueLayout,
            CharacterSkillId abilityId,
            int tickRate,
            IReadOnlyList<SimulationOperation> operations,
            IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramConstantInputBinding> constantInputBindings,
            IReadOnlyList<ProgramControlFlowEdge> controlFlow,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            IReadOnlyList<ProgramScopeLayout> scopes,
            IReadOnlyList<ProgramCatalogEntry> catalogEntries,
            IReadOnlyList<ProgramSourceMapEntry> sourceMap,
            IReadOnlyList<ProgramProducer> producers,
            ProgramCatalogRuntimeIndex catalogIndex,
            OperationExecutionTopology topology)
        {
            if (!abilityId.IsValid)
                throw new ArgumentException("Ability identity is invalid.", nameof(abilityId));
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            m_Operations = operations ?? throw new ArgumentNullException(nameof(operations));
            m_Constants = constants ?? throw new ArgumentNullException(nameof(constants));
            m_ConstantInputBindings = constantInputBindings ?? throw new ArgumentNullException(nameof(constantInputBindings));
            m_ControlFlow = controlFlow ?? throw new ArgumentNullException(nameof(controlFlow));
            m_References = references ?? throw new ArgumentNullException(nameof(references));
            m_GraphCallFrames = graphCallFrames ?? throw new ArgumentNullException(nameof(graphCallFrames));
            m_StateSlots = stateSlots ?? throw new ArgumentNullException(nameof(stateSlots));
            m_Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            m_CatalogEntries = catalogEntries ?? throw new ArgumentNullException(nameof(catalogEntries));
            m_Producers = producers ?? throw new ArgumentNullException(nameof(producers));
            CatalogIndex = catalogIndex ?? throw new ArgumentNullException(nameof(catalogIndex));
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            AbilityId = abilityId;
            TickRate = tickRate;
            ValueLayout = valueLayout;
            m_NamedConstantIndexes = BuildNamedConstantIndexes(m_Operations, m_Constants);
            BuildGlobalStateSlots(
                m_StateSlots,
                out m_FirstStateSlots,
                out m_StateSlotsByOwner);
            BuildTypedStateLayout(
                m_Constants,
                m_StateSlots,
                out m_TypedAddresses,
                out m_Partitions);
            BuildDomainIndexes(
                m_Operations,
                m_CatalogEntries,
                m_Constants,
                m_StateSlots,
                m_TypedAddresses,
                CatalogIndex,
                out m_InputRequestIds,
                out m_ActionCapacities,
                out m_ActionInstanceCapacity,
                out HashSet<int> motionWarpOperations);
            m_MotionWarpOperationIds = SortedIndexes(motionWarpOperations);
            var operationCodes = new SimulationOperationCode[m_Operations.Count];
            for (int index = 0; index < operationCodes.Length; index++)
                operationCodes[index] = m_Operations[index].Code;
            m_ActionTargetSnapshotByOperation = BlackboardTargetProjection.Build(operationCodes, m_References, m_StateSlots);
            m_BlackboardInputBindings = BuildBlackboardInputBindings(
                m_Constants,
                m_StateSlots,
                m_CatalogEntries,
                CatalogIndex,
                m_TypedAddresses,
                m_Operations.Count);
            m_SkillExecutionStateSlots = BuildSkillExecutionStateSlots(
                m_Operations.Count,
                m_Scopes,
                m_StateSlots,
                Topology);
            m_OperationSourcePaths = BuildOperationSourceIndexes(
                abilityId, m_Operations.Count, sourceMap, out m_OperationSources);
            m_TimelineAnimationProducers = new TimelineAnimationProducerIndex(
                Topology,
                operation => IsTimelineAnimationTrackMuted(operation),
                operation => RequireTimelineAnimationProducerIdentity(operation));
        }

        public Float32GraphValueLayout ValueLayout { get; }
        public CharacterSkillId AbilityId { get; }
        public int TickRate { get; }
        public ProgramCatalogRuntimeIndex CatalogIndex { get; }
        public OperationExecutionTopology Topology { get; }
        public OperationHandle RootOperation => Topology.RootOperation;
        public IReadOnlyList<SimulationOperation> Operations => m_Operations;
        public IReadOnlyList<ProgramConstant> Constants => m_Constants;
        public IReadOnlyList<ProgramStateSlot> StateSlots => m_StateSlots;
        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames => m_GraphCallFrames;
        public IReadOnlyList<TypedStatePartitionDescriptor> StatePartitions => m_Partitions;
        public IReadOnlyList<BlackboardInputStateBinding> BlackboardInputBindings => m_BlackboardInputBindings;
        public IReadOnlyList<string> InputRequestIds => m_InputRequestIds;
        public int ActionInstanceCapacity => m_ActionInstanceCapacity;
        public IReadOnlyList<int> MotionWarpOperationIds => m_MotionWarpOperationIds;

        public SimulationOperation Operation(OperationHandle operation)
        {
            RequireOperation(operation);
            return m_Operations[operation.Value];
        }

        public ProgramConstant FindNamedConstant(OperationHandle operation, OperationNamedConstant field)
        {
            RequireOperation(operation);
            int fieldIndex = (int)field;
            if (fieldIndex < 0 || fieldIndex >= OperationNamedConstantSchema.Count)
                throw new ArgumentOutOfRangeException(nameof(field));
            int constantIndex = m_NamedConstantIndexes[operation.Value * OperationNamedConstantSchema.Count + fieldIndex];
            return constantIndex < 0 ? null : m_Constants[constantIndex];
        }

        public IReadOnlyList<ProgramControlFlowEdge> Outgoing(OperationHandle source, ProgramControlFlowKind kind) =>
            Topology.Outgoing(source, kind);

        public IReadOnlyList<ProgramReference> References(OperationHandle source, ProgramReferenceKind kind) =>
            Topology.References(source, kind);

        public int FindOperationStateSlot(OperationHandle operation, ProgramStateSemantic semantic) =>
            Topology.FindOperationStateSlot(operation, semantic);

        public int FindStateSlot(ProgramStateSemantic semantic, string ownerIdentity = null)
        {
            int semanticIndex = (int)semantic;
            if (semanticIndex < 0 || semanticIndex >= m_FirstStateSlots.Length)
                return -1;
            if (ownerIdentity == null)
                return m_FirstStateSlots[semanticIndex];
            Dictionary<string, int> owners = m_StateSlotsByOwner[semanticIndex];
            return owners != null && owners.TryGetValue(ownerIdentity, out int slot) ? slot : -1;
        }

        public int RequireStateSlot(ProgramStateSemantic semantic, string ownerIdentity = null)
        {
            int slot = FindStateSlot(semantic, ownerIdentity);
            return slot < 0
                ? throw new InvalidOperationException($"Ability '{AbilityId}' has no '{semantic}' state slot for owner '{ownerIdentity ?? "*"}'.")
                : slot;
        }

        public TypedStateAddress Address(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= m_TypedAddresses.Length)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return m_TypedAddresses[slotIndex];
        }

        public bool HasInputRequest(string requestId) =>
            Array.BinarySearch(m_InputRequestIds, requestId ?? string.Empty, StringComparer.Ordinal) >= 0;
        public int ActionCapacity(string actionId) =>
            m_ActionCapacities.TryGetValue(actionId ?? string.Empty, out int capacity)
                ? capacity
                : throw new InvalidOperationException($"Ability '{AbilityId}' has no Action '{actionId}' capacity.");
        public bool TryResolveActionIdentity(CanonicalTextValue value, out string identity)
        {
            foreach (KeyValuePair<string, int> entry in m_ActionCapacities)
                if (value.Equals(entry.Key))
                {
                    identity = entry.Key;
                    return true;
                }
            identity = null;
            return false;
        }
        public bool HasMotionWarp(OperationHandle operation) =>
            operation.IsValid && Array.BinarySearch(m_MotionWarpOperationIds, operation.Value) >= 0;
        public bool IsSkillExecutionStateSlot(int slotIndex) =>
            Array.BinarySearch(m_SkillExecutionStateSlots, slotIndex) >= 0;
        public BlackboardTargetProjection TargetProjection(OperationHandle operation) => m_ActionTargetSnapshotByOperation[operation.Value];

        public IReadOnlyList<OperationHandle> TimelineAnimationRepresentatives(OperationHandle timeline) =>
            m_TimelineAnimationProducers.Representatives(timeline);

        public string SourcePath(OperationHandle operation)
        {
            RequireOperation(operation);
            return m_OperationSourcePaths[operation.Value];
        }

        public (ProgramExecutionSource Source, int Count) OperationSources(OperationHandle operation)
        {
            m_OperationSources.TryGetValue(operation.Value, out var sources);
            return sources;
        }

        public ProgramCatalogEntry RequireCatalog(OperationHandle operation, ProgramCatalogEntryKind kind) =>
            CatalogIndex.RequireEntry(operation, kind);

        public ProgramCatalogEntry FindCatalog(OperationHandle operation, ProgramCatalogEntryKind kind) =>
            CatalogIndex.FindEntry(operation, kind);

        public ProgramCatalogEntry FindCatalog(ProgramCatalogEntryKind kind, string identity) =>
            CatalogIndex.FindEntry(kind, identity);

        public ProgramCatalogField RequireCatalogField(ProgramCatalogEntry entry, ProgramCatalogFieldId field) =>
            CatalogIndex.RequireField(entry, field);

        public bool TryGetCatalogField(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out ProgramCatalogField value) =>
            CatalogIndex.TryGetField(entry, field, out value);

        public bool TryGetCatalogIdentity(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out string identity) =>
            CatalogIndex.TryGetIdentity(entry, field, out identity);

        void RequireOperation(OperationHandle operation) => Topology.RequireOperation(operation);

        bool IsTimelineAnimationTrackMuted(OperationHandle operation)
        {
            ProgramCatalogEntry clip = CatalogIndex.RequireEntry(operation, ProgramCatalogEntryKind.TimelineClip);
            if (!CatalogIndex.TryGetIdentity(clip, ProgramCatalogFieldId.Track, out string trackIdentity))
                throw new InvalidOperationException($"Timeline clip '{clip.Identity}' has no Track identity.");
            ProgramCatalogEntry track = CatalogIndex.FindEntry(ProgramCatalogEntryKind.TimelineTrack, trackIdentity) ??
                throw new InvalidOperationException($"Timeline track '{trackIdentity}' is absent from Ability '{AbilityId}'.");
            ProgramCatalogField muted = CatalogIndex.RequireField(track, ProgramCatalogFieldId.Muted);
            if (muted.Kind != ProgramCatalogFieldKind.Constant ||
                muted.ConstantIndex < 0 || muted.ConstantIndex >= m_Constants.Count ||
                m_Constants[muted.ConstantIndex].Kind != ProgramConstantKind.Boolean)
            {
                throw new InvalidOperationException($"Timeline track '{track.Identity}' Muted field is invalid.");
            }
            return m_Constants[muted.ConstantIndex].Boolean;
        }

        string RequireTimelineAnimationProducerIdentity(OperationHandle operation)
        {
            IReadOnlyList<ProgramReference> references = Topology.References(operation, ProgramReferenceKind.Producer);
            if (references.Count != 1)
                throw new InvalidOperationException($"Timeline animation operation '{operation}' requires exactly one Producer reference.");
            int producerIndex = references[0].TargetIndex;
            if (producerIndex < 0 || producerIndex >= m_Producers.Count)
                throw new InvalidOperationException($"Timeline animation operation '{operation}' Producer reference is outside the Ability data.");
            return m_Producers[producerIndex].Identity;
        }

        static int[] BuildNamedConstantIndexes(
            IReadOnlyList<SimulationOperation> operations,
            IReadOnlyList<ProgramConstant> constants)
        {
            int[] indexes = new int[operations.Count * OperationNamedConstantSchema.Count];
            Array.Fill(indexes, -1);
            for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                SimulationOperation operation = operations[operationIndex];
                for (int i = 0; i < operation.ConstantReferences.Count; i++)
                {
                    int constantIndex = operation.ConstantReferences[i];
                    ProgramConstant constant = constants[constantIndex];
                    if (!OperationNamedConstantSchema.TryParseIdentity(constant.Identity, out OperationNamedConstant field))
                        continue;
                    int index = operationIndex * OperationNamedConstantSchema.Count + (int)field;
                    if (indexes[index] >= 0)
                        throw new InvalidDataException($"Operation '{operation.Handle}' contains duplicate named constant '{field}'.");
                    indexes[index] = constantIndex;
                }
            }
            return indexes;
        }

        static void BuildGlobalStateSlots(
            IReadOnlyList<ProgramStateSlot> stateSlots,
            out int[] firstSlots,
            out Dictionary<string, int>[] slotsByOwner)
        {
            firstSlots = new int[EnumValueCount(typeof(ProgramStateSemantic))];
            Array.Fill(firstSlots, -1);
            slotsByOwner = new Dictionary<string, int>[firstSlots.Length];
            for (int i = 0; i < stateSlots.Count; i++)
            {
                ProgramStateSlot slot = stateSlots[i];
                int semanticIndex = (int)slot.Semantic;
                if (firstSlots[semanticIndex] < 0)
                    firstSlots[semanticIndex] = i;
                Dictionary<string, int> owners = slotsByOwner[semanticIndex] ??=
                    new Dictionary<string, int>(StringComparer.Ordinal);
                if (!owners.ContainsKey(slot.OwnerIdentity))
                    owners.Add(slot.OwnerIdentity, i);
            }
        }

        static void BuildTypedStateLayout(
            IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            out TypedStateAddress[] addresses,
            out IReadOnlyList<TypedStatePartitionDescriptor> partitions)
        {
            var slotsByKind = new SortedDictionary<ProgramStateValueKind, List<int>>();
            for (int i = 0; i < stateSlots.Count; i++)
            {
                ProgramStateSlot slot = stateSlots[i];
                ProgramStateSchema.RequireSlot(slot.ValueKind, slot.OwnerKind, slot.Semantic);
                ValidateDefault(constants, slot);
                if (!slotsByKind.TryGetValue(slot.ValueKind, out List<int> slots))
                {
                    slots = new List<int>();
                    slotsByKind.Add(slot.ValueKind, slots);
                }
                slots.Add(i);
            }
            addresses = new TypedStateAddress[stateSlots.Count];
            var descriptors = new List<TypedStatePartitionDescriptor>(slotsByKind.Count);
            foreach (KeyValuePair<ProgramStateValueKind, List<int>> pair in slotsByKind)
            {
                int partitionIndex = descriptors.Count;
                int[] slots = pair.Value.ToArray();
                descriptors.Add(new TypedStatePartitionDescriptor(partitionIndex, pair.Key, Array.AsReadOnly(slots)));
                for (int offset = 0; offset < slots.Length; offset++)
                {
                    addresses[slots[offset]] = new TypedStateAddress(
                        slots[offset],
                        pair.Key,
                        partitionIndex,
                        offset / 32,
                        offset % 32);
                }
            }
            partitions = descriptors.AsReadOnly();
        }

        static void ValidateDefault(
            IReadOnlyList<ProgramConstant> constants,
            ProgramStateSlot slot)
        {
            if (slot.DefaultConstantIndex < 0)
            {
                if (slot.ValueKind == ProgramStateValueKind.ActionTargetSnapshot)
                    throw new InvalidDataException($"Action target snapshot slot '{slot.Identity}' requires a typed default constant.");
                return;
            }
            if (slot.DefaultConstantIndex >= constants.Count)
                throw new InvalidDataException($"State slot '{slot.Identity}' default constant is outside the Ability data.");
            ProgramConstant constant = constants[slot.DefaultConstantIndex];
            bool valid = slot.ValueKind switch
            {
                ProgramStateValueKind.Boolean => constant.Kind == ProgramConstantKind.Boolean,
                ProgramStateValueKind.Int32 => constant.Kind == ProgramConstantKind.Int32,
                ProgramStateValueKind.UInt64 => constant.Kind == ProgramConstantKind.UInt64,
                ProgramStateValueKind.Scalar => constant.Kind == ProgramConstantKind.Scalar,
                ProgramStateValueKind.Vector2 => constant.Kind == ProgramConstantKind.Vector2,
                ProgramStateValueKind.Vector3 => constant.Kind == ProgramConstantKind.Vector3,
                ProgramStateValueKind.Yaw => constant.Kind == ProgramConstantKind.Yaw,
                ProgramStateValueKind.Identity => constant.Kind == ProgramConstantKind.String,
                ProgramStateValueKind.ActionTargetSnapshot => constant.Kind == ProgramConstantKind.Bytes,
                _ => false
            };
            if (!valid)
                throw new InvalidDataException($"State slot '{slot.Identity}' kind '{slot.ValueKind}' has invalid default constant kind '{constant.Kind}'.");
        }

        static void BuildDomainIndexes(
            IReadOnlyList<SimulationOperation> operations,
            IReadOnlyList<ProgramCatalogEntry> catalogEntries,
            IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            IReadOnlyList<TypedStateAddress> addresses,
            ProgramCatalogRuntimeIndex catalog,
            out string[] inputRequestIds,
            out Dictionary<string, int> actionCapacities,
            out int actionInstanceCapacity,
            out HashSet<int> motionWarpOperations)
        {
            var inputs = new HashSet<string>(StringComparer.Ordinal);
            var capacities = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalActionCapacity = 0;
            var motionWarp = new HashSet<int>();
            for (int index = 0; index < operations.Count; index++)
                if (operations[index].Code == SimulationOperationCode.MotionWarp)
                    motionWarp.Add(index);
            for (int i = 0; i < catalogEntries.Count; i++)
            {
                ProgramCatalogEntry entry = catalogEntries[i];
                if (entry.Kind == ProgramCatalogEntryKind.InputRequest)
                {
                    string requestId = TrimPrefix(entry.Identity, "input:request:");
                    if (!inputs.Add(requestId))
                        throw new InvalidDataException($"Input request '{requestId}' is duplicated.");
                }
                if (entry.Kind == ProgramCatalogEntryKind.Action)
                {
                    string actionId = TrimPrefix(entry.Identity, "action:");
                    ActionAdmissionProfile profile = ActionAdmissionProfileCompiler.Compile(
                        entry,
                        constantIndex => ReadInt32Constant(constants, constantIndex),
                        Float32GameplayEffectRuntimeCatalog.NormalizeTag);
                    if (!capacities.TryAdd(actionId, profile.MaxConcurrentInstances))
                        throw new InvalidDataException($"Action '{actionId}' is duplicated.");
                    totalActionCapacity += profile.MaxConcurrentInstances;
                }
            }

            inputRequestIds = SortedStrings(inputs);
            actionCapacities = capacities;
            actionInstanceCapacity = totalActionCapacity;
            motionWarpOperations = motionWarp;
        }

        static int ReadInt32Constant(IReadOnlyList<ProgramConstant> constants, int constantIndex)
        {
            if (constantIndex < 0 || constantIndex >= constants.Count || constants[constantIndex].Kind != ProgramConstantKind.Int32)
                throw new InvalidDataException($"Action target requirement constant '{constantIndex}' is invalid.");
            return constants[constantIndex].Int32;
        }

        static IReadOnlyList<BlackboardInputStateBinding> BuildBlackboardInputBindings(
            IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            IReadOnlyList<ProgramCatalogEntry> catalogEntries,
            ProgramCatalogRuntimeIndex catalog,
            IReadOnlyList<TypedStateAddress> addresses,
            int operationCount)
        {
            var stateByOwner = new Dictionary<string, TypedStateAddress>(StringComparer.Ordinal);
            for (int i = 0; i < stateSlots.Count; i++)
            {
                if (stateSlots[i].Semantic == ProgramStateSemantic.BlackboardValue)
                    stateByOwner.Add(stateSlots[i].OwnerIdentity, addresses[i]);
            }
            var bindings = new List<BlackboardInputStateBinding>();
            for (int i = 0; i < catalogEntries.Count; i++)
            {
                ProgramCatalogEntry declaration = catalogEntries[i];
                if (declaration.Kind != ProgramCatalogEntryKind.BlackboardDeclaration ||
                    !TryReadString(constants, catalog, declaration, ProgramCatalogFieldId.InputValueId, out string inputId))
                    continue;
                ProgramCatalogEntry input = catalog.FindEntry(ProgramCatalogEntryKind.InputValue, $"input:value:{inputId}") ??
                    throw new InvalidDataException($"Blackboard Input Binding '{declaration.Identity}' references absent input '{inputId}'.");
                int valueType = ReadInt32(constants, catalog, input, ProgramCatalogFieldId.ValueType);
                var kind = (ProgramInputValueKind)valueType;
                if (!Enum.IsDefined(typeof(ProgramInputValueKind), kind))
                    throw new InvalidDataException($"Input '{input.Identity}' has unknown value kind '{valueType}'.");
                if (!stateByOwner.TryGetValue(declaration.Identity, out TypedStateAddress address))
                    throw new InvalidDataException($"Blackboard Input Binding '{declaration.Identity}' has no state slot.");
                var projection = (ProgramBlackboardInputProjection)ReadInt32(constants, catalog, declaration, ProgramCatalogFieldId.InputProjection);
                if (projection != ProgramBlackboardInputProjection.Direct && kind != ProgramInputValueKind.ActionTargetSnapshot)
                    throw new InvalidDataException($"Blackboard Input Binding '{declaration.Identity}' target projection requires ActionTargetSnapshot input.");
                if (address.ValueKind != StateKind(kind, projection))
                    throw new InvalidDataException($"Blackboard Input Binding '{declaration.Identity}' state kind '{address.ValueKind}' does not match input kind '{kind}'.");
                bindings.Add(new BlackboardInputStateBinding(inputId, kind, address, projection));
            }
            bindings.Sort((left, right) => string.CompareOrdinal(left.InputId, right.InputId));
            return bindings.AsReadOnly();
        }

        static int ReadInt32(
            IReadOnlyList<ProgramConstant> constants,
            ProgramCatalogRuntimeIndex catalog,
            ProgramCatalogEntry entry,
            ProgramCatalogFieldId field)
        {
            ProgramCatalogField value = catalog.RequireField(entry, field);
            if (value.Kind != ProgramCatalogFieldKind.Constant ||
                value.ConstantIndex < 0 || value.ConstantIndex >= constants.Count ||
                constants[value.ConstantIndex].Kind != ProgramConstantKind.Int32)
                throw new InvalidDataException($"Catalog '{entry.Identity}' field '{field}' is not Int32.");
            return constants[value.ConstantIndex].Int32;
        }

        static bool TryReadString(
            IReadOnlyList<ProgramConstant> constants,
            ProgramCatalogRuntimeIndex catalog,
            ProgramCatalogEntry entry,
            ProgramCatalogFieldId field,
            out string text)
        {
            if (!catalog.TryGetField(entry, field, out ProgramCatalogField value))
            {
                text = string.Empty;
                return false;
            }
            if (value.Kind != ProgramCatalogFieldKind.Constant ||
                value.ConstantIndex < 0 || value.ConstantIndex >= constants.Count ||
                constants[value.ConstantIndex].Kind != ProgramConstantKind.String)
                throw new InvalidDataException($"Catalog '{entry.Identity}' field '{field}' is not String.");
            text = SimulationIdentity.Require(constants[value.ConstantIndex].Text, field.ToString());
            return true;
        }

        static ProgramStateValueKind StateKind(ProgramInputValueKind kind, ProgramBlackboardInputProjection projection) =>
            projection switch
            {
                ProgramBlackboardInputProjection.ActionTargetIdentity => ProgramStateValueKind.Identity,
                ProgramBlackboardInputProjection.ActionTargetPosition => ProgramStateValueKind.Vector3,
                ProgramBlackboardInputProjection.ActionTargetYaw => ProgramStateValueKind.Scalar,
                _ => kind switch
            {
                ProgramInputValueKind.Boolean => ProgramStateValueKind.Boolean,
                ProgramInputValueKind.Scalar => ProgramStateValueKind.Scalar,
                ProgramInputValueKind.Vector2 => ProgramStateValueKind.Vector2,
                ProgramInputValueKind.Vector3 => ProgramStateValueKind.Vector3,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                }
            };

        static int[] BuildSkillExecutionStateSlots(
            int operationCount,
            IReadOnlyList<ProgramScopeLayout> scopes,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            OperationExecutionTopology topology)
        {
            var characterScoped = new HashSet<int>();
            for (int i = 0; i < scopes.Count; i++)
            {
                if (scopes[i].Kind != ProgramScopeKind.Character)
                    continue;
                for (int slotIndex = 0; slotIndex < scopes[i].StateSlots.Count; slotIndex++)
                    characterScoped.Add(scopes[i].StateSlots[slotIndex]);
            }
            var result = new HashSet<int>();
            var visited = new bool[operationCount];
            Visit(Root(topology), topology, visited);
            for (int operationIndex = 0; operationIndex < visited.Length; operationIndex++)
            {
                if (!visited[operationIndex])
                    continue;
                OperationExecutionDescriptor operation = topology.Operation(new OperationHandle(operationIndex));
                for (int slotIndex = 0; slotIndex < operation.StateSlots.Count; slotIndex++)
                    AddSkillStateSlot(operation.StateSlots[slotIndex], stateSlots, characterScoped, result);
                for (int kind = 0; kind < EnumValueCount(typeof(ProgramReferenceKind)); kind++)
                {
                    IReadOnlyList<ProgramReference> references = topology.References(
                        new OperationHandle(operationIndex),
                        (ProgramReferenceKind)kind);
                    for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
                    {
                        if (references[referenceIndex].Kind == ProgramReferenceKind.StateSlot)
                            AddSkillStateSlot(references[referenceIndex].TargetIndex, stateSlots, characterScoped, result);
                    }
                }
            }
            for (int scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
            {
                ProgramScopeLayout scope = scopes[scopeIndex];
                if (scope.Kind == ProgramScopeKind.Character ||
                    !scope.OwnerOperation.IsValid ||
                    !visited[scope.OwnerOperation.Value])
                    continue;
                for (int slotIndex = 0; slotIndex < scope.StateSlots.Count; slotIndex++)
                    AddSkillStateSlot(scope.StateSlots[slotIndex], stateSlots, characterScoped, result);
            }
            return SortedIndexes(result);
        }

        static OperationHandle Root(OperationExecutionTopology topology) => topology.RootOperation;

        static void Visit(
            OperationHandle operation,
            OperationExecutionTopology topology,
            bool[] visited)
        {
            if (!operation.IsValid || operation.Value < 0 || operation.Value >= visited.Length || visited[operation.Value])
                return;
            visited[operation.Value] = true;
            for (int kind = 0; kind < EnumValueCount(typeof(ProgramControlFlowKind)); kind++)
            {
                IReadOnlyList<ProgramControlFlowEdge> edges = topology.Outgoing(operation, (ProgramControlFlowKind)kind);
                for (int i = 0; i < edges.Count; i++)
                    Visit(edges[i].Target, topology, visited);
            }
        }

        static void AddSkillStateSlot(
            int slotIndex,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            HashSet<int> characterScoped,
            HashSet<int> result)
        {
            if (slotIndex < 0 || slotIndex >= stateSlots.Count || characterScoped.Contains(slotIndex))
                return;
            ProgramStateOwnerKind owner = stateSlots[slotIndex].OwnerKind;
            if (owner == ProgramStateOwnerKind.Runnable ||
                owner == ProgramStateOwnerKind.StateMachine ||
                owner == ProgramStateOwnerKind.Timeline ||
                owner == ProgramStateOwnerKind.Blackboard)
            {
                result.Add(slotIndex);
            }
        }

        static string[] BuildOperationSourceIndexes(
            CharacterSkillId abilityId,
            int operationCount,
            IReadOnlyList<ProgramSourceMapEntry> sourceMap,
            out Dictionary<int, (ProgramExecutionSource Source, int Count)> sources)
        {
            sources = new Dictionary<int, (ProgramExecutionSource Source, int Count)>();
            var result = new string[operationCount];
            var assigned = new bool[operationCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = $"ability:{abilityId.Value}/operation:{i}";
            for (int i = 0; i < (sourceMap?.Count ?? 0); i++)
            {
                ProgramSourceMapEntry diagnostic = sourceMap[i];
                ProgramExecutionSource source = diagnostic.Source;
                if (source.TargetKind != ProgramSourceTargetKind.Operation)
                    continue;
                if (sources.TryGetValue(source.TargetIndex, out var indexed))
                    sources[source.TargetIndex] = (indexed.Source, indexed.Count + 1);
                else
                    sources.Add(source.TargetIndex, (source, 1));
                if (source.TargetIndex < 0 ||
                    source.TargetIndex >= result.Length ||
                    assigned[source.TargetIndex])
                    continue;
                result[source.TargetIndex] = diagnostic.DisplayPath;
                assigned[source.TargetIndex] = true;
            }
            return result;
        }

        static string[] SortedStrings(HashSet<string> values)
        {
            string[] result = new string[values.Count];
            values.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        static int[] SortedIndexes(HashSet<int> values)
        {
            int[] result = new int[values.Count];
            values.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        static string TrimPrefix(string value, string prefix)
        {
            if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length == prefix.Length)
                throw new InvalidDataException($"Identity '{value}' does not match '{prefix}*'.");
            return value.Substring(prefix.Length);
        }

        static void AddUnique<TKey>(
            IDictionary<TKey, TypedStateAddress> values,
            TKey key,
            TypedStateAddress address,
            string label)
        {
            if (!values.TryAdd(key, address))
                throw new InvalidDataException($"{label} '{key}' is duplicated.");
        }

        static int EnumValueCount(Type enumType)
        {
            Array values = Enum.GetValues(enumType);
            int maximum = 0;
            for (int i = 0; i < values.Length; i++)
                maximum = Math.Max(maximum, Convert.ToInt32(values.GetValue(i)));
            return checked(maximum + 1);
        }
    }
}
