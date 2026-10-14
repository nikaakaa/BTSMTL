using ProgramValues = ThirdPersonSimulation.ProgramValueCalculation<ThirdPersonSimulation.AbilityStateValue, ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32SimulationMath, ThirdPersonSimulation.Float32ProgramNumericValueAccess>;
using System;
using static ThirdPersonSimulation.Float32GraphValueRuntime;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public readonly struct Float32PresentationGraphFacts
    {
        public Float32PresentationGraphFacts(ulong renderFrame, Float32Vector3 position,
            Float32Vector3 velocity, Float32Yaw yaw, bool grounded, ulong branchRevision)
        {
            if (renderFrame == 0)
                throw new ArgumentOutOfRangeException(nameof(renderFrame));
            if (branchRevision == 0)
                throw new ArgumentOutOfRangeException(nameof(branchRevision));
            RenderFrame = renderFrame;
            Position = position;
            Velocity = velocity;
            Yaw = yaw;
            Grounded = grounded;
            BranchRevision = branchRevision;
        }

        public ulong RenderFrame { get; }
        public Float32Vector3 Position { get; }
        public Float32Vector3 Velocity { get; }
        public Float32Yaw Yaw { get; }
        public bool Grounded { get; }
        public ulong BranchRevision { get; }

        internal AbilityStateValue ReadCharacterState(string field) => field switch
        {
            CharacterStateProviderFields.Position => AbilityStateValue.FromVector3(Position),
            CharacterStateProviderFields.Velocity => AbilityStateValue.FromVector3(Velocity),
            CharacterStateProviderFields.VerticalVelocity => AbilityStateValue.FromScalar(Velocity.Y),
            CharacterStateProviderFields.BodyYaw => AbilityStateValue.FromYaw(Yaw),
            CharacterStateProviderFields.Grounded => AbilityStateValue.FromBoolean(Grounded),
            _ => throw new InvalidOperationException($"Presentation Character State field '{field}' is unsupported.")
        };
    }

    public readonly struct Float32PresentationGraphOutputIdentity
    {
        public Float32PresentationGraphOutputIdentity(
            EventId eventId,
            string treeGraphId,
            string treeGraphRevision,
            string nodeAuthoringId,
            ulong playbackGeneration,
            ulong branchRevision)
        {
            if (!eventId.IsValid)
                throw new ArgumentException("Presentation output EventId is invalid.", nameof(eventId));
            TreeGraphId = SimulationIdentity.Require(treeGraphId, nameof(treeGraphId));
            TreeGraphRevision = SimulationIdentity.Require(treeGraphRevision, nameof(treeGraphRevision));
            NodeAuthoringId = SimulationIdentity.Require(nodeAuthoringId, nameof(nodeAuthoringId));
            if (playbackGeneration == 0)
                throw new ArgumentOutOfRangeException(nameof(playbackGeneration));
            if (branchRevision == 0)
                throw new ArgumentOutOfRangeException(nameof(branchRevision));
            EventId = eventId;
            PlaybackGeneration = playbackGeneration;
            BranchRevision = branchRevision;
        }

        public EventId EventId { get; }
        public string TreeGraphId { get; }
        public string TreeGraphRevision { get; }
        public string NodeAuthoringId { get; }
        public ulong PlaybackGeneration { get; }
        public ulong BranchRevision { get; }
    }

    public interface IFloat32PresentationGraphOutput
    {
        bool CaptureGraphTrace { get; }
        bool CaptureValueTrace { get; }
        void TraceOperation(OperationHandle operation, string code, string detail, ulong generation);
        void TraceEdge(ProgramControlFlowEdge edge, bool selected, bool passed);
        void TraceValue(OperationHandle operation, string port, AbilityStateValue value, bool input);
        void SubmitCamera(in Float32PresentationGraphOutputIdentity identity, string producer,
            in PresentationCameraRequest activation,
            in PresentationCameraRequest retirement, bool retiring);
        void SubmitVfx(in Float32PresentationGraphOutputIdentity identity, string producer,
            in PresentationVfxRequest request);
        void SubmitAudio(in Float32PresentationGraphOutputIdentity identity, string producer,
            in PresentationAudioRequest request);
    }

    public sealed class Float32PresentationGraphRuntime
    {
        readonly struct BindingKey : IEquatable<BindingKey>
        {
            readonly CanonicalTextValue m_ParentSourcePath;
            readonly CanonicalTextValue m_TimelineNodeId;
            readonly CanonicalTextValue m_CallerId;
            readonly CanonicalTextValue m_GraphId;
            readonly CanonicalTextValue m_Revision;
            readonly ProgramInvocationCallerKind m_CallerKind;
            readonly AbilityTreeClipHook m_Hook;

            internal BindingKey(CanonicalTextValue parentSourcePath, CanonicalTextValue timelineNodeId, CanonicalTextValue callerId,
                CanonicalTextValue graphId, CanonicalTextValue revision, ProgramInvocationCallerKind callerKind, AbilityTreeClipHook hook)
            {
                m_ParentSourcePath = parentSourcePath;
                m_TimelineNodeId = timelineNodeId;
                m_CallerId = callerId;
                m_GraphId = graphId;
                m_Revision = revision;
                m_CallerKind = callerKind;
                m_Hook = hook;
            }

            public bool Equals(BindingKey other) =>
                m_ParentSourcePath.Equals(other.m_ParentSourcePath) &&
                m_TimelineNodeId.Equals(other.m_TimelineNodeId) &&
                m_CallerId.Equals(other.m_CallerId) &&
                m_GraphId.Equals(other.m_GraphId) &&
                m_Revision.Equals(other.m_Revision) &&
                m_CallerKind == other.m_CallerKind && m_Hook == other.m_Hook;

            public override bool Equals(object obj) => obj is BindingKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                m_ParentSourcePath, m_TimelineNodeId, m_CallerId, m_GraphId, m_Revision, (int)m_CallerKind, (int)m_Hook);
        }

        readonly Float32GameplayAbilityExecutionData m_Data;
        readonly Dictionary<CanonicalTextValue, string> m_InvocationSources = new();
        readonly Dictionary<BindingKey, int> m_Bindings;
        readonly GameplayAbilityExecutionLayout m_Layout;
        readonly ProgramExecutionSource[] m_Entries;
        readonly AbilityStateValue[] m_Defaults;
        readonly AbilityStateValue[] m_State;
        readonly int[] m_ResetStateSlots;
        readonly ProgramExecutionSource[] m_OutputSources;
        readonly string[] m_OutputSourceRevisions;
        readonly string[][] m_InputPortNames;
        readonly string[][] m_OutputPortNames;
        readonly bool[] m_Parameters;
        readonly Values m_Values;
        readonly OperationControlRuntime<Target> m_Control;
        Float32PresentationGraphFacts m_Facts;
        bool m_Executing;
        bool m_CaptureGraphTrace;
        bool m_CaptureValueTrace;
        IFloat32PresentationGraphOutput m_Output;
        bool m_Retiring;
        bool m_ExitRequested;
        bool m_CanRequestExit;
        ulong m_PlaybackGeneration;
        ulong m_BranchRevision;
        readonly AbilityTreeClipHook[] m_Hooks;
        readonly string[] m_Producers;
        readonly PresentationCameraRequest[] m_Activations;
        readonly PresentationCameraRequest[] m_Retirements;
        readonly PresentationVfxRequest[] m_VfxRequests;
        readonly PresentationAudioRequest[] m_AudioRequests;

        public Float32PresentationGraphRuntime(Float32GameplayAbilityExecutionData data
#if UNITY_EDITOR
            , IProgramValuePortInputs<AbilityStateValue> ports
#endif
            )
        {
            m_Data = data ?? throw new ArgumentNullException(nameof(data));
            m_Layout = data.ExecutionLayout;
            var entries = new List<ProgramExecutionSource>();
            foreach (ProgramExecutionSource source in data.ExecutionSources)
            {
                if (source.TargetKind != ProgramSourceTargetKind.GraphInvocation)
                    continue;
                m_InvocationSources.Add(source.GraphInvocationPath, source.SourceInvocationPath);
                if (source.InvocationCallerKind == ProgramInvocationCallerKind.PresentationMarker ||
                    source.InvocationCallerKind == ProgramInvocationCallerKind.PresentationTreeClip)
                    entries.Add(source);
            }
            if (entries.Count == 0)
                throw new InvalidOperationException("The program contains no Presentation graph entry.");
            m_Entries = entries.ToArray();
            m_Bindings = new Dictionary<BindingKey, int>(m_Entries.Length);
            m_Hooks = new AbilityTreeClipHook[m_Entries.Length];
            m_Producers = new string[data.Operations.Count];
            m_OutputSources = new ProgramExecutionSource[data.Operations.Count];
            m_OutputSourceRevisions = new string[data.Operations.Count];
            m_InputPortNames = new string[data.Operations.Count][];
            m_OutputPortNames = new string[data.Operations.Count][];
            m_Activations = new PresentationCameraRequest[data.Operations.Count];
            m_Retirements = new PresentationCameraRequest[data.Operations.Count];
            m_VfxRequests = new PresentationVfxRequest[data.Operations.Count];
            m_AudioRequests = new PresentationAudioRequest[data.Operations.Count];
            m_Defaults = new AbilityStateValue[data.StateSlots.Count];
            m_State = new AbilityStateValue[data.StateSlots.Count];
            m_Parameters = new bool[data.StateSlots.Count];
            foreach (ProgramGraphCallFrame frame in data.GraphCallFrames)
            {
                foreach (ProgramGraphParameterBinding parameter in frame.Inputs)
                    PrepareParameter(parameter.StateSlot);
                foreach (ProgramGraphParameterBinding parameter in frame.Outputs)
                    PrepareParameter(parameter.StateSlot);
            }
            var visited = new byte[data.Operations.Count];
            for (int index = 0; index < m_Entries.Length; index++)
            {
                ProgramExecutionSource entry = m_Entries[index];
                SimulationOperation entryOperation = m_Layout.Operation(new OperationHandle(entry.TargetIndex));
                m_Hooks[index] = entryOperation.Code == SimulationOperationCode.Root
                    ? AbilityTreeClipHook.Root : entryOperation.Code == SimulationOperationCode.TimelineEnter
                        ? (AbilityTreeClipHook)entryOperation.Integer0
                        : throw new InvalidOperationException("Presentation graph binding must target a lifecycle entry.");
                bool marker = entry.InvocationCallerKind == ProgramInvocationCallerKind.PresentationMarker;
                if (marker && m_Hooks[index] != AbilityTreeClipHook.OnEnable)
                    throw new InvalidOperationException("Presentation Marker requires OnEnable.");
                string caller = marker ? entry.InvocationCallerId
                    : entry.InvocationCallerId.Substring(0, entry.InvocationCallerId.LastIndexOf('/'));
                var key = new BindingKey(entry.ParentInvocationPath, caller, entry.InvocationCallerClipId,
                    "tree:" + entry.GraphId, data.GraphSourceRevisions.Require(entry.GraphId), entry.InvocationCallerKind, m_Hooks[index]);
                if (!m_Bindings.TryAdd(key, index))
                    m_Bindings[key] = -1;
                PrepareOperation(new OperationHandle(entry.TargetIndex), visited);
            }
            var resetSlots = new HashSet<int>();
            for (int slot = 0; slot < m_Parameters.Length; slot++)
                if (m_Parameters[slot])
                    resetSlots.Add(slot);
            for (int operation = 0; operation < visited.Length; operation++)
                if (visited[operation] == 2)
                    foreach (int slot in data.Operations[operation].StateSlots)
                        resetSlots.Add(slot);
            m_ResetStateSlots = new int[resetSlots.Count];
            resetSlots.CopyTo(m_ResetStateSlots);
            Array.Sort(m_ResetStateSlots);
            m_Values = new Values(this, new Float32GraphValueWorkspace(data)
#if UNITY_EDITOR
                , ports
#endif
                );
            m_Control = new OperationControlRuntime<Target>(data.Topology, new Target(this),
                checked(Math.Max(1024, data.Operations.Count * 128)));
            m_Control.PrepareStateExecutionText(new CanonicalTextStorage(0, 0), 0);
        }

