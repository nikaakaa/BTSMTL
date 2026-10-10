using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ThirdPersonSimulation
{
    public enum ProgramCatalogFieldId : byte
    {
        ActionWindowDigest = 0,
        ActionWindowId = 1,
        ActionWindowType = 2,
        BlendMode = 3,
        Channel = 4,
        ConsumeLowerChannels = 5,
        EaseInCurve = 6,
        EaseOutCurve = 7,
        Intensity = 8,
        Muted = 9,
        Parent = 10,
        PositionX = 11,
        PositionY = 12,
        PositionZ = 13,
        Priority = 14,
        Projection = 15,
        Space = 16,
        Timeline = 17,
        Track = 18,
        WeightCurve = 19,
        Yaw = 20,
        TargetRequirement = 21,
        ValueType = 22,
        InputValueId = 23,
        SectionCount = 24,
        ActionWindowTag = 25
    }

    public sealed class ProgramCatalogRuntimeIndex
    {
        const int FieldCount = 26;
        readonly IReadOnlyList<ProgramCatalogEntry> m_Entries;
        readonly int m_KindCount;
        readonly int[] m_OperationEntries;
        readonly ProgramCatalogField[] m_Fields;
        readonly Dictionary<string, int>[] m_EntriesByIdentity;

        public ProgramCatalogRuntimeIndex(
            int operationCount,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramCatalogEntry> entries)
        {
            if (operationCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationCount));
            m_Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            m_KindCount = EnumValueCount(typeof(ProgramCatalogEntryKind));
            m_OperationEntries = new int[checked(operationCount * m_KindCount)];
            Array.Fill(m_OperationEntries, -1);
            m_Fields = new ProgramCatalogField[checked(entries.Count * FieldCount)];
            m_EntriesByIdentity = new Dictionary<string, int>[m_KindCount];

            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                ProgramCatalogEntry entry = entries[entryIndex]
                    ?? throw new ArgumentException($"Program catalog entry '{entryIndex}' is null.", nameof(entries));
                if (entry.Index != entryIndex)
                    throw new ArgumentException($"Program catalog entry '{entry.Index}' does not match index '{entryIndex}'.", nameof(entries));
                int kindIndex = (int)entry.Kind;
                Dictionary<string, int> identities = m_EntriesByIdentity[kindIndex] ??=
                    new Dictionary<string, int>(StringComparer.Ordinal);
                if (!identities.TryAdd(entry.Identity, entryIndex))
                    throw new ArgumentException($"Program catalog identity '{entry.Kind}/{entry.Identity}' is duplicated.", nameof(entries));

                for (int fieldIndex = 0; fieldIndex < entry.Fields.Count; fieldIndex++)
                {
                    ProgramCatalogField field = entry.Fields[fieldIndex];
                    if (!Enum.TryParse(field.Name, false, out ProgramCatalogFieldId fieldId) ||
                        !Enum.IsDefined(typeof(ProgramCatalogFieldId), fieldId))
                    {
                        continue;
                    }
                    int index = checked(entryIndex * FieldCount + (int)fieldId);
                    if (m_Fields[index] != null)
                        throw new ArgumentException($"Program catalog field '{entry.Identity}/{field.Name}' is duplicated.", nameof(entries));
                    m_Fields[index] = field;
                }
            }

            if (references == null)
                throw new ArgumentNullException(nameof(references));
            for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                ProgramReference reference = references[referenceIndex];
                if (reference == null || !reference.HasSourceOperation || reference.Kind != ProgramReferenceKind.CatalogEntry)
                    continue;
                if (reference.SourceOperation.Value >= operationCount || reference.TargetIndex < 0 || reference.TargetIndex >= entries.Count)
                    throw new ArgumentException($"Program catalog reference '{reference?.Identity}' is invalid.", nameof(references));
                ProgramCatalogEntry entry = entries[reference.TargetIndex];
                int index = checked(reference.SourceOperation.Value * m_KindCount + (int)entry.Kind);
                int existing = m_OperationEntries[index];
                if (existing == -1)
                    m_OperationEntries[index] = reference.TargetIndex;
                else if (existing != reference.TargetIndex)
                    m_OperationEntries[index] = -2;
            }
        }

        public ProgramCatalogEntry[] BindBlackboardDeclarations(
            IReadOnlyList<ProgramStateSlot> stateSlots,
            IReadOnlyList<ProgramReference> references)
        {
            var declarations = new ProgramCatalogEntry[stateSlots.Count];
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (reference.HasSourceOperation || reference.Kind != ProgramReferenceKind.StateSlot)
                    continue;
                if (reference.TargetIndex >= stateSlots.Count)
                    throw new ArgumentException($"Blackboard reference '{reference.Identity}' has an invalid state slot.", nameof(references));
                ProgramStateSlot slot = stateSlots[reference.TargetIndex];
                if (slot.Semantic != ProgramStateSemantic.BlackboardValue)
                    continue;
                if (declarations[slot.Index] != null)
                    throw new ArgumentException($"Blackboard state '{slot.Identity}' has multiple declaration bindings.", nameof(references));
                declarations[slot.Index] = FindEntry(ProgramCatalogEntryKind.BlackboardDeclaration, reference.ExternalIdentity)
                    ?? throw new ArgumentException($"Blackboard state '{slot.Identity}' references missing declaration '{reference.ExternalIdentity}'.", nameof(references));
            }
            for (int i = 0; i < stateSlots.Count; i++)
            {
                if (stateSlots[i].Semantic == ProgramStateSemantic.BlackboardValue && declarations[i] == null)
                    throw new ArgumentException($"Blackboard state '{stateSlots[i].Identity}' has no compiled declaration binding. Rebuild the ability graph.", nameof(references));
            }
            return declarations;
        }

        public ProgramCatalogEntry RequireEntry(OperationHandle operation, ProgramCatalogEntryKind kind)
        {
            ProgramCatalogEntry entry = FindEntry(operation, kind);
            return entry ?? throw new InvalidOperationException($"Operation '{operation}' has no '{kind}' catalog reference.");
        }

        public ProgramCatalogEntry FindEntry(OperationHandle operation, ProgramCatalogEntryKind kind)
        {
            if (!operation.IsValid || operation.Value * m_KindCount >= m_OperationEntries.Length)
                throw new ArgumentOutOfRangeException(nameof(operation));
            int entryIndex = m_OperationEntries[operation.Value * m_KindCount + (int)kind];
            if (entryIndex == -2)
                throw new InvalidOperationException($"Operation '{operation}' has multiple '{kind}' catalog references.");
            return entryIndex < 0 ? null : m_Entries[entryIndex];
        }

        public ProgramCatalogEntry FindEntry(ProgramCatalogEntryKind kind, string identity)
        {
            int kindIndex = (int)kind;
            if (kindIndex < 0 || kindIndex >= m_EntriesByIdentity.Length)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Dictionary<string, int> entries = m_EntriesByIdentity[kindIndex];
            return entries != null && entries.TryGetValue(identity ?? string.Empty, out int index)
                ? m_Entries[index]
                : null;
        }

        public ProgramCatalogField RequireField(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            int fieldIndex = (int)field;
            if (entry.Index < 0 || entry.Index >= m_Entries.Count || fieldIndex < 0 || fieldIndex >= FieldCount)
                throw new ArgumentOutOfRangeException(nameof(field));
            ProgramCatalogField value = m_Fields[entry.Index * FieldCount + fieldIndex];
            return value ?? throw new InvalidOperationException($"Catalog '{entry.Identity}' has no field '{field}'.");
        }

        public bool TryGetField(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out ProgramCatalogField value)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            int fieldIndex = (int)field;
            if (entry.Index < 0 || entry.Index >= m_Entries.Count || fieldIndex < 0 || fieldIndex >= FieldCount)
                throw new ArgumentOutOfRangeException(nameof(field));
            value = m_Fields[entry.Index * FieldCount + fieldIndex];
            return value != null;
        }

        public bool TryGetIdentity(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out string identity)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            int fieldIndex = (int)field;
            if (entry.Index < 0 || entry.Index >= m_Entries.Count || fieldIndex < 0 || fieldIndex >= FieldCount)
                throw new ArgumentOutOfRangeException(nameof(field));
            ProgramCatalogField value = m_Fields[entry.Index * FieldCount + fieldIndex];
            if (value != null && value.Kind == ProgramCatalogFieldKind.Identity)
            {
                identity = value.Identity;
                return true;
            }
            identity = string.Empty;
            return false;
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

    public enum OperationNamedConstant : byte
    {
        ActionContext = 0,
        SourceInputRequest = 1,
        ConsumeSourceInputRequest = 2,
        TargetKey = 3,
        TargetSnapshotDeclaration = 4,
        TargetSnapshotOwner = 5,
        FactContext = 6,
        Predicted = 7,
        Effect = 8,
        DefinitionRevision = 9,
        MoveSpeed = 11,
        TurnSpeedDegrees = 12,
        Weight = 13,
        Intensity = 14,
        DurationSeconds = 15,
        ActionMotionPositionX = 16,
        ActionMotionPositionZ = 17,
        ActionMotionDuration = 18,
        SequenceId = 19,
        Priority = 20,
        BlendInSeconds = 21,
        BlendOutSeconds = 22,
        RequestId = 23,
        ResourceId = 24,
        ManualOrbitWeight = 25,
        PitchResponseWeight = 26,
        YawResponseWeight = 27,
        AnchorKey = 28,
        AimPointKey = 29,
        PreferredBoneKey = 30,
        AttackEventId = 31,
        VfxTarget = 32,
        HasPoseOverride = 33,
        CoordinatesOption = 34,
        UseRotationY = 35,
        UseRotationXZ = 36,
        NeedRaycastGround = 37,
        LocalPositionX = 38,
        LocalPositionY = 39,
        LocalPositionZ = 40,
        LocalEulerAnglesX = 41,
        LocalEulerAnglesY = 42,
        LocalEulerAnglesZ = 43,
        LocalScaleX = 44,
        LocalScaleY = 45,
        LocalScaleZ = 46,
        WindowEndTime = 47,
        TargetPlanarOffsetX = 48,
        TargetPlanarOffsetZ = 49,
        TargetYawOffsetDegrees = 50,
        MaximumPositionCorrection = 51,
        MaximumYawCorrectionDegrees = 52,
        MaximumYawRateDegreesPerSecond = 53,
        PositionProgressCurve = 54,
        YawProgressCurve = 55,
        YawResponseCurve = 56,
        InputYawResponseCurve = 57,
        AudioTarget = 58,
        AudioLifetime = 59,
        AudioSoundSlot = 60,
        AudioParameterSlot = 61,
        AudioParameterValue = 62,
        UseAttachPointRotation = 63,
        SourceAbility = 64,
        FollowDelaySeconds = 65,
        FollowDurationSeconds = 66,
        AimDelaySeconds = 67,
        AimDurationSeconds = 68
    }

    public static class OperationNamedConstantSchema
    {
        const string Marker = "/constant/";

        public static int Count { get; } = Enum.GetValues(typeof(OperationNamedConstant))
            .Cast<OperationNamedConstant>().Max(field => (int)field) + 1;

        public static bool TryParseIdentity(string identity, out OperationNamedConstant field)
        {
            field = default;
            int markerIndex = identity?.LastIndexOf(Marker, StringComparison.Ordinal) ?? -1;
            if (markerIndex < 0)
                return false;
            string value = identity.Substring(markerIndex + Marker.Length);
            return Enum.TryParse(value, false, out field) && Enum.IsDefined(typeof(OperationNamedConstant), field);
        }

        public static bool TryGetDynamicField(string identity, out string field)
        {
            int markerIndex = identity?.LastIndexOf(Marker, StringComparison.Ordinal) ?? -1;
            field = markerIndex < 0 ? string.Empty : identity.Substring(markerIndex + Marker.Length);
            return field.Length != 0;
        }
    }

    public sealed class OperationExecutionDescriptor
    {
        readonly ReadOnlyCollection<int> m_StateSlots;

        public OperationExecutionDescriptor(
            OperationHandle handle,
            SimulationOperationCode code,
            int integer0,
            int integer1,
            ulong unsigned0,
            string text0,
            uint flags,
            IEnumerable<int> stateSlots)
        {
            if (!handle.IsValid)
                throw new ArgumentException("Operation handle is invalid.", nameof(handle));
            Handle = handle;
            Code = code;
            CodeName = code.ToString();
            Integer0 = integer0;
            Integer1 = integer1;
            Unsigned0 = unsigned0;
            Text0 = text0 ?? string.Empty;
            Flags = flags;
            var slots = stateSlots == null ? new List<int>() : new List<int>(stateSlots);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] < 0)
                    throw new ArgumentOutOfRangeException(nameof(stateSlots));
            }
            m_StateSlots = slots.AsReadOnly();
        }

        public OperationHandle Handle { get; }
        public SimulationOperationCode Code { get; }
        public string CodeName { get; }
        public int Integer0 { get; }
        public int Integer1 { get; }
        public ulong Unsigned0 { get; }
        public string Text0 { get; }
        public uint Flags { get; }
        public IReadOnlyList<int> StateSlots => m_StateSlots;
    }

    public sealed class OperationExecutionTopology
    {
        readonly ReadOnlyCollection<OperationExecutionDescriptor> m_Operations;
        readonly string[] m_Identities;
        readonly Dictionary<CanonicalTextValue, OperationHandle> m_OperationByIdentity;
        readonly IReadOnlyList<ProgramControlFlowEdge>[][] m_Outgoing;
        readonly IReadOnlyList<ProgramReference>[][] m_References;
        readonly int[][] m_OperationStateSlots;
        readonly int[] m_TimelineOperations;
        readonly int[] m_TimelineOwners;
        readonly int[] m_StateMachineOwners;
        readonly OperationHandle[] m_StateMachineInitialEntries;
        readonly OperationHandle[] m_StateMachineAnyStateEntries;
        readonly string[] m_StateIdentities;
        readonly ProgramControlFlowEdge[] m_StateOnEnter;
        readonly ProgramControlFlowEdge[] m_StateRoot;
        readonly ProgramControlFlowEdge[] m_StateOnExit;
        readonly OperationHandle[] m_StateSingleLocomotions;
        readonly IReadOnlyList<ProgramGraphCallFrame>[] m_GraphCallFrames;

        public OperationExecutionTopology(
            IEnumerable<OperationExecutionDescriptor> operations,
            IEnumerable<ProgramControlFlowEdge> controlFlow,
            IEnumerable<ProgramReference> references,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            IReadOnlyList<ProgramSourceMapEntry> sourceMap,
            OperationHandle rootOperation,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames = null)
        {
            var operationList = operations == null
                ? new List<OperationExecutionDescriptor>()
                : new List<OperationExecutionDescriptor>(operations);
            if (operationList.Count == 0)
                throw new ArgumentException("Operation topology is empty.", nameof(operations));
            for (int i = 0; i < operationList.Count; i++)
            {
                OperationExecutionDescriptor operation = operationList[i]
                    ?? throw new ArgumentException($"Operation topology contains null at index {i}.", nameof(operations));
                if (operation.Handle.Value != i)
                    throw new ArgumentException($"Operation topology handle '{operation.Handle}' does not match index '{i}'.", nameof(operations));
            }
            if (!rootOperation.IsValid || rootOperation.Value >= operationList.Count)
                throw new ArgumentOutOfRangeException(nameof(rootOperation));
            if (operationList[rootOperation.Value].Code != SimulationOperationCode.Root)
                throw new ArgumentException($"Root operation '{rootOperation}' is '{operationList[rootOperation.Value].Code}'.", nameof(rootOperation));
            if (stateSlots == null)
                throw new ArgumentNullException(nameof(stateSlots));
            if (sourceMap == null)
                throw new ArgumentNullException(nameof(sourceMap));

            var edges = controlFlow == null
                ? new List<ProgramControlFlowEdge>()
                : new List<ProgramControlFlowEdge>(controlFlow);
            ValidateEdges(edges, operationList.Count);
            var referenceList = references == null
                ? new List<ProgramReference>()
                : new List<ProgramReference>(references);
            ValidateReferences(referenceList, operationList.Count);
            ValidateStateSlots(operationList, stateSlots);

            m_Operations = operationList.AsReadOnly();
            m_Identities = BuildIdentities(operationList.Count);
            m_StateIdentities = BuildStateIdentities(operationList, m_Identities);
            m_OperationByIdentity = BuildOperationIdentities(m_Identities);
            m_Outgoing = BuildOutgoing(operationList.Count, edges);
            m_References = BuildReferences(operationList.Count, referenceList);
            m_OperationStateSlots = BuildOperationStateSlots(operationList, stateSlots);
            BuildTimelineIndexes(operationList, edges, out m_TimelineOperations, out m_TimelineOwners);
            m_StateMachineOwners = BuildStateMachineOwners(operationList, referenceList);
            BuildStateMachineEntries(operationList, m_Outgoing, out m_StateMachineInitialEntries, out m_StateMachineAnyStateEntries);
            BuildStateEdges(operationList, edges, out m_StateOnEnter, out m_StateRoot, out m_StateOnExit);
            m_StateSingleLocomotions = BuildStateSingleLocomotions(operationList, m_StateRoot, m_Outgoing);
            m_GraphCallFrames = BuildGraphCallFrames(operationList, graphCallFrames);
            RootOperation = rootOperation;
        }

        public IReadOnlyList<OperationExecutionDescriptor> Operations => m_Operations;
        public OperationHandle RootOperation { get; }

        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames(OperationHandle owner)
        {
            if (!owner.IsValid || owner.Value >= m_GraphCallFrames.Length)
                return Array.Empty<ProgramGraphCallFrame>();
            return m_GraphCallFrames[owner.Value];
        }

        public OperationExecutionDescriptor Operation(OperationHandle handle)
        {
            RequireOperation(handle);
            return m_Operations[handle.Value];
        }

        public string OperationIdentity(OperationHandle handle)
        {
            RequireOperation(handle);
            return m_Identities[handle.Value];
        }

        public bool TryResolveOperation(CanonicalTextValue identity, out OperationHandle operation)
        {
            return m_OperationByIdentity.TryGetValue(identity, out operation);
        }

        public string StateIdentity(OperationHandle state)
        {
            RequireOperation(state);
            return m_StateIdentities[state.Value];
        }

        public IReadOnlyList<ProgramControlFlowEdge> Outgoing(OperationHandle source, ProgramControlFlowKind kind)
        {
            RequireOperation(source);
            int kindIndex = (int)kind;
            if (kindIndex < 0 || kindIndex >= m_Outgoing[source.Value].Length)
                throw new ArgumentOutOfRangeException(nameof(kind));
            return m_Outgoing[source.Value][kindIndex];
        }

        public IReadOnlyList<ProgramReference> References(OperationHandle source, ProgramReferenceKind kind)
        {
            RequireOperation(source);
            int kindIndex = (int)kind;
            if (kindIndex < 0 || kindIndex >= m_References[source.Value].Length)
                throw new ArgumentOutOfRangeException(nameof(kind));
            return m_References[source.Value][kindIndex];
        }

        public ProgramReference FirstReference(OperationHandle source, ProgramReferenceKind kind)
        {
            IReadOnlyList<ProgramReference> references = References(source, kind);
            return references.Count == 0 ? null : references[0];
        }

        public int TimelineOperationCount => m_TimelineOperations.Length;

        public OperationExecutionDescriptor TimelineOperationAt(int index)
        {
            if (index < 0 || index >= m_TimelineOperations.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_Operations[m_TimelineOperations[index]];
        }

        public OperationHandle TimelineOwner(OperationHandle child)
        {
            RequireOperation(child);
            int owner = m_TimelineOwners[child.Value];
            if (owner < 0)
                throw new InvalidOperationException($"Operation '{child}' has no Timeline owner.");
            return new OperationHandle(owner);
        }

        public OperationHandle StateMachineOwner(OperationHandle state)
        {
            RequireOperation(state);
            int owner = m_StateMachineOwners[state.Value];
            return owner < 0 ? OperationHandle.Invalid : new OperationHandle(owner);
        }

        public OperationHandle StateMachineInitialEntry(OperationHandle machine)
        {
            RequireOperation(machine);
            return m_StateMachineInitialEntries[machine.Value];
        }

        public OperationHandle StateMachineAnyStateEntry(OperationHandle machine)
        {
            RequireOperation(machine);
            return m_StateMachineAnyStateEntries[machine.Value];
        }

        public ProgramControlFlowEdge StateOnEnter(OperationHandle state) => StateEdge(m_StateOnEnter, state);
        public ProgramControlFlowEdge StateRoot(OperationHandle state) => StateEdge(m_StateRoot, state);
        public ProgramControlFlowEdge StateOnExit(OperationHandle state) => StateEdge(m_StateOnExit, state);
        public OperationExecutionDescriptor StateSingleLocomotion(OperationHandle state)
        {
            RequireOperation(state);
            OperationHandle locomotion = m_StateSingleLocomotions[state.Value];
            return locomotion.IsValid ? m_Operations[locomotion.Value] : null;
        }

        public int FindOperationStateSlot(OperationHandle operation, ProgramStateSemantic semantic)
        {
            RequireOperation(operation);
            int semanticIndex = (int)semantic;
            return semanticIndex < m_OperationStateSlots[operation.Value].Length
                ? m_OperationStateSlots[operation.Value][semanticIndex]
                : -1;
        }

        public int RequireOperationStateSlot(OperationHandle operation, ProgramStateSemantic semantic)
        {
            int slot = FindOperationStateSlot(operation, semantic);
            if (slot < 0)
                throw new InvalidOperationException($"Operation '{operation}' has no state slot for '{semantic}'.");
            return slot;
        }

        public void RequireOperation(OperationHandle operation)
        {
            if (!operation.IsValid || operation.Value >= m_Operations.Count)
                throw new ArgumentOutOfRangeException(nameof(operation), $"Operation handle '{operation}' is outside this topology.");
        }

        static string[] BuildIdentities(int operationCount)
        {
            var identities = new string[operationCount];
            for (int i = 0; i < operationCount; i++)
                identities[i] = i.ToString(CultureInfo.InvariantCulture);
            return identities;
        }

        static Dictionary<CanonicalTextValue, OperationHandle> BuildOperationIdentities(string[] identities)
        {
            var operationByIdentity = new Dictionary<CanonicalTextValue, OperationHandle>(identities.Length);
            for (int i = 0; i < identities.Length; i++)
                operationByIdentity.Add(identities[i], new OperationHandle(i));
            return operationByIdentity;
        }

        static string[] BuildStateIdentities(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            string[] identities)
        {
            var stateIdentities = new string[operations.Count];
            for (int i = 0; i < operations.Count; i++)
            {
                if (operations[i].Code == SimulationOperationCode.State)
                    stateIdentities[i] = $"state:{identities[i]}";
            }
            return stateIdentities;
        }

        static void ValidateEdges(IReadOnlyList<ProgramControlFlowEdge> edges, int operationCount)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                ProgramControlFlowEdge edge = edges[i]
                    ?? throw new ArgumentException($"Control-flow contains null at index {i}.", nameof(edges));
                if (!edge.Source.IsValid || edge.Source.Value >= operationCount ||
                    !edge.Target.IsValid || edge.Target.Value >= operationCount ||
                    edge.HasCondition && (!edge.Condition.IsValid || edge.Condition.Value >= operationCount))
                {
                    throw new ArgumentException($"Control-flow edge '{edge.Identity}' targets an operation outside this topology.", nameof(edges));
                }
            }
        }

        static void ValidateReferences(IReadOnlyList<ProgramReference> references, int operationCount)
        {
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i]
                    ?? throw new ArgumentException($"Program reference contains null at index {i}.", nameof(references));
                if (reference.HasSourceOperation && reference.SourceOperation.Value >= operationCount)
                    throw new ArgumentException($"Program reference '{reference.Identity}' has an invalid source operation.", nameof(references));
                if (reference.Kind == ProgramReferenceKind.Operation &&
                    reference.TargetIndex >= operationCount)
                    throw new ArgumentException($"Program reference '{reference.Identity}' has an invalid target operation.", nameof(references));
            }
        }

        static void ValidateStateSlots(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            IReadOnlyList<ProgramStateSlot> stateSlots)
        {
            for (int i = 0; i < stateSlots.Count; i++)
            {
                if (stateSlots[i] == null || stateSlots[i].Index != i)
                    throw new ArgumentException($"State slot at index '{i}' is invalid.", nameof(stateSlots));
            }
            for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                IReadOnlyList<int> slots = operations[operationIndex].StateSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] >= stateSlots.Count)
                        throw new ArgumentException($"Operation '{operationIndex}' state slot '{slots[i]}' is outside the layout.", nameof(operations));
                }
            }
        }

        static IReadOnlyList<ProgramControlFlowEdge>[][] BuildOutgoing(
            int operationCount,
            IReadOnlyList<ProgramControlFlowEdge> edges)
        {
            int kindCount = EnumValueCount(typeof(ProgramControlFlowKind));
            var grouped = new List<ProgramControlFlowEdge>[operationCount][];
            for (int operationIndex = 0; operationIndex < operationCount; operationIndex++)
            {
                grouped[operationIndex] = new List<ProgramControlFlowEdge>[kindCount];
                for (int kindIndex = 0; kindIndex < kindCount; kindIndex++)
                    grouped[operationIndex][kindIndex] = new List<ProgramControlFlowEdge>();
            }
            for (int i = 0; i < edges.Count; i++)
                grouped[edges[i].Source.Value][(int)edges[i].Kind].Add(edges[i]);

            var result = new IReadOnlyList<ProgramControlFlowEdge>[operationCount][];
            for (int operationIndex = 0; operationIndex < operationCount; operationIndex++)
            {
                result[operationIndex] = new IReadOnlyList<ProgramControlFlowEdge>[kindCount];
                for (int kindIndex = 0; kindIndex < kindCount; kindIndex++)
                {
                    List<ProgramControlFlowEdge> values = grouped[operationIndex][kindIndex];
                    values.Sort(CompareEdges);
                    result[operationIndex][kindIndex] = values.Count == 0
                        ? Array.Empty<ProgramControlFlowEdge>()
                        : Array.AsReadOnly(values.ToArray());
                }
            }
            return result;
        }

        static int[][] BuildOperationStateSlots(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            IReadOnlyList<ProgramStateSlot> stateSlots)
        {
            int semanticCount = EnumValueCount(typeof(ProgramStateSemantic));
            var result = new int[operations.Count][];
            for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                var slots = new int[semanticCount];
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = -1;
                IReadOnlyList<int> operationSlots = operations[operationIndex].StateSlots;
                for (int i = 0; i < operationSlots.Count; i++)
                {
                    int slotIndex = operationSlots[i];
                    int semanticIndex = (int)stateSlots[slotIndex].Semantic;
                    if (slots[semanticIndex] < 0)
                        slots[semanticIndex] = slotIndex;
                }
                result[operationIndex] = slots;
            }
            return result;
        }

        static IReadOnlyList<ProgramReference>[][] BuildReferences(
            int operationCount,
            IReadOnlyList<ProgramReference> references)
        {
            int kindCount = EnumValueCount(typeof(ProgramReferenceKind));
            var grouped = new List<ProgramReference>[operationCount][];
            for (int operationIndex = 0; operationIndex < operationCount; operationIndex++)
            {
                grouped[operationIndex] = new List<ProgramReference>[kindCount];
                for (int kindIndex = 0; kindIndex < kindCount; kindIndex++)
                    grouped[operationIndex][kindIndex] = new List<ProgramReference>();
            }
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (reference.HasSourceOperation)
                    grouped[reference.SourceOperation.Value][(int)reference.Kind].Add(reference);
            }

            var result = new IReadOnlyList<ProgramReference>[operationCount][];
            for (int operationIndex = 0; operationIndex < operationCount; operationIndex++)
            {
                result[operationIndex] = new IReadOnlyList<ProgramReference>[kindCount];
                for (int kindIndex = 0; kindIndex < kindCount; kindIndex++)
                {
                    List<ProgramReference> values = grouped[operationIndex][kindIndex];
                    result[operationIndex][kindIndex] = values.Count == 0
                        ? Array.Empty<ProgramReference>()
                        : Array.AsReadOnly(values.ToArray());
                }
            }
            return result;
        }

        static void BuildTimelineIndexes(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            IReadOnlyList<ProgramControlFlowEdge> edges,
            out int[] timelines,
            out int[] owners)
        {
            var timelineValues = new List<int>();
            owners = new int[operations.Count];
            for (int i = 0; i < owners.Length; i++)
            {
                owners[i] = -1;
                if (operations[i].Code == SimulationOperationCode.Timeline)
                    timelineValues.Add(i);
            }
            for (int i = 0; i < edges.Count; i++)
            {
                ProgramControlFlowEdge edge = edges[i];
                if (edge.Kind != ProgramControlFlowKind.Child || operations[edge.Source.Value].Code != SimulationOperationCode.Timeline)
                    continue;
                if (owners[edge.Target.Value] >= 0)
                    throw new ArgumentException($"Operation '{edge.Target}' has multiple Timeline owners.", nameof(edges));
                owners[edge.Target.Value] = edge.Source.Value;
            }
            timelines = timelineValues.ToArray();
        }

        static int[] BuildStateMachineOwners(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            IReadOnlyList<ProgramReference> references)
        {
            var result = new int[operations.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = -1;
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (reference.Kind != ProgramReferenceKind.Operation || !reference.HasSourceOperation ||
                    operations[reference.SourceOperation.Value].Code != SimulationOperationCode.State)
                    continue;
                if (operations[reference.TargetIndex].Code != SimulationOperationCode.StateMachine)
                    continue;
                int state = reference.SourceOperation.Value;
                if (result[state] >= 0)
                    throw new ArgumentException($"State '{state}' declares multiple StateMachine owner references.");
                result[state] = reference.TargetIndex;
            }
            for (int i = 0; i < operations.Count; i++)
                if (operations[i].Code == SimulationOperationCode.State && result[i] < 0)
                    throw new ArgumentException($"State '{i}' has no compiled StateMachine owner reference.");
            return result;
        }

        static void BuildStateMachineEntries(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            IReadOnlyList<ProgramControlFlowEdge>[][] outgoing,
            out OperationHandle[] initialEntries,
            out OperationHandle[] anyStateEntries)
        {
            initialEntries = new OperationHandle[operations.Count];
            anyStateEntries = new OperationHandle[operations.Count];
            for (int i = 0; i < operations.Count; i++)
            {
                if (operations[i].Code != SimulationOperationCode.StateMachine)
                    continue;
                IReadOnlyList<ProgramControlFlowEdge> entries = outgoing[i][(int)ProgramControlFlowKind.Enter];
                for (int j = 0; j < entries.Count; j++)
                {
                    bool isAnyState = string.Equals(entries[j].SourcePort, "AnyState", StringComparison.Ordinal);
                    if (isAnyState && !anyStateEntries[i].IsValid)
                        anyStateEntries[i] = entries[j].Target;
                    else if (!isAnyState && !initialEntries[i].IsValid)
                        initialEntries[i] = entries[j].Target;
                }
                if (!initialEntries[i].IsValid && entries.Count > 0)
                    initialEntries[i] = entries[0].Target;
            }
        }

        static void BuildStateEdges(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            IReadOnlyList<ProgramControlFlowEdge> edges,
            out ProgramControlFlowEdge[] onEnter,
            out ProgramControlFlowEdge[] root,
            out ProgramControlFlowEdge[] onExit)
        {
            onEnter = new ProgramControlFlowEdge[operations.Count];
            root = new ProgramControlFlowEdge[operations.Count];
            onExit = new ProgramControlFlowEdge[operations.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                ProgramControlFlowEdge edge = edges[i];
                if (operations[edge.Source.Value].Code != SimulationOperationCode.State)
                    continue;
                if (edge.Kind == ProgramControlFlowKind.Enter && string.Equals(edge.SourcePort, "OnEnter", StringComparison.Ordinal))
                    Assign(onEnter, edge);
                else if (edge.Kind == ProgramControlFlowKind.Enter && string.Equals(edge.SourcePort, "Root", StringComparison.Ordinal))
                    Assign(root, edge);
                else if (edge.Kind == ProgramControlFlowKind.Exit && string.Equals(edge.SourcePort, "OnExit", StringComparison.Ordinal))
                    Assign(onExit, edge);
            }
        }

        static IReadOnlyList<ProgramGraphCallFrame>[] BuildGraphCallFrames(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            IReadOnlyList<ProgramGraphCallFrame> frames)
        {
            int operationCount = operations.Count;
            var result = new IReadOnlyList<ProgramGraphCallFrame>[operationCount];
            IReadOnlyList<ProgramGraphCallFrame> frameList = frames ?? Array.Empty<ProgramGraphCallFrame>();
            var operationCodes = new SimulationOperationCode[operationCount];
            for (int i = 0; i < operationCount; i++)
                operationCodes[i] = operations[i].Code;
            ProgramGraphCallFrameContract.ValidateCoverage(operationCodes, frameList);
            if (frameList.Count == 0)
                return result;
            var grouped = new List<ProgramGraphCallFrame>[operationCount];
            for (int i = 0; i < frameList.Count; i++)
            {
                ProgramGraphCallFrame frame = frameList[i];
                if (frame.OwnerOperation.Value < 0 || frame.OwnerOperation.Value >= operationCount)
                    throw new ArgumentException($"Graph call frame '{frame.Identity}' owner is outside the operation table.", nameof(frameList));
                List<ProgramGraphCallFrame> values = grouped[frame.OwnerOperation.Value];
                if (values == null)
                {
                    values = new List<ProgramGraphCallFrame>();
                    grouped[frame.OwnerOperation.Value] = values;
                }
                values.Add(frame);
            }
            for (int i = 0; i < grouped.Length; i++)
            {
                if (grouped[i] == null)
                    continue;
                grouped[i].Sort((left, right) => left.Index.CompareTo(right.Index));
                result[i] = grouped[i].AsReadOnly();
            }
            return result;
        }

        static OperationHandle[] BuildStateSingleLocomotions(
            IReadOnlyList<OperationExecutionDescriptor> operations,
            ProgramControlFlowEdge[] roots,
            IReadOnlyList<ProgramControlFlowEdge>[][] outgoing)
        {
            var result = new OperationHandle[operations.Count];
            var pending = new Stack<OperationHandle>();
            for (int i = 0; i < operations.Count; i++)
            {
                if (operations[i].Code != SimulationOperationCode.State || roots[i] == null)
                    continue;
                OperationHandle found = OperationHandle.Invalid;
                bool unique = true;
                pending.Push(roots[i].Target);
                while (pending.Count > 0)
                {
                    OperationHandle handle = pending.Pop();
                    if (operations[handle.Value].Code == SimulationOperationCode.LocomotionInputMotion)
                    {
                        if (found.IsValid)
                        {
                            unique = false;
                            break;
                        }
                        found = handle;
                    }
                    IReadOnlyList<ProgramControlFlowEdge> children = outgoing[handle.Value][(int)ProgramControlFlowKind.Child];
                    for (int j = children.Count - 1; j >= 0; j--)
                        pending.Push(children[j].Target);
                }
                pending.Clear();
                result[i] = unique ? found : OperationHandle.Invalid;
            }
            return result;
        }

        static void Assign(ProgramControlFlowEdge[] values, ProgramControlFlowEdge edge)
        {
            if (values[edge.Source.Value] != null)
                throw new ArgumentException($"State '{edge.Source}' has duplicate semantic edge '{edge.SourcePort}'.");
            values[edge.Source.Value] = edge;
        }

        ProgramControlFlowEdge StateEdge(ProgramControlFlowEdge[] values, OperationHandle state)
        {
            RequireOperation(state);
            return values[state.Value];
        }

        static int CompareEdges(ProgramControlFlowEdge left, ProgramControlFlowEdge right)
        {
            int byPriority = left.Kind == ProgramControlFlowKind.Transition
                ? left.Priority.CompareTo(right.Priority)
                : 0;
            if (byPriority != 0)
                return byPriority;
            int byOrder = left.Order.CompareTo(right.Order);
            if (byOrder != 0)
                return byOrder;
            int byPort = string.CompareOrdinal(left.SourcePort, right.SourcePort);
            return byPort != 0 ? byPort : string.CompareOrdinal(left.Identity, right.Identity);
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
