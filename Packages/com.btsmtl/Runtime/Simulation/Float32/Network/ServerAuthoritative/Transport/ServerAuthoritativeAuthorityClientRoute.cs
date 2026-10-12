using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative.Transport
{
    sealed class ServerAuthoritativeAuthorityClientRoute
    {
        readonly int m_Capacity;
        readonly CommandInputSlot[] m_Inputs;
        int m_InputHead;
        int m_InputCount;
        readonly ulong[] m_SentSequenceOrder;
        readonly NetworkCheckpoint[] m_SentCheckpoints;
        int m_SentSequenceHead;
        int m_SentSequenceCount;
        CommandInputSlot m_Held;
        ulong m_HeldAcceptedTick;
        ulong m_LastEnqueuedInputSequence;
        ulong m_SendPacketSequence;
        ulong m_SnapshotSequence;
        ulong m_PreviousCommandPacketCount;
        ulong m_PreviousCommandPayloadBytes;
        ulong m_PreviousDeltaSnapshotCount;
        ulong m_PreviousDeltaPayloadBytes;
        ulong m_DeltaPayloadBytes;

        public ServerAuthoritativeAuthorityClientRoute(ServerAuthoritativeRosterEntry roster, int capacity,
            int maximumDatagramBytes)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Roster = roster;
            m_Capacity = capacity;
            m_Inputs = new CommandInputSlot[capacity];
            int values = maximumDatagramBytes / Float32SimulationInputCodec.MinimumValueBytes;
            int requests = maximumDatagramBytes / Float32SimulationInputCodec.MinimumRequestBytes;
            for (int i = 0; i < m_Inputs.Length; i++)
                m_Inputs[i] = new CommandInputSlot(new Float32SimulationInputFrameStorage(values, requests));
            m_Held = new CommandInputSlot(new Float32SimulationInputFrameStorage(values, requests));
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
        public bool HasInput => m_Held.Sample.IsValid || m_InputCount > 0;
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

        public void Enqueue(CanonicalInputSample sample, ref Float32SimulationInputFrameStorage inputStorage)
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
                index = InputStorageIndex(index);
                if (sample.InputSequence <= m_Inputs[index].Sample.InputSequence)
                    return;
                Float32SimulationInputFrameStorage previous = m_Inputs[index].Storage;
                m_Inputs[index] = new CommandInputSlot(inputStorage, sample);
                inputStorage = previous;
                return;
            }

            InsertInput(~index, sample, ref inputStorage);
        }

        public AcceptedAuthorityInput Select(ulong authorityTick, int holdTicks)
        {
            CommandInputSlot selected = default;
            int selectedIndex = -1;
            int expiredCount = 0;
            int nextHead = m_InputHead;
            while (expiredCount < m_InputCount && m_Inputs[nextHead].Sample.TargetAuthorityTick <= authorityTick)
            {
                selected = m_Inputs[nextHead];
                selectedIndex = nextHead;
                expiredCount++;
                nextHead++;
                if (nextHead == m_Capacity)
                    nextHead = 0;
            }

            if (expiredCount > 0)
            {
                CommandInputSlot previous = m_Held;
                m_Held = selected;
                m_Inputs[selectedIndex] = previous;
                int storageIndex = m_InputHead;
                for (int index = 0; index < expiredCount; index++)
                {
                    m_Inputs[storageIndex].Storage.Release();
                    m_Inputs[storageIndex].Sample = default;
                    storageIndex++;
                    if (storageIndex == m_Capacity)
                        storageIndex = 0;
                }
                m_InputHead = nextHead;
                m_InputCount -= expiredCount;
            }

            if (selected.Sample.IsValid)
            {
                if (selected.Sample.TargetAuthorityTick == authorityTick)
                    ExactInputCount++;
                else
                    LateInputCount++;
                m_HeldAcceptedTick = authorityTick;
            }
            if (!m_Held.Sample.IsValid)
                throw new InvalidOperationException($"Authority has no canonical input for Actor '{Roster.ActorId}' at Tick '{authorityTick}'.");
            if (authorityTick - m_HeldAcceptedTick > (ulong)holdTicks)
                NeutralInputCount++;
            else if (!selected.Sample.IsValid)
                HeldInputCount++;
            return new AcceptedAuthorityInput(Roster.ActorId, m_Held.Sample.InputSequence, m_Held.Sample.Input);
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
                ulong middleTick = m_Inputs[InputStorageIndex(middle)].Sample.TargetAuthorityTick;
                if (middleTick == targetTick)
                    return middle;
                if (middleTick < targetTick)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return ~low;
        }

        public void Dispose()
        {
            int storageIndex = m_InputHead;
            for (int index = 0; index < m_InputCount; index++)
            {
                m_Inputs[storageIndex].Storage.Release();
                m_Inputs[storageIndex].Sample = default;
                storageIndex++;
                if (storageIndex == m_Capacity)
                    storageIndex = 0;
            }
            m_Held.Storage.Release();
            m_InputHead = 0;
            m_InputCount = 0;
            m_Held.Sample = default;
        }

        void InsertInput(int index, CanonicalInputSample sample, ref Float32SimulationInputFrameStorage inputStorage)
        {
            int storageIndex = InputStorageIndex(m_InputCount);
            Float32SimulationInputFrameStorage available = m_Inputs[storageIndex].Storage;
            for (int i = m_InputCount; i > index; i--)
            {
                int previousIndex = storageIndex == 0 ? m_Capacity - 1 : storageIndex - 1;
                m_Inputs[storageIndex] = m_Inputs[previousIndex];
                storageIndex = previousIndex;
            }
            m_Inputs[storageIndex] = new CommandInputSlot(inputStorage, sample);
            inputStorage = available;
            m_InputCount++;
        }

        int InputStorageIndex(int index) => (m_InputHead + index) % m_Capacity;

        struct CommandInputSlot
        {
            public CommandInputSlot(Float32SimulationInputFrameStorage storage, CanonicalInputSample sample = default)
            {
                Storage = storage;
                Sample = sample;
            }

            public readonly Float32SimulationInputFrameStorage Storage;
            public CanonicalInputSample Sample;
        }
    }
}
