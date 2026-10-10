using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public enum SimulationSessionLifecycleState : byte
    {
        Uninitialized = 1,
        Preparing = 2,
        Active = 3,
        Failed = 4,
        Disposed = 5
    }

    public enum SimulationSessionPreparationStatus : byte
    {
        Pending = 1,
        Ready = 2,
        Failed = 3
    }

    public enum SimulationSessionFailureStage : byte
    {
        Composition = 1,
        Preparation = 2,
        PipelineCompilation = 3,
        Runtime = 4,
        Ingress = 5,
        Schedule = 6,
        Step = 7,
        Egress = 8,
        Commit = 9,
        Disposal = 10
    }

    public sealed class SimulationSessionFailure
    {
        public SimulationSessionFailure(
            SimulationSessionFailureStage stage,
            string code,
            string message,
            string componentIdentity = "",
            string passIdentity = "",
            string productIdentity = "")
        {
            if (!Enum.IsDefined(typeof(SimulationSessionFailureStage), stage))
                throw new ArgumentOutOfRangeException(nameof(stage));
            Stage = stage;
            Code = SimulationIdentity.Require(code, nameof(code));
            Message = SimulationIdentity.Require(message, nameof(message));
            ComponentIdentity = componentIdentity ?? string.Empty;
            PassIdentity = passIdentity ?? string.Empty;
            ProductIdentity = productIdentity ?? string.Empty;
        }

        public SimulationSessionFailureStage Stage { get; }
        public string Code { get; }
        public string Message { get; }
        public string ComponentIdentity { get; }
        public string PassIdentity { get; }
        public string ProductIdentity { get; }
        public override string ToString() => $"{Stage}:{Code} {Message}";
    }

    public sealed class SimulationSessionCompositionException : InvalidOperationException
    {
        public SimulationSessionCompositionException(SimulationSessionFailure failure, Exception innerException = null)
            : base(failure?.ToString() ?? throw new ArgumentNullException(nameof(failure)), innerException)
        {
            Failure = failure;
        }

        public SimulationSessionFailure Failure { get; }
        public SimulationSessionFailureStage Stage => Failure.Stage;
        public string ComponentIdentity => Failure.ComponentIdentity;
        public string PassIdentity => Failure.PassIdentity;
        public string ProductIdentity => Failure.ProductIdentity;
    }

    public readonly struct SimulationSessionLogicTickContext
    {
        public SimulationSessionLogicTickContext(
            SimulationTickSourceIdentity source,
            WorldRevision worldRevision,
            long elapsedTimeTicks)
        {
            if (CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0 ||
                string.IsNullOrEmpty(worldRevision.Value) || elapsedTimeTicks <= 0)
            {
                throw new ArgumentException("Session LogicTick context is incomplete.");
            }
            Source = source;
            WorldRevision = worldRevision;
            ElapsedTimeTicks = elapsedTimeTicks;
        }

        public SimulationTickSourceIdentity Source { get; }
        public WorldRevision WorldRevision { get; }
        public long ElapsedTimeTicks { get; }
    }

    public interface ISimulationSessionPreparation : IDisposable
    {
        SimulationSessionPreparationStatus Status { get; }
        SimulationSessionFailure Failure { get; }
        SimulationSessionLaunchPlan LaunchPlan { get; }
        SimulationSessionDiagnosticsSnapshot Diagnostics { get; }
        SimulationSessionPreparationStatus Step(SimulationSessionLogicTickContext context);
    }

    public interface ISimulationSessionRuntimeHandle : IDisposable
    {
        SimulationSessionCompositionDescriptor Descriptor { get; }
        SimulationSessionLifecycleState LifecycleState { get; }
        SimulationSessionFailure Failure { get; }
        SimulationSessionDiagnosticsSnapshot Diagnostics { get; }
        void LogicTick(SimulationSessionLogicTickContext context);
    }

    public interface ISimulationSessionLocalCombatClock
    {
        float WorldTimeScale { get; }
    }

    public enum SimulationSessionResourceReleasePhase : byte
    {
        RuntimeAndPasses = 1,
        SourceAndEndpoint = 2,
        Solver = 3,
        ActorAndPresentationRegistration = 4
    }

    public sealed class SimulationSessionResourceRegistry : IDisposable
    {
        readonly List<IDisposable>[] m_Resources =
        {
            null,
            new List<IDisposable>(),
            new List<IDisposable>(),
            new List<IDisposable>(),
            new List<IDisposable>()
        };
        bool m_Disposed;

        public void Register(SimulationSessionResourceReleasePhase phase, IDisposable resource)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(SimulationSessionResourceRegistry));
            if (!Enum.IsDefined(typeof(SimulationSessionResourceReleasePhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (resource == null)
                throw new ArgumentNullException(nameof(resource));
            for (int i = 1; i < m_Resources.Length; i++)
            {
                for (int j = 0; j < m_Resources[i].Count; j++)
                {
                    if (ReferenceEquals(m_Resources[i][j], resource))
                        throw new InvalidOperationException("Session resource is already registered.");
                }
            }
            m_Resources[(int)phase].Add(resource);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            List<Exception> errors = null;
            for (int phase = 1; phase < m_Resources.Length; phase++)
            {
                List<IDisposable> resources = m_Resources[phase];
                for (int i = resources.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        resources[i].Dispose();
                    }
                    catch (Exception exception)
                    {
                        if (errors == null)
                            errors = new List<Exception>();
                        errors.Add(exception);
                    }
                }
                resources.Clear();
            }
            if (errors != null)
                throw new AggregateException("One or more Simulation Session resources failed to dispose.", errors);
        }
    }

    public sealed class SimulationSessionLifecycleController
    {
        readonly SimulationSessionCompositionDescriptor m_Descriptor;
        SimulationSessionCompositionIdentity m_ActiveIdentity;

        public SimulationSessionLifecycleController(SimulationSessionCompositionDescriptor descriptor)
        {
            m_Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            State = SimulationSessionLifecycleState.Uninitialized;
        }

        public SimulationSessionLifecycleState State { get; private set; }
        public SimulationSessionFailure Failure { get; private set; }

        public void BeginPreparing()
        {
            RequireState(SimulationSessionLifecycleState.Uninitialized);
            State = SimulationSessionLifecycleState.Preparing;
        }

        public void Activate(SimulationSessionCompositionDescriptor current)
        {
            RequireState(SimulationSessionLifecycleState.Preparing);
            RequireSameComposition(current);
            m_ActiveIdentity = current.Identity;
            State = SimulationSessionLifecycleState.Active;
        }

        public void RequireActive(SimulationSessionCompositionDescriptor current)
        {
            RequireState(SimulationSessionLifecycleState.Active);
            RequireSameComposition(current);
            if (!m_ActiveIdentity.Equals(current.Identity))
                throw HotSwitchException(current);
        }

        public void Fail(SimulationSessionFailure failure)
        {
            if (State == SimulationSessionLifecycleState.Disposed)
                throw new ObjectDisposedException(nameof(SimulationSessionLifecycleController));
            Failure = failure ?? throw new ArgumentNullException(nameof(failure));
            State = SimulationSessionLifecycleState.Failed;
        }

        public void MarkDisposed()
        {
            State = SimulationSessionLifecycleState.Disposed;
        }

        void RequireSameComposition(SimulationSessionCompositionDescriptor current)
        {
            if (current == null || !m_Descriptor.Identity.Equals(current.Identity))
                throw HotSwitchException(current);
        }

        SimulationSessionCompositionException HotSwitchException(SimulationSessionCompositionDescriptor current)
        {
            return new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                "active_composition_changed",
                "Active Simulation Session composition is immutable; destroy and recreate the Session.",
                current?.Identity.ToString() ?? "missing"));
        }

        void RequireState(SimulationSessionLifecycleState expected)
        {
            if (State != expected)
                throw new InvalidOperationException($"Simulation Session lifecycle is '{State}', expected '{expected}'.");
        }
    }
}
