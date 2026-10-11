using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public enum ServerAuthoritativeEventDisposition : byte
    {
        PredictedCommitted = 1,
        AuthorityConfirmed = 2,
        SuppressedDuplicate = 3,
        PredictedRejected = 4
    }

    public readonly struct ServerAuthoritativeJournalEntry
    {
        public ServerAuthoritativeJournalEntry(
            EventId eventId,
            SimulationTick tick,
            ulong sequence,
            ServerAuthoritativeEventDisposition disposition)
        {
            if (!eventId.IsValid || !tick.IsValid || sequence == 0 ||
                !IsValidDisposition(disposition))
            {
                throw new ArgumentException("Prediction disposition journal entry is invalid.");
            }
            EventId = eventId;
            Tick = tick;
            Sequence = sequence;
            Disposition = disposition;
        }

        public EventId EventId { get; }
        public SimulationTick Tick { get; }
        public ulong Sequence { get; }
        public ServerAuthoritativeEventDisposition Disposition { get; }
        internal static bool IsValidDisposition(ServerAuthoritativeEventDisposition disposition) =>
            disposition is ServerAuthoritativeEventDisposition.PredictedCommitted or
                ServerAuthoritativeEventDisposition.AuthorityConfirmed or
                ServerAuthoritativeEventDisposition.SuppressedDuplicate or
                ServerAuthoritativeEventDisposition.PredictedRejected;
    }

    public sealed class ServerAuthoritativePredictionHistoryRecord
    {
        readonly ServerAuthoritativePredictionHistory.RecordPage m_Storage;

        internal ServerAuthoritativePredictionHistoryRecord(ServerAuthoritativePredictionHistory.RecordPage storage) =>
            m_Storage = storage;

        public ServerAuthoritativePredictionHistoryRecord(
            OwnerCanonicalInputBatch input,
            SimulationSessionCompositionIdentity compositionIdentity,
            SimulationWorldSnapshot world,
            SimulationPipelineStateSnapshot pipelineProjection,
            ObservedWorldConstraintFrame observedWorldConstraints,
            ulong journalCursor)
        {
            Initialize(input, compositionIdentity, world, pipelineProjection, observedWorldConstraints, journalCursor);
        }

        internal void Initialize(
            OwnerCanonicalInputBatch input,
            SimulationSessionCompositionIdentity compositionIdentity,
            SimulationWorldSnapshot world,
            SimulationPipelineStateSnapshot pipelineProjection,
            ObservedWorldConstraintFrame observedWorldConstraints,
            ulong journalCursor)
        {
            if (!input.IsValid || !compositionIdentity.IsValid || world == null || pipelineProjection == null || observedWorldConstraints == null ||
                world.Tick.Value != pipelineProjection.LastCompletedTick || world.Tick.Value == 0)
            {
                throw new ArgumentException("Prediction history record identity is incomplete.");
            }
            Input = input;
            if (world.Actors.Count != 1 || world.Actors[0].ActorId != input.ActorId)
                throw new ArgumentException("Prediction history record must contain the exact owner Actor.");
            if (observedWorldConstraints.Tick != world.Tick)
                throw new ArgumentException("Prediction history observed frame Tick does not match its snapshot.", nameof(observedWorldConstraints));
            CompositionIdentity = compositionIdentity;
            World = world;
            PipelineProjection = pipelineProjection;
            ObservedWorldConstraints = observedWorldConstraints;
            JournalCursor = journalCursor;
        }

        public SimulationTick Tick => World.Tick;
        public OwnerCanonicalInputBatch Input { get; private set; }
        public SimulationSessionCompositionIdentity CompositionIdentity { get; private set; }
        public SimulationWorldSnapshot World { get; private set; }
        public SimulationPipelineStateSnapshot PipelineProjection { get; private set; }
        public ObservedWorldConstraintFrame ObservedWorldConstraints { get; private set; }
        public ulong JournalCursor { get; private set; }
        public SimulationActorSnapshot Character => World.Actors[0];

        internal void RetainStorage()
        {
            m_Storage?.Retain();
            Input.ActorId.Retain();
            Input.Input.RetainText();
            for (int index = 0; index < World.Actors.Count; index++)
                World.Actors[index].ActorId.Retain();
            for (int index = 0; index < ObservedWorldConstraints.Constraints.Count; index++)
            {
                ObservedWorldConstraint constraint = ObservedWorldConstraints.Constraints[index];
                constraint.ActorId.Retain();
                constraint.BeforeBody.ActorId.Retain();
                constraint.FinalBody.ActorId.Retain();
            }
            PipelineProjection.RetainStepStorage();
        }

        internal void ReleaseStorage()
        {
            PipelineProjection.ReleaseStepStorage();
            for (int index = 0; index < ObservedWorldConstraints.Constraints.Count; index++)
            {
                ObservedWorldConstraint constraint = ObservedWorldConstraints.Constraints[index];
                constraint.FinalBody.ActorId.Release();
                constraint.BeforeBody.ActorId.Release();
                constraint.ActorId.Release();
            }
            for (int index = 0; index < World.Actors.Count; index++)
                World.Actors[index].ActorId.Release();
            Input.Input.ReleaseText();
            Input.ActorId.Release();
            m_Storage?.Release();
        }

        internal void Clear()
        {
            Input = default;
            CompositionIdentity = default;
            World = null;
            PipelineProjection = null;
            ObservedWorldConstraints = null;
            JournalCursor = 0;
        }
    }

    public sealed class ServerAuthoritativePredictionState
    {
        readonly ServerAuthoritativePredictionConfirmationState m_Confirmation;
        readonly ServerAuthoritativePredictionHistory m_History;
        readonly ServerAuthoritativePredictionDispositionJournal m_Journal;
        readonly ServerAuthoritativePredictionReconciler m_Reconciler;
        readonly ServerAuthoritativePredictionRestorePort m_Restore;

        public ServerAuthoritativePredictionState(
            ServerAuthoritativeModelPolicy policy,
            ServerAuthoritativePredictionRestorePort restore,
            Float32CharacterRuntime characterRuntime,
            ServerAuthoritativePipelineCompatibilityIdentity compatibility,
            ServerAuthoritativeWorldIdentity authorityWorld,
            IEnumerable<ActorId> lockedRemoteActors)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_Restore = restore ?? throw new ArgumentNullException(nameof(restore));
            m_Confirmation = new ServerAuthoritativePredictionConfirmationState(Policy.HistoryCapacity);
            m_History = new ServerAuthoritativePredictionHistory(
                Policy.HistoryCapacity,
                Policy.SimulationTickRate,
                Policy.MaximumRemoteBodyExtrapolationTicks,
                lockedRemoteActors);
            m_Journal = new ServerAuthoritativePredictionDispositionJournal(Policy.HistoryCapacity);
            m_Reconciler = new ServerAuthoritativePredictionReconciler(characterRuntime, compatibility, authorityWorld);
        }

        public ServerAuthoritativeModelPolicy Policy { get; }
        internal void PrepareWorldReadStorage(int maximumPayloadBytes) =>
            m_Reconciler.PrepareWorldReadStorage(maximumPayloadBytes);

        public ulong ConfirmedInputSequence => m_Confirmation.ConfirmedInputSequence;
        public ServerAuthoritativeEventHorizon ConfirmedEventHorizon => m_Confirmation.ConfirmedEventHorizon;
        public ulong LastAuthorityAckTick => m_Confirmation.LastAuthorityAckTick;
        public ulong LastBaselineTick => m_Confirmation.LastBaselineTick;
        public ulong LastAuthorityClockEstimate => m_Confirmation.LastAuthorityClockEstimate;
        public ulong JournalCursor => m_Journal.Cursor;
        public int JournalCount => m_Journal.Count;
        public int HistoryCount => m_History.Count;
        internal int MaximumHistoryTextValues => m_History.MaximumTextValues;
        public int RemoteBodySampleCount => m_History.RemoteBodySampleCount;
        public int RemoteBodyCapacityPerActor => m_History.RemoteBodyCapacityPerActor;
        public ulong RemoteBodyFirstSampleTick => m_History.RemoteBodyFirstSampleTick;
        public ulong RemoteBodyLastSampleTick => m_History.RemoteBodyLastSampleTick;
        public ulong RemoteBodyEvictionCount => m_History.RemoteBodyEvictionCount;
        public bool IsRemoteObservationPrimed => m_History.IsRemoteObservationPrimed;
        public int PendingRequestCount => m_Confirmation.PendingRequestCount;
        public int LastRejectedCount => m_Journal.LastRejectedCount;
        public ulong LastPredictedInputSequence =>
            m_History.GetLastPredictedInputSequence(m_Confirmation.ConfirmedInputSequence);

        public void ObserveAuthorityClock(ulong authorityTickEstimate)
        {
            m_Confirmation.ObserveAuthorityClock(authorityTickEstimate);
        }

        public void ObserveRemotePresentation(RemotePresentationBatch batch) => m_History.Observe(batch);

        internal ServerAuthoritativeRemoteBodySelectionFrame SelectRemoteBodyFrame(SimulationTick tick) =>
            m_History.SelectRemoteBodyFrame(tick);

        internal IReadOnlyList<SimulationInputRequest> ScheduleRequests(
            IReadOnlyList<SimulationInputRequest> incoming,
            bool consume) => m_Confirmation.ScheduleRequests(incoming, consume);

        public bool TryGetHistory(
            SimulationTick tick,
            out ServerAuthoritativePredictionHistoryRecord record) => m_History.TryGet(tick, out record);

        internal IReadOnlyList<ServerAuthoritativePredictionHistoryRecord> GetReplayAfter(
            ulong confirmedInputSequence) => m_History.GetReplayAfter(confirmedInputSequence);

        public int CountReplayAfter(ulong confirmedInputSequence) =>
            m_History.CountReplayAfter(confirmedInputSequence);

        public void AddHistory(OwnerCanonicalInputBatch input, Float32CompletedSimulationStep completed)
        {
            m_History.Add(
                input,
                completed,
                m_Journal.Cursor,
                m_Confirmation.ConfirmedInputSequence,
                m_Confirmation.LastAuthorityAckTick,
                m_Confirmation.LastBaselineTick);
            m_Journal.Prune(m_History.FirstRetainedTick);
        }

        public void ApplyAck(AuthoritativeInputAck ack)
        {
            if (!ack.IsValid)
                return;
            if (m_History.Count > 0 && m_History.FirstRecord().Input.ActorId != ack.ActorId)
                throw new InvalidOperationException("Authority input ack targets another Prediction owner.");
            m_Confirmation.ApplyAck(ack);
            m_Journal.ApplyConfirmation(
                ack.AuthorityTick,
                ack.ConfirmedEventHorizon,
                m_History.FirstRetainedTick);
        }

        public PredictionCorrectionDecision Decide(AuthoritativeActorBaseline baseline)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            ServerAuthoritativePredictionHistoryRecord first = m_History.Count == 0
                ? null
                : m_History.FirstRecord();
            m_Reconciler.ValidateBaselineIdentity(baseline, first);
            m_History.TryGet(baseline.AuthorityTick, out ServerAuthoritativePredictionHistoryRecord local);
            int replayCount = local == null
                ? 0
                : m_History.CountReplayAfter(baseline.ConfirmedInputSequence);
            PredictionCorrectionDecision decision = m_Reconciler.Decide(baseline, local, replayCount, Policy);
            if (decision.Kind == PredictionCorrectionDecisionKind.NoCorrection)
                AdvanceConfirmation(baseline);
            return decision;
        }

        public SimulationRestoreDirective BuildRestore(
            AuthoritativeActorBaseline baseline,
            PredictionCorrectionDecision decision,
            SimulationPipelineIdentity pipeline)
        {
            if (!baseline.IsValid || !decision.IsValid || decision.Kind == PredictionCorrectionDecisionKind.NoCorrection)
                throw new ArgumentException("Prediction restore requires a corrective baseline and decision.");
            if (!m_History.TryGet(baseline.AuthorityTick, out ServerAuthoritativePredictionHistoryRecord local))
                local = m_History.LastRecord();

            using ServerAuthoritativePredictionCorrectionCheckpoint confirmation = m_Confirmation.PrepareBaseline(baseline);
            using ServerAuthoritativePredictionJournalCheckpoint journal = m_Journal.PrepareConfirmation(
                baseline.AuthorityTick,
                confirmation.ConfirmedEventHorizon,
                m_History.FirstRetainedTick);
            using ServerAuthoritativePredictionHistoryCheckpoint history = m_History.PrepareClear();
            m_Journal.Prune(journal, history.FirstRetainedTick);

            ServerAuthoritativePredictionRestorePlan plan = m_Reconciler.BuildRestorePlan(
                local,
                baseline,
                decision,
                pipeline,
                ServerAuthoritativePredictionStateCodec.WriteCorrection(confirmation),
                ServerAuthoritativePredictionStateCodec.WriteHistory(history),
                ServerAuthoritativePredictionStateCodec.WriteJournal(journal));
            m_Restore.Store(plan.SnapshotId, plan.Snapshot);
            m_Journal.Restore(journal);
            m_History.Restore(history);
            m_Confirmation.Restore(confirmation);
            return plan.Directive;
        }

        public bool WasCommitted(EventId eventId) => m_Journal.WasCommitted(eventId);

        public void SealHistoryJournalCursor(SimulationTick tick)
        {
            m_History.SealJournalCursor(tick, m_Journal.Cursor);
        }

        public void Record(SimulationEventHeader header, ServerAuthoritativeEventDisposition disposition)
        {
            m_Journal.Record(
                new ServerAuthoritativeJournalEntry(header.EventId, header.Tick, header.Sequence, disposition),
                m_History.FirstRetainedTick);
        }

        public void WriteCorrectionState(CanonicalWriter writer) => m_Confirmation.WriteState(writer);
        public void WriteHistoryState(CanonicalWriter writer) => m_History.WriteState(writer);
        public void WriteJournalState(CanonicalWriter writer) => m_Journal.WriteState(writer);

        internal void RestoreCorrectionState(CanonicalReader reader, CanonicalReader inputReader, CanonicalWriter canonicalScratch)
        {
            ServerAuthoritativePredictionCorrectionCheckpoint checkpoint =
                ServerAuthoritativePredictionStateCodec.ReadCorrection(reader, Policy.HistoryCapacity);
            m_Confirmation.Restore(checkpoint);
        }

        internal ServerAuthoritativePredictionCorrectionCheckpoint CaptureCorrectionCheckpoint() =>
            m_Confirmation.Capture();

        internal void RestoreCorrectionCheckpoint(ServerAuthoritativePredictionCorrectionCheckpoint checkpoint)
        {
            m_Confirmation.Restore(checkpoint);
        }

        internal void RestoreHistoryState(CanonicalReader reader, CanonicalReader inputReader, CanonicalWriter canonicalScratch)
        {
            using ServerAuthoritativePredictionHistoryCheckpoint checkpoint =
                ServerAuthoritativePredictionStateCodec.ReadHistory(reader, inputReader, canonicalScratch, Policy.HistoryCapacity);
            m_History.Restore(checkpoint);
        }

        internal void ReleaseHistoryStorage() => m_History.ReleaseStateStorage();

        internal ServerAuthoritativePredictionHistoryCheckpoint CaptureHistoryCheckpoint() => m_History.Capture();

        internal void RestoreHistoryCheckpoint(ServerAuthoritativePredictionHistoryCheckpoint checkpoint)
        {
            m_History.Restore(checkpoint);
        }

        internal void RestoreJournalState(CanonicalReader reader, CanonicalReader inputReader, CanonicalWriter canonicalScratch)
        {
            using ServerAuthoritativePredictionJournalCheckpoint checkpoint =
                ServerAuthoritativePredictionStateCodec.ReadJournal(reader, Policy.HistoryCapacity);
            m_Journal.Restore(checkpoint);
        }

        internal ServerAuthoritativePredictionJournalCheckpoint CaptureJournalCheckpoint() => m_Journal.Capture();

        internal void RestoreJournalCheckpoint(ServerAuthoritativePredictionJournalCheckpoint checkpoint)
        {
            m_Journal.Restore(checkpoint);
        }

        void AdvanceConfirmation(AuthoritativeActorBaseline baseline)
        {
            m_Confirmation.ApplyBaseline(baseline);
            m_Journal.ApplyConfirmation(
                baseline.AuthorityTick,
                m_Confirmation.ConfirmedEventHorizon,
                m_History.FirstRetainedTick);
            m_History.PruneConfirmedThrough(baseline.ConfirmedInputSequence);
            m_Journal.Prune(m_History.FirstRetainedTick);
        }
    }

    public sealed class ServerAuthoritativePredictionStatePort : IServerAuthoritativePredictionStatePort
    {
        public ServerAuthoritativePredictionStatePort(
            SimulationComponentIdentity source,
            ServerAuthoritativeModelPolicy policy,
            ServerAuthoritativePredictionRestorePort restore,
            Float32CharacterRuntime characterRuntime,
            ServerAuthoritativePipelineCompatibilityIdentity compatibility,
            ServerAuthoritativeWorldIdentity authorityWorld,
            IEnumerable<ActorId> lockedRemoteActors)
        {
            State = new ServerAuthoritativePredictionState(
                policy,
                restore,
                characterRuntime,
                compatibility,
                authorityWorld,
                lockedRemoteActors);
            Descriptor = SimulationPortDescriptor.CreateSource(
                ServerAuthoritativeSourcePortContracts.PredictionState,
                source);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public ServerAuthoritativePredictionState State { get; }
    }

    static class ServerAuthoritativePredictionStateSnapshot
    {
        public static SimulationPipelinePassStateSnapshot Create(
            SimulationPipelinePassId passId,
            string stateOwner,
            string schema,
            byte[] payload,
            int schemaVersion = 1)
        {
            if (schemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            return SimulationPipelinePassStateSnapshot.FromOwnedPayload(
                passId,
                new SimulationPipelinePassImplementationVersion(ServerAuthoritativePredictionPassIds.ImplementationVersion),
                stateOwner,
                schema,
                schemaVersion,
                payload);
        }
    }
}