#if UNITY_EDITOR
        public Float32GameplayAbilityExecutionData Data => m_Data;

#endif

        void SetState(int slot, AbilityStateValue value)
        {
            value.Retain();
            m_State[slot].Release();
            m_State[slot] = value;
        }

        public void Dispose()
        {
            for (int i = 0; i < m_State.Length; i++)
                m_State[i].Release();
            Array.Clear(m_State, 0, m_State.Length);
            m_Control.Dispose();
        }

        public CharacterSkillId AbilityId => m_Data.AbilityId;
        public int EntryCount => m_Entries.Length;
        public ProgramExecutionSource Entry(int binding) => m_Entries[binding];
        public bool TryBind(CanonicalTextValue parentInvocationPath, CanonicalTextValue timelineNodeId, CanonicalTextValue markerId, CanonicalTextValue graphId, CanonicalTextValue revision,
            ProgramInvocationCallerKind callerKind, AbilityTreeClipHook hook, out int binding)
        {
            binding = -1;
            if (!m_InvocationSources.TryGetValue(parentInvocationPath, out string parentSourcePath))
                return false;
            var key = new BindingKey(parentSourcePath, timelineNodeId, markerId, graphId, revision, callerKind, hook);
            if (!m_Bindings.TryGetValue(key, out int match))
                return false;
            if (match < 0)
                throw new InvalidOperationException("Presentation Marker graph binding is ambiguous.");
            binding = match;
            return true;
        }

        public bool Evaluate(int binding, in Float32PresentationGraphFacts facts, ulong playbackGeneration,
            IFloat32PresentationGraphOutput output)
        {
            if (m_Executing)
                throw new InvalidOperationException("Presentation graph evaluation is already active.");
            if (facts.RenderFrame == 0)
                throw new InvalidOperationException("Presentation Marker requires its current read-only fact frame.");
            ProgramExecutionSource entry = m_Entries[binding];
            m_Facts = facts;
            m_Output = output ?? throw new ArgumentNullException(nameof(output));
            m_CaptureGraphTrace = output.CaptureGraphTrace;
            m_CaptureValueTrace = output.CaptureValueTrace;
            m_Retiring = m_Hooks[binding] == AbilityTreeClipHook.OnDisable || m_Hooks[binding] == AbilityTreeClipHook.OnDestroy;
            m_ExitRequested = false;
            m_CanRequestExit = entry.InvocationCallerKind == ProgramInvocationCallerKind.PresentationTreeClip && !m_Retiring;
            m_PlaybackGeneration = playbackGeneration;
            m_BranchRevision = facts.BranchRevision;
            m_Executing = true;
            for (int i = 0; i < m_ResetStateSlots.Length; i++)
            {
                int slot = m_ResetStateSlots[i];
                SetState(slot, m_Defaults[slot]);
            }
            try
            {
                m_Values.BeginEvaluation();
                m_Control.BeginEvaluation();
                OperationExecutionResult result = OperationControlRuntime.Execute(m_Control,
                    new GraphExecutionEntry(new OperationHandle(entry.TargetIndex), GraphExecutionContract.Resumable));
                if (result != OperationExecutionResult.Success && result != OperationExecutionResult.Failure)
                    throw new InvalidOperationException($"Presentation Marker '{entry.InvocationCallerClipId}' did not complete its OnEnable graph: {result}.");
                return m_ExitRequested;
            }
            finally
            {
                m_Output = null;
                m_CaptureGraphTrace = false;
                m_CaptureValueTrace = false;
                m_Facts = default;
                m_PlaybackGeneration = 0;
                m_BranchRevision = 0;
                m_Executing = false;
            }
        }

        void PrepareParameter(int slot)
        {
            m_Parameters[slot] = true;
            m_Defaults[slot] = InitialValue(m_Data.StateSlots[slot]);
        }

        AbilityStateValue InitialValue(ProgramStateSlot slot) => slot.DefaultConstantIndex >= 0
            ? Values.FromConstant(m_Data.Constants[slot.DefaultConstantIndex])
            : AbilityStateValue.Default(slot.ValueKind);

        void PrepareOperation(OperationHandle handle, byte[] visited)
        {
            if (visited[handle.Value] == 1)
                throw new InvalidOperationException("Presentation graph contains recursive operations.");
            if (visited[handle.Value] == 2)
                return;
            visited[handle.Value] = 1;
            SimulationOperation operation = m_Layout.Operation(handle);
            switch (operation.Code)
            {
                case SimulationOperationCode.TimelineClipExitRequest:
                case SimulationOperationCode.Root:
                case SimulationOperationCode.Sequence:
                case SimulationOperationCode.Selector:
                case SimulationOperationCode.Parallel:
                case SimulationOperationCode.Succeed:
                case SimulationOperationCode.SubGraph:
                case SimulationOperationCode.ConditionResult:
                case SimulationOperationCode.Compare:
                case SimulationOperationCode.And:
                case SimulationOperationCode.Or:
                case SimulationOperationCode.Not:
                case SimulationOperationCode.Constant:
                    break;
                case SimulationOperationCode.TimelineEnter:
                    break;
                case SimulationOperationCode.CameraStateRequest:
                case SimulationOperationCode.CameraEffectRequest:
                case SimulationOperationCode.CameraResponse:
                case SimulationOperationCode.CameraTarget:
                case SimulationOperationCode.VfxRequest:
                case SimulationOperationCode.AudioRequest:
                    ProgramReference producer = m_Layout.Topology.FirstReference(handle, ProgramReferenceKind.Producer);
                    if (producer == null || CanonicalTextValue.IsNullOrWhiteSpace(producer.ExternalIdentity))
                        throw new InvalidOperationException("Presentation output operation requires its compiled producer identity.");
                    m_OutputSources[handle.Value] = RequireOperationSource(handle);
                    m_OutputSourceRevisions[handle.Value] = m_Data.GraphSourceRevisions.Require(m_OutputSources[handle.Value].GraphId);
                    m_Producers[handle.Value] = producer.ExternalIdentity;
                    if (operation.Code == SimulationOperationCode.VfxRequest)
                    {
                        m_VfxRequests[handle.Value] = BuildVfxRequest(operation);
                        break;
                    }
                    if (operation.Code == SimulationOperationCode.AudioRequest)
                    {
                        m_AudioRequests[handle.Value] = BuildAudioRequest(operation);
                        break;
                    }
                    m_Activations[handle.Value] = CameraProgramRequestFactory.Build(operation.Code, operation.Integer1,
                        operation.Flags, PresentationCameraRequestLifecycle.Activate, new Float32CameraProgramConstantReader(m_Layout, handle));
                    m_Retirements[handle.Value] = CameraProgramRequestFactory.Build(operation.Code, operation.Integer1,
                        operation.Flags, PresentationCameraRequestLifecycle.Retire, new Float32CameraProgramConstantReader(m_Layout, handle));
                    break;
                case SimulationOperationCode.CharacterStateRead:
                    if (!CharacterStateProviderFields.IsValid(operation.Text0))
                        throw new InvalidOperationException($"Presentation Character State field '{operation.Text0}' is unsupported.");
                    break;
                case SimulationOperationCode.BlackboardGet:
                case SimulationOperationCode.BlackboardSet:
                    ParameterSlot(operation);
                    break;
                default:
                    throw new InvalidOperationException($"Operation '{m_Layout.SourcePath(handle)}' ({operation.Code}) cannot execute in a Presentation graph.");
            }
            OperationValuePortContract ports = GameplayAbilityValuePortContracts.Require(operation.Code, handle, m_Data.GraphCallFrames);
            var inputNames = ports.Inputs.Count == 0 ? Array.Empty<string>() : new string[ports.Inputs.Count];
            for (int i = 0; i < inputNames.Length; i++)
                inputNames[i] = ports.Inputs[i].Identity;
            m_InputPortNames[handle.Value] = inputNames;
            var outputNames = ports.Outputs.Count == 0 ? Array.Empty<string>() : new string[ports.Outputs.Count];
            for (int i = 0; i < outputNames.Length; i++)
                outputNames[i] = ports.Outputs[i].Identity;
            m_OutputPortNames[handle.Value] = outputNames;
            foreach (int slot in operation.StateSlots)
                m_Defaults[slot] = InitialValue(m_Data.StateSlots[slot]);
            ReadOnlySpan<CompiledValueInputBinding> inputs = m_Layout.ValueLayout.ValueInputs(handle);
            for (int index = 0; index < inputs.Length; index++)
                if (inputs[index].SourceKind == CompiledValueInputSourceKind.Operation)
                    PrepareOperation(inputs[index].SourceOperation, visited);
            PrepareEdges(handle, ProgramControlFlowKind.Child, visited);
            PrepareEdges(handle, ProgramControlFlowKind.Enter, visited);
            visited[handle.Value] = 2;
        }

        void PrepareEdges(OperationHandle operation, ProgramControlFlowKind kind, byte[] visited)
        {
            foreach (ProgramControlFlowEdge edge in m_Layout.Outgoing(operation, kind))
            {
                if (edge.HasCondition)
                    PrepareOperation(edge.Condition, visited);
                PrepareOperation(edge.Target, visited);
            }
        }

        int ParameterSlot(SimulationOperation operation)
        {
            ProgramReference reference = m_Layout.Topology.FirstReference(operation.Handle, ProgramReferenceKind.StateSlot);
            if (reference == null || !m_Parameters[reference.TargetIndex])
                throw new InvalidOperationException($"Presentation graph '{m_Layout.SourcePath(operation.Handle)}' cannot access Gameplay blackboard state.");
            return reference.TargetIndex;
        }

        ProgramExecutionSource RequireOperationSource(OperationHandle operation)
        {
            var sources = m_Layout.OperationSources(operation);
            if (sources.Count > 1)
                throw new InvalidOperationException($"Presentation operation '{operation.Value}' has multiple source entries.");
            if (sources.Source == null)
                throw new InvalidOperationException($"Presentation operation '{operation.Value}' has no source entry.");

            return sources.Source;
        }

        public void VisitVfxBindings(Action<string, string, string> visit)
        {
            for (int i = 0; i < m_Data.Operations.Count; i++)
            {
                SimulationOperation operation = m_Data.Operations[i];
                if (operation.Code == SimulationOperationCode.VfxRequest)
                    visit(m_Layout.SourcePath(operation.Handle),
                        m_Layout.FindNamedConstant(operation.Handle, OperationNamedConstant.ResourceId).Text,
                        operation.Integer1 == (int)PresentationVfxCommand.Play ||
                        operation.Integer1 == (int)PresentationVfxCommand.MaintainStateModifier
                            ? m_Layout.FindNamedConstant(operation.Handle, OperationNamedConstant.AnchorKey).Text
                            : string.Empty);
            }
        }

        PresentationVfxRequest BuildVfxRequest(SimulationOperation operation)
        {
            OperationHandle handle = operation.Handle;
            return new PresentationVfxRequest(
                (PresentationVfxCommand)operation.Integer1,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.RequestId).Text,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.ResourceId).Text,
                (PresentationVfxTarget)m_Layout.FindNamedConstant(handle, OperationNamedConstant.VfxTarget).Int32,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.AnchorKey).Text,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.HasPoseOverride).Boolean,
                (PresentationVfxCoordinatesOption)m_Layout.FindNamedConstant(handle, OperationNamedConstant.CoordinatesOption).Int32,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.UseRotationY).Boolean,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.UseRotationXZ).Boolean,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.NeedRaycastGround).Boolean,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.UseAttachPointRotation).Boolean);
        }

        PresentationAudioRequest BuildAudioRequest(SimulationOperation operation)
        {
            OperationHandle handle = operation.Handle;
            return new PresentationAudioRequest(
                (PresentationAudioCommand)operation.Integer0, operation.Integer1,
                (PresentationAudioTarget)m_Layout.FindNamedConstant(handle, OperationNamedConstant.AudioTarget).Int32,
                (PresentationAudioLifetime)m_Layout.FindNamedConstant(handle, OperationNamedConstant.AudioLifetime).Int32,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.AudioSoundSlot).Int32,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.AudioParameterSlot).Int32,
                m_Layout.FindNamedConstant(handle, OperationNamedConstant.AudioParameterValue).Scalar.ToSingle());
        }

        Float32PresentationGraphOutputIdentity CreateOutputIdentity(SimulationOperation operation)
        {
            ProgramExecutionSource source = m_OutputSources[operation.Handle.Value];
            string sourceRevision = m_OutputSourceRevisions[operation.Handle.Value];
            Span<byte> block = stackalloc byte[64];
            var builder = new EventIdBuilder(block);
            builder.Append("presentation-treeclip-output");
            builder.Append(source.GraphId);
            builder.Append(sourceRevision);
            builder.Append(source.NodeId);
            builder.Append(m_PlaybackGeneration);
            builder.Append(m_BranchRevision);
            return new Float32PresentationGraphOutputIdentity(
                builder.Build(),
                source.GraphId,
                sourceRevision,
                source.NodeId,
                m_PlaybackGeneration,
                m_BranchRevision);
        }

        void ResetOperation(OperationExecutionDescriptor operation)
        {
            for (int index = 0; index < operation.StateSlots.Count; index++)
            {
                int slot = operation.StateSlots[index];
                if (m_Data.StateSlots[slot].Semantic != ProgramStateSemantic.RunnableActivationGeneration)
                    SetState(slot, m_Defaults[slot]);
            }
        }

        sealed class Values : Float32OperationValueRuntime
        {
            readonly Float32PresentationGraphRuntime m_Owner;
            protected override bool IsAvailabilityQuery => false;
            protected override bool CaptureValues => m_Owner.m_CaptureValueTrace;
            protected override void TraceObservedValue(SimulationOperation operation, in OperationValueObservationPort port,
                AbilityStateValue value) => m_Owner.m_Output.TraceValue(operation.Handle, port.Identity, value,
                    port.Direction == ProgramValuePortDirection.Input);
            protected override AbilityStateValue ReadDefaultState(int slot) => m_Owner.m_Defaults[slot];

            internal Values(Float32PresentationGraphRuntime owner, Float32GraphValueWorkspace workspace
#if UNITY_EDITOR
                , IProgramValuePortInputs<AbilityStateValue> ports
#endif
                )
                : base(owner.m_Data, owner.m_Layout, workspace
#if UNITY_EDITOR
                    , ports
#endif
                    ) => m_Owner = owner;

            internal static AbilityStateValue FromConstant(ProgramConstant constant) => ValueFromConstant(constant);

            protected override AbilityStateValue EvaluateDomainValue<TTarget>(OperationControlCursor<TTarget> cursor,
                SimulationOperation operation, string outputPort, Float32ValueInputLease inputs)
            {
                return operation.Code switch
                {
                    SimulationOperationCode.BlackboardGet => m_Owner.m_State[m_Owner.ParameterSlot(operation)],
                    SimulationOperationCode.CharacterStateRead => m_Owner.m_Facts.ReadCharacterState(operation.Text0),
                    _ => throw new InvalidOperationException($"Presentation value operation '{operation.Code}' has no bound read capability.")
                };
            }

            internal void SubmitVfx<TTarget>(OperationControlCursor<TTarget> cursor, SimulationOperation operation)
                where TTarget : struct, IOperationControlTarget<TTarget>
            {
                using Float32ValueInputLease inputs = ReadInputs(cursor, operation);
                Float32Vector3 position = inputs[0].Vector3;
                Float32Vector3 euler = inputs[1].Vector3;
                Float32Vector3 scale = inputs[2].Vector3;
                PresentationVfxRequest request = m_Owner.m_VfxRequests[operation.Handle.Value].WithPose(
                    position.X.ToSingle(), position.Y.ToSingle(), position.Z.ToSingle(),
                    euler.X.ToSingle(), euler.Y.ToSingle(), euler.Z.ToSingle(),
                    scale.X.ToSingle(), scale.Y.ToSingle(), scale.Z.ToSingle());
                m_Owner.m_Output.SubmitVfx(m_Owner.CreateOutputIdentity(operation),
                    m_Owner.m_Producers[operation.Handle.Value], in request);
            }

            internal void WriteParameter<TTarget>(OperationControlCursor<TTarget> cursor, SimulationOperation operation)
                where TTarget : struct, IOperationControlTarget<TTarget>
            {
                int slot = m_Owner.ParameterSlot(operation);
                using Float32ValueInputLease inputs = ReadInputs(cursor, operation);
                if (inputs.Count != 1)
                    throw new InvalidOperationException("Macro parameter write requires exactly one value.");
                m_Owner.SetState(slot, ConvertValue(inputs[0], m_Ability.StateSlots[slot].ValueKind));
            }

            protected override void ResetGraphCallParameter(int slot) => m_Owner.SetState(slot, m_Owner.m_Defaults[slot]);
            protected override void WriteGraphCallParameter(int slot, AbilityStateValue value) => m_Owner.SetState(slot, value);
            protected override AbilityStateValue ReadGraphCallParameter(int slot) => m_Owner.m_State[slot];
            protected override void TraceResult(SimulationOperation operation, string port, AbilityStateValue value,
                bool predictive, bool captureValue)
            {
                if (!predictive && captureValue && m_Owner.m_CaptureValueTrace)
                    m_Owner.m_Output.TraceValue(operation.Handle, CanonicalTextValue.IsNullOrEmpty(port)
                        ? m_Owner.m_OutputPortNames[operation.Handle.Value][0] : port, value, false);
            }
            protected override void TraceInput(SimulationOperation operation, CompiledValueInputBinding input, AbilityStateValue value,
                bool predictive, bool captureValue)
            {
                if (!predictive && captureValue && m_Owner.m_CaptureValueTrace)
                    m_Owner.m_Output.TraceValue(operation.Handle, m_Owner.m_InputPortNames[operation.Handle.Value][input.TargetPortIndex], value, true);
            }
        }

        readonly struct Target : IOperationControlTarget<Target>
        {
            readonly Float32PresentationGraphRuntime m_Owner;
            internal Target(Float32PresentationGraphRuntime owner) => m_Owner = owner;
            public bool DiagnosticsEnabled => m_Owner.m_CaptureGraphTrace || m_Owner.m_CaptureValueTrace;
            public bool ControlTraceEnabled => m_Owner.m_CaptureGraphTrace;
            public void TraceEdge(ProgramControlFlowEdge edge, bool selected, bool passed)
            {
                if (m_Owner.m_CaptureGraphTrace)
                    m_Owner.m_Output.TraceEdge(edge, selected, passed);
            }
            public int ReadInt32(int slot) => m_Owner.m_State[slot].Int32;
            public void WriteInt32(int slot, int value) => m_Owner.SetState(slot, AbilityStateValue.FromInt32(value));
            public ulong ReadUInt64(int slot) => m_Owner.m_State[slot].UInt64;
            public void WriteUInt64(int slot, ulong value) => m_Owner.SetState(slot, AbilityStateValue.FromUInt64(value));
            public CanonicalTextValue ReadIdentity(int slot) => m_Owner.m_State[slot].Identity;
            public void WriteIdentity(int slot, CanonicalTextValue value) => m_Owner.SetState(slot, AbilityStateValue.FromIdentity(value));
            public bool EvaluateCondition(OperationControlCursor<Target> cursor, ProgramControlFlowEdge edge) => m_Owner.m_Values.EvaluateCondition(cursor, edge);
            public void PrepareSubGraph(OperationControlCursor<Target> cursor, OperationExecutionDescriptor operation) =>
                m_Owner.m_Values.PrepareSubGraph(cursor, m_Owner.m_Layout.Operation(operation.Handle));
            public void ResetOperationState(OperationExecutionDescriptor operation) => m_Owner.ResetOperation(operation);

            public OperationExecutionResult ExecuteLeaf(OperationControlCursor<Target> cursor, OperationExecutionDescriptor descriptor)
            {
                SimulationOperation operation = m_Owner.m_Layout.Operation(descriptor.Handle);
                switch (operation.Code)
                {
                    case SimulationOperationCode.CameraStateRequest:
                    case SimulationOperationCode.CameraEffectRequest:
                    case SimulationOperationCode.CameraResponse:
                    case SimulationOperationCode.CameraTarget:
                        m_Owner.m_Output.SubmitCamera(m_Owner.CreateOutputIdentity(operation),
                            m_Owner.m_Producers[operation.Handle.Value],
                            m_Owner.m_Activations[operation.Handle.Value], m_Owner.m_Retirements[operation.Handle.Value], m_Owner.m_Retiring);
                        return OperationExecutionResult.Success;
                    case SimulationOperationCode.VfxRequest:
                        m_Owner.m_Values.SubmitVfx(cursor, operation);
                        return OperationExecutionResult.Success;
                    case SimulationOperationCode.AudioRequest:
                        m_Owner.m_Output.SubmitAudio(m_Owner.CreateOutputIdentity(operation),
                            m_Owner.m_Producers[operation.Handle.Value], m_Owner.m_AudioRequests[operation.Handle.Value]);
                        return OperationExecutionResult.Success;
                    case SimulationOperationCode.TimelineClipExitRequest:
                        if (!m_Owner.m_CanRequestExit)
                            throw new InvalidOperationException("Only an active Presentation TreeClip can request its own exit.");
                        m_Owner.m_ExitRequested = true;
                        return OperationExecutionResult.Success;
                    case SimulationOperationCode.BlackboardSet:
                        m_Owner.m_Values.WriteParameter(cursor, operation);
                        return OperationExecutionResult.Success;
                    default:
                        return ProgramValues.ToBoolean(m_Owner.m_Values.Evaluate(cursor, operation.Handle))
                            ? OperationExecutionResult.Success : OperationExecutionResult.Failure;
                }
            }

            public void PrepareActivation(OperationExecutionDescriptor operation) { }
            public void ActivateScopes(OperationControlCursor<Target> cursor, OperationExecutionDescriptor operation, ulong generation) { }
            public void CompleteScopes(OperationExecutionDescriptor operation) { }
            public void ClearStateScope(OperationExecutionDescriptor state) => throw UnsupportedState();
            public OperationStopStatus ContinueLeafStop(OperationControlCursor<Target> cursor, OperationExecutionDescriptor operation, OperationStopContext context) => throw UnsupportedState();
            public void ForceStopLeaf(OperationControlCursor<Target> cursor, OperationExecutionDescriptor operation, OperationStopContext context) => throw UnsupportedState();
            public void EmitTrace(OperationExecutionDescriptor operation, string code, OperationControlTraceSeverity severity, string detail) =>
                m_Owner.m_Output.TraceOperation(operation.Handle, code, detail, m_Owner.m_Control.Cursor.ReadGeneration(operation.Handle));
            public void NotifyStateLifecycle(OperationExecutionDescriptor machine, OperationHandle state, OperationStateLifecyclePhase phase) => throw UnsupportedState();
            public void NotifyStateTransition(OperationExecutionDescriptor machine, OperationHandle exitingState, OperationHandle targetState) => throw UnsupportedState();
            static InvalidOperationException UnsupportedState() => new("Presentation Marker graphs cannot retain a Simulation state lifecycle.");
        }
    }
}
