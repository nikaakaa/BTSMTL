using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative.Transport
{
    sealed class ServerAuthoritativeAuthorityClientRoute
    {
        readonly int m_Capacity;
        readonly CanonicalInputSample[] m_Inputs;
        readonly ulong[] m_InputTargetTicks;
        int m_InputCount;
        readonly ulong[] m_SentSequenceOrder;
        readonly NetworkCheckpoint[] m_SentCheckpoints;
        int m_SentSequenceHead;
        int m_SentSequenceCount;
        CanonicalInputSample m_Held;
        ulong m_HeldAcceptedTick;
        SimulationInputValue[] m_NeutralValues;
        ulong m_LastEnqueuedInputSequence;
        ulong m_SendPacketSequence;
        ulong m_SnapshotSequence;
        ulong m_PreviousCommandPacketCount;
        ulong m_PreviousCommandPayloadBytes;
        ulong m_PreviousDeltaSnapshotCount;
        ulong m_PreviousDeltaPayloadBytes;
        ulong m_DeltaPayloadBytes;

        public ServerAuthoritativeAuthorityClientRoute(ServerAuthoritativeRosterEntry roster, int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Roster = roster;
            m_Capacity = capacity;
            m_Inputs = new CanonicalInputSample[capacity];
            m_InputTargetTicks = new ulong[capacity];
            m_SentSequenceOrder = new ulong[capacity + 1];
            m_SentCheckpoints = new NetworkCheckpoint[capacity + 1];
        }

        public ServerAuthoritativeRosterEntry Roster { get; }
        public ServerAuthoritativeAuthorityDataPlaneTicket Ticket { get; private set; }
        public ServerAuthoritativeDatagramIdentity Identity { get; private set; }
        public bool DataPlaneReady { get; set; }
        public bool TicketConsumptionReported { get; set; }
        public ulong LastReceivedPacketSequence { get; private set; }
        public ulong PendingCheckpointRequest { get; set; }
        public NetworkCheckpoint AcknowledgedCheckpoint { get; private set; }
        public ulong AcknowledgedSnapshotSequence { get; private set; }
        public ulong DeltaSnapshotCount { get; private set; }
        public ulong FullCheckpointCount { get; private set; }
        public ulong DeltaMtuExceededCount { get; private set; }
        public int LastDeltaPayloadBytes { get; private set; }
        public bool HasInput => m_Held.IsValid || m_InputCount > 0;
        public ulong CommandPacketCount { get; private set; }
        public ulong CommandPayloadBytes { get; private set; }
        public ulong PacketSequenceGaps { get; private set; }
        public ulong DuplicatePackets { get; private set; }
        public ulong OutOfOrderPackets { get; private set; }
        public ulong ExactInputCount { get; private set; }
        public ulong HeldInputCount { get; private set; }
        public ulong NeutralInputCount { get; private set; }
        public ulong LateInputCount { get; private set; }
        public long LastCommandLead { get; private set; }
        public ulong LastCommandSourceTick { get; private set; }

        public void SetTicket(
            ServerAuthoritativeAuthorityDataPlaneTicket ticket,
            ServerAuthoritativeDatagramIdentity identity)
        {
            if (Ticket.IsValid)
                throw new InvalidOperationException("Authority route received more than one data-plane ticket.");
            if (!ticket.IsValid)
                throw new ArgumentException("Authority data-plane ticket is invalid.", nameof(ticket));
            Ticket = ticket;
            Identity = identity;
        }

        public void Enqueue(CanonicalInputSample sample)
        {
            if (!sample.IsValid)
                throw new ArgumentException("Canonical input sample is invalid.", nameof(sample));
            if (sample.InputSequence <= m_LastEnqueuedInputSequence)
                return;
            m_LastEnqueuedInputSequence = sample.InputSequence;
            if (m_InputCount >= m_Capacity)
                throw new InvalidOperationException($"Authority command queue for Actor '{Roster.ActorId}' overflowed.");

            int index = FindInputIndex(sample.TargetAuthorityTick);
            if (index >= 0)
            {
                if (sample.InputSequence <= m_Inputs[index].InputSequence)
                    return;
                sample.Input.RetainText();
                m_Inputs[index].Input.ReleaseText();
                m_Inputs[index] = sample;
                return;
            }

            InsertInput(~index, sample);
        }

        public AcceptedAuthorityInput Select(ulong authorityTick, int holdTicks)
        {
            CanonicalInputSample selected = default;
            int expiredCount = 0;
            while (expiredCount < m_InputCount && m_InputTargetTicks[expiredCount] <= authorityTick)
            {
                selected = m_Inputs[expiredCount];
                expiredCount++;
            }

            if (expiredCount > 0)
            {
                selected.Input.RetainText();
                for (int index = 0; index < expiredCount; index++) m_Inputs[index].Input.ReleaseText();
                int remaining = m_InputCount - expiredCount;
                Array.Copy(m_Inputs, expiredCount, m_Inputs, 0, remaining);
                Array.Copy(m_InputTargetTicks, expiredCount, m_InputTargetTicks, 0, remaining);
                Array.Clear(m_Inputs, remaining, expiredCount);
                Array.Clear(m_InputTargetTicks, remaining, expiredCount);
                m_InputCount = remaining;
            }

            if (selected.IsValid)
            {
                if (selected.TargetAuthorityTick == authorityTick)
                    ExactInputCount++;
                else
                    LateInputCount++;
                if (m_Held.IsValid) m_Held.Input.ReleaseText();
                m_Held = selected;
                m_HeldAcceptedTick = authorityTick;
            }
            if (!m_Held.IsValid)
                throw new InvalidOperationException($"Authority has no canonical input for Actor '{Roster.ActorId}' at Tick '{authorityTick}'.");
            if (authorityTick - m_HeldAcceptedTick > (ulong)holdTicks)
            {
                NeutralInputCount++;
                return new AcceptedAuthorityInput(Roster.ActorId, m_Held.InputSequence, Neutral(m_Held.Input, authorityTick));
            }
            if (!selected.IsValid)
                HeldInputCount++;
            return new AcceptedAuthorityInput(Roster.ActorId, m_Held.InputSequence, m_Held.Input);
        }

        public void AcceptHelloSequence(ulong sequence)
        {
            if (sequence > LastReceivedPacketSequence)
                LastReceivedPacketSequence = sequence;
        }

        public bool AcceptPacketSequence(ulong sequence)
        {
            if (sequence <= LastReceivedPacketSequence)
            {
                if (sequence == LastReceivedPacketSequence)
                    DuplicatePackets++;
                else
                    OutOfOrderPackets++;
                return false;
            }
            if (LastReceivedPacketSequence != 0 && sequence > LastReceivedPacketSequence + 1)
                PacketSequenceGaps = checked(PacketSequenceGaps + sequence - LastReceivedPacketSequence - 1);
            LastReceivedPacketSequence = sequence;
            return true;
        }

        public void RecordCommand(int payloadBytes, ulong sourceTick)
        {
            if (payloadBytes < 0 || sourceTick == 0)
                throw new ArgumentOutOfRangeException(payloadBytes < 0 ? nameof(payloadBytes) : nameof(sourceTick));
            CommandPacketCount++;
            CommandPayloadBytes = checked(CommandPayloadBytes + (ulong)payloadBytes);
            LastCommandSourceTick = sourceTick;
        }

        public void RecordCommandLead(ulong targetTick, ulong authorityTick)
        {
            LastCommandLead = targetTick >= authorityTick
                ? checked((long)(targetTick - authorityTick))
                : -checked((long)(authorityTick - targetTick));
        }

        public void AcknowledgeSnapshot(ulong latestSnapshot, ulong latestBase)
        {
            if (latestSnapshot == 0 && latestBase == 0)
                return;
            if (latestSnapshot != latestBase || latestSnapshot < AcknowledgedSnapshotSequence)
                throw new InvalidOperationException("Client snapshot acknowledgement is inconsistent or regressed.");
            if (!TryGetSentCheckpoint(latestSnapshot, out NetworkCheckpoint checkpoint))
                return;
            AcknowledgedSnapshotSequence = latestSnapshot;
            AcknowledgedCheckpoint = checkpoint;
            while (m_SentSequenceOrder[m_SentSequenceHead] < latestSnapshot)
                DequeueSentSequence();
        }

        public void StoreSent(ulong sequence, NetworkCheckpoint checkpoint)
        {
            if (!checkpoint.IsValid)
                throw new ArgumentOutOfRangeException(nameof(checkpoint));
            EnqueueSentSequence(sequence, checkpoint);
            while (m_SentSequenceCount > m_Capacity)
            {
                ulong oldestSequence = m_SentSequenceOrder[m_SentSequenceHead];
                if (oldestSequence >= AcknowledgedSnapshotSequence && AcknowledgedSnapshotSequence != 0)
                    throw new InvalidOperationException("Authority snapshot baseline capacity cannot discard an unconfirmed checkpoint.");
                DequeueSentSequence();
            }
        }

        public void RecordDeltaSnapshot(int payloadBytes)
        {
            DeltaSnapshotCount++;
            m_DeltaPayloadBytes = checked(m_DeltaPayloadBytes + (ulong)payloadBytes);
            LastDeltaPayloadBytes = payloadBytes;
        }

        public void RecordFullCheckpoint(int payloadBytes)
        {
            FullCheckpointCount++;
            LastDeltaPayloadBytes = payloadBytes;
        }

        public void RecordDeltaMtuExceeded(int payloadBytes)
        {
            DeltaMtuExceededCount++;
            LastDeltaPayloadBytes = payloadBytes;
        }

        public void RequestFullCheckpoint(ulong sequence)
        {
            if (PendingCheckpointRequest != 0)
                throw new InvalidOperationException("Authority route already has a pending full checkpoint request.");
            PendingCheckpointRequest = sequence;
        }

        public ulong NextSendPacketSequence() => ++m_SendPacketSequence;
        public ulong NextSnapshotSequence() => ++m_SnapshotSequence;

        public string DescribeMetrics(float elapsedSeconds)
        {
            float seconds = Math.Max(elapsedSeconds, 0.001f);
            float commandPacketsPerSecond = (CommandPacketCount - m_PreviousCommandPacketCount) / seconds;
            float commandBytesPerSecond = (CommandPayloadBytes - m_PreviousCommandPayloadBytes) / seconds;
            float snapshotPacketsPerSecond = (DeltaSnapshotCount - m_PreviousDeltaSnapshotCount) / seconds;
            float snapshotBytesPerSecond = (m_DeltaPayloadBytes - m_PreviousDeltaPayloadBytes) / seconds;
            m_PreviousCommandPacketCount = CommandPacketCount;
            m_PreviousCommandPayloadBytes = CommandPayloadBytes;
            m_PreviousDeltaSnapshotCount = DeltaSnapshotCount;
            m_PreviousDeltaPayloadBytes = m_DeltaPayloadBytes;
            return $"{Roster.ActorId}:commands={CommandPacketCount}/{CommandPayloadBytes}@{commandPacketsPerSecond:0.##}pps/{commandBytesPerSecond:0.##}Bps,packetGaps={PacketSequenceGaps},duplicates={DuplicatePackets},outOfOrder={OutOfOrderPackets},exact={ExactInputCount},held={HeldInputCount},neutral={NeutralInputCount},late={LateInputCount},lead={LastCommandLead},delta={DeltaSnapshotCount}@{snapshotPacketsPerSecond:0.##}pps/{snapshotBytesPerSecond:0.##}Bps,full={FullCheckpointCount},oversize={DeltaMtuExceededCount},lastBytes={LastDeltaPayloadBytes}";
        }

        SimulationInput Neutral(SimulationInput source, ulong authorityTick)
        {
            return SimulationInput.FromOwnedArrays(
                source.NumericProfile,
                new SimulationTickSourceIdentity(SimulationTickSourceKind.Authoritative, source.TickSource.ClockId, authorityTick),
                source.InputSourceIdentity,
                source.Sequence,
                BuildNeutralValues(source.Values),
                Array.Empty<SimulationInputRequest>());
        }

        SimulationInputValue[] BuildNeutralValues(IReadOnlyList<SimulationInputValue> values)
        {
            if (!HasNeutralLayout(m_NeutralValues, values))
                m_NeutralValues = CreateNeutralValues(values);
            return m_NeutralValues;
        }

        static bool HasNeutralLayout(IReadOnlyList<SimulationInputValue> cache, IReadOnlyList<SimulationInputValue> values)
        {
            if (cache == null || cache.Count != values.Count)
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (!CanonicalTextValue.Equals(cache[i].InputId, values[i].InputId, StringComparison.Ordinal) ||
                    cache[i].Kind != values[i].Kind)
                {
                    return false;
                }
            }
            return true;
        }

        static SimulationInputValue[] CreateNeutralValues(IReadOnlyList<SimulationInputValue> values)
        {
            if (values.Count == 0)
                return Array.Empty<SimulationInputValue>();
            var result = new SimulationInputValue[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                SimulationInputValue value = values[i];
                result[i] = value.Kind switch
                {
                    ProgramInputValueKind.Boolean => SimulationInputValue.FromBoolean(value.InputId, false),
                    ProgramInputValueKind.Scalar => SimulationInputValue.FromScalar(value.InputId, Float32Scalar.Zero),
                    ProgramInputValueKind.Vector2 => SimulationInputValue.FromVector2(value.InputId, Float32Vector2.Zero),
                    ProgramInputValueKind.Vector3 => SimulationInputValue.FromVector3(value.InputId, Float32Vector3.Zero),
                    ProgramInputValueKind.Yaw => SimulationInputValue.FromYaw(value.InputId, Float32Yaw.Zero),
                    ProgramInputValueKind.ActionTargetSnapshot => SimulationInputValue.FromActionTargetSnapshot(value.InputId, SimulationActionTargetSnapshot.None),
                    _ => throw new InvalidDataException($"Unsupported input value kind '{value.Kind}'.")
                };
            }
            return result;
        }

        void EnqueueSentSequence(ulong sequence, NetworkCheckpoint checkpoint)
        {
            int index = (m_SentSequenceHead + m_SentSequenceCount) % m_SentSequenceOrder.Length;
            m_SentSequenceOrder[index] = sequence;
            m_SentCheckpoints[index] = checkpoint;
            m_SentSequenceCount++;
        }

        ulong DequeueSentSequence()
        {
            ulong sequence = m_SentSequenceOrder[m_SentSequenceHead];
            m_SentSequenceOrder[m_SentSequenceHead] = 0;
            m_SentCheckpoints[m_SentSequenceHead] = default;
            m_SentSequenceHead = (m_SentSequenceHead + 1) % m_SentSequenceOrder.Length;
            m_SentSequenceCount--;
            return sequence;
        }

        bool TryGetSentCheckpoint(ulong sequence, out NetworkCheckpoint checkpoint)
        {
            for (int i = 0; i < m_SentSequenceCount; i++)
            {
                int index = (m_SentSequenceHead + i) % m_SentSequenceOrder.Length;
                if (m_SentSequenceOrder[index] != sequence)
                    continue;
                checkpoint = m_SentCheckpoints[index];
                return true;
            }

            checkpoint = default;
            return false;
        }

        int FindInputIndex(ulong targetTick)
        {
            int low = 0;
            int high = m_InputCount - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (m_InputTargetTicks[middle] == targetTick)
                    return middle;
                if (m_InputTargetTicks[middle] < targetTick)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return ~low;
        }

        public void Dispose()
        {
            for (int index = 0; index < m_InputCount; index++) m_Inputs[index].Input.ReleaseText();
            if (m_Held.IsValid) m_Held.Input.ReleaseText();
            Array.Clear(m_Inputs, 0, m_InputCount);
            m_InputCount = 0;
            m_Held = default;
        }

        void InsertInput(int index, CanonicalInputSample sample)
        {
            Array.Copy(m_Inputs, index, m_Inputs, index + 1, m_InputCount - index);
            Array.Copy(m_InputTargetTicks, index, m_InputTargetTicks, index + 1, m_InputCount - index);
            sample.Input.RetainText();
            m_Inputs[index] = sample;
            m_InputTargetTicks[index] = sample.TargetAuthorityTick;
            m_InputCount++;
        }
    }
}
