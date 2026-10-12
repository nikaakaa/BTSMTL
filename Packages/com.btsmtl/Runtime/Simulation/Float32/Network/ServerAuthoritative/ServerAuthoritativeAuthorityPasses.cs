using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class AuthorityAcceptedInputIngressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public AuthorityAcceptedInputIngressPassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.AcceptedInputIngress(policy));
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new AuthorityAcceptedInputIngressReads(
                context.BindSourcePort<IServerAuthoritativeAcceptedInputSourcePort>(ServerAuthoritativeSourcePortContracts.AcceptedInputPortId));
            var writes = new AuthorityAcceptedInputIngressWrites(
                context.Products.BindExclusiveWriter<AcceptedAuthorityInputBatch>(ServerAuthoritativeProducts.AcceptedAuthorityInputBatch));
            return new Float32IngressPassRuntimeAdapter<AuthorityAcceptedInputIngressReads, AuthorityAcceptedInputIngressWrites>(
                new AuthorityAcceptedInputIngressPassRuntime(context.Pass.Descriptor),
                reads,
                writes);
        }
    }

    public sealed class AuthorityAcceptedInputIngressPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationIngressPassRuntime<AuthorityAcceptedInputIngressReads, AuthorityAcceptedInputIngressWrites>
    {
        public AuthorityAcceptedInputIngressPassRuntime(SimulationPipelinePassDescriptor descriptor) : base(descriptor) { }

        public void Execute(
            SimulationPipelineIngressContext context,
            AuthorityAcceptedInputIngressReads readPorts,
            AuthorityAcceptedInputIngressWrites writePorts)
        {
            RequireExecution();
            AcceptedAuthorityInputBatch batch = readPorts.Source.Read(context.Source);
            if (!batch.IsValid)
                throw new InvalidOperationException("Authority accepted-input Source returned no canonical batch.");
            writePorts.Batch.Write(batch);
        }
    }

    public sealed class AuthorityAcceptedInputIngressReads : ISimulationPipelineReadPortSet
    {
        public AuthorityAcceptedInputIngressReads(IServerAuthoritativeAcceptedInputSourcePort source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public IServerAuthoritativeAcceptedInputSourcePort Source { get; }
    }

    public sealed class AuthorityAcceptedInputIngressWrites : ISimulationPipelineWritePortSet
    {
        public AuthorityAcceptedInputIngressWrites(
            IExclusiveSimulationPipelineProductWriter<AcceptedAuthorityInputBatch> batch)
        {
            Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        }

        public IExclusiveSimulationPipelineProductWriter<AcceptedAuthorityInputBatch> Batch { get; }
    }

    public sealed class AuthorityTickSchedulePassRuntimeFactory : IFloat32PipelinePassRuntimeFactory, ISimulationScheduleCapacity
    {
        readonly ServerAuthoritativeModelPolicy m_Policy;
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public AuthorityTickSchedulePassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.AuthorityTickSchedule(policy),
                ServerAuthoritativeAuthorityPassIds.ScheduleStateSchema);
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public GameplayEffectScheduleCapacity GameplayEffectCapacity => m_Policy.AuthorityGameplayEffectCapacity;
        public int MaximumStepCount => GameplayEffectCapacity.MaximumStepCount;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new AuthorityTickScheduleReads(
                context.Products.BindExclusiveReader<AcceptedAuthorityInputBatch>(ServerAuthoritativeProducts.AcceptedAuthorityInputBatch),
                context.BindSourcePort<IServerAuthoritativeAuthorityClockSourcePort>(ServerAuthoritativeSourcePortContracts.AuthorityClockPortId),
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            var writes = new AuthorityTickScheduleWrites(
                context.Products.BindExclusiveWriter<SimulationSessionExecutionPlan<Float32SimulationStep>>(SimulationPipelineProducts.ExecutionPlan));
            return new Float32SchedulePassRuntimeAdapter<AuthorityTickScheduleReads, AuthorityTickScheduleWrites>(
                new AuthorityTickSchedulePassRuntime(context.Pass.Descriptor, m_Policy,
                    reads.CharacterRuntime.Runtime.RosterDescriptor.Actors).PrepareStateStorage(context.Composition),
                reads,
                writes);
        }
    }

    public sealed class AuthorityTickSchedulePassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationExecutionPlanSchedulePassRuntime<AuthorityTickScheduleReads, AuthorityTickScheduleWrites>,
        ISimulationPipelineStateParticipant
    {
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => m_Policy.AuthorityGameplayEffectCapacity;
        public int MaximumStepCount => GameplayEffectCapacity.MaximumStepCount;
        readonly ServerAuthoritativeModelPolicy m_Policy;
        const int MaximumHeldActors = 64;
        readonly ActorId[] m_HeldActors = new ActorId[MaximumHeldActors];
        readonly HeldAuthorityInput[] m_Held = new HeldAuthorityInput[MaximumHeldActors];
        readonly CanonicalBuffer<SimulationInputValue>[] m_NeutralValues = new CanonicalBuffer<SimulationInputValue>[MaximumHeldActors];
        readonly int[] m_HeldIndices = new int[MaximumHeldActors];
        int m_HeldCount;
        readonly HeldAuthorityInput[] m_HeldWorkspace;
        readonly SimulationPipelineActorInput<Float32StepInput>[] m_ActorInputs;
        readonly Float32SimulationInputFrameStorage[] m_InputViews;
        readonly SimulationPipelineStepSourceMapping[] m_SourceMappings = new SimulationPipelineStepSourceMapping[1];
        readonly Float32SimulationStep[] m_Steps = new Float32SimulationStep[1];
        readonly ObservedWorldConstraintFrame m_ObservedWorldConstraints = new ObservedWorldConstraintFrame();

        public AuthorityTickSchedulePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            ServerAuthoritativeModelPolicy policy,
            IReadOnlyList<ActorId> actors) : base(descriptor)
        {
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            int values = policy.MaxGameplayDatagramBytes / Float32SimulationInputCodec.MinimumValueBytes;
            int requests = policy.MaxGameplayDatagramBytes / Float32SimulationInputCodec.MinimumRequestBytes;
            for (int i = 0; i < m_Held.Length; i++)
            {
                m_Held[i] = new HeldAuthorityInput(values, requests);
                m_NeutralValues[i] = new CanonicalBuffer<SimulationInputValue>(values);
            }
            m_HeldWorkspace = new HeldAuthorityInput[actors.Count];
            m_ActorInputs = new SimulationPipelineActorInput<Float32StepInput>[actors.Count];
            m_InputViews = new Float32SimulationInputFrameStorage[actors.Count];
            for (int i = 0; i < m_InputViews.Length; i++)
                m_InputViews[i] = new Float32SimulationInputFrameStorage();
            m_Steps[0] = new Float32SimulationStep(actors);
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                ServerAuthoritativeAuthorityPassIds.ScheduleStateOwner,
                ServerAuthoritativeAuthorityPassIds.ScheduleStateSchema,
                1);
        }

        ServerAuthoritativePassStateRestoreTransaction m_StateStorage;

        internal AuthorityTickSchedulePassRuntime PrepareStateStorage(SimulationSessionCompositionDescriptor composition)
        {
            m_StateStorage = new ServerAuthoritativePassStateRestoreTransaction(this,
                composition.MaximumSessionCheckpointPayloadBytes, MaximumTextValues,
                checked(1 + m_Policy.AuthorityGameplayEffectCapacity.MaximumStepCount + composition.RecordedFrameCapacity),
                Restore);
            return this;
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;
        public int MaximumTextValues => checked(m_HeldActors.Length *
            (1 + ServerAuthoritativeCanonicalCodec.MaximumInputTextValues));

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            RequireCaptureOrRestore();
            return m_StateStorage.CaptureCheckpoint();
        }

        public void Execute(
            SimulationPipelineScheduleContext context,
            AuthorityTickScheduleReads readPorts,
            AuthorityTickScheduleWrites writePorts)
        {
            RequireExecution();
            for (int i = 0; i < m_InputViews.Length; i++)
                m_InputViews[i].ClearBorrowedView();
            AcceptedAuthorityInputBatch accepted = readPorts.Accepted.Read();
            SimulationTick authorityTick = readPorts.Clock.ReadAuthorityTick(context.Source);
            if (authorityTick.Value != checked(context.CurrentCompletedTick + 1) ||
                accepted.AuthorityTick != authorityTick)
            {
                throw new InvalidOperationException("Authority clock, accepted input and Session Tick are not contiguous.");
            }
            Accept(accepted, authorityTick);
            var authoritySource = new SimulationTickSourceIdentity(
                SimulationTickSourceKind.Authoritative,
                context.Source.ClockId,
                authorityTick.Value);
            int actorCount = readPorts.CharacterRuntime.Runtime.Roster.Count;
            for (int i = 0; i < actorCount; i++)
            {
                ActorId actorId = readPorts.CharacterRuntime.Runtime.Roster[i].ActorId;
                int heldIndex = FindHeld(actorId);
                if (heldIndex < 0)
                {
                    writePorts.ExecutionPlan.Write(Pending(context, readPorts.CharacterRuntime));
                    Array.Clear(m_HeldWorkspace, 0, actorCount);
                    return;
                }
                HeldAuthorityInput held = m_Held[heldIndex];
                m_HeldWorkspace[i] = held;
                m_HeldIndices[i] = heldIndex;
            }
            SimulationPipelineActorInput<Float32StepInput>[] actorInputs = m_ActorInputs;
            for (int i = 0; i < actorCount; i++)
            {
                HeldAuthorityInput held = m_HeldWorkspace[i];
                ActorId actorId = m_HeldActors[m_HeldIndices[i]];
                SimulationInput input = BuildInput(i, m_HeldIndices[i], held, authoritySource, authorityTick);
                actorInputs[i] = new SimulationPipelineActorInput<Float32StepInput>(
                    actorId,
                    held.InputSequence,
                    new Float32StepInput(input));
                held.MarkConsumed(authorityTick);
                m_HeldWorkspace[i] = null;
            }
            m_ObservedWorldConstraints.ResetEmpty(authorityTick);
            m_Steps[0].Reset(
                authorityTick,
                new SimulationPipelineStepProvenance(
                    SimulationPipelineStepExecutionKind.Authoritative,
                    authoritySource,
                    authorityTick.Value),
                actorInputs,
                Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>(),
                m_ObservedWorldConstraints);
            m_SourceMappings[0] = new SimulationPipelineStepSourceMapping(
                context.Source.ClockId,
                context.Source.ClockId,
                SimulationTickSourceKind.Authoritative);
            writePorts.ExecutionPlan.Write(SimulationSessionExecutionPlan<Float32SimulationStep>.FromOwnedCollections(
                SimulationSessionExecutionPlanStatus.Executable,
                context.Source,
                readPorts.CharacterRuntime.Runtime.GameplayContentHash,
                context.Pipeline.Hash,
                readPorts.CharacterRuntime.Runtime.RosterDescriptor,
                m_SourceMappings,
                null,
                m_Steps,
                SimulationSessionPlanRequirement.WorkingState |
                SimulationSessionPlanRequirement.OutputDisposition |
                SimulationSessionPlanRequirement.StateHash));
            if (readPorts.Diagnostics.Sink.IsEnabled)
            {
                for (int i = 0; i < actorInputs.Length; i++)
                {
                    readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                        SimulationModelTraceKind.Queue,
                        "authority_schedule",
                        $"roster={actorInputs.Length};held={m_HeldCount};missingPolicy={m_Policy.MissingInputPolicy};maxLag={m_Policy.MaximumInputLagTicks}",
                        actorInputs[i].ActorId,
                        context.Source.SourceTick,
                        authorityTick.Value,
                        actorInputs[i].Sequence,
                        0,
                        m_HeldCount));
                }
            }
        }

        void Accept(AcceptedAuthorityInputBatch batch, SimulationTick authorityTick)
        {
            for (int i = 0; i < batch.Inputs.Count; i++)
            {
                AcceptedAuthorityInput input = batch.Inputs[i];
                int heldIndex = FindHeld(input.ActorId);
                if (heldIndex >= 0 && input.InputSequence <= m_Held[heldIndex].InputSequence)
                {
                    continue;
                }
                if (heldIndex < 0)
                    AddHeld(input.ActorId, input.InputSequence, input.Input, authorityTick);
                else
                    m_Held[heldIndex].Reset(input.InputSequence, input.Input, authorityTick);
            }
        }

        void AddHeld(
            ActorId actorId,
            ulong inputSequence,
            SimulationInput input,
            SimulationTick acceptedTick)
        {
            int insertionIndex = FindHeld(actorId);
            if (insertionIndex >= 0)
                throw new InvalidOperationException($"Authority input hold Actor '{actorId}' already exists.");
            insertionIndex = ~insertionIndex;
            if (m_HeldCount >= MaximumHeldActors)
                throw new InvalidOperationException("Authority input hold capacity is exhausted.");
            HeldAuthorityInput held = m_Held[m_HeldCount];
            CanonicalBuffer<SimulationInputValue> neutral = m_NeutralValues[m_HeldCount];
            Array.Copy(m_HeldActors, insertionIndex, m_HeldActors, insertionIndex + 1, m_HeldCount - insertionIndex);
            Array.Copy(m_Held, insertionIndex, m_Held, insertionIndex + 1, m_HeldCount - insertionIndex);
            Array.Copy(m_NeutralValues, insertionIndex, m_NeutralValues, insertionIndex + 1, m_HeldCount - insertionIndex);
            m_NeutralValues[insertionIndex] = neutral;
            m_HeldActors[insertionIndex] = actorId;
            m_Held[insertionIndex] = held;
            held.Reset(inputSequence, input, acceptedTick);
            m_HeldCount++;
        }

        int FindHeld(ActorId actorId)
        {
            int left = 0;
            int right = m_HeldCount - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                int comparison = m_HeldActors[middle].CompareTo(actorId);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }

        SimulationInput BuildInput(
            int actorIndex,
            int heldIndex,
            HeldAuthorityInput held,
            SimulationTickSourceIdentity source,
            SimulationTick authorityTick)
        {
            bool fresh = held.AcceptedTick == authorityTick && held.LastConsumedTick < authorityTick.Value;
            int age = checked((int)(authorityTick.Value - held.AcceptedTick.Value));
            bool reuse = fresh || m_Policy.MissingInputPolicy == ServerAuthoritativeMissingInputPolicy.ReuseLastCanonicalInput &&
                age <= m_Policy.MaximumInputLagTicks;
            CanonicalBuffer<SimulationInputValue> values = reuse
                ? held.Input.CanonicalValues.Values
                : GetNeutralValues(heldIndex, held.Input.CanonicalValues.Values);
            return m_InputViews[actorIndex].BindBorrowed(
                held.Input,
                values,
                source,
                held.InputSequence,
                fresh);
        }

        CanonicalBuffer<SimulationInputValue> GetNeutralValues(int heldIndex, CanonicalBuffer<SimulationInputValue> values)
        {
            CanonicalBuffer<SimulationInputValue> neutral = m_NeutralValues[heldIndex];
            if (!HasNeutralLayout(neutral, values))
            {
                ReleaseNeutralValues(heldIndex);
                for (int i = 0; i < values.Count; i++)
                {
                    ref readonly SimulationInputValue value = ref values.Element(i);
                    neutral.Add(value.Kind switch
                    {
                        ProgramInputValueKind.Boolean => SimulationInputValue.FromBoolean(value.InputId, false),
                        ProgramInputValueKind.Scalar => SimulationInputValue.FromScalar(value.InputId, Float32Scalar.Zero),
                        ProgramInputValueKind.Vector2 => SimulationInputValue.FromVector2(value.InputId, Float32Vector2.Zero),
                        ProgramInputValueKind.Vector3 => SimulationInputValue.FromVector3(value.InputId, Float32Vector3.Zero),
                        ProgramInputValueKind.Yaw => SimulationInputValue.FromYaw(value.InputId, Float32Yaw.Zero),
                        ProgramInputValueKind.ActionTargetSnapshot => SimulationInputValue.FromActionTargetSnapshot(value.InputId, SimulationActionTargetSnapshot.None),
                        _ => throw new InvalidDataException($"Unsupported input value kind '{value.Kind}'.")
                    });
                    neutral.Element(i).Retain();
                }
            }
            return neutral;
        }

        void ReleaseNeutralValues(int heldIndex)
        {
            CanonicalBuffer<SimulationInputValue> values = m_NeutralValues[heldIndex];
            for (int i = 0; i < values.Count; i++)
                values.Element(i).Release();
            values.Clear();
        }

        static bool HasNeutralLayout(CanonicalBuffer<SimulationInputValue> cache, CanonicalBuffer<SimulationInputValue> values)
        {
            if (cache.Count != values.Count)
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                ref readonly SimulationInputValue cached = ref cache.Element(i);
                ref readonly SimulationInputValue value = ref values.Element(i);
                if (!CanonicalTextValue.Equals(cached.InputId, value.InputId, StringComparison.Ordinal) ||
                    cached.Kind != value.Kind)
                {
                    return false;
                }
            }
            return true;
        }

        static SimulationSessionExecutionPlan<Float32SimulationStep> Pending(
            SimulationPipelineScheduleContext context,
            IFloat32CharacterRuntimePort runtime) =>
            new SimulationSessionExecutionPlan<Float32SimulationStep>(
                SimulationSessionExecutionPlanStatus.Pending,
                context.Source,
                runtime.Runtime.GameplayContentHash,
                context.Pipeline.Hash,
                runtime.Runtime.RosterDescriptor,
                Array.Empty<SimulationPipelineStepSourceMapping>(),
                null,
                Array.Empty<Float32SimulationStep>(),
                SimulationSessionPlanRequirement.None);

        public SimulationPipelinePassStateSnapshot CaptureState()
        {
            RequireCaptureOrRestore();
            return m_StateStorage.CaptureState();
        }

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            RequireCaptureOrRestore();
            return m_StateStorage.Prepare(snapshot);
        }

        public void WriteCheckpointState(CanonicalWriter writer)
        {
            RequireCaptureOrRestore();
            writer.WriteUInt32(0x48494153);
            writer.WriteInt32(1);
            writer.WriteInt32(m_HeldCount);
            for (int i = 0; i < m_HeldCount; i++)
            {
                writer.WriteText(m_HeldActors[i].Value);
                writer.WriteUInt64(m_Held[i].InputSequence);
                writer.WriteUInt64(m_Held[i].AcceptedTick.Value);
                writer.WriteUInt64(m_Held[i].LastConsumedTick);
                ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedInput(writer, m_Held[i].Input);
            }
        }

        void Restore(CanonicalReader reader, CanonicalReader inputReader, CanonicalWriter canonicalScratch)
        {
            if (reader.ReadUInt32() != 0x48494153 || reader.ReadInt32() != 1)
                throw new InvalidDataException("Authority input hold state header is invalid.");
            int count = reader.ReadInt32();
            if (count < 0 || count > 64)
                throw new InvalidDataException("Authority input hold state count is invalid.");
            for (int index = 0; index < m_HeldCount; index++)
            {
                ReleaseNeutralValues(index);
                m_Held[index].Dispose();
            }
            Array.Clear(m_HeldActors, 0, m_HeldCount);
            m_HeldCount = 0;
            for (int i = 0; i < count; i++)
            {
                var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                ulong sequence = reader.ReadUInt64();
                var acceptedTick = new SimulationTick(reader.ReadUInt64());
                ulong consumedTick = reader.ReadUInt64();
                ArraySegment<byte> inputBytes = reader.ReadBytesSegment();
                if (i > 0 && m_HeldActors[i - 1].CompareTo(actorId) >= 0)
                    throw new InvalidDataException("Authority input hold state Actor order is invalid.");
                m_HeldActors[i] = actorId;
                m_HeldCount++;
                m_Held[i].Read(inputBytes, inputReader, reader.TextResolver, sequence, acceptedTick, consumedTick);
            }
            reader.RequireComplete();
        }

        protected override void OnDispose()
        {
            m_StateStorage.ReleaseStorage();
            for (int i = 0; i < m_InputViews.Length; i++)
                m_InputViews[i].ClearBorrowedView();
            Array.Clear(m_HeldWorkspace, 0, m_HeldWorkspace.Length);
            Array.Clear(m_ActorInputs, 0, m_ActorInputs.Length);
            m_SourceMappings[0] = default;
            m_ObservedWorldConstraints.SetPreparedMetadata(default, default);
            m_Steps[0].Reset(default, default,
                Array.Empty<SimulationPipelineActorInput<Float32StepInput>>(),
                Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>(),
                m_ObservedWorldConstraints);
            for (int index = 0; index < m_HeldCount; index++)
            {
                ReleaseNeutralValues(index);
                m_Held[index].Dispose();
            }
            Array.Clear(m_HeldActors, 0, m_HeldCount);
            Array.Clear(m_Held, 0, m_HeldCount);
            m_HeldCount = 0;
        }

        sealed class HeldAuthorityInput
        {
            readonly Float32SimulationInputFrameStorage m_InputStorage;

            public HeldAuthorityInput(int values, int requests) =>
                m_InputStorage = new Float32SimulationInputFrameStorage(values, requests);

            public ulong InputSequence { get; private set; }
            public SimulationInput Input { get; private set; }
            public SimulationTick AcceptedTick { get; private set; }
            public ulong LastConsumedTick { get; private set; }

            public void Reset(
                ulong inputSequence,
                SimulationInput input,
                SimulationTick acceptedTick,
                ulong lastConsumedTick = 0)
            {
                SetInput(m_InputStorage.Capture(input), inputSequence, acceptedTick, lastConsumedTick);
            }

            public void Read(ArraySegment<byte> bytes, CanonicalReader reader, ICanonicalTextResolver textResolver,
                ulong inputSequence, SimulationTick acceptedTick, ulong lastConsumedTick)
            {
                SetInput(ServerAuthoritativeCanonicalCodec.ReadInput(bytes, reader, textResolver, m_InputStorage),
                    inputSequence, acceptedTick, lastConsumedTick);
            }

            void SetInput(SimulationInput input, ulong inputSequence, SimulationTick acceptedTick, ulong lastConsumedTick)
            {
                InputSequence = inputSequence;
                Input = input;
                AcceptedTick = acceptedTick;
                LastConsumedTick = lastConsumedTick;
            }

            public void MarkConsumed(SimulationTick tick) => LastConsumedTick = tick.Value;
            public void Dispose() { m_InputStorage.Release(); Input = null; }
        }
    }

    public sealed class AuthorityTickScheduleReads : ISimulationPipelineReadPortSet
    {
        public AuthorityTickScheduleReads(
            IReadOnlySimulationPipelineProductPort<AcceptedAuthorityInputBatch> accepted,
            IServerAuthoritativeAuthorityClockSourcePort clock,
            IFloat32CharacterRuntimePort characterRuntime,
            IFloat32DiagnosticsRuntimePort diagnostics)
        {
            Accepted = accepted ?? throw new ArgumentNullException(nameof(accepted));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<AcceptedAuthorityInputBatch> Accepted { get; }
        public IServerAuthoritativeAuthorityClockSourcePort Clock { get; }
        public IFloat32CharacterRuntimePort CharacterRuntime { get; }
        public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class AuthorityTickScheduleWrites : ISimulationPipelineWritePortSet
    {
        public AuthorityTickScheduleWrites(
            IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<Float32SimulationStep>> executionPlan)
        {
            ExecutionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<Float32SimulationStep>> ExecutionPlan { get; }
    }

    public sealed class AuthorityReplicationEgressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly ServerAuthoritativeModelPolicy m_Policy;
        readonly ServerAuthoritativeReplicationPolicy m_ReplicationPolicy;
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public AuthorityReplicationEgressPassRuntimeFactory(
            ServerAuthoritativeModelPolicy policy,
            ServerAuthoritativeReplicationPolicy replicationPolicy)
        {
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_ReplicationPolicy = replicationPolicy ?? throw new ArgumentNullException(nameof(replicationPolicy));
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.AuthorityReplicationEgress(policy, replicationPolicy),
                ServerAuthoritativeAuthorityPassIds.ReplicationStateSchema);
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new AuthorityReplicationEgressReads(
                context.Products.BindExclusiveReader<AcceptedAuthorityInputBatch>(ServerAuthoritativeProducts.AcceptedAuthorityInputBatch),
                context.Products.BindAppendReader<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult),
                context.BindSourcePort<IServerAuthoritativeNetworkSendPort>(ServerAuthoritativeSourcePortContracts.AuthoritySendPortId),
                context.BindSourcePort<IServerAuthoritativeFullBaselineRequestSourcePort>(ServerAuthoritativeSourcePortContracts.FullBaselineRequestPortId),
                context.BindTargetPort<IFloat32CompletedStepReadPort>(Float32PipelineRuntimePortIds.CompletedSteps),
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime),
                context.BindSolverPort<IFloat32WorldSolverRuntimePort>(Float32PipelineRuntimePortIds.WorldSolver),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            var writes = new AuthorityReplicationEgressWrites(
                context.Products.BindExclusiveWriter<AuthorityReplicationBatch>(ServerAuthoritativeProducts.AuthorityReplicationBatch),
                context.Products.BindExclusiveWriter<SimulationPipelineOutputDispositionSet>(SimulationPipelineProducts.OutputDispositionSet),
                context.Products.BindAppendWriter<Float32SourceEgressRecord>(SimulationPipelineProducts.SourceEgress));
            return new Float32EgressPassRuntimeAdapter<AuthorityReplicationEgressReads, AuthorityReplicationEgressWrites>(
                new AuthorityReplicationEgressPassRuntime(context.Pass.Descriptor, m_Policy, m_ReplicationPolicy,
                    reads.CharacterRuntime.Runtime).PrepareStateStorage(context.Composition),
                reads,
                writes);
        }
    }

    public sealed class AuthorityReplicationEgressPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<AuthorityReplicationEgressReads, AuthorityReplicationEgressWrites>,
        ISimulationPipelineStateParticipant
    {
        readonly ServerAuthoritativeModelPolicy m_Policy;
        readonly ServerAuthoritativeReplicationPolicy m_ReplicationPolicy;
        readonly ActorId[] m_HorizonActors;
        readonly ServerAuthoritativeEventHorizon[] m_Horizons;
        int m_HorizonCount;
        readonly List<AuthoritativeActorBaseline> m_Baselines;
        readonly List<AuthoritativeInputAck> m_Acks;
        readonly List<RemotePresentationBatch> m_Remote;
        readonly RemoteActorOutputs[] m_RemoteOutputs;
        readonly CanonicalHashWriter m_BodyHashWriter = new CanonicalHashWriter();
        readonly SimulationOutputDispositionBuffer m_Dispositions;
        readonly long m_MaximumEgressTextValues;
        CanonicalTextReferences m_EgressText;
        CanonicalWriter m_EgressWriter;
        Float32SourceEgressRecord m_EgressRecord;
        SimulationPipelinePassStateStorage m_StateSnapshots;
        ActorId[] m_CheckpointActors;
        ServerAuthoritativeEventHorizon[] m_CheckpointHorizons;
        int m_CheckpointCount;
        SimulationPipelinePassStateCheckpoint m_Checkpoint;
        ReplicationRestoreTransaction m_Restore;

        internal AuthorityReplicationEgressPassRuntime PrepareStateStorage(SimulationSessionCompositionDescriptor composition)
        {
            int payloadCapacity = composition.MaximumSessionCheckpointPayloadBytes;
            if (payloadCapacity <= 0)
                throw new InvalidOperationException("Authority replication state requires Composition.MaximumSessionCheckpointPayloadBytes.");
            int textValues = SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(
                payloadCapacity, MaximumTextValues, 0, 0);
            m_StateSnapshots = new SimulationPipelinePassStateStorage(StateIdentity,
                checked(1 + m_Policy.AuthorityGameplayEffectCapacity.MaximumStepCount + composition.RecordedFrameCapacity),
                payloadCapacity, textValues, null);
            m_CheckpointActors = new ActorId[m_HorizonActors.Length];
            m_CheckpointHorizons = new ServerAuthoritativeEventHorizon[m_HorizonActors.Length];
            m_Checkpoint = new SimulationPipelinePassStateCheckpoint(StateIdentity, RestoreCheckpoint, ReleaseCheckpoint);
            m_Restore = new ReplicationRestoreTransaction(this, m_HorizonActors.Length);
            int egressTexts = SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(
                payloadCapacity, m_MaximumEgressTextValues, 0, 0);
            m_EgressText = new CanonicalTextReferences(egressTexts, null);
            var egressBuffer = new byte[payloadCapacity];
            m_EgressWriter = new CanonicalWriter(egressBuffer);
            m_EgressWriter.SetTextCapture(m_EgressText);
            m_EgressRecord = new Float32SourceEgressRecord(
                ServerAuthoritativeEgressChannels.AuthorityReplication,
                ServerAuthoritativeEgressChannels.AuthorityReplicationSchema,
                ServerAuthoritativeEgressChannels.AuthorityReplicationSchemaVersion,
                egressBuffer, m_EgressText, new CanonicalReader(Array.Empty<byte>()),
                new CanonicalReader(Array.Empty<byte>()), new CanonicalWriter(new byte[payloadCapacity]));
            return this;
        }

        public AuthorityReplicationEgressPassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            ServerAuthoritativeModelPolicy policy,
            ServerAuthoritativeReplicationPolicy replicationPolicy,
            Float32CharacterRuntime characterRuntime) : base(descriptor)
        {
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_ReplicationPolicy = replicationPolicy ?? throw new ArgumentNullException(nameof(replicationPolicy));
            int actorCount = characterRuntime.Roster.Count;
            m_HorizonActors = new ActorId[actorCount];
            m_Horizons = new ServerAuthoritativeEventHorizon[actorCount];
            m_Baselines = new List<AuthoritativeActorBaseline>(actorCount);
            m_Acks = new List<AuthoritativeInputAck>(actorCount);
            m_Remote = new List<RemotePresentationBatch>(actorCount);
            m_RemoteOutputs = new RemoteActorOutputs[actorCount];
            int outputCapacity = 0;
            for (int actor = 0; actor < actorCount; actor++)
            {
                int actorOutputCapacity = characterRuntime.Roster[actor].GameplayOutputCapacity;
                outputCapacity = checked(outputCapacity + actorOutputCapacity);
                m_RemoteOutputs[actor] = new RemoteActorOutputs(actorOutputCapacity);
            }
            m_Dispositions = new SimulationOutputDispositionBuffer(outputCapacity);
            m_MaximumEgressTextValues = actorCount * 13L + (long)outputCapacity *
                (GameplayFact.MaximumTextReferences + PresentationCommand.MaximumTextReferences);
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                ServerAuthoritativeAuthorityPassIds.ReplicationStateOwner,
                ServerAuthoritativeAuthorityPassIds.ReplicationStateSchema,
                1);
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;
        public int MaximumTextValues => m_HorizonActors.Length;

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            RequireCaptureOrRestore();
            m_Checkpoint.Begin();
            CaptureHorizons(m_CheckpointActors, m_CheckpointHorizons, out m_CheckpointCount);
            return m_Checkpoint;
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            AuthorityReplicationEgressReads readPorts,
            AuthorityReplicationEgressWrites writePorts)
        {
            RequireExecution();
            ClearScratch();
            AcceptedAuthorityInputBatch accepted = readPorts.Accepted.Read();
            if (readPorts.Completed.Steps.Count != 1 ||
                readPorts.Completed.Steps[0].Step.ExecutionKind != SimulationPipelineStepExecutionKind.Authoritative)
            {
                throw new InvalidOperationException("Authority Egress requires exactly one authoritative completed Step.");
            }
            Float32CompletedSimulationStep completed = readPorts.Completed.Steps[0];
            if (readPorts.Results.Count != completed.Result.Actors.Count)
                throw new InvalidOperationException("Authority finalized result count does not match the completed Step.");
            ulong snapshotInterval = checked((ulong)(m_Policy.SimulationTickRate / m_Policy.SnapshotPacketRate));
            bool emitBaseline = completed.Step.Tick.Value == 1 ||
                completed.Step.Tick.Value % snapshotInterval == 0 ||
                readPorts.FullBaselineRequest.IsRequested;
            int actorCount = completed.Result.Actors.Count;
            int dispositionCount = 0;
            for (int i = 0; i < actorCount; i++)
            {
                SimulationActorTickResult actor = completed.Result.Actors[i];
                dispositionCount += actor.GameplayFacts.Count + actor.PresentationCommands.Count;
            }
            for (int i = 0; i < completed.Result.Actors.Count; i++)
            {
                SimulationActorTickResult actor = completed.Result.Actors[i];
                Float32CharacterRuntime characterRuntime = readPorts.CharacterRuntime.Runtime;
                m_Remote.Add(BuildRemote(actor, characterRuntime, m_RemoteOutputs[i]));
                int acceptedIndex = FindAcceptedInput(accepted, actor.ActorId);
                ServerAuthoritativeEventHorizon horizon = GetHorizon(actor.ActorId);
                m_Acks.Add(new AuthoritativeInputAck(
                    actor.ActorId,
                    completed.Step.Tick,
                    accepted.Inputs[acceptedIndex].InputSequence,
                    horizon));
                if (emitBaseline)
                    m_Baselines.Add(BuildBaseline(actor, completed, readPorts));
            }
            AuthorityReplicationBatch replication = AuthorityReplicationBatch.FromPreparedOutputs(
                completed.Step.Tick, m_Acks, m_Baselines, m_Remote);
            writePorts.Replication.Write(replication);
            writePorts.Dispositions.Write(SimulationPipelineOutputDispositionSet.FromBufferedDispositions(
                context.TransactionIdentity,
                m_Dispositions));
            m_EgressWriter.Reset();
            try
            {
                ServerAuthoritativeEgressCodec.WriteAuthorityReplication(m_EgressWriter, replication);
                m_EgressRecord.Publish(completed.Result.Actors[0].ActorId, completed.Step.Tick,
                    checked((int)m_EgressWriter.Length));
                writePorts.Egress.Append(
                    new SimulationPipelineAppendEntryIdentity(
                        completed.Result.Actors[0].ActorId,
                        completed.Step.Tick,
                        completed.Step.Tick.Value,
                        context.Source),
                    m_EgressRecord);
            }
            catch
            {
                m_EgressText.Reset();
                throw;
            }
            if (readPorts.Diagnostics.Sink.IsEnabled)
            {
                readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                    SimulationModelTraceKind.Transport,
                    "authority_replication_commit",
                    $"acks={m_Acks.Count};baselines={m_Baselines.Count};remote={m_Remote.Count};fullBaseline={emitBaseline};reliable={dispositionCount}",
                    default,
                    context.Source.SourceTick,
                    completed.Step.Tick.Value,
                    0,
                    0,
                    0,
                    0,
                    0f,
                    0f,
                    true));
            }
        }

        void ClearScratch()
        {
            m_Baselines.Clear();
            m_Acks.Clear();
            m_Remote.Clear();
            m_Dispositions.Begin();
            for (int actor = 0; actor < m_RemoteOutputs.Length; actor++)
                m_RemoteOutputs[actor].Begin();
        }

        RemotePresentationBatch BuildRemote(
            SimulationActorTickResult actor,
            Float32CharacterRuntime runtime,
            RemoteActorOutputs outputs)
        {
            outputs.Reliable.Bind(actor);
            for (int i = 0; i < actor.GameplayFacts.Count; i++)
            {
                GameplayFact fact = actor.GameplayFacts[i];
                if (m_ReplicationPolicy.ShouldReplicateReliably(fact))
                    outputs.Reliable.AddGameplay(i);
                m_Dispositions.Add(new SimulationOutputDisposition(
                    fact.Header.EventId,
                    actor.ActorId,
                    SimulationOutputDispositionKind.Suppress));
                AdvanceHorizon(actor.ActorId, fact.Header);
            }
            for (int i = 0; i < actor.PresentationCommands.Count; i++)
            {
                PresentationCommand command = actor.PresentationCommands[i];
                if (m_ReplicationPolicy.ShouldStream(command, runtime))
                    outputs.Samples.Add(command);
                if (m_ReplicationPolicy.ShouldReplicateReliably(command, runtime))
                    outputs.Reliable.AddPresentation(i);
                m_Dispositions.Add(new SimulationOutputDisposition(
                    command.Header.EventId,
                    actor.ActorId,
                    SimulationOutputDispositionKind.Suppress));
                AdvanceHorizon(actor.ActorId, command.Header);
            }
            outputs.Bodies.Add(actor.BodySample);
            outputs.Sort();
            return RemotePresentationBatch.FromPreparedOutputs(actor.ActorId, outputs.Bodies,
                outputs.Samples, outputs.Reliable, false);
        }

        protected override void OnDispose()
        {
            m_Restore.Dispose();
            m_Checkpoint.Dispose();
            m_EgressText.Reset();
            m_EgressWriter.Dispose();
            m_EgressRecord.CanonicalScratch.Dispose();
            ClearScratch();
            ClearHorizons();
        }

        sealed class RemoteActorOutputs
        {
            readonly Comparison<PresentationCommand> m_SampleComparison = ServerAuthoritativeProductOrder.CompareCommands;

            internal RemoteActorOutputs(int outputCapacity)
            {
                Bodies = new List<CharacterBodySample>(1);
                Samples = new List<PresentationCommand>(outputCapacity);
                Reliable = new ReliableEventBuffer(outputCapacity);
            }

            internal List<CharacterBodySample> Bodies { get; }
            internal List<PresentationCommand> Samples { get; }
            internal ReliableEventBuffer Reliable { get; }

            internal void Begin()
            {
                Bodies.Clear();
                Samples.Clear();
                Reliable.Begin();
            }

            internal void Sort()
            {
                Samples.Sort(m_SampleComparison);
                Reliable.Sort();
            }

        }

        sealed class ReliableEventBuffer : IReadOnlyList<ServerAuthoritativeReliableEvent>
        {
            readonly List<ReliablePosition> m_Positions;
            readonly Comparison<ReliablePosition> m_PositionComparison = ComparePositions;
            SimulationActorTickResult m_Actor;

            internal ReliableEventBuffer(int outputCapacity)
            {
                m_Positions = new List<ReliablePosition>(outputCapacity);
            }

            public int Count => m_Positions.Count;
            public ServerAuthoritativeReliableEvent this[int index]
            {
                get
                {
                    ReliablePosition position = m_Positions[index];
                    return position.Gameplay
                        ? new ServerAuthoritativeReliableEvent(m_Actor.GameplayFacts[position.Index])
                        : new ServerAuthoritativeReliableEvent(m_Actor.PresentationCommands[position.Index]);
                }
            }

            internal void Begin()
            {
                m_Positions.Clear();
                m_Actor = null;
            }

            internal void Bind(SimulationActorTickResult actor) => m_Actor = actor;
            internal void AddGameplay(int index) =>
                m_Positions.Add(new ReliablePosition(m_Actor.GameplayFacts[index].Header, index, true));
            internal void AddPresentation(int index) =>
                m_Positions.Add(new ReliablePosition(m_Actor.PresentationCommands[index].Header, index, false));
            internal void Sort() => m_Positions.Sort(m_PositionComparison);

            public Enumerator GetEnumerator() => new Enumerator(this);
            IEnumerator<ServerAuthoritativeReliableEvent> IEnumerable<ServerAuthoritativeReliableEvent>.GetEnumerator() => GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<ServerAuthoritativeReliableEvent>
            {
                readonly ReliableEventBuffer m_Buffer;
                int m_Index;

                internal Enumerator(ReliableEventBuffer buffer)
                {
                    m_Buffer = buffer;
                    m_Index = -1;
                }

                public ServerAuthoritativeReliableEvent Current => m_Buffer[m_Index];
                object System.Collections.IEnumerator.Current => Current;
                public bool MoveNext() => ++m_Index < m_Buffer.Count;
                public void Reset() => m_Index = -1;
                public void Dispose() { }
            }

            readonly struct ReliablePosition
            {
                internal ReliablePosition(SimulationEventHeader header, int index, bool gameplay)
                {
                    Header = header;
                    Index = index;
                    Gameplay = gameplay;
                }

                internal SimulationEventHeader Header { get; }
                internal int Index { get; }
                internal bool Gameplay { get; }
            }

            static int ComparePositions(ReliablePosition left, ReliablePosition right)
            {
                int tick = left.Header.Tick.CompareTo(right.Header.Tick);
                if (tick != 0)
                    return tick;
                int sequence = left.Header.Sequence.CompareTo(right.Header.Sequence);
                return sequence != 0 ? sequence : left.Header.EventId.CompareTo(right.Header.EventId);
            }
        }

        AuthoritativeActorBaseline BuildBaseline(
            SimulationActorTickResult actor,
            Float32CompletedSimulationStep completed,
            AuthorityReplicationEgressReads ports)
        {
            int actorIndex = FindActorInput(completed.Step, actor.ActorId);
            ulong inputSequence = completed.Step.Inputs[actorIndex].Sequence;
            ServerAuthoritativeEventHorizon horizon = GetHorizon(actor.ActorId);
            return new AuthoritativeActorBaseline(
                actor.ActorId,
                completed.Step.Tick,
                actor.State.NumericProfile,
                actor.State.NumericProfile.AbiVersion,
                Float32CharacterRuntimeStateCodec.CodecIdentity,
                actor.State.GameplayContentHash,
                ports.CharacterRuntime.Runtime.OperationSetVersion,
                actor.State.CanonicalPayload.Bytes,
                actor.State.CanonicalPayload.Hash,
                completed.State.WorldState.WorldRevision,
                completed.State.WorldState.SolverId,
                completed.State.WorldState.SolverVersion,
                ports.Solver.Solver.Descriptor.Capabilities,
                actor.BodySample.FinalBody,
                inputSequence,
                horizon,
                m_BodyHashWriter);
        }

        static int FindActorInput(Float32SimulationStep step, ActorId actorId)
        {
            for (int i = 0; i < step.Inputs.Count; i++)
            {
                if (step.Inputs[i].ActorId == actorId)
                    return i;
            }
            throw new InvalidOperationException($"Authority Step has no input for Actor '{actorId}'.");
        }

        static int FindAcceptedInput(AcceptedAuthorityInputBatch batch, ActorId actorId)
        {
            for (int i = 0; i < batch.Inputs.Count; i++)
            {
                if (batch.Inputs[i].ActorId == actorId)
                    return i;
            }
            throw new InvalidOperationException($"Authority accepted input batch has no Actor '{actorId}'.");
        }

        void AdvanceHorizon(ActorId actorId, SimulationEventHeader header)
        {
            int horizonIndex = FindHorizon(actorId);
            if (horizonIndex < 0)
                InsertHorizon(actorId, new ServerAuthoritativeEventHorizon(header.Sequence, header.EventId));
            else if (header.Sequence > m_Horizons[horizonIndex].Sequence)
                m_Horizons[horizonIndex] = new ServerAuthoritativeEventHorizon(header.Sequence, header.EventId);
        }

        ServerAuthoritativeEventHorizon GetHorizon(ActorId actorId)
        {
            int horizonIndex = FindHorizon(actorId);
            return horizonIndex < 0 ? ServerAuthoritativeEventHorizon.Empty : m_Horizons[horizonIndex];
        }

        int FindHorizon(ActorId actorId)
        {
            int left = 0;
            int right = m_HorizonCount - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                int comparison = m_HorizonActors[middle].CompareTo(actorId);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }

        void InsertHorizon(ActorId actorId, ServerAuthoritativeEventHorizon horizon)
        {
            int insertionIndex = FindHorizon(actorId);
            if (insertionIndex >= 0)
                throw new InvalidOperationException($"Authority replication horizon Actor '{actorId}' already exists.");
            insertionIndex = ~insertionIndex;
            if (m_HorizonCount >= m_HorizonActors.Length)
                throw new InvalidOperationException("Authority replication horizon capacity is exhausted.");
            Array.Copy(m_HorizonActors, insertionIndex, m_HorizonActors, insertionIndex + 1, m_HorizonCount - insertionIndex);
            Array.Copy(m_Horizons, insertionIndex, m_Horizons, insertionIndex + 1, m_HorizonCount - insertionIndex);
            actorId.Retain();
            m_HorizonActors[insertionIndex] = actorId;
            m_Horizons[insertionIndex] = horizon;
            m_HorizonCount++;
        }

        public SimulationPipelinePassStateSnapshot CaptureState()
        {
            RequireCaptureOrRestore();
            SimulationPipelinePassStateStorage.Page page = m_StateSnapshots.BeginWrite();
            try
            {
                WriteCheckpointState(page.Writer);
                return page.Commit();
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            RequireCaptureOrRestore();
            return m_Restore.Prepare(snapshot);
        }

        public void WriteCheckpointState(CanonicalWriter writer)
        {
            RequireCaptureOrRestore();
            writer.WriteUInt32(0x52494153);
            writer.WriteInt32(1);
            writer.WriteInt32(m_HorizonCount);
            for (int i = 0; i < m_HorizonCount; i++)
            {
                writer.WriteText(m_HorizonActors[i].Value);
                writer.WriteUInt64(m_Horizons[i].Sequence);
                writer.WriteEventId(m_Horizons[i].EventId);
            }
        }

        void Restore(CanonicalReader reader)
        {
            if (reader.ReadUInt32() != 0x52494153 || reader.ReadInt32() != 1)
                throw new InvalidDataException("Authority replication state header is invalid.");
            int count = reader.ReadInt32();
            if (count < 0 || count > m_HorizonActors.Length)
                throw new InvalidDataException("Authority replication state count is invalid.");
            ClearHorizons();
            for (int i = 0; i < count; i++)
            {
                CanonicalTextValue actorText = reader.ReadText();
                var actorId = new ActorId(actorText);
                if (!actorText.Equals(actorId.Value))
                    throw new InvalidDataException("Authority replication Actor identity is not canonical.");
                ulong sequence = reader.ReadUInt64();
                var eventId = reader.ReadEventId();
                if (i > 0 && m_HorizonActors[i - 1].CompareTo(actorId) >= 0)
                    throw new InvalidDataException("Authority replication state Actor order is invalid.");
                var horizon = new ServerAuthoritativeEventHorizon(sequence, eventId);
                actorId.Retain();
                m_HorizonActors[i] = actorId;
                m_Horizons[i] = horizon;
                m_HorizonCount++;
            }
            reader.RequireComplete();
        }

        void ClearHorizons()
        {
            for (int index = 0; index < m_HorizonCount; index++)
                m_HorizonActors[index].Release();
            Array.Clear(m_HorizonActors, 0, m_HorizonCount);
            Array.Clear(m_Horizons, 0, m_HorizonCount);
            m_HorizonCount = 0;
        }

        void RestoreCheckpoint() => RestoreHorizons(m_CheckpointActors, m_CheckpointHorizons, m_CheckpointCount);

        void ReleaseCheckpoint() => ReleaseHorizons(m_CheckpointActors, m_CheckpointHorizons, ref m_CheckpointCount);

        void CaptureHorizons(ActorId[] actors, ServerAuthoritativeEventHorizon[] horizons, out int count)
        {
            count = m_HorizonCount;
            for (int index = 0; index < count; index++)
            {
                ActorId actor = m_HorizonActors[index];
                actor.Retain();
                actors[index] = actor;
                horizons[index] = m_Horizons[index];
            }
        }

        void RestoreHorizons(ActorId[] actors, ServerAuthoritativeEventHorizon[] horizons, int count)
        {
            ClearHorizons();
            for (int index = 0; index < count; index++)
            {
                actors[index].Retain();
                m_HorizonActors[index] = actors[index];
                m_Horizons[index] = horizons[index];
            }
            m_HorizonCount = count;
        }

        static void ReleaseHorizons(ActorId[] actors, ServerAuthoritativeEventHorizon[] horizons, ref int count)
        {
            for (int index = 0; index < count; index++)
                actors[index].Release();
            Array.Clear(actors, 0, count);
            Array.Clear(horizons, 0, count);
            count = 0;
        }

        void ReadState(CanonicalReader reader, ReadOnlyMemory<byte> bytes, ICanonicalTextResolver text)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(text);
            try
            {
                Restore(reader);
            }
            finally
            {
                reader.Reset(Array.Empty<byte>());
                reader.SetTextResolver(null);
            }
        }

        sealed class ReplicationRestoreTransaction : ISimulationPipelinePassRestoreTransaction
        {
            enum Stage : byte { Released, Prepared, Applied, Published }

            readonly AuthorityReplicationEgressPassRuntime m_Owner;
            readonly ActorId[] m_PreviousActors;
            readonly ServerAuthoritativeEventHorizon[] m_PreviousHorizons;
            readonly CanonicalReader m_Reader;
            int m_PreviousCount;
            SimulationPipelinePassStateSnapshot m_Next;
            Stage m_Stage;

            internal ReplicationRestoreTransaction(AuthorityReplicationEgressPassRuntime owner, int actorCapacity)
            {
                m_Owner = owner;
                m_PreviousActors = new ActorId[actorCapacity];
                m_PreviousHorizons = new ServerAuthoritativeEventHorizon[actorCapacity];
                m_Reader = new CanonicalReader(Array.Empty<byte>());
            }

            public SimulationPipelineStateParticipantIdentity Participant => m_Owner.StateIdentity;

            internal ReplicationRestoreTransaction Prepare(SimulationPipelinePassStateSnapshot snapshot)
            {
                if (m_Stage != Stage.Released)
                    throw new InvalidOperationException("Authority replication restore is still held by a Session transaction.");
                m_Owner.CaptureHorizons(m_PreviousActors, m_PreviousHorizons, out m_PreviousCount);
                snapshot.RetainStepStorage();
                m_Next = snapshot;
                m_Stage = Stage.Prepared;
                return this;
            }

            public void Apply()
            {
                if (m_Stage != Stage.Prepared)
                    throw new InvalidOperationException("Authority replication restore is not prepared.");
                m_Stage = Stage.Applied;
                m_Owner.ReadState(m_Reader, m_Next.Payload, m_Next.TextResolver);
            }

            public void ValidateApplied()
            {
                if (m_Stage != Stage.Applied)
                    throw new InvalidOperationException("Authority replication restore has not applied its canonical state.");
            }

            public void CompleteAfterSessionPublish()
            {
                if (m_Stage != Stage.Applied)
                    throw new InvalidOperationException("Authority replication restore has not applied before Session publish.");
                m_Stage = Stage.Published;
            }

            public void Rollback()
            {
                if (m_Stage != Stage.Applied)
                    return;
                m_Owner.RestoreHorizons(m_PreviousActors, m_PreviousHorizons, m_PreviousCount);
                m_Stage = Stage.Prepared;
            }

            public void Dispose()
            {
                try
                {
                    Rollback();
                }
                finally
                {
                    m_Next?.ReleaseStepStorage();
                    m_Next = null;
                    ReleaseHorizons(m_PreviousActors, m_PreviousHorizons, ref m_PreviousCount);
                    m_Stage = Stage.Released;
                }
            }
        }
    }

    public sealed class AuthorityReplicationEgressReads : ISimulationPipelineReadPortSet
    {
        public AuthorityReplicationEgressReads(
            IReadOnlySimulationPipelineProductPort<AcceptedAuthorityInputBatch> accepted,
            IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> results,
            IServerAuthoritativeNetworkSendPort send,
            IServerAuthoritativeFullBaselineRequestSourcePort fullBaselineRequest,
            IFloat32CompletedStepReadPort completed,
            IFloat32CharacterRuntimePort characterRuntime,
            IFloat32WorldSolverRuntimePort solver,
            IFloat32DiagnosticsRuntimePort diagnostics)
        {
            Accepted = accepted ?? throw new ArgumentNullException(nameof(accepted));
            Results = results ?? throw new ArgumentNullException(nameof(results));
            Send = send ?? throw new ArgumentNullException(nameof(send));
            FullBaselineRequest = fullBaselineRequest ?? throw new ArgumentNullException(nameof(fullBaselineRequest));
            Completed = completed ?? throw new ArgumentNullException(nameof(completed));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<AcceptedAuthorityInputBatch> Accepted { get; }
        public IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> Results { get; }
        public IServerAuthoritativeNetworkSendPort Send { get; }
        public IServerAuthoritativeFullBaselineRequestSourcePort FullBaselineRequest { get; }
        public IFloat32CompletedStepReadPort Completed { get; }
        public IFloat32CharacterRuntimePort CharacterRuntime { get; }
        public IFloat32WorldSolverRuntimePort Solver { get; }
        public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class AuthorityReplicationEgressWrites : ISimulationPipelineWritePortSet
    {
        public AuthorityReplicationEgressWrites(
            IExclusiveSimulationPipelineProductWriter<AuthorityReplicationBatch> replication,
            IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> dispositions,
            IAppendOnlySimulationPipelineProductWriter<Float32SourceEgressRecord> egress)
        {
            Replication = replication ?? throw new ArgumentNullException(nameof(replication));
            Dispositions = dispositions ?? throw new ArgumentNullException(nameof(dispositions));
            Egress = egress ?? throw new ArgumentNullException(nameof(egress));
        }

        public IExclusiveSimulationPipelineProductWriter<AuthorityReplicationBatch> Replication { get; }
        public IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> Dispositions { get; }
        public IAppendOnlySimulationPipelineProductWriter<Float32SourceEgressRecord> Egress { get; }
    }
}
