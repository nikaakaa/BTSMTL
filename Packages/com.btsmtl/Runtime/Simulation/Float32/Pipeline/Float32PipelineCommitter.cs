using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public interface IFloat32SourceEgressOutputPort
    {
        void Commit(Float32SourceEgressRecord record);
    }

    public interface IFloat32PublishedActorResultObserver
    {
        void ObservePublished(SimulationActorTickResult result);
    }

    public sealed class NullFloat32SourceEgressOutputPort : IFloat32SourceEgressOutputPort
    {
        public static readonly NullFloat32SourceEgressOutputPort Instance = new NullFloat32SourceEgressOutputPort();
        NullFloat32SourceEgressOutputPort() { }
        public void Commit(Float32SourceEgressRecord record) { }
    }

    public sealed class Float32SimulationCommitterAdapter : IFloat32SimulationCommitter
    {
        readonly SimulationCommitter m_CharacterCommitter;
        readonly IFloat32SourceEgressOutputPort m_SourceEgress;
        readonly IFloat32PublishedActorResultObserver m_ResultObserver;
        readonly Dictionary<EventId, SimulationOutputDisposition> m_DispositionsByEvent =
            new Dictionary<EventId, SimulationOutputDisposition>();
        SimulationOutputDisposition[] m_StepDispositions = Array.Empty<SimulationOutputDisposition>();
        int m_StepDispositionCount;

        public Float32SimulationCommitterAdapter(
            SimulationComponentIdentity identity,
            SimulationCommitter characterCommitter,
            IFloat32SourceEgressOutputPort sourceEgress,
            IFloat32PublishedActorResultObserver resultObserver)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.Committer)
                throw new ArgumentException("Committer identity is invalid.", nameof(identity));
            Identity = identity;
            m_CharacterCommitter = characterCommitter ?? throw new ArgumentNullException(nameof(characterCommitter));
            m_SourceEgress = sourceEgress ?? throw new ArgumentNullException(nameof(sourceEgress));
            m_ResultObserver = resultObserver ?? throw new ArgumentNullException(nameof(resultObserver));
        }

        public SimulationComponentIdentity Identity { get; }

        public void PrepareOutputCapacity(int stepOutputCapacity, int frameOutputCapacity)
        {
            m_DispositionsByEvent.EnsureCapacity(frameOutputCapacity);
            m_StepDispositions = new SimulationOutputDisposition[stepOutputCapacity];
            m_CharacterCommitter.PrepareOutputCapacity(stepOutputCapacity);
        }

        public void Commit(Float32SimulationCommitBatch batch)
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
