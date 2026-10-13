using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    [CanonicalContract(OwnsMemberResources = true)]
    internal sealed partial class ServerAuthoritativePredictionConfirmationData
    {
        internal ServerAuthoritativePredictionConfirmationData(int requestCapacity) =>
            PendingRequests = new List<SimulationInputRequest>(requestCapacity);

        public ulong ConfirmedInputSequence { get; set; }
        public ServerAuthoritativeEventHorizon ConfirmedEventHorizon { get; set; }
        public ulong LastAuthorityAckTick { get; set; }
        public ulong LastBaselineTick { get; set; }
        public ulong LastAuthorityClockEstimate { get; set; }
        public List<SimulationInputRequest> PendingRequests { get; }

        [CanonicalValidation]
        void RequireRequestOrder()
        {
            for (int index = 1; index < PendingRequests.Count; index++)
                if (PendingRequests[index - 1].Sequence >= PendingRequests[index].Sequence)
                    throw new InvalidDataException("Prediction pending request sequences must be strictly ordered.");
        }
    }

    [CanonicalStorageOwner]
    internal sealed partial class ServerAuthoritativePredictionConfirmationState
    {
        [CanonicalStorage]
        readonly ServerAuthoritativePredictionConfirmationData m_Data;
        readonly List<SimulationInputRequest> m_ScheduledRequests;
        readonly ServerAuthoritativePredictionCorrectionCheckpoint[] m_Checkpoints;

        public ServerAuthoritativePredictionConfirmationState(int requestCapacity)
        {
            if (requestCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestCapacity));
            m_Data = new ServerAuthoritativePredictionConfirmationData(requestCapacity);
            m_ScheduledRequests = new List<SimulationInputRequest>(requestCapacity);
            m_Checkpoints = new ServerAuthoritativePredictionCorrectionCheckpoint[2];
            for (int index = 0; index < m_Checkpoints.Length; index++)
                m_Checkpoints[index] = new ServerAuthoritativePredictionCorrectionCheckpoint(requestCapacity);
        }

        public int PendingRequestCount => PendingRequests.Count;
        public int MaximumTextValues => ServerAuthoritativePredictionConfirmationData.CanonicalMeasureTextCapacity(m_Data);

        public void ObserveAuthorityClock(ulong authorityTickEstimate) =>
            LastAuthorityClockEstimate = authorityTickEstimate;

        public IReadOnlyList<SimulationInputRequest> ScheduleRequests(
            IReadOnlyList<SimulationInputRequest> incoming,
            bool consume)
        {
            if (incoming == null)
                throw new ArgumentNullException(nameof(incoming));
            for (int i = 0; i < incoming.Count; i++)
                RetainRequest(incoming[i]);
            if (!consume || PendingRequests.Count == 0)
                return Array.Empty<SimulationInputRequest>();
            ClearScheduledRequests();
            for (int index = 0; index < PendingRequests.Count; index++)
                m_ScheduledRequests.Add(PendingRequests[index]);
            PendingRequests.Clear();
            return m_ScheduledRequests;
        }

        public void ApplyAck(AuthoritativeInputAck ack)
        {
            if (!ack.IsValid)
                throw new ArgumentException("Authority input ack is incomplete.", nameof(ack));
            if (ack.AuthorityTick.Value < LastAuthorityAckTick)
            {
                throw new InvalidOperationException(
                    $"Authority input ack cursor regressed: actor={ack.ActorId};incomingTick={ack.AuthorityTick.Value};lastTick={LastAuthorityAckTick};incomingSequence={ack.ConfirmedInputSequence};confirmedSequence={ConfirmedInputSequence}.");
            }
            ConfirmedInputSequence = Math.Max(ConfirmedInputSequence, ack.ConfirmedInputSequence);
            ConfirmedEventHorizon = MergeConfirmationHorizon(ConfirmedEventHorizon, ack.ConfirmedEventHorizon);
            LastAuthorityAckTick = ack.AuthorityTick.Value;
        }

        public ServerAuthoritativePredictionCorrectionCheckpoint PrepareBaseline(AuthoritativeActorBaseline baseline)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            ServerAuthoritativeEventHorizon horizon =
                MergeConfirmationHorizon(ConfirmedEventHorizon, baseline.ConfirmedEventHorizon);
            ServerAuthoritativePredictionCorrectionCheckpoint checkpoint = Capture();
            checkpoint.ConfirmedInputSequence = Math.Max(ConfirmedInputSequence, baseline.ConfirmedInputSequence);
            checkpoint.ConfirmedEventHorizon = horizon;
            checkpoint.LastBaselineTick = Math.Max(LastBaselineTick, baseline.AuthorityTick.Value);
            return checkpoint;
        }

        public void ApplyBaseline(AuthoritativeActorBaseline baseline)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            ConfirmedInputSequence = Math.Max(ConfirmedInputSequence, baseline.ConfirmedInputSequence);
            ConfirmedEventHorizon = MergeConfirmationHorizon(ConfirmedEventHorizon, baseline.ConfirmedEventHorizon);
            LastBaselineTick = Math.Max(LastBaselineTick, baseline.AuthorityTick.Value);
        }

        public void WriteState(CanonicalWriter writer) =>
            ServerAuthoritativePredictionStateCodec.WriteCorrection(writer, m_Data);

        public ServerAuthoritativePredictionCorrectionCheckpoint Capture()
        {
            ServerAuthoritativePredictionCorrectionCheckpoint checkpoint = AcquireCheckpoint();
            ServerAuthoritativePredictionConfirmationData.CanonicalCopyInto(m_Data, checkpoint.Data);
            return checkpoint;
        }

        public void Restore(ServerAuthoritativePredictionCorrectionCheckpoint checkpoint) =>
            ServerAuthoritativePredictionConfirmationData.CanonicalCopyInto(checkpoint.Data, m_Data);

        internal void ReleaseStorage()
        {
            ServerAuthoritativePredictionConfirmationData.CanonicalClear(m_Data);
            ClearScheduledRequests();
        }

        void ClearScheduledRequests()
        {
            for (int index = 0; index < m_ScheduledRequests.Count; index++)
                SimulationInputRequest.CanonicalRelease(m_ScheduledRequests[index]);
            m_ScheduledRequests.Clear();
        }

        static ServerAuthoritativeEventHorizon MergeConfirmationHorizon(
            ServerAuthoritativeEventHorizon current,
            ServerAuthoritativeEventHorizon incoming)
        {
            if (incoming.Sequence > current.Sequence)
                return incoming;
            if (incoming.Sequence < current.Sequence)
                return current;
            if (incoming.Sequence != 0 && !incoming.EventId.Equals(current.EventId))
                throw new InvalidOperationException("Authority confirmation EventId changed at the same sequence.");
            return current;
        }

        internal ServerAuthoritativePredictionCorrectionCheckpoint AcquireCheckpoint()
        {
            for (int index = 0; index < m_Checkpoints.Length; index++)
            {
                ServerAuthoritativePredictionCorrectionCheckpoint checkpoint = m_Checkpoints[index];
                if (checkpoint.IsHeld)
                    continue;
                checkpoint.Begin();
                return checkpoint;
            }
            throw new InvalidOperationException("Prediction correction checkpoints exceeded their prepared transaction capacity.");
        }

        void RetainRequest(SimulationInputRequest request)
        {
            int requestIndex = Find(request.Sequence);
            if (requestIndex >= 0)
            {
                SimulationInputRequest existing = PendingRequests[requestIndex];
                if (!CanonicalTextValue.Equals(existing.RequestId, request.RequestId, StringComparison.Ordinal) ||
                    existing.SourceTick != request.SourceTick ||
                    existing.ExpireSimulationTick != request.ExpireSimulationTick ||
                    existing.Priority != request.Priority)
                {
                    throw new InvalidOperationException($"Prediction request sequence '{request.Sequence}' changed while pending.");
                }
                return;
            }
            if (PendingRequests.Count >= PendingRequests.Capacity)
                throw new InvalidOperationException("Prediction pending request capacity is exhausted.");
            SimulationInputRequest.CanonicalRetain(in request);
            PendingRequests.Insert(~requestIndex, request);
        }

        int Find(ulong sequence)
        {
            int left = 0;
            int right = PendingRequests.Count - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                if (PendingRequests[middle].Sequence == sequence)
                    return middle;
                if (PendingRequests[middle].Sequence < sequence)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }
    }

    [CanonicalStorageOwner]
    internal sealed partial class ServerAuthoritativePredictionCorrectionCheckpoint : IDisposable
    {
        [CanonicalStorage]
        internal readonly ServerAuthoritativePredictionConfirmationData Data;

        internal ServerAuthoritativePredictionCorrectionCheckpoint(int requestCapacity) =>
            Data = new ServerAuthoritativePredictionConfirmationData(requestCapacity);

        internal bool IsHeld { get; private set; }
        internal void Begin() => IsHeld = true;

        public void Dispose()
        {
            ServerAuthoritativePredictionConfirmationData.CanonicalClear(Data);
            IsHeld = false;
        }
    }
}
