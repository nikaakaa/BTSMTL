using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal sealed class ServerAuthoritativePredictionDispositionJournal
    {
        readonly int m_Capacity;
        readonly ServerAuthoritativeJournalEntry[] m_Entries;
        readonly ServerAuthoritativePredictionJournalCheckpoint[] m_Checkpoints;
        int m_Count;

        public ServerAuthoritativePredictionDispositionJournal(int historyCapacity)
        {
            if (historyCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(historyCapacity));
            m_Capacity = checked(historyCapacity * 64);
            m_Entries = new ServerAuthoritativeJournalEntry[m_Capacity];
            m_Checkpoints = new ServerAuthoritativePredictionJournalCheckpoint[2];
            for (int index = 0; index < m_Checkpoints.Length; index++)
                m_Checkpoints[index] = new ServerAuthoritativePredictionJournalCheckpoint(m_Capacity);
        }

        public ulong Cursor { get; private set; }
        public int Count => m_Count;
        public int LastRejectedCount { get; private set; }

        public bool WasCommitted(EventId eventId)
        {
            int index = Find(m_Entries, m_Count, eventId);
            return index >= 0 &&
                m_Entries[index].Disposition != ServerAuthoritativeEventDisposition.PredictedRejected;
        }

        public void Record(in ServerAuthoritativeJournalEntry entry, ulong firstRetainedHistoryTick)
        {
            ulong cursor = Cursor;
            Record(m_Entries, ref m_Count, ref cursor, entry, firstRetainedHistoryTick, m_Capacity);
            Cursor = cursor;
        }

        public void Prune(ulong firstRetainedHistoryTick) =>
            Prune(m_Entries, ref m_Count, firstRetainedHistoryTick);

        public void ApplyConfirmation(
            SimulationTick authorityTick,
            ServerAuthoritativeEventHorizon horizon,
            ulong firstRetainedHistoryTick)
        {
            ulong cursor = Cursor;
            int rejectedCount = ApplyConfirmation(
                m_Entries, ref m_Count, ref cursor, authorityTick, horizon, firstRetainedHistoryTick, m_Capacity);
            Cursor = cursor;
            LastRejectedCount = rejectedCount;
        }

        public ServerAuthoritativePredictionJournalCheckpoint PrepareConfirmation(
            SimulationTick authorityTick,
            ServerAuthoritativeEventHorizon horizon,
            ulong firstRetainedHistoryTick)
        {
            ServerAuthoritativePredictionJournalCheckpoint checkpoint = Capture();
            try
            {
                ulong cursor = checkpoint.Cursor;
                checkpoint.LastRejectedCount = ApplyConfirmation(
                    checkpoint.Buffer, ref checkpoint.Count, ref cursor,
                    authorityTick, horizon, firstRetainedHistoryTick, m_Capacity);
                checkpoint.Cursor = cursor;
                return checkpoint;
            }
            catch
            {
                checkpoint.Dispose();
                throw;
            }
        }

        public void Prune(
            ServerAuthoritativePredictionJournalCheckpoint checkpoint,
            ulong firstRetainedHistoryTick) =>
            Prune(checkpoint.Buffer, ref checkpoint.Count, firstRetainedHistoryTick);

        public void WriteState(CanonicalWriter writer)
        {
            ServerAuthoritativePredictionStateCodec.WriteJournalHeader(writer, Cursor, m_Count);
            for (int index = 0; index < m_Count; index++)
                ServerAuthoritativePredictionStateCodec.WriteJournalEntry(writer, m_Entries[index]);
        }

        public ServerAuthoritativePredictionJournalCheckpoint Capture() =>
            CreateCheckpoint(m_Entries.AsSpan(0, m_Count), Cursor, LastRejectedCount);

        public void Restore(ServerAuthoritativePredictionJournalCheckpoint checkpoint)
        {
            Array.Clear(m_Entries, 0, m_Count);
            checkpoint.Entries.CopyTo(m_Entries);
            m_Count = checkpoint.Count;
            Cursor = checkpoint.Cursor;
            LastRejectedCount = checkpoint.LastRejectedCount;
        }

        ServerAuthoritativePredictionJournalCheckpoint CreateCheckpoint(
            ReadOnlySpan<ServerAuthoritativeJournalEntry> entries,
            ulong cursor,
            int lastRejectedCount)
        {
            for (int index = 0; index < m_Checkpoints.Length; index++)
            {
                ServerAuthoritativePredictionJournalCheckpoint checkpoint = m_Checkpoints[index];
                if (checkpoint.IsHeld)
                    continue;
                checkpoint.Begin(entries, cursor, lastRejectedCount);
                return checkpoint;
            }
            throw new InvalidOperationException("Prediction journal checkpoints exceeded their prepared transaction capacity.");
        }

        static int ApplyConfirmation(
            ServerAuthoritativeJournalEntry[] entries,
            ref int count,
            ref ulong cursor,
            SimulationTick authorityTick,
            ServerAuthoritativeEventHorizon horizon,
            ulong firstRetainedHistoryTick,
            int capacity)
        {
            int rejectedCount = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly ServerAuthoritativeJournalEntry entry = ref entries[i];
                if (entry.Tick.Value > authorityTick.Value ||
                    entry.Disposition == ServerAuthoritativeEventDisposition.AuthorityConfirmed ||
                    entry.Disposition == ServerAuthoritativeEventDisposition.PredictedRejected)
                {
                    continue;
                }
                bool confirmed = !horizon.IsEmpty &&
                    (entry.Sequence < horizon.Sequence ||
                     entry.Sequence == horizon.Sequence && entry.EventId.Equals(horizon.EventId));
                if (!confirmed)
                    rejectedCount++;
                Record(
                    entries,
                    ref count,
                    ref cursor,
                    new ServerAuthoritativeJournalEntry(
                        entry.EventId,
                        entry.Tick,
                        entry.Sequence,
                        confirmed
                            ? ServerAuthoritativeEventDisposition.AuthorityConfirmed
                            : ServerAuthoritativeEventDisposition.PredictedRejected),
                    firstRetainedHistoryTick,
                    capacity);
            }
            if (!horizon.IsEmpty && Find(entries, count, horizon.EventId) < 0)
            {
                Record(
                    entries,
                    ref count,
                    ref cursor,
                    new ServerAuthoritativeJournalEntry(
                        horizon.EventId,
                        authorityTick,
                        horizon.Sequence,
                        ServerAuthoritativeEventDisposition.AuthorityConfirmed),
                    firstRetainedHistoryTick,
                    capacity);
            }
            return rejectedCount;
        }

        static void Record(
            ServerAuthoritativeJournalEntry[] entries,
            ref int count,
            ref ulong cursor,
            in ServerAuthoritativeJournalEntry entry,
            ulong firstRetainedHistoryTick,
            int capacity)
        {
            int entryIndex = Find(entries, count, entry.EventId);
            if (entryIndex < 0)
            {
                Prune(entries, ref count, firstRetainedHistoryTick);
                if (count >= capacity)
                    throw new InvalidOperationException("Prediction disposition journal capacity is exhausted by live predicted events.");
                entryIndex = ~Find(entries, count, entry.EventId);
                Array.Copy(entries, entryIndex, entries, entryIndex + 1, count - entryIndex);
                count++;
            }
            else if (entries[entryIndex].Disposition == ServerAuthoritativeEventDisposition.AuthorityConfirmed ||
                entries[entryIndex].Tick == entry.Tick &&
                entries[entryIndex].Sequence == entry.Sequence &&
                entries[entryIndex].Disposition == entry.Disposition)
            {
                return;
            }

            entries[entryIndex] = entry;
            cursor = checked(cursor + 1);
        }

        static void Prune(
            ServerAuthoritativeJournalEntry[] entries,
            ref int count,
            ulong firstRetainedHistoryTick)
        {
            int outputIndex = 0;
            for (int i = 0; i < count; i++)
            {
                if (entries[i].Tick.Value >= firstRetainedHistoryTick ||
                    entries[i].Disposition == ServerAuthoritativeEventDisposition.PredictedCommitted ||
                    entries[i].Disposition == ServerAuthoritativeEventDisposition.SuppressedDuplicate)
                {
                    if (outputIndex != i)
                        entries[outputIndex] = entries[i];
                    outputIndex++;
                }
            }
            count = outputIndex;
        }

        static int Find(ServerAuthoritativeJournalEntry[] entries, int count, EventId eventId)
        {
            int left = 0;
            int right = count - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                int comparison = entries[middle].EventId.CompareTo(eventId);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }
    }

    internal sealed class ServerAuthoritativePredictionJournalCheckpoint : IDisposable
    {
        internal readonly ServerAuthoritativeJournalEntry[] Buffer;
        internal int Count;
        internal ulong Cursor;
        internal int LastRejectedCount;

        internal ServerAuthoritativePredictionJournalCheckpoint(int capacity) =>
            Buffer = new ServerAuthoritativeJournalEntry[capacity];

        public ServerAuthoritativePredictionJournalCheckpoint(
            ServerAuthoritativeJournalEntry[] entries,
            ulong cursor,
            int lastRejectedCount)
        {
            Buffer = entries;
            Count = entries.Length;
            Cursor = cursor;
            LastRejectedCount = lastRejectedCount;
            IsHeld = true;
        }

        internal bool IsHeld { get; private set; }
        public ReadOnlySpan<ServerAuthoritativeJournalEntry> Entries => Buffer.AsSpan(0, Count);

        internal void Begin(ReadOnlySpan<ServerAuthoritativeJournalEntry> entries, ulong cursor, int lastRejectedCount)
        {
            IsHeld = true;
            Count = entries.Length;
            Cursor = cursor;
            LastRejectedCount = lastRejectedCount;
            entries.CopyTo(Buffer);
        }

        public void Dispose()
        {
            Array.Clear(Buffer, 0, Count);
            Count = 0;
            IsHeld = false;
        }
    }
}
