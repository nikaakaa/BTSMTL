using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    public interface IFPSourceEgressOutputPort
    {
        void Commit(FPSourceEgressRecord record);
    }

    public interface IFPPublishedActorResultObserver
    {
        void ObservePublished(SimulationActorTickResult result);
    }

    public interface IFPPresentationCommitOutputPort : ISimulationPresentationOutputPort
    {
        void BeginCommit();
        void CompleteCommit(ulong committedTick);
        void AbortCommit();
    }

    public interface IFPSimulationResultOutputPort :
        IFPPresentationCommitOutputPort,
        IFPPublishedActorResultObserver
    {
    }

    public sealed class NullFPSourceEgressOutputPort : IFPSourceEgressOutputPort
    {
        public static readonly NullFPSourceEgressOutputPort Instance = new NullFPSourceEgressOutputPort();
        NullFPSourceEgressOutputPort() { }
        public void Commit(FPSourceEgressRecord record) { }
    }

    public sealed class FPSimulationCommitterAdapter : IFPSimulationCommitter
    {
        readonly SimulationCommitter m_CharacterCommitter;
        readonly IFPSourceEgressOutputPort m_SourceEgress;
        readonly IFPPublishedActorResultObserver m_ResultObserver;
        readonly Dictionary<EventId, SimulationOutputDisposition> m_DispositionsByEvent =
            new Dictionary<EventId, SimulationOutputDisposition>();
        SimulationOutputDisposition[] m_StepDispositions = Array.Empty<SimulationOutputDisposition>();
        int m_StepDispositionCount;

        public FPSimulationCommitterAdapter(
            SimulationComponentIdentity identity,
            SimulationCommitter characterCommitter,
            IFPSourceEgressOutputPort sourceEgress,
            IFPPublishedActorResultObserver resultObserver,
            int stepOutputCapacity,
            int frameOutputCapacity)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.Committer)
                throw new ArgumentException("Committer identity is invalid.", nameof(identity));
            Identity = identity;
            m_CharacterCommitter = characterCommitter ?? throw new ArgumentNullException(nameof(characterCommitter));
            m_SourceEgress = sourceEgress ?? throw new ArgumentNullException(nameof(sourceEgress));
            m_ResultObserver = resultObserver ?? throw new ArgumentNullException(nameof(resultObserver));
            m_DispositionsByEvent.EnsureCapacity(frameOutputCapacity);
            m_StepDispositions = new SimulationOutputDisposition[stepOutputCapacity];
            m_CharacterCommitter.PrepareOutputCapacity(stepOutputCapacity);
        }

        public SimulationComponentIdentity Identity { get; }

        public void Commit(FPSimulationCommitBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Commit batch is invalid.", nameof(batch));
            try
            {
                for (int i = 0; i < batch.OutputDispositions.Dispositions.Count; i++)
                {
                    SimulationOutputDisposition disposition = batch.OutputDispositions.Dispositions[i];
                    m_DispositionsByEvent.Add(disposition.SourceEventId, disposition);
                }
                for (int stepIndex = 0; stepIndex < batch.Steps.Count; stepIndex++)
                {
                    SimulationTickResult result = batch.Steps[stepIndex].Result;
                    m_StepDispositionCount = 0;
                    for (int actorIndex = 0; actorIndex < result.Actors.Count; actorIndex++)
                    {
                        SimulationActorTickResult actor = result.Actors[actorIndex];
                        AppendStepDispositions(actor.GameplayFacts);
                        AppendStepDispositions(actor.PresentationCommands);
                    }
                    m_CharacterCommitter.Commit(result, m_StepDispositions, m_StepDispositionCount);
                    for (int actorIndex = 0; actorIndex < result.Actors.Count; actorIndex++)
                        m_ResultObserver.ObservePublished(result.Actors[actorIndex]);
                }
                for (int i = 0; i < batch.SourceEgress.Count; i++)
                    m_SourceEgress.Commit(batch.SourceEgress[i]);
            }
            finally
            {
                m_StepDispositionCount = 0;
                m_DispositionsByEvent.Clear();
            }
        }

        void AppendStepDispositions(IReadOnlyList<GameplayFact> outputs)
        {
            for (int i = 0; i < outputs.Count; i++)
            {
                EventId eventId = outputs[i].Header.EventId;
                if (!m_DispositionsByEvent.TryGetValue(eventId, out SimulationOutputDisposition disposition))
                    throw new InvalidOperationException($"Commit batch has no disposition for EventId '{eventId}'.");
                m_StepDispositions[m_StepDispositionCount++] = disposition;
            }
        }

        void AppendStepDispositions(IReadOnlyList<PresentationCommand> outputs)
        {
            for (int i = 0; i < outputs.Count; i++)
            {
                EventId eventId = outputs[i].Header.EventId;
                if (!m_DispositionsByEvent.TryGetValue(eventId, out SimulationOutputDisposition disposition))
                    throw new InvalidOperationException($"Commit batch has no disposition for EventId '{eventId}'.");
                m_StepDispositions[m_StepDispositionCount++] = disposition;
            }
        }

    }
}

