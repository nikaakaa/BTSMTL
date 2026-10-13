using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public static class ServerAuthoritativeSourcePortContracts
    {
        public const string ObservationPortId = "server-authoritative.source.authoritative-observation";
        public const string ObservationSchemaId = "server-authoritative-observation-source";
        public const string AcceptedInputPortId = "server-authoritative.source.accepted-input";
        public const string AcceptedInputSchemaId = "server-authoritative-accepted-input-source";
        public const string AuthorityClockPortId = "server-authoritative.source.authority-clock";
        public const string AuthorityClockSchemaId = "server-authoritative-authority-clock-source";
        public const string FullBaselineRequestPortId = "server-authoritative.source.full-baseline-request";
        public const string FullBaselineRequestSchemaId = "server-authoritative-full-baseline-request-source";
        public const string PredictionSendPortId = "server-authoritative.source.prediction-send";
        public const string PredictionSendSchemaId = "server-authoritative-prediction-send";
        public const string PredictionRestorePortId = "server-authoritative.source.prediction-restore";
        public const string PredictionRestoreSchemaId = "server-authoritative-prediction-restore";
        public const string PredictionStatePortId = "server-authoritative.source.prediction-state";
        public const string PredictionStateSchemaId = "server-authoritative-prediction-state";
        public const string AuthoritySendPortId = "server-authoritative.source.authority-send";
        public const string AuthoritySendSchemaId = "server-authoritative-authority-send";
        public const int SchemaVersion = 2;

        public static SimulationPipelinePortRequirement Observation => Source(
            ObservationPortId,
            ObservationSchemaId,
            SimulationPortDirection.Input);

        public static SimulationPipelinePortRequirement AcceptedInput => Source(
            AcceptedInputPortId,
            AcceptedInputSchemaId,
            SimulationPortDirection.Input);

        public static SimulationPipelinePortRequirement AuthorityClock => Source(
            AuthorityClockPortId,
            AuthorityClockSchemaId,
            SimulationPortDirection.Input);

        public static SimulationPipelinePortRequirement FullBaselineRequest => Source(
            FullBaselineRequestPortId,
            FullBaselineRequestSchemaId,
            SimulationPortDirection.Input);

        public static SimulationPipelinePortRequirement PredictionSend => Source(
            PredictionSendPortId,
            PredictionSendSchemaId,
            SimulationPortDirection.Output);

        public static SimulationPipelinePortRequirement PredictionRestore => Source(
            PredictionRestorePortId,
            PredictionRestoreSchemaId,
            SimulationPortDirection.Input);

        public static SimulationPipelinePortRequirement PredictionState => Source(
            PredictionStatePortId,
            PredictionStateSchemaId,
            SimulationPortDirection.Input);

        public static SimulationPipelinePortRequirement AuthoritySend => Source(
            AuthoritySendPortId,
            AuthoritySendSchemaId,
            SimulationPortDirection.Output);

        static SimulationPipelinePortRequirement Source(
            string portId,
            string schemaId,
            SimulationPortDirection direction)
        {
            return new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Source,
                portId,
                schemaId,
                SchemaVersion,
                direction);
        }

    }

    public interface IServerAuthoritativeObservationSourcePort : ISimulationRuntimePort
    {
        AuthoritativeObservationBatch Drain(SimulationTickSourceIdentity source);
    }

    public interface IServerAuthoritativeAcceptedInputSourcePort : ISimulationRuntimePort
    {
        AcceptedAuthorityInputBatch Read(SimulationTickSourceIdentity source);
    }

    public interface IServerAuthoritativeAuthorityClockSourcePort : ISimulationRuntimePort
    {
        SimulationTick ReadAuthorityTick(SimulationTickSourceIdentity source);
    }

    public interface IServerAuthoritativeFullBaselineRequestSourcePort : ISimulationRuntimePort
    {
        bool IsRequested { get; }
    }

    public interface IServerAuthoritativeNetworkSendPort : ISimulationRuntimePort, IFloat32SourceEgressOutputPort
    {
    }

    public interface IServerAuthoritativePredictionRestorePort : IFloat32SimulationRestoreSource
    {
    }

    public interface IServerAuthoritativePredictionStatePort : ISimulationRuntimePort
    {
        ServerAuthoritativePredictionState State { get; }
    }
}
