using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.Threading;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public static class StandardFPLocalPipeline
    {
        public const string PipelineId = "thirdperson.simulation.pipeline.standard-fp-local";
        public const string Revision = "1";

        public static SimulationPipelineDescriptor CreateDescriptor()
        {
            return new SimulationPipelineDescriptor(
                new SimulationPipelineId(PipelineId),
                new SimulationPipelineRevision(Revision),
                new SimulationPipelineSchemaVersion(1),
                new[] { StandardFPLocalPipelinePassContracts.LocalInputIngress },
                new[] { StandardFPLocalPipelinePassContracts.LocalSingleStepSchedule },
                new[]
                {
                    StandardFPPipelinePassContracts.AbilityEvaluate,
                    StandardFPPipelinePassContracts.WorldResolveBatch,
                    StandardFPPipelinePassContracts.AbilityFinalize
                },
                new[] { StandardFPLocalPipelinePassContracts.LocalImmediateOutput });
        }

        public static SimulationPipelinePassFactoryCatalog CreatePortableFactoryCatalog()
        {
            return new SimulationPipelinePassFactoryCatalog(
                FPPassExecutionBackend.Descriptor.Identity,
                new[]
                {
                    StandardFPLocalPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFPLocalPipelinePassContracts.LocalInputIngress),
                    StandardFPLocalPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFPLocalPipelinePassContracts.LocalSingleStepSchedule),
                    StandardFPPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFPPipelinePassContracts.AbilityEvaluate),
                    StandardFPPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFPPipelinePassContracts.WorldResolveBatch),
                    StandardFPPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFPPipelinePassContracts.AbilityFinalize),
                    StandardFPLocalPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFPLocalPipelinePassContracts.LocalImmediateOutput)
                },
                SimulationPipelineProducts.All);
        }

        public static FPSimulationPipelineRuntimePackage CreateRuntimePackage()
        {
            return new FPSimulationPipelineRuntimePackage(
                CreateDescriptor(),
                CreatePortableFactoryCatalog(),
                new FPPipelinePassRuntimeFactoryCatalog(new IFPPipelinePassRuntimeFactory[]
                {
                    new FPLocalInputIngressPassRuntimeFactory(),
                    new FPLocalSingleStepSchedulePassRuntimeFactory(),
                    new FPAbilityEvaluatePassRuntimeFactory(),
                    new FPWorldResolveBatchPassRuntimeFactory(),
                    new FPAbilityFinalizePassRuntimeFactory(),
                    new FPLocalImmediateOutputPassRuntimeFactory()
                }),
                FPPassExecutionBackend.CreateProductRuntimeCatalog());
        }
    }

    public sealed class FPImmediateOutputCommitter : IFPSimulationCommitter
    {
        readonly IFPSimulationResultOutputPort m_Output;

        public FPImmediateOutputCommitter(
            SimulationComponentIdentity identity,
            IFPSimulationResultOutputPort output)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.Committer)
                throw new ArgumentException("FP Local Committer identity is invalid.", nameof(identity));
            Identity = identity;
            m_Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public SimulationComponentIdentity Identity { get; }

        public void Commit(FPSimulationCommitBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Commit batch is invalid.", nameof(batch));
            if (batch.Steps.Count != 1 || batch.SourceEgress.Count != 0 ||
                batch.Steps[0].Step.ExecutionKind != SimulationPipelineStepExecutionKind.Forward)
            {
                throw new InvalidOperationException("FP Local Committer requires exactly one forward Step and no Source egress.");
            }
            for (int i = 0; i < batch.OutputDispositions.Dispositions.Count; i++)
            {
                if (batch.OutputDispositions.Dispositions[i].Kind != SimulationOutputDispositionKind.Publish)
                    throw new InvalidOperationException("FP Local Committer accepts only immediate Publish dispositions.");
            }
            FPCompletedSimulationStep step = batch.Steps[0];
            m_Output.BeginCommit();
            try
            {
                for (int actorIndex = 0; actorIndex < step.Result.Actors.Count; actorIndex++)
                {
                    SimulationActorTickResult actor = step.Result.Actors[actorIndex];
                    for (int commandIndex = 0; commandIndex < actor.PresentationCommands.Count; commandIndex++)
                        m_Output.Publish(actor.PresentationCommands[commandIndex]);
                    m_Output.ObservePublished(actor);
                }
                m_Output.CompleteCommit(step.Step.Tick.Value);
            }
            catch
            {
                m_Output.AbortCommit();
                throw;
            }
        }
    }

    public sealed class FPLocalSimulationRuntimeLauncher : IFPSimulationSessionRuntimeLauncher
    {
        readonly SimulationComponentIdentity m_SourceIdentity;
        int m_Launched;

        public FPLocalSimulationRuntimeLauncher(SimulationSessionSourceDescriptor source)
        {
            if (source == null || source.Model.HasValue || source.Endpoint.HasValue || source.Protocol.HasValue ||
                !source.RequiredPipelineId.Equals(new SimulationPipelineId(StandardFPLocalPipeline.PipelineId)))
            {
                throw new ArgumentException("FP Local Runtime Launcher requires a model-neutral Local FP Session Source.", nameof(source));
            }
            m_SourceIdentity = source.Identity;
            Descriptor = new FPSimulationSessionRuntimeLauncherDescriptor(
                "thirdperson.simulation.runtime-launcher.fp-local",
                "1",
                source.NumericProfileId,
                source.TargetAbiVersion,
                StableHash.Compute(
                    "fp-local-runtime-launcher/1",
                    source.Identity.ToString(),
                    source.RequiredPipelineId.ToString()));
        }

        public FPSimulationSessionRuntimeLauncherDescriptor Descriptor { get; }

        public FPPassBackendCompositionResult Launch(FPSimulationSessionCompositionRequest request)
        {
            if (Interlocked.Exchange(ref m_Launched, 1) != 0)
                throw new InvalidOperationException("FP Local Runtime Launcher is single-use.");
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Source.Identity.Equals(m_SourceIdentity) ||
                !request.PipelineRuntimePackage.Pipeline.PipelineId.Equals(
                    new SimulationPipelineId(StandardFPLocalPipeline.PipelineId)) ||
                request.RestoreSource != null || request.Committer is not FPImmediateOutputCommitter)
            {
                throw new InvalidOperationException("FP Local composition is missing its formal Pipeline or immediate Committer.");
            }
            if (!request.SolverDefinition.Deterministic ||
                (request.SolverDefinition.Capabilities & RequiredWorldCapabilities) != RequiredWorldCapabilities)
            {
                throw new InvalidOperationException("FP Local composition requires the deterministic reconstructible KCC Solver.");
            }
            return FPSimulationSessionComposer.Compose(request);
        }

        public static WorldCapability RequiredWorldCapabilities =>
            WorldCapability.BodyMotion |
            WorldCapability.Grounding |
            WorldCapability.Collision |
            WorldCapability.Reconstructible |
            WorldCapability.AirborneVerticalMotion;
    }
}
