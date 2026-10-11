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
        readonly List<SimulationInputRequest> m_ScheduledRequests;
        readonly ServerAuthoritativePredictionCorrectionCheckpoint[] m_Checkpoints;
        int m_RequestCount;

        public ServerAuthoritativePredictionConfirmationState(int requestCapacity)
        {
            if (requestCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestCapacity));
            m_RequestCapacity = requestCapacity;
            m_RequestSequences = new ulong[requestCapacity];
            m_Requests = new SimulationInputRequest[requestCapacity];
            m_ScheduledRequests = new List<SimulationInputRequest>(requestCapacity);
            m_Checkpoints = new ServerAuthoritativePredictionCorrectionCheckpoint[2];
            for (int index = 0; index < m_Checkpoints.Length; index++)
                m_Checkpoints[index] = new ServerAuthoritativePredictionCorrectionCheckpoint(requestCapacity);
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
            m_ScheduledRequests.Clear();
            for (int index = 0; index < m_RequestCount; index++)
                m_ScheduledRequests.Add(m_Requests[index]);
            m_RequestCount = 0;
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
            for (int index = 0; index < m_Checkpoints.Length; index++)
            {
                ServerAuthoritativePredictionCorrectionCheckpoint checkpoint = m_Checkpoints[index];
                if (checkpoint.IsHeld)
                    continue;
                checkpoint.Begin(confirmedInputSequence, confirmedEventHorizon, lastAuthorityAckTick,
                    lastBaselineTick, lastAuthorityClockEstimate, m_Requests.AsSpan(0, m_RequestCount));
                return checkpoint;
            }
            throw new InvalidOperationException("Prediction correction checkpoints exceeded their prepared transaction capacity.");
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

    internal sealed class ServerAuthoritativePredictionCorrectionCheckpoint : IDisposable
    {
        readonly IReadOnlyList<SimulationInputRequest> m_PendingRequests;
        readonly List<SimulationInputRequest> m_PreparedRequests;

        internal ServerAuthoritativePredictionCorrectionCheckpoint(int requestCapacity)
        {
            m_PreparedRequests = new List<SimulationInputRequest>(requestCapacity);
            m_PendingRequests = m_PreparedRequests;
        }

        internal bool IsHeld { get; private set; }

        internal void Begin(ulong confirmedInputSequence, ServerAuthoritativeEventHorizon confirmedEventHorizon,
            ulong lastAuthorityAckTick, ulong lastBaselineTick, ulong lastAuthorityClockEstimate,
            ReadOnlySpan<SimulationInputRequest> pendingRequests)
        {
            IsHeld = true;
            ConfirmedInputSequence = confirmedInputSequence;
            ConfirmedEventHorizon = confirmedEventHorizon;
            LastAuthorityAckTick = lastAuthorityAckTick;
            LastBaselineTick = lastBaselineTick;
            LastAuthorityClockEstimate = lastAuthorityClockEstimate;
            for (int index = 0; index < pendingRequests.Length; index++)
                m_PreparedRequests.Add(pendingRequests[index]);
        }

        public void Dispose()
        {
            if (m_PreparedRequests != null)
            {
                m_PreparedRequests.Clear();
                IsHeld = false;
            }
        }

        public ServerAuthoritativePredictionCorrectionCheckpoint(
            ulong confirmedInputSequence,
            ServerAuthoritativeEventHorizon confirmedEventHorizon,
            ulong lastAuthorityAckTick,
            ulong lastBaselineTick,
            ulong lastAuthorityClockEstimate,
            SimulationInputRequest[] pendingRequests)
        {
            ConfirmedInputSequence = confirmedInputSequence;
            ConfirmedEventHorizon = confirmedEventHorizon;
            LastAuthorityAckTick = lastAuthorityAckTick;
            LastBaselineTick = lastBaselineTick;
            LastAuthorityClockEstimate = lastAuthorityClockEstimate;
            m_PendingRequests = pendingRequests;
        }

        public ulong ConfirmedInputSequence { get; private set; }
        public ServerAuthoritativeEventHorizon ConfirmedEventHorizon { get; private set; }
        public ulong LastAuthorityAckTick { get; private set; }
        public ulong LastBaselineTick { get; private set; }
        public ulong LastAuthorityClockEstimate { get; private set; }
        public IReadOnlyList<SimulationInputRequest> PendingRequests => m_PendingRequests;
    }
}
