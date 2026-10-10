using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ThirdPersonSimulation
{
    public enum SimulationSessionComponentDiagnosticState : byte
    {
        Pending = 1,
        Ready = 2,
        Active = 3,
        Failed = 4,
        Disposed = 5
    }

    public readonly struct SimulationSessionComponentDiagnostic
    {
        public SimulationSessionComponentDiagnostic(
            string component,
            string identity,
            SimulationSessionComponentDiagnosticState state,
            string detail = "")
        {
            if (state is not (SimulationSessionComponentDiagnosticState.Pending or SimulationSessionComponentDiagnosticState.Ready or
                SimulationSessionComponentDiagnosticState.Active or SimulationSessionComponentDiagnosticState.Failed or
                SimulationSessionComponentDiagnosticState.Disposed))
                throw new ArgumentOutOfRangeException(nameof(state));
            Component = SimulationIdentity.Require(component, nameof(component));
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            State = state;
            Detail = detail ?? string.Empty;
        }

        public string Component { get; }
        public string Identity { get; }
        public SimulationSessionComponentDiagnosticState State { get; }
        public string Detail { get; }
    }

    public sealed class SimulationSessionDiagnosticsSnapshot
    {
        readonly ReadOnlyCollection<SimulationSessionComponentDiagnostic> m_Components;

        public SimulationSessionDiagnosticsSnapshot(
            SimulationSessionCompositionDescriptor descriptor,
            SimulationSessionLifecycleState lifecycleState,
            SimulationSessionPreparationStatus preparationStatus,
            ulong latestOuterTick,
            SimulationSessionFailure failure,
            IEnumerable<SimulationSessionComponentDiagnostic> components)
            : this(
                descriptor?.SessionId ?? throw new ArgumentNullException(nameof(descriptor)),
                descriptor,
                lifecycleState,
                preparationStatus,
                latestOuterTick,
                failure,
                components)
        {
        }

        public SimulationSessionDiagnosticsSnapshot(
            SimulationSessionId sessionId,
            SimulationSessionLifecycleState lifecycleState,
            SimulationSessionPreparationStatus preparationStatus,
            ulong latestOuterTick,
            SimulationSessionFailure failure,
            IEnumerable<SimulationSessionComponentDiagnostic> components)
            : this(
                sessionId,
                null,
                lifecycleState,
                preparationStatus,
                latestOuterTick,
                failure,
                components)
        {
        }

        SimulationSessionDiagnosticsSnapshot(
            SimulationSessionId sessionId,
            SimulationSessionCompositionDescriptor descriptor,
            SimulationSessionLifecycleState lifecycleState,
            SimulationSessionPreparationStatus preparationStatus,
            ulong latestOuterTick,
            SimulationSessionFailure failure,
            IEnumerable<SimulationSessionComponentDiagnostic> components)
        {
            if (!sessionId.IsValid || lifecycleState is not (SimulationSessionLifecycleState.Uninitialized or
                    SimulationSessionLifecycleState.Preparing or SimulationSessionLifecycleState.Active or
                    SimulationSessionLifecycleState.Failed or SimulationSessionLifecycleState.Disposed) ||
                preparationStatus is not (SimulationSessionPreparationStatus.Pending or
                    SimulationSessionPreparationStatus.Ready or SimulationSessionPreparationStatus.Failed))
            {
                throw new ArgumentException("Diagnostics lifecycle state is invalid.");
            }
            SessionId = sessionId;
            Descriptor = descriptor;
            LifecycleState = lifecycleState;
            PreparationStatus = preparationStatus;
            LatestOuterTick = latestOuterTick;
            Failure = failure;
            SimulationSessionComponentDiagnostic[] values = components == null
                ? Array.Empty<SimulationSessionComponentDiagnostic>()
                : components.ToArray();
            Array.Sort(values, (left, right) =>
            {
                int component = string.CompareOrdinal(left.Component, right.Component);
                return component != 0 ? component : string.CompareOrdinal(left.Identity, right.Identity);
            });
            for (int i = 1; i < values.Length; i++)
            {
                if (string.Equals(values[i - 1].Component, values[i].Component, StringComparison.Ordinal) &&
                    string.Equals(values[i - 1].Identity, values[i].Identity, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Diagnostics snapshot contains a duplicate component identity.", nameof(components));
                }
            }
            m_Components = Array.AsReadOnly(values);
        }

        public SimulationSessionCompositionDescriptor Descriptor { get; }
        public SimulationSessionId SessionId { get; }
        public bool IsComposed => Descriptor != null;
        public SimulationSessionCompositionIdentity CompositionIdentity => Descriptor?.Identity ?? default;
        public SimulationSessionLifecycleState LifecycleState { get; }
        public SimulationSessionPreparationStatus PreparationStatus { get; }
        public ulong LatestOuterTick { get; }
        public SimulationSessionFailure Failure { get; }
        public IReadOnlyList<SimulationSessionComponentDiagnostic> Components => m_Components;
    }
}
