using System;
using System.Collections.Generic;
using System.Threading;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public static class StandardFixedLocalPipeline
    {
        public const string PipelineId = "thirdperson.simulation.pipeline.standard-fixed-local";
        public const string Revision = "1";

        public static SimulationPipelineDescriptor CreateDescriptor()
        {
            return new SimulationPipelineDescriptor(
                new SimulationPipelineId(PipelineId),
                new SimulationPipelineRevision(Revision),
                new SimulationPipelineSchemaVersion(1),
                new[] { StandardFixedLocalPipelinePassContracts.LocalInputIngress },
                new[] { StandardFixedLocalPipelinePassContracts.LocalSingleStepSchedule },
                new[]
                {
                    StandardFixedPipelinePassContracts.AbilityEvaluate,
                    StandardFixedPipelinePassContracts.WorldResolveBatch,
                    StandardFixedPipelinePassContracts.AbilityFinalize
                },
                new[] { StandardFixedLocalPipelinePassContracts.LocalImmediateOutput });
        }

        public static SimulationPipelinePassFactoryCatalog CreatePortableFactoryCatalog()
        {
            return new SimulationPipelinePassFactoryCatalog(
                FixedPassExecutionBackend.Descriptor.Identity,
                new[]
                {
                    StandardFixedLocalPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFixedLocalPipelinePassContracts.LocalInputIngress),
                    StandardFixedLocalPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFixedLocalPipelinePassContracts.LocalSingleStepSchedule),
                    StandardFixedPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFixedPipelinePassContracts.AbilityEvaluate),
                    StandardFixedPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFixedPipelinePassContracts.WorldResolveBatch),
                    StandardFixedPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFixedPipelinePassContracts.AbilityFinalize),
                    StandardFixedLocalPipelinePassContracts.CreateFactoryDescriptor(
                        StandardFixedLocalPipelinePassContracts.LocalImmediateOutput)
                },
                SimulationPipelineProducts.All);
        }

        public static FixedSimulationPipelineRuntimePackage CreateRuntimePackage()
        {
            return new FixedSimulationPipelineRuntimePackage(
                CreateDescriptor(),
                CreatePortableFactoryCatalog(),
                new FixedPipelinePassRuntimeFactoryCatalog(new IFixedPipelinePassRuntimeFactory[]
                {
                    new FixedLocalInputIngressPassRuntimeFactory(),
                    new FixedLocalSingleStepSchedulePassRuntimeFactory(),
                    new FixedAbilityEvaluatePassRuntimeFactory(),
                    new FixedWorldResolveBatchPassRuntimeFactory(),
                    new FixedAbilityFinalizePassRuntimeFactory(),
                    new FixedLocalImmediateOutputPassRuntimeFactory()
                }),
                FixedPassExecutionBackend.CreateProductRuntimeCatalog());
        }
    }

    public sealed class FixedImmediateOutputCommitter : IFixedSimulationCommitter
    {
        readonly IFixedSimulationResultOutputPort m_Output;

        public FixedImmediateOutputCommitter(
            SimulationComponentIdentity identity,
            IFixedSimulationResultOutputPort output)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.Committer)
                throw new ArgumentException("Fixed Local Committer identity is invalid.", nameof(identity));
            Identity = identity;
            m_Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public SimulationComponentIdentity Identity { get; }

        public void Commit(FixedSimulationCommitBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Commit batch is invalid.", nameof(batch));
            if (batch.Steps.Count != 1 || batch.SourceEgress.Count != 0 ||
                batch.Steps[0].Step.ExecutionKind != SimulationPipelineStepExecutionKind.Forward)
            {
                throw new InvalidOperationException("Fixed Local Committer requires exactly one forward Step and no Source egress.");
            }
            for (int i = 0; i < batch.OutputDispositions.Dispositions.Count; i++)
            {
                if (batch.OutputDispositions.Dispositions[i].Kind != SimulationOutputDispositionKind.Publish)
                    throw new InvalidOperationException("Fixed Local Committer accepts only immediate Publish dispositions.");
            }
            FixedCompletedSimulationStep step = batch.Steps[0];
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

    public sealed class FixedLocalSimulationRuntimeLauncher : IFixedSimulationSessionRuntimeLauncher
    {
        readonly SimulationComponentIdentity m_SourceIdentity;
        int m_Launched;

        public FixedLocalSimulationRuntimeLauncher(SimulationSessionSourceDescriptor source)
        {
            if (source == null || source.Model.HasValue || source.Endpoint.HasValue || source.Protocol.HasValue ||
                !source.RequiredPipelineId.Equals(new SimulationPipelineId(StandardFixedLocalPipeline.PipelineId)))
            {
                throw new ArgumentException("Fixed Local Runtime Launcher requires a model-neutral Local Fixed Session Source.", nameof(source));
            }
            m_SourceIdentity = source.Identity;
            Descriptor = new FixedSimulationSessionRuntimeLauncherDescriptor(
                "thirdperson.simulation.runtime-launcher.fixed-local",
                "1",
                source.NumericProfileId,
                source.TargetAbiVersion,
                StableHash.Compute(
                    "fixed-local-runtime-launcher/1",
                    source.Identity.ToString(),
                    source.RequiredPipelineId.ToString()));
        }

        public FixedSimulationSessionRuntimeLauncherDescriptor Descriptor { get; }

        public FixedPassBackendCompositionResult Launch(FixedSimulationSessionCompositionRequest request)
        {
            if (Interlocked.Exchange(ref m_Launched, 1) != 0)
                throw new InvalidOperationException("Fixed Local Runtime Launcher is single-use.");
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Source.Identity.Equals(m_SourceIdentity) ||
                !request.PipelineRuntimePackage.Pipeline.PipelineId.Equals(
                    new SimulationPipelineId(StandardFixedLocalPipeline.PipelineId)) ||
                request.RestoreSource != null || request.Committer is not FixedImmediateOutputCommitter)
            {
                throw new InvalidOperationException("Fixed Local composition is missing its formal Pipeline or immediate Committer.");
            }
            if (!request.SolverDefinition.Deterministic ||
                (request.SolverDefinition.Capabilities & RequiredWorldCapabilities) != RequiredWorldCapabilities)
            {
                throw new InvalidOperationException("Fixed Local composition requires the deterministic reconstructible KCC Solver.");
            }
            return FixedSimulationSessionComposer.Compose(request);
        }

        public static WorldCapability RequiredWorldCapabilities =>
            WorldCapability.BodyMotion |
            WorldCapability.Grounding |
            WorldCapability.Collision |
            WorldCapability.Reconstructible |
            WorldCapability.AirborneVerticalMotion;
    }
}
