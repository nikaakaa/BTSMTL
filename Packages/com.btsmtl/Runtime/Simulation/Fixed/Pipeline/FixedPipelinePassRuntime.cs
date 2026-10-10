using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation.Fixed
{
    public interface IFixedCompiledPipelinePassRuntime : ICompiledSimulationPipelinePassRuntime
    {
    }

    public abstract class FixedPipelinePassRuntimeBase : ISimulationPipelinePassRuntime
    {
        readonly SimulationPipelinePassLifecycleController m_Lifecycle = new SimulationPipelinePassLifecycleController();

        protected FixedPipelinePassRuntimeBase(SimulationPipelinePassDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public SimulationPipelinePassDescriptor Descriptor { get; }
        public SimulationPipelinePassRuntimeState State => m_Lifecycle.State;

        public void Activate()
        {
            m_Lifecycle.Activate();
            OnActivate();
        }

        protected void RequireExecution() => m_Lifecycle.RequireExecution();
        protected void RequireCaptureOrRestore() => m_Lifecycle.RequireCaptureOrRestore();
        protected virtual void OnActivate() { }
        protected virtual void OnDispose() { }

        public void Dispose()
        {
            if (m_Lifecycle.State == SimulationPipelinePassRuntimeState.Disposed)
                return;
            OnDispose();
            m_Lifecycle.MarkDisposed();
        }
    }

    public sealed class FixedIngressPassRuntimeAdapter<TReadPorts, TWritePorts> : IFixedCompiledPipelinePassRuntime
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        readonly ISimulationIngressPassRuntime<TReadPorts, TWritePorts> m_Runtime;
        readonly TReadPorts m_ReadPorts;
        readonly TWritePorts m_WritePorts;

        public FixedIngressPassRuntimeAdapter(
            ISimulationIngressPassRuntime<TReadPorts, TWritePorts> runtime,
            TReadPorts readPorts,
            TWritePorts writePorts)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_ReadPorts = readPorts;
            m_WritePorts = writePorts;
        }

        public SimulationPipelinePassDescriptor Descriptor => m_Runtime.Descriptor;
        public SimulationPipelinePhase Phase => SimulationPipelinePhase.Ingress;
        public SimulationPipelinePassRuntimeState State => m_Runtime.State;
        public ISimulationPipelineStateParticipant StateParticipant => m_Runtime as ISimulationPipelineStateParticipant;
        public ISimulationPipelineReconstructiblePass Reconstructible => m_Runtime as ISimulationPipelineReconstructiblePass;
        public void Activate() => m_Runtime.Activate();
        public void Execute(SimulationPipelineIngressContext context) => m_Runtime.Execute(context, m_ReadPorts, m_WritePorts);
        public void Execute(SimulationPipelineScheduleContext context) => WrongPhase();
        public void Execute(SimulationPipelineStepTransactionContext context) => WrongPhase();
        public void Execute(SimulationPipelineEgressContext context) => WrongPhase();
        public void Dispose() => m_Runtime.Dispose();
        void WrongPhase() => throw new InvalidOperationException($"Ingress Pass '{Descriptor.PassId}' cannot execute in another phase.");
    }

    public sealed class FixedSchedulePassRuntimeAdapter<TReadPorts, TWritePorts> : IFixedCompiledPipelinePassRuntime, ISimulationScheduleCapacity
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        readonly ISimulationSchedulePassRuntime<TReadPorts, TWritePorts> m_Runtime;
        public int MaximumStepCount => m_Runtime.MaximumStepCount;
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => m_Runtime.GameplayEffectCapacity;
        readonly TReadPorts m_ReadPorts;
        readonly TWritePorts m_WritePorts;

        public FixedSchedulePassRuntimeAdapter(
            ISimulationSchedulePassRuntime<TReadPorts, TWritePorts> runtime,
            TReadPorts readPorts,
            TWritePorts writePorts)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_ReadPorts = readPorts;
            m_WritePorts = writePorts;
        }

        public SimulationPipelinePassDescriptor Descriptor => m_Runtime.Descriptor;
        public SimulationPipelinePhase Phase => SimulationPipelinePhase.Schedule;
        public SimulationPipelinePassRuntimeState State => m_Runtime.State;
        public ISimulationPipelineStateParticipant StateParticipant => m_Runtime as ISimulationPipelineStateParticipant;
        public ISimulationPipelineReconstructiblePass Reconstructible => m_Runtime as ISimulationPipelineReconstructiblePass;
        public void Activate() => m_Runtime.Activate();
        public void Execute(SimulationPipelineIngressContext context) => WrongPhase();
        public void Execute(SimulationPipelineScheduleContext context) => m_Runtime.Execute(context, m_ReadPorts, m_WritePorts);
        public void Execute(SimulationPipelineStepTransactionContext context) => WrongPhase();
        public void Execute(SimulationPipelineEgressContext context) => WrongPhase();
        public void Dispose() => m_Runtime.Dispose();
        void WrongPhase() => throw new InvalidOperationException($"Schedule Pass '{Descriptor.PassId}' cannot execute in another phase.");
    }

    public sealed class FixedStepPassRuntimeAdapter<TReadPorts, TWritePorts> : IFixedCompiledPipelinePassRuntime
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        readonly ISimulationStepPassRuntime<TReadPorts, TWritePorts> m_Runtime;
        readonly TReadPorts m_ReadPorts;
        readonly TWritePorts m_WritePorts;

        public FixedStepPassRuntimeAdapter(
            ISimulationStepPassRuntime<TReadPorts, TWritePorts> runtime,
            TReadPorts readPorts,
            TWritePorts writePorts)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_ReadPorts = readPorts;
            m_WritePorts = writePorts;
        }

        public SimulationPipelinePassDescriptor Descriptor => m_Runtime.Descriptor;
        public SimulationPipelinePhase Phase => SimulationPipelinePhase.Step;
        public SimulationPipelinePassRuntimeState State => m_Runtime.State;
        public ISimulationPipelineStateParticipant StateParticipant => m_Runtime as ISimulationPipelineStateParticipant;
        public ISimulationPipelineReconstructiblePass Reconstructible => m_Runtime as ISimulationPipelineReconstructiblePass;
        public void Activate() => m_Runtime.Activate();
        public void Execute(SimulationPipelineIngressContext context) => WrongPhase();
        public void Execute(SimulationPipelineScheduleContext context) => WrongPhase();
        public void Execute(SimulationPipelineStepTransactionContext context) => m_Runtime.Execute(context, m_ReadPorts, m_WritePorts);
        public void Execute(SimulationPipelineEgressContext context) => WrongPhase();
        public void Dispose() => m_Runtime.Dispose();
        void WrongPhase() => throw new InvalidOperationException($"Step Pass '{Descriptor.PassId}' cannot execute in another phase.");
    }

    public sealed class FixedEgressPassRuntimeAdapter<TReadPorts, TWritePorts> : IFixedCompiledPipelinePassRuntime
        where TReadPorts : ISimulationPipelineReadPortSet
        where TWritePorts : ISimulationPipelineWritePortSet
    {
        readonly ISimulationEgressPassRuntime<TReadPorts, TWritePorts> m_Runtime;
        readonly TReadPorts m_ReadPorts;
        readonly TWritePorts m_WritePorts;

        public FixedEgressPassRuntimeAdapter(
            ISimulationEgressPassRuntime<TReadPorts, TWritePorts> runtime,
            TReadPorts readPorts,
            TWritePorts writePorts)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_ReadPorts = readPorts;
            m_WritePorts = writePorts;
        }

        public SimulationPipelinePassDescriptor Descriptor => m_Runtime.Descriptor;
        public SimulationPipelinePhase Phase => SimulationPipelinePhase.Egress;
        public SimulationPipelinePassRuntimeState State => m_Runtime.State;
        public ISimulationPipelineStateParticipant StateParticipant => m_Runtime as ISimulationPipelineStateParticipant;
        public ISimulationPipelineReconstructiblePass Reconstructible => m_Runtime as ISimulationPipelineReconstructiblePass;
        public void Activate() => m_Runtime.Activate();
        public void Execute(SimulationPipelineIngressContext context) => WrongPhase();
        public void Execute(SimulationPipelineScheduleContext context) => WrongPhase();
        public void Execute(SimulationPipelineStepTransactionContext context) => WrongPhase();
        public void Execute(SimulationPipelineEgressContext context) => m_Runtime.Execute(context, m_ReadPorts, m_WritePorts);
        public void Dispose() => m_Runtime.Dispose();
        void WrongPhase() => throw new InvalidOperationException($"Egress Pass '{Descriptor.PassId}' cannot execute in another phase.");
    }

    public interface IFixedPipelinePassRuntimeFactory
    {
        SimulationPipelinePassFactoryDescriptor Descriptor { get; }
        IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context);
    }

    public sealed class FixedPipelinePassRuntimeFactoryContext
    {
        readonly List<SimulationPortDescriptor>[] m_BoundPorts =
        {
            null,
            new List<SimulationPortDescriptor>(),
            new List<SimulationPortDescriptor>(),
            new List<SimulationPortDescriptor>(),
            new List<SimulationPortDescriptor>()
        };
        readonly HashSet<string> m_BoundPortIds = new HashSet<string>(StringComparer.Ordinal);
        readonly SimulationRuntimePortSet[] m_PortSets;

        internal FixedPipelinePassRuntimeFactoryContext(
            CompiledSimulationPipelinePass pass,
            FixedPipelineProductStore products,
            SimulationRuntimePortSet sourcePorts,
            SimulationRuntimePortSet targetPorts,
            SimulationRuntimePortSet solverPorts,
            SimulationRuntimePortSet diagnosticsPorts,
            SimulationSessionCompositionDescriptor composition)
        {
            Composition = composition;
            Pass = pass ?? throw new ArgumentNullException(nameof(pass));
            Products = products.Bind(pass.Descriptor);
            m_PortSets = new[]
            {
                null,
                sourcePorts ?? throw new ArgumentNullException(nameof(sourcePorts)),
                targetPorts ?? throw new ArgumentNullException(nameof(targetPorts)),
                solverPorts ?? throw new ArgumentNullException(nameof(solverPorts)),
                diagnosticsPorts ?? throw new ArgumentNullException(nameof(diagnosticsPorts))
            };
        }

        public CompiledSimulationPipelinePass Pass { get; }
        public SimulationSessionCompositionDescriptor Composition { get; }
        public FixedPipelineProductPortBinder Products { get; }

        public TPort BindSourcePort<TPort>(string portId) where TPort : class, ISimulationRuntimePort =>
            BindPort<TPort>(SimulationPipelineBindingPortRole.Source, portId);

        public TPort BindTargetPort<TPort>(string portId) where TPort : class, ISimulationRuntimePort =>
            BindPort<TPort>(SimulationPipelineBindingPortRole.Target, portId);

        public TPort BindSolverPort<TPort>(string portId) where TPort : class, ISimulationRuntimePort =>
            BindPort<TPort>(SimulationPipelineBindingPortRole.Solver, portId);

        public TPort BindDiagnosticsPort<TPort>(string portId) where TPort : class, ISimulationRuntimePort =>
            BindPort<TPort>(SimulationPipelineBindingPortRole.Diagnostics, portId);

        TPort BindPort<TPort>(SimulationPipelineBindingPortRole role, string portId)
            where TPort : class, ISimulationRuntimePort
        {
            SimulationPipelinePortRequirement requirement = FindRequirement(role, portId);
            string key = $"{(int)role}|{portId}";
            if (!m_BoundPortIds.Add(key))
                throw new InvalidOperationException($"Pass '{Pass.Descriptor.PassId}' bound runtime port '{role}:{portId}' more than once.");
            TPort port = m_PortSets[(int)role].GetRequired<TPort>(requirement);
            m_BoundPorts[(int)role].Add(port.Descriptor);
            return port;
        }

        SimulationPipelinePortRequirement FindRequirement(SimulationPipelineBindingPortRole role, string portId)
        {
            IReadOnlyList<SimulationPipelinePortRequirement> requirements = Pass.Descriptor.GetPortRequirements(role);
            for (int i = 0; i < requirements.Count; i++)
            {
                if (string.Equals(requirements[i].PortId, portId, StringComparison.Ordinal))
                    return requirements[i];
            }
            throw new InvalidOperationException($"Pass '{Pass.Descriptor.PassId}' did not declare runtime port '{role}:{portId}'.");
        }

        internal SimulationPipelinePassBindingDescriptor CompleteBindings()
        {
            Products.RequireCompleteBindings();
            for (int role = 1; role < m_BoundPorts.Length; role++)
            {
                IReadOnlyList<SimulationPipelinePortRequirement> required =
                    Pass.Descriptor.GetPortRequirements((SimulationPipelineBindingPortRole)role);
                if (required.Count != m_BoundPorts[role].Count)
                    throw new InvalidOperationException($"Pass '{Pass.Descriptor.PassId}' did not bind every declared runtime port.");
            }
            return new SimulationPipelinePassBindingDescriptor(
                Pass.Descriptor,
                m_BoundPorts[(int)SimulationPipelineBindingPortRole.Source],
                m_BoundPorts[(int)SimulationPipelineBindingPortRole.Target],
                m_BoundPorts[(int)SimulationPipelineBindingPortRole.Solver],
                m_BoundPorts[(int)SimulationPipelineBindingPortRole.Diagnostics]);
        }
    }

    public sealed class FixedPipelinePassRuntimeFactoryCatalog
    {
        readonly ReadOnlyCollection<IFixedPipelinePassRuntimeFactory> m_Factories;

        public FixedPipelinePassRuntimeFactoryCatalog(IEnumerable<IFixedPipelinePassRuntimeFactory> factories)
        {
            var values = factories == null
                ? new List<IFixedPipelinePassRuntimeFactory>()
                : new List<IFixedPipelinePassRuntimeFactory>(factories);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null)
                    throw new ArgumentException("Pass runtime factory catalog contains a missing factory.", nameof(factories));
            }
            values.Sort((left, right) =>
            {
                int pass = left.Descriptor.Identity.PassId.CompareTo(right.Descriptor.Identity.PassId);
                return pass != 0
                    ? pass
                    : string.CompareOrdinal(left.Descriptor.Identity.ImplementationVersion.Value, right.Descriptor.Identity.ImplementationVersion.Value);
            });
            for (int i = 1; i < values.Count; i++)
            {
                if (SameIdentity(values[i - 1].Descriptor, values[i].Descriptor))
                    throw new ArgumentException("Pass runtime factory catalog contains duplicate factory identity.", nameof(factories));
            }
            m_Factories = values.AsReadOnly();
        }

        public IReadOnlyList<IFixedPipelinePassRuntimeFactory> Factories => m_Factories;

        public IFixedPipelinePassRuntimeFactory GetRequired(CompiledSimulationPipelinePass pass)
        {
            for (int i = 0; i < m_Factories.Count; i++)
            {
                IFixedPipelinePassRuntimeFactory factory = m_Factories[i];
                if (!SameIdentity(factory.Descriptor, pass.Factory))
                    continue;
                RequireDescriptorMatch(factory.Descriptor, pass.Factory);
                return factory;
            }
            throw new KeyNotFoundException($"Fixed runtime factory '{pass.Descriptor.PassId}@{pass.Descriptor.ImplementationVersion}' is not installed.");
        }

        static bool SameIdentity(
            SimulationPipelinePassFactoryDescriptor left,
            SimulationPipelinePassFactoryDescriptor right)
        {
            return left.Identity.PassId.Equals(right.Identity.PassId) &&
                   left.Identity.ImplementationVersion.Equals(right.Identity.ImplementationVersion);
        }

        static void RequireDescriptorMatch(
            SimulationPipelinePassFactoryDescriptor actual,
            SimulationPipelinePassFactoryDescriptor expected)
        {
            if (actual.Phase != expected.Phase ||
                !string.Equals(actual.BackendId, expected.BackendId, StringComparison.Ordinal) ||
                !string.Equals(actual.BackendSemanticVersion, expected.BackendSemanticVersion, StringComparison.Ordinal) ||
                !actual.SupportedConfigurationHash.Equals(expected.SupportedConfigurationHash) ||
                actual.ExecutionSupport != expected.ExecutionSupport || actual.Deterministic != expected.Deterministic ||
                actual.SupportsSnapshotCapture != expected.SupportsSnapshotCapture ||
                actual.SupportsSnapshotRestore != expected.SupportsSnapshotRestore ||
                actual.SupportsReconstruction != expected.SupportsReconstruction ||
                !string.Equals(actual.StateSchemaId, expected.StateSchemaId, StringComparison.Ordinal) ||
                actual.StateSchemaVersion != expected.StateSchemaVersion)
            {
                throw new InvalidOperationException($"Fixed runtime factory '{expected.Identity.PassId}' descriptor does not match the compiled plan.");
            }
        }
    }
}

