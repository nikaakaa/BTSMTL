using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal sealed class ServerAuthoritativePredictionConfirmationState
    {
        readonly int m_RequestCapacity;
        readonly ulong[] m_RequestSequences;
        readonly SimulationInputRequest[] m_Requests;
        int m_RequestCount;

        public ServerAuthoritativePredictionConfirmationState(int requestCapacity)
        {
            if (requestCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestCapacity));
            m_RequestCapacity = requestCapacity;
            m_RequestSequences = new ulong[requestCapacity];
            m_Requests = new SimulationInputRequest[requestCapacity];
        }

        public ulong ConfirmedInputSequence { get; private set; }
        public ServerAuthoritativeEventHorizon ConfirmedEventHorizon { get; private set; }
        public ulong LastAuthorityAckTick { get; private set; }
        public ulong LastBaselineTick { get; private set; }
        public ulong LastAuthorityClockEstimate { get; private set; }
        public int PendingRequestCount => m_RequestCount;

        public void ObserveAuthorityClock(ulong authorityTickEstimate)
        {
            LastAuthorityClockEstimate = authorityTickEstimate;
        }

        public IReadOnlyList<SimulationInputRequest> ScheduleRequests(
            IReadOnlyList<SimulationInputRequest> incoming,
            bool consume)
        {
            if (incoming == null)
                throw new ArgumentNullException(nameof(incoming));
            for (int i = 0; i < incoming.Count; i++)
                RetainRequest(incoming[i]);
            if (!consume || m_RequestCount == 0)
                return Array.Empty<SimulationInputRequest>();
            var result = new SimulationInputRequest[m_RequestCount];
            Array.Copy(m_Requests, result, m_RequestCount);
            m_RequestCount = 0;
            return result;
        }

        public ServerAuthoritativePredictionCorrectionCheckpoint PrepareAck(AuthoritativeInputAck ack)
        {
            if (!ack.IsValid)
                throw new ArgumentException("Authority input ack is incomplete.", nameof(ack));
            if (ack.AuthorityTick.Value < LastAuthorityAckTick)
            {
                throw new InvalidOperationException(
                    $"Authority input ack cursor regressed: actor={ack.ActorId};incomingTick={ack.AuthorityTick.Value};lastTick={LastAuthorityAckTick};incomingSequence={ack.ConfirmedInputSequence};confirmedSequence={ConfirmedInputSequence}.");
            }
            return CreateCheckpoint(
                Math.Max(ConfirmedInputSequence, ack.ConfirmedInputSequence),
                MergeConfirmationHorizon(ConfirmedEventHorizon, ack.ConfirmedEventHorizon),
                ack.AuthorityTick.Value,
                LastBaselineTick,
                LastAuthorityClockEstimate);
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
            return CreateCheckpoint(
                Math.Max(ConfirmedInputSequence, baseline.ConfirmedInputSequence),
                MergeConfirmationHorizon(ConfirmedEventHorizon, baseline.ConfirmedEventHorizon),
                LastAuthorityAckTick,
                Math.Max(LastBaselineTick, baseline.AuthorityTick.Value),
                LastAuthorityClockEstimate);
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
            ServerAuthoritativePredictionStateCodec.WriteCorrection(writer, ConfirmedInputSequence,
                ConfirmedEventHorizon, LastAuthorityAckTick, LastBaselineTick, LastAuthorityClockEstimate,
                m_Requests, m_RequestCount);

        public ServerAuthoritativePredictionCorrectionCheckpoint Capture() =>
            CreateCheckpoint(
                ConfirmedInputSequence,
                ConfirmedEventHorizon,
                LastAuthorityAckTick,
                LastBaselineTick,
                LastAuthorityClockEstimate);

        public void Restore(ServerAuthoritativePredictionCorrectionCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            if (checkpoint.PendingRequests.Count > m_RequestCapacity)
                throw new InvalidOperationException("Prediction pending request checkpoint exceeds its configured capacity.");
            for (int i = 1; i < checkpoint.PendingRequests.Count; i++)
            {
                if (checkpoint.PendingRequests[i - 1].Sequence >= checkpoint.PendingRequests[i].Sequence)
                    throw new InvalidOperationException("Prediction pending request checkpoint sequence order is invalid.");
            }
            Array.Clear(m_RequestSequences, 0, m_RequestCount);
            Array.Clear(m_Requests, 0, m_RequestCount);
            for (int i = 0; i < checkpoint.PendingRequests.Count; i++)
            {
                SimulationInputRequest request = checkpoint.PendingRequests[i];
                m_RequestSequences[i] = request.Sequence;
                m_Requests[i] = request;
            }
            ConfirmedInputSequence = checkpoint.ConfirmedInputSequence;
            ConfirmedEventHorizon = checkpoint.ConfirmedEventHorizon;
            LastAuthorityAckTick = checkpoint.LastAuthorityAckTick;
            LastBaselineTick = checkpoint.LastBaselineTick;
            LastAuthorityClockEstimate = checkpoint.LastAuthorityClockEstimate;
            m_RequestCount = checkpoint.PendingRequests.Count;
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

        ServerAuthoritativePredictionCorrectionCheckpoint CreateCheckpoint(
            ulong confirmedInputSequence,
            ServerAuthoritativeEventHorizon confirmedEventHorizon,
            ulong lastAuthorityAckTick,
            ulong lastBaselineTick,
            ulong lastAuthorityClockEstimate)
        {
            var requests = new SimulationInputRequest[m_RequestCount];
            Array.Copy(m_Requests, requests, m_RequestCount);
            return new ServerAuthoritativePredictionCorrectionCheckpoint(
                confirmedInputSequence,
                confirmedEventHorizon,
                lastAuthorityAckTick,
                lastBaselineTick,
                lastAuthorityClockEstimate,
                requests);
        }

        void RetainRequest(SimulationInputRequest request)
        {
            int requestIndex = Find(request.Sequence);
            if (requestIndex >= 0)
            {
                SimulationInputRequest existing = m_Requests[requestIndex];
                if (!CanonicalTextValue.Equals(existing.RequestId, request.RequestId, StringComparison.Ordinal) ||
                    existing.SourceTick != request.SourceTick ||
                    existing.ExpireSimulationTick != request.ExpireSimulationTick ||
                    existing.Priority != request.Priority)
                {
                    throw new InvalidOperationException($"Prediction request sequence '{request.Sequence}' changed while pending.");
                }
                return;
            }
            if (m_RequestCount >= m_RequestCapacity)
                throw new InvalidOperationException("Prediction pending request capacity is exhausted.");
            int insertionIndex = ~requestIndex;
            Array.Copy(m_RequestSequences, insertionIndex, m_RequestSequences, insertionIndex + 1, m_RequestCount - insertionIndex);
            Array.Copy(m_Requests, insertionIndex, m_Requests, insertionIndex + 1, m_RequestCount - insertionIndex);
            m_RequestSequences[insertionIndex] = request.Sequence;
            m_Requests[insertionIndex] = request;
            m_RequestCount++;
        }

        int Find(ulong sequence)
        {
            int left = 0;
            int right = m_RequestCount - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                if (m_RequestSequences[middle] == sequence)
                    return middle;
                if (m_RequestSequences[middle] < sequence)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }
    }

    internal sealed class ServerAuthoritativePredictionCorrectionCheckpoint
    {
        readonly SimulationInputRequest[] m_PendingRequests;

        public ServerAuthoritativePredictionCorrectionCheckpoint(
            ulong confirmedInputSequence,
            ServerAuthoritativeEventHorizon confirmedEventHorizon,
            ulong lastAuthorityAckTick,
            ulong lastBaselineTick,
            ulong lastAuthorityClockEstimate,
            SimulationInputRequest[] pendingRequests)
        {
            SimulationInputRequest[] requests = pendingRequests ?? Array.Empty<SimulationInputRequest>();
            for (int i = 1; i < requests.Length; i++)
            {
                if (requests[i - 1].Sequence >= requests[i].Sequence)
                    throw new ArgumentException("Prediction pending request checkpoint sequence order is invalid.", nameof(pendingRequests));
            }
            ConfirmedInputSequence = confirmedInputSequence;
            ConfirmedEventHorizon = confirmedEventHorizon;
            LastAuthorityAckTick = lastAuthorityAckTick;
            LastBaselineTick = lastBaselineTick;
            LastAuthorityClockEstimate = lastAuthorityClockEstimate;
            m_PendingRequests = requests;
        }

        public ulong ConfirmedInputSequence { get; }
        public ServerAuthoritativeEventHorizon ConfirmedEventHorizon { get; }
        public ulong LastAuthorityAckTick { get; }
        public ulong LastBaselineTick { get; }
        public ulong LastAuthorityClockEstimate { get; }
        public IReadOnlyList<SimulationInputRequest> PendingRequests => m_PendingRequests;
    }
}
