using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public static class ServerAuthoritativePipelineIdentity
    {
        public const string PredictionPipelineId = "thirdperson.simulation.pipeline.server-authoritative-prediction";
        public const string AuthorityPipelineId = "thirdperson.simulation.pipeline.server-authoritative-authority";
        public const string Revision = "1";
        public const int SchemaVersion = 2;
    }

    public static class ServerAuthoritativePredictionPassIds
    {
        public const string ImplementationVersion = "3";
        public const string OwnerInputIngressValue = "thirdperson.server-authoritative.prediction.owner-input-ingress";
        public const string ObservationIngressValue = "thirdperson.server-authoritative.prediction.observation-ingress";
        public const string CorrectionScheduleValue = "thirdperson.server-authoritative.prediction.correction-schedule";
        public const string HistoryEgressValue = "thirdperson.server-authoritative.prediction.history-egress";
        public const string OutputDispositionValue = "thirdperson.server-authoritative.prediction.output-disposition";
        public const string InputCommandEgressValue = "thirdperson.server-authoritative.prediction.input-command-egress";
        public const string RemotePresentationEgressValue = "thirdperson.server-authoritative.prediction.remote-presentation-egress";
        public const string CorrectionStateOwner = "server-authoritative.prediction.correction";
        public const string HistoryStateOwner = "server-authoritative.prediction.history";
        public const string JournalStateOwner = "server-authoritative.prediction.output-journal";
        public const string CorrectionStateSchema = "server-authoritative-correction-state/5";
        public const int CorrectionStateSchemaVersion = 4;
        public const string HistoryStateSchema = "server-authoritative-prediction-history/5|owner-input:" +
            OwnerCanonicalInputHeader.CanonicalSchema + "|input:" + Float32SimulationInputCodec.Schema;
        public const int HistoryStateSchemaVersion = 5;
        public const string JournalStateSchema = "server-authoritative-event-disposition-journal/3";
        public const int JournalStateSchemaVersion = 3;

        public static readonly SimulationPipelinePassId OwnerInputIngress = new SimulationPipelinePassId(OwnerInputIngressValue);
        public static readonly SimulationPipelinePassId ObservationIngress = new SimulationPipelinePassId(ObservationIngressValue);
        public static readonly SimulationPipelinePassId CorrectionSchedule = new SimulationPipelinePassId(CorrectionScheduleValue);
        public static readonly SimulationPipelinePassId HistoryEgress = new SimulationPipelinePassId(HistoryEgressValue);
        public static readonly SimulationPipelinePassId OutputDisposition = new SimulationPipelinePassId(OutputDispositionValue);
        public static readonly SimulationPipelinePassId InputCommandEgress = new SimulationPipelinePassId(InputCommandEgressValue);
        public static readonly SimulationPipelinePassId RemotePresentationEgress = new SimulationPipelinePassId(RemotePresentationEgressValue);

        public static bool IsPredictionStatePass(SimulationPipelinePassId passId) =>
            passId.Equals(CorrectionSchedule) || passId.Equals(HistoryEgress) || passId.Equals(OutputDisposition);

        public static bool IsPredictionPass(SimulationPipelinePassId passId) =>
            passId.Equals(OwnerInputIngress) ||
            passId.Equals(ObservationIngress) ||
            passId.Equals(CorrectionSchedule) ||
            passId.Equals(HistoryEgress) ||
            passId.Equals(OutputDisposition) ||
            passId.Equals(InputCommandEgress) ||
            passId.Equals(RemotePresentationEgress);
    }

    public static class ServerAuthoritativeAuthorityPassIds
    {
        public const string ImplementationVersion = "1";
        public const string AcceptedInputIngressValue = "thirdperson.server-authoritative.authority.accepted-input-ingress";
        public const string TickScheduleValue = "thirdperson.server-authoritative.authority.tick-schedule";
        public const string ReplicationEgressValue = "thirdperson.server-authoritative.authority.replication-egress";
        public const string ScheduleStateOwner = "server-authoritative.authority.input-hold";
        public const string ScheduleStateSchema = "server-authoritative-authority-input-hold/1";
        public const string ReplicationStateOwner = "server-authoritative.authority.replication";
        public const string ReplicationStateSchema = "server-authoritative-authority-replication-state/1";

        public static readonly SimulationPipelinePassId AcceptedInputIngress = new SimulationPipelinePassId(AcceptedInputIngressValue);
        public static readonly SimulationPipelinePassId TickSchedule = new SimulationPipelinePassId(TickScheduleValue);
        public static readonly SimulationPipelinePassId ReplicationEgress = new SimulationPipelinePassId(ReplicationEgressValue);
    }

    public static class ServerAuthoritativePipelinePassContracts
    {
        static readonly SimulationPipelineExecutionSupport s_PredictionExecution =
            SimulationPipelineExecutionSupport.Forward |
            SimulationPipelineExecutionSupport.Replay |
            SimulationPipelineExecutionSupport.Restore;

        static readonly SimulationPipelineExecutionSupport s_AuthorityExecution =
            SimulationPipelineExecutionSupport.Forward |
            SimulationPipelineExecutionSupport.Authoritative;

        public static SimulationPipelinePassDescriptor OwnerInputIngress(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativePredictionPassIds.OwnerInputIngress,
            SimulationPipelinePhase.Ingress,
            s_PredictionExecution,
            SimulationPipelinePassStateClass.ExternalSource,
            "server-authoritative.prediction-source",
            policy,
            new[] { Produce(ServerAuthoritativeProducts.OwnerCanonicalInputBatch) },
            new[]
            {
                Float32LocalInputSourcePortContract.Requirement,
                Target(Float32PipelineRuntimePortIds.CommittedObservation, Float32PipelineRuntimePortIds.CommittedObservationSchema)
            });

        public static SimulationPipelinePassDescriptor ObservationIngress(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativePredictionPassIds.ObservationIngress,
            SimulationPipelinePhase.Ingress,
            s_PredictionExecution,
            SimulationPipelinePassStateClass.ExternalSource,
            "server-authoritative.prediction-source",
            policy,
            new[]
            {
                Produce(ServerAuthoritativeProducts.AuthoritativeObservationBatch),
                Produce(ServerAuthoritativeProducts.RemotePresentationBatch)
            },
            new[]
            {
                ServerAuthoritativeSourcePortContracts.Observation,
                ServerAuthoritativeSourcePortContracts.PredictionState
            });

        public static SimulationPipelinePassDescriptor CorrectionSchedule(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativePredictionPassIds.CorrectionSchedule,
            SimulationPipelinePhase.Schedule,
            s_PredictionExecution,
            SimulationPipelinePassStateClass.SnapshotParticipant,
            ServerAuthoritativePredictionPassIds.CorrectionStateOwner,
            policy,
            new[]
            {
                Consume(ServerAuthoritativeProducts.OwnerCanonicalInputBatch),
                Consume(ServerAuthoritativeProducts.AuthoritativeObservationBatch),
                Produce(ServerAuthoritativeProducts.PredictionCorrectionDecision),
                Produce(ServerAuthoritativeProducts.SelectedRemoteBodyBatch),
                Produce(SimulationPipelineProducts.ExecutionPlan)
            },
            new[]
            {
                Float32LocalInputSourcePortContract.Requirement,
                ServerAuthoritativeSourcePortContracts.PredictionState,
                ServerAuthoritativeSourcePortContracts.PredictionRestore,
                Target(Float32PipelineRuntimePortIds.WorldSolver, Float32PipelineRuntimePortIds.WorldSolverSchema),
                Diagnostics()
            });

        public static SimulationPipelinePassDescriptor HistoryEgress(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativePredictionPassIds.HistoryEgress,
            SimulationPipelinePhase.Egress,
            s_PredictionExecution,
            SimulationPipelinePassStateClass.SnapshotParticipant,
            ServerAuthoritativePredictionPassIds.HistoryStateOwner,
            policy,
            new[] { Consume(ServerAuthoritativeProducts.OwnerCanonicalInputBatch) },
            new[]
            {
                ServerAuthoritativeSourcePortContracts.PredictionState,
                Target(Float32PipelineRuntimePortIds.CompletedSteps, Float32PipelineRuntimePortIds.CompletedStepsSchema),
                Diagnostics()
            });

        public static SimulationPipelinePassDescriptor OutputDisposition(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativePredictionPassIds.OutputDisposition,
            SimulationPipelinePhase.Egress,
            s_PredictionExecution,
            SimulationPipelinePassStateClass.SnapshotParticipant,
            ServerAuthoritativePredictionPassIds.JournalStateOwner,
            policy,
            new[]
            {
                Consume(ServerAuthoritativeProducts.PredictionCorrectionDecision),
                Consume(SimulationPipelineProducts.FinalizedStepResult),
                Produce(SimulationPipelineProducts.OutputDispositionSet)
            },
            new[]
            {
                ServerAuthoritativeSourcePortContracts.PredictionState,
                Target(Float32PipelineRuntimePortIds.CompletedSteps, Float32PipelineRuntimePortIds.CompletedStepsSchema),
                Target(Float32PipelineRuntimePortIds.CharacterRuntime, Float32PipelineRuntimePortIds.CharacterRuntimeSchema),
                Diagnostics()
            });

        public static SimulationPipelinePassDescriptor InputCommandEgress(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativePredictionPassIds.InputCommandEgress,
            SimulationPipelinePhase.Egress,
            s_PredictionExecution,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            policy,
            new[]
            {
                Consume(ServerAuthoritativeProducts.OwnerCanonicalInputBatch),
                Append(SimulationPipelineProducts.SourceEgress)
            },
            new[]
            {
                ServerAuthoritativeSourcePortContracts.PredictionSend,
                Target(Float32PipelineRuntimePortIds.CompletedSteps, Float32PipelineRuntimePortIds.CompletedStepsSchema)
            });

        public static SimulationPipelinePassDescriptor RemotePresentationEgress(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativePredictionPassIds.RemotePresentationEgress,
            SimulationPipelinePhase.Egress,
            s_PredictionExecution,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            policy,
            new[]
            {
                Consume(ServerAuthoritativeProducts.RemotePresentationBatch),
                Consume(ServerAuthoritativeProducts.SelectedRemoteBodyBatch),
                Append(SimulationPipelineProducts.SourceEgress)
            },
            Array.Empty<SimulationPipelinePortRequirement>());

        public static SimulationPipelinePassDescriptor AcceptedInputIngress(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativeAuthorityPassIds.AcceptedInputIngress,
            SimulationPipelinePhase.Ingress,
            s_AuthorityExecution,
            SimulationPipelinePassStateClass.ExternalSource,
            "server-authoritative.authority-source",
            policy,
            new[] { Produce(ServerAuthoritativeProducts.AcceptedAuthorityInputBatch) },
            new[] { ServerAuthoritativeSourcePortContracts.AcceptedInput });

        public static SimulationPipelinePassDescriptor AuthorityTickSchedule(ServerAuthoritativeModelPolicy policy) => Create(
            ServerAuthoritativeAuthorityPassIds.TickSchedule,
            SimulationPipelinePhase.Schedule,
            s_AuthorityExecution,
            SimulationPipelinePassStateClass.SnapshotParticipant,
            ServerAuthoritativeAuthorityPassIds.ScheduleStateOwner,
            policy,
            new[]
            {
                Consume(ServerAuthoritativeProducts.AcceptedAuthorityInputBatch),
                Produce(SimulationPipelineProducts.ExecutionPlan)
            },
            new[]
            {
                ServerAuthoritativeSourcePortContracts.AuthorityClock,
                Diagnostics()
            });

        public static SimulationPipelinePassDescriptor AuthorityReplicationEgress(
            ServerAuthoritativeModelPolicy policy,
            ServerAuthoritativeReplicationPolicy replicationPolicy) => Create(
            ServerAuthoritativeAuthorityPassIds.ReplicationEgress,
            SimulationPipelinePhase.Egress,
            s_AuthorityExecution,
            SimulationPipelinePassStateClass.SnapshotParticipant,
            ServerAuthoritativeAuthorityPassIds.ReplicationStateOwner,
            policy,
            new[]
            {
                Consume(ServerAuthoritativeProducts.AcceptedAuthorityInputBatch),
                Consume(SimulationPipelineProducts.FinalizedStepResult),
                Produce(ServerAuthoritativeProducts.AuthorityReplicationBatch),
                Produce(SimulationPipelineProducts.OutputDispositionSet),
                Append(SimulationPipelineProducts.SourceEgress)
            },
            new[]
            {
                ServerAuthoritativeSourcePortContracts.AuthoritySend,
                ServerAuthoritativeSourcePortContracts.FullBaselineRequest,
                Target(Float32PipelineRuntimePortIds.CompletedSteps, Float32PipelineRuntimePortIds.CompletedStepsSchema),
                Target(Float32PipelineRuntimePortIds.CharacterRuntime, Float32PipelineRuntimePortIds.CharacterRuntimeSchema),
                Solver(),
                Diagnostics()
            },
            (replicationPolicy ?? throw new ArgumentNullException(nameof(replicationPolicy))).ConfigurationHash);

        public static SimulationPipelinePassFactoryDescriptor CreateFactoryDescriptor(
            SimulationPipelinePassDescriptor descriptor,
            string stateSchema = "",
            int stateSchemaVersion = 2)
        {
            bool snapshot = descriptor.StateClass == SimulationPipelinePassStateClass.SnapshotParticipant;
            return new SimulationPipelinePassFactoryDescriptor(
                new SimulationPipelinePassFactoryIdentity(
                    descriptor.PassId,
                    descriptor.ImplementationVersion,
                    "1",
                    StableHash.Compute("server-authoritative-float32-pass-binding/1", descriptor.DescriptorHash.ToString())),
                descriptor.Phase,
                Float32PassExecutionBackend.BackendId,
                Float32PassExecutionBackend.SemanticVersion,
                descriptor.ConfigurationHash,
                descriptor.ExecutionSupport,
                false,
                snapshot,
                snapshot,
                false,
                snapshot ? stateSchema : string.Empty,
                snapshot ? stateSchemaVersion : 0);
        }

        static SimulationPipelinePassDescriptor Create(
            SimulationPipelinePassId passId,
            SimulationPipelinePhase phase,
            SimulationPipelineExecutionSupport support,
            SimulationPipelinePassStateClass stateClass,
            string stateOwner,
            ServerAuthoritativeModelPolicy policy,
            IEnumerable<SimulationPipelineProductAccess> products,
            IEnumerable<SimulationPipelinePortRequirement> ports,
            StableHash configurationExtension = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            StableHash configurationHash = configurationExtension.IsValid
                ? StableHash.Compute(
                    "server-authoritative-pass-config/2",
                    passId.Value,
                    policy.ConfigurationHash.ToString(),
                    configurationExtension.ToString())
                : StableHash.Compute(
                    "server-authoritative-pass-config/1",
                    passId.Value,
                    policy.ConfigurationHash.ToString());
            return new SimulationPipelinePassDescriptor(
                passId,
                new SimulationPipelinePassImplementationVersion(
                    ServerAuthoritativePredictionPassIds.IsPredictionPass(passId)
                        ? ServerAuthoritativePredictionPassIds.ImplementationVersion
                        : ServerAuthoritativeAuthorityPassIds.ImplementationVersion),
                phase,
                configurationHash,
                Float32SimulationNumericProfile.Value.Id,
                Float32SimulationNumericProfile.Value.AbiVersion,
                Float32PassExecutionBackend.BackendId,
                Float32PassExecutionBackend.SemanticVersion,
                WorldCapability.None,
                support,
                stateClass,
                stateOwner,
                products,
                ports);
        }

        static SimulationPipelineProductAccess Produce(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.ExclusiveProducer);
        static SimulationPipelineProductAccess Append(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.AppendOnlyProducer);
        static SimulationPipelineProductAccess Consume(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.ReadOnlyConsumer);
        static SimulationPipelinePortRequirement Target(string portId, string schemaId) =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Target,
                portId,
                schemaId,
                1,
                SimulationPortDirection.Input);

        static SimulationPipelinePortRequirement Solver() =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Solver,
                Float32PipelineRuntimePortIds.WorldSolver,
                Float32PipelineRuntimePortIds.WorldSolverSchema,
                1,
                SimulationPortDirection.Input);

        static SimulationPipelinePortRequirement Diagnostics() =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Diagnostics,
                Float32PipelineRuntimePortIds.Diagnostics,
                Float32PipelineRuntimePortIds.DiagnosticsSchema,
                1,
                SimulationPortDirection.Input);
    }
}
