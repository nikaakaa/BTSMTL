using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public static class OrchestrationOperationSet
    {
        static readonly ReadOnlyCollection<OrchestrationOperationCode> s_Operations =
            Array.AsReadOnly((OrchestrationOperationCode[])Enum.GetValues(typeof(OrchestrationOperationCode)));

        public static IReadOnlyList<OrchestrationOperationCode> Operations => s_Operations;

        public static void RequireOperation(OrchestrationOperationCode code)
        {
            for (int i = 0; i < s_Operations.Count; i++)
                if (s_Operations[i] == code)
                    return;
            throw new InvalidOperationException($"Operation code '{(ushort)code}' is not a BTSMTL orchestration operation.");
        }
    }

    public readonly partial struct ProgramEnumConstant
    {
        public ProgramEnumConstant(string typeIdentity, int value)
        {
            TypeIdentity = typeIdentity ?? string.Empty;
            Value = value;
        }

        public string TypeIdentity { get; }
        public int Value { get; }
    }

    public enum ProgramControlFlowKind : byte
    {
        Child = 1,
        Transition = 2,
        Enter = 3,
        Exit = 4,
        Interrupt = 5,
        Value = 6
    }

    public sealed partial class ProgramControlFlowEdge
    {
        public ProgramControlFlowEdge(
            string identity,
            OperationHandle source,
            OperationHandle target,
            string sourcePort,
            string targetPort,
            ProgramControlFlowKind kind,
            int order,
            int priority,
            OrchestrationAbortPolicy abortPolicy,
            bool hasCondition,
            OperationHandle condition)
        {
            Identity = identity.Trim();
            Source = source;
            Target = target;
            SourcePort = sourcePort ?? string.Empty;
            TargetPort = targetPort ?? string.Empty;
            Kind = kind;
            Order = order;
            Priority = priority;
            AbortPolicy = abortPolicy;
            HasCondition = hasCondition;
            Condition = condition;
            RequireRecord();
        }

        public string Identity { get; }
        public OperationHandle Source { get; }
        public OperationHandle Target { get; }
        public string SourcePort { get; }
        public string TargetPort { get; }
        public ProgramControlFlowKind Kind { get; }
        public int Order { get; }
        public int Priority { get; }
        public OrchestrationAbortPolicy AbortPolicy { get; }
        public bool HasCondition { get; }
        public OperationHandle Condition { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SimulationIdentity.Require(Identity, nameof(Identity));
            if (!Source.IsValid || !Target.IsValid || HasCondition && !Condition.IsValid)
                throw new ArgumentException("Control-flow edge contains an invalid operation handle.");
        }
    }

    public enum ProgramReferenceKind : byte
    {
        Operation = 1,
        Constant = 2,
        StateSlot = 3,
        Scope = 4,
        OutputChannel = 6,
        Producer = 7,
        CatalogEntry = 8
    }

    public sealed partial class ProgramReference
    {
        public ProgramReference(string identity, OperationHandle sourceOperation, ProgramReferenceKind kind, int targetIndex, string externalIdentity)
        {
            Identity = identity.Trim();
            SourceOperation = sourceOperation;
            Kind = kind;
            TargetIndex = targetIndex;
            ExternalIdentity = externalIdentity ?? string.Empty;
            RequireRecord();
        }
        public string Identity { get; }
        public OperationHandle SourceOperation { get; }
        public bool HasSourceOperation => SourceOperation.IsValid;
        public ProgramReferenceKind Kind { get; }
        public int TargetIndex { get; }
        public string ExternalIdentity { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (TargetIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(TargetIndex));
            SimulationIdentity.Require(Identity, nameof(Identity));
        }
    }

    public enum ProgramGraphParameterDirection : byte
    {
        Input = 1,
        Output = 2
    }

    public sealed partial class ProgramGraphParameterBinding
    {
        public ProgramGraphParameterBinding(
            ProgramGraphParameterDirection direction,
            string parameterName,
            string declarationIdentity,
            int stateSlot,
            string portId,
            SemanticValueKind valueKind)
        {
            if (!Enum.IsDefined(typeof(ProgramGraphParameterDirection), direction) ||
                !Enum.IsDefined(typeof(SemanticValueKind), valueKind))
                throw new ArgumentException("Program graph parameter binding is incomplete.");
            Direction = direction;
            ParameterName = parameterName.Trim();
            DeclarationIdentity = declarationIdentity.Trim();
            StateSlot = stateSlot;
            PortId = portId.Trim();
            ValueKind = valueKind;
            RequireRecord();
        }

        public ProgramGraphParameterDirection Direction { get; }
        public string ParameterName { get; }
        public string DeclarationIdentity { get; }
        public int StateSlot { get; }
        public string PortId { get; }
        public SemanticValueKind ValueKind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (StateSlot < 0)
                throw new ArgumentException("Program graph parameter binding is incomplete.");
            SimulationIdentity.Require(ParameterName, nameof(ParameterName));
            SimulationIdentity.Require(DeclarationIdentity, nameof(DeclarationIdentity));
            SimulationIdentity.Require(PortId, nameof(PortId));
        }
    }

    [ValueStorage]
    public sealed partial class ProgramGraphCallFrame
    {
        readonly ReadOnlyCollection<ProgramGraphParameterBinding> m_Inputs;
        readonly ReadOnlyCollection<ProgramGraphParameterBinding> m_Outputs;

        public ProgramGraphCallFrame(
            int index,
            string identity,
            OperationHandle ownerOperation,
            OperationHandle entryOperation,
            string childGraphIdentity,
            IEnumerable<ProgramGraphParameterBinding> inputs,
            IEnumerable<ProgramGraphParameterBinding> outputs)
        {
            Index = index;
            Identity = identity.Trim();
            OwnerOperation = ownerOperation;
            EntryOperation = entryOperation;
            ChildGraphIdentity = childGraphIdentity.Trim();
            m_Inputs = Freeze(inputs);
            m_Outputs = Freeze(outputs);
            RequireRecord();
        }

        public int Index { get; }
        public string Identity { get; }
        public OperationHandle OwnerOperation { get; }
        public OperationHandle EntryOperation { get; }
        public string ChildGraphIdentity { get; }
        public IReadOnlyList<ProgramGraphParameterBinding> Inputs => m_Inputs;
        public IReadOnlyList<ProgramGraphParameterBinding> Outputs => m_Outputs;

        static ReadOnlyCollection<ProgramGraphParameterBinding> Freeze(
            IEnumerable<ProgramGraphParameterBinding> source)
        {
            var values = new List<ProgramGraphParameterBinding>(source ?? Array.Empty<ProgramGraphParameterBinding>());
            values.Sort((left, right) =>
            {
                int byParameter = string.CompareOrdinal(left.ParameterName, right.ParameterName);
                return byParameter != 0 ? byParameter : string.CompareOrdinal(left.PortId, right.PortId);
            });
            return values.AsReadOnly();
        }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0 || !OwnerOperation.IsValid || !EntryOperation.IsValid)
                throw new ArgumentException("Program graph call frame identity is incomplete.");
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(ChildGraphIdentity, nameof(ChildGraphIdentity));
            RequireBindings(m_Inputs, ProgramGraphParameterDirection.Input, "input");
            RequireBindings(m_Outputs, ProgramGraphParameterDirection.Output, "output");
        }

        static void RequireBindings(IReadOnlyList<ProgramGraphParameterBinding> values,
            ProgramGraphParameterDirection direction, string label)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ports = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                ProgramGraphParameterBinding value = values[i];
                if (value.Direction != direction || !names.Add(value.ParameterName) || !ports.Add(value.PortId))
                    throw new ArgumentException($"Program graph call frame {label} bindings are duplicated or use the wrong direction.");
            }
        }
    }

    public static class ProgramGraphCallFrameContract
    {
        public static void ValidateCoverage(
            IReadOnlyList<OrchestrationOperationCode> operationCodes,
            IReadOnlyList<ProgramGraphCallFrame> frames)
        {
            if (operationCodes == null)
                throw new ArgumentNullException(nameof(operationCodes));
            if (frames == null)
                throw new ArgumentNullException(nameof(frames));
            var counts = new int[operationCodes.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                ProgramGraphCallFrame frame = frames[i]
                    ?? throw new ArgumentException("Graph call frame collection contains a missing frame.", nameof(frames));
                if (frame.OwnerOperation.Value < 0 || frame.OwnerOperation.Value >= operationCodes.Count ||
                    frame.EntryOperation.Value < 0 || frame.EntryOperation.Value >= operationCodes.Count)
                {
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' references an operation outside the table.");
                }
                Validate(
                    frame,
                    operationCodes[frame.OwnerOperation.Value],
                    operationCodes[frame.EntryOperation.Value]);
                counts[frame.OwnerOperation.Value]++;
            }
            for (int i = 0; i < operationCodes.Count; i++)
            {
                bool isSubGraph = operationCodes[i] == OrchestrationOperationCode.SubGraph;
                if (isSubGraph && counts[i] != 1)
                    throw new InvalidDataException($"SubGraph operation '{i}' requires exactly one graph call frame, received '{counts[i]}'.");
                if (!isSubGraph && counts[i] != 0)
                    throw new InvalidDataException($"Operation '{i}' is not a SubGraph but owns a graph call frame.");
            }
        }

        public static void Validate(
            ProgramGraphCallFrame frame,
            OrchestrationOperationCode ownerCode,
            OrchestrationOperationCode entryCode)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (ownerCode != OrchestrationOperationCode.SubGraph)
                throw new InvalidDataException($"Graph call frame '{frame.Identity}' owner operation must be SubGraph, received '{ownerCode}'.");
            if (entryCode != OrchestrationOperationCode.Root)
                throw new InvalidDataException($"Graph call frame '{frame.Identity}' entry operation must be Root, received '{entryCode}'.");
        }
    }

    public enum ProgramStateOwnerKind : byte
    {
        Runnable = 2,
        StateMachine = 3,
        Timeline = 4,
        Blackboard = 5
    }

    // 领域自有的状态语义使用未列出的数值声明，由领域的 ProgramStateSchema 校验。
    public enum ProgramStateSemantic : ushort
    {
        RunnableLifecycle = 1,
        RunnableChildCursor = 2,
        RunnableStopBarrier = 3,
        RunnableActivationGeneration = 4,
        StateMachineActive = 20,
        StateMachinePending = 21,
        StateMachineExiting = 22,
        StateMachineTransition = 23,
        StateMachineExecutionPath = 24,
        TimelinePlayback = 40,
        TimelineLoop = 41,
        BlackboardValue = 60,
        BlackboardOwnerToken = 61,
        BlackboardLifetime = 63,
        BlackboardWriteStamp = 64
    }

    public sealed partial class ProgramStateSlot
    {
        public ProgramStateSlot(
            int index,
            string identity,
            ProgramStateValueKind valueKind,
            ProgramStateOwnerKind ownerKind,
            ProgramStateSemantic semantic,
            string ownerIdentity,
            string stateCodecIdentity,
            int defaultConstantIndex)
        {
            Index = index;
            Identity = identity.Trim();
            ValueKind = valueKind;
            OwnerKind = ownerKind;
            Semantic = semantic;
            OwnerIdentity = ownerIdentity.Trim();
            DefaultConstantIndex = defaultConstantIndex;
            StateCodecIdentity = stateCodecIdentity.Trim();
            RequireRecord();
        }
        public int Index { get; }
        public string Identity { get; }
        public ProgramStateValueKind ValueKind { get; }
        public ProgramStateOwnerKind OwnerKind { get; }
        public ProgramStateSemantic Semantic { get; }
        public string OwnerIdentity { get; }
        public int DefaultConstantIndex { get; }
        public string StateCodecIdentity { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0 || DefaultConstantIndex < -1)
                throw new ArgumentOutOfRangeException();
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(OwnerIdentity, nameof(OwnerIdentity));
            SimulationIdentity.Require(StateCodecIdentity, nameof(StateCodecIdentity));
        }
    }

    public class ProgramStateSchema
    {
        public static readonly ProgramStateSchema Core = new ProgramStateSchema();

        public virtual string SemanticIdentity(ProgramStateSemantic semantic) => semantic.ToString();

        public virtual string CodecIdentity(ProgramStateValueKind kind)
        {
            return kind switch
            {
                ProgramStateValueKind.Boolean => "state.boolean/v1",
                ProgramStateValueKind.Int32 => "state.int32/v1",
                ProgramStateValueKind.UInt64 => "state.uint64/v1",
                ProgramStateValueKind.Scalar => "state.float32-scalar/v1",
                ProgramStateValueKind.Vector2 => "state.float32-vector2/v1",
                ProgramStateValueKind.Vector3 => "state.float32-vector3/v1",
                ProgramStateValueKind.Quaternion => "state.float32-quaternion/v1",
                ProgramStateValueKind.Enum => "state.enum/v1",
                ProgramStateValueKind.Yaw => "state.float32-yaw/v1",
                ProgramStateValueKind.Identity => "state.identity/v1",
                ProgramStateValueKind.BlackboardOwnerToken => "state.blackboard-owner-token/v1",
                ProgramStateValueKind.BlackboardWriteStamp => "state.blackboard-write-stamp/v3",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        public virtual bool HasDefaultValue(ProgramStateValueKind kind) =>
            kind != ProgramStateValueKind.BlackboardOwnerToken &&
            kind != ProgramStateValueKind.BlackboardWriteStamp;

        public virtual OrchestrationValueKind RuntimeKind(ProgramStateValueKind kind) => kind switch
        {
            ProgramStateValueKind.Boolean => OrchestrationValueKind.Boolean,
            ProgramStateValueKind.Int32 => OrchestrationValueKind.Int32,
            ProgramStateValueKind.UInt64 => OrchestrationValueKind.UInt64,
            ProgramStateValueKind.Scalar => OrchestrationValueKind.Scalar,
            ProgramStateValueKind.Vector2 => OrchestrationValueKind.Vector2,
            ProgramStateValueKind.Vector3 => OrchestrationValueKind.Vector3,
            ProgramStateValueKind.Yaw => OrchestrationValueKind.Yaw,
            ProgramStateValueKind.Identity => OrchestrationValueKind.Identity,
            ProgramStateValueKind.Quaternion => OrchestrationValueKind.Quaternion,
            ProgramStateValueKind.Enum => OrchestrationValueKind.Enum,
            _ => throw new InvalidOperationException($"状态类型 '{kind}' 没有通用运行值映射。")
        };

        public void RequireSlot(
            ProgramStateValueKind kind,
            ProgramStateOwnerKind owner,
            ProgramStateSemantic semantic)
        {
            if (!Accepts(kind, owner, semantic))
                throw new ArgumentException($"State semantic '{semantic}' cannot use kind '{kind}' with owner '{owner}'.");
        }

        protected virtual bool Accepts(
            ProgramStateValueKind kind,
            ProgramStateOwnerKind owner,
            ProgramStateSemantic semantic)
        {
            return semantic switch
            {
                ProgramStateSemantic.RunnableLifecycle => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.RunnableChildCursor => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.RunnableStopBarrier => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.RunnableActivationGeneration => kind == ProgramStateValueKind.UInt64 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.StateMachineActive => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachinePending => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachineExiting => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachineTransition => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachineExecutionPath => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.TimelinePlayback => kind == ProgramStateValueKind.UInt64 && owner == ProgramStateOwnerKind.Timeline,
                ProgramStateSemantic.TimelineLoop => kind == ProgramStateValueKind.Boolean && owner == ProgramStateOwnerKind.Timeline,
                ProgramStateSemantic.BlackboardValue => IsBlackboardValue(kind) && owner == ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardOwnerToken => kind == ProgramStateValueKind.BlackboardOwnerToken && owner == ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardLifetime => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardWriteStamp => kind == ProgramStateValueKind.BlackboardWriteStamp && owner == ProgramStateOwnerKind.Blackboard,
                _ => false
            };
        }

        protected virtual bool IsBlackboardValue(ProgramStateValueKind kind)
        {
            return kind == ProgramStateValueKind.Boolean ||
                   kind == ProgramStateValueKind.Int32 ||
                   kind == ProgramStateValueKind.UInt64 ||
                   kind == ProgramStateValueKind.Scalar ||
                   kind == ProgramStateValueKind.Vector2 ||
                   kind == ProgramStateValueKind.Vector3 ||
                   kind == ProgramStateValueKind.Yaw ||
                   kind == ProgramStateValueKind.Identity ||
                   kind == ProgramStateValueKind.Quaternion ||
                   kind == ProgramStateValueKind.Enum;
        }
    }

    public enum ProgramScopeKind : byte
    {
        Character = 1,
        Graph = 2,
        State = 3,
        ActionInstance = 4,
        Frame = 5
    }

    [ValueStorage]
    public sealed partial class ProgramScopeLayout
    {
        readonly ReadOnlyCollection<int> m_StateSlots;

        public ProgramScopeLayout(
            int compiledOwnerIndex,
            string identity,
            ProgramScopeKind kind,
            string ownerIdentity,
            OperationHandle ownerOperation,
            IEnumerable<int> stateSlots)
        {
            CompiledOwnerIndex = compiledOwnerIndex;
            Identity = identity.Trim();
            Kind = kind;
            OwnerIdentity = ownerIdentity.Trim();
            OwnerOperation = ownerOperation;
            var values = stateSlots == null ? new List<int>() : new List<int>(stateSlots);
            values.Sort();
            m_StateSlots = values.AsReadOnly();
            RequireRecord();
        }

        [ValueValidation]
        void RequireRecord()
        {
            if (CompiledOwnerIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(CompiledOwnerIndex));
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(OwnerIdentity, nameof(OwnerIdentity));
            for (int i = 0; i < m_StateSlots.Count; i++)
            {
                if (m_StateSlots[i] < 0 || i > 0 && m_StateSlots[i - 1] >= m_StateSlots[i])
                    throw new ArgumentException("Program scope state slots must be non-negative, sorted and unique.");
            }
        }

        public int CompiledOwnerIndex { get; }
        public string Identity { get; }
        public ProgramScopeKind Kind { get; }
        public string OwnerIdentity { get; }
        public OperationHandle OwnerOperation { get; }
        public IReadOnlyList<int> StateSlots => m_StateSlots;
    }

    public enum ProgramSourceTargetKind : byte
    {
        Operation = 1,
        Constant = 2,
        StateSlot = 3,
        Reference = 4,
        Producer = 5,
        CatalogEntry = 6,
        BodyMotion = 7,
        ControlModule = 8,
        ControlTransition = 10,
        OperationPort = 11,
        GraphInvocation = 12,
        OptimizedAway = 13
    }

    public enum ProgramValuePortDirection : byte
    {
        None,
        Input,
        Output
    }

    public enum ProgramInvocationCallerKind : byte
    {
        None,
        Node,
        Edge,
        TimelineClip,
        PresentationMarker,
        PresentationTreeClip,
        AbilityRule
    }

	public sealed partial class ProgramExecutionSource
	{
		public ProgramExecutionSource(
			ProgramSourceTargetKind targetKind,
			int targetIndex,
			string graphId,
			string nodeId,
			string portId,
			string edgeId,
			string declarationId,
			string timelineId,
			string trackId,
			string clipId,
			bool isInputPort,
			string graphInvocationPath = "",
			string compiledPortId = "",
			ProgramValuePortDirection valuePortDirection = ProgramValuePortDirection.None,
			string sourceInvocationPath = "",
			string parentInvocationPath = "",
			ProgramInvocationCallerKind invocationCallerKind = ProgramInvocationCallerKind.None,
			string invocationCallerId = "",
			string invocationCallerClipId = "",
			int invocationCallerOperationIndex = -1,
			int invocationEntryOperationIndex = -1)
		{
			TargetKind = targetKind;
			TargetIndex = targetIndex;
			GraphId = graphId ?? string.Empty;
			NodeId = nodeId ?? string.Empty;
			PortId = portId ?? string.Empty;
			EdgeId = edgeId ?? string.Empty;
			DeclarationId = declarationId ?? string.Empty;
			TimelineId = timelineId ?? string.Empty;
			TrackId = trackId ?? string.Empty;
			ClipId = clipId ?? string.Empty;
			IsInputPort = isInputPort;
			GraphInvocationPath = graphInvocationPath ?? string.Empty;
			CompiledPortId = compiledPortId ?? string.Empty;
			ValuePortDirection = valuePortDirection;
			SourceInvocationPath = sourceInvocationPath ?? string.Empty;
			ParentInvocationPath = parentInvocationPath ?? string.Empty;
			InvocationCallerKind = invocationCallerKind;
			InvocationCallerId = invocationCallerId ?? string.Empty;
			InvocationCallerClipId = invocationCallerClipId ?? string.Empty;
			InvocationCallerOperationIndex = invocationCallerOperationIndex;
			InvocationEntryOperationIndex = invocationEntryOperationIndex;
			RequireRecord();
		}

        [ValueValidation]
        void RequireRecord()
        {
            if (TargetIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(TargetIndex));
			if (GraphId.Length == 0 && TimelineId.Length == 0 && TargetKind != ProgramSourceTargetKind.BodyMotion)
				throw new ArgumentException("Source map entry requires a graph or timeline identity.");
        }

		public ProgramSourceTargetKind TargetKind { get; }
		public int TargetIndex { get; }
		public string GraphId { get; }
		public string NodeId { get; }
		public string PortId { get; }
		public string EdgeId { get; }
		public string DeclarationId { get; }
		public string TimelineId { get; }
		public string TrackId { get; }
		public string ClipId { get; }
		public bool IsInputPort { get; }
		public string GraphInvocationPath { get; }
		public string CompiledPortId { get; }
		public ProgramValuePortDirection ValuePortDirection { get; }
		public string SourceInvocationPath { get; }
		public string ParentInvocationPath { get; }
		public ProgramInvocationCallerKind InvocationCallerKind { get; }
		public string InvocationCallerId { get; }
		public string InvocationCallerClipId { get; }
		public int InvocationCallerOperationIndex { get; }
		public int InvocationEntryOperationIndex { get; }
	}

    public sealed class ProgramExecutionSourceLayout
    {
        public ProgramExecutionSourceLayout(IReadOnlyList<ProgramExecutionSource> entries)
        {
            Entries = entries;
        }

        public IReadOnlyList<ProgramExecutionSource> Entries { get; }
    }

    public sealed partial class ProgramSourceMapText
    {
        public ProgramSourceMapText(string sourceType, ReadOnlyMemory<string> displayPathSegments)
        {
            SourceType = sourceType;
            DisplayPathSegments = displayPathSegments;
        }

        public string SourceType { get; }
        public ReadOnlyMemory<string> DisplayPathSegments { get; }
    }

    [ValueStorage]
    public sealed partial class ProgramSourceMapEntry
    {
        public ProgramSourceMapEntry(ProgramExecutionSource source, ProgramSourceMapText text, string displayPath)
        {
            Source = source;
            Text = text;
            DisplayPath = displayPath;
        }

        public ProgramExecutionSource Source { get; }
        public ProgramSourceMapText Text { get; }
        public string SourceType => Text.SourceType;
        public string DisplayPath { get; }
    }

    public sealed class ProgramGraphSources
    {
        public ProgramGraphSources(ProgramExecutionSourceLayout layout, IReadOnlyList<ProgramSourceMapEntry> diagnostics,
            ProgramGraphSourceRevisionTable revisions)
        {
            Layout = layout;
            Diagnostics = diagnostics;
            Revisions = revisions;
        }

        public ProgramExecutionSourceLayout Layout { get; }
        public IReadOnlyList<ProgramSourceMapEntry> Diagnostics { get; }
        public ProgramGraphSourceRevisionTable Revisions { get; }
        public ProgramGraphSources WithRevisions(IEnumerable<ProgramGraphSourceRevision> revisions)
        {
            return new ProgramGraphSources(Layout, Diagnostics, new ProgramGraphSourceRevisionTable(revisions));
        }

        public static ProgramGraphSources Create(IEnumerable<ProgramSourceMapEntry> diagnostics,
            IEnumerable<ProgramGraphSourceRevision> revisions)
        {
            var entries = new List<ProgramSourceMapEntry>(diagnostics);
            entries.Sort(Compare);
            var sources = new ProgramExecutionSource[entries.Count];
            for (int i = 0; i < sources.Length; i++)
                sources[i] = entries[i].Source;
            return new ProgramGraphSources(new ProgramExecutionSourceLayout(Array.AsReadOnly(sources)),
                entries.AsReadOnly(), new ProgramGraphSourceRevisionTable(revisions));
        }

        static int Compare(ProgramSourceMapEntry left, ProgramSourceMapEntry right)
        {
            ProgramExecutionSource a = left.Source;
            ProgramExecutionSource b = right.Source;
            int result = ((byte)a.TargetKind).CompareTo((byte)b.TargetKind);
            if (result != 0) return result;
            result = a.TargetIndex.CompareTo(b.TargetIndex);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.GraphId, b.GraphId);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.GraphInvocationPath, b.GraphInvocationPath);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.NodeId, b.NodeId);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.PortId, b.PortId);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.CompiledPortId, b.CompiledPortId);
            if (result != 0) return result;
            return ((byte)a.ValuePortDirection).CompareTo((byte)b.ValuePortDirection);
        }
    }

    public readonly partial struct ProgramGraphSourceRevision
    {
        public ProgramGraphSourceRevision(string graphId, string revision)
        {
            GraphId = graphId;
            Revision = revision;
        }

        public string GraphId { get; }
        public string Revision { get; }
    }

    public sealed partial class ProgramGraphSourceRevisions
    {
        public ProgramGraphSourceRevisions(IReadOnlyList<ProgramGraphSourceRevision> sources) => Sources = sources;
        public IReadOnlyList<ProgramGraphSourceRevision> Sources { get; }
    }

    public sealed class ProgramGraphSourceRevisionTable
    {
        readonly Dictionary<string, string> m_Revisions = new(StringComparer.Ordinal);

        public ProgramGraphSourceRevisionTable(IEnumerable<ProgramGraphSourceRevision> sources)
        {
            var values = new List<ProgramGraphSourceRevision>(sources);
            values.Sort((left, right) => string.CompareOrdinal(left.GraphId, right.GraphId));
            foreach (ProgramGraphSourceRevision value in values)
                m_Revisions.Add(value.GraphId, value.Revision);
            Data = new ProgramGraphSourceRevisions(values.AsReadOnly());
        }

        internal ProgramGraphSourceRevisions Data { get; }
        public IReadOnlyList<ProgramGraphSourceRevision> Sources => Data.Sources;
        public string Require(string graphId) => m_Revisions[graphId];
    }

    public static class ProgramExecutionSourceCoverage
    {
        public static void Require(
            IReadOnlyList<ProgramExecutionSource> entries,
            int operationCount,
            int stateSlotCount)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (operationCount < 0)
                throw new ArgumentOutOfRangeException(nameof(operationCount));
            if (stateSlotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(stateSlotCount));

            var operations = new bool[operationCount];
            var stateSlots = new bool[stateSlotCount];
            var invocations = new Dictionary<string, ProgramExecutionSource>(StringComparer.Ordinal);
            var invocationSources = new Dictionary<string, List<ProgramExecutionSource>>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                ProgramExecutionSource entry = entries[i] ??
                    throw new InvalidDataException("Program source map contains a missing entry.");
                switch (entry.TargetKind)
                {
                    case ProgramSourceTargetKind.Operation:
                        RequireTarget(entry.TargetIndex, operations, "operation");
                        break;
                    case ProgramSourceTargetKind.OperationPort:
                        if (entry.TargetIndex >= operationCount || string.IsNullOrEmpty(entry.GraphId) ||
                            string.IsNullOrEmpty(entry.NodeId) || string.IsNullOrEmpty(entry.PortId) ||
                            string.IsNullOrEmpty(entry.SourceInvocationPath) ||
                            !Enum.IsDefined(typeof(ProgramValuePortDirection), entry.ValuePortDirection) ||
                            (entry.ValuePortDirection == ProgramValuePortDirection.None) != string.IsNullOrEmpty(entry.CompiledPortId))
                            throw new InvalidDataException("Program operation-port source is incomplete.");
                        break;
                    case ProgramSourceTargetKind.GraphInvocation:
                        bool timelineCaller = entry.InvocationCallerKind == ProgramInvocationCallerKind.TimelineClip ||
                            entry.InvocationCallerKind == ProgramInvocationCallerKind.PresentationMarker ||
                            entry.InvocationCallerKind == ProgramInvocationCallerKind.PresentationTreeClip;
                        if (entry.TargetIndex >= operationCount || entry.InvocationEntryOperationIndex < 0 ||
                            entry.InvocationEntryOperationIndex >= operationCount || string.IsNullOrEmpty(entry.GraphId) ||
                            string.IsNullOrEmpty(entry.GraphInvocationPath) || string.IsNullOrEmpty(entry.SourceInvocationPath) ||
                            !invocations.TryAdd(entry.GraphInvocationPath, entry) ||
                            !Enum.IsDefined(typeof(ProgramInvocationCallerKind), entry.InvocationCallerKind) ||
                            (entry.InvocationCallerKind == ProgramInvocationCallerKind.None) != string.IsNullOrEmpty(entry.ParentInvocationPath) ||
                            (entry.InvocationCallerKind == ProgramInvocationCallerKind.None) != string.IsNullOrEmpty(entry.InvocationCallerId) ||
                            timelineCaller != !string.IsNullOrEmpty(entry.InvocationCallerClipId) ||
                            timelineCaller && (entry.InvocationCallerOperationIndex < 0 ||
                                entry.InvocationCallerOperationIndex >= operationCount))
                            throw new InvalidDataException("Program graph invocation source is incomplete.");
                        if (!invocationSources.TryGetValue(entry.SourceInvocationPath, out List<ProgramExecutionSource> sourceInvocations))
                        {
                            sourceInvocations = new List<ProgramExecutionSource>();
                            invocationSources.Add(entry.SourceInvocationPath, sourceInvocations);
                        }
                        sourceInvocations.Add(entry);
                        break;
                    case ProgramSourceTargetKind.OptimizedAway:
                        if (string.IsNullOrEmpty(entry.GraphId) ||
                            string.IsNullOrEmpty(entry.NodeId))
                            throw new InvalidDataException("Program optimized source is incomplete.");
                        break;
                    case ProgramSourceTargetKind.StateSlot:
                        RequireTarget(entry.TargetIndex, stateSlots, "state slot");
                        break;
                }
            }

            foreach (KeyValuePair<string, List<ProgramExecutionSource>> source in invocationSources)
            {
                string parent = source.Value[0].ParentInvocationPath;
                for (int i = 1; i < source.Value.Count; i++)
                    if (!string.Equals(parent, source.Value[i].ParentInvocationPath, StringComparison.Ordinal))
                        throw new InvalidDataException("Program graph invocation hierarchy is invalid.");
                var visited = new HashSet<string>(StringComparer.Ordinal) { source.Key };
                while (!string.IsNullOrEmpty(parent))
                {
                    if (!visited.Add(parent) || !invocationSources.TryGetValue(parent, out List<ProgramExecutionSource> owners))
                        throw new InvalidDataException("Program graph invocation hierarchy is invalid.");
                    parent = owners[0].ParentInvocationPath;
                }
            }

            for (int i = 0; i < operations.Length; i++)
            {
                if (!operations[i])
                    throw new InvalidDataException($"Program operation '{i}' is absent from the source map.");
            }
            for (int i = 0; i < stateSlots.Length; i++)
            {
                if (!stateSlots[i])
                    throw new InvalidDataException($"Program state slot '{i}' is absent from the source map.");
            }
        }

        static void RequireTarget(int index, bool[] targets, string kind)
        {
            if ((uint)index >= (uint)targets.Length)
                throw new InvalidDataException($"Program source map {kind} target '{index}' is outside the Program.");
            if (targets[index])
                throw new InvalidDataException($"Program source map {kind} target '{index}' is duplicated.");
            targets[index] = true;
        }
    }

    public enum ProgramBlackboardLifetime : byte
    {
        Config = 0,
        Spawn = 1,
        StateEnterToExit = 2,
        ActionInstance = 3,
        Frame = 4,
        ManualClear = 5,
        GraphInstance = 6
    }
}
