using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public interface ISimulationPipelineReadPortSet
    {
    }

    public interface ISimulationPipelineWritePortSet
    {
    }

    public interface IReadOnlySimulationPipelineProductPort<T>
    {
        SimulationPipelineProductContract Contract { get; }
        bool HasValue { get; }
        T Read();
    }

    public interface IExclusiveSimulationPipelineProductWriter<T>
    {
        SimulationPipelineProductContract Contract { get; }
        void Write(T value);
    }

    public interface IAppendOnlySimulationPipelineProductWriter<T>
    {
        SimulationPipelineProductContract Contract { get; }
        void Append(SimulationPipelineAppendEntryIdentity identity, T value);
    }

    public readonly struct SimulationPipelineAppendProductEntry<T>
    {
        public SimulationPipelineAppendProductEntry(SimulationPipelineAppendEntryIdentity identity, T value)
        {
            Identity = identity;
            Value = value;
        }

        public SimulationPipelineAppendEntryIdentity Identity { get; }
        public T Value { get; }
    }

    public interface IReadOnlySimulationPipelineAppendPort<T>
    {
        SimulationPipelineProductContract Contract { get; }
        int Count { get; }
        SimulationPipelineAppendProductEntry<T> Get(int index);
    }

    public readonly struct SimulationPipelineIngressContext
    {
        public SimulationPipelineIngressContext(
            SimulationSessionCompositionIdentity session,
            SimulationPipelineIdentity pipeline,
            SimulationTickSourceIdentity source,
            ulong currentCompletedTick)
        {
            if (!session.IsValid || !pipeline.IsValid || CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0)
                throw new ArgumentException("Ingress context identity is incomplete.");
            Session = session;
            Pipeline = pipeline;
            Source = source;
            CurrentCompletedTick = currentCompletedTick;
        }

        public SimulationSessionCompositionIdentity Session { get; }
        public SimulationPipelineIdentity Pipeline { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong CurrentCompletedTick { get; }
    }

    public readonly struct SimulationPipelineScheduleContext
    {
        public SimulationPipelineScheduleContext(
            SimulationSessionCompositionIdentity session,
            SimulationPipelineIdentity pipeline,
            SimulationTickSourceIdentity source,
            ulong currentCompletedTick)
        {
            if (!session.IsValid || !pipeline.IsValid || CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0)
                throw new ArgumentException("Schedule context identity is incomplete.");
            Session = session;
            Pipeline = pipeline;
            Source = source;
            CurrentCompletedTick = currentCompletedTick;
        }

        public SimulationSessionCompositionIdentity Session { get; }
        public SimulationPipelineIdentity Pipeline { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong CurrentCompletedTick { get; }
    }

    public readonly struct SimulationPipelineStepTransactionContext
    {
        public SimulationPipelineStepTransactionContext(
            SimulationSessionCompositionIdentity session,
            SimulationPipelineIdentity pipeline,
            SimulationTick tick,
            SimulationPipelineStepExecutionKind executionKind,
            int stepIndex,
            int stepCount,
            StableHash transactionIdentity)
        {
            if (!session.IsValid || !pipeline.IsValid || !tick.IsValid ||
                (byte)executionKind < (byte)SimulationPipelineStepExecutionKind.Forward ||
                (byte)executionKind > (byte)SimulationPipelineStepExecutionKind.Authoritative ||
                stepIndex < 0 || stepCount <= 0 || stepIndex >= stepCount || !transactionIdentity.IsValid)
            {
                throw new ArgumentException("Step transaction context is incomplete.");
            }
            Session = session;
            Pipeline = pipeline;
            Tick = tick;
            ExecutionKind = executionKind;
            StepIndex = stepIndex;
            StepCount = stepCount;
            TransactionIdentity = transactionIdentity;
        }

        public SimulationSessionCompositionIdentity Session { get; }
        public SimulationPipelineIdentity Pipeline { get; }
        public SimulationTick Tick { get; }
        public SimulationPipelineStepExecutionKind ExecutionKind { get; }
        public int StepIndex { get; }
        public int StepCount { get; }
        public StableHash TransactionIdentity { get; }
    }

    public readonly struct SimulationPipelineEgressContext
    {
        public SimulationPipelineEgressContext(
            SimulationSessionCompositionIdentity session,
            SimulationPipelineIdentity pipeline,
            SimulationTickSourceIdentity source,
            int completedStepCount,
            StableHash transactionIdentity)
        {
            if (!session.IsValid || !pipeline.IsValid || CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0 ||
                completedStepCount < 0 || !transactionIdentity.IsValid)
            {
                throw new ArgumentException("Egress context identity is incomplete.");
            }
            Session = session;
            Pipeline = pipeline;
            Source = source;
            CompletedStepCount = completedStepCount;
            TransactionIdentity = transactionIdentity;
        }

        public SimulationSessionCompositionIdentity Session { get; }
        public SimulationPipelineIdentity Pipeline { get; }
        public SimulationTickSourceIdentity Source { get; }
        public int CompletedStepCount { get; }
        public StableHash TransactionIdentity { get; }
    }

    public interface ISimulationIngressPassRuntime<TReadPorts, TWritePorts> : ISimulationPipelinePassRuntime
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        void Execute(SimulationPipelineIngressContext context, TReadPorts readPorts, TWritePorts writePorts);
    }

    public interface ISimulationScheduleCapacity
    {
        int MaximumStepCount { get; }
        GameplayEffectScheduleCapacity GameplayEffectCapacity { get; }
    }

    public interface ISimulationSchedulePassRuntime<TReadPorts, TWritePorts> : ISimulationPipelinePassRuntime, ISimulationScheduleCapacity
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        void Execute(SimulationPipelineScheduleContext context, TReadPorts readPorts, TWritePorts writePorts);
    }

    public interface ISimulationExecutionPlanSchedulePassRuntime<TReadPorts, TWritePorts> :
        ISimulationSchedulePassRuntime<TReadPorts, TWritePorts>
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
    }

    public interface ISimulationStepPassRuntime<TReadPorts, TWritePorts> : ISimulationPipelinePassRuntime
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        void Execute(SimulationPipelineStepTransactionContext context, TReadPorts readPorts, TWritePorts writePorts);
    }

    public interface ISimulationEgressPassRuntime<TReadPorts, TWritePorts> : ISimulationPipelinePassRuntime
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        void Execute(SimulationPipelineEgressContext context, TReadPorts readPorts, TWritePorts writePorts);
    }

    public enum SimulationPipelinePassRuntimeState : byte
    {
        Created = 1,
        Active = 2,
        Disposed = 3
    }

    public interface ISimulationPipelinePassRuntime : IDisposable
    {
        SimulationPipelinePassDescriptor Descriptor { get; }
        SimulationPipelinePassRuntimeState State { get; }
        void Activate();
    }

    public sealed class SimulationPipelinePassStateSnapshot
    {
        ReadOnlyMemory<byte> m_Payload;
        int m_PayloadLength;
        readonly SimulationPipelineStateParticipantIdentity m_Identity;
        readonly SimulationPipelinePassStateStorage.Page m_StepStorage;

        private SimulationPipelinePassStateSnapshot(
            SimulationPipelineStateParticipantIdentity identity,
            StableHash stateHash,
            byte[] ownedPayload,
            SimulationPipelinePassStateStorage.Page stepStorage = null)
        {
            m_Identity = identity;
            StateHash = stateHash;
            m_Payload = ownedPayload;
            m_PayloadLength = ownedPayload.Length;
            m_StepStorage = stepStorage;
            TextResolver = stepStorage?.Text;
            TextReferences = stepStorage?.Text;
        }

        internal static SimulationPipelinePassStateSnapshot FromStepStorage(
            SimulationPipelineStateParticipantIdentity identity, byte[] payload,
            SimulationPipelinePassStateStorage.Page page) =>
            new SimulationPipelinePassStateSnapshot(identity,
                SimulationCanonicalPayloadHash.Compute(ReadOnlySpan<byte>.Empty), payload, page);

        internal static SimulationPipelinePassStateSnapshot FromPreparedIdentity(
            SimulationPipelineStateParticipantIdentity identity) =>
            new SimulationPipelinePassStateSnapshot(identity,
                SimulationCanonicalPayloadHash.Compute(ReadOnlySpan<byte>.Empty), Array.Empty<byte>());

        internal void SetPreparedPayload(ReadOnlyMemory<byte> payload, StableHash hash, ICanonicalTextResolver resolver)
        {
            m_Payload = payload;
            m_PayloadLength = payload.Length;
            StateHash = hash;
            TextResolver = resolver;
        }

        internal void SetPreparedTextResolver(ICanonicalTextResolver resolver) => TextResolver = resolver;

        internal void ClearPreparedPayload()
        {
            m_Payload = ReadOnlyMemory<byte>.Empty;
            m_PayloadLength = 0;
            TextResolver = null;
        }

        internal void CommitStepPayload(int length)
        {
            m_PayloadLength = length;
            StateHash = SimulationCanonicalPayloadHash.Compute(m_Payload.Span.Slice(0, length));
        }

        internal void RetainStepStorage()
        {
            if (m_StepStorage != null)
                m_StepStorage.Owner.Retain(m_StepStorage);
        }

        internal void ReleaseStepStorage()
        {
            if (m_StepStorage != null)
                m_StepStorage.Owner.Release(m_StepStorage);
        }

        public static SimulationPipelinePassStateSnapshot FromOwnedPayload(
            SimulationPipelinePassId passId,
            SimulationPipelinePassImplementationVersion implementationVersion,
            string stateOwner,
            string stateSchemaId,
            int stateSchemaVersion,
            byte[] ownedPayload)
        {
            if (ownedPayload == null)
                throw new ArgumentNullException(nameof(ownedPayload));
            return new SimulationPipelinePassStateSnapshot(
                new SimulationPipelineStateParticipantIdentity(passId, implementationVersion,
                    stateOwner, stateSchemaId, stateSchemaVersion),
                SimulationCanonicalPayloadHash.Compute(ownedPayload),
                ownedPayload);
        }

        public static SimulationPipelinePassStateSnapshot FromWirePayload(
            SimulationPipelinePassId passId,
            SimulationPipelinePassImplementationVersion implementationVersion,
            string stateOwner,
            string stateSchemaId,
            int stateSchemaVersion,
            StableHash expectedStateHash,
            byte[] ownedPayload)
        {
            if (ownedPayload == null)
                throw new ArgumentNullException(nameof(ownedPayload));
            StableHash computed = SimulationCanonicalPayloadHash.Compute(ownedPayload);
            if (!computed.Equals(expectedStateHash))
                throw new ArgumentException("Pass state payload hash does not match its canonical bytes.", nameof(expectedStateHash));
            return new SimulationPipelinePassStateSnapshot(
                new SimulationPipelineStateParticipantIdentity(passId, implementationVersion,
                    stateOwner, stateSchemaId, stateSchemaVersion),
                computed,
                ownedPayload);
        }

        public SimulationPipelinePassId PassId => m_Identity.PassId;
        public SimulationPipelinePassImplementationVersion ImplementationVersion => m_Identity.ImplementationVersion;
        public string StateOwner => m_Identity.StateOwner;
        public string StateSchemaId => m_Identity.StateSchemaId;
        public int StateSchemaVersion => m_Identity.StateSchemaVersion;
        public ref readonly SimulationPipelineStateParticipantIdentity Identity => ref m_Identity;
        public StableHash StateHash { get; private set; }
        public ReadOnlyMemory<byte> Payload => m_Payload.Slice(0, m_PayloadLength);
        public ICanonicalTextResolver TextResolver { get; private set; }
        public CanonicalTextReferences TextReferences { get; private set; }
        public byte[] CopyPayload() => Payload.ToArray();
    }

    public readonly struct SimulationPipelineReconstructionContext
    {
        public SimulationPipelineReconstructionContext(
            SimulationSessionCompositionIdentity session,
            SimulationPipelineIdentity pipeline,
            GameplayContentHash gameplayContentHash,
            StableHash rosterHash,
            WorldRevision worldRevision)
        {
            if (!session.IsValid || !pipeline.IsValid || !gameplayContentHash.IsValid || !rosterHash.IsValid || string.IsNullOrEmpty(worldRevision.Value))
                throw new ArgumentException("Pass reconstruction context is incomplete.");
            Session = session;
            Pipeline = pipeline;
            GameplayContentHash = gameplayContentHash;
            RosterHash = rosterHash;
            WorldRevision = worldRevision;
        }

        public SimulationSessionCompositionIdentity Session { get; }
        public SimulationPipelineIdentity Pipeline { get; }
        public GameplayContentHash GameplayContentHash { get; }
        public StableHash RosterHash { get; }
        public WorldRevision WorldRevision { get; }
    }

    public interface ISimulationPipelineReconstructiblePass
    {
        void Reconstruct(SimulationPipelineReconstructionContext context);
    }

    public sealed class SimulationPipelinePassLifecycleController
    {
        public SimulationPipelinePassRuntimeState State { get; private set; } = SimulationPipelinePassRuntimeState.Created;

        public void Activate()
        {
            Require(SimulationPipelinePassRuntimeState.Created);
            State = SimulationPipelinePassRuntimeState.Active;
        }

        public void RequireExecution()
        {
            Require(SimulationPipelinePassRuntimeState.Active);
        }

        public void RequireCaptureOrRestore()
        {
            Require(SimulationPipelinePassRuntimeState.Active);
        }

        public void MarkDisposed()
        {
            State = SimulationPipelinePassRuntimeState.Disposed;
        }

        void Require(SimulationPipelinePassRuntimeState expected)
        {
            if (State != expected)
                throw new InvalidOperationException($"Pipeline Pass lifecycle is '{State}', expected '{expected}'.");
        }
    }

    public sealed class SimulationPipelinePassBindingDescriptor
    {
        readonly ReadOnlyCollection<SimulationPortDescriptor> m_SourcePorts;
        readonly ReadOnlyCollection<SimulationPortDescriptor> m_TargetPorts;
        readonly ReadOnlyCollection<SimulationPortDescriptor> m_SolverPorts;
        readonly ReadOnlyCollection<SimulationPortDescriptor> m_DiagnosticsPorts;

        public SimulationPipelinePassBindingDescriptor(
            SimulationPipelinePassDescriptor pass,
            IEnumerable<SimulationPortDescriptor> sourcePorts,
            IEnumerable<SimulationPortDescriptor> targetPorts,
            IEnumerable<SimulationPortDescriptor> solverPorts,
            IEnumerable<SimulationPortDescriptor> diagnosticsPorts)
        {
            Pass = pass ?? throw new ArgumentNullException(nameof(pass));
            m_SourcePorts = Freeze(sourcePorts, nameof(sourcePorts));
            m_TargetPorts = Freeze(targetPorts, nameof(targetPorts));
            m_SolverPorts = Freeze(solverPorts, nameof(solverPorts));
            m_DiagnosticsPorts = Freeze(diagnosticsPorts, nameof(diagnosticsPorts));
            RequireExactPorts(pass, SimulationPipelineBindingPortRole.Source, m_SourcePorts, nameof(sourcePorts));
            RequireExactPorts(pass, SimulationPipelineBindingPortRole.Target, m_TargetPorts, nameof(targetPorts));
            RequireExactPorts(pass, SimulationPipelineBindingPortRole.Solver, m_SolverPorts, nameof(solverPorts));
            RequireExactPorts(pass, SimulationPipelineBindingPortRole.Diagnostics, m_DiagnosticsPorts, nameof(diagnosticsPorts));
        }

        public SimulationPipelinePassDescriptor Pass { get; }
        public IReadOnlyList<SimulationPortDescriptor> SourcePorts => m_SourcePorts;
        public IReadOnlyList<SimulationPortDescriptor> TargetPorts => m_TargetPorts;
        public IReadOnlyList<SimulationPortDescriptor> SolverPorts => m_SolverPorts;
        public IReadOnlyList<SimulationPortDescriptor> DiagnosticsPorts => m_DiagnosticsPorts;

        static ReadOnlyCollection<SimulationPortDescriptor> Freeze(IEnumerable<SimulationPortDescriptor> source, string parameter)
        {
            var values = source == null ? new List<SimulationPortDescriptor>() : new List<SimulationPortDescriptor>(source);
            values.Sort((left, right) => string.CompareOrdinal(left.PortId, right.PortId));
            for (int i = 1; i < values.Count; i++)
            {
                if (string.Equals(values[i - 1].PortId, values[i].PortId, StringComparison.Ordinal))
                    throw new ArgumentException("Pass binding contains duplicate port identity.", parameter);
            }
            return values.AsReadOnly();
        }

        static void RequireExactPorts(
            SimulationPipelinePassDescriptor pass,
            SimulationPipelineBindingPortRole role,
            IReadOnlyList<SimulationPortDescriptor> actual,
            string parameter)
        {
            IReadOnlyList<SimulationPipelinePortRequirement> required = pass.GetPortRequirements(role);
            if (required.Count != actual.Count)
                throw new ArgumentException($"Pass binding {role} port count does not match its declaration.", parameter);
            for (int i = 0; i < required.Count; i++)
            {
                SimulationPipelinePortRequirement requirement = required[i];
                SimulationPortDescriptor port = actual[i];
                if (!string.Equals(port.PortId, requirement.PortId, StringComparison.Ordinal) ||
                    !string.Equals(port.SchemaId, requirement.SchemaId, StringComparison.Ordinal) ||
                    port.SchemaVersion != requirement.SchemaVersion || port.Direction != requirement.Direction)
                {
                    throw new ArgumentException($"Pass binding {role} port '{port.PortId}' does not match its declaration.", parameter);
                }
            }
        }
    }

    public readonly struct SimulationPipelinePassFactoryIdentity
    {
        public SimulationPipelinePassFactoryIdentity(
            SimulationPipelinePassId passId,
            SimulationPipelinePassImplementationVersion implementationVersion,
            string factoryVersion,
            StableHash bindingSchemaHash)
        {
            if (!passId.IsValid || !implementationVersion.IsValid || !bindingSchemaHash.IsValid)
                throw new ArgumentException("Pass factory identity is incomplete.");
            PassId = passId;
            ImplementationVersion = implementationVersion;
            FactoryVersion = SimulationIdentity.Require(factoryVersion, nameof(factoryVersion));
            BindingSchemaHash = bindingSchemaHash;
        }

        public SimulationPipelinePassId PassId { get; }
        public SimulationPipelinePassImplementationVersion ImplementationVersion { get; }
        public string FactoryVersion { get; }
        public StableHash BindingSchemaHash { get; }
    }

}
